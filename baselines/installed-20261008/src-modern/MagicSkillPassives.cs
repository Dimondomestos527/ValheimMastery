using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MagicSkillPassives
    {
        internal static float EitrFactor(float level) => 1f + .003f * Mathf.Clamp(level, 0f, 100f);
        internal static float BloodHealing(float level) => .10f * Mathf.Clamp(level, 0f, 100f);

        internal static bool OwnerReady(Player player) => MasteryPlugin.Settings?.Enabled.Value == true && player != null && player == Player.m_localPlayer &&
            player.m_nview != null && player.m_nview.IsValid() && player.m_nview.IsOwner() && !player.IsDead();
    }

    // Scale native base regeneration, not the equipment/status multiplier or the regen delay.
    [HarmonyPatch(typeof(Player), "UpdateStats", new[] { typeof(float) })]
    internal static class ElementalBaseEitrRegenPatch
    {
        private static void Prefix(Player __instance, out float? __state)
        {
            __state = null;
            if (!MagicSkillPassives.OwnerReady(__instance)) return;
            __state = __instance.m_eiterRegen;
            __instance.m_eiterRegen *= MagicSkillPassives.EitrFactor(
                PerkRuntimeService.GetActualSkillLevel(__instance, Skills.SkillType.ElementalMagic));
        }

        private static Exception Finalizer(Player __instance, float? __state, Exception __exception)
        {
            if (__instance != null && __state.HasValue) __instance.m_eiterRegen = __state.Value;
            return __exception;
        }
    }

    // Use the native ten-second food-healing clock. Add AFTER its status multipliers;
    // neither Rested nor a feast may multiply the flat mastery bonus again.
    [HarmonyPatch(typeof(Player), "UpdateFood", new[] { typeof(float), typeof(bool) })]
    internal static class BloodNativeHealingTickPatch
    {
        private static void Prefix(Player __instance, float dt, bool forceUpdate, out bool __state)
        {
            __state = MagicSkillPassives.OwnerReady(__instance) && !forceUpdate && dt >= 0f &&
                __instance.m_foodRegenTimer + dt >= 10f;
        }

        private static void Postfix(Player __instance, bool __state)
        {
            if (!__state || !MagicSkillPassives.OwnerReady(__instance) || __instance.m_foodRegenTimer != 0f) return;
            float amount = MagicSkillPassives.BloodHealing(
                PerkRuntimeService.GetActualSkillLevel(__instance, Skills.SkillType.BloodMagic));
            if (MagicShield35Service.HasMasterShield(__instance))
            {
                amount += 5f;
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Shield35] player healing tick: barrier bonus=5");
            }
            if (amount > 0f) __instance.Heal(amount, true);
        }
    }
}
