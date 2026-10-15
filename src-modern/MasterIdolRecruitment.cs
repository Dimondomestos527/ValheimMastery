using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace ValheimMastery
{
    // Four stable WORLD slots; only empty or verified-dead eligible slots may spawn.
    internal static class MasterIdolRecruitment
    {
        private const int CheapProbes=48,TerrainCandidates=12,NativePaths=4;
        private static ZNet Session;private static ZDOMan Manager;private static ulong Generation;private static long World;private static float Next;private static int Cursor;private static string SearchHome;private static bool CadenceStarted;
        private static readonly float[] Retry=new float[4];
        private static readonly ulong[] Probes=new ulong[4];
        private static ZNet SearchSession;private static ZDOMan SearchManager;private static ulong SearchGeneration;
        private readonly struct SpawnShape
        {
            internal readonly Vector3 Center;internal readonly float Radius,HalfLine,Margin;
            internal SpawnShape(Vector3 center,float radius,float halfLine)
            {Center=center;Radius=radius+.1f;HalfLine=halfLine;Margin=Radius+new Vector3(center.x,0,center.z).magnitude;}
        }
        internal static IEnumerable<MasterIdolResidentCandidate> Pending(MasterIdolEffectZone zone)=>MasterIdolForestGuardState.Candidates(zone);
        private static bool Finite(Vector3 point)=>float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z);
        internal static bool TrySpawnCircle(MasterIdolEffectZone zone,out MasterIdolEffectCircle circle,out string reason)
        {
            circle=default;reason="home-circle-missing";
            if(zone?.Type!="BlackForest"||!Finite(zone.Home)||zone.Circles==null||zone.Circles.Length==0||zone.Circles.Length>4096)return false;
            bool found=false;
            foreach(var candidate in zone.Circles)
            {
                if(candidate.Point.x!=zone.Home.x||candidate.Point.y!=zone.Home.y||candidate.Point.z!=zone.Home.z)continue;
                if(!Finite(candidate.Point)||!float.IsFinite(candidate.Radius)||candidate.Radius<=0||candidate.Radius>1000){reason="invalid-home-circle";return false;}
                if(found&&candidate.Radius!=circle.Radius){reason="ambiguous-home-circle";return false;}
                circle=candidate;found=true;
            }
            if(found)reason="ready";return found;
        }
        private static bool Shape(BaseAI prototype,out SpawnShape shape)
        {
            shape=default;var capsule=prototype.GetComponent<CapsuleCollider>();var scale=prototype.transform.lossyScale;
            if(capsule==null||capsule.isTrigger||!capsule.enabled||capsule.direction!=1||!Finite(scale)||!Finite(capsule.center))return false;
            float radius=capsule.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z)),height=capsule.height*Mathf.Abs(scale.y);
            if(!float.IsFinite(radius)||!float.IsFinite(height)||radius<=.05f||radius>3||height<radius*2||height>8)return false;
            shape=new SpawnShape(Vector3.Scale(capsule.center,scale),radius,Mathf.Max(0,height*.5f-radius));return true;
        }
        // Only the actual active Ward workbench circle; every family has its own sweep.
        private static bool Sample(MasterIdolEffectCircle circle,int family,ref ulong cursor,out Vector3 point,out Vector3 endpoint)
        {
            point=endpoint=default;if(family<0||family>3||!Finite(circle.Point)||!float.IsFinite(circle.Radius)||circle.Radius<=0)return false;
            ulong ordinal=cursor++;double sweep=ordinal%1048576UL;
            double angle=(sweep*.618033988749895+family*.25)*Math.PI*2;
            double radial=(sweep+1)*.754877666246693;radial-=Math.Floor(radial);
            float distance=circle.Radius*(float)Math.Sqrt(.01+.95*radial);
            point=circle.Point+new Vector3((float)Math.Cos(angle),0,(float)Math.Sin(angle))*distance;
            endpoint=point-new Vector3((float)Math.Cos(angle),0,(float)Math.Sin(angle))*4;
            return Finite(point)&&Finite(endpoint);
        }
        private static bool Ground(MasterIdolEffectCircle circle,SpawnShape shape,ref Vector3 point)
        {
            if(!Finite(point)||!circle.Contains(point,-shape.Margin)||!ZoneSystem.instance.IsZoneLoaded(point)){MasterIdolForestSpawnDiagnostics.Trace("ground","not-loaded-or-circle");return false;}
            ZoneSystem.instance.GetGroundData(ref point,out var normal,out var biome,out var area,out var heightmap);
            if(heightmap==null||!Finite(point)||!Finite(normal)||normal.y<.7f){MasterIdolForestSpawnDiagnostics.Trace("ground","heightmap-normal");return false;}
            if(!float.IsFinite(ZoneSystem.instance.m_waterLevel)||point.y<ZoneSystem.instance.m_waterLevel+.3f){MasterIdolForestSpawnDiagnostics.Trace("ground","water");return false;}
            var center=point+shape.Center;var line=Vector3.up*shape.HalfLine;
            bool clear=!Physics.CheckCapsule(center-line,center+line,shape.Radius,LayerMask.GetMask("piece","static_solid","Default","character","character_net"),QueryTriggerInteraction.Ignore);
            if(!clear)MasterIdolForestSpawnDiagnostics.Trace("ground","occupied-capsule");return clear;
        }
        private static bool FindPoint(MasterIdolEffectZone zone,BaseAI prototype,int family,Vector3? observer,out Vector3 point,out Vector3 finish)
        {
            point=finish=default;if(!TrySpawnCircle(zone,out var circle,out var reason)){MasterIdolForestSpawnDiagnostics.Trace("search",reason);return false;}
            if(!Shape(prototype,out var shape)){MasterIdolForestSpawnDiagnostics.Trace("search","native-shape");return false;}int terrain=0,paths=0,loaded=0;
            for(int attempt=0;attempt<CheapProbes;attempt++)
            {
                if(!Sample(circle,family,ref Probes[family],out var candidate,out var endpoint)||!circle.Contains(candidate,-shape.Margin)||!ZoneSystem.instance.IsZoneLoaded(candidate))continue;
                loaded++;if(terrain++>=TerrainCandidates){MasterIdolForestSpawnDiagnostics.Trace("search","terrain-budget","loaded="+loaded+" paths="+paths);return false;}
                endpoint=circle.Inside(endpoint,-shape.Margin);
                if(observer.HasValue&&(observer.Value-candidate).sqrMagnitude>4096)continue;
                var horizontal=endpoint-candidate;horizontal.y=0;
                if(horizontal.sqrMagnitude<4||horizontal.sqrMagnitude>16.01f||!Ground(circle,shape,ref candidate)||!Ground(circle,shape,ref endpoint))continue;
                if(paths++>=NativePaths){MasterIdolForestSpawnDiagnostics.Trace("search","path-candidate-budget");return false;}
                if(!MasterIdolResidentBudget.Path()){MasterIdolForestSpawnDiagnostics.Trace("search","shared-path-budget");return false;}
                if(!Pathfinding.instance.HavePath(candidate,endpoint,prototype.m_pathAgentType))continue;
                point=candidate;finish=endpoint;MasterIdolForestSpawnDiagnostics.Trace("search","point-ready","family="+family+" loaded="+loaded+" paths="+paths);return true;
            }
            MasterIdolForestSpawnDiagnostics.Trace("search","no-physical-point","family="+family+" loaded="+loaded+" terrain="+terrain+" paths="+paths);return false;
        }
        internal static bool TryPosition(MasterIdolEffectZone zone,int family,Vector3? observer,out Vector3 point,out Vector3 endpoint)
        {
            point=endpoint=default;var net=ZNet.instance;
            if(net==null||net.GetWorld()==null||family<0||family>3||zone?.Type!="BlackForest"||ZoneSystem.instance==null||Pathfinding.instance==null)return false;
            if(SearchSession!=net||SearchManager!=ZDOMan.instance||SearchGeneration!=MasterIdolLoadIdentity.Generation||SearchHome!=zone.Id)
            {SearchSession=net;SearchManager=ZDOMan.instance;SearchGeneration=MasterIdolLoadIdentity.Generation;SearchHome=zone.Id;Array.Clear(Probes,0,4);}
            var prototype=ZNetScene.instance?.GetPrefab(MasterIdolForestRules.Families[family])?.GetComponent<BaseAI>();
            return prototype!=null&&FindPoint(zone,prototype,family,observer,out point,out endpoint);
        }
        internal static bool CheapPosition(MasterIdolEffectZone zone,int family,Vector3 point,Vector3 endpoint)
        {
            if(zone==null||family<0||family>3||!Finite(point)||!Finite(endpoint)||ZoneSystem.instance==null)return false;
            var prototype=ZNetScene.instance?.GetPrefab(MasterIdolForestRules.Families[family])?.GetComponent<BaseAI>();
            if(prototype==null||!Shape(prototype,out var shape)||!TrySpawnCircle(zone,out var circle,out _)||!circle.Contains(point,-shape.Margin)||!circle.Contains(endpoint,-shape.Margin))return false;
            var distance=endpoint-point;distance.y=0;float water=ZoneSystem.instance.m_waterLevel;
            return distance.sqrMagnitude>=4&&distance.sqrMagnitude<=16.01f&&float.IsFinite(water)&&point.y>=water+.3f&&endpoint.y>=water+.3f;
        }
        private static bool Context(ZNet net)
            =>net==Session&&ZNet.instance==Session&&ZDOMan.instance==Manager&&MasterIdolLoadIdentity.Generation==Generation&&net?.GetWorld()!=null&&net.GetWorldUID()==World&&net.IsServer()&&WorkbenchEnchantmentRuntime.Ready&&MasterIdolForestGuardState.Ready;
        internal static void Tick(List<MasterIdolEffectZone> zones,ISet<string> assigned,Func<bool> publishable)
        {
            var net=ZNet.instance;if(net?.IsServer()!=true||net.GetWorld()==null)return;
            if(!WorkbenchEnchantmentRuntime.Ready){MasterIdolForestSpawnDiagnostics.Trace("server","enchantment-not-ready");return;}
            if(!MasterIdolForestGuardState.Ready){MasterIdolForestSpawnDiagnostics.Trace("server","store-not-ready");return;}
            if(Session!=net||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||World!=net.GetWorldUID())
            {Session=net;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;World=net.GetWorldUID();Next=0;Cursor=0;SearchHome=null;CadenceStarted=false;Array.Clear(Retry,0,4);Array.Clear(Probes,0,4);}
            var forest=zones.Where(z=>z.Type=="BlackForest").Take(2).ToArray();if(forest.Length!=1){MasterIdolForestSpawnDiagnostics.Trace("server","forest-count-not-one","count="+forest.Length);return;}var zone=forest[0];
            MasterIdolForestGuardState.Observe(zone);
            if(!TrySpawnCircle(zone,out var circle,out var circleReason)){MasterIdolForestSpawnDiagnostics.Trace("server",circleReason);return;}
            if(!CadenceStarted)
            {
                if(!MasterIdolActivity.SpawnNear(circle.Point,circle.Radius+32)){MasterIdolForestSpawnDiagnostics.Trace("server","no-player-near-ward",MasterIdolActivity.DescribeNear());return;}
                if(!publishable()){MasterIdolForestSpawnDiagnostics.Trace("server","projection-not-publishable");return;}
                CadenceStarted=true;Next=Time.unscaledTime+2;MasterIdolForestSpawnDiagnostics.Trace("server","cadence-ready");return;
            }
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+2;
            bool born=false;
            if(publishable()&&MasterIdolForestSpawnProbe.TryTake(zone,out var lease))
            {
                try{born=Create(zone,assigned,publishable,lease.Family,lease.Before,lease.Point,lease);}
                finally{MasterIdolForestSpawnProbe.Release(lease);}
            }
            int family=Cursor;Cursor=(Cursor+1)%4;string type=MasterIdolForestRules.Families[family];var slots=MasterIdolForestGuardState.Slots;
            slots.TryGetValue(type,out var previous);
            if(previous!=null&&previous.Death==0){MasterIdolForestSpawnDiagnostics.Trace("server","slot-occupied");return;} // Missing/unloaded is not death; never re-create occupied slots.
            long now=net.GetTime().Ticks;
            if(previous!=null&&(!previous.Valid()||now<previous.Respawn))return;
            if(Time.unscaledTime<Retry[family])return;
            if(!MasterIdolActivity.SpawnNear(circle.Point,circle.Radius+32)){MasterIdolForestSpawnDiagnostics.Trace("server","no-player-near-ward",MasterIdolActivity.DescribeNear());return;}
            if(!publishable()){MasterIdolForestSpawnDiagnostics.Trace("server","projection-not-publishable");return;}Retry[family]=Time.unscaledTime+30;
            if(!born&&!net.IsDedicated()&&TryPosition(zone,family,null,out var point,out _))
            {Create(zone,assigned,publishable,family,previous,point,null);return;}
            MasterIdolForestSpawnProbe.Request(zone,family,previous);
        }
        private static bool Create(MasterIdolEffectZone zone,ISet<string> assigned,Func<bool> publishable,int family,MasterIdolForestGuardRecord previous,Vector3 point,MasterIdolForestSpawnProbe.Lease lease)
        {
            var net=ZNet.instance;var slots=MasterIdolForestGuardState.Slots;string type=MasterIdolForestRules.Families[family];
            if(!Context(net)||slots==null||!publishable()||slots.TryGetValue(type,out var current)&&!ReferenceEquals(current,previous)||lease!=null&&!MasterIdolForestSpawnProbe.StillValid(lease,zone))return false;
            if(previous!=null&&(previous.Death==0||!previous.Valid()||net.GetTime().Ticks<previous.Respawn))return false;
            var prefab=ZNetScene.instance?.GetPrefab(type);if(prefab==null)return false;
            var created=UnityEngine.Object.Instantiate(prefab,point,Quaternion.identity);var view=created.GetComponent<ZNetView>();var actor=created.GetComponent<Character>();var data=view?.GetZDO();
            if(view?.IsValid()!=true||!view.IsOwner()||actor==null||data?.Persistent!=true||data.GetPrefab()!=type.GetStableHashCode()||!ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid),data)||
                !Context(net)||!publishable()||lease!=null&&!MasterIdolForestSpawnProbe.StillValid(lease,zone))
            {MasterIdolForestSpawnDiagnostics.Trace("server","factory-registration-rejected");if(view?.IsValid()==true&&view.IsOwner())ZNetScene.instance.Destroy(created);else UnityEngine.Object.Destroy(created);return false;}
            MasterIdolForestGuardRecord record=null;
            try
            {
                if(created.GetComponent<MonsterAI>() is MonsterAI ai){ai.m_eventCreature=false;ai.m_despawnInDay=false;data.Set(ZDOVars.s_eventCreature,false);data.Set(ZDOVars.s_despawnInDay,false);}
                record=new MasterIdolForestGuardRecord{Home=zone.Id,Family=type,Uid=data.m_uid.ToString(),Nonce=Guid.NewGuid().ToString("N"),Born=net.GetTime().Ticks,Name=previous?.Name??MasterIdolForestGuardState.DefaultKey(type)};
                MasterIdolForestGuardState.Stamp(actor,record);MasterIdolForestGuardState.Added(record);zone.Residents[record.Uid]=type;assigned.Add(record.Uid);MasterIdolWorldIndex.Observe(data);
                MasterIdolResidentMetrics.Count("guard.native-birth");
                MasterIdolForestSpawnDiagnostics.Trace("server","native-birth-registered","family="+family);
                // Only this newly registered persistent UID is handed to its physical validator.
                if(lease!=null)
                {
                    if(!Context(net)||!publishable()||!MasterIdolForestSpawnProbe.TransferReady(lease,zone,record,data))throw new InvalidOperationException("Guard handoff witness changed");
                    data.SetOwner(lease.Peer.m_uid);
                }
                return true;
            }
            catch
            {
                bool own=false;
                try{own=Context(net)&&view.IsValid()&&view.IsOwner()&&data!=null&&ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid),data)&&ReferenceEquals(view.GetZDO(),data);}catch(Exception){}
                bool registered=record!=null&&slots.TryGetValue(record.Family,out var published)&&ReferenceEquals(published,record);
                if(registered&&(!own||record.Death!=0))
                {
                    // Applied or uncertain owner transfer must keep the occupied UID and consumed nonce.
                    MasterIdolResidentMetrics.Count("guard.handoff-retained");return true;
                }
                if(!own)return false;
                MasterIdolForestGuardState.CancelBirth(record,previous);
                if(record!=null){zone.Residents.Remove(record.Uid);assigned.Remove(record.Uid);}
                ZNetScene.instance.Destroy(created);return false;
            }
        }
        internal static string Describe()=>"recruitment=world-four native guards; ready="+MasterIdolForestGuardState.Ready+" slots="+(MasterIdolForestGuardState.Slots?.Count??0)+" search=active Ward workbench circle; dedicated=bounded peer position; wild recruitment=off; respawn=verified death+one game-day; "+MasterIdolActivity.DescribeNear();
    }

    internal static class MasterIdolForestSpawnDiagnostics
    {
        private static readonly HashSet<string> Seen=new HashSet<string>(StringComparer.Ordinal);
        private static ZNet Net;private static long World;private static ulong Generation;
        internal static void Trace(string stage,string reason,string detail=null)
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(Net!=net||World!=world||Generation!=MasterIdolLoadIdentity.Generation){Seen.Clear();Net=net;World=world;Generation=MasterIdolLoadIdentity.Generation;}
            string key=stage+":"+reason;if(Seen.Count>=32||!Seen.Add(key))return;
            try{MasteryPlugin.Log?.LogInfo("[GuardSpawn] "+(net?.IsServer()==true?"server":"client")+" world="+world+" stage="+stage+" reason="+reason+(detail==null?"":" "+detail));}catch(Exception){}
        }
    }

    internal sealed class MasterIdolForestGuardTravel:MonoBehaviour
    {
        private readonly List<Vector3> Route=new List<Vector3>(32);private Vector3 Goal;private string Home;private float Next,Until;private int Step;
        internal static bool Move(Character actor,MasterIdolEffectZone zone,float dt,out bool processed)
        {
            processed=false;if(actor==null||zone==null||actor.m_nview?.IsOwner()!=true||!MasterIdolForestGuardState.ModuleBorn(actor.m_nview.GetZDO()))return false;
            var travel=actor.GetComponent<MasterIdolForestGuardTravel>()??actor.gameObject.AddComponent<MasterIdolForestGuardTravel>();return travel.StepTo(actor,zone,dt,out processed);
        }
        private bool StepTo(Character actor,MasterIdolEffectZone zone,float dt,out bool processed)
        {
            processed=false;var ai=actor.GetBaseAI();if(ai==null)return false;
            if(Home!=zone.Id){Home=zone.Id;Route.Clear();Next=Until=0;}
            if(!MasterIdolActivity.Near(actor.transform.position))return true;
            if(Route.Count==0||Time.unscaledTime>=Until||(actor.transform.position-Goal).sqrMagnitude<1)
            {
                Route.Clear();if(Time.unscaledTime>=Next){Next=Time.unscaledTime+10;Plan(actor,zone,ai);}
            }
            processed=MasterIdolBaseUpdateBridge.Run(ai,dt);if(!processed)return true;
            if(Route.Count==0){ai.StopMoving();return true;}
            while(Step<Route.Count&&(actor.transform.position-Route[Step]).sqrMagnitude<1.44f)Step++;
            ai.MoveTo(dt,Step<Route.Count?Route[Step]:Goal,1f,false);return true;
        }
        private void Plan(Character actor,MasterIdolEffectZone zone,BaseAI ai)
        {
            if(ZoneSystem.instance==null||Pathfinding.instance==null)return;Vector3 direction=zone.Home-actor.transform.position;direction.y=0;
            if(direction.sqrMagnitude<1)return;Vector3 point=actor.transform.position+direction.normalized*Math.Min(24,direction.magnitude);
            if(!ZoneSystem.instance.IsZoneLoaded(point))return;ZoneSystem.instance.GetGroundData(ref point,out var normal,out var biome,out var area,out var heightmap);
            if(heightmap==null||normal.y<.7f||point.y<ZoneSystem.instance.m_waterLevel+.3f||Physics.CheckSphere(point+Vector3.up,.5f,LayerMask.GetMask("piece","static_solid"),QueryTriggerInteraction.Ignore)||!MasterIdolResidentBudget.Path())return;
            if(!Pathfinding.instance.GetPath(actor.transform.position,point,Route,ai.m_pathAgentType,true,false,false)||Route.Count>128){Route.Clear();return;}
            Goal=point;Step=0;Until=Time.unscaledTime+30;
        }
    }
}
