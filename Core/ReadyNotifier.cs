using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items.Slots;
using Kingmaker.PubSubSystem;
using Kingmaker.ResourceLinks;
using Kingmaker.ResourceManagement;
using Kingmaker.Sound;
using Kingmaker.UI;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.Visual.Particles;
using Kingmaker.Visual.Particles.FxSpawnSystem;
using Kingmaker.Visual.Sound;
using UnityEngine;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// "已就绪"提示：原版 Buff 26f9b2e6… 出现的瞬间 = 下一次施法会带上强效/极效。
    ///
    /// 通道分工：
    ///   状态图标  —— BlueprintPatcher 去掉 HiddenInUi，常驻可见；
    ///   持久微光  —— 写进 Buff 蓝图的 FxOnStart，引擎自己挂/卸，本类不插手；
    ///   一次性爆发 —— 本类在就绪瞬间挂一下（可挂武器/手部），几秒后自动回收；
    ///   音效 / 战斗日志 / 头顶浮动文字 —— 本类在就绪瞬间各打一次。
    /// </summary>
    internal class ReadyNotifier : IUnitBuffHandler
    {
        // 已经播过"就绪"的单位（防止重新装备等重复触发时重复播）
        private readonly HashSet<UnitEntityData> m_Primed = new HashSet<UnitEntityData>();

        public void HandleBuffDidAdded(Buff buff)
        {
            try
            {
                UnitEntityData unit = ResolveUnit(buff);
                if (unit == null)
                {
                    return;
                }
                if (!m_Primed.Add(unit))
                {
                    return; // 这个单位已经提示过了
                }
                OnReady(unit);
            }
            catch (Exception ex)
            {
                Main.LogDebug("HandleBuffDidAdded 异常：" + ex);
            }
        }

        public void HandleBuffDidRemoved(Buff buff)
        {
            try
            {
                UnitEntityData unit = ResolveUnit(buff);
                if (unit == null)
                {
                    return;
                }
                // 持久微光由 Buff 的 FxOnStart 托管，随 Buff 消失自动销毁，这里只需解除标记
                m_Primed.Remove(unit);
            }
            catch (Exception ex)
            {
                Main.LogDebug("HandleBuffDidRemoved 异常：" + ex);
            }
        }

        /// <summary>清掉已经离场单位的残留标记。</summary>
        public void Tick()
        {
            if (m_Primed.Count == 0)
            {
                return;
            }

            List<UnitEntityData> dead = null;
            foreach (UnitEntityData unit in m_Primed)
            {
                if (unit == null || unit.View == null)
                {
                    (dead ?? (dead = new List<UnitEntityData>())).Add(unit);
                }
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                {
                    m_Primed.Remove(dead[i]);
                }
            }
        }

        public void CleanupAll()
        {
            m_Primed.Clear();
        }

        // ---------------- 内部 ----------------

        private static UnitEntityData ResolveUnit(Buff buff)
        {
            if (buff == null || buff.Blueprint == null)
            {
                return null;
            }
            if (!string.Equals(buff.Blueprint.AssetGuid.ToString(), GameRefs.BuffGuid, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (buff.Owner == null)
            {
                return null;
            }
            return buff.Owner.Unit;
        }

        private void OnReady(UnitEntityData unit)
        {
            Settings s = Main.Settings;

            if (s == null || s.EnableCombatLog)
            {
                RaiseLog(ModLocalization.Get("ready.log"));
                ShowBark(unit, ModLocalization.Get("ready.bark"));
            }

            if (s == null || s.EnableReadySound)
            {
                PlaySound(unit, s != null ? s.ReadySoundEvent : "Doom");
            }

            if (s != null && s.EnableReadyFx && s.FxPlayBurst)
            {
                IFxHandle burst = SpawnBurstFx(unit, s.ReadyFxPrefabId, s.FxAttachToWeapon, s.FxLocator);
                if (burst != null)
                {
                    FxAutoRelease.Attach(burst, s.FxBurstSeconds);
                }
            }
            else if (s == null)
            {
                IFxHandle burst = SpawnBurstFx(unit, null, true, null);
                if (burst != null)
                {
                    FxAutoRelease.Attach(burst, 2.5f);
                }
            }

            Main.LogDebug("已就绪提示已发出：" + (unit.CharacterName ?? unit.UniqueId));
        }

        private static void RaiseLog(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            try
            {
                EventBus.RaiseEvent<ILogMessageUIHandler>(h => h.HandleLogMessage(text), true);
            }
            catch (Exception ex)
            {
                Main.LogDebug("战斗日志写入失败：" + ex.Message);
            }
        }

        private static void ShowBark(UnitEntityData unit, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            UIAccess ui = Game.Instance != null ? Game.Instance.UI : null;
            if (ui == null)
            {
                return;
            }
            try
            {
                ui.Bark(unit, text, 4f);
            }
            catch (Exception ex)
            {
                Main.LogDebug("浮动文字失败：" + ex.Message);
            }
        }

        private static void PlaySound(UnitEntityData unit, string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }
            try
            {
                GameObject anchor = null;
                if (unit != null && unit.View != null)
                {
                    anchor = unit.View.gameObject;
                }
                if (anchor == null)
                {
                    anchor = SoundState.Get2DSoundObject();
                }
                if (anchor == null)
                {
                    return;
                }
                // canBeStopped = false：一次性播完就走，不占用"可停止"通道
                SoundEventsManager.PostEvent(eventName, anchor, false);
            }
            catch (Exception ex)
            {
                Main.LogDebug("音效播放失败：" + ex.Message);
            }
        }

        /// <summary>找主手 / 副手的武器特效挂点；都没有则返回 null（调用方会回退到头顶）。</summary>
        private static WeaponParticlesSnapMap FindHandSnapMap(UnitEntityData unit)
        {
            try
            {
                if (unit == null || unit.Body == null)
                {
                    return null;
                }

                HandSlot main = unit.Body.PrimaryHand;
                if (main != null && main.FxSnapMap != null)
                {
                    return main.FxSnapMap;
                }

                HandSlot off = unit.Body.SecondaryHand;
                if (off != null && off.FxSnapMap != null)
                {
                    return off.FxSnapMap;
                }
            }
            catch (Exception ex)
            {
                Main.LogDebug("读取武器挂点失败：" + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 一次性爆发特效：优先挂在主手武器/手部，挂点不可用时在单位上方炸一下。
        /// 由 FxAutoRelease 定时回收。
        /// </summary>
        private static IFxHandle SpawnBurstFx(UnitEntityData unit, string prefabId, bool toWeapon, string locator)
        {
            if (unit == null || unit.View == null)
            {
                return null;
            }

            Settings s = Main.Settings;
            string id = !string.IsNullOrEmpty(prefabId)
                ? prefabId
                : (s != null ? s.ReadyFxPrefabId : null);
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            try
            {
                PrefabLink link = new PrefabLink { AssetId = id };
                using (BundledResourceHandle<GameObject> handle = link.RequestHandle())
                {
                    GameObject prefab = handle != null ? handle.Object : null;
                    if (prefab == null)
                    {
                        Main.LogWarning("找不到特效预制体：" + id);
                        return null;
                    }

                    if (toWeapon && string.IsNullOrEmpty(locator))
                    {
                        try
                        {
                            WeaponParticlesSnapMap snap = FindHandSnapMap(unit);
                            if (snap != null)
                            {
                                IFxHandle fx = FxHelper.SpawnFxOnWeapon(prefab, unit.View, snap);
                                if (fx != null)
                                {
                                    return fx;
                                }
                            }
                            else
                            {
                                Main.LogDebug("主手/副手都没有 FxSnapMap，爆发特效改为头顶播放。");
                            }
                        }
                        catch (Exception ex)
                        {
                            Main.LogDebug("武器挂点失败，改为头顶：" + ex.Message);
                        }
                    }

                    Transform t = unit.View.transform;
                    Vector3 point = t.position + Vector3.up * 1.6f;
                    return FxHelper.SpawnFxOnPoint(prefab, point, unit.IsPlayerFaction, t.rotation, FxPriority.Important);
                }
            }
            catch (Exception ex)
            {
                Main.LogDebug("生成爆发特效失败：" + ex.Message);
                return null;
            }
        }
    }

    /// <summary>给没有 AutoDestroy 的一次性特效兜一层定时回收。</summary>
    internal class FxAutoRelease : MonoBehaviour
    {
        private IFxHandle m_Handle;
        private float m_TimeLeft;

        public static void Attach(IFxHandle handle, float seconds)
        {
            if (handle == null)
            {
                return;
            }
            GameObject host = new GameObject("[PlagueOfMadnessHint.FxTimer]");
            FxAutoRelease timer = host.AddComponent<FxAutoRelease>();
            timer.m_Handle = handle;
            timer.m_TimeLeft = Mathf.Max(0.1f, seconds);
        }

        private void Update()
        {
            m_TimeLeft -= Time.unscaledDeltaTime;
            if (m_TimeLeft > 0f)
            {
                return;
            }

            try
            {
                if (m_Handle != null && !m_Handle.IsDestroyed)
                {
                    FxHelper.Destroy(m_Handle);
                }
            }
            catch (Exception ex)
            {
                Main.LogDebug("定时回收特效失败：" + ex.Message);
            }

            Destroy(gameObject);
        }
    }
}
