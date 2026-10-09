using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.FactLogic;
using UnityEngine;
using UnityModManagerNet;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// UMM 入口点（Info.json 的 EntryMethod = PlagueOfMadnessHint.Main.Load）。
    /// </summary>
    internal static class Main
    {
        internal static UnityModManager.ModEntry.ModLogger Logger;
        internal static Settings Settings;
        internal static ReadyNotifier Notifier;
        internal static UnityModManager.ModEntry Entry;

        private static Harmony s_Harmony;
        private static bool s_Subscribed;
        private static bool s_LastStrict;
        private static string s_LastSound;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Entry = modEntry;
            Logger = modEntry.Logger;

            Settings = UnityModManager.ModSettings.Load<PlagueOfMadnessHint.Settings>(modEntry);
            if (Settings == null)
            {
                Settings = new PlagueOfMadnessHint.Settings();
                LogWarning("未能加载设置文件，已使用默认值。");
            }
            s_LastStrict = Settings.StrictConsecutiveRule;
            s_LastSound = Settings.ReadySoundEvent;

            ModLocalization.Initialize(modEntry);

            s_Harmony = new Harmony(modEntry.Info.Id);
            try
            {
                s_Harmony.PatchAll(Assembly.GetExecutingAssembly());
                LogPatchStatus();
            }
            catch (Exception ex)
            {
                LogError("Harmony 补丁应用失败：" + ex);
            }

            Notifier = new ReadyNotifier();

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;
            modEntry.OnUnload = OnUnload;

            // 直接在这里初始化，不依赖 UMM 是否会在启动时回调 OnToggle。
            Subscribe();
            // 蓝图库此时可能还没加载完，Apply() 会返回 false，之后由 OnUpdate 重试。
            BlueprintPatcher.Apply();

            Log("v" + modEntry.Info.Version + " 已载入。");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (value)
            {
                Subscribe();
                BlueprintPatcher.Apply();
            }
            else
            {
                Unsubscribe();
                if (Notifier != null)
                {
                    Notifier.CleanupAll();
                }
            }
            return true;
        }

        private static void Subscribe()
        {
            if (s_Subscribed || Notifier == null)
            {
                return;
            }
            EventBus.Subscribe(Notifier);
            s_Subscribed = true;
            LogDebug("ReadyNotifier 已订阅 EventBus。");
        }

        private static void Unsubscribe()
        {
            if (!s_Subscribed || Notifier == null)
            {
                return;
            }
            EventBus.Unsubscribe(Notifier);
            s_Subscribed = false;
            LogDebug("ReadyNotifier 已取消订阅。");
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            // 跟随游戏语言：Locale 变化后重新加载本 Mod 的文本
            ModLocalization.Tick(modEntry);

            // 让状态图标里的自定义文本跟上语言包（含存档读档后语言包重建的情况）
            LocalizationBridge.Tick();

            // 蓝图库就绪后补一次 Apply
            if (!BlueprintPatcher.Applied
                && Game.Instance != null
                && Game.Instance.BlueprintRoot != null)
            {
                BlueprintPatcher.Apply();
            }

            // 补丁开关变化时给一条明确日志，方便确认"开关是否真的生效"
            if (Settings != null && Settings.StrictConsecutiveRule != s_LastStrict)
            {
                s_LastStrict = Settings.StrictConsecutiveRule;
                Log("补丁「描述正确生效」：" + (s_LastStrict
                    ? "已启用 —— 现在必须连续三次施放同一个法术，中途施放别的法术会立刻清零计数。"
                    : "已关闭 —— 保持原版行为（各法术分别累计，只有休息才清零）。"));
            }

            // 清理已离场单位的残留标记
            if (Notifier != null)
            {
                Notifier.Tick();
            }
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            SettingsGUI.Draw(modEntry);
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
            BlueprintPatcher.SetIconVisible(Settings.EnableReadyIcon);
            BlueprintPatcher.ApplyWeaponIcon();
            BlueprintPatcher.SetFxPrefab(Settings.ReadyFxPrefabId);
            BlueprintPatcher.RegisterTexts();
            LogDebug("设置已保存。");
        }

        /// <summary>
        /// 设置项被改动后的即时应用（由 Settings.OnChange 回调）。
        /// 改动无需重启，立即生效。
        /// </summary>
        internal static void OnSettingsChanged()
        {
            try
            {
                if (Settings == null)
                {
                    return;
                }

                // 音效换了就当场试听一下，方便挑
                string sound = Settings.ReadySoundEvent;
                if (!string.Equals(sound, s_LastSound, StringComparison.Ordinal))
                {
                    bool firstTime = s_LastSound == null;
                    s_LastSound = sound;
                    if (!firstTime && Settings.EnableReadySound)
                    {
                        PreviewReadySound(sound);
                    }
                }

                // 文本语言（会重新加载本地化表，并刷新蓝图上的名称/描述）
                ModLocalization.ApplySettings(Entry);

                // 蓝图上的开关跟着设置走
                BlueprintPatcher.SetIconVisible(Settings.EnableReadyIcon);
                BlueprintPatcher.ApplyWeaponIcon();
                BlueprintPatcher.SetFxPrefab(Settings.ReadyFxPrefabId);
                BlueprintPatcher.RegisterTexts();
            }
            catch (Exception ex)
            {
                LogWarning("应用设置时出错：" + ex.Message);
            }
        }

        /// <summary>设置界面里点选音效时的即时试听（也给设置界面上的"试听"按钮用）。</summary>
        internal static void PreviewReadySound(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }
            try
            {
                GameObject anchor = Kingmaker.Visual.Sound.SoundState.Get2DSoundObject();
                if (anchor == null)
                {
                    return;
                }
                Kingmaker.Sound.SoundEventsManager.PostEvent(eventName, anchor, false);
            }
            catch (Exception ex)
            {
                LogDebug("试听失败：" + ex.Message);
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Unsubscribe();
            if (Notifier != null)
            {
                Notifier.CleanupAll();
            }
            BlueprintPatcher.Restore();
            if (s_Harmony != null)
            {
                s_Harmony.UnpatchAll(s_Harmony.Id);
                s_Harmony = null;
            }
            Log("已卸载。");
            return true;
        }

        /// <summary>启动时打印补丁挂载情况，"补丁到底进没进去"一眼可见。</summary>
        private static void LogPatchStatus()
        {
            try
            {
                int count = 0;
                bool strictOk = false;
                foreach (MethodBase mb in s_Harmony.GetPatchedMethods())
                {
                    count++;
                    if (mb != null
                        && mb.DeclaringType == typeof(AbilityUsagesCountTrigger)
                        && mb.Name == "OnEventDidTrigger")
                    {
                        strictOk = true;
                    }
                }
                Log("Harmony 已挂载 " + count + " 个方法。补丁「癫狂灾瘟描述正确生效」（AbilityUsagesCountTrigger.OnEventDidTrigger）："
                    + (strictOk ? "已生效" : "未命中（请把日志发给作者）"));
                Log("该补丁的反射通道自检：" + StrictConsecutivePatch.SelfTest());
            }
            catch (Exception ex)
            {
                LogWarning("检查补丁状态失败：" + ex.Message);
            }
        }

        // ---------------- 日志 ----------------

        internal static void Log(string msg)
        {
            if (Logger != null)
            {
                Logger.Log(msg);
            }
        }

        internal static void LogWarning(string msg)
        {
            if (Logger != null)
            {
                Logger.Warning(msg);
            }
        }

        internal static void LogError(string msg)
        {
            if (Logger != null)
            {
                Logger.Error(msg);
            }
        }

        internal static void LogDebug(string msg)
        {
            if (Logger != null && Settings != null && Settings.EnableVerboseLogging)
            {
                Logger.Log("[DEBUG] " + msg);
            }
        }
    }
}
