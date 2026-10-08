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
        private Rigidbody Body;private bool OriginalKinematic,Applied;private Chair AssignmentChair;private uint AssignmentKey;private string ActorId;private string LastSeat;private long PoseOwner,BlockedEnd;
        private void Awake(){Creature=GetComponent<Character>();Ai=GetComponent<BaseAI>();View=GetComponent<ZNetView>();Animator=GetComponentInChildren<Animator>();Body=GetComponent<Rigidbody>();}
        internal static bool Taken(Chair chair)=>chair!=null&&Occupied.TryGetValue(chair,out var actor)&&actor!=null&&actor.Applied;
        internal bool Active=>Applied;
        internal static bool Battle(BaseAI ai,Character character)=>ai==null||character==null||character.IsDead()||character.InAttack()||character.IsStaggering()||ai.IsAlerted()||ai is MonsterAI m&&(m.m_targetCreature!=null||m.m_targetStatic!=null);
        private void Update()
        {
            if(View?.IsValid()!=true){Restore();return;}
            long lease=View.GetZDO().GetLong(EndKey,0);
            if(lease==0&&!Applied)return; // No furniture/ward lookup for an actor without a seat lease.
            if(lease!=0&&lease==BlockedEnd){Restore();return;}
            if(Applied&&PoseOwner!=View.GetZDO().GetOwner()){BlockedEnd=lease;Stop();return;}
            var home=GetComponent<MasterIdolLeash>()?.SocialHome;
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
        internal static bool Supported(Chair chair)=>chair!=null&&chair.m_attachPoint!=null&&!chair.m_inShip&&chair.GetComponentInParent<Rigidbody>()==null&&chair.GetComponentInParent<ZNetView>()?.IsValid()==true;
        internal bool Begin(Chair chair,MasterIdolEffectZone home)
        {
            if(View?.IsOwner()!=true||!Supported(chair)||Battle(Ai,Creature)||chair.IsInUse()||Taken(chair)||!home.Contains(chair.transform.position))return false;
            string family=Utils.GetPrefabName(gameObject);if(family!="Greyling"&&family!="Greydwarf")return false;
            var furniture=chair.GetComponentInParent<ZNetView>();var zdo=furniture.GetZDO();int slot=Array.IndexOf(MasterIdolProps.Chairs(furniture),chair);if(slot<0||slot>=16)return false;
            
            // Stable disjoint assignment across NPC owners. Overlapping homes use the same canonical zone.
            if(MasterIdolEffectZones.At("BlackForest",chair.transform.position)?.Id!=home.Id||!MayUse(chair,home))return false;
            Seat=chair;LastSeat=zdo.m_uid.ToString();
            View.GetZDO().Set(SeatKey,LastSeat);View.GetZDO().Set(SlotKey,slot);View.GetZDO().Set(EndKey,(long)((ZNet.instance.GetTimeSeconds()+UnityEngine.Random.Range(12f,25f))*1000));
            return true;
        }
        internal bool Hold(float dt){if(!Applied)return false;if(Battle(Ai,Creature)){Stop();return false;}Ai.StopMoving();return true;}
        internal void Stop(){if(View?.IsValid()==true&&View.IsOwner())Clear();Restore();}
        private void Clear(){if(View.GetZDO().GetLong(EndKey,0)/1000d!=0)View.GetZDO().Set(EndKey,0L);}
                private bool MayUse(Chair chair,MasterIdolEffectZone home)
        {
            if(MasterIdolEffectZones.At("BlackForest",chair.transform.position)?.Id!=home.Id)return false;
            string uid=ActorId??(ActorId=View.GetZDO().m_uid.ToString());int count=0,rank=0;bool found=false;
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
            return found&&MasterIdolSocialRules.AssignedSeat(AssignmentKey,rank,count);
        }
        private bool Apply()
        {
            if(Taken(Seat)||Player.GetClosestPlayer(Seat.m_attachPoint.position,.05f)!=null)return false;
            // Native Greydwarf controller contains George Vibing and shares the audited Greyling Avatar.
            var donor=ZNetScene.instance?.GetPrefab("Greydwarf")?.GetComponentInChildren<Animator>();
            if(Animator!=null&&(donor==null||donor.runtimeAnimatorController==null))return false;
            OriginalController=Animator?.runtimeAnimatorController;
            if(Animator!=null){Animator.runtimeAnimatorController=donor.runtimeAnimatorController;Animator.SetBool("george_vibing",true);Animator.Play("Base Layer.George Vibing",0,0);}
            if(Body!=null){OriginalKinematic=Body.isKinematic;Body.isKinematic=true;}
            Occupied[Seat]=this;PoseOwner=View.GetZDO().GetOwner();Applied=true;return true;
        }
        private void Restore()
        {
            if(Applied)
            {
                if(Animator!=null){Animator.SetBool("george_vibing",false);Animator.runtimeAnimatorController=OriginalController;}
                                if(View?.IsValid()==true&&View.IsOwner()&&Seat!=null&&Seat.m_attachPoint!=null)
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






