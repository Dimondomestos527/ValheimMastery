using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // This is the capability layer only. It never claims or consumes chest resources.
    internal static class WorkshopNetwork
    {
        private sealed class Node
        {
            internal CraftingStation Station;
            internal Vector3 Position;
            internal float Radius;
            internal int Component;
        }

        private static readonly List<Node> Nodes = new List<Node>();
        private static readonly Dictionary<CraftingStation, float> KnownRadii = new Dictionary<CraftingStation, float>();
        private static bool _dirty = true;
        internal static uint Revision { get; private set; }

        internal static void MarkDirty() { WorkshopPerformanceProbe.Query(2); _dirty = true; }

        internal static void NoticeRadius(CraftingStation station, float radius)
        {
            if (station == null) return;
            if (!KnownRadii.TryGetValue(station, out float old) || Mathf.Abs(old - radius) > 0.01f)
            {
                KnownRadii[station] = radius;
                MarkDirty();
            }
        }

        internal static void Forget(CraftingStation station)
        {
            if (station != null) KnownRadii.Remove(station);
            MarkDirty();
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private static void RebuildIfDirty()
        {
            WorkshopPerformanceProbe.Query(1);
            if (!_dirty) return;
            var perf = WorkshopPerformanceProbe.Begin(1);
            bool perfCompleted = false;
            try
            {
            _dirty = false;
            Revision++;
            Nodes.Clear();
            if (CraftingStation.m_allStations == null) return;
            foreach (CraftingStation station in CraftingStation.m_allStations)
            {
                if (station == null || station.m_rangeBuild <= 0f) continue;
                float radius = WorkshopStationRadius.EffectiveRadius(station.m_rangeBuild, station.GetLevel(false));
                if (radius <= 0f) continue;
                Nodes.Add(new Node { Station = station, Position = station.transform.position, Radius = radius, Component = -1 });
            }
            int nextComponent = 0;
            Queue<int> queue = new Queue<int>();
            for (int start = 0; start < Nodes.Count; start++)
            {
                if (Nodes[start].Component >= 0) continue;
                Nodes[start].Component = nextComponent;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Node current = Nodes[queue.Dequeue()];
                    for (int candidate = 0; candidate < Nodes.Count; candidate++)
                    {
                        Node other = Nodes[candidate];
                        if (other.Component >= 0 ||
                            HorizontalDistance(current.Position, other.Position) > current.Radius + other.Radius) continue;
                        other.Component = nextComponent;
                        queue.Enqueue(candidate);
                    }
                }
                nextComponent++;
            }
            perfCompleted = true;
            }
            finally
            {
                WorkshopPerformanceProbe.End(perf, perfCompleted, 0, Nodes.Count, Nodes.Count, 0,
                    (long)Nodes.Count * Nodes.Count, Nodes.Count);
            }
        }

        internal static CraftingStation FindCapability(string stationName, Vector3 point, int requiredLevel = 1)
        {
            if (string.IsNullOrEmpty(stationName)) return null;
            RebuildIfDirty();
            // The point belongs to the union only if it is covered by an actual node.
            HashSet<int> coveringComponents = new HashSet<int>();
            foreach (Node node in Nodes)
                if (HorizontalDistance(node.Position, point) < node.Radius)
                    coveringComponents.Add(node.Component);
            if (coveringComponents.Count == 0) return null;
            CraftingStation best = null;
            float bestDistance = float.MaxValue;
            foreach (Node node in Nodes)
            {
                if (!coveringComponents.Contains(node.Component) ||
                    !string.Equals(node.Station.m_name, stationName, StringComparison.Ordinal) ||
                    node.Station.GetLevel(false) < requiredLevel) continue;
                float distance = HorizontalDistance(node.Position, point);
                if (distance < bestDistance)
                {
                    best = node.Station;
                    bestDistance = distance;
                }
            }
            return best;
        }

        internal static int CoveredComponent(Vector3 point)
        {
            RebuildIfDirty();
            foreach (Node node in Nodes)
                if (HorizontalDistance(node.Position, point) < node.Radius)
                    return node.Component;
            return -1;
        }

        internal static string DebugSummary(Vector3 point)
        {
            RebuildIfDirty();
            HashSet<int> covered = new HashSet<int>();
            foreach (Node node in Nodes)
                if (HorizontalDistance(node.Position, point) < node.Radius) covered.Add(node.Component);
            List<string> details = new List<string>();
            foreach (Node node in Nodes)
            {
                if (!covered.Contains(node.Component)) continue;
                details.Add(node.Station.m_name + " L" + node.Station.GetLevel(false) +
                    " r=" + node.Radius.ToString("0.0") + "m d=" +
                    HorizontalDistance(node.Position, point).ToString("0.0") + "m");
            }
            return "Workshop: stations=" + Nodes.Count + ", covering components=" + covered.Count +
                ", connected stations at player=" + details.Count + ". " + string.Join("; ", details);
        }
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Start))]
    internal static class WorkshopStationStartPatch
    {
        private static void Postfix() => WorkshopNetwork.MarkDirty();
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.OnDestroy))]
    internal static class WorkshopStationDestroyPatch
    {
        private static void Postfix(CraftingStation __instance) => WorkshopNetwork.Forget(__instance);
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.HaveBuildStationInRange))]
    internal static class WorkshopBuildCapabilityPatch
    {
        private static void Postfix(string name, Vector3 point, ref CraftingStation __result)
        {
            if (__result != null || !PerkRuntimeService.HasPerk(Player.m_localPlayer, Skills.SkillType.Crafting, 35)) return;
            __result = WorkshopNetwork.FindCapability(name, point);
        }
    }

}
