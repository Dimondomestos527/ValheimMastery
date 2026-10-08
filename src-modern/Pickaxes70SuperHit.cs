using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class Pickaxes70Strike
    {
        internal float IncomingPickaxe;
        internal bool Rolled;
    }

    internal sealed class Pickaxes70HitState
    {
        internal Player Player;
        internal Vector3 Point;
        internal MineRock5.HitArea Area;
        internal float HealthBefore;
        internal Pickaxes70Strike Strike;
    }

    // Same native RPC only: a later unrelated hit never inherits proc attribution.
    internal sealed class Pickaxes70DamageScope
    {
        [ThreadStatic] internal static Pickaxes70DamageScope Current;
        internal Pickaxes70DamageScope Previous;
        internal MineRock5 Rock;
        internal MineRock5.HitArea[] Areas;
        internal Player Player;
        internal Vector3 Point;
    }

    [HarmonyPatch(typeof(MineRock5), "RPC_Damage")]
    internal static class Pickaxes70DamageScopePatch
    {
        private static void Prefix(MineRock5 __instance, HitData hit, out Pickaxes70DamageScope __state)
        {
            __state = null;
            if (__instance?.m_nview?.IsOwner() != true || !(hit?.GetAttacker() is Player player) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Pickaxes, 70)) return;
            __state = new Pickaxes70DamageScope { Previous = Pickaxes70DamageScope.Current, Rock = __instance };
            Pickaxes70DamageScope.Current = __state;
        }

        private static Exception Finalizer(Exception __exception, Pickaxes70DamageScope __state)
        {
            if (__state == null) return __exception;
            // Restore before feedback as feedback may itself call into other services.
            Pickaxes70DamageScope.Current = __state.Previous;
            if (__exception == null && __state.Areas != null)
                Pickaxes70SuperHitPatch.ConfirmCollapse(__state.Rock, __state.Areas, __state.Player, __state.Point);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.DamageArea))]
    internal static class Pickaxes70SuperHitPatch
    {
        private const float AreaRadius = 3f;
        [ThreadStatic] private static int _generatedDepth;
        private sealed class CollapseReceipt { }
        private static readonly ConditionalWeakTable<HitData, Pickaxes70Strike> Observed = new ConditionalWeakTable<HitData, Pickaxes70Strike>();
        private static readonly ConditionalWeakTable<MineRock5, CollapseReceipt> CollapseSent = new ConditionalWeakTable<MineRock5, CollapseReceipt>();

        private static void Prefix(MineRock5 __instance, int hitAreaIndex, HitData hit, out Pickaxes70HitState __state)
        {
            __state = null;
            Player player = hit?.GetAttacker() as Player;
            if (_generatedDepth > 0 || __instance?.m_nview?.IsOwner() != true ||
                player == null || hit.m_damage.m_pickaxe <= 0f ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Pickaxes, 70)) return;
            __instance.LoadHealth();
            MineRock5.HitArea area = __instance.GetHitArea(hitAreaIndex);
            if (area == null || area.m_health <= 0f) return;
            if (!Observed.TryGetValue(hit, out var strike))
            {
                // DamageArea mutates the hit through resistance: preserve raw damage once.
                strike = new Pickaxes70Strike { IncomingPickaxe = hit.m_damage.m_pickaxe };
                Observed.Add(hit, strike);
            }
            if (strike.Rolled) return;
            __state = new Pickaxes70HitState { Player = player, Point = hit.m_point, Area = area,
                HealthBefore = area.m_health, Strike = strike };
        }

        private static void Postfix(MineRock5 __instance, HitData hit, Pickaxes70HitState __state)
        {
            if (__state?.Area == null || __state.Area.m_health >= __state.HealthBefore || __state.Strike.Rolled) return;
            __state.Strike.Rolled = true;
            if (!PerkRuntimeService.RollChance(.20f)) return;
            bool collapse = PerkRuntimeService.RollChance(.05f);
            MineRock5.HitArea[] areas = __instance.m_hitAreas.ToArray();
            List<int> neighbours = new List<int>();
            for (int i = 0; i < areas.Length; i++)
            {
                var area = areas[i];
                if (area == null || ReferenceEquals(area, __state.Area) || area.m_health <= 0f || area.m_collider == null) continue;
                // Non-convex ore meshes require Bounds.ClosestPoint, not Collider.ClosestPoint.
                if ((area.m_collider.bounds.ClosestPoint(__state.Point) - __state.Point).sqrMagnitude <= AreaRadius * AreaRadius)
                    neighbours.Add(i);
            }
            neighbours.Sort((a, b) => (areas[a].m_collider.bounds.ClosestPoint(__state.Point) - __state.Point).sqrMagnitude.CompareTo(
                (areas[b].m_collider.bounds.ClosestPoint(__state.Point) - __state.Point).sqrMagnitude));
            int extra = Mathf.Min(UnityEngine.Random.Range(1, 3), neighbours.Count);
            float bonusDamage = __state.Strike.IncomingPickaxe * .5f;
            if (bonusDamage <= 0f) return;
            bool damaged = false, extraKilled = false;
            try
            {
                _generatedDepth++;
                for (int n = 0; n < extra; n++)
                {
                    var area = areas[neighbours[n]];
                    float before = area.m_health;
                    DamageNeighbour(__instance, areas, neighbours[n], hit, bonusDamage);
                    damaged |= area.m_health < before;
                    extraKilled |= before > 0f && area.m_health <= 0f;
                }
                if (collapse)
                {
                    HitData probe = hit.Clone();
                    probe.m_damage = new HitData.DamageTypes { m_pickaxe = 1f };
                    probe.ApplyResistance(__instance.m_damageModifiers, out HitData.DamageModifier ignored);
                    float effective = probe.GetTotalDamage();
                    if (effective > 0f)
                        for (int i = 0; i < areas.Length; i++)
                        {
                            float before = areas[i]?.m_health ?? 0f;
                            BreakArea(__instance, areas, i, hit, effective);
                            damaged |= areas[i] != null && areas[i].m_health < before;
                        }
                }
                // Native RPC already checks support when the ORIGINAL area broke.
                // Do not damage unsupported high-HP segments twice in that case.
                if (__state.Area.m_health > 0f && extraKilled && !AllBroken(areas) &&
                    __instance != null && __instance.m_nview != null && __instance.m_nview.IsValid() &&
                    __instance.m_nview.IsOwner() && __instance.m_supportCheck)
                    __instance.CheckSupport();
            }
            finally { _generatedDepth--; }
            if (!damaged) return;
            PerkVisualService.PlayProc(__state.Player, "pickaxes_70", __state.Point, true, true);
            var scope = Pickaxes70DamageScope.Current;
            if (scope != null && ReferenceEquals(scope.Rock, __instance))
            {
                scope.Areas = areas; scope.Player = __state.Player; scope.Point = __state.Point;
            }
            else ConfirmCollapse(__instance, areas, __state.Player, __state.Point);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Pickaxes70] applied: chances=0.20/0.05 neighbourDamage=0.50 forceProc=" + PerkRuntimeService.ForceProc +
                    " neighbours=" + extra + " forcedCollapse=" + collapse + " extraKilled=" + extraKilled + " veinDestroyed=" + AllBroken(areas));
        }

        private static void DamageNeighbour(MineRock5 rock, MineRock5.HitArea[] areas, int index, HitData source, float damage)
        {
            var area = areas[index];
            if (area == null || area.m_health <= 0f || rock == null || rock.m_nview?.IsOwner() != true) return;
            HitData generated = source.Clone();
            generated.m_damage = new HitData.DamageTypes { m_pickaxe = damage };
            generated.m_point = area.m_collider != null ? area.m_collider.bounds.ClosestPoint(source.m_point) : source.m_point;
            generated.m_skillRaiseAmount = 0f;
            rock.DamageArea(index, generated);
        }

        // Guaranteed lethal damage belongs ONLY to the separately rolled full collapse.
        private static void BreakArea(MineRock5 rock, MineRock5.HitArea[] areas, int index, HitData source, float effective)
        {
            var area = areas[index];
            if (area == null || area.m_health <= 0f) return;
            DamageNeighbour(rock, areas, index, source, (area.m_health + .01f) / effective);
        }

        internal static bool AllBroken(MineRock5.HitArea[] areas)
        {
            if (areas == null || areas.Length == 0) return false;
            foreach (var area in areas) if (area != null && area.m_health > 0f) return false;
            return true;
        }

        internal static void ConfirmCollapse(MineRock5 rock, MineRock5.HitArea[] areas, Player player, Vector3 point)
        {
            if (ReferenceEquals(rock, null) || player == null || !AllBroken(areas) || CollapseSent.TryGetValue(rock, out _)) return;
            CollapseSent.Add(rock, new CollapseReceipt());
            PerkVisualService.PlayProc(player, "pickaxes_70_collapse", point, false, true);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Pickaxes70] confirmed collapse: cueOnce=true");
        }
    }
}
