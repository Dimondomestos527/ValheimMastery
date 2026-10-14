using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Temporary untamed visitors; only observed UID witnesses are retained for cleanup.
    internal sealed class MasterIdolForestGuests:MonoBehaviour
    {
        private const string Marker="vm.bf.guest.v1",WorldKey="vm.bf.guest.world.v1",FirstKey="vm.bf.guest.first.v1",UntilKey="vm.bf.guest.until.v1",HomeKey="vm.bf.guest.home.v1",PointKey="vm.bf.guest.point.v1",RadiusKey="vm.bf.guest.radius.v1",StartKey="vm.bf.guest.depart.v1",StartedKey="vm.bf.guest.leaving.v1",WalkedKey="vm.bf.guest.walked.v1";
        private const string ObserveRpc="VM_BF_GuestObserve_v1",CullRpc="VM_BF_GuestCull_v1",PermitRpc="VM_BF_GuestPermit_v1";
        private sealed class Known{internal ZDO Data;internal long First,Until;internal LinkedListNode<ZDOID> Node;}
        private static readonly Dictionary<ZDOID,Known> KnownGuests=new Dictionary<ZDOID,Known>();
        private static readonly LinkedList<ZDOID> Rotation=new LinkedList<ZDOID>();
        private static readonly LinkedList<MasterIdolForestGuests> Loaded=new LinkedList<MasterIdolForestGuests>();
        private static ZNet GlobalSession;private static ZDOMan GlobalManager;private static ulong GlobalGeneration;private static long GlobalWorld;private static float Next;
        private Character Actor;private MonsterAI Ai;private ZNetView View;private ZDO Data;private long Owner;private ushort Revision;
        private ZNet Session;private ZDOMan Manager;private ulong Generation;private LinkedListNode<MasterIdolForestGuests> Node;
        private bool Override,OldAttack,OldFire,Going;private Vector3 Goal;private float NextPlan,PlanUntil,NextMessage;private int Step;
        private readonly List<Vector3> Route=new List<Vector3>(32);
        private void Awake(){Actor=GetComponent<Character>();Ai=GetComponent<MonsterAI>();View=GetComponent<ZNetView>();}
        private static bool Eligible(ZDO data)
            =>data?.Persistent==true&&MasterIdolForestGuardState.Family(data)>=0&&!data.GetBool(ZDOVars.s_tamed,false)&&!MasterIdolForestGuardState.ModuleBorn(data)&&
                !data.GetBool(ZDOVars.s_eventCreature,false)&&!data.GetBool(ZDOVars.s_despawnInDay,false)&&data.GetFloat(ZDOVars.s_health,1)>0;
        private static bool Tagged(ZDO data)
        {
            var net=ZNet.instance;return Eligible(data)&&net?.GetWorld()!=null&&data.GetBool(Marker,false)&&data.GetLong(WorldKey,0)==net.GetWorldUID()&&
                MasterIdolForestRules.Home(data.GetString(HomeKey,""))&&MasterIdolForestRules.Guest(data.GetLong(FirstKey,0),data.GetLong(UntilKey,0),net.GetTime().Ticks);
        }
        internal static bool Peaceful(Character actor)
        {
            if(!GoldCraftingService.Enabled||!MasterIdolLoadIdentity.LoadReady||actor==null||actor.IsDead()||actor.m_nview?.IsValid()!=true)return false;
            var data=actor.m_nview.GetZDO();if(!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data)||!Eligible(data))return false;
            return Tagged(data)||MasterIdolEffectZones.At("BlackForest",actor.transform.position)!=null;
        }
        private static void GlobalContext()
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(GlobalSession==net&&GlobalManager==ZDOMan.instance&&GlobalGeneration==MasterIdolLoadIdentity.Generation&&GlobalWorld==world)return;
            KnownGuests.Clear();Rotation.Clear();Loaded.Clear();GlobalSession=net;GlobalManager=ZDOMan.instance;GlobalGeneration=MasterIdolLoadIdentity.Generation;GlobalWorld=world;Next=0;
        }
        private bool Context()
        {
            GlobalContext();if(!GoldCraftingService.Enabled||!MasterIdolLoadIdentity.LoadReady||Actor==null||Ai==null||View?.IsValid()!=true||!View.IsOwner()||ZNet.instance?.GetWorld()==null)return false;
            var data=View.GetZDO();if(!Eligible(data)||!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data))return false;
            if(!ReferenceEquals(Data,data)||Owner!=data.GetOwner()||Revision!=data.OwnerRevision||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation)
            {Reset();Data=data;Owner=data.GetOwner();Revision=data.OwnerRevision;Session=ZNet.instance;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;NextPlan=NextMessage=0;}
            return true;
        }
        private void Admit()
        {
            if(!Context())return;
            if(!Tagged(Data))
            {
                var zone=MasterIdolEffectZones.At("BlackForest",transform.position);long now=Session.GetTime().Ticks;
                if(zone==null||!MasterIdolForestRules.Deadline(now,EnvMan.instance?.m_dayLengthSec??0,out long until))return;
                // First deadline is immutable within this NPC/world; an invalid prior tag is never renewed.
                if(Data.GetBool(Marker,false))return;
                float radius=0;foreach(var circle in zone.Circles)radius=Math.Max(radius,Vector3.Distance(zone.Home,circle.Point)+circle.Radius);
                Data.Set(WorldKey,Session.GetWorldUID());Data.Set(FirstKey,now);Data.Set(UntilKey,until);Data.Set(HomeKey,zone.Id);Data.Set(PointKey,zone.Home);Data.Set(RadiusKey,radius);Data.Set(Marker,true);
            }
            if(Node==null||Node.List!=Loaded){Node=Loaded.AddLast(this);if(Session.IsServer())Observe(Owner,Header(Data));}
        }
        internal static void UpdateOwner(MonsterAI ai)
        {
            var actor=ai?.GetComponent<Character>();if(actor==null)return;
            if(ai.m_nview?.IsOwner()!=true||!Peaceful(actor)){actor.GetComponent<MasterIdolForestGuests>()?.Reset();return;}
            var guest=actor.GetComponent<MasterIdolForestGuests>()??actor.gameObject.AddComponent<MasterIdolForestGuests>();guest.Admit();guest.Clear();
        }
        private void Clear()
        {
            if(!Context()||!Peaceful(Actor))return;
            if(!Override){OldAttack=Ai.m_attackPlayerObjects;OldFire=Ai.m_afraidOfFire;Override=true;}
            Ai.m_attackPlayerObjects=false;Ai.m_afraidOfFire=false;
            if(Ai.m_targetCreature!=null||Ai.m_targetStatic!=null){Ai.m_targetCreature=null;Ai.m_targetStatic=null;Ai.SetTargetInfo(ZDOID.None);}
            if(Ai.IsAlerted())Ai.SetAlerted(false);
        }
        private static ZPackage Header(ZDO data)
        {var p=new ZPackage();p.Write(ZNet.instance.GetWorldUID());MasterIdolResidentWork.WriteUid(p,data.m_uid);p.Write(data.GetLong(FirstKey,0));p.Write(data.GetLong(UntilKey,0));p.Write((int)data.OwnerRevision);return p;}
        private static bool Read(long sender,ZPackage packet,out ZDO data)
        {
            data=null;if(!MasterIdolLoadIdentity.LoadReady||ZNet.instance?.IsServer()!=true||packet==null||packet.Size()>96)return false;
            long world=packet.ReadLong();var id=MasterIdolResidentWork.ReadUid(packet);long first=packet.ReadLong(),until=packet.ReadLong();int revision=packet.ReadInt();
            data=ZDOMan.instance?.GetZDO(id);
            return packet.GetPos()==packet.Size()&&world==ZNet.instance.GetWorldUID()&&Tagged(data)&&data.GetOwner()==sender&&data.OwnerRevision==revision&&data.GetLong(FirstKey,0)==first&&data.GetLong(UntilKey,0)==until;
        }
        private static void Observe(long sender,ZPackage packet)
        {
            try
            {
                GlobalContext();if(!Read(sender,packet,out var data))return;
                if(KnownGuests.TryGetValue(data.m_uid,out var old))
                {if(old.First==data.GetLong(FirstKey,0)&&old.Until==data.GetLong(UntilKey,0))old.Data=data;return;}
                if(KnownGuests.Count>=4096)return;
                KnownGuests.Add(data.m_uid,new Known{Data=data,First=data.GetLong(FirstKey,0),Until=data.GetLong(UntilKey,0),Node=Rotation.AddLast(data.m_uid)});
            }
            catch(Exception){MasterIdolResidentMetrics.Count("guest.observe-rejected");}
        }
        private static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        private static bool Beyond(Vector3 point,Vector3 reference,int radius)
        {
            if(!Finite(point)||!Finite(reference)||ZoneSystem.instance==null)return false;
            var a=ZoneSystem.GetZone(point);var b=ZoneSystem.GetZone(reference);return MasterIdolForestRules.Outside(a.x,a.y,b.x,b.y,radius);
        }
        private static bool Unseen(Vector3 point)
        {
            var net=ZNet.instance;if(net?.IsServer()!=true||!MasterIdolLoadIdentity.LoadReady||ZoneSystem.instance==null||!Finite(point))return false;
            int radius=net.GetSyncedSimulationDistance().TotalSimulationDistance;if(radius<0)return false;
            var local=Player.m_localPlayer;if(local!=null&&(!Beyond(point,local.transform.position,radius)||!Beyond(point,net.GetReferencePosition(),radius)))return false;
            foreach(var peer in net.GetPeers())
            {
                if(!peer.IsReady())continue;if(!Beyond(point,peer.m_refPos,radius)||peer.m_characterID.IsNone())return false;
                var player=ZDOMan.instance.GetZDO(peer.m_characterID);
                if(player==null||player.GetOwner()!=peer.m_uid||player.GetLong(ZDOVars.s_playerID,0)!=peer.m_playerID||!Beyond(point,player.GetPosition(),radius))return false;
            }
            return true;
        }
        private static bool OutsideHome(ZDO data,Vector3 point)
        {
            if(!MasterIdolEffectZones.Ready)return false;var zone=MasterIdolEffectZones.Find(data.GetString(HomeKey,""));
            if(MasterIdolEffectZones.At("BlackForest",point)!=null)return false;if(zone!=null)return !zone.Contains(point,5);
            float radius=data.GetFloat(RadiusKey,-1);Vector3 home=data.GetVec3(PointKey,Vector3.zero);return float.IsFinite(radius)&&radius>=0&&Finite(home)&&(point-home).sqrMagnitude>(radius+6)*(radius+6);
        }
        private static bool MayCull(ZDO data)
            =>Tagged(data)&&MasterIdolForestRules.Expired(data.GetLong(UntilKey,0),ZNet.instance.GetTime().Ticks)&&data.GetBool(WalkedKey,false)&&OutsideHome(data,data.GetPosition())&&Unseen(data.GetPosition());
        private static void Cull(long sender,ZPackage packet)
        {
            try
            {
                if(!Read(sender,packet,out var data)||!KnownGuests.TryGetValue(data.m_uid,out var known)||!ReferenceEquals(known.Data,data)||!MayCull(data))return;
                if(data.IsOwner()){DestroyOwned(data);return;}
                var peer=ZNet.instance.GetPeer(sender);if(peer?.IsReady()!=true)return;
                var reply=Header(data);reply.Write(ZNet.instance.GetTime().Ticks);reply.Write(data.GetPosition());peer.m_rpc.Invoke(PermitRpc,reply);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("guest.cull-rejected");}
        }
        private static void DestroyOwned(ZDO data)
        {
            if(data==null||!data.IsOwner()||!Tagged(data))return;
            var instance=ZNetScene.instance?.FindInstance(data.m_uid);
            if(instance!=null)ZNetScene.instance.Destroy(instance);else ZDOMan.instance.DestroyZDO(data);
        }
        private static void Permit(ZRpc rpc,ZPackage packet)
        {
            try
            {
                var net=ZNet.instance;if(net?.IsServer()!=false||net.GetServerPeer()?.m_rpc!=rpc||!MasterIdolLoadIdentity.LoadReady||packet==null||packet.Size()>128)return;
                long world=packet.ReadLong();var id=MasterIdolResidentWork.ReadUid(packet);long first=packet.ReadLong(),until=packet.ReadLong();int revision=packet.ReadInt();long issued=packet.ReadLong();var point=packet.ReadVector3();
                var data=ZDOMan.instance?.GetZDO(id);long now=net.GetTime().Ticks;
                if(packet.GetPos()!=packet.Size()||world!=net.GetWorldUID()||!Tagged(data)||!data.IsOwner()||data.OwnerRevision!=revision||data.GetLong(FirstKey,0)!=first||data.GetLong(UntilKey,0)!=until||
                    issued>now||now-issued>TimeSpan.TicksPerSecond||!data.GetBool(WalkedKey,false)||!MasterIdolForestRules.Expired(until,now)||!OutsideHome(data,data.GetPosition())||!Finite(point)||(data.GetPosition()-point).sqrMagnitude>1)return;
                DestroyOwned(data);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("guest.permit-rejected");}
        }
        internal static void Register(ZNetPeer peer)
        {
            var rpc=peer?.m_rpc;if(rpc==null)return;
            rpc.Register<ZPackage>(ObserveRpc,(sender,p)=>{var current=ZNet.instance?.GetPeer(sender);if(current?.IsReady()==true)Observe(current.m_uid,p);});
            rpc.Register<ZPackage>(CullRpc,(sender,p)=>{var current=ZNet.instance?.GetPeer(sender);if(current?.IsReady()==true)Cull(current.m_uid,p);});rpc.Register<ZPackage>(PermitRpc,Permit);
        }
        internal static void Tick()
        {
            GlobalContext();if(GlobalSession?.GetWorld()==null||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;
            if(!GoldCraftingService.Enabled||!MasterIdolLoadIdentity.LoadReady){int release=Math.Min(8,Loaded.Count);for(int i=0;i<release;i++)Loaded.First.Value?.Reset();return;}
            int count=Math.Min(8,Loaded.Count);for(int i=0;i<count;i++){var guest=Loaded.First.Value;Loaded.RemoveFirst();if(guest!=null){guest.Node=Loaded.AddLast(guest);guest.Refresh();}}
            if(!GlobalSession.IsServer())return;count=Math.Min(4,Rotation.Count);
            for(int i=0;i<count;i++)
            {
                var id=Rotation.First.Value;Rotation.RemoveFirst();var known=KnownGuests[id];known.Node=Rotation.AddLast(id);var data=GlobalManager.GetZDO(id);
                if(data==null){if(GlobalManager.m_deadZDOs.ContainsKey(id)){Rotation.Remove(known.Node);KnownGuests.Remove(id);}continue;}
                if(!ReferenceEquals(known.Data,data)||!Tagged(data)||data.GetLong(FirstKey,0)!=known.First||data.GetLong(UntilKey,0)!=known.Until){Rotation.Remove(known.Node);KnownGuests.Remove(id);continue;}
                if(data.IsOwner()&&MayCull(data))DestroyOwned(data);
            }
        }
        private void Refresh()
        {
            if(!Context()||!Tagged(Data)){Reset();return;}Clear();long now=Session.GetTime().Ticks;bool expired=MasterIdolForestRules.Expired(Data.GetLong(UntilKey,0),now);
            if(expired&&!Data.GetBool(StartedKey,false)){Data.Set(StartKey,transform.position);Data.Set(StartedKey,true);Going=false;NextPlan=0;}
            if(expired&&OutsideHome(Data,transform.position)&&(transform.position-Data.GetVec3(StartKey,transform.position)).sqrMagnitude>=4)Data.Set(WalkedKey,true);
            if(Time.unscaledTime>=NextMessage)
            {
                NextMessage=Time.unscaledTime+30;
                if(Session.IsServer()){Observe(Owner,Header(Data));if(expired)Cull(Owner,Header(Data));}
                else if(Session.GetServerPeer()?.IsReady()==true){Session.GetServerPeer().m_rpc.Invoke(ObserveRpc,Header(Data));if(expired)Session.GetServerPeer().m_rpc.Invoke(CullRpc,Header(Data));}
            }
            if(Going&&(Time.unscaledTime>=PlanUntil||(transform.position-Goal).sqrMagnitude<1)){Going=false;Route.Clear();}
            if(!Going&&Time.unscaledTime>=NextPlan&&(expired||!OutsideHome(Data,transform.position)))Plan(expired);
        }
        private void Plan(bool expired)
        {
            NextPlan=Time.unscaledTime+30;var zone=MasterIdolEffectZones.Find(Data.GetString(HomeKey,""));Vector3 home=Data.GetVec3(PointKey,transform.position);float radius=Data.GetFloat(RadiusKey,0);
            int nearest=0;float distance=float.MaxValue;if(zone!=null)for(int n=0;n<zone.Circles.Length;n++){float score=(transform.position-zone.Circles[n].Point).sqrMagnitude;if(score<distance){nearest=n;distance=score;}}
            if(ZoneSystem.instance==null||Pathfinding.instance==null)return;Vector3 direction=transform.position-home;direction.y=0;if(direction.sqrMagnitude<1)direction=Vector3.forward;direction.Normalize();
            for(int i=0;i<3;i++)
            {
                Vector3 point;
                if(zone!=null&&zone.Circles.Length>0){var circle=zone.Circles[nearest];var outward=transform.position-circle.Point;outward.y=0;if(outward.sqrMagnitude<1)outward=direction;point=circle.Point+Quaternion.Euler(0,i*120,0)*outward.normalized*(circle.Radius+8);if(expired&&OutsideHome(Data,transform.position))point=transform.position+Quaternion.Euler(0,i*120,0)*direction*24;}
                else point=home+Quaternion.Euler(0,i*120,0)*direction*(radius+12);
                if(!OutsideHome(Data,point)||!ZoneSystem.instance.IsZoneLoaded(point))continue;
                ZoneSystem.instance.GetGroundData(ref point,out var normal,out var biome,out var area,out var heightmap);
                if(heightmap==null||normal.y<.7f||point.y<ZoneSystem.instance.m_waterLevel+.3f||Physics.CheckSphere(point+Vector3.up,.5f,LayerMask.GetMask("piece","static_solid"),QueryTriggerInteraction.Ignore))continue;
                if(!MasterIdolResidentBudget.Path())return;Route.Clear();if(!Pathfinding.instance.GetPath(transform.position,point,Route,Ai.m_pathAgentType,true,false,false)||Route.Count>128)continue;
                Goal=point;Step=0;Going=true;PlanUntil=Time.unscaledTime+30;return;
            }
        }
        private bool Idle(float dt)
        {
            if(!Context()||!Tagged(Data))return false;Clear();if(!Going)return false;
            if(Time.unscaledTime>=PlanUntil){Going=false;return false;}while(Step<Route.Count&&(transform.position-Route[Step]).sqrMagnitude<1.44f)Step++;
            Ai.MoveTo(dt,Step<Route.Count?Route[Step]:Goal,1f,false);return true;
        }
        private void Reset()
        {if(Node?.List==Loaded)Loaded.Remove(Node);Node=null;Going=false;Route.Clear();if(Override&&Ai!=null){Ai.m_attackPlayerObjects=OldAttack;Ai.m_afraidOfFire=OldFire;}Override=false;}
        private void OnDisable()=>Reset();private void OnDestroy()=>Reset();
        internal static bool Idle(Character actor,float dt)=>actor?.GetComponent<MasterIdolForestGuests>()?.Idle(dt)==true;
    }
    [HarmonyPatch(typeof(MonsterAI),nameof(MonsterAI.UpdateAI))]
    internal static class MasterIdolForestGuestUpdatePatch
    {[HarmonyPriority(Priority.First)]private static void Prefix(MonsterAI __instance)=>MasterIdolForestGuests.UpdateOwner(__instance);}
    [HarmonyPatch(typeof(BaseAI),"IdleMovement")]
    internal static class MasterIdolForestGuestIdlePatch
    {[HarmonyPriority(Priority.High)]private static bool Prefix(BaseAI __instance,float __0)=>!MasterIdolForestGuests.Idle(__instance.GetComponent<Character>(),__0);}
}
