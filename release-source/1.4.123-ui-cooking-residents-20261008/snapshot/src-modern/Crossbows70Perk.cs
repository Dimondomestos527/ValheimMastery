#if !MASTERY_RELEASE_SAFE
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class Crossbows70BoltState : MonoBehaviour
    {
        internal readonly HashSet<Character> HitTargets = new HashSet<Character>();
    }

    internal static class Crossbows70Service
    {
        internal const float SecondTargetDamageMultiplier = 0.75f;

        internal static bool TryHandleHit(Projectile projectile, Collider collider, Vector3 hitPoint)
        {
            if (projectile == null || projectile.m_originalHitData == null ||
                projectile.m_skill != Skills.SkillType.Crossbows ||
                projectile.GetComponent<OverdrawProjectileTag>() != null ||
                (projectile.m_nview != null && projectile.m_nview.IsValid() && !projectile.m_nview.IsOwner()))
                return false;
            Player owner = projectile.m_owner as Player;
            Character target = collider?.GetComponentInParent<Character>();
            if (owner == null || target == null || target == owner || target.IsDead() ||
                !PerkRuntimeService.HasPerk(owner, Skills.SkillType.Crossbows, 70) ||
                !BaseAI.IsEnemy(owner, target)) return false;

            Crossbows70BoltState state = projectile.GetComponent<Crossbows70BoltState>() ??
                projectile.gameObject.AddComponent<Crossbows70BoltState>();
            Vector3 direction = projectile.m_vel.sqrMagnitude > 0.01f ? projectile.m_vel.normalized : projectile.transform.forward;
            if (state.HitTargets.Contains(target))
            {
                ContinuePast(projectile, collider, target, hitPoint, direction);
                return true;
            }

            float multiplier = state.HitTargets.Count == 0 ? 1f : SecondTargetDamageMultiplier;
            HitData hit = projectile.m_originalHitData.Clone();
            hit.m_damage = projectile.m_damage;
            hit.m_damage.Modify(multiplier);
            hit.m_skill = Skills.SkillType.Crossbows;
            hit.m_point = hitPoint;
            hit.m_dir = direction;
            hit.m_ranged = true;
            hit.m_pushForce = Mathf.Max(hit.m_pushForce, projectile.m_attackForce);
            hit.SetAttacker(owner);
            target.Damage(hit);
            state.HitTargets.Add(target);
            PerkVisualService.PlayProc(owner, "crossbows_70", hitPoint, false, false);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Crossbows70] target=" + target.gameObject.name +
                    " hit=" + state.HitTargets.Count + " damageMultiplier=" + multiplier.ToString("0.##"));

            if (state.HitTargets.Count >= 2)
            {
                if (ZNetScene.instance != null) ZNetScene.instance.Destroy(projectile.gameObject);
                else Object.Destroy(projectile.gameObject);
            }
            else ContinuePast(projectile, collider, target, hitPoint, direction);
            return true;
        }

        private static void ContinuePast(Projectile projectile, Collider collider, Character target, Vector3 hitPoint, Vector3 direction)
        {
            Bounds bounds = collider != null ? collider.bounds : target.GetCollider().bounds;
            Collider body = target.GetCollider();
            if (body != null) bounds.Encapsulate(body.bounds);
            float projectedCenter = Vector3.Dot(bounds.center - hitPoint, direction);
            float projectedExtent = Mathf.Abs(direction.x) * bounds.extents.x +
                                    Mathf.Abs(direction.y) * bounds.extents.y +
                                    Mathf.Abs(direction.z) * bounds.extents.z;
            projectile.transform.position = hitPoint + direction * Mathf.Max(0.45f, projectedCenter + projectedExtent + 0.35f);
            projectile.m_didHit = false;
            projectile.m_hitList?.Clear();
            projectile.m_hitHistory?.Clear();
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    [HarmonyPriority(Priority.First)]
    internal static class Crossbows70PiercingHitPatch
    {
        private static bool Prefix(Projectile __instance, Collider collider, Vector3 hitPoint) =>
            !Crossbows70Service.TryHandleHit(__instance, collider, hitPoint);
    }
}
#endif
