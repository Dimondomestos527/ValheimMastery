using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static partial class MasterIdolResidentCargo
    {
        internal static bool Peace(Character actor)
        {
            var ai=actor?.GetComponent<BaseAI>();return actor!=null&&ai!=null&&!MasterIdolSocialSeats.Battle(ai,actor)&&ai.m_timeSinceHurt>=8&&!ai.IsSleeping()&&!actor.InDodge()&&!actor.InEmote()&&
                !MasterIdolResidentBirthPresentation.FreshIdleHold(actor)&&!MasterIdolResidentBirthPresentation.Playing(actor)&&actor.GetComponent<MasterIdolWelcome>()?.IsPresenting!=true&&
                !MasterIdolResidentUniqueActions.Busy(actor)&&!MasterIdolResidentWork.Busy(actor)&&!MasterIdolResidentSceneAnimation.Playing(actor)&&actor.GetComponent<MasterIdolSocialSeats>()?.Active!=true;
        }
        internal static bool Busy(Character actor)
        {
            var data=actor?.m_nview?.GetZDO();if(!MasterIdolForestGuardState.ModuleBorn(data))return false;
            var cargo=ZDOMan.instance?.GetZDO(data.GetZDOID(Link));
            return Opening(data)||actor.GetComponent<MasterIdolResidentCargoAgent>()?.Working==true||PendingFor(data.m_uid)||
                cargo!=null&&(cargo.GetLong(OpenPeerKey,0)!=0||cargo.GetInt(ZDOVars.s_inUse,0)!=0);
        }
        private static bool Opening(ZDO actor)=>actor!=null&&ZNet.instance?.GetWorld()!=null&&actor.GetLong(OpeningKey,0)>ZNet.instance.GetTime().Ticks;
        internal static bool ManualBusy(Character actor)
        {
            var data=actor?.m_nview?.GetZDO();if(!MasterIdolForestGuardState.ModuleBorn(data))return false;
            var cargo=ZDOMan.instance?.GetZDO(data.GetZDOID(Link));return Opening(data)||cargo!=null&&(cargo.GetLong(OpenPeerKey,0)!=0||cargo.GetInt(ZDOVars.s_inUse,0)!=0);
        }
        internal static bool Cancel(Character actor)
        {
            var data=actor?.m_nview?.GetZDO();if(!MasterIdolForestGuardState.ModuleBorn(data))return true;
            if(actor.m_nview?.IsValid()!=true||!actor.m_nview.IsOwner())return false;
            if(Opening(data))return false;
            var cargo=ZDOMan.instance?.GetZDO(data.GetZDOID(Link));
            if(cargo!=null&&(cargo.GetLong(OpenPeerKey,0)!=0||cargo.GetInt(ZDOVars.s_inUse,0)!=0))return false;
            actor.GetComponent<MasterIdolResidentCargoAgent>()?.Stop();CancelExecutor(data.m_uid);data.Set(EpochKey,Guid.NewGuid().ToString("N"));
            Request(Kind.Cancel,data,ZDOID.None,Guid.NewGuid().ToString("N"));return true;
        }
        internal static bool Idle(Character actor,float dt)
        {
            if(actor?.m_nview?.IsOwner()==true&&ManualBusy(actor)&&Peace(actor)){actor.GetComponent<BaseAI>()?.StopMoving();return true;}
            return actor?.GetComponent<MasterIdolResidentCargoAgent>()?.Step(dt)==true;
        }
    }
    internal sealed class MasterIdolResidentCargoAgent:MonoBehaviour
    {
        private Character Actor;private BaseAI Ai;private ZNetView View;private ZDO Data;private long Owner;private ushort Revision;private ulong Generation;private string Home;
        private Component Target;private Vector3 TargetPoint;private string Nonce;private float Next,Until;private bool Sent;
        private readonly Collider[] Hits=new Collider[96];private readonly HashSet<ItemDrop> Seen=new HashSet<ItemDrop>();
        private readonly List<Vector3> Route=new List<Vector3>();private readonly List<MasterIdolSocialRoutes.Interval> Ranges=new List<MasterIdolSocialRoutes.Interval>();private int Corner;
        internal bool Working=>Target!=null;
        internal void Attach(Character actor){Actor=actor;Ai=actor.GetComponent<BaseAI>();View=actor.m_nview;}
        internal void Stop(){Target=null;Sent=false;Nonce=null;Route.Clear();Next=Time.unscaledTime+2;}
        private bool Current()
        {
            if(Actor==null||Ai==null||View?.IsValid()!=true||!View.IsOwner()||!MasterIdolResidentCargo.Context())return false;
            var data=View.GetZDO();string home=data.GetString(MasterIdolNativeTaming.HomeKey,"");
            if(!ReferenceEquals(Data,data)||Owner!=data.GetOwner()||Revision!=data.OwnerRevision||Generation!=MasterIdolLoadIdentity.Generation||Home!=home)
            {Stop();Data=data;Owner=data.GetOwner();Revision=data.OwnerRevision;Generation=MasterIdolLoadIdentity.Generation;Home=home;}
            return MasterIdolForestGuardState.ModuleBorn(Data)&&MasterIdolResidentPolicy.TryHome(Actor,out _);
        }
        private void Update()
        {
            if(!Current()){Stop();return;}
            if(!MasterIdolResidentCargo.Peace(Actor)||MasterIdolResidentCargo.ManualBusy(Actor)||!MasterIdolActivity.Near(transform.position)){Stop();return;}
            var data=ZDOMan.instance.GetZDO(Data.GetZDOID(MasterIdolResidentCargo.Link));var cargo=MasterIdolResidentCargo.ContainerFor(data);
            if(cargo==null||data.GetZDOID(MasterIdolResidentCargo.BodyKey)!=Data.m_uid||data.GetLong(MasterIdolResidentCargo.OpenPeerKey,0)!=0||data.GetInt(ZDOVars.s_inUse,0)!=0){Stop();return;}
            if(Target!=null){if(Time.unscaledTime>Until||Target==null||(Target.transform.position-TargetPoint).sqrMagnitude>2.25f)Stop();return;}
            if(Time.unscaledTime<Next||!MasterIdolResidentBudget.Scan())return;Next=Time.unscaledTime+2;cargo.Load();
            var zone=MasterIdolEffectZones.Find(Home);if(zone==null)return;
            // Deposit first when a usable registered destination exists; a full
            // or unavailable destination never discards or prevents safe carry.
            Container destination=ResidentCargoContainers.Nearest(Actor,zone,cargo.GetInventory());
            if(destination!=null&&cargo.GetInventory().NrOfItems()>0&&Plan(destination,zone))return;
            int count=Physics.OverlapSphereNonAlloc(transform.position,12,Hits,LayerMask.GetMask("item"),QueryTriggerInteraction.Ignore);Seen.Clear();ItemDrop best=null;float distance=float.MaxValue;
            for(int i=0;i<count;i++)
            {
                var item=Hits[i]?.GetComponentInParent<ItemDrop>();Hits[i]=null;
                if(item==null||!Seen.Add(item)||item.m_nview?.IsValid()!=true||item.m_itemData?.m_shared==null||!zone.Contains(item.transform.position)||!ResidentCargoTransfer.CanFit(cargo.GetInventory(),item.m_itemData))continue;
                float d=(item.transform.position-transform.position).sqrMagnitude;if(d<distance){distance=d;best=item;}
            }
            if(best!=null)Plan(best,zone);
        }
        private bool Plan(Component target,MasterIdolEffectZone zone)
        {
            if(Pathfinding.instance==null||!MasterIdolResidentBudget.Path())return false;
            Vector3 delta=transform.position-target.transform.position;delta.y=0;if(delta.sqrMagnitude<.01f)delta=Vector3.forward;
            Vector3 goal=target.transform.position+delta.normalized*1.5f;Route.Clear();
            if(!zone.Contains(goal)||!Pathfinding.instance.GetPath(transform.position,goal,Route,Ai.m_pathAgentType,true,false,false)||Route.Count>64||!MasterIdolSocialRoutes.Inside(zone,transform.position,goal,Route,Ranges))return false;
            float length=0;Vector3 last=transform.position;foreach(var point in Route){length+=Vector3.Distance(last,point);last=point;}if(length>128)return false;
            // Owner-local movement intent, not permission to transfer any item.
            // A real usable route exists; an empty scan never interrupts dance.
            var pin=new MasterIdolResidentCargo.DancePin(Actor);var savedData=Data;var savedView=View;var savedAi=Ai;
            long savedOwner=Owner;ushort savedRevision=Revision;ulong savedGeneration=Generation;string savedHome=Home;
            var targetView=target is ItemDrop drop?drop.m_nview:target is Container box?box.m_nview:null;var targetData=targetView?.GetZDO();
            if(!pin.Current()||targetView?.IsValid()!=true||targetData==null||!ReferenceEquals(ZDOMan.instance.GetZDO(targetData.m_uid),targetData)||
                !MasterIdolResidentCargo.Peace(Actor)||MasterIdolResidentCargo.ManualBusy(Actor)||!MasterIdolActivity.Near(transform.position))return false;
            Vector3 savedPoint=target.transform.position;int corners=Route.Count;
            MasterIdolResidentDance.Cancel(Actor,"cargo.route");
            if(!pin.Current()||!Current()||!ReferenceEquals(Data,savedData)||!ReferenceEquals(View,savedView)||!ReferenceEquals(Ai,savedAi)||
                Owner!=savedOwner||Revision!=savedRevision||Generation!=savedGeneration||Home!=savedHome||Route.Count!=corners||
                !ReferenceEquals(MasterIdolEffectZones.Find(Home),zone)||target==null||targetView?.IsValid()!=true||!ReferenceEquals(targetView.GetZDO(),targetData)||
                !ReferenceEquals(ZDOMan.instance.GetZDO(targetData.m_uid),targetData)||(target.transform.position-savedPoint).sqrMagnitude>.01f||
                !zone.Contains(goal)||!MasterIdolSocialRoutes.Inside(zone,transform.position,goal,Route,Ranges)||
                !MasterIdolResidentCargo.Peace(Actor)||MasterIdolResidentCargo.ManualBusy(Actor)||!MasterIdolActivity.Near(transform.position))return false;
            Target=target;TargetPoint=target.transform.position;Nonce=Guid.NewGuid().ToString("N");Until=Time.unscaledTime+25;Corner=0;Sent=false;return true;
        }
        internal bool Step(float dt)
        {
            if(!Current()||Target==null||!MasterIdolResidentCargo.Peace(Actor))return false;
            var zone=MasterIdolEffectZones.Find(Home);if(zone==null||Time.unscaledTime>Until||(Target.transform.position-TargetPoint).sqrMagnitude>2.25f){Stop();return false;}
            if((transform.position-TargetPoint).sqrMagnitude>6.25f)
            {
                while(Corner<Route.Count&&(transform.position-Route[Corner]).sqrMagnitude<.36f)Corner++;
                if(Corner>=Route.Count){Stop();return false;}Vector3 next=Route[Corner];
                if(!zone.Contains(transform.position)||!MasterIdolSocialRoutes.Inside(zone,transform.position,next,Array.Empty<Vector3>(),Ranges)){Stop();return false;}
                Ai.MoveTo(dt,next,.6f,false);return true;
            }
            Ai.StopMoving();Ai.LookAt(TargetPoint);
            if(!Sent)
            {
                Sent=Target is ItemDrop drop?MasterIdolResidentCargo.RequestPickup(Data,drop,Nonce):Target is Container box&&MasterIdolResidentCargo.RequestDeposit(Data,box,Nonce);
                Until=Time.unscaledTime+7;
            }
            return true;
        }
        internal void Result(string nonce){if(nonce==Nonce)Stop();}
    }
    internal static class ResidentCargoContainers
    {
        private static readonly HashSet<Container> Boxes=new HashSet<Container>();
        internal static void Add(Container box){if(box!=null&&box.GetComponentInParent<Incinerator>()!=null)Boxes.Add(box);}
        internal static void Remove(Container box)=>Boxes.Remove(box);
        internal static Container Nearest(Character actor,MasterIdolEffectZone zone,Inventory inventory)
        {
            if(inventory.NrOfItems()==0)return null;Container best=null;float distance=float.MaxValue;
            Boxes.RemoveWhere(box=>box==null);
            foreach(var box in Boxes)
            {
                if(box.m_nview?.IsValid()!=true||box.m_nview.GetZDO().GetInt(ZDOVars.s_inUse,0)!=0||box.GetComponentInParent<Incinerator>().isInUse||!zone.Contains(box.transform.position))continue;
                float d=(box.transform.position-actor.transform.position).sqrMagnitude;if(d>=distance)continue;box.Load();
                bool fits=false;foreach(var item in inventory.GetAllItems())if(ResidentCargoTransfer.CanFit(box.GetInventory(),item)){fits=true;break;}
                if(fits){best=box;distance=d;}
            }
            return best;
        }
    }
    [HarmonyPatch(typeof(Character),"Awake")]
    internal static class ResidentCargoAttachPatch{private static void Postfix(Character __instance)=>MasterIdolResidentCargo.Bound(__instance);}
    [HarmonyPatch(typeof(MasterIdolForestGuardState),nameof(MasterIdolForestGuardState.Stamp))]
    internal static class ResidentCargoStampPatch{private static void Postfix(Character actor)=>MasterIdolResidentCargo.Bound(actor);}
    [HarmonyPatch(typeof(Container),"Awake")]
    internal static class ResidentCargoContainerIndexPatch{private static void Postfix(Container __instance)=>ResidentCargoContainers.Add(__instance);}
}
