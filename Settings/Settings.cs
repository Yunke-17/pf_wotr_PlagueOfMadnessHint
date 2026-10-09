using System.Xml.Serialization;
using UnityModManagerNet;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// 本 Mod 的设置项。
    ///
    /// 界面由 <see cref="SettingsGUI"/> 手工绘制（用 UMM 自己的控件 UI.PopupToggleGroup /
    /// UI.ToggleGroup / UI.button + UI.Scale，风格与 UMM 一致），之所以不再用 UMM 的
    /// 自动绘制 UI.DrawFields，是因为那些 [Draw("标签")] 的标签是**编译期写死**的，
    /// 没法跟随语言切换，也没法做"音效提示关掉就把音效选择器藏起来"这种条件显示。
    ///
    /// 序列化照旧走 UMM 的 ModSettings（XmlSerializer 只认 public 字段）。
    /// 不想让玩家看到的参数一律 internal —— 它们不会被序列化，也不会出现在界面上。
    /// </summary>
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // ================= 显示给玩家的项（界面顺序由 SettingsGUI 决定） =================

        /// <summary>界面 / 提示文本语言。默认跟随游戏，识别不出时用英文。</summary>
        public UiLanguage Language = UiLanguage.Auto;

        public bool EnableReadyIcon = true;

        public bool EnableReadySound = true;

        public bool EnableReadyFx = true;

        public bool EnableCombatLog = true;

        /// <summary>就绪音效（仅在 EnableReadySound 打开时显示在界面上）。</summary>
        public ReadySound Sound = ReadySound.BloodMist;

        /// <summary>就绪光环（仅在 EnableReadyFx 打开时显示在界面上）。</summary>
        public ReadyFx Fx = ReadyFx.Spectre;

        /// <summary>
        /// 补丁：让"癫狂灾瘟"的描述真正成立 —— 必须「连续」三次施放同一个法术。
        /// 默认关闭，保持原版行为。
        /// </summary>
        public bool StrictConsecutiveRule = false;

        public bool EnableVerboseLogging = false;

        // ================= 内部参数（internal，不会出现在界面上） =================

        /// <summary>一次性爆发特效是否挂在武器/手部（false = 头顶）。</summary>
        internal bool FxAttachToWeapon = true;

        /// <summary>是否播放"就绪瞬间"的一次性爆发特效。</summary>
        internal bool FxPlayBurst = true;

        /// <summary>爆发特效存活秒数，到点自动回收。</summary>
        internal float FxBurstSeconds = 4f;

        /// <summary>爆发特效的挂点名（空 = 引擎默认）。</summary>
        internal string FxLocator = string.Empty;

        // ================= 给其它模块用的只读视图 =================
        // 只读属性不会被 XmlSerializer 序列化。

        [XmlIgnore]
        public string ReadySoundEvent { get { return ReadyCatalog.SoundId(Sound); } }

        [XmlIgnore]
        public string ReadyFxPrefabId { get { return ReadyCatalog.FxPrefab(Fx); } }

        /// <summary>强制指定的语言代码；空串表示"跟随游戏语言"。</summary>
        [XmlIgnore]
        public string SelectedLanguage { get { return ReadyCatalog.LangCode(Language); } }

        // ================= UMM 接口 =================

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        /// <summary>UMM 在设置项被改动后回调（IDrawable）。</summary>
        public void OnChange()
        {
            Main.OnSettingsChanged();
        }
    }
}
