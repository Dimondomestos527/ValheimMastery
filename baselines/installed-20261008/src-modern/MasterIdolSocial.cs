using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolSocial:MonoBehaviour
    {
        private const string PauseKey="vm.idol.social.pause.v1";
        private sealed class Visit{internal double Last;internal bool Present;}
        private readonly Dictionary<long,Visit> Visits=new Dictionary<long,Visit>();
        private readonly Collider[] Nearby=new Collider[48];private readonly List<Vector3> Route=new List<Vector3>(32);private readonly List<MasterIdolSocialRoutes.Interval> RouteRanges=new List<MasterIdolSocialRoutes.Interval>(32);private int RouteIndex,PathAttempts;private MasterIdolEffectZone RouteHome;
        private BaseAI Ai;private Character Creature;private ZNetView View;private MasterIdolLeash Leash;private MasterIdolSocialSeats Seats;
        private Player Greeting;private Vector3 Goal,Look;private Chair ChairGoal;private float Next,End;private bool Pause,HasGoal,Dormant;private string Home;private string Kind="idle",SpotNonce;private float NextYield;
        private void Awake(){Ai=GetComponent<BaseAI>();Creature=GetComponent<Character>();View=GetComponent<ZNetView>();Leash=GetComponent<MasterIdolLeash>();Seats=GetComponent<MasterIdolSocialSeats>();}
        private void Update()
        {
            Leash=Leash??GetComponent<MasterIdolLeash>();Seats=Seats??GetComponent<MasterIdolSocialSeats>();
            if(View?.IsValid()!=true||!View.IsOwner()||Leash?.SocialHome==null||MasterIdolSocialSeats.Battle(Ai,Creature)||MasterIdolPilotA.Get<MasterIdolWelcome>(this)?.IsPresenting==true){Reset();return;}
            if(!MasterIdolActivity.Near(transform.position,MasterIdolResidentCycleRules.ActivityRadius(Dormant))){if(!Dormant){Finish();Seats?.Stop();Dormant=true;}return;}
            if(Dormant){MasterIdolActivity.Wakes++;Dormant=false;Next=Time.unscaledTime+UnityEngine.Random.Range(.2f,2f);}
            if(Home!=Leash.SocialHome.Id){Reset();Home=Leash.SocialHome.Id;}
        }
        private void Reset(){ReleaseSpot();ClearPause();HasGoal=Pause=false;ChairGoal=null;Greeting=null;Visits.Clear();Home=null;if(View?.IsOwner()==true)Seats?.Stop();Next=Time.unscaledTime+3f;}
        internal bool Idle(float dt)
        {
            var zone=Leash?.SocialHome;
            if(View?.IsOwner()!=true||zone==null||!zone.Contains(transform.position)||MasterIdolSocialSeats.Battle(Ai,Creature)||MasterIdolPilotA.Get<MasterIdolWelcome>(this)?.IsPresenting==true)return false;
            if(!MasterIdolActivity.Near(transform.position,MasterIdolResidentCycleRules.ActivityRadius(Dormant))){MasterIdolActivity.FarIdle++;return Wait();}MasterIdolActivity.NearIdle++;
            if(MasterIdolResidentWork.Idle(Creature,dt))return true;
            if(Time.unscaledTime>=NextYield){NextYield=Time.unscaledTime+1f;if(MasterIdolResidentSpacing.MustYield(Creature,zone,HasGoal?Goal:transform.position,out bool worker)&&(!HasGoal||Kind!="greeting"&&Kind!="observe-work")){Seats?.Stop();Finish();Next=Time.unscaledTime;PathAttempts=0;if(ChoosePatrol(zone)){Kind="yield";HasGoal=true;Pause=false;End=Time.unscaledTime+25;PublishSpot(zone);MasterIdolResidentMetrics.Count("social.yield-start");}NextYield=Time.unscaledTime+12f;}}
            if(Seats?.Hold(dt)==true)return true;
            float now=Time.unscaledTime;
            if(HasGoal)
            {
                if(now>=End||Greeting!=null&&(Greeting.IsDead()||Greeting.InPlaceMode()||Greeting.InAttack()||!zone.Contains(Greeting.transform.position)||(Greeting.transform.position-Look).sqrMagnitude>16)){Finish();return Wait();}
                if(!Pause)
                {
                    if((transform.position-Goal).sqrMagnitude<2.25f)
                    {
                        Ai.StopMoving();Pause=true;
                        if(ChairGoal!=null){bool seated=Seats?.Begin(ChairGoal,zone)==true;MasterIdolResidentMetrics.Count(seated?"seat.begin-success":"seat.begin-denied");Finish();return true;}
                        End=now+UnityEngine.Random.Range(3f,7f);View.GetZDO().Set(PauseKey,(long)((ZNet.instance.GetTimeSeconds()+End-now)*1000));
                    }
                                        else
                    {
                        if(!ReferenceEquals(RouteHome,zone)){if(!SameCoverage(RouteHome,zone)){PathAttempts=0;if(!BuildRoute(Goal,zone)){Finish();return Wait();}}RouteHome=zone;}
                        while(RouteIndex<Route.Count&&(transform.position-Route[RouteIndex]).sqrMagnitude<1.44f)RouteIndex++;
                        var step=RouteIndex<Route.Count?Route[RouteIndex]:Goal;
                        if(!MasterIdolSocialRoutes.Inside(zone,transform.position,step,Array.Empty<Vector3>(),RouteRanges)){Finish();return Wait();}
                        Ai.MoveTo(dt,step,1.2f,false);return true;
                    }
                }
                Ai.StopMoving();Ai.LookAt(Look);return true;
            }
            if(now<Next)return Wait();
            Next=now+UnityEngine.Random.Range(5f,10f);
            PathAttempts=0;
            if(Greet(zone)||ObserveWork(zone)||ChooseLocal(zone)||ChoosePatrol(zone)){HasGoal=true;Pause=false;End=now+25f;PublishSpot(zone);MasterIdolResidentMetrics.Count("social.goal-start");return true;}
            return Wait();
        }
        private void PublishSpot(MasterIdolEffectZone zone){if(Kind=="greeting"||Kind=="observe-work"||Kind=="seat")return;SpotNonce=Guid.NewGuid().ToString("N");GetComponent<MasterIdolResidentWork>()?.Spot(zone.Id,SpotNonce,Goal,25,false,true);}
        private void ReleaseSpot(){if(SpotNonce!=null&&Home!=null){MasterIdolResidentSpacing.Remove(Creature,Home,SpotNonce);GetComponent<MasterIdolResidentWork>()?.Spot(Home,SpotNonce,Goal,0,false,false);}SpotNonce=null;}
        private void ClearPause(){if(View?.IsValid()==true&&View.IsOwner()&&View.GetZDO().GetLong(PauseKey,0)!=0)View.GetZDO().Set(PauseKey,0L);}
        private bool Wait(){Ai.StopMoving();return true;}
        private void Finish(){ReleaseSpot();ClearPause();HasGoal=Pause=false;ChairGoal=null;Greeting=null;Next=Time.unscaledTime+UnityEngine.Random.Range(6f,12f);}
        private bool Reachable(Vector3 point,MasterIdolEffectZone zone,bool socialEvent=false)
        {
            if(PathAttempts>=3||!zone.Contains(point)||ZoneSystem.instance==null||!ZoneSystem.instance.IsZoneLoaded(point)||!MasterIdolResidentSpacing.Allowed(Creature,zone,point,socialEvent))return false;
            ZoneSystem.instance.GetGroundData(ref point,out var normal,out var biome,out var area,out var heightmap);
            if(heightmap==null||normal.y<.65f||point.y<ZoneSystem.instance.m_waterLevel+.15f)return false;
                        foreach(var player in Player.GetAllPlayers())if(player!=null&&(player.transform.position-point).sqrMagnitude<6.25f)return false;
            if(Physics.CheckSphere(point+Vector3.up,.6f,LayerMask.GetMask("piece","static_solid"),QueryTriggerInteraction.Ignore))return false;
            if(!BuildRoute(point,zone))return false;
            Goal=point;return true;
        }
                        private static bool SameCoverage(MasterIdolEffectZone left,MasterIdolEffectZone right)
        {
            if(left==null||right==null||left.Id!=right.Id||left.Circles.Length!=right.Circles.Length)return false;
            for(int n=0;n<left.Circles.Length;n++)if(left.Circles[n].Radius!=right.Circles[n].Radius||(left.Circles[n].Point-right.Circles[n].Point).sqrMagnitude!=0)return false;
            return true;
        }
        private bool BuildRoute(Vector3 point,MasterIdolEffectZone zone)
        {
            if(!MasterIdolResidentBudget.Path())return false;if(++PathAttempts>3)return false;MasterIdolActivity.PathQueries++;Route.Clear();RouteIndex=0;RouteHome=zone;
            return Pathfinding.instance!=null&&MasterIdolTiming.Measure("social.path",()=>Pathfinding.instance.GetPath(transform.position,point,Route,Ai.m_pathAgentType,true,false,false)&&MasterIdolSocialRoutes.Inside(zone,transform.position,point,Route,RouteRanges));
        }
        private bool Greet(MasterIdolEffectZone zone)
        {
            // One elected Greydwarf per home prevents two owners greeting the same player.
            if(Utils.GetPrefabName(gameObject)!="Greydwarf"||zone.Residents.Where(x=>x.Value=="Greydwarf").Select(x=>x.Key).OrderBy(x=>x,StringComparer.Ordinal).FirstOrDefault()!=View.GetZDO().m_uid.ToString())return false;
            double now=ZNet.instance.GetTimeSeconds();var present=new HashSet<long>();Player candidate=null;
            foreach(var player in Player.GetAllPlayers())
            {
                if(player==null||player.IsDead()||!zone.Contains(player.transform.position)||!MasterIdolEffectZones.Allows(player.GetPlayerID(),player.transform.position))continue;
                long id=player.GetPlayerID();present.Add(id);
                if(!Visits.TryGetValue(id,out var visit)){visit=new Visit{Last=now,Present=true};Visits[id]=visit;continue;}
                bool returned=MasterIdolSocialRules.Returned(now,visit.Last,visit.Present);visit.Last=now;visit.Present=true;
                if(returned&&!player.InPlaceMode()&&!player.InAttack()&&candidate==null)candidate=player;
            }
            foreach(var pair in Visits)if(!present.Contains(pair.Key))pair.Value.Present=false;
            // Bounded history, no lifetime growth from rotating visitors.
            if(Visits.Count>64)foreach(long id in Visits.Where(x=>!x.Value.Present&&now-x.Value.Last>3600).Select(x=>x.Key).ToArray())Visits.Remove(id);
            if(candidate==null)return false;
            Look=candidate.transform.position;if(!Reachable(Look+candidate.transform.right*3f-candidate.transform.forward,zone,true))return false;Kind="greeting";Greeting=candidate;return true;
        }
                private bool ObserveWork(MasterIdolEffectZone zone)
        {
            if(UnityEngine.Random.value>.15f||Utils.GetPrefabName(gameObject)!="Greydwarf"||zone.Residents.Where(x=>x.Value=="Greydwarf").Select(x=>x.Key).OrderBy(x=>x,StringComparer.Ordinal).FirstOrDefault()!=View.GetZDO().m_uid.ToString())return false;
            foreach(var player in Player.GetAllPlayers())
            {
                if(player==null||player.IsDead()||!player.InPlaceMode()||player.InAttack()||!zone.Contains(player.transform.position)||(player.transform.position-transform.position).sqrMagnitude>144||!MasterIdolEffectZones.Allows(player.GetPlayerID(),player.transform.position))continue;
                Look=player.transform.position;if(Reachable(Look+player.transform.right*4f,zone,true)){Kind="observe-work";return true;}
            }
            return false;
        }
        private bool ChooseLocal(MasterIdolEffectZone zone)
        {
            string family=Utils.GetPrefabName(gameObject);if(!MasterIdolResidentBudget.Scan())return false;int count=Physics.OverlapSphereNonAlloc(transform.position,16f,Nearby,~0,QueryTriggerInteraction.Ignore);
            int start=count==0?0:UnityEngine.Random.Range(0,count);
            for(int n=0;n<count;n++)
            {
                var collider=Nearby[(start+n)%count];if(collider==null)continue;
                var chair=collider.GetComponentInParent<Chair>();
                if(chair==null){var furniture=collider.GetComponentInParent<ZNetView>();var chairs=furniture?.GetComponentsInChildren<Chair>();if(chairs!=null&&chairs.Length>0&&chairs.Length<=16)chair=chairs[UnityEngine.Random.Range(0,chairs.Length)];}
                if((family=="Greyling"||family=="Greydwarf")&&Seats?.CanBegin(chair,zone)==true&&UnityEngine.Random.value<.25f)
                {Look=chair.m_attachPoint.position+chair.m_attachPoint.forward*3f;if(Reachable(chair.m_attachPoint.position+chair.m_attachPoint.forward,zone,true)){Kind="seat";ChairGoal=chair;return true;}}
                var fire=collider.GetComponentInParent<Fireplace>();
                if(fire!=null&&UnityEngine.Random.value<.15f){Look=fire.transform.position;if(Reachable(Look+(transform.position-Look).normalized*3f,zone)){Kind="fire-rest";return true;}}
                var station=collider.GetComponentInParent<CraftingStation>();
                if(station!=null&&UnityEngine.Random.value<.2f){Look=station.transform.position;if(Reachable(Look+station.transform.right*3f,zone)){Kind="station-inspect";return true;}}
                var other=collider.GetComponentInParent<MasterIdolSocial>();
                if(other!=null&&other!=this&&(other.Pause||other.Seats?.Active==true||other.View?.GetZDO()?.GetLong(PauseKey,0)>ZNet.instance.GetTimeSeconds()*1000)&&other.Leash?.SocialHome?.Id==zone.Id&&UnityEngine.Random.value<.05f){Look=other.transform.position;if(Reachable(Look+(transform.position-Look).normalized*3f,zone)){Kind="resident-observe";return true;}}
            }
            return false;
        }
        private bool ChoosePatrol(MasterIdolEffectZone zone)
        {
            for(int n=0;n<3;n++)
            {
                var circle=zone.Circles[UnityEngine.Random.Range(0,zone.Circles.Length)];float angle=(MasterIdolResidentSpacing.Rank(Creature,zone)*.125f+UnityEngine.Random.Range(-.04f,.04f)+n*.21f)*Mathf.PI*2f;
                float radius=circle.Radius*UnityEngine.Random.Range(.55f,.85f);var point=circle.Point+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                Look=point+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*4f;
                if(Reachable(point,zone)){Kind="patrol";return true;}
            }
            return false;
        }
        internal string Describe()=>"social kind="+Kind+" nextIn="+Mathf.Max(0,Next-Time.unscaledTime)+" sleep="+Ai.IsSleeping()+" work="+MasterIdolResidentWork.Busy(Creature)+" home="+(Home??"none")+" goal="+HasGoal+" observing="+Pause+" greeting="+(Greeting!=null)+" seat="+(Seats?.Active==true)+" dormant="+Dormant+" nearPlayer="+MasterIdolActivity.Near(transform.position,MasterIdolResidentCycleRules.ActivityRadius(Dormant))+" alert="+Ai.IsAlerted()+" hurtAge="+Ai.m_timeSinceHurt+" routeCorner="+RouteIndex+"/"+Route.Count;
        private void OnDisable()=>Reset();private void OnDestroy()=>Reset();
    }
    [HarmonyPatch(typeof(BaseAI),"Awake")]
    internal static class MasterIdolSocialAttachPatch
    {
        private static void Postfix(BaseAI __instance)
        {
            if(MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject))==0)return;
            if(__instance.GetComponent<MasterIdolSocialSeats>()==null)__instance.gameObject.AddComponent<MasterIdolSocialSeats>();
            if(__instance.GetComponent<MasterIdolSocial>()==null)__instance.gameObject.AddComponent<MasterIdolSocial>();
        }
    }
    [HarmonyPatch(typeof(BaseAI),"IdleMovement")]
    internal static class MasterIdolSocialIdlePatch
    { [HarmonyPriority(Priority.Low)] private static bool Prefix(BaseAI __instance,float __0)=>MasterIdolPilotA.Get<MasterIdolWelcome>(__instance)?.IsPresenting==true||MasterIdolPilotA.Get<MasterIdolSocial>(__instance)?.Idle(__0)!=true; }
}















