using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class FistsAdrenalinePolicy
    {
        internal static bool Eligible(Player player) => player != null && FistsGainRules.WeaponEligible(
            player.GetCurrentWeapon()?.m_shared?.m_skillType == Skills.SkillType.Unarmed,
            player.m_leftItem == null || player.m_leftItem.m_shared?.m_skillType == Skills.SkillType.Unarmed);

        internal static void Log(Player player, string phase, float requested, float before, float after, float baseGain, float modified)
        {
            if (player == null || !MasteryPlugin.Settings.VerboseLogging.Value) return;
            string action = player.m_currentAttack?.m_attackAnimation ?? (player.IsBlocking() ? "block/parry" : "native/rpc/status");
            MasteryPlugin.Log.LogInfo("[FistsGain] player=" + player.GetPlayerID() + " frame=" + Time.frameCount +
                " phase=" + phase + " action=" + action +
                " weapon=" + PerkRuntimeService.ItemPrefabName(player.GetCurrentWeapon()) + " fists=" + Eligible(player) +
                " requested=" + requested + " base=" + baseGain + " modified=" + modified +
                " before=" + before + " after=" + after + " current=" + player.GetAdrenaline() + " max=" + player.GetMaxAdrenaline());
        }
    }

    // All native AddAdrenaline callers converge here through Character's virtual call.
    // Do not zero the stored bar or skip negative native costs/decay.
    [HarmonyPatch(typeof(Player), nameof(Player.AddAdrenaline))]
    [HarmonyPriority(Priority.First)]
    internal static class FistsPositiveAdrenalineGate
    {
        internal struct GainState { internal float Before, Requested; internal bool Blocked; }
        private static bool Prefix(Player __instance, float v, out GainState __state)
        {
            __state = new GainState { Before = __instance.GetAdrenaline(), Requested = v };
            bool allow = FistsGainRules.Allow(MasteryPlugin.Settings.Enabled.Value, FistsAdrenalinePolicy.Eligible(__instance), v);
            __state.Blocked = !allow;
            if (!allow) FistsAdrenalinePolicy.Log(__instance, "blocked-nonfist", v, __state.Before, __state.Before, v, 0f);
            return allow;
        }
        private static void Postfix(Player __instance, GainState __state)
        {
            if (!__state.Blocked && __state.Requested > 0f)
                FistsAdrenalinePolicy.Log(__instance, "native-complete", __state.Requested, __state.Before,
                    __instance.GetAdrenaline(), __state.Requested, __instance.GetAdrenaline() - __state.Before);
        }
    }
}
