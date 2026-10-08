using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MagicShield35Service
    {
        internal sealed class ShieldState
        {
            internal bool MasterCast;
            internal bool Broken;
            internal float NextHeal;
        }

        private static readonly ConditionalWeakTable<SE_Shield, ShieldState> States =
            new ConditionalWeakTable<SE_Shield, ShieldState>();
        internal static void SetCasterLevel(SE_Shield shield, float level)
        {
            if (shield == null) return;
            ShieldState state = States.GetOrCreateValue(shield);
            state.MasterCast = shield.m_levelUpSkillOnBreak == Skills.SkillType.BloodMagic && level >= 35f;
            state.NextHeal = Time.time + 10f;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Shield35] level=" + level + " skill=" + shield.m_levelUpSkillOnBreak + " master=" + state.MasterCast);
            if (state.MasterCast && shield.m_character != null && shield.m_character.GetComponent<Shield35HealingVisual>() == null)
                shield.m_character.gameObject.AddComponent<Shield35HealingVisual>();
            // Renewal never marks an exhausted barrier as intact.
            state.Broken = shield.m_damage > shield.m_totalAbsorbDamage;
        }

        internal static bool HasMasterShield(Character player)
        {
            List<StatusEffect> effects = player?.GetSEMan()?.GetStatusEffects();
            if (effects == null) return false;
            foreach (StatusEffect effect in effects)
                if (effect is SE_Shield shield && States.TryGetValue(shield, out ShieldState state) &&
                    state.MasterCast && !state.Broken && shield.m_damage <= shield.m_totalAbsorbDamage &&
                    (shield.m_ttl <= 0f || shield.m_time < shield.m_ttl)) return true;
            return false;
        }

        internal static void OnDamage(SE_Shield shield)
        {
            if (shield != null && States.TryGetValue(shield, out ShieldState state) &&
                shield.m_damage > shield.m_totalAbsorbDamage) state.Broken = true;
            // The failed pressure mechanic is removed. Vanilla owns break VFX,
            // removal and XP; no extra damaging/pushing burst is dispatched.
        }
        internal static void HealSummon(SE_Shield shield)
        {
            Character recipient = shield?.m_character;
            if (recipient == null || recipient.IsPlayer() || recipient.IsDead() || recipient.m_nview?.IsOwner() != true ||
                MasteryPlugin.Settings.Enabled.Value != true || !States.TryGetValue(shield, out ShieldState state) ||
                !state.MasterCast || state.Broken || shield.m_damage > shield.m_totalAbsorbDamage ||
                (shield.m_ttl > 0f && shield.m_time >= shield.m_ttl) || Time.time < state.NextHeal) return;
            state.NextHeal = Time.time + 10f;
            // Only actual friendly summons, not wild creatures or another
            // player; Player uses the existing food-healing tick instead.
            if (recipient.GetComponent<MonsterAI>()?.GetFollowTarget()?.GetComponent<Player>() != null)
                recipient.Heal(5f, true);
        }
    }
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class Shield35RenewRegistrationPatch
    { private static void Postfix(Character __instance) => Shield35Renewal.Register(__instance); }

    [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.SetLevel))]
    internal static class MagicShieldCasterLevelPatch
    {
        private static void Postfix(SE_Shield __instance, float skillLevel) =>
            MagicShield35Service.SetCasterLevel(__instance, skillLevel);
    }

    [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
    internal static class MagicShieldPressurePatch
    {
        private static void Postfix(SE_Shield __instance) => MagicShield35Service.OnDamage(__instance);
    }
    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.UpdateStatusEffect))]
    internal static class MagicShieldSummonHealingPatch
    {
        private static void Postfix(StatusEffect __instance)
        { if (__instance is SE_Shield shield) MagicShield35Service.HealSummon(shield); }
    }
}
