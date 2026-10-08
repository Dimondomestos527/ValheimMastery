using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class IceStaff35Trail
    {
        private const string EffectName = "VM_FimbulTrail";
        internal static readonly int EffectHash = EffectName.GetStableHashCode();
        internal static readonly int NativeFrostHash = "Frost".GetStableHashCode();
        private const int MaxZones = 8;
        private sealed class Zone
        {
            internal Vector3 Point;
            internal float Until;
        }
        private static readonly List<Zone> Zones = new List<Zone>(MaxZones);
        private static readonly Collider[] Hits = new Collider[64];
        private static readonly HashSet<Character> Seen = new HashSet<Character>();
        private static Player Owner;
        private static IceTrailVisual Visual;
        private static float NextTick;
        private static int SceneEpoch;

        internal static bool IsIceStaff(ItemDrop.ItemData item) => item?.m_dropPrefab != null &&
            item.m_dropPrefab.name == "StaffIceShards";

        internal static void RegisterEffect(ObjectDB db)
        {
            if (db == null || db.GetStatusEffect(EffectHash) != null) return;
            SE_Frost vanilla = db.GetStatusEffect(NativeFrostHash) as SE_Frost;
            if (vanilla == null) return;
            // Preserve vanilla frost-resistance rules and presentation. This is a separate
            // effect so refreshing a ground patch cannot erase a stronger projectile freeze.
            SE_Frost trail = Object.Instantiate(vanilla);
            trail.name = EffectName;
            // Instantiate copies the cached native hash as well as the fields.
            trail.m_nameHash = EffectHash;
            trail.m_name = "Слід Фімбулвінтеру";
            trail.m_ttl = 1.5f;
            trail.m_minSpeedFactor = .65f;
            db.m_StatusEffects.Add(trail);
        }

        internal static void Add(Player player, Vector3 point)
        {
            if (!MagicSkillPassives.OwnerReady(player) || player.IsTeleporting() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 35)) return;
            ResetFor(player);
            // Use actual nearby solid surface, not the enemy's body or empty air.
            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            if (!Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 8f, mask) ||
                ground.normal.y < .5f) return;
            point = ground.point + Vector3.up * .06f;
            float duration = 12f + .08f * Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.ElementalMagic), 0f, 100f);
            foreach (Zone zone in Zones)
                if ((zone.Point - point).sqrMagnitude <= 2.25f)
                { zone.Until = Time.time + duration; return; }
            if (Zones.Count >= MaxZones) Zones.RemoveAt(0);
            Zones.Add(new Zone { Point = point, Until = Time.time + duration });
            if (Visual == null) Visual = IceTrailVisual.Create();
        }

        private static void ResetFor(Player player)
        {
            int epoch = ZNetScene.instance != null ? ZNetScene.instance.GetInstanceID() : 0;
            if (Owner == player && SceneEpoch == epoch) return;
            Zones.Clear();
            if (Visual != null) Object.Destroy(Visual.gameObject);
            Visual = null;
            Owner = player;
            SceneEpoch = epoch;
            NextTick = 0f;
        }

        internal static void Update(Player player)
        {
            if (player != Player.m_localPlayer) return;
            ResetFor(player);
            if (!MagicSkillPassives.OwnerReady(player) || player.IsTeleporting())
            {
                Zones.Clear();
                if (Visual != null) Object.Destroy(Visual.gameObject);
                Visual = null;
                return;
            }
            if (Time.time < NextTick) return;
            NextTick = Time.time + .5f;
            if (Zones.Count == 0) return;
            RegisterEffect(ObjectDB.instance);
            // Ignore the zones until all local status definitions are ready.
            if (ObjectDB.instance?.GetStatusEffect(EffectHash) == null) return;
            Seen.Clear();
            for (int z = Zones.Count - 1; z >= 0; z--)
            {
                Zone zone = Zones[z];
                if (Time.time >= zone.Until) { Zones.RemoveAt(z); continue; }
                int count = Physics.OverlapSphereNonAlloc(zone.Point, 2f, Hits, LayerMask.GetMask("character", "character_net"));
                for (int i = 0; i < count; i++)
                {
                    Character enemy = Hits[i]?.GetComponentInParent<Character>();
                    Hits[i] = null;
                    if (enemy == null || enemy.IsDead() || enemy.IsPlayer() || !BaseAI.IsEnemy(player, enemy) ||
                        Mathf.Abs(enemy.transform.position.y - zone.Point.y) > 2f || !Seen.Add(enemy)) continue;
                    // One damage tick per enemy across all overlapping zones;
                    // native resistance/armor and owner routing remain authoritative.
                    enemy.GetSEMan().AddStatusEffect(EffectHash, true);
                    HitData tick = new HitData { m_point = enemy.GetCenterPoint(), m_dir = Vector3.zero,
                        m_blockable = false, m_dodgeable = false, m_staggerMultiplier = 0f,
                        m_skillRaiseAmount = 0f, m_variant = 1270 };
                    tick.m_damage.m_frost = .5f * (2f + .04f * Mathf.Clamp(
                        PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.ElementalMagic), 0f, 100f));
                    tick.SetAttacker(player);
                    enemy.Damage(tick);
                }
            }
            Seen.Clear();
            if (Zones.Count == 0 && Visual != null)
            { Object.Destroy(Visual.gameObject); Visual = null; }
        }

        internal static int FillZonePoints(Vector3[] destination)
        {
            if (destination == null) return 0;
            int count = 0;
            foreach (Zone zone in Zones)
                if (zone.Until > Time.time && count < destination.Length)
                    destination[count++] = zone.Point;
            return count;
        }

        internal static void MarkDirectImpact(Player player, Collider collider)
        {
            if (!MagicSkillPassives.OwnerReady(player) || collider == null ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 35)) return;
            Character enemy = collider.GetComponentInParent<Character>();
            if (enemy == null || enemy.IsDead() || enemy.IsPlayer() || !BaseAI.IsEnemy(player, enemy)) return;
            RegisterEffect(ObjectDB.instance);
            if (ObjectDB.instance?.GetStatusEffect(EffectHash) == null) return;
            // Variant 1 is the direct-hit mark: it lasts beyond the ground patch so
            // a flying enemy stays low through the freeze. Owner applies the status.
            enemy.GetSEMan().AddStatusEffect(EffectHash, true, 0, 0f, 1);
        }
    }

    [HarmonyPatch(typeof(SEMan), "Internal_AddStatusEffect")]
    internal static class IceTrailDirectHitDurationPatch
    {
        private static void Postfix(SEMan __instance, int nameHash, short variant)
        {
            if (nameHash != IceStaff35Trail.EffectHash || variant != 1) return;
            if (__instance.GetStatusEffect(nameHash) is SE_Frost mark)
                mark.m_ttl = 6f;
        }
    }

    [HarmonyPatch(typeof(SE_Frost), nameof(SE_Frost.ModifySpeed))]
    internal static class IceTrailNoDoubleSlowPatch
    {
        private static bool Prefix(SE_Frost __instance, Character character) =>
            __instance.NameHash() != IceStaff35Trail.EffectHash ||
            character?.GetSEMan()?.HaveStatusEffect(IceStaff35Trail.NativeFrostHash) != true;
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class IceTrailDatabasePatch
    {
        private static void Postfix(ObjectDB __instance) => IceStaff35Trail.RegisterEffect(__instance);
    }

    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class IceTrailDatabaseCopyPatch
    {
        private static void Postfix(ObjectDB __instance) => IceStaff35Trail.RegisterEffect(__instance);
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class IceTrailImpactPatch
    {
        // Capture before native destruction/hiding, but only commit after the hit ran.
        private static void Prefix(Projectile __instance, bool water, out Player __state)
        {
            __state = !water && __instance != null && IceStaff35Trail.IsIceStaff(__instance.m_weapon)
                ? __instance.m_owner as Player : null;
        }
        private static void Postfix(Collider collider, Vector3 hitPoint, Player __state)
        {
            if (__state == null) return;
            IceStaff35Trail.Add(__state, hitPoint);
            IceStaff35Trail.MarkDirectImpact(__state, collider);
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class IceTrailUpdatePatch
    {
        private static void Postfix(Player __instance) => IceStaff35Trail.Update(__instance);
    }
}
