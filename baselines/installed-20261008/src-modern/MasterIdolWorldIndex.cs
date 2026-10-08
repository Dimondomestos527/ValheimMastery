using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Server-side membership, not a count guessed from rendered scene objects.
    internal static class MasterIdolWorldIndex
    {
        internal static readonly Dictionary<ZDOID, ZDO> Idols = new Dictionary<ZDOID, ZDO>();
        private static readonly Dictionary<ZDOID, ZDO> Benches = new Dictionary<ZDOID, ZDO>(), Wards = new Dictionary<ZDOID, ZDO>(), Extensions = new Dictionary<ZDOID, ZDO>();
        internal static readonly Dictionary<ZDOID,ZDO> Stations = new Dictionary<ZDOID,ZDO>();
        private static readonly Dictionary<ZDOID,ZDO> Families=new Dictionary<ZDOID,ZDO>();
        private static readonly Dictionary<ZDOID,string> FamilyHomes=new Dictionary<ZDOID,string>();
        private static readonly Dictionary<string,HashSet<ZDOID>> HomeMembers=new Dictionary<string,HashSet<ZDOID>>(StringComparer.Ordinal);
        private static readonly MasterIdolSpatialIndex FamilyPoints=new MasterIdolSpatialIndex();
        private sealed class Definition { internal string Family;internal float Health; internal CraftingStation Station, Bench; internal PrivateArea Ward; internal StationExtension Extension; internal string ExtensionName; }
        private static readonly Dictionary<int, Definition> Definitions = new Dictionary<int, Definition>();
        private static readonly Dictionary<ZDOID, float> Radii = new Dictionary<ZDOID, float>();
        private static ZNet Session;
        private static ZDOMan Manager;
        private static long World;
        private static int Loading;
        private static bool Blocked, LoadFailed;
        private static readonly HashSet<ZDOID> DirtyBenches = new HashSet<ZDOID>();
        private static readonly MasterIdolSpatialIndex BenchPoints = new MasterIdolSpatialIndex(), BenchCoverage = new MasterIdolSpatialIndex(), WardCoverage = new MasterIdolSpatialIndex(), ExtensionCoverage = new MasterIdolSpatialIndex();
        internal static uint TopologyRevision { get; private set; }
        internal static int RadiusRebuilds { get; private set; }
        internal static bool Ready { get; private set; }
        internal static uint Revision { get; private set; }
        internal static int StartupScans { get; private set; }
        internal static void Reset()
        { Families.Clear();FamilyHomes.Clear();HomeMembers.Clear();FamilyPoints.Clear(); Idols.Clear(); Stations.Clear(); Benches.Clear(); Wards.Clear(); Extensions.Clear(); Definitions.Clear(); Radii.Clear(); Ready = false; DirtyBenches.Clear(); BenchPoints.Clear(); BenchCoverage.Clear(); WardCoverage.Clear(); ExtensionCoverage.Clear(); TopologyRevision++; Revision++; Session = null; Manager = null; World = 0; }
        internal static void BeginLoad() { if (Loading == 0) LoadFailed = false; Loading++; Blocked = true; Reset(); }
        internal static void EndLoad(bool failed) { LoadFailed |= failed; Loading = Math.Max(0, Loading - 1); if (Loading == 0) Blocked = LoadFailed; }
        internal static void Shutdown() { Reset(); Loading = 0; Blocked = LoadFailed = false; }
        internal static bool Ensure()=>MasterIdolPilotA.Enabled?EnsureOptimized():EnsureOriginal();
        private static bool EnsureOriginal()
        {
            var net = ZNet.instance; var manager = ZDOMan.instance;
            if (net?.IsServer() != true || net.GetWorld() == null || manager == null || ZNetScene.instance == null || MasterIdolPieces.Workbench == null || Blocked || Loading != 0) return false;
            if (!ReferenceEquals(Session, net) || !ReferenceEquals(Manager, manager) || World != net.GetWorldUID())
            { Reset(); Session = net; Manager = manager; World = net.GetWorldUID(); }
            if (Ready) return true;
            // The only entire-world enumeration. Native load/reset invalidates completeness.
            MasterIdolTiming.Measure("index.startup", () => { foreach (var zdo in manager.m_objectsByID.Values) Add(zdo); return true; });
            StartupScans++; Ready = true; Revision++; return true;
        }
        private static bool EnsureOptimized()
        {
            var net = ZNet.instance; var manager = ZDOMan.instance;
            if (net?.IsServer() != true || net.GetWorld() == null || manager == null || ZNetScene.instance == null || MasterIdolPieces.Workbench == null || Blocked || Loading != 0) return false;
            if (!ReferenceEquals(Session, net) || !ReferenceEquals(Manager, manager) || World != net.GetWorldUID())
            { Reset(); Session = net; Manager = manager; World = net.GetWorldUID(); }
            if (Ready) return true;
            // The only entire-world enumeration. Native load/reset invalidates completeness.
            Populate(manager);
            StartupScans++; Ready = true; Revision++; return true;
        }
        private static void Populate(ZDOMan manager)
        {MasterIdolTiming.Measure("index.startup",()=>{foreach(var zdo in manager.m_objectsByID.Values)Add(zdo);return true;});}
        private static Definition Describe(int hash)
        {
            if (Definitions.TryGetValue(hash, out var known)) return known;
            var prefab = ZNetScene.instance?.GetPrefab(hash);
            var result = new Definition();
            if (prefab != null)
            {
                if(MasterIdolResidentRoster.Limit(prefab.name)>0){result.Family=prefab.name;result.Health=prefab.GetComponent<Character>()?.m_health??1f;}
                var station = prefab.GetComponent<CraftingStation>(); result.Station=station;
                if (station != null && station.m_name == MasterIdolPieces.Workbench?.m_name) result.Bench = station;
                result.Ward = prefab.GetComponent<PrivateArea>(); result.Extension = prefab.GetComponent<StationExtension>();
                result.ExtensionName = prefab.GetComponent<Piece>()?.m_name ?? prefab.name;
            }
            if (prefab != null && Definitions.Count < 4096) Definitions[hash] = result;
            return result;
        }
        private static bool Actual(ZDO zdo) => zdo != null && ReferenceEquals(ZDOMan.instance?.GetZDO(zdo.m_uid), zdo);
        private static void Add(ZDO zdo)
        {
            if (!Actual(zdo)) return;
            var definition = Describe(zdo.GetPrefab());
            IndexFamily(zdo,definition);
            if(definition.Station!=null) { Stations[zdo.m_uid]=zdo;TopologyRevision++; }
            if (MasterIdolWorldRegistry.Profile(zdo.GetPrefab()) != null) Idols[zdo.m_uid] = zdo;
            if (definition.Bench != null) { Benches[zdo.m_uid] = zdo; BenchPoints.Set(zdo.m_uid,zdo.GetPosition(),0); DirtyBenches.Add(zdo.m_uid); TopologyRevision++; }
            if (definition.Ward != null) { Wards[zdo.m_uid] = zdo; WardCoverage.Set(zdo.m_uid,zdo.GetPosition(),definition.Ward.m_radius); }
            if (definition.Extension != null) { Extensions[zdo.m_uid] = zdo; ExtensionCoverage.Set(zdo.m_uid,zdo.GetPosition(),definition.Extension.m_maxStationDistance); DirtyNearby(zdo.GetPosition(),definition.Extension.m_maxStationDistance); TopologyRevision++; }
        }
        internal static void Observe(ZDO zdo)
        {
            if (!Ready || Blocked || Loading != 0 || !ReferenceEquals(Session, ZNet.instance) || !ReferenceEquals(Manager, ZDOMan.instance) || ZNet.instance?.GetWorld() == null || World != ZNet.instance.GetWorldUID() || !Actual(zdo)) return;
            bool wasIdol = Idols.Remove(zdo.m_uid);
            RemoveCategories(zdo.m_uid); Add(zdo);
            if (wasIdol || Idols.ContainsKey(zdo.m_uid)) Revision++;
        }
        internal static void Moved(ZDO zdo)
        {
            if (!Ready || Blocked || zdo == null || !ReferenceEquals(Session, ZNet.instance) || !ReferenceEquals(Manager, ZDOMan.instance) ||
                ZNet.instance?.GetWorld() == null || World != ZNet.instance.GetWorldUID() || !Actual(zdo)) return;
            if(Families.ContainsKey(zdo.m_uid))IndexFamily(zdo,Describe(zdo.GetPrefab()));
            if (Stations.ContainsKey(zdo.m_uid) || Extensions.ContainsKey(zdo.m_uid) || Wards.ContainsKey(zdo.m_uid))
            { RemoveCategories(zdo.m_uid); Add(zdo); }
            if (Idols.ContainsKey(zdo.m_uid)) Revision++;
        }
        internal static void Removed(ZDOID id)
        {
            if (!Ready || ZDOMan.instance?.GetZDO(id) != null) return;
            RemoveFamily(id);
            if (Idols.Remove(id)) Revision++;
            RemoveCategories(id);
        }
        private static void DirtyNearby(Vector3 point,float radius)
        { foreach(var id in BenchPoints.Near(point,radius)) if(Benches.TryGetValue(id,out var bench) && HorizontalSquared(point,bench.GetPosition()) < radius*radius) DirtyBenches.Add(id); }
        private static void RemoveCategories(ZDOID id)
        {
            if(Stations.Remove(id))TopologyRevision++;
            if (Benches.Remove(id)) { BenchPoints.Remove(id); BenchCoverage.Remove(id); DirtyBenches.Remove(id); Radii.Remove(id); TopologyRevision++; }
            if (Extensions.Remove(id))
            { if(ExtensionCoverage.Location(id,out var point,out float radius)) DirtyNearby(point,radius); ExtensionCoverage.Remove(id); TopologyRevision++; }
            Wards.Remove(id); WardCoverage.Remove(id);
        }
        private static void RebuildRadii()
        {
            if (DirtyBenches.Count == 0) return;
            foreach(var id in DirtyBenches)
            {
                if(!Benches.TryGetValue(id,out var bench)||!Actual(bench))continue;
                var station=Describe(bench.GetPrefab()).Bench;if(station==null)continue;
                float radius=StationRadius(bench);
                Radii[id]=radius;BenchCoverage.Set(id,bench.GetPosition(),radius);RadiusRebuilds++;
            }
            DirtyBenches.Clear();
        }
        internal static float StationRadius(ZDO stationData)
        {
            if(!Actual(stationData))return 0;
            var station=Describe(stationData.GetPrefab()).Station;if(station==null)return 0;
            int level=1;var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var id in ExtensionCoverage.At(stationData.GetPosition()))
            {
                if(!Extensions.TryGetValue(id,out var data)||!Actual(data))continue;
                var definition=Describe(data.GetPrefab());var ext=definition.Extension;
                if(ext?.m_craftingStation==null||ext.m_craftingStation.m_name!=station.m_name||
                    Vector3.Distance(data.GetPosition(),stationData.GetPosition())>=ext.m_maxStationDistance)continue;
                if(ext.m_stack||names.Add(definition.ExtensionName))level++;
            }
            return WorkshopStationRadius.EffectiveRadius(station.m_rangeBuild,level);
        }
        private static float HorizontalSquared(Vector3 a, Vector3 b) { float x = a.x-b.x, z = a.z-b.z; return x*x+z*z; }
        internal static string Diagnostics() => "ready="+Ready+", benches="+Benches.Count+", wards="+Wards.Count+", idols="+Idols.Count+", scans="+StartupScans;
        internal static bool HasWorkbench(Vector3 point)
        {
            if (!Ensure()) return false; RebuildRadii();
            foreach (var id in BenchCoverage.At(point))
                if (Benches.TryGetValue(id,out var bench) && Actual(bench) && Radii.TryGetValue(bench.m_uid, out float radius) && radius > 0 && HorizontalSquared(point, bench.GetPosition()) < radius*radius) return true;
            return false;
        }
        internal static List<MasterIdolEffectWard> EffectWards()
        {
            var result=new List<MasterIdolEffectWard>();if(!Ensure())return result;
            foreach(var zdo in Wards.Values)
            {
                if(!Actual(zdo)||!zdo.GetBool(ZDOVars.s_enabled,false))continue;var ward=Describe(zdo.GetPrefab()).Ward;if(ward==null||ward.m_radius<=0)continue;
                int count=zdo.GetInt(ZDOVars.s_permitted,0);if(count<0||count>4096)count=0;
                var ids=new long[count];for(int i=0;i<count;i++)ids[i]=zdo.GetLong("pu_id"+i,0);
                result.Add(new MasterIdolEffectWard{Circle=new MasterIdolEffectCircle(zdo.GetPosition(),ward.m_radius),Creator=zdo.GetLong(ZDOVars.s_creator,0),Permitted=ids});
            }
            return result;
        }
        private static void RemoveFamily(ZDOID id)
        {
            Families.Remove(id);FamilyPoints.Remove(id);
            if(FamilyHomes.TryGetValue(id,out string home)){FamilyHomes.Remove(id);if(HomeMembers.TryGetValue(home,out var ids)){ids.Remove(id);if(ids.Count==0)HomeMembers.Remove(home);}}
        }
        private static void IndexFamily(ZDO zdo,Definition definition)
        {
            var id=zdo.m_uid;if(definition.Family==null){if(Families.ContainsKey(id))RemoveFamily(id);return;}
            Families[id]=zdo;var point=zdo.GetPosition();
            if(!FamilyPoints.Location(id,out var old,out _)||Math.Floor(old.x/32f)!=Math.Floor(point.x/32f)||Math.Floor(old.z/32f)!=Math.Floor(point.z/32f))FamilyPoints.Set(id,point,0);
            string home=MasterIdolNativeTaming.Token(zdo.GetString("vm.idol.home.v1",""))??"";
            if(FamilyHomes.TryGetValue(id,out string previous)&&previous==home)return;
            if(previous!=null&&HomeMembers.TryGetValue(previous,out var former)){former.Remove(id);if(former.Count==0)HomeMembers.Remove(previous);}
            FamilyHomes[id]=home;if(home.Length==0)return;
            if(!HomeMembers.TryGetValue(home,out var members)){members=new HashSet<ZDOID>();HomeMembers[home]=members;}members.Add(id);
        }
        internal static IEnumerable<MasterIdolResidentCandidate> ResidentCandidates(MasterIdolEffectZone zone,IEnumerable<string> previous)
        {
            if(!Ensure())yield break;var ids=new HashSet<ZDOID>();
            foreach(var circle in zone.Circles)foreach(var id in FamilyPoints.Near(circle.Point,circle.Radius))ids.Add(id);
            if(HomeMembers.TryGetValue(MasterIdolNativeTaming.Token(zone.Id)??"",out var homes))foreach(var id in homes)ids.Add(id);
            foreach(string uid in previous)if(MasterIdolNetworkQuota.TryUid(uid,out long user,out uint id))ids.Add(new ZDOID(user,id));
            foreach(var id in ids)
            {
                if(!Families.TryGetValue(id,out var zdo)||!Actual(zdo))continue;var definition=Describe(zdo.GetPrefab());
                float health=zdo.GetFloat(ZDOVars.s_health,definition.Health);
                if(!zdo.Persistent||zdo.GetBool(ZDOVars.s_eventCreature,false)||zdo.GetBool(ZDOVars.s_despawnInDay,false)||definition.Family==null||float.IsNaN(health)||float.IsInfinity(health)||health<=0)continue;
                bool tamed=zdo.GetBool(ZDOVars.s_tamed,false);if(tamed&&(!MasterIdolNativeTaming.Assigned(zdo)||!MasterIdolNativeTaming.SameHome(zdo.GetString("vm.idol.home.v1",""),zone.Id)))continue;
                bool existing=MasterIdolNativeTaming.SameHome(zdo.GetString("vm.idol.home.v1",""),zone.Id);
                if(existing||zone.Contains(zdo.GetPosition())||System.Linq.Enumerable.Contains(previous,id.ToString()))yield return new MasterIdolResidentCandidate(id.ToString(),definition.Family,existing,existing?zone.Id:zdo.GetString("vm.idol.home.v1",""));
            }
        }
                internal static IEnumerable<ZDO> NearbyForestResidents(Vector3 center,float radius,string family)
        {
            if(!Ensure())yield break;
            int visited=0;
            foreach(var id in FamilyPoints.Near(center,radius))
            {
                if(++visited>256)yield break;
                if(!Families.TryGetValue(id,out var zdo)||!Actual(zdo))continue;
                var definition=Describe(zdo.GetPrefab());
                if(!zdo.Persistent||definition.Family!=family||HorizontalSquared(center,zdo.GetPosition())>radius*radius||zdo.GetFloat(ZDOVars.s_health,definition.Health)<=0||zdo.GetBool(ZDOVars.s_tamed,false))continue;
                yield return zdo;
            }
        }
        internal static bool WardAllows(long player, Vector3 point)
        {
            if (!Ensure()) return false;
            foreach (var id in WardCoverage.At(point))
            {
                if (!Wards.TryGetValue(id,out var zdo) || !Actual(zdo)) continue;
                var ward = Describe(zdo.GetPrefab()).Ward; if (ward == null || !zdo.GetBool(ZDOVars.s_enabled, false) ||
                    HorizontalSquared(point,zdo.GetPosition()) >= ward.m_radius*ward.m_radius || zdo.GetLong(ZDOVars.s_creator,0)==player) continue;
                int count=zdo.GetInt(ZDOVars.s_permitted,0); if(count<0 || count>4096)return false;
                bool allowed=false; for(int i=0;i<count;i++) if(zdo.GetLong("pu_id"+i,0)==player){allowed=true;break;}
                if(!allowed)return false;
            }
            return true;
        }
    }
}



