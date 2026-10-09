using System;
using System.Collections.Generic;
using System.IO;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// 本 Mod 自己的文本表：Localization/&lt;lang&gt;.json。
    ///
    /// 语言选择规则（与设置界面上的"界面语言"一致）：
    ///   · 默认（Auto）—— 跟随游戏当前语言；
    ///   · 游戏语言不是中文 —— 一律用英文（本 Mod 目前只提供 zh-CN / en 两套）；
    ///   · 手动指定中文 / 英文 —— 以手动选择为准。
    /// 找不到对应文件时回退到 en.json。
    /// </summary>
    internal static class ModLocalization
    {
        public const string EnglishCode = "en";
        public const string ChineseCode = "zh-CN";

        private static Dictionary<string, string> s_Translations;
        private static bool s_Initialized;
        private static string s_Language = EnglishCode;

        public static string Language { get { return s_Language; } }

        public static void Initialize(UnityModManager.ModEntry modEntry)
        {
            s_Initialized = false;
            Load(modEntry, ResolveWantedCode());
            s_Initialized = true;
            RefreshDynamicTexts();
        }

        /// <summary>每帧调用；语言（设置或游戏）变化后自动重新加载。</summary>
        public static void Tick(UnityModManager.ModEntry modEntry)
        {
            string want = ResolveWantedCode();
            if (!string.Equals(want, s_Language, StringComparison.OrdinalIgnoreCase))
            {
                Load(modEntry, want);
                RefreshDynamicTexts();
            }
        }

        /// <summary>设置里的语言被改动后调用，立即生效（不需要等下一帧）。</summary>
        public static void ApplySettings(UnityModManager.ModEntry modEntry)
        {
            string want = ResolveWantedCode();
            if (!string.Equals(want, s_Language, StringComparison.OrdinalIgnoreCase))
            {
                Load(modEntry, want);
            }
            RefreshDynamicTexts();
        }

        /// <summary>让改写过名称/描述的状态图标文本跟着换语言。</summary>
        private static void RefreshDynamicTexts()
        {
            BlueprintPatcher.RegisterTexts();
        }

        /// <summary>当前设置 + 游戏语言 → 应该用哪套文本。</summary>
        private static string ResolveWantedCode()
        {
            Settings s = Main.Settings;
            if (s != null)
            {
                if (s.Language == UiLanguage.Chinese)
                {
                    return ChineseCode;
                }
                if (s.Language == UiLanguage.English)
                {
                    return EnglishCode;
                }
            }
            return ResolveGameLanguageCode();
        }

        /// <summary>把游戏的 Locale 映射成我们的文件名；非中文一律英文。</summary>
        private static string ResolveGameLanguageCode()
        {
            try
            {
                switch (Kingmaker.Localization.LocalizationManager.CurrentLocale)
                {
                    case Locale.zhCN: return ChineseCode;
                    default: return EnglishCode;
                }
            }
            catch
            {
                return EnglishCode;
            }
        }

        private static void Load(UnityModManager.ModEntry modEntry, string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                languageCode = EnglishCode;
            }
            s_Language = languageCode;

            string path = Path.Combine(modEntry.Path, "Localization", languageCode + ".json");
            Dictionary<string, string> table = TryRead(path);

            if (table == null && !string.Equals(languageCode, EnglishCode, StringComparison.OrdinalIgnoreCase))
            {
                // 没有这套语言 → 回退英文
                path = Path.Combine(modEntry.Path, "Localization", EnglishCode + ".json");
                table = TryRead(path);
                Main.LogWarning("缺少本地化文件 " + languageCode + ".json，已回退到英文。");
            }

            s_Translations = table ?? new Dictionary<string, string>();
            Main.LogDebug("已加载本地化：" + path + "（" + s_Translations.Count + " 条）");
        }

        private static Dictionary<string, string> TryRead(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                Dictionary<string, string> table =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                return table ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Main.LogError("本地化加载失败 " + path + "：" + ex.Message);
                return new Dictionary<string, string>();
            }
        }

        /// <summary>取文本；找不到 key 时返回 key 本身（方便一眼看出漏了哪条）。</summary>
        public static string Get(string key)
        {
            return Get(key, null);
        }

        public static string Get(string key, params object[] args)
        {
            if (!s_Initialized)
            {
                return key;
            }

            string fmt;
            if (s_Translations != null && s_Translations.TryGetValue(key, out fmt))
            {
                if (args == null || args.Length == 0)
                {
                    return fmt;
                }
                try
                {
                    return string.Format(fmt, args);
                }
                catch (FormatException)
                {
                    return fmt;
                }
            }
            return key;
        }
    }

    /// <summary>
    /// 往游戏的语言包里塞我们自己的键值，这样就能给蓝图挂上"会跟随语言"的本地化文本。
    /// 语言包会在换语言/读档时被整个替换，所以 Tick() 里检测引用变化后重新写入。
    /// </summary>
    internal static class LocalizationBridge
    {
        private static readonly Dictionary<string, string> s_Strings = new Dictionary<string, string>();
        private static LocalizationPack s_LastPack;

        public static void Register(string key, string text)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
            s_Strings[key] = text ?? string.Empty;
            Flush();
        }

        public static void Tick()
        {
            if (s_Strings.Count == 0)
            {
                return;
            }
            if (!ReferenceEquals(Kingmaker.Localization.LocalizationManager.CurrentPack, s_LastPack))
            {
                Flush();
            }
        }

        private static void Flush()
        {
            LocalizationPack pack = Kingmaker.Localization.LocalizationManager.CurrentPack;
            if (pack == null)
            {
                return;
            }
            foreach (KeyValuePair<string, string> kv in s_Strings)
            {
                try
                {
                    pack.PutString(kv.Key, kv.Value);
                }
                catch (Exception ex)
                {
                    Main.LogDebug("写入语言包失败 " + kv.Key + "：" + ex.Message);
                }
            }
            s_LastPack = pack;
        }
    }
}
