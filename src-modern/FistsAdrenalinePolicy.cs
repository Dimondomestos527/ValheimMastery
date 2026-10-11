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

    // Native Player.AddAdrenaline remains untouched. Trinkets and all ordinary game
    // sources must keep filling the bar regardless of the currently equipped weapon.
    // Only the mastery bonus multiplier is fists-only; see FistsPassiveAdrenalineGainPatch.
}
