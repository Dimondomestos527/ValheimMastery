using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Crossbows35ReloadGuard
    {
        internal static bool Active(Player player)
        {
            if (MasteryPlugin.Settings?.Enabled.Value != true || player == null || player.IsDead() ||
                player.m_nview?.IsOwner() != true || player.InAttack() || player.InDodge() ||
                player.IsStaggering() || player.m_actionQueuePause > 0f ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crossbows, 35)) return false;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon?.m_shared?.m_skillType != Skills.SkillType.Crossbows ||
                weapon.m_shared.m_attack?.m_requiresReload != true ||
                player.m_weaponLoaded == weapon || player.m_actionQueue == null || player.m_actionQueue.Count == 0) return false;
            Player.MinorActionData action = player.m_actionQueue[0];
            // A queued reload behind equip/unequip or a paused/cancelled action
            // is not active protection. Never use Player.m_isLoading: profile load.
            return action != null && (int)action.m_type == 2 && ReferenceEquals(action.m_item, weapon) &&
                action.m_time > 0f && action.m_time <= action.m_duration &&
                !string.IsNullOrEmpty(action.m_animation) &&
                string.Equals(action.m_animation, weapon.m_shared.m_attack.m_reloadAnimation, StringComparison.Ordinal) &&
                string.Equals(player.m_actionAnimation, action.m_animation, StringComparison.Ordinal);
        }
        internal static float Armor(float current) => Mathf.Max(0f, current) * 1.4f + 20f;
    }
    [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
    internal static class Crossbows35ReloadArmorPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance, ref float __result)
        { if (Crossbows35ReloadGuard.Active(__instance)) __result = Crossbows35ReloadGuard.Armor(__result); }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.GetStaggerTreshold))]
    internal static class Crossbows35ReloadStaggerPatch
    {
        private static void Postfix(Character __instance, ref float __result)
        { if (__instance is Player player && Crossbows35ReloadGuard.Active(player)) __result *= 2f; }
    }
}
