using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Blocking70ReflectionService
    {
        internal static void HandleBlock(Player player, Character attacker, float blockedDamage, bool perfect)
        {
#if MASTERY_SHIELD35_EXPERIMENT
            if (Blocking35ProjectileService.Handling(player)) return;
#endif
#if MASTERY_SHIELD_RUSH_EXPERIMENT
            return; // New70 is shield rush, never legacy passive HP reflection.
#else
            if (player == null || attacker == null || attacker.IsDead() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Blocking, 70)) return;
            bool shield = player.GetCurrentBlocker()?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield;
            // Blocking 70 is shield-only. Weapons and fists must never create reflected damage.
            if (!shield) return;
            if (perfect && PerkCooldownStateService.TryConsume(player, "blocking_70", 30d))
            {
                DealParrySplash(player, attacker, blockedDamage * 0.50f);
                return;
            }
            DealDamage(player, attacker, blockedDamage * 0.05f, "blocking_70");
            PerkFeedbackService.Play(player, "blocking_70_block", attacker.GetCenterPoint(), false);
#endif
        }

        internal static void ReflectProjectile(Player player, Projectile projectile, Character attacker, string feedbackId = "blocking_70_projectile")
        {
            if (player == null || projectile == null) return;
            Vector3 direction = attacker != null && !attacker.IsDead()
                ? (attacker.GetCenterPoint() - player.GetCenterPoint()).normalized
                : -projectile.m_vel.normalized;
            if (direction.sqrMagnitude < 0.01f) direction = player.transform.forward;
            float speed = Mathf.Max(1f, projectile.m_vel.magnitude) * 1.5f;
            projectile.m_owner = player;
            projectile.m_vel = direction * speed;
            projectile.m_didHit = false;
            projectile.m_hitList?.Clear();
            projectile.m_hitHistory?.Clear();
            if (projectile.m_originalHitData != null) projectile.m_originalHitData.SetAttacker(player);
            projectile.transform.position = player.GetCenterPoint() + direction * (player.GetRadius() + 0.75f);
            PerkVisualService.PlayAtWorldPosition(player, feedbackId, player.GetCenterPoint(), true);
        }

        private static void DealParrySplash(Player player, Character primary, float damage)
        {
            if (damage <= 0.01f) return;
            DealDamage(player, primary, damage, "blocking_70_parry");
            List<Character> nearby = new List<Character>();
            foreach (Character candidate in Character.GetAllCharacters())
                if (candidate != null && candidate != primary && candidate != player && !candidate.IsDead() && !candidate.IsPlayer() &&
                    BaseAI.IsEnemy(player, candidate) && (candidate.GetCenterPoint() - primary.GetCenterPoint()).sqrMagnitude <= 36f)
                    nearby.Add(candidate);
            nearby.Sort((a, b) => (a.GetCenterPoint() - primary.GetCenterPoint()).sqrMagnitude.CompareTo((b.GetCenterPoint() - primary.GetCenterPoint()).sqrMagnitude));
            for (int i = 0; i < Mathf.Min(3, nearby.Count); ++i) DealDamage(player, nearby[i], damage, "blocking_70_parry");
            PerkVisualService.PlayAtWorldPosition(player, "blocking_70_parry", primary.GetCenterPoint(), true);
        }

        private static void DealDamage(Player player, Character target, float damage, string perkId)
        {
            if (target == null || target.IsDead() || damage <= 0.01f) return;
            HitData reflected = new HitData();
            reflected.m_skill = Skills.SkillType.Blocking;
            reflected.m_point = target.GetCenterPoint();
            reflected.m_dir = (target.GetCenterPoint() - player.GetCenterPoint()).normalized;
            reflected.m_damage.m_damage = damage;
            reflected.SetAttacker(player);
            PerkHitContext context = PerkRuntimeService.GetHitContext(reflected);
            context.IsPerkGenerated = true; context.PerkId = perkId; context.AllowSelfProc = false; context.AllowOtherPerkProc = false;
            target.Damage(reflected);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    [HarmonyPriority(Priority.First)]
    internal static class Blocking70ProjectileParryPatch
    {
        [ThreadStatic] internal static bool IsManualBlock;

#if MASTERY_SHIELD35_EXPERIMENT || MASTERY_SHIELD_RUSH_EXPERIMENT
        // The next design moves projectile reflection to Blocking35; keep this
        // legacy class for shared-hook compatibility, but never patch it in draft builds.
        private static bool Prepare() => false;
#endif

        private static bool Prefix(Projectile __instance, Collider collider, Vector3 hitPoint)
        {
            Player defender = collider?.GetComponentInParent<Player>();
            if (defender == null || __instance == null || __instance.m_owner == defender ||
                !PerkRuntimeService.HasPerk(defender, Skills.SkillType.Blocking, 70) ||
                defender.GetCurrentBlocker()?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.Shield ||
                defender.m_blockTimer < 0f || defender.m_blockTimer > Humanoid.m_perfectBlockInterval)
                return true;

            Character attacker = __instance.m_owner;
            if (attacker == null || attacker.IsDead()) return true;
            HitData blockHit = __instance.m_originalHitData != null ? __instance.m_originalHitData.Clone() : new HitData();
            blockHit.m_damage = __instance.m_damage;
            blockHit.SetAttacker(attacker);
            IsManualBlock = true;
            bool blocked;
            try { blocked = defender.BlockAttack(blockHit, attacker); }
            finally { IsManualBlock = false; }
            if (!blocked) return true;

            Blocking70ReflectionService.ReflectProjectile(defender, __instance, attacker);
            return false;
        }
    }
}
