#if !MASTERY_RELEASE_SAFE || MASTERY_SHIELD35_EXPERIMENT
namespace ValheimMastery
{
    internal enum MasteryShieldClass { NotShield, ParryCapable, Heavy }

    // Valheim 1.0.x Humanoid.BlockAttack enters its timed-parry branch only when
    // SharedData.m_timedBlockBonus > 1. The same boundary must drive Mastery perks.
    internal static class ShieldWeaponClassService
    {
        internal static MasteryShieldClass Classify(ItemDrop.ItemData item)
        {
            if (item?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.Shield)
                return MasteryShieldClass.NotShield;
            return ClassifyTimedBlockBonus(item.m_shared.m_timedBlockBonus);
        }

        internal static MasteryShieldClass ClassifyTimedBlockBonus(float bonus) =>
            bonus > 1f ? MasteryShieldClass.ParryCapable : MasteryShieldClass.Heavy;
    }
}
#endif
