using System;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// 就绪音效候选。全部取自游戏自己的音效库（SoundbanksInfo.xml），
    /// 并且已确认 DurationType = OneShot —— 一次放完就结束，不存在循环播放。
    /// 显示名在 Localization/*.json 里（键：sound.&lt;枚举名&gt;）。
    /// </summary>
    public enum ReadySound
    {
        Doom,
        DirgeOfDoom,
        UnholyNimbus,
        NecromancyCone,
        NegativeEnergy,
        LichEruption,
        ChillTouch,
        ExplosionOfRot,
        AbyssalChains,
        ProfaneHymn,
        ShamanHex,
        BloodHeal,
        BoneShaker,
        DisruptUndead,
        BloodMist,
    }

    /// <summary>
    /// 就绪光环候选。取自游戏内"阴森"主题 Buff 自带的光环特效（FxOnStart），
    /// 是常驻微光：随"已就绪"状态出现，法术放出去后同步消失。
    /// 显示名在 Localization/*.json 里（键：fx.&lt;枚举名&gt;）。
    /// </summary>
    public enum ReadyFx
    {
        UnholyAura,
        Spectre,
        DweomerNecromancy,
        DeathRealm,
        UnholyBlight,
        DirgeOfDoom,
        ShadowShield,
        Fear,
        GhostWraith,
        WitchDeathCurse,
    }

    /// <summary>
    /// 提示文本 / 界面文本使用的语言。
    /// 顺序必须与设置界面上的按钮顺序一致（跟随游戏 / 中文 / English）。
    /// </summary>
    public enum UiLanguage
    {
        /// <summary>跟随游戏语言；识别不出时用英文。</summary>
        Auto,
        /// <summary>强制简体中文。</summary>
        Chinese,
        /// <summary>强制英文。</summary>
        English,
    }

    /// <summary>
    /// 枚举 &lt;-&gt; 游戏资源 id / 本地化键 的映射表。
    /// 放在这里而不是 Settings 里，是为了让设置类只保留需要显示的字段。
    /// </summary>
    internal static class ReadyCatalog
    {
        private static readonly string[] SoundIds =
        {
            "Doom",
            "DirgeOfDoom",
            "VFX_Creature_UnholyNimbus_AoE",
            "ConeNecromancy",
            "VFX_ChannelNegativeEnergyCone30Feet",
            "VFX_LichNegativeEruption00_Aoe",
            "ChillTouch",
            "ExplosionOfRot",
            "Mythic4lvlDemon_AbyssalChains00",
            "VFX_Mythic7lvlDemon_ProfaneHymn00",
            "ShamanHexChantAbility",
            "Mythic6lvlLich_BloodHeal00_Up_Play",
            "BoneShaker",
            "DisruptUndead",
            "BloodMist",
        };

        private static readonly string[] FxPrefabs =
        {
            "48a3a2315c3ad2243a41f4757356587d",
            "ff3a2ba38558ec04fa0e831eb6e50f7c",
            "6e01d9f56e260ea4088836571d0e6404",
            "debefcaeac48b8e4eaa7205b88df1dbe",
            "d3c03d0642effaf4c8f4deb356926870",
            "39da71647ad4747468d41920d0edd721",
            "d0eb97ed13ca55f48af2e26ebac2f669",
            "72e115990e2d31c40b9573f3d50ec60a",
            "3365595fd05222546989ede9a139283b",
            "f4adcaac22054a042ab43b09bf107dd6",
        };

        public static string SoundId(ReadySound value)
        {
            int i = (int)value;
            return (i >= 0 && i < SoundIds.Length) ? SoundIds[i] : SoundIds[0];
        }

        public static string FxPrefab(ReadyFx value)
        {
            int i = (int)value;
            return (i >= 0 && i < FxPrefabs.Length) ? FxPrefabs[i] : FxPrefabs[0];
        }

        /// <summary>音效显示名的本地化键，例如 sound.BloodMist。</summary>
        public static string SoundKey(ReadySound value)
        {
            return "sound." + value;
        }

        /// <summary>光环显示名的本地化键，例如 fx.Spectre。</summary>
        public static string FxKey(ReadyFx value)
        {
            return "fx." + value;
        }

        /// <summary>给下拉框用的、当前语言下的音效名列表（顺序与枚举一致）。</summary>
        public static string[] SoundNames()
        {
            int n = SoundIds.Length;
            string[] names = new string[n];
            for (int i = 0; i < n; i++)
            {
                ReadySound v = (ReadySound)i;
                names[i] = ModLocalization.Get(SoundKey(v));
            }
            return names;
        }

        /// <summary>给下拉框用的、当前语言下的光环名列表（顺序与枚举一致）。</summary>
        public static string[] FxNames()
        {
            int n = FxPrefabs.Length;
            string[] names = new string[n];
            for (int i = 0; i < n; i++)
            {
                ReadyFx v = (ReadyFx)i;
                names[i] = ModLocalization.Get(FxKey(v));
            }
            return names;
        }

        /// <summary>en / zh-CN / ""（空串 = 跟随游戏语言）。</summary>
        public static string LangCode(UiLanguage value)
        {
            switch (value)
            {
                case UiLanguage.English: return "en";
                case UiLanguage.Chinese: return "zh-CN";
                default: return string.Empty;
            }
        }

        public static UiLanguage LangFromCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return UiLanguage.Auto;
            }
            if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return UiLanguage.Chinese;
            }
            if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return UiLanguage.English;
            }
            return UiLanguage.Auto;
        }
    }
}
