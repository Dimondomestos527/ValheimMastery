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
        private readonly struct SpawnShape
        {
            internal readonly Vector3 Center;internal readonly float Radius,HalfLine,Margin;
            internal SpawnShape(Vector3 center,float radius,float halfLine)
            {Center=center;Radius=radius+.1f;HalfLine=halfLine;Margin=Radius+new Vector3(center.x,0,center.z).magnitude;}
        }
        internal static IEnumerable<MasterIdolResidentCandidate> Pending(MasterIdolEffectZone zone)=>MasterIdolForestGuardState.Candidates(zone);
        private static bool Finite(Vector3 point)=>float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z);
        private static bool Shape(BaseAI prototype,out SpawnShape shape)
        {
            shape=default;var capsule=prototype.GetComponent<CapsuleCollider>();var scale=prototype.transform.lossyScale;
            if(capsule==null||capsule.isTrigger||!capsule.enabled||capsule.direction!=1||!Finite(scale)||!Finite(capsule.center))return false;
            float radius=capsule.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z)),height=capsule.height*Mathf.Abs(scale.y);
            if(!float.IsFinite(radius)||!float.IsFinite(height)||radius<=.05f||radius>3||height<radius*2||height>8)return false;
            shape=new SpawnShape(Vector3.Scale(capsule.center,scale),radius,Mathf.Max(0,height*.5f-radius));return true;
        }
        // Visit every actual network circle; later sweeps change both angle and radius.
        private static bool Sample(MasterIdolEffectZone zone,int family,ref ulong cursor,out Vector3 point,out Vector3 endpoint)
        {
            point=endpoint=default;var circles=zone.Circles;if(circles==null||circles.Length==0||circles.Length>4096||family<0||family>3)return false;
            ulong ordinal=cursor++;int index=(int)(ordinal%(ulong)circles.Length);var circle=circles[index];
            if(!Finite(circle.Point)||!float.IsFinite(circle.Radius)||circle.Radius<=0)return false;
            double sweep=(ordinal/(ulong)circles.Length)%1048576UL;
            double angle=(sweep*.618033988749895+family*.25+index*.381966011250105)*Math.PI*2;
            double radial=(sweep+1)*.754877666246693;radial-=Math.Floor(radial);
            float distance=circle.Radius*(float)Math.Sqrt(.01+.95*radial);
            point=circle.Point+new Vector3((float)Math.Cos(angle),0,(float)Math.Sin(angle))*distance;
            endpoint=point-new Vector3((float)Math.Cos(angle),0,(float)Math.Sin(angle))*4;
            return Finite(point)&&Finite(endpoint);
        }
        private static bool Ground(MasterIdolEffectZone zone,SpawnShape shape,ref Vector3 point)
        {
            if(!Finite(point)||!zone.Contains(point,-shape.Margin)||!ZoneSystem.instance.IsZoneLoaded(point))return false;
            ZoneSystem.instance.GetGroundData(ref point,out var normal,out var biome,out var area,out var heightmap);
            if(heightmap==null||!Finite(point)||!Finite(normal)||normal.y<.7f||!float.IsFinite(ZoneSystem.instance.m_waterLevel)||point.y<ZoneSystem.instance.m_waterLevel+.3f)return false;
            var center=point+shape.Center;var line=Vector3.up*shape.HalfLine;
            return !Physics.CheckCapsule(center-line,center+line,shape.Radius,LayerMask.GetMask("piece","static_solid","Default","character","character_net"),QueryTriggerInteraction.Ignore);
        }
        private static bool FindPoint(MasterIdolEffectZone zone,BaseAI prototype,int family,out Vector3 point)
        {
            point=default;if(!Shape(prototype,out var shape))return false;int terrain=0,paths=0;
            for(int attempt=0;attempt<CheapProbes;attempt++)
            {
                if(!Sample(zone,family,ref Probes[family],out var candidate,out var endpoint)||!zone.Contains(candidate,-shape.Margin)||!ZoneSystem.instance.IsZoneLoaded(candidate))continue;
                if(terrain++>=TerrainCandidates)return false;
                endpoint=zone.Inside(endpoint,-shape.Margin);
                var horizontal=endpoint-candidate;horizontal.y=0;
                if(horizontal.sqrMagnitude<4||horizontal.sqrMagnitude>16.01f||!Ground(zone,shape,ref candidate)||!Ground(zone,shape,ref endpoint))continue;
                if(paths++>=NativePaths||!MasterIdolResidentBudget.Path())return false;
                if(!Pathfinding.instance.HavePath(candidate,endpoint,prototype.m_pathAgentType))continue;
                point=candidate;return true;
            }
            return false;
        }
        private static bool Context(ZNet net)
            =>net==Session&&ZNet.instance==Session&&ZDOMan.instance==Manager&&MasterIdolLoadIdentity.Generation==Generation&&net?.GetWorld()!=null&&net.GetWorldUID()==World&&net.IsServer()&&WorkbenchEnchantmentRuntime.Ready&&MasterIdolForestGuardState.Ready;
        internal static void Tick(List<MasterIdolEffectZone> zones,ISet<string> assigned,Func<bool> publishable)
        {
            var net=ZNet.instance;if(net?.IsServer()!=true||net.GetWorld()==null||!WorkbenchEnchantmentRuntime.Ready||!MasterIdolForestGuardState.Ready)return;
            if(Session!=net||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||World!=net.GetWorldUID())
            {Session=net;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;World=net.GetWorldUID();Next=0;Cursor=0;SearchHome=null;CadenceStarted=false;Array.Clear(Retry,0,4);Array.Clear(Probes,0,4);}
            var forest=zones.Where(z=>z.Type=="BlackForest").Take(2).ToArray();if(forest.Length!=1)return;var zone=forest[0];
            if(SearchHome!=zone.Id){SearchHome=zone.Id;Array.Clear(Probes,0,4);}
            MasterIdolForestGuardState.Observe(zone);
            if(!CadenceStarted){if(!MasterIdolActivity.Near(zone)||!publishable())return;CadenceStarted=true;Next=Time.unscaledTime+2;return;}
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+2;
            int family=Cursor;Cursor=(Cursor+1)%4;string type=MasterIdolForestRules.Families[family];var slots=MasterIdolForestGuardState.Slots;
            slots.TryGetValue(type,out var previous);
            if(previous!=null&&previous.Death==0)return; // Missing/unloaded is not death; never re-create occupied slots.
            long now=net.GetTime().Ticks;
            if(previous!=null&&(!previous.Valid()||now<previous.Respawn))return;
            if(Time.unscaledTime<Retry[family]||!MasterIdolActivity.Near(zone)||!publishable())return;Retry[family]=Time.unscaledTime+30;
            var prefab=ZNetScene.instance?.GetPrefab(type);var prototype=prefab?.GetComponent<BaseAI>();if(prototype==null||ZoneSystem.instance==null||Pathfinding.instance==null)return;
            if(!FindPoint(zone,prototype,family,out var point)||!Context(net)||!publishable()||slots.TryGetValue(type,out var current)&&!ReferenceEquals(current,previous))return;
            var created=UnityEngine.Object.Instantiate(prefab,point,Quaternion.identity);var view=created.GetComponent<ZNetView>();var actor=created.GetComponent<Character>();var data=view?.GetZDO();
            if(view?.IsValid()!=true||!view.IsOwner()||actor==null||data?.Persistent!=true||data.GetPrefab()!=type.GetStableHashCode()||!ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid),data))
            {if(view?.IsValid()==true&&view.IsOwner())ZNetScene.instance.Destroy(created);else UnityEngine.Object.Destroy(created);return;}
            MasterIdolForestGuardRecord record=null;
            try
            {
                if(created.GetComponent<MonsterAI>() is MonsterAI ai){ai.m_eventCreature=false;ai.m_despawnInDay=false;data.Set(ZDOVars.s_eventCreature,false);data.Set(ZDOVars.s_despawnInDay,false);}
                record=new MasterIdolForestGuardRecord{Home=zone.Id,Family=type,Uid=data.m_uid.ToString(),Nonce=Guid.NewGuid().ToString("N"),Born=net.GetTime().Ticks,Name=previous?.Name??MasterIdolForestGuardState.DefaultKey(type)};
                MasterIdolForestGuardState.Stamp(actor,record);MasterIdolForestGuardState.Added(record);zone.Residents[record.Uid]=type;assigned.Add(record.Uid);MasterIdolWorldIndex.Observe(data);
                MasterIdolResidentMetrics.Count("guard.native-birth");return;
            }
            catch
            {
                MasterIdolForestGuardState.CancelBirth(record,previous);
                if(record!=null){zone.Residents.Remove(record.Uid);assigned.Remove(record.Uid);}
                if(view.IsValid()&&view.IsOwner())ZNetScene.instance.Destroy(created);return;
            }
        }
        internal static string Describe()=>"recruitment=world-four native guards; ready="+MasterIdolForestGuardState.Ready+" slots="+(MasterIdolForestGuardState.Slots?.Count??0)+" search=whole-network fair bounded; initial=one four-slot cycle ~8s; wild recruitment=off; respawn=verified death+one game-day";
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
