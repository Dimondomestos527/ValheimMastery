using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Failure-only, at most one line per second; never changes input or attack gates.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class SecondaryAttackDiagnostics
    {
        private static float Next;
        private static void Postfix(Humanoid __instance, bool secondaryAttack, bool __result)
        {
            if (!secondaryAttack || __result || __instance != Player.m_localPlayer ||
                !MasteryPlugin.Settings.VerboseLogging.Value || Time.time < Next) return;
            Next = Time.time + 1f;
            ItemDrop.ItemData weapon = __instance.GetCurrentWeapon();
            MasteryPlugin.Log.LogInfo("[SecondaryDenied] weapon=" + PerkRuntimeService.ItemPrefabName(weapon) +
                " nativeSecondary=" + (weapon?.HaveSecondaryAttack() == true) +
                " attack=" + __instance.InAttack() + " dodge=" + __instance.InDodge() +
                " stagger=" + __instance.IsStaggering() + " move=" + __instance.CanMove() +
                " knockback=" + __instance.IsKnockedBack() + " minor=" + __instance.InMinorAction() +
                " animatorSpeed=" + (__instance.m_animator != null ? __instance.m_animator.speed : -1f));
        }
    }
}
