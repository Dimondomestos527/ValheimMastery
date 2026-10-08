using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // No Player attachment spoof. Pose is transient ordinary ZDO data, read by nearby peers.
    internal sealed class MasterIdolSocialSeats:MonoBehaviour
    {
        private const string SeatKey="vm.idol.seat.v1",EndKey="vm.idol.seat.end.v1",SlotKey="vm.idol.seat.slot.v1";
        private static readonly Dictionary<Chair,MasterIdolSocialSeats> Occupied=new Dictionary<Chair,MasterIdolSocialSeats>();
        private Character Creature;private BaseAI Ai;private ZNetView View;private Chair Seat;
        private Animator Animator;private RuntimeAnimatorController OriginalController;
        private Rigidbody Body;private bool OriginalKinematic,Applied;private Chair AssignmentChair;private uint AssignmentKey;private string ActorId;private string LastSeat;private long PoseOwner,BlockedEnd;private ushort PoseRevision;private ZDO PoseData,IdentityData;private ZNet PoseSession;private ZDOMan PoseManager;private ulong PoseGeneration;private string LastDecision="not-attempted";private ZDO ObservedData;private long ObservedOwner;private ushort ObservedRevision;private ZNet ObservedSession;private ZDOMan ObservedManager;private ulong ObservedGeneration;private bool Observed;
        private void Awake(){Creature=GetComponent<Character>();Ai=GetComponent<BaseAI>();View=GetComponent<ZNetView>();Animator=GetComponentInChildren<Animator>();Body=GetComponent<Rigidbody>();}
        internal static bool Taken(Chair chair)=>chair!=null&&Occupied.TryGetValue(chair,out var actor)&&actor!=null&&actor.Applied;
        internal bool Active=>Applied;
        internal static bool Battle(BaseAI ai,Character character)=>ai==null||character==null||character.IsDead()||character.InAttack()||character.IsStaggering()||ai.IsAlerted()||ai is MonsterAI m&&(m.m_targetCreature!=null||m.m_targetStatic!=null);
        private void Update()
        {
            if(!ObserveContext()){Restore();return;}
            long lease=View.GetZDO().GetLong(EndKey,0);
            if(lease!=0&&lease==BlockedEnd){Restore();return;}
            if(Applied&&(PoseOwner!=View.GetZDO().GetOwner()||PoseRevision!=View.GetZDO().OwnerRevision||!ReferenceEquals(PoseData,View.GetZDO())||PoseSession!=ZNet.instance||PoseManager!=ZDOMan.instance||PoseGeneration!=MasterIdolLoadIdentity.Generation)){BlockedEnd=lease;Stop();return;}
            if(MasterIdolPilotA.Enabled&&lease==0&&!Applied){Restore();return;}
            var home=MasterIdolPilotA.Get<MasterIdolLeash>(this)?.SocialHome;
            bool valid=MasterIdolSocialRules.SeatLease(ZNet.instance.GetTimeSeconds(),View.GetZDO().GetLong(EndKey,0)/1000d,home!=null,Battle(Ai,Creature));
            if(!valid){if(View.IsOwner())Clear();Restore();return;}
            string id=View.GetZDO().GetString(SeatKey,"");
            if(id!=LastSeat||Seat==null)
            {
                Restore();LastSeat=id;
                if(!MasterIdolNetworkQuota.TryUid(id,out long user,out uint number))return;
                var root=ZNetScene.instance?.FindInstance(new ZDOID(user,number));var chairs=MasterIdolProps.Chairs(root?.GetComponent<ZNetView>());int slot=View.GetZDO().GetInt(SlotKey,0);Seat=chairs!=null&&slot>=0&&slot<chairs.Length?chairs[slot]:null;
            }
            if(!Supported(Seat)||!home.Contains(Seat.transform.position)||!MayUse(Seat,home)){if(View.IsOwner())Clear();Restore();return;}
            if(!Applied&&!Apply()){if(View.IsOwner())Clear();return;}
            if(View.IsOwner()){Ai.StopMoving();transform.SetPositionAndRotation(Seat.m_attachPoint.position,Seat.m_attachPoint.rotation);if(Body!=null){Body.position=transform.position;Body.rotation=transform.rotation;}}
        }
        private bool ObserveContext(){if(View?.IsValid()!=true||ZNet.instance?.GetWorld()==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(View.GetZDO().m_uid),View.GetZDO()))return false;var data=View.GetZDO();bool changed=!Observed||!ReferenceEquals(ObservedData,data)||ObservedOwner!=data.GetOwner()||ObservedRevision!=data.OwnerRevision||ObservedSession!=ZNet.instance||ObservedManager!=ZDOMan.instance||ObservedGeneration!=MasterIdolLoadIdentity.Generation;if(changed){long lease=data.GetLong(EndKey,0);if(lease!=0){BlockedEnd=lease;if(View.IsOwner())data.Set(EndKey,0L);LastDecision="stale-context-lease";MasterIdolResidentMetrics.Count("seat.denied.stale-context");}Restore();Observed=true;ObservedData=data;ObservedOwner=data.GetOwner();ObservedRevision=data.OwnerRevision;ObservedSession=ZNet.instance;ObservedManager=ZDOMan.instance;ObservedGeneration=MasterIdolLoadIdentity.Generation;}return true;}
        internal static bool Supported(Chair chair)=>chair!=null&&chair.m_attachPoint!=null&&!chair.m_inShip&&chair.GetComponentInParent<Rigidbody>()==null&&chair.GetComponentInParent<ZNetView>()?.IsValid()==true;
        internal bool Begin(Chair chair,MasterIdolEffectZone home)
        {
            if(!CanBegin(chair,home))return false;
            string family=Utils.GetPrefabName(gameObject);if(family!="Greyling"&&family!="Greydwarf")return false;
            var furniture=chair.GetComponentInParent<ZNetView>();var zdo=furniture.GetZDO();int slot=Array.IndexOf(MasterIdolProps.Chairs(furniture),chair);if(slot<0||slot>=16)return false;
            
            // Stable disjoint assignment across NPC owners. Overlapping homes use the same canonical zone.
            if(MasterIdolEffectZones.At("BlackForest",chair.transform.position)?.Id!=home.Id||!MayUse(chair,home))return false;
            Seat=chair;LastSeat=zdo.m_uid.ToString();
            View.GetZDO().Set(SeatKey,LastSeat);View.GetZDO().Set(SlotKey,slot);View.GetZDO().Set(EndKey,(long)(MasterIdolSocialRules.SeatEnd(ZNet.instance.GetTimeSeconds(),UnityEngine.Random.Range(12f,25f))*1000));LastDecision="lease-started";
            return true;
        }
        internal bool CanBegin(Chair chair,MasterIdolEffectZone home){string reason=!ObserveContext()||!View.IsOwner()?"not-owner":home==null?"no-home":!Supported(chair)?"unsupported":Battle(Ai,Creature)?"battle":chair.IsInUse()||Taken(chair)?"occupied":!home.Contains(chair.transform.position)?"outside":Utils.GetPrefabName(gameObject)!="Greyling"&&Utils.GetPrefabName(gameObject)!="Greydwarf"?"family":!MasterIdolSeatRosterGuard.Stable(home)?"roster-settling":!MasterIdolSocialRules.SeatStartWindow(ZNet.instance.GetTimeSeconds())?"turn-gap":!MayUse(chair,home)?"other-turn":null;if(reason==null)return true;LastDecision=reason;MasterIdolResidentMetrics.Count("seat.denied."+reason);return false;}
        internal string Describe()=>"seat active="+Applied+" decision="+LastDecision+" turn="+MasterIdolSocialRules.SeatTurn(ZNet.instance?.GetTimeSeconds()??0)+" remaining="+Math.Max(0,(View?.GetZDO()?.GetLong(EndKey,0)??0)/1000d-(ZNet.instance?.GetTimeSeconds()??0));
        internal bool Hold(float dt){if(!Applied)return false;if(Battle(Ai,Creature)){Stop();return false;}Ai.StopMoving();return true;}
        internal void Stop(){if(View?.IsValid()==true&&View.IsOwner())Clear();Restore();}
        private void Clear(){if(View.GetZDO().GetLong(EndKey,0)/1000d!=0)View.GetZDO().Set(EndKey,0L);}
                private bool MayUse(Chair chair,MasterIdolEffectZone home)
        {
            if(MasterIdolEffectZones.At("BlackForest",chair.transform.position)?.Id!=home.Id)return false;
            if(!ReferenceEquals(IdentityData,View.GetZDO())){IdentityData=View.GetZDO();ActorId=IdentityData.m_uid.ToString();AssignmentChair=null;}string uid=ActorId;int count=0,rank=0;bool found=false;
            foreach(var member in home.Residents)
            {
                if(member.Value!="Greyling"&&member.Value!="Greydwarf")continue;
                count++;if(member.Key==uid)found=true;else if(string.CompareOrdinal(member.Key,uid)<0)rank++;
            }
                        if(AssignmentChair!=chair)
            {
                var furniture=chair.GetComponentInParent<ZNetView>();int slot=Array.IndexOf(MasterIdolProps.Chairs(furniture),chair);if(slot<0||slot>=16)return false;
                AssignmentChair=chair;AssignmentKey=unchecked(furniture.GetZDO().m_uid.ID*31+(uint)slot);
            }
            return found&&MasterIdolSocialRules.AssignedSeatAt(AssignmentKey,rank,count,ZNet.instance.GetTimeSeconds());
        }
        private bool Apply()
        {
            if(Taken(Seat)||Player.GetClosestPlayer(Seat.m_attachPoint.position,.05f)!=null){LastDecision="apply-occupied";MasterIdolResidentMetrics.Count("seat.denied.apply-occupied");return false;}
            // Native Greydwarf controller contains George Vibing and shares the audited Greyling Avatar.
            var donor=ZNetScene.instance?.GetPrefab("Greydwarf")?.GetComponentInChildren<Animator>();
            if(Animator==null||donor==null||donor.runtimeAnimatorController==null){LastDecision="animator-unavailable";MasterIdolResidentMetrics.Count("seat.denied.animator-unavailable");return false;}
            OriginalController=Animator?.runtimeAnimatorController;
            if(Animator!=null){Animator.runtimeAnimatorController=donor.runtimeAnimatorController;Animator.SetBool("george_vibing",true);Animator.Play("Base Layer.George Vibing",0,0);}
            if(Body!=null){OriginalKinematic=Body.isKinematic;Body.isKinematic=true;}
            Occupied[Seat]=this;PoseOwner=View.GetZDO().GetOwner();PoseRevision=View.GetZDO().OwnerRevision;PoseData=View.GetZDO();PoseSession=ZNet.instance;PoseManager=ZDOMan.instance;PoseGeneration=MasterIdolLoadIdentity.Generation;Applied=true;LastDecision="applied";MasterIdolResidentMetrics.Count("seat.pose-applied");return true;
        }
        private void Restore()
        {
            if(Applied)
            {
                if(Animator!=null){Animator.SetBool("george_vibing",false);Animator.runtimeAnimatorController=OriginalController;}
                                if(View?.IsValid()==true&&View.IsOwner()&&ReferenceEquals(PoseData,View.GetZDO())&&PoseOwner==View.GetZDO().GetOwner()&&PoseRevision==View.GetZDO().OwnerRevision&&Seat!=null&&Seat.m_attachPoint!=null)
                {
                    transform.position=Seat.m_attachPoint.position+Seat.m_attachPoint.rotation*Seat.m_detachOffset;
                    if(Body!=null)Body.position=transform.position;
                }
                if(Body!=null){Body.isKinematic=OriginalKinematic;if(!OriginalKinematic)Body.linearVelocity=Vector3.zero;}
                if(!ReferenceEquals(Seat,null)&&Occupied.TryGetValue(Seat,out var owner)&&owner==this)Occupied.Remove(Seat);
            }
            Applied=false;OriginalController=null;Seat=null;LastSeat=null;
        }
                private void LateUpdate(){if(Applied&&Animator!=null){Animator.SetBool("george_vibing",true);Animator.SetFloat("forward_speed",0);Animator.SetFloat("turn_speed",0);}}
        private void OnDisable()=>Stop();private void OnDestroy()=>Stop();
    }
    [HarmonyPatch(typeof(Chair),"IsInUse")]
    internal static class MasterIdolSeatOccupiedPatch
    {private static void Postfix(Chair __instance,ref bool __result){if(!__result)__result=MasterIdolSocialSeats.Taken(__instance);}}
}






