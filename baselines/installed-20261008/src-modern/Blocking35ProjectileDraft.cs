#if false // Retired unsafe manual-block draft; replacement is Blocking35CorrelatedProjectiles.
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Source-only draft: runtime multiplayer validation is required before release.
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    [HarmonyPriority(Priority.First)]
    internal static class Blocking35ProjectileParryPatch
    {
        private sealed class ReflectedMarker { }
        private static readonly ConditionalWeakTable<Projectile, ReflectedMarker> Reflected =
            new ConditionalWeakTable<Projectile, ReflectedMarker>();

        private static bool Prefix(Projectile __instance, Collider collider)
        {
            Player defender = collider?.GetComponentInParent<Player>();
            if (__instance == null || defender == null || __instance.m_owner == defender ||
                Reflected.TryGetValue(__instance, out _) ||
                !PerkRuntimeService.HasPerk(defender, Skills.SkillType.Blocking, 35) ||
                ShieldWeaponClassService.Classify(defender.GetCurrentBlocker()) != MasteryShieldClass.ParryCapable ||
                !defender.IsBlocking() || defender.m_blockTimer < 0f ||
                defender.m_blockTimer > Humanoid.m_perfectBlockInterval)
                return true;

            Character attacker = __instance.m_owner;
            if (attacker == null || attacker.IsDead()) return true;
            HitData blockHit = __instance.m_originalHitData != null ? __instance.m_originalHitData.Clone() : new HitData();
            blockHit.m_damage = __instance.m_damage;
            blockHit.SetAttacker(attacker);
            Blocking70ProjectileParryPatch.IsManualBlock = true;
            bool blocked;
            try { blocked = defender.BlockAttack(blockHit, attacker); }
            finally { Blocking70ProjectileParryPatch.IsManualBlock = false; }
            if (!blocked) return true;

            Reflected.Add(__instance, new ReflectedMarker());
            Blocking70ReflectionService.ReflectProjectile(defender, __instance, attacker, "blocking_35_reflect");
            return false; // Original projectile payload never touches the defender.
        }
    }
}
#endif
