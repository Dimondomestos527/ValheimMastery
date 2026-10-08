using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Base Fists scaling is intentionally bare-hand only. Fist weapons still use their own item stats
    // and the separate milestone perks, but do not inherit this free 40% damage / 20% speed package.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class FistsBareHandDamagePatch
    {
        private static void Prefix(HitData hit)
        {
            Player player = hit?.GetAttacker() as Player;
            if (player == null || hit.m_skill != Skills.SkillType.Unarmed ||
                !PerkRuntimeService.IsBareHands(player.GetCurrentWeapon()) ||
                !PerkRuntimeService.TryMarkApplied(hit, "fists_bare_damage"))
                return;

            float level = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Unarmed);
            hit.ApplyModifier(1f + 0.004f * Mathf.Clamp(level, 0f, 100f));
        }
    }

    // Captured Attack intent survives vanilla clearing m_currentAttackIsSecondary. Only the
    // standard unarmed kick receives this bonus; generated Maul and other secondaries do not.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class FistsKickScalingPatch
    {
        private static void Prefix(HitData hit)
        {
            Player player = hit?.GetAttacker() as Player;
            Attack attack = player?.m_currentAttack;
            if (player == null || hit.m_skill != Skills.SkillType.Unarmed || hit.m_ranged ||
                PerkRuntimeService.IsPerkGenerated(hit) ||
                player.GetCurrentWeapon()?.m_shared?.m_skillType != Skills.SkillType.Unarmed ||
                !AttackIntentService.IsSecondary(attack, player) ||
                !string.Equals(attack?.m_attackAnimation, "kick", System.StringComparison.OrdinalIgnoreCase) ||
                !PerkRuntimeService.TryMarkApplied(hit, "fists_kick_scaling"))
                return;

            float level = Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Unarmed), 0f, 100f);
            hit.ApplyModifier(1f + 0.030f * level);
            hit.m_staggerMultiplier *= 1f + 0.0125f * level;
            hit.m_pushForce *= 1f + 0.020f * level;
        }
    }
}
