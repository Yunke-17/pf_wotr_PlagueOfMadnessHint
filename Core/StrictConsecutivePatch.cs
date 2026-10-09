using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.FactLogic;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// "补丁开关"：严格按武器描述实现——「连续」施放三次同一个法术。
    ///
    /// 原版 AbilityUsagesCountTrigger 是按法术分别累计次数、只有休息才清零，
    /// 中间放别的法术不会打断（所以描述里的"连续"其实不准确，
    /// 奥法维持之靴"一天中四次"的描述才是原版真实行为）。
    ///
    /// 这里在原版逻辑之后补一刀：本次施放的法术保留计数，其余法术的计数全部清零。
    /// 只对本武器的 Feature（a05cd6b2…）生效，不会影响共用同一组件的奥法维持之靴。
    ///
    /// ------------------------------------------------------------------
    /// 【0.3.0 关键修复】0.2.0 里这个补丁"挂上了却完全不生效"，日志里是：
    ///     Method `UnitFactComponentDelegate`1[SpellCastsData].get_Fact()' is inaccessible
    ///
    /// 原因是：编译时我们引用的是经过"公开化"的 Assembly-CSharp_public.dll，
    /// 但运行时游戏加载的是原始 Assembly-CSharp.dll，那里的成员还是 protected/internal。
    /// 只要在补丁里直接写 __instance.Fact / __instance.Data，JIT 就会抛 MethodAccessException，
    /// 异常被 catch 吞掉 —— 补丁等于没跑。
    ///
    /// 修复办法：
    ///   · 识别"是不是本武器"改用 BlueprintComponent.OwnerBlueprint（public，无需反射）；
    ///   · 取 SpellCastsData 改用反射读 protected 属性 Data；
    ///   · Fact 反射作为 OwnerBlueprint 为空时的兜底。
    /// 反射取的是属性 **getter**，它内部读 ComponentEventContext.CurrentRuntime；
    /// 而事件分发是 using (proxy.RequestEventContext()) { handler.OnEventDidTrigger(evt); }，
    /// Postfix 仍在这个 using 作用域内，所以 CurrentRuntime 有效（已对反编译源码逐一确认）。
    /// ------------------------------------------------------------------
    ///
    /// 这是 Harmony 补丁，在模组载入（= 游戏启动）时一次性挂上；
    /// 是否启用由设置里的开关在运行时判断，所以改开关立即生效、不需要重启游戏。
    /// </summary>
    [HarmonyPatch(typeof(AbilityUsagesCountTrigger), "OnEventDidTrigger", new Type[] { typeof(RuleCastSpell) })]
    internal static class StrictConsecutivePatch
    {
        // ---- 反射缓存 ----
        private static PropertyInfo s_DataProperty;    // EntityFactComponentDelegate<TEntity,TData>.Data（protected）
        private static PropertyInfo s_FactProperty;    // UnitFactComponentDelegate<TData>.Fact（protected new）
        private static bool s_DataLookupFailed;

        /// <summary>补丁真正清掉过计数的次数（给设置界面/日志用，可自证"到底跑没跑"）。</summary>
        internal static int ClearedCount { get; private set; }

        /// <summary>
        /// 启动自检：只解析反射通道、不依赖真实施法。
        /// 把结果打进 Log.txt，这样"补丁到底能不能工作"在游戏启动时就能确认，
        /// 不用等到实际施法才发现问题（0.2.0 就是这么踩坑的）。
        /// </summary>
        internal static string SelfTest()
        {
            try
            {
                PropertyInfo data = FindProperty(typeof(AbilityUsagesCountTrigger), "Data");
                PropertyInfo fact = FindProperty(typeof(AbilityUsagesCountTrigger), "Fact");
                if (data == null)
                {
                    return "失败：找不到 AbilityUsagesCountTrigger 的 Data 属性，严格连续补丁不会生效";
                }
                return "正常（Data → " + data.PropertyType.Name +
                       "，Fact → " + (fact != null ? fact.PropertyType.Name : "未找到，将只用 OwnerBlueprint") + "）";
            }
            catch (Exception ex)
            {
                return "异常：" + ex.Message;
            }
        }

        private static void Postfix(AbilityUsagesCountTrigger __instance, RuleCastSpell evt)
        {
            try
            {
                if (Main.Settings == null || !Main.Settings.StrictConsecutiveRule)
                {
                    return;
                }
                if (__instance == null || evt == null)
                {
                    return;
                }

                // 只作用癫狂灾瘟（不影响共用同一组件的奥法维持之靴）
                if (!IsTargetFeature(__instance))
                {
                    return;
                }

                SpellCastsData data = GetData(__instance);
                if (data == null || data.SpellCasts == null || data.SpellCasts.Count == 0)
                {
                    return;
                }

                BlueprintAbility cast = evt.Spell != null ? evt.Spell.Blueprint : null;
                if (cast == null)
                {
                    return;
                }

                if (Main.Settings.EnableVerboseLogging)
                {
                    DumpCounters("施放 " + cast.name + " 之后", data);
                }

                List<BlueprintAbility> stale = null;
                foreach (KeyValuePair<BlueprintAbility, int> kv in data.SpellCasts)
                {
                    if (!ReferenceEquals(kv.Key, cast) && kv.Value > 0)
                    {
                        (stale ?? (stale = new List<BlueprintAbility>())).Add(kv.Key);
                    }
                }

                if (stale == null)
                {
                    return;
                }

                for (int i = 0; i < stale.Count; i++)
                {
                    data.SpellCasts.Remove(stale[i]);
                }

                ClearedCount++;
                // 普通日志级别：这个分支正是"补丁有没有生效"的直接证据
                Main.Log(ModLocalization.Get("log.strict", cast.name, stale.Count));
            }
            catch (Exception ex)
            {
                Main.LogDebug("StrictConsecutivePatch 异常：" + ex.Message);
            }
        }

        // ---------------- 反射辅助 ----------------

        /// <summary>判断这个 trigger 是不是挂在癫狂灾瘟的 Feature 上。</summary>
        private static bool IsTargetFeature(AbilityUsagesCountTrigger inst)
        {
            // 首选：BlueprintComponent.OwnerBlueprint（public，能直接拿到"这个组件属于哪个蓝图"）
            BlueprintScriptableObject owner = null;
            try
            {
                owner = inst.OwnerBlueprint;
            }
            catch (Exception ex)
            {
                Main.LogDebug("读取 OwnerBlueprint 失败：" + ex.Message);
            }

            if (owner != null)
            {
                return string.Equals(owner.AssetGuid.ToString(), GameRefs.FeatureGuid, StringComparison.OrdinalIgnoreCase);
            }

            // 兜底：反射读 protected Fact → UnitFact.Blueprint
            return string.Equals(GetFactOwnerGuid(inst), GameRefs.FeatureGuid, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFactOwnerGuid(AbilityUsagesCountTrigger inst)
        {
            if (s_FactProperty == null)
            {
                s_FactProperty = FindProperty(inst.GetType(), "Fact");
            }
            if (s_FactProperty == null)
            {
                return null;
            }

            object fact = s_FactProperty.GetValue(inst, null);
            if (fact == null)
            {
                return null;
            }

            UnitFact unitFact = fact as UnitFact;
            if (unitFact != null && unitFact.Blueprint != null)
            {
                return unitFact.Blueprint.AssetGuid.ToString();
            }

            EntityFact entityFact = fact as EntityFact;
            if (entityFact != null && entityFact.Blueprint != null)
            {
                return entityFact.Blueprint.AssetGuid.ToString();
            }

            return null;
        }

        /// <summary>反射读 protected TData Data。</summary>
        private static SpellCastsData GetData(AbilityUsagesCountTrigger inst)
        {
            if (s_DataProperty == null && !s_DataLookupFailed)
            {
                s_DataProperty = FindProperty(inst.GetType(), "Data");
                if (s_DataProperty == null)
                {
                    s_DataLookupFailed = true;
                    Main.LogWarning("找不到 AbilityUsagesCountTrigger.Data 属性，「严格连续」补丁无法工作。");
                }
            }
            if (s_DataProperty == null)
            {
                return null;
            }

            return s_DataProperty.GetValue(inst, null) as SpellCastsData;
        }

        /// <summary>沿继承链找属性（DeclaredOnly，保证拿到最派生的那个）。</summary>
        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (p != null && p.CanRead)
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>调试用：把当前每个法术的累计次数打出来。</summary>
        private static void DumpCounters(string prefix, SpellCastsData data)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("严格连续 · 计数明细（").Append(prefix).Append("）：");
                foreach (KeyValuePair<BlueprintAbility, int> kv in data.SpellCasts)
                {
                    sb.Append('[').Append(kv.Key != null ? kv.Key.name : "null").Append('=').Append(kv.Value).Append("] ");
                }
                Main.LogDebug(sb.ToString());
            }
            catch
            {
                // 仅调试用，失败无所谓
            }
        }
    }
}
