using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Passive traversal scaling, not a milestone or a weapon skill reclassification.
    internal static class CrossbowsNativeHookScaling
    {
        private static readonly ConditionalWeakTable<Projectile, object> Applied = new ConditionalWeakTable<Projectile, object>();
        private static readonly ConditionalWeakTable<GrapplingPoint, object> Points = new ConditionalWeakTable<GrapplingPoint, object>();
        internal static float Level(float level)
            => float.IsNaN(level) || float.IsInfinity(level) ? 0f : Mathf.Clamp(level, 0f, 100f);
        internal static float RangeFactor(float level) => 1f + Level(level) / 100f;
        internal static float ChargeFactor(float level) => 1f + 1.5f * Level(level) / 100f;
        internal static float FlightTime(float native, float level) => native * RangeFactor(level);
        internal static float ChargeTime(float native, float level) => native / ChargeFactor(level);
        internal static bool Actor(Player player)
            => MasteryPlugin.Settings?.Enabled.Value == true && player != null &&
                player == Player.m_localPlayer && player.m_nview?.IsOwner() == true && !player.IsDead();
        internal static float Skill(Player player)
            => Actor(player) ? PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Crossbows) : 0f;

        internal static void ScaleFlight(Projectile projectile, Character owner, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            if (!(owner is Player player) || !Actor(player) || !Crossbows35ReloadGuard.IsTraversalHook(item) ||
                ammo != null || projectile == null || projectile.m_nview?.IsOwner() != true ||
                !string.Equals(projectile.gameObject.name, "Projectile_GrapplingHook(Clone)", StringComparison.Ordinal) ||
                projectile.m_spawnOnHit == null || projectile.m_spawnOnHit.GetComponent<GrapplingPoint>() == null ||
                projectile.m_ttl <= 0f || Applied.TryGetValue(projectile, out _)) return;
            // Extend this owned primary projectile's flight window only. Velocity,
            // gravity, damage, native collision/DeepNorth blocker and pull speed stay native.
            // Horizontal maximum flight extent scales; obstacles and ballistic drop still apply.
            Applied.Add(projectile, new object());
            projectile.m_ttl = FlightTime(projectile.m_ttl, Skill(player));
        }

        internal static void ScaleQueuedCharge(Player player, int countBefore)
        {
            if (!Actor(player) || countBefore < 0 || player.m_actionQueue == null ||
                player.m_actionQueue.Count != countBefore + 1) return;
            Player.MinorActionData action = player.m_actionQueue[countBefore];
            if (action == null || (int)action.m_type != 2 || action.m_time != 0f || action.m_duration <= 0f ||
                !ReferenceEquals(action.m_item, player.GetCurrentWeapon()) || !Crossbows35ReloadGuard.IsTraversalHook(action.m_item) ||
                action.m_item.m_shared?.m_attack?.m_requiresReload != true ||
                !string.Equals(action.m_animation, action.m_item.m_shared.m_attack.m_reloadAnimation, StringComparison.Ordinal)) return;
            // Exactly the newly appended native action. Repeated queue calls, resume,
            // equip actions and ordinary crossbows cannot compound this multiplier.
            action.m_duration = ChargeTime(action.m_duration, Skill(player));
        }

        internal static void ScalePoint(GrapplingPoint point, Character character)
        {
            if (!(character is Player player) || !Actor(player) || point == null || point.m_secondary ||
                point.m_nview?.IsOwner() != true || point.m_maxLength <= 0f ||
                !Crossbows35ReloadGuard.IsTraversalHook(point.m_equipCheck?.m_itemData) ||
                Points.TryGetValue(point, out _)) return;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (!Crossbows35ReloadGuard.IsTraversalHook(weapon)) return;
            Projectile donor = weapon.m_shared?.m_attack?.m_attackProjectile?.GetComponent<Projectile>();
            if (donor?.m_spawnOnHit == null ||
                !string.Equals(point.gameObject.name, donor.m_spawnOnHit.name + "(Clone)", StringComparison.Ordinal)) return;
            // Native Update breaks the rope above this length. Scale this primary
            // instance before Activate, never the shared donor or secondary chain.
            Points.Add(point, new object());
            point.m_maxLength = FlightTime(point.m_maxLength, Skill(player));
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.QueueReloadAction))]
    internal static class CrossbowsNativeHookChargePatch
    {
        private static void Prefix(Player __instance, out int __state)
            => __state = __instance.m_actionQueue?.Count ?? -1;
        private static void Postfix(Player __instance, int __state)
            => CrossbowsNativeHookScaling.ScaleQueuedCharge(__instance, __state);
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class CrossbowsNativeHookFlightPatch
    {
        private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
            => CrossbowsNativeHookScaling.ScaleFlight(__instance, owner, item, ammo);
    }

    [HarmonyPatch(typeof(GrapplingPoint), nameof(GrapplingPoint.Activate))]
    internal static class CrossbowsNativeHookPointPatch
    {
        private static void Prefix(GrapplingPoint __instance, Character character)
            => CrossbowsNativeHookScaling.ScalePoint(__instance, character);
    }
}
