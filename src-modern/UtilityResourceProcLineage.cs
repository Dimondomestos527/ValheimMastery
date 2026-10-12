using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // XP transport/API failure must never prevent or replay the native resource hit.
    internal static class UtilityResourceProcApi
    {
        internal static bool TryGetCurrent(Component target, HitData hit, int index, out ResourceProcXpTicket ticket)
        {
            ticket = null;
            try { return FullResourceProcExperience.TryGetCurrent(target, hit, index, out ticket); }
            catch (Exception) { ticket = null; return false; }
        }
        internal static ResourceProcXpResult BindChild(ResourceProcXpTicket parent, Component child, string prefab,
            out ResourceProcXpTicket ticket)
        {
            ticket = null;
            try { return FullResourceProcExperience.BindChild(parent, child, prefab, out ticket); }
            catch (Exception) { ticket = null; return ResourceProcXpResult.NoOp; }
        }
        internal static IDisposable EnterContinuation(ResourceProcXpTicket ticket, Component target, Player actor, HitData hit)
        {
            try { return FullResourceProcExperience.EnterContinuation(ticket, target, actor, hit); }
            catch (Exception) { return null; }
        }
        internal static ResourceProcXpResult ConfirmDestroyed(ResourceProcXpTicket ticket, ZDOID uid, int index,
            string prefab, int tier)
        {
            try { return FullResourceProcExperience.ConfirmDestroyed(ticket, uid, index, prefab, tier); }
            catch (Exception) { return ResourceProcXpResult.NoOp; }
        }
        internal static ResourceProcXpResult CloseEpisode(ResourceProcXpTicket ticket, bool complete)
        {
            try { return FullResourceProcExperience.CloseEpisode(ticket, complete); }
            catch (Exception) { return ResourceProcXpResult.NoOp; }
        }
    }

    internal sealed class UtilityWoodNode
    {
        internal Component Target;
        internal UtilityWoodGraph Graph;
        internal UtilityWoodNode Parent;
        internal ResourceProcXpTicket Ticket;
        internal ZNetView View;
        internal ZDO Zdo;
        internal ZDOID Uid;
        internal string Prefab;
        internal int Tier;
        internal float Before;
        internal bool Destroyed, SpawnClosed, ReceiptProven;
    }

    internal sealed class UtilityWoodGraph
    {
        internal Player Actor;
        internal short ToolTier;
        internal Vector3 Point;
        internal float ExpiresAt;
        internal ResourceProcXpTicket Ticket;
        internal bool Closed, Invalid;
        internal readonly List<UtilityWoodNode> Nodes = new List<UtilityWoodNode>();
    }

    internal sealed class UtilityWoodRpcScope
    {
        [ThreadStatic] internal static UtilityWoodRpcScope Current;
        internal UtilityWoodRpcScope Previous;
        internal Component Target;
        internal HitData Hit;
        internal UtilityWoodNode Node;
    }

    internal sealed class UtilityWoodSpawnScope
    {
        [ThreadStatic] internal static UtilityWoodSpawnScope Current;
        internal UtilityWoodSpawnScope Previous;
        internal UtilityWoodNode Parent;
        internal readonly Dictionary<string, int> Expected = new Dictionary<string, int>(StringComparer.Ordinal);
        internal int Remaining;
    }

    internal static class UtilityWoodLineage
    {
        private static readonly ConditionalWeakTable<Component, UtilityWoodNode> Nodes =
            new ConditionalWeakTable<Component, UtilityWoodNode>();
        private static readonly List<UtilityWoodGraph> Active = new List<UtilityWoodGraph>();
        [ThreadStatic] private static UtilityWoodNode Generated;

        internal static bool Positive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Dead(float value) => value <= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        internal static string PrefabKey(string name) => (name ?? string.Empty).Replace("(Clone)", string.Empty).Trim();

        private static void Snapshot(UtilityWoodNode node, float fallback)
        {
            node.View = node.Target.GetComponent<ZNetView>();
            node.Zdo = node.View?.GetZDO();
            if (node.Zdo == null || node.View.IsOwner() != true) { node.Before = 0f; return; }
            node.Uid = node.Zdo.m_uid;
            node.Before = node.Zdo.GetFloat(ZDOVars.s_health, fallback);
            node.Prefab = PrefabKey(node.Target.gameObject.name);
            node.Tier = GatheringProgressionService.GetResourceTier(node.Target, Skills.SkillType.WoodCutting);
        }

        internal static void RegisterRoot(TreeBase tree, Player actor, short toolTier)
        {
            var rpc = UtilityWoodRpcScope.Current;
            if (tree == null || actor == null || rpc == null || !ReferenceEquals(rpc.Target, tree) ||
                !ReferenceEquals(rpc.Hit?.GetAttacker(), actor) || rpc.Node != null) return;
            UtilityResourceProcApi.TryGetCurrent(tree, rpc.Hit, -1, out var ticket);
            var graph = new UtilityWoodGraph { Actor = actor, ToolTier = toolTier, Ticket = ticket,
                Point = tree.transform.position, ExpiresAt = Time.time + 12f };
            // Tracking limits never alter native damage or the work queue.
            if (Active.Count >= 64) { graph.Invalid = true; graph.Ticket = null; }
            else Active.Add(graph);
            var node = new UtilityWoodNode { Target = tree, Graph = graph, Ticket = graph.Ticket };
            Snapshot(node, tree.m_health);
            graph.Nodes.Add(node);
            rpc.Node = node;
        }

        internal static UtilityWoodNode ObserveSpawn(Component child)
        {
            var spawn = UtilityWoodSpawnScope.Current;
            if (child == null || spawn?.Parent == null || Nodes.TryGetValue(child, out _)) return null;
            // A prefab containing both components represents one native log, not two pieces.
            if (child is Destructible && child.GetComponent<TreeLog>() != null) return null;
            string key = PrefabKey(child.gameObject.name);
            if (!spawn.Expected.TryGetValue(key, out int left) || left <= 0)
            { spawn.Parent.Graph.Invalid = true; return null; }
            spawn.Expected[key] = left - 1;
            spawn.Remaining--;
            var parent = spawn.Parent;
            var node = new UtilityWoodNode { Target = child, Parent = parent, Graph = parent.Graph, Prefab = key };
            Nodes.Add(child, node);
            if (parent.Graph.Nodes.Count >= 4096) parent.Graph.Invalid = true;
            else parent.Graph.Nodes.Add(node);
            Bind(node);
            return node;
        }

        internal static void Bind(UtilityWoodNode node)
        {
            if (node == null || node.Ticket != null || node.Parent?.Ticket == null || node.Graph.Closed) return;
            var result = UtilityResourceProcApi.BindChild(node.Parent.Ticket, node.Target, node.Prefab, out var ticket);
            if (result == ResourceProcXpResult.Accepted || result == ResourceProcXpResult.Duplicate)
                node.Ticket = ticket;
            else if (result == ResourceProcXpResult.NoOp) node.Graph.Invalid = true;
            // Pending retains this exact parent/child. Retry at its existing Tick only.
        }

        internal static UtilityWoodRpcScope BeginRpc(Component target, HitData hit)
        {
            var scope = new UtilityWoodRpcScope { Previous = UtilityWoodRpcScope.Current, Target = target, Hit = hit };
            if (Generated != null && ReferenceEquals(Generated.Target, target) &&
                ReferenceEquals(hit?.GetAttacker(), Generated.Graph.Actor))
            {
                scope.Node = Generated;
                float fallback = target is TreeLog log ? log.m_health : target is Destructible d ? d.m_health : 0f;
                Snapshot(scope.Node, fallback);
                if (target is Destructible stump && TreePrefab(stump.m_spawnWhenDamaged))
                    scope.Node.Graph.Invalid = true; // Unsupported birth phase cannot claim complete lineage.
            }
            UtilityWoodRpcScope.Current = scope;
            return scope;
        }

        internal static void EndRpc(UtilityWoodRpcScope scope, Exception error)
        {
            if (scope == null) return;
            UtilityWoodRpcScope.Current = scope.Previous;
            if (scope.Node == null) return;
            if (error != null) { Fail(scope.Node.Graph); return; }
            // Native ZNetScene.Destroy synchronously ResetZDO. Health alone is insufficient.
            if (Positive(scope.Node.Before) && scope.Node.Zdo != null &&
                Dead(scope.Node.Zdo.GetFloat(ZDOVars.s_health, scope.Node.Before)) &&
                !ReferenceEquals(scope.Node.View, null) && scope.Node.View.GetZDO() == null)
                Destroyed(scope.Node);
        }

        internal static UtilityWoodSpawnScope BeginSpawn(Component parent, params GameObject[] prefabs)
        {
            var rpc = UtilityWoodRpcScope.Current;
            var scope = new UtilityWoodSpawnScope { Previous = UtilityWoodSpawnScope.Current };
            if (rpc?.Node != null && ReferenceEquals(rpc.Target, parent)) scope.Parent = rpc.Node;
            if (scope.Parent != null)
                foreach (var prefab in prefabs)
                {
                    if (prefab == null) continue;
                    string key = PrefabKey(prefab.name);
                    scope.Expected.TryGetValue(key, out int count);
                    scope.Expected[key] = count + 1;
                    scope.Remaining++;
                }
            // Install an empty scope too: unrelated nested spawn cannot inherit its parent.
            UtilityWoodSpawnScope.Current = scope;
            return scope;
        }

        internal static void EndSpawn(UtilityWoodSpawnScope scope, Exception error)
        {
            if (scope == null) return;
            UtilityWoodSpawnScope.Current = scope.Previous;
            if (scope.Parent == null) return;
            scope.Parent.SpawnClosed = error == null && scope.Remaining == 0;
            if (!scope.Parent.SpawnClosed) scope.Parent.Graph.Invalid = true;
        }

        internal static void Destroyed(UtilityWoodNode node)
        {
            if (node.Destroyed || !Positive(node.Before)) return;
            node.Destroyed = true;
            if (node.Ticket != null)
            {
                var result = UtilityResourceProcApi.ConfirmDestroyed(node.Ticket, node.Uid, -1, node.Prefab, node.Tier);
                node.ReceiptProven = result != ResourceProcXpResult.NoOp;
            }
            Complete(node.Graph);
        }

        private static void Complete(UtilityWoodGraph graph)
        {
            if (graph.Closed) return;
            foreach (var node in graph.Nodes)
                if (!node.Destroyed || !node.SpawnClosed) return;
            bool proven = !graph.Invalid && graph.Ticket != null;
            foreach (var node in graph.Nodes) proven &= node.ReceiptProven;
            graph.Closed = true;
            if (graph.Ticket == null) return;
            var result = UtilityResourceProcApi.CloseEpisode(graph.Ticket, proven);
            if (proven && (result == ResourceProcXpResult.Accepted || result == ResourceProcXpResult.Pending))
                PerkVisualService.PlayProc(graph.Actor, "woodcutting_35_complete", graph.Point, false, true);
        }

        internal static void Fail(UtilityWoodGraph graph)
        {
            if (graph == null || graph.Closed) return;
            graph.Closed = true;
            if (graph.Ticket != null) UtilityResourceProcApi.CloseEpisode(graph.Ticket, false);
        }

        internal static void Tick()
        {
            for (int i = Active.Count - 1; i >= 0; --i)
            {
                if (!Active[i].Closed && Time.time > Active[i].ExpiresAt) Fail(Active[i]);
                if (Active[i].Closed) Active.RemoveAt(i);
            }
        }

        internal static IDisposable EnterGenerated(UtilityWoodNode node, Component target, Player actor, HitData hit)
        {
            var previous = Generated;
            // This scope supplies provenance even if XP proof is unavailable; damage is unchanged.
            Generated = node != null && ReferenceEquals(node.Target, target) &&
                ReferenceEquals(node.Graph.Actor, actor) ? node : null;
            IDisposable xp = node?.Ticket != null
                ? UtilityResourceProcApi.EnterContinuation(node.Ticket, target, actor, hit) : null;
            if (node?.Ticket != null && xp == null) node.Graph.Invalid = true;
            return new RestoreGenerated(previous, xp);
        }

        internal static bool TreePrefab(GameObject prefab) => prefab != null &&
            (prefab.GetComponent<TreeLog>() != null ||
             (prefab.GetComponent<Destructible>() is Destructible d &&
              (d.m_destructibleType & DestructibleType.Tree) != 0));

        private sealed class RestoreGenerated : IDisposable
        {
            private readonly UtilityWoodNode Previous;
            private readonly IDisposable Xp;
            internal RestoreGenerated(UtilityWoodNode previous, IDisposable xp) { Previous = previous; Xp = xp; }
            public void Dispose()
            { try { Xp?.Dispose(); } catch (Exception) { } finally { Generated = Previous; } }
        }
    }

    [HarmonyPatch]
    internal static class UtilityWoodDamageProofPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TreeBase), "RPC_Damage");
            yield return AccessTools.Method(typeof(TreeLog), "RPC_Damage");
            yield return AccessTools.Method(typeof(Destructible), "RPC_Damage");
        }
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Component __instance, HitData __1, out UtilityWoodRpcScope __state) =>
            __state = UtilityWoodLineage.BeginRpc(__instance, __1);
        private static Exception Finalizer(Exception __exception, UtilityWoodRpcScope __state)
        {
            UtilityWoodLineage.EndRpc(__state, __exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(TreeBase), "SpawnLog")]
    internal static class UtilityWoodRootSpawnPatch
    {
        private static void Prefix(TreeBase __instance, out UtilityWoodSpawnScope __state) =>
            __state = UtilityWoodLineage.BeginSpawn(__instance, __instance.m_logPrefab, __instance.m_stubPrefab);
        private static Exception Finalizer(Exception __exception, UtilityWoodSpawnScope __state)
        {
            UtilityWoodLineage.EndSpawn(__state, __exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(TreeLog), "Destroy")]
    internal static class UtilityWoodSubLogSpawnPatch
    {
        private static void Prefix(TreeLog __instance, out UtilityWoodSpawnScope __state)
        {
            int count = __instance.m_subLogPrefab != null ? (__instance.m_subLogPoints?.Length ?? 0) : 0;
            var prefabs = new GameObject[count];
            for (int i = 0; i < count; ++i) prefabs[i] = __instance.m_subLogPrefab;
            __state = UtilityWoodLineage.BeginSpawn(__instance, prefabs);
        }
        private static Exception Finalizer(Exception __exception, UtilityWoodSpawnScope __state)
        {
            UtilityWoodLineage.EndSpawn(__state, __exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Destructible), "Destroy")]
    internal static class UtilityWoodStumpSpawnPatch
    {
        private static void Prefix(Destructible __instance, out UtilityWoodSpawnScope __state) =>
            __state = UtilityWoodLineage.BeginSpawn(__instance,
                UtilityWoodLineage.TreePrefab(__instance.m_spawnWhenDestroyed) ? __instance.m_spawnWhenDestroyed : null);
        private static Exception Finalizer(Exception __exception, UtilityWoodSpawnScope __state)
        {
            UtilityWoodLineage.EndSpawn(__state, __exception);
            return __exception;
        }
    }
}

