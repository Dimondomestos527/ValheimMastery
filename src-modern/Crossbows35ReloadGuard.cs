using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Crossbows35ReloadGuard
    {
        internal static bool Active(Player player)
            => Reloading(player, true);
        internal static bool SprintReload(Player player)
            => player != null && !player.IsSwimming() && Reloading(player, false);
        // Steam 1.0.17's traversal hook is a Tool with SkillType.None. Match
        // its exact registered item, not all None-skill items or the harpoon.
        internal static bool IsTraversalHook(ItemDrop.ItemData item)
            => item?.m_dropPrefab != null &&
                string.Equals(item.m_dropPrefab.name, "GrapplingHook", StringComparison.Ordinal);
        private static bool Reloading(Player player, bool started)
        {
            if (MasteryPlugin.Settings?.Enabled.Value != true || player == null || player.IsDead() ||
                player.m_nview?.IsOwner() != true || player.InAttack() || player.InDodge() ||
                player.IsStaggering() || player.m_actionQueuePause > 0f ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crossbows, 35)) return false;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon?.m_shared == null ||
                (weapon.m_shared.m_skillType != Skills.SkillType.Crossbows && !IsTraversalHook(weapon)) ||
                weapon.m_shared.m_attack?.m_requiresReload != true ||
                player.m_weaponLoaded == weapon || player.m_actionQueue == null || player.m_actionQueue.Count == 0) return false;
            Player.MinorActionData action = player.m_actionQueue[0];
            // A queued reload behind equip/unequip or a paused/cancelled action
            // is not active protection. Never use Player.m_isLoading: profile load.
            return action != null && ActionMatches(action.m_item, weapon, (int)action.m_type == 2,
                action.m_time, action.m_duration, action.m_animation, weapon.m_shared.m_attack.m_reloadAnimation,
                player.m_actionAnimation, started);
        }
        internal static bool ActionMatches(object item, object weapon, bool reload, float time, float duration,
            string animation, string expected, string current, bool started)
            => reload && ReferenceEquals(item, weapon) && (started ? time > 0f : time >= 0f) && time <= duration &&
                !string.IsNullOrEmpty(animation) && string.Equals(animation, expected, StringComparison.Ordinal) &&
                (!started && time == 0f || string.Equals(current, animation, StringComparison.Ordinal));
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
