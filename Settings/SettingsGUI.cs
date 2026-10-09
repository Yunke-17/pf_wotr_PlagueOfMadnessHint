using System;
using UnityEngine;
using UnityModManagerNet;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// 设置界面。用 UMM 自己的控件（UI.PopupToggleGroup / UI.ToggleGroup / UI.button / UI.Scale）手工排版，
    /// 风格与 UMM 一致，但每一项都能跟着语言走，并且支持"开关关掉就把子选项藏起来"。
    ///
    /// 之所以不再用 UMM 的自动绘制 UI.DrawFields：那些 [Draw("标签")] 的标签是编译期写死的，
    /// 没法本地化，也没法做条件显示。
    ///
    /// 布局（就绪音效/光环选择器只在对应开关打开时出现）：
    ///   界面语言   [跟随游戏][中文][English]
    ///   [ ] 状态图标
    ///   [ ] 音效提示
    ///         └ 就绪音效 [血雾 ▼]  [试听]
    ///   [ ] 视觉特效
    ///         └ 就绪光环 [幽魂 ▼]
    ///   [ ] 战斗日志与浮动文字
    ///   [ ] 补丁：癫狂灾瘟描述正确生效
    ///         └ 说明……
    ///   [ ] 记录调试日志
    /// </summary>
    internal static class SettingsGUI
    {
        private static readonly GUILayoutOption[] NoOptions = new GUILayoutOption[0];

        private static GUIStyle s_Note;
        private static bool s_ReportedFailure;

        public static void Draw(UnityModManager.ModEntry modEntry)
        {
            Settings s = Main.Settings;
            if (s == null)
            {
                return;
            }

            bool changed = false;

            changed |= DrawLanguage(s);

            GUILayout.Space(Scale(8f));

            // —— 状态图标 ——
            changed |= Toggle(ref s.EnableReadyIcon, "ui.channel.icon");

            // —— 音效提示：选择器只在打开时出现 ——
            changed |= Toggle(ref s.EnableReadySound, "ui.channel.sound");
            if (s.EnableReadySound)
            {
                changed |= DrawSoundPicker(s);
            }

            // —— 视觉特效：选择器只在打开时出现 ——
            changed |= Toggle(ref s.EnableReadyFx, "ui.channel.fx");
            if (s.EnableReadyFx)
            {
                changed |= DrawFxPicker(s);
            }

            // —— 战斗日志与浮动文字 ——
            changed |= Toggle(ref s.EnableCombatLog, "ui.channel.log");

            // —— 补丁开关 + 说明 ——
            changed |= Toggle(ref s.StrictConsecutiveRule, "ui.patch.strict");
            Note(L("ui.patch.strict.desc"));

            // —— 调试日志 ——
            changed |= Toggle(ref s.EnableVerboseLogging, "ui.debug");

            GUILayout.Space(Scale(8f));
            Note(L("ui.footer"));

            if (changed)
            {
                Main.OnSettingsChanged();
            }
        }

        // ---------------- 各界面的小部件 ----------------

        /// <summary>中 / 英 切换：跟随游戏 / 中文 / English。</summary>
        private static bool DrawLanguage(Settings s)
        {
            bool changed = false;

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("ui.lang"), GUILayout.ExpandWidth(false));

            // 顺序必须与 UiLanguage 枚举一致：Auto / Chinese / English
            string[] names = { L("ui.lang.auto"), L("ui.lang.zh"), L("ui.lang.en") };
            int idx = (int)s.Language;
            try
            {
                if (UnityModManager.UI.ToggleGroup(ref idx, names, Btn(), NoOptions))
                {
                    if (idx < 0) idx = 0;
                    if (idx > 2) idx = 2;
                    s.Language = (UiLanguage)idx;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                ReportOnce("语言切换控件绘制失败", ex);
            }

            GUILayout.EndHorizontal();
            return changed;
        }

        private static bool DrawSoundPicker(Settings s)
        {
            int idx = (int)s.Sound;
            if (Dropdown(L("ui.sound.pick"), ReadyCatalog.SoundNames(), ref idx,
                         delegate { Main.PreviewReadySound(s.ReadySoundEvent); }))
            {
                s.Sound = (ReadySound)idx;
                return true;
            }
            return false;
        }

        private static bool DrawFxPicker(Settings s)
        {
            int idx = (int)s.Fx;
            if (Dropdown(L("ui.fx.pick"), ReadyCatalog.FxNames(), ref idx, null))
            {
                s.Fx = (ReadyFx)idx;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 下拉选择器：交给 UMM 自己的 PopupToggleGroup 绘制。
        /// 平时只显示当前项（一个普通按钮），点开才弹出候选列表，不会撑开面板。
        /// </summary>
        private static bool Dropdown(string title, string[] names, ref int index, Action onPreview)
        {
            bool changed = false;

            GUILayout.BeginHorizontal();
            GUILayout.Space(Scale(18f));
            GUILayout.Label(title, GUILayout.ExpandWidth(false));

            int selected = index;
            try
            {
                if (UnityModManager.UI.PopupToggleGroup(ref selected, names, Btn(), GUILayout.Width(Scale(190f))))
                {
                    if (selected != index)
                    {
                        index = selected;
                        changed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                ReportOnce("下拉选择器绘制失败", ex);
            }

            if (onPreview != null && GUILayout.Button(L("ui.preview"), Btn(), GUILayout.Width(Scale(64f))))
            {
                try
                {
                    onPreview();
                }
                catch (Exception ex)
                {
                    ReportOnce("试听失败", ex);
                }
            }

            GUILayout.EndHorizontal();
            return changed;
        }

        private static bool Toggle(ref bool value, string key)
        {
            bool next = GUILayout.Toggle(value, L(key));
            if (next == value)
            {
                return false;
            }
            value = next;
            return true;
        }

        /// <summary>缩进的说明文字（自动换行）。</summary>
        private static void Note(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Space(Scale(18f));
            GUILayout.Label(text, NoteStyle(), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        // ---------------- 样式 / 工具 ----------------

        private static GUIStyle Btn()
        {
            GUIStyle st = UnityModManager.UI.button;
            return st != null ? st : GUI.skin.button;
        }

        private static GUIStyle NoteStyle()
        {
            if (s_Note == null)
            {
                s_Note = new GUIStyle(GUI.skin != null ? GUI.skin.label : new GUIStyle());
                s_Note.wordWrap = true;
            }
            return s_Note;
        }

        private static float Scale(float value)
        {
            try
            {
                return UnityModManager.UI.Scale(value);
            }
            catch
            {
                return value;
            }
        }

        private static string L(string key)
        {
            return ModLocalization.Get(key);
        }

        private static void ReportOnce(string what, Exception ex)
        {
            if (s_ReportedFailure)
            {
                return;
            }
            s_ReportedFailure = true;
            Main.LogWarning(what + "：" + ex.Message);
        }
    }
}
