using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace PlagueOfMadnessHint
{
    /// <summary>
    /// 癫狂灾瘟（Plague of Madness）相关蓝图的固定 GUID。
    /// 蓝图链路：Item -> Enchantment(AddUnitFeatureEquipment) -> Feature(AbilityUsagesCountTrigger x3) -> Buff(AutoMetamagic: Empower+Maximize)
    /// </summary>
    internal static class GameRefs
    {
        public const string ItemGuid = "39755c2bcd870e1499825a4eb8b5abf8";    // PlaguePersistenceQuarterstaffItem
        public const string EnchantGuid = "748be82ca42748f4adf255d6768d40e7"; // PlaguePersistenceQuarterstaffEnchantment
        public const string FeatureGuid = "a05cd6b2f01240c5943779fef1ccc0d4"; // PlaguePersistenceQuarterstaffFeature
        public const string BuffGuid = "26f9b2e682474494822b42be9f23c23e";    // PlaguePersistenceQuarterstaffBuff（"已就绪"状态的载体）

        public static BlueprintBuff Buff
        {
            get { return ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(BuffGuid); }
        }

        public static BlueprintFeature Feature
        {
            get { return ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(FeatureGuid); }
        }
    }
}
