using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Read-only metadata. Never clone a prefab, load a world scene or edit static lists.
    internal static class WorkshopWorldRecords
    {
        internal sealed class Entry
        {
            internal ZDO Data;
            internal GameObject Definition;
            internal CraftingStation Station;
            internal Container Chest;
            internal StationExtension Extension;
            internal PrivateArea Ward;
            internal int Level, Component;
            internal float Radius;
        }
        internal static readonly List<Entry> Entries = new List<Entry>();
        private static ZNet Session;
        private static readonly Dictionary<int, Entry> Definitions = new Dictionary<int, Entry>();
        private static float Next;
        internal static void Refresh()
        {
            WorkshopPerformanceProbe.Query(0);
            if (ZNet.instance?.IsServer() != true || ZDOMan.instance == null || ZNetScene.instance == null) return;
            if (Session != ZNet.instance) { Entries.Clear(); Definitions.Clear(); Next = 0f; Session = ZNet.instance; }
            if (Time.time < Next) return;
            Next = Time.time + 1f; Entries.Clear();
            var perf = WorkshopPerformanceProbe.Begin(0);
            int perfDefinitions = Definitions.Count, perfRecords = ZDOMan.instance.m_objectsByID.Count, perfLevelStations = 0;
            var stations = new List<Entry>();
            bool perfCompleted = false;
            try
            {
            foreach (ZDO record in ZDOMan.instance.m_objectsByID.Values)
            {
                int prefab = record.GetPrefab();
                if (!Definitions.TryGetValue(prefab, out Entry descriptor))
                {
                    GameObject definition = ZNetScene.instance.GetPrefab(prefab);
                    if (definition == null) continue;
                    descriptor = new Entry { Definition = definition, Station = definition.GetComponent<CraftingStation>(),
                        Chest = definition.name.StartsWith("piece_chest", StringComparison.OrdinalIgnoreCase) ? definition.GetComponent<Container>() : null,
                        Extension = definition.GetComponent<StationExtension>(), Ward = definition.GetComponent<PrivateArea>() };
                    Definitions.Add(prefab, descriptor);
                }
                if (descriptor.Station == null && descriptor.Chest == null && descriptor.Extension == null && descriptor.Ward == null) continue;
                Entries.Add(new Entry { Data = record, Definition = descriptor.Definition, Station = descriptor.Station,
                    Chest = descriptor.Chest, Extension = descriptor.Extension, Ward = descriptor.Ward, Component = -1 });
            }
            foreach (Entry entry in Entries)
            {
                if (entry.Station == null) continue;
                if (perf.Active) perfLevelStations++;
                entry.Level = 1; var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (Entry other in Entries)
                {
                    StationExtension ext = other.Extension;
                    if (ext?.m_craftingStation == null || ext.m_craftingStation.m_name != entry.Station.m_name ||
                        Vector3.Distance(entry.Data.GetPosition(), other.Data.GetPosition()) >= ext.m_maxStationDistance) continue;
                    string name = other.Definition.GetComponent<Piece>()?.m_name ?? other.Definition.name;
                    if (ext.m_stack || names.Add(name)) entry.Level++;
                }
                entry.Radius = WorkshopStationRadius.EffectiveRadius(entry.Station.m_rangeBuild, entry.Level);
                if (entry.Radius > 0f) stations.Add(entry);
            }
            int component = 0; var queue = new Queue<Entry>();
            foreach (Entry start in stations)
            {
                if (start.Component >= 0) continue;
                start.Component = component; queue.Enqueue(start);
                while (queue.Count != 0)
                {
                    Entry current = queue.Dequeue();
                    foreach (Entry other in stations)
                        if (other.Component < 0 && Horizontal(current.Data.GetPosition(), other.Data.GetPosition()) <= current.Radius + other.Radius)
                        { other.Component = component; queue.Enqueue(other); }
                }
                component++;
            }
            perfCompleted = true;
            }
            finally
            {
                WorkshopPerformanceProbe.End(perf, perfCompleted, perfRecords, Entries.Count, stations.Count,
                    (long)perfLevelStations * Entries.Count, (long)stations.Count * stations.Count,
                    Entries.Count + Math.Max(0, Definitions.Count - perfDefinitions));
            }
        }
        private static float Horizontal(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
        internal static int Covered(Vector3 point)
        {
            Refresh();
            foreach (Entry entry in Entries)
                if (entry.Station != null && entry.Radius > 0f && Horizontal(point, entry.Data.GetPosition()) < entry.Radius) return entry.Component;
            return -1;
        }
        internal static bool WardAllows(long playerId, Vector3 point)
        {
            Refresh();
            foreach (Entry entry in Entries)
            {
                if (entry.Ward == null || !entry.Data.GetBool(ZDOVars.s_enabled, false) ||
                    Horizontal(point, entry.Data.GetPosition()) >= entry.Ward.m_radius || entry.Data.GetLong(ZDOVars.s_creator, 0) == playerId) continue;
                bool allowed = false; int count = entry.Data.GetInt(ZDOVars.s_permitted, 0);
                if (count < 0 || count > 4096) return false;
                for (int i = 0; i < count; i++) if (entry.Data.GetLong("pu_id" + i, 0) == playerId) { allowed = true; break; }
                if (!allowed) return false;
            }
            return true;
        }
        internal static Entry Find(ZDOID id)
        {
            Refresh();
            return Entries.Find(e => e.Data.m_uid == id && ReferenceEquals(ZDOMan.instance.GetZDO(id), e.Data));
        }
        internal static bool HasCapability(string name, Vector3 point, int level)
        {
            int component = Covered(point);
            if (component < 0) return false;
            return Entries.Exists(e => e.Station != null && e.Component == component && e.Level >= level && e.Station.m_name == name);
        }
        internal static List<Entry> Chests(WorkshopActor actor, Vector3 point, bool serverOwned, string timingId = null)
        {
            var result = new List<Entry>();
            if (actor == null || !actor.HasCrafting70 || actor.IsDead() || actor.IsTeleporting()) return result;
            int component = Covered(point);
            if (timingId != null) WorkshopTiming.Mark(timingId, true, "T1.networkResolved");
            if (component < 0) return result;
            foreach (Entry entry in Entries)
            {
                ZDO data = entry.Data;
                if (entry.Chest == null || !ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid), data) ||
                    data.GetByteArray(ZDOVars.s_items, null) == null ||
                    (serverOwned && data.GetOwner() != ZNet.GetUID()) || data.GetInt(ZDOVars.s_inUse, 0) != 0 ||
                    WorkshopChestLease.BlocksRecord(data, actor.GetPlayerID()) || WorkshopAtomicDebit.IsLocked(data) ||
                    data.GetString(WorkshopRemoteCraft.EscrowKey, "").Length != 0 || Covered(data.GetPosition()) != component ||
                    !WardAllows(actor.GetPlayerID(), data.GetPosition())) continue;
                // Native Container.CheckAccess: Group is denied, Private requires creator, Public is allowed.
                if (entry.Chest.m_privacy == Container.PrivacySetting.Group ||
                    (entry.Chest.m_privacy == Container.PrivacySetting.Private && data.GetLong(ZDOVars.s_creator, 0) != actor.GetPlayerID()) ||
                    (entry.Chest.m_privacy != Container.PrivacySetting.Private && entry.Chest.m_privacy != Container.PrivacySetting.Public)) continue;
                Container loaded = ZNetScene.instance.FindInstance(data.m_uid)?.GetComponent<Container>();
                if (loaded != null && (loaded.IsInUse() || WorkshopAtomicDebit.IsLocked(loaded.GetInventory()))) continue;
                result.Add(entry);
            }
            result.Sort((a, b) => {
                int distance = (a.Data.GetPosition() - point).sqrMagnitude.CompareTo((b.Data.GetPosition() - point).sqrMagnitude);
                return distance != 0 ? distance : string.CompareOrdinal(a.Data.m_uid.ToString(), b.Data.m_uid.ToString());
            });
            return result;
        }
    }
}
