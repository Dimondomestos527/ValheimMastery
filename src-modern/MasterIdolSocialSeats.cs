using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Native walking/physics; native humanoid chair pose affects its owned representation only.
    internal sealed class MasterIdolSocialSeats:MonoBehaviour
    {
        private const string SeatKey="vm.idol.seat.v2",SlotKey="vm.idol.seat.slot.v2",StartKey="vm.idol.seat.start.v2",OwnerKey="vm.idol.seat.owner.v2",RevisionKey="vm.idol.seat.revision.v2";
        private const float RestSeconds=120f;
        private static readonly Dictionary<Chair,MasterIdolSocialSeats> Occupied=new Dictionary<Chair,MasterIdolSocialSeats>();
        private sealed class HomeClock{internal float Next,Keep;internal MasterIdolSocialSeats Actor;}
        private static readonly Dictionary<string,HomeClock> Homes=new Dictionary<string,HomeClock>(StringComparer.Ordinal);
        private static ZNet GlobalNet;private static ZDOMan GlobalManager;private static ulong GlobalGeneration;
        private readonly List<MasterIdolProp> Props=new List<MasterIdolProp>(32);
        private readonly List<Vector3> Route=new List<Vector3>(32);
        private readonly List<MasterIdolSocialRoutes.Interval> Ranges=new List<MasterIdolSocialRoutes.Interval>(32);
        private Character Creature;private BaseAI Ai;private ZNetView View,Furniture;private Chair Seat;
        private ZDO Data,SeatData;private ZNet Net;private ZDOMan Manager;private ulong Generation;private long Owner,World,Started,Blocked;
        private ushort Revision;private string Home;private Vector3 Goal,SeatPoint,LeftFloor,RightFloor,SeatPosition;private Quaternion SeatRotation;
        private Collider SeatSupport,LeftSupport,RightSupport;private MasterIdolNativeSeatPose Pose;private HomeClock Clock;
        private bool Walking,Applied,GraphicsLease;private long GraphicsEpoch;private float WalkUntil,RestUntil,NextObserve,NextAttempt;private int Step;private string Decision="idle";
        private void Awake(){Creature=GetComponent<Character>();Ai=GetComponent<BaseAI>();View=GetComponent<ZNetView>();}
        internal static void Attach(Character actor)
        {if(actor!=null&&MasterIdolForestGuardState.ModuleBorn(actor.m_nview?.GetZDO())&&actor.GetComponent<MasterIdolSocialSeats>()==null)actor.gameObject.AddComponent<MasterIdolSocialSeats>();}
        internal bool Active=>Walking||Applied;
        internal static bool Battle(BaseAI ai,Character character)=>ai==null||character==null||character.IsDead()||character.InAttack()||character.IsStaggering()||ai.IsAlerted()||ai is MonsterAI m&&(m.m_targetCreature!=null||m.m_targetStatic!=null);
        internal static bool Supported(Chair chair)=>chair!=null&&chair.m_attachPoint!=null&&!chair.m_inShip&&chair.GetComponentInParent<Rigidbody>()==null&&chair.GetComponentInParent<ZNetView>()?.IsValid()==true;
        // Exact native Chair.IsInUse predicate, bypassing only our own occupancy postfix.
        private static bool NativePlayer(Chair chair)=>chair==null||chair.m_attachPoint==null||Player.GetAllPlayers().Count>128||Player.GetClosestPlayer(chair.m_attachPoint.position,.05f)!=null;
        internal static bool Taken(Chair chair)=>chair!=null&&(Occupied.TryGetValue(chair,out var actor)&&actor!=null&&actor.Active||OtherLease(chair,null));
        private static bool OtherLease(Chair chair,Character except)
        {
            var home=MasterIdolEffectZones.At("BlackForest",chair.transform.position);var furniture=chair.GetComponentInParent<ZNetView>();
            if(home==null||home.Residents.Count>4||furniture?.IsValid()!=true)return false;int slot=Array.IndexOf(MasterIdolProps.Chairs(furniture),chair);long now=ZNet.instance.GetTime().Ticks;
            foreach(var member in home.Residents)
            {
                if(!MasterIdolNetworkQuota.TryUid(member.Key,out long user,out uint id))continue;var data=ZDOMan.instance?.GetZDO(new ZDOID(user,id));long start=data?.GetLong(StartKey,0)??0;
                if(data==null||ReferenceEquals(except?.m_nview?.GetZDO(),data)||!MasterIdolForestGuardState.ModuleBorn(data)||!float.IsFinite(data.GetFloat(ZDOVars.s_health,0))||data.GetFloat(ZDOVars.s_health,0)<=0||start<=0||start>now||now-start>=120*TimeSpan.TicksPerSecond||
                    data.GetLong(OwnerKey,0)!=data.GetOwner()||data.GetInt(RevisionKey,-1)!=data.OwnerRevision)continue;
                if(data.GetString(SeatKey,"")==furniture.GetZDO().m_uid.ToString()&&data.GetInt(SlotKey,-1)==slot)return true;
            }
            return false;
        }
        private bool Assigned(Chair chair,MasterIdolEffectZone home)
        {
            int rank=0,count=0;bool found=false;string uid=Data.m_uid.ToString();foreach(var member in home.Residents){count++;if(member.Key==uid)found=true;else if(string.CompareOrdinal(member.Key,uid)<0)rank++;}
            var furniture=chair.GetComponentInParent<ZNetView>();int slot=Array.IndexOf(MasterIdolProps.Chairs(furniture),chair);
            return found&&count<=4&&slot>=0&&MasterIdolSocialRules.AssignedSeat(unchecked(furniture.GetZDO().m_uid.ID*31+(uint)slot),rank,count);
        }
        internal static Character Occupant(Chair chair,MasterIdolEffectZone home)
        {return home!=null&&Taken(chair)&&Occupied.TryGetValue(chair,out var actor)&&actor.Applied&&actor.Home==home.Id&&actor.Valid(home)?actor.Creature:null;}
        private static void GlobalContext()
        {
            if(GlobalNet==ZNet.instance&&GlobalManager==ZDOMan.instance&&GlobalGeneration==MasterIdolLoadIdentity.Generation)return;
            Homes.Clear();Occupied.Clear();GlobalNet=ZNet.instance;GlobalManager=ZDOMan.instance;GlobalGeneration=MasterIdolLoadIdentity.Generation;
        }
        private bool Observe()
        {
            GlobalContext();if(Creature==null||View?.IsValid()!=true||ZNet.instance?.GetWorld()==null||!MasterIdolLoadIdentity.LoadReady)return false;
            var current=View.GetZDO();if(current==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(current.m_uid),current)||!MasterIdolForestGuardState.ModuleBorn(current))return false;
            if(!ReferenceEquals(Data,current)||Net!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||Owner!=current.GetOwner()||Revision!=current.OwnerRevision||World!=ZNet.instance.GetWorldUID())
            {Stop();Data=current;Net=ZNet.instance;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;Owner=current.GetOwner();Revision=current.OwnerRevision;World=Net.GetWorldUID();Blocked=current.GetLong(StartKey,0);if(View.IsOwner()&&Blocked!=0)current.Set(StartKey,0L);}
            return true;
        }
        private bool Peace()=>!Battle(Ai,Creature)&&!Ai.IsSleeping()&&!Creature.InDodge()&&!Creature.InEmote()&&Ai.m_timeSinceHurt>=8&&!MasterIdolResidentWork.Busy(Creature)&&
            GetComponent<MasterIdolWelcome>()?.IsPresenting!=true&&GetComponent<MasterIdolResidentPresentation>()?.Playing!=true&&!MasterIdolResidentBirthPresentation.Playing(Creature)&&!MasterIdolResidentSceneAnimation.Playing(Creature);
        private bool Valid(MasterIdolEffectZone home)=>home?.Id==Home&&Peace()&&Supported(Seat)&&!NativePlayer(Seat)&&Furniture?.IsValid()==true&&ReferenceEquals(Furniture.GetZDO(),SeatData)&&
            ReferenceEquals(Manager?.GetZDO(SeatData.m_uid),SeatData)&&Furniture.transform.position==SeatPosition&&Furniture.transform.rotation==SeatRotation&&home.Contains(transform.position)&&home.Contains(SeatPoint)&&
            MasterIdolResidentPolicy.TryHome(Creature,out var assigned)&&assigned.Id==Home&&ReferenceEquals(View.GetZDO(),Data)&&Owner==Data.GetOwner()&&Revision==Data.OwnerRevision&&Net==ZNet.instance&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&World==Net.GetWorldUID();
        internal bool CanBegin(Chair chair,MasterIdolEffectZone home)=>Observe()&&View.IsOwner()&&!Active&&Supported(chair)&&home!=null&&home.Contains(chair.transform.position)&&Peace()&&MasterIdolResidentGrounding.Slow(Creature)&&!Taken(chair)&&!chair.IsInUse();
        internal bool Begin(Chair chair,MasterIdolEffectZone home)
        {
            if(!CanBegin(chair,home)||!Assigned(chair,home)||OtherLease(chair,Creature))return false;MasterIdolSocialScenes.Cancel(GetComponent<MasterIdolSocial>());
            MasterIdolNativeSeatPose pose=null;
            try
            {
                var furniture=chair.GetComponentInParent<ZNetView>();var seatData=furniture.GetZDO();
                if(!Surface(chair.m_attachPoint.position+Vector3.up*2,3,out var hit)||hit.collider.GetComponentInParent<ZNetView>()!=furniture)return false;
                Vector3 direction=chair.transform.forward;direction.y=0;direction.Normalize();var bounds=hit.collider.bounds;
                float reach=Creature.GetRadius()+Mathf.Abs(direction.x)*bounds.extents.x+Mathf.Abs(direction.z)*bounds.extents.z+.08f;
                if(!Surface(hit.point+direction*reach+Vector3.up*2,4,out var floor)||floor.collider==hit.collider||!home.Contains(floor.point)||ZoneSystem.instance?.IsZoneLoaded(floor.point)!=true||Pathfinding.instance==null||!MasterIdolResidentBudget.Path())return false;
                var goal=floor.point+Vector3.up*.05f;Route.Clear();
                if(!Pathfinding.instance.GetPath(transform.position,goal,Route,Ai.m_pathAgentType,true,false,false)||Route.Count>32||!MasterIdolSocialRoutes.Inside(home,transform.position,goal,Route,Ranges))return false;
                pose=MasterIdolNativeSeatPose.Prepare(Creature);if(pose==null){Decision="native-sit-not-ready";return false;}
                var matrix=Matrix4x4.TRS(goal,Quaternion.LookRotation(direction),transform.lossyScale);Vector3 shift=hit.point-matrix.MultiplyPoint3x4(pose.HipOffsets[0]);float limit=Creature.GetRadius()+.35f;
                if(!MasterIdolNativeSeatPose.Finite(shift)||Mathf.Abs(shift.y)>.5f||shift.x*shift.x+shift.z*shift.z>limit*limit||shift.sqrMagnitude>1)return false;
                for(int phase=0;phase<3;phase++)if(!FootSurface(matrix.MultiplyPoint3x4(pose.LeftOffsets[phase])+shift,out _)||!FootSurface(matrix.MultiplyPoint3x4(pose.RightOffsets[phase])+shift,out _))return false;
                Seat=chair;Furniture=furniture;SeatData=seatData;SeatPosition=furniture.transform.position;SeatRotation=furniture.transform.rotation;SeatPoint=hit.point;SeatSupport=hit.collider;
                Home=home.Id;Goal=goal;Pose=pose;pose=null;Occupied[Seat]=this;Walking=true;WalkUntil=Time.unscaledTime+20;Step=0;Decision="walking-to-chair";return true;
            }
            finally{pose?.Dispose();}
        }
        private static bool Surface(Vector3 start,float distance,out RaycastHit hit)=>Physics.Raycast(start,Vector3.down,out hit,distance,LayerMask.GetMask("piece","static_solid","terrain"),QueryTriggerInteraction.Ignore)&&hit.collider!=null&&hit.collider.enabled&&!hit.collider.isTrigger&&hit.collider.attachedRigidbody==null&&MasterIdolNativeSeatPose.Finite(hit.point)&&hit.normal.y>=.7f;
        private static bool FootSurface(Vector3 foot,out RaycastHit hit)=>Surface(foot+Vector3.up*.15f,.4f,out hit)&&foot.y-hit.point.y>=-.05f&&foot.y-hit.point.y<=.18f;
        internal static bool Idle(MasterIdolSocial social,MasterIdolEffectZone home,float dt)
        {
            var seat=social?.GetComponent<MasterIdolSocialSeats>();if(seat==null||!seat.Observe()||!seat.View.IsOwner())return false;if(seat.Active)return seat.Hold(dt);
            if(!seat.Peace()||!MasterIdolResidentGrounding.Slow(seat.Creature)||home.Residents.Count>4)return false;
            if(!Homes.TryGetValue(home.Id,out var clock))
            {
                if(Homes.Count>=64){var stale=new List<string>();foreach(var entry in Homes)if(entry.Value.Actor==null&&entry.Value.Keep<=Time.unscaledTime)stale.Add(entry.Key);foreach(var id in stale)Homes.Remove(id);}
                if(Homes.Count>=64)return false;Homes.Add(home.Id,clock=new HomeClock());
            }
            clock.Keep=Time.unscaledTime+180;
            if(clock.Actor!=null&&clock.Actor.Active||Time.unscaledTime<clock.Next||Time.unscaledTime<seat.NextAttempt)return false;
            seat.NextAttempt=Time.unscaledTime+10;seat.Clock=clock;MasterIdolProps.Nearby(seat.transform.position,12,seat.Props);
            int checkedChairs=0;
            foreach(var prop in seat.Props)
            {if(prop?.View?.IsValid()!=true)continue;var chairs=MasterIdolProps.Chairs(prop.View);if(chairs==null)continue;foreach(var chair in chairs){if(++checkedChairs>4)return false;if(seat.Begin(chair,home)){clock.Actor=seat;return true;}}}
            seat.Decision="no-reachable-supported-native-sit";return false;
        }
        internal bool Hold(float dt)
        {
            if(!Observe()||!View.IsOwner()||!MasterIdolResidentPolicy.TryHome(Creature,out var home)||!Valid(home)){Stop();return false;}
            if(Applied){if(Time.unscaledTime>=RestUntil||!MasterIdolResidentGrounding.Slow(Creature)){Stop();return false;}Ai.StopMoving();Ai.LookAt(transform.position+Seat.transform.forward*10);return true;}
            if(!Walking)return false;if(Time.unscaledTime>=WalkUntil||!MasterIdolSocialRoutes.Inside(home,transform.position,Goal,Route,Ranges)){Stop();return false;}
            while(Step<Route.Count&&(transform.position-Route[Step]).sqrMagnitude<.04f)Step++;Vector3 delta=transform.position-Goal;delta.y=0;
            if(delta.sqrMagnitude>.04f){Ai.MoveTo(dt,Step<Route.Count?Route[Step]:Goal,.15f,false);return true;}
            Ai.StopMoving();Ai.LookAt(transform.position+Seat.transform.forward*10);if(!MasterIdolResidentGrounding.Slow(Creature)||Vector3.Angle(transform.forward,Seat.transform.forward)>10)return true;
            if(!Commit()){Stop();return false;}return true;
        }
        private bool Commit()
        {
            if(!MasterIdolResidentPolicy.TryHome(Creature,out var home)||NativePlayer(Seat)||!Assigned(Seat,home)||OtherLease(Seat,Creature))return false;
            if(Pose==null||!Pose.Align(SeatPoint,out var shift)||!FootSurface(Pose.Foot(0,true,shift),out var left)||!FootSurface(Pose.Foot(0,false,shift),out var right))return false;
            if(!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null){if(!MasterIdolVisibleGesture.ReserveSeat(out GraphicsEpoch))return false;GraphicsLease=true;}
            if(!Pose.Apply(shift)||!Pose.Contact(SeatPoint,left.point,right.point))return false;
            LeftFloor=left.point;RightFloor=right.point;LeftSupport=left.collider;RightSupport=right.collider;Walking=false;Applied=true;RestUntil=Time.unscaledTime+RestSeconds;
            Started=Net.GetTime().Ticks;
            try{Data.Set(SeatKey,SeatData.m_uid.ToString());Data.Set(SlotKey,Array.IndexOf(MasterIdolProps.Chairs(Furniture),Seat));Data.Set(OwnerKey,Owner);Data.Set(RevisionKey,(int)Revision);Data.Set(StartKey,Started);Decision="native-chair-sit-120s";return true;}
            catch(Exception){MasterIdolResidentMetrics.Count("seat.metadata-commit-failed");return false;}
        }
        private void Update()
        {
            if(!Observe()){Stop();return;}
            if(Active){if(!MasterIdolResidentPolicy.TryHome(Creature,out var home)||!Valid(home)||Applied&&(Data.GetLong(StartKey,0)!=Started||!MasterIdolResidentGrounding.Slow(Creature)||Time.unscaledTime>=RestUntil)){Stop();return;}if(Applied&&View.IsOwner()){Ai.StopMoving();Ai.LookAt(transform.position+Seat.transform.forward*10);}return;}
            if(View.IsOwner()||Time.unscaledTime<NextObserve)return;NextObserve=Time.unscaledTime+.2f;long start=Data.GetLong(StartKey,0);
            if(start<=0||start==Blocked||Data.GetLong(OwnerKey,0)!=Owner||Data.GetInt(RevisionKey,-1)!=Revision)return;Blocked=start;
            float age=(float)((Net.GetTime().Ticks-start)/(double)TimeSpan.TicksPerSecond);if(age<0||age>=RestSeconds)return;
            if(!MasterIdolNetworkQuota.TryUid(Data.GetString(SeatKey,""),out long user,out uint number))return;
            Furniture=ZNetScene.instance?.FindInstance(new ZDOID(user,number))?.GetComponent<ZNetView>();var chairs=MasterIdolProps.Chairs(Furniture);int slot=Data.GetInt(SlotKey,-1);Seat=chairs!=null&&slot>=0&&slot<chairs.Length?chairs[slot]:null;
            if(!Supported(Seat)||!MasterIdolResidentPolicy.TryHome(Creature,out var zone)||!Surface(Seat.m_attachPoint.position+Vector3.up*2,3,out var hit)||hit.collider.GetComponentInParent<ZNetView>()!=Furniture){Stop();return;}
            SeatData=Furniture.GetZDO();SeatPosition=Furniture.transform.position;SeatRotation=Furniture.transform.rotation;SeatPoint=hit.point;SeatSupport=hit.collider;Home=zone.Id;Pose=MasterIdolNativeSeatPose.Prepare(Creature);
            if(!Valid(zone)||!MasterIdolResidentGrounding.Slow(Creature)||Pose==null||!Pose.Align(SeatPoint,out var shift)||!FootSurface(Pose.Foot(0,true,shift),out var left)||!FootSurface(Pose.Foot(0,false,shift),out var right)||Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null||!MasterIdolVisibleGesture.ReserveSeat(out GraphicsEpoch)){Stop();return;}
            GraphicsLease=true;if(!Pose.Apply(shift)||!Pose.Contact(SeatPoint,left.point,right.point)){Stop();return;}
            LeftFloor=left.point;RightFloor=right.point;LeftSupport=left.collider;RightSupport=right.collider;Started=start;Applied=true;RestUntil=Time.unscaledTime+RestSeconds-age;Occupied[Seat]=this;
        }
        private void LateUpdate()
        {
            if(!Applied)return;if(SeatSupport==null||LeftSupport==null||RightSupport==null||!SeatSupport.enabled||!LeftSupport.enabled||!RightSupport.enabled||SeatSupport.bounds.SqrDistance(SeatPoint)>.01f||LeftSupport.bounds.SqrDistance(LeftFloor)>.01f||RightSupport.bounds.SqrDistance(RightFloor)>.01f||Pose?.Contact(SeatPoint,LeftFloor,RightFloor)!=true){Stop();return;}Pose.Hold();
        }
        internal void Stop()
        {
            Walking=Applied=false;
            try{if(View?.IsValid()==true&&View.IsOwner()&&ReferenceEquals(View.GetZDO(),Data)&&Data.GetOwner()==Owner&&Data.OwnerRevision==Revision&&Started>0&&Data.GetLong(StartKey,0)==Started)Data.Set(StartKey,0L);}
            catch(Exception){MasterIdolResidentMetrics.Count("seat.metadata-clear-failed");}
            Started=0;var pose=Pose;Pose=null;
            try{pose?.Dispose();}
            finally{if(GraphicsLease){GraphicsLease=false;MasterIdolVisibleGesture.ReleaseSeat(GraphicsEpoch);}if(Seat!=null&&Occupied.TryGetValue(Seat,out var value)&&value==this)Occupied.Remove(Seat);if(Clock?.Actor==this){Clock.Actor=null;Clock.Next=Time.unscaledTime+10;}Clock=null;Seat=null;Furniture=null;SeatData=null;Home=null;Route.Clear();Decision="native-restored";}
        }
        internal string Describe()=>"seat="+Decision+" walking="+Walking+" seated="+Applied+" remaining="+Math.Max(0,RestUntil-Time.unscaledTime);
        private void OnDisable()=>Stop();private void OnDestroy()=>Stop();
    }
    [HarmonyPatch(typeof(Chair),"IsInUse")]
    internal static class MasterIdolSeatOccupiedPatch
    {private static void Postfix(Chair __instance,ref bool __result){if(!__result)__result=MasterIdolSocialSeats.Taken(__instance);}}
}
