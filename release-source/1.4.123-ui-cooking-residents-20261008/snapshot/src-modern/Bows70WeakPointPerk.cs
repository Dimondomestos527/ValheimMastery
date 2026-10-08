using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class BowWeakPointState
    {
        internal Character Target;
        internal Vector3 LocalPoint;
        internal float ExpiresAt;
        internal float NextScanAt;
        internal float HitRadius;
        internal float VisualRadius;
        internal BowWeakPointMarker Marker;
    }

    internal static class Bows70WeakPointService
    {
        private const float MarkDuration = 5f;
        private const float HeavyHitRadius = 0.95f;
        private const float MaxAimAngle = 12f;
        private static readonly ConditionalWeakTable<Player, BowWeakPointState> States = new ConditionalWeakTable<Player, BowWeakPointState>();

        internal static void Update(Player player)
        {
            // Aim direction and camera intent only exist reliably on the owning client.
            if (player == null || player != Player.m_localPlayer ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Bows, 35) || !IsDrawingBow(player)) return;

            BowWeakPointState state = States.GetOrCreateValue(player);
            if (state.Target != null && !state.Target.IsDead() && Time.time < state.ExpiresAt && state.Marker != null) return;
            if (Time.time < state.NextScanAt) return;
            state.NextScanAt = Time.time + 0.20f;

            Character target = FindTarget(player);
            CapsuleCollider collider = target?.GetCollider();
            if (target == null || collider == null) return;

            Bounds bounds = collider.bounds;
            if (state.Marker != null) Object.Destroy(state.Marker.gameObject);
            state.Target = target;
            state.HitRadius = GetHitRadius(target, bounds);
            state.VisualRadius = GetVisualRadius(target, bounds);
            state.ExpiresAt = Time.time + MarkDuration;
            state.Marker = PerkVisualService.MarkBowWeakPointLocal(player, target, collider, MarkDuration, state.VisualRadius);
            state.LocalPoint = target.transform.InverseTransformPoint(state.Marker != null ? state.Marker.CurrentWorldPoint : collider.ClosestPoint(player.GetEyePoint()));
        }

        internal static bool ConsumeIfHit(Player player, Character target, HitData hit)
        {
            if (player == null || player != Player.m_localPlayer || target == null || hit == null || hit.m_skill != Skills.SkillType.Bows ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Bows, 35) ||
                !States.TryGetValue(player, out BowWeakPointState state) || state.Target != target || Time.time > state.ExpiresAt) return false;

            Vector3 point = state.Marker != null ? state.Marker.CurrentWorldPoint : target.transform.TransformPoint(state.LocalPoint);
            if (Vector3.Distance(hit.m_point, point) > state.HitRadius) return false;
            state.ExpiresAt = 0f;
            state.Target = null;
            if (state.Marker != null) Object.Destroy(state.Marker.gameObject);
            state.Marker = null;
            hit.m_damage.Modify(2f);
            // The weak-point mark and its damage bonus are the level-35 bow perk.
            // Routing this as bows_70 only produced a false level-70 proc message;
            // the actual Overdraw mechanics correctly remained locked.
            PerkVisualService.PlayAtWorldPosition(player, "bows_35", point, true);
            return true;
        }

        // Pure query used by Bow70 before Character.Damage invokes the existing Bow35
        // consumer.  It does not move, consume or multiply the weak point.
        internal static bool IsWeakPointHit(Player player, Character target, Vector3 hitPoint)
        {
            if (player == null || player != Player.m_localPlayer || target == null ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Bows, 35) ||
                !States.TryGetValue(player, out BowWeakPointState state) || state.Target != target || Time.time > state.ExpiresAt) return false;
            Vector3 point = state.Marker != null ? state.Marker.CurrentWorldPoint : target.transform.TransformPoint(state.LocalPoint);
            return Vector3.Distance(hitPoint, point) <= state.HitRadius;
        }

        private static float GetHitRadius(Character target, Bounds bounds)
        {
            CreatureClass kind = MasteryClassificationService.GetCreatureClass(target);
            if (kind == CreatureClass.Boss || kind == CreatureClass.Heavy) return HeavyHitRadius;
            float modelScale = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            return Mathf.Clamp(modelScale * 0.32f, 0.18f, 0.58f);
        }

        private static float GetVisualRadius(Character target, Bounds bounds)
        {
            CreatureClass kind = MasteryClassificationService.GetCreatureClass(target);
            if (kind == CreatureClass.Boss || kind == CreatureClass.Heavy) return 0.35f;
            float modelScale = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            // The visible diameter must remain 0.5-0.7m per the Eyes of Huginn spec;
            // smaller creatures scale toward the lower end instead of becoming invisible.
            return Mathf.Clamp(modelScale * 0.18f, 0.25f, 0.30f);
        }
        private static bool IsDrawingBow(Player player)
        {
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            // IsDrawingBow() is the stable Valheim 1.0 signal. m_attackHold can be false while
            // the bow is visibly held at draw, which made the marker silently disappear.
            return weapon?.m_shared?.m_skillType == Skills.SkillType.Bows && player.IsDrawingBow();
        }

        private static Character FindTarget(Player player)
        {
            Vector3 eye = player.GetEyePoint();
            Vector3 aim = player.GetAimDir(eye).normalized;
            Character chosen = null;
            float bestAngle = MaxAimAngle;
            float bestDepth = float.MaxValue;
            foreach (Character candidate in Character.GetAllCharacters())
            {
                if (candidate == null || candidate == player || candidate.IsDead() || candidate is Player || !BaseAI.IsEnemy(player, candidate)) continue;
                Vector3 to = candidate.GetCenterPoint() - eye;
                float depth = Vector3.Dot(to, aim);
                if (depth <= 0f || depth > 80f) continue;
                float angle = Vector3.Angle(aim, to);
                if (angle > bestAngle + 0.01f || (Mathf.Abs(angle - bestAngle) < 0.01f && depth >= bestDepth)) continue;
                RaycastHit obstruction;
                if (Physics.Linecast(eye, candidate.GetCenterPoint(), out obstruction, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    obstruction.collider?.GetComponentInParent<Character>() != candidate) continue;
                chosen = candidate;
                bestAngle = angle;
                bestDepth = depth;
            }
            return chosen;
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Bows70WeakPointAimPatch
    {
        private static void Postfix(Player __instance) => Bows70WeakPointService.Update(__instance);
    }

    // Tag the outgoing hit before vanilla serializes it to the target owner/server.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class Bows70WeakPointDamagePatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Bows70WeakPointService.ConsumeIfHit(hit?.GetAttacker() as Player, __instance, hit);
        }
    }
}
