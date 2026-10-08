using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // A cosmetic-free master mark carries the casting player's entitlement to
    // the target owner. It NEVER applies frost, changes resistance, or extends it.
    internal static class Ice35Exposure
    {
        internal static readonly int MasterFrostHash = "VM_MasterFrost35".GetStableHashCode();
        internal static void Register(ObjectDB db)
        {
            if (db == null || db.GetStatusEffect(MasterFrostHash) != null) return;
            SE_Stats mark = ScriptableObject.CreateInstance<SE_Stats>();
            mark.name = "VM_MasterFrost35"; mark.m_nameHash = MasterFrostHash;
            mark.m_ttl = 30f; mark.m_icon = null;
            db.m_StatusEffects.Add(mark);
        }
        internal static void Mark(Character target, HitData hit)
        {
            Player caster = hit?.GetAttacker() as Player;
            if (target == null || target.IsBoss() || target.IsPlayer() || target.IsTamed() ||
                hit == null || hit.m_damage.m_frost <= 0f || !MagicSkillPassives.OwnerReady(caster) ||
                !PerkRuntimeService.HasPerk(caster, Skills.SkillType.ElementalMagic, 35) ||
                !BaseAI.IsEnemy(caster, target)) return;
            HitData.DamageModifier resistance = target.GetDamageModifiers().m_frost;
            if (resistance == HitData.DamageModifier.Immune || resistance == HitData.DamageModifier.Ignore) return;
            Register(ObjectDB.instance);
            if (ObjectDB.instance?.GetStatusEffect(MasterFrostHash) != null)
                target.GetSEMan().AddStatusEffect(MasterFrostHash, true);
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Ice35MasterFrostHitPatch
    { private static void Prefix(Character __instance, HitData hit) => Ice35Exposure.Mark(__instance, hit); }
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class Ice35MasterFrostDatabasePatch
    { private static void Postfix(ObjectDB __instance) => Ice35Exposure.Register(__instance); }
    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class Ice35MasterFrostCopyPatch
    { private static void Postfix(ObjectDB __instance) => Ice35Exposure.Register(__instance); }
}
