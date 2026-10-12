using System;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Scope the native SpawnOnHit loop; native still instantiates and sets up
    // every child, including speed, inherited hit data, author, item and physics.
    internal static class Ashlands35ClusterSpread
    {
        internal struct Frame
        {
            internal Projectile Parent;
            internal int Count, Index;
            internal bool Active;
        }
        [ThreadStatic] internal static Frame Current;

        internal static Frame Begin(Projectile parent)
        {
            if (parent == null || !parent.name.StartsWith("staff_clusterbombstaff_projectile", StringComparison.Ordinal) ||
                parent.m_weapon?.m_dropPrefab?.name != "StaffClusterbomb" || parent.m_originalHitData == null ||
                parent.m_spawnCount < 1 || parent.m_spawnCount > 24 || parent.m_nview?.IsValid() != true ||
                !parent.m_nview.IsOwner()) return default;
            bool marked = parent.m_hitVariant >= 7031 && parent.m_hitVariant <= 7036;
            if (!marked && (!(parent.m_owner is Player player) || !MagicSkillPassives.OwnerReady(player) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 35))) return default;
            return new Frame { Parent = parent, Count = parent.m_spawnCount, Active = true };
        }
        internal static Vector3 Direction(int index, int count)
        {
            // Equal-area latitude bands and golden-angle azimuth cover the full
            // sphere without the native 50% bias toward the incoming projectile.
            float y = 1f - 2f * (index + .5f) / count;
            float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = index * 2.39996323f;
            return new Vector3(radial * Mathf.Cos(angle), y, radial * Mathf.Sin(angle));
        }
        internal static void Apply(Projectile child, Character owner, ref Vector3 velocity, HitData hit, ItemDrop.ItemData item)
        {
            if (!Current.Active || child == null ||
                !child.name.StartsWith("staff_clusterbombstaff_splinter_projectile", StringComparison.Ordinal) ||
                item?.m_dropPrefab?.name != "StaffClusterbomb" || owner != Current.Parent.m_owner ||
                !ReferenceEquals(hit, Current.Parent.m_originalHitData) || Current.Index >= Current.Count) return;
            float speed = velocity.magnitude;
            if (!float.IsFinite(speed) || speed <= 0f) return;
            velocity = Direction(Current.Index++, Current.Count) * speed;
        }
    }
    [HarmonyPatch(typeof(Projectile), "SpawnOnHit")]
    internal static class Ashlands35ClusterSpreadPatch
    {
        private static void Prefix(Projectile __instance, out Ashlands35ClusterSpread.Frame __state)
        {
            __state = Ashlands35ClusterSpread.Current;
            // Every nested native spawn gets its own scope, including a no-op
            // foreign spawn, so its children cannot consume an outer slot.
            Ashlands35ClusterSpread.Current = Ashlands35ClusterSpread.Begin(__instance);
        }
        private static Exception Finalizer(Ashlands35ClusterSpread.Frame __state, Exception __exception)
        { Ashlands35ClusterSpread.Current = __state; return __exception; }
    }
}
