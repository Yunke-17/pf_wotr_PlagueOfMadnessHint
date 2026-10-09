using System;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// 运行时改写"癫狂灾瘟"那句"已就绪"Buff 的蓝图：
    ///   1. 去掉 HiddenInUi 标记 —— 这是原版看不到任何提示的根因，去掉后状态栏会出现图标；
    ///   2. 补上名称/描述 —— 原蓝图这两个字段是空的，图标会是个没有任何文字的方块；
    ///   3. 写入 FxOnStart —— 引擎自带的 Buff 光环机制：
    ///      Buff.TrySpawnParticleEffect() 会在 Buff 生效期间调用 FxHelper.SpawnFxOnUnit 挂上这个特效，
    ///      Buff 消失时 ClearParticleEffect() 自动销毁。生命周期完全由引擎托管，比手动管句柄可靠。
    /// 只动这一个 Buff（26f9b2e6…），不会影响共用 AbilityUsagesCountTrigger 的奥法维持之靴。
    /// </summary>
    internal static class BlueprintPatcher
    {
        public const string BuffNameKey = "pom.buff.name";
        public const string BuffDescKey = "pom.buff.desc";

        // BlueprintBuff.Flags.HiddenInUi = 2（Flags 是私有嵌套枚举，按数值处理更稳）
        private const int HiddenInUiBit = 2;

        private static FieldInfo s_FlagsField;
        private static FieldInfo s_DisplayNameField;
        private static FieldInfo s_DescriptionField;
        private static FieldInfo s_IconField;

        private static int s_OriginalFlags = -1;
        private static PrefabLink s_OriginalFxOnStart;
        private static object s_OriginalIcon;
        private static bool s_IconCaptured;

        public static bool Applied { get; private set; }

        public static bool Apply()
        {
            if (Applied)
            {
                return true;
            }

            BlueprintBuff buff = GameRefs.Buff;
            if (buff == null)
            {
                // 蓝图库还没加载好，稍后由 Main.OnUpdate 重试
                return false;
            }

            try
            {
                if (s_OriginalFlags < 0)
                {
                    s_OriginalFlags = GetFlags(buff);
                    s_OriginalFxOnStart = buff.FxOnStart;
                    Main.LogDebug("捕捉到原始 m_Flags = " + s_OriginalFlags);
                }

                RegisterTexts();

                FieldInfo nameField = GetDisplayNameField();
                FieldInfo descField = GetDescriptionField();
                if (nameField != null)
                {
                    nameField.SetValue(buff, new LocalizedString { Key = BuffNameKey });
                }
                if (descField != null)
                {
                    descField.SetValue(buff, new LocalizedString { Key = BuffDescKey });
                }

                SetIconVisible(Main.Settings == null || Main.Settings.EnableReadyIcon);
                ApplyWeaponIcon();
                SetFxPrefab(Main.Settings != null ? Main.Settings.ReadyFxPrefabId : null);

                Applied = true;
                Main.Log("已改写「癫狂灾瘟 · 就绪」状态：状态图标 " +
                         ((Main.Settings == null || Main.Settings.EnableReadyIcon) ? "已显示" : "保持隐藏") +
                         "，持久特效 " +
                         ((Main.Settings == null || Main.Settings.EnableReadyFx) ? "已启用" : "已关闭") + "。");
            }
            catch (Exception ex)
            {
                Main.LogError("BlueprintPatcher.Apply 失败：" + ex);
            }

            return Applied;
        }

        /// <summary>把当前语言的名称/描述写进游戏语言包（换语言时会被再次调用）。</summary>
        public static void RegisterTexts()
        {
            LocalizationBridge.Register(BuffNameKey, ModLocalization.Get("buff.name"));
            LocalizationBridge.Register(BuffDescKey, ModLocalization.Get("buff.desc"));
        }

        /// <summary>显示或隐藏状态栏图标。</summary>
        public static void SetIconVisible(bool visible)
        {
            BlueprintBuff buff = GameRefs.Buff;
            if (buff == null)
            {
                return;
            }
            try
            {
                int flags = GetFlags(buff);
                if (s_OriginalFlags < 0)
                {
                    s_OriginalFlags = flags;
                }

                int target = visible
                    ? (flags & ~HiddenInUiBit)
                    : ((flags & ~HiddenInUiBit) | (s_OriginalFlags & HiddenInUiBit));

                if (target != flags)
                {
                    SetFlags(buff, target);
                    Main.LogDebug("m_Flags " + flags + " -> " + target);
                }
            }
            catch (Exception ex)
            {
                Main.LogError("SetIconVisible 失败：" + ex);
            }
        }

        /// <summary>
        /// 把"已就绪"状态图标的图案换成癫狂灾瘟这把武器自己的图标
        /// （蓝图自带的是另一张通用增益图，跟武器对不上）。
        /// </summary>
        public static void ApplyWeaponIcon()
        {
            BlueprintBuff buff = GameRefs.Buff;
            if (buff == null)
            {
                return;
            }

            try
            {
                FieldInfo iconField = GetIconField();
                if (iconField == null)
                {
                    Main.LogWarning("找不到状态图标的 m_Icon 字段，无法替换成武器图标。");
                    return;
                }

                if (!s_IconCaptured)
                {
                    s_OriginalIcon = iconField.GetValue(buff);
                    s_IconCaptured = true;
                }

                object weaponIcon = GetWeaponIcon();
                if (weaponIcon == null)
                {
                    Main.LogWarning("没取到癫狂灾瘟的武器图标，状态图标保持原样。");
                    return;
                }

                if (!ReferenceEquals(iconField.GetValue(buff), weaponIcon))
                {
                    iconField.SetValue(buff, weaponIcon);
                    Main.Log("状态图标已改用癫狂灾瘟的武器图标。");
                }
            }
            catch (Exception ex)
            {
                Main.LogError("ApplyWeaponIcon 失败：" + ex.Message);
            }
        }

        /// <summary>读"癫狂灾瘟"武器蓝图上的图标（Sprite）。</summary>
        private static object GetWeaponIcon()
        {
            try
            {
                BlueprintItem item = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(GameRefs.ItemGuid);
                if (item == null)
                {
                    return null;
                }
                FieldInfo f = FindField(typeof(BlueprintItem), "m_Icon");
                return f != null ? f.GetValue(item) : null;
            }
            catch (Exception ex)
            {
                Main.LogDebug("读取武器图标失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 设置持久微光特效：写进 Buff 蓝图的 FxOnStart。
        /// 传空串或不启用特效时清掉（恢复成原版的"没有特效"）。
        /// </summary>
        public static void SetFxPrefab(string assetId)
        {
            BlueprintBuff buff = GameRefs.Buff;
            if (buff == null)
            {
                return;
            }

            try
            {
                bool enabled = Main.Settings == null || Main.Settings.EnableReadyFx;
                string id = enabled ? (assetId ?? string.Empty).Trim() : string.Empty;

                if (string.IsNullOrEmpty(id))
                {
                    string current = buff.FxOnStart != null ? buff.FxOnStart.AssetId : null;
                    if (!string.IsNullOrEmpty(current))
                    {
                        // 我们写入过特效，现在要撤掉：恢复原始（通常是 null）
                        buff.FxOnStart = s_OriginalFxOnStart;
                        Main.LogDebug("已清除持久特效（FxOnStart）。");
                    }
                    return;
                }

                string existing = buff.FxOnStart != null ? buff.FxOnStart.AssetId : null;
                if (string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                buff.FxOnStart = new PrefabLink { AssetId = id };
                Main.LogDebug("FxOnStart -> " + id);
            }
            catch (Exception ex)
            {
                Main.LogError("SetFxPrefab 失败：" + ex);
            }
        }

        /// <summary>卸载时还原原始状态（图标重新隐藏、图案还原、特效清掉）。</summary>
        public static void Restore()
        {
            BlueprintBuff buff = GameRefs.Buff;
            if (buff == null || s_OriginalFlags < 0)
            {
                return;
            }
            try
            {
                SetFlags(buff, s_OriginalFlags);
                buff.FxOnStart = s_OriginalFxOnStart;

                if (s_IconCaptured)
                {
                    FieldInfo iconField = GetIconField();
                    if (iconField != null)
                    {
                        iconField.SetValue(buff, s_OriginalIcon);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogError("Restore 失败：" + ex);
            }
        }

        // ---------------- 反射辅助 ----------------

        private static FieldInfo GetFlagsField()
        {
            if (s_FlagsField == null)
            {
                s_FlagsField = FindField(typeof(BlueprintBuff), "m_Flags");
            }
            return s_FlagsField;
        }

        private static FieldInfo GetDisplayNameField()
        {
            if (s_DisplayNameField == null)
            {
                s_DisplayNameField = FindField(typeof(BlueprintBuff), "m_DisplayName");
            }
            return s_DisplayNameField;
        }

        private static FieldInfo GetDescriptionField()
        {
            if (s_DescriptionField == null)
            {
                s_DescriptionField = FindField(typeof(BlueprintBuff), "m_Description");
            }
            return s_DescriptionField;
        }

        private static FieldInfo GetIconField()
        {
            if (s_IconField == null)
            {
                s_IconField = FindField(typeof(BlueprintBuff), "m_Icon");
            }
            return s_IconField;
        }

        /// <summary>
        /// 沿继承链找字段（DeclaredOnly，逐级向上）。
        /// m_DisplayName / m_Description / m_Icon 其实都声明在基类 BlueprintUnitFact 上，
        /// 显式走一遍链子比指望 GetField 帮忙翻基类更稳。
        /// </summary>
        private static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    return f;
                }
            }
            return null;
        }

        private static int GetFlags(BlueprintBuff buff)
        {
            FieldInfo f = GetFlagsField();
            if (f == null)
            {
                return 0;
            }
            object value = f.GetValue(buff);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static void SetFlags(BlueprintBuff buff, int value)
        {
            FieldInfo f = GetFlagsField();
            if (f == null)
            {
                return;
            }
            f.SetValue(buff, Enum.ToObject(f.FieldType, value));
        }
    }
}
