#if MASTERY_RELEASE_SAFE && !MASTERY_SPEAR35_EXPERIMENT
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class PinnedAnchorState { internal Vector3 Position; }

    internal static class SpearPinned70Service
    {
        internal const string PinnedId = "pinned";
        internal static void TryPin(Projectile projectile, Collider collider, Vector3 hitPoint)
        {
            Character target = collider?.GetComponentInParent<Character>();
            Player player = projectile?.m_owner as Player;
            HitData source = projectile?.m_originalHitData;
            if (target == null || player == null || source == null || source.m_skill != Skills.SkillType.Spears || target.IsDead() || target.IsBoss() || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Spears, 70)) return;
            Vector3 direction = projectile.m_vel.sqrMagnitude > 0.01f ? projectile.m_vel.normalized : source.m_dir.normalized;
            if (direction.sqrMagnitude < 0.01f) return;
            Bounds bounds = target.GetCollider()?.bounds ?? new Bounds(target.GetCenterPoint(), Vector3.one);
            bool heavy = MasteryClassificationService.GetCreatureClass(target) == CreatureClass.Heavy;
            if (heavy && hitPoint.y > bounds.min.y + bounds.size.y * 0.40f) return;
            if (!Physics.Raycast(hitPoint + direction * 0.08f, direction, out RaycastHit anchor, heavy ? 2f : 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            // Any non-character collider behind the target is valid world geometry: terrain, tree, stone, or building.
            if (anchor.collider == null || anchor.collider.GetComponentInParent<Character>() != null) return;
            TargetEffectService.Apply(target, PinnedId, player, 1, 0.25f, heavy ? 5f : 30f);
            MasteryStateStore.GetTargetState<PinnedAnchorState>(target).Position = target.transform.position;
            PinnedRootConstraint constraint = target.GetComponent<PinnedRootConstraint>();
            if (constraint == null) constraint = target.gameObject.AddComponent<PinnedRootConstraint>();
            constraint.Bind(target);
            PerkFeedbackService.Play(player, "spears_70_pin", hitPoint, true);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    [HarmonyPriority(Priority.Last)]
    internal static class SpearPinned70ProjectileHitPatch
    {
        private static void Postfix(Projectile __instance, Collider collider, Vector3 hitPoint) => SpearPinned70Service.TryPin(__instance, collider, hitPoint);
    }

    // Character does not declare a Unity Update method in current Valheim assemblies.
    // The component is attached only while a target is pinned and naturally dies with that target.
    internal sealed class PinnedRootConstraint : MonoBehaviour
    {
        private Character _target;
        internal void Bind(Character target) { _target = target; }
        private void LateUpdate()
        {
            if (_target == null || _target.IsDead() || !TargetEffectService.TryGet(_target, SpearPinned70Service.PinnedId, out TargetEffect effect) || !MasteryStateStore.TryGetTargetState<PinnedAnchorState>(_target, out PinnedAnchorState anchor)) { Destroy(this); return; }
            Vector3 delta = _target.transform.position - anchor.Position;
            if (delta.sqrMagnitude > 0.0225f) _target.transform.position = anchor.Position;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddStaggerDamage))]
    [HarmonyPriority(Priority.First)]
    internal static class SpearPinned70StaggerPatch
    {
        private static void Prefix(Character __instance, ref float damage)
        {
            if (damage > 0f && TargetEffectService.TryGet(__instance, SpearPinned70Service.PinnedId, out TargetEffect effect)) damage *= 1f + effect.Strength;
        }
    }
}
#endif
