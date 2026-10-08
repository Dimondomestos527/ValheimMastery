using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Transient presentation only. Native name/friend/work/fuel/health state remains elsewhere.
    internal sealed class MasterIdolResidentSceneAnimation:MonoBehaviour
    {
        private const string CueRpc="VM_Idol_ResidentScene_v1",PetRpc="VM_Idol_ResidentPetScene_v1";
        private const int Movement=unchecked((int)3885065915u);
        private sealed class HomeClock
        {
            internal MasterIdolResidentSceneAnimation Active;
            internal double Next,Keep;
            internal readonly Dictionary<string,double> Slots=new Dictionary<string,double>(StringComparer.Ordinal);
        }
        private static readonly Dictionary<string,HomeClock> Homes=new Dictionary<string,HomeClock>(StringComparer.Ordinal);
        private static ZNet GlobalSession;private static ZDOMan GlobalManager;private static ulong GlobalGeneration;
        private static long GlobalWorld,GlobalEpoch;private static int GraphicsScenes;
        private Character Actor;private BaseAI Ai;private ZNetView View,Registered;
        private ZDO Data;private ZNet Session;private ZDOMan Manager;private ulong Generation;
        private long World,Owner,ContextTicks,LastCueTicks;private ushort Revision;
        private string LastCue,LastPetRequest,Home,SlotKey;
        private double NextPet,NextSeat,NextRefill,NextPetRequest;
        private bool Active,GraphicsLease,CastAttempted,BodyStarted;private long GraphicsEpoch,Started;
        private MasterIdolResidentSceneKind Kind;private HomeClock Clock;
        private Player PetPlayer;private Chair Seat;private Character Occupant;private Fireplace Fire;
        private ZDO TargetData,OtherData;private long TargetOwner,OtherOwner;private ushort TargetRevision,OtherRevision;
        private Animator BodyAnimator;private RuntimeAnimatorController BodyController;
        private MasterIdolResidentPresentation Presentation;
        private string LastDecision="not-attempted";

        private void Awake(){Actor=GetComponent<Character>();Ai=GetComponent<BaseAI>();View=GetComponent<ZNetView>();Register();}
        private void Register()
        {
            if(View?.IsValid()!=true||ReferenceEquals(Registered,View))return;
            View.Register<ZPackage>(CueRpc,ReceiveCue);View.Register<ZPackage>(PetRpc,ReceivePet);Registered=View;
        }
        private static void GlobalContext()
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(GlobalSession==net&&GlobalManager==ZDOMan.instance&&GlobalGeneration==MasterIdolLoadIdentity.Generation&&GlobalWorld==world)return;
            Homes.Clear();GraphicsScenes=0;GlobalEpoch++;GlobalSession=net;GlobalManager=ZDOMan.instance;
            GlobalGeneration=MasterIdolLoadIdentity.Generation;GlobalWorld=world;
        }
        private bool Context()
        {
            Register();GlobalContext();
            if(View?.IsValid()!=true||ZNet.instance?.GetWorld()==null||!MasterIdolLoadIdentity.LoadReady)return false;
            var data=View.GetZDO();if(data==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data))return false;
            bool identityChanged=!ReferenceEquals(Data,data)||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||World!=ZNet.instance.GetWorldUID();
            if(identityChanged||Owner!=data.GetOwner()||Revision!=data.OwnerRevision)
            {
                End("context-changed");Data=data;Session=ZNet.instance;Manager=ZDOMan.instance;
                Generation=MasterIdolLoadIdentity.Generation;World=Session.GetWorldUID();Owner=data.GetOwner();Revision=data.OwnerRevision;
                ContextTicks=Session.GetTime().Ticks;LastCueTicks=0;LastCue=LastPetRequest=null;
                if(identityChanged){NextPet=NextPetRequest=Session.GetTimeSeconds()+MasterIdolResidentSceneRules.PetQuiet;NextSeat=NextRefill=Session.GetTimeSeconds();}
            }
            return true;
        }
        private bool Peace()
        {
            return Actor!=null&&Ai!=null&&Ai.m_timeSinceHurt>=8&&!MasterIdolSocialSeats.Battle(Ai,Actor)&&!Ai.IsSleeping()&&!Actor.InDodge()&&!Actor.InEmote()&&
                GetComponent<MasterIdolSocialSeats>()?.Active!=true&&GetComponent<MasterIdolWelcome>()?.IsPresenting!=true&&
                GetComponent<MasterIdolResidentPresentation>()?.BlocksWork!=true&&!MasterIdolResidentWork.Busy(Actor);
        }
        private bool Source(out MasterIdolEffectZone zone)
        {
            return MasterIdolResidentPolicy.TryHome(Actor,out zone)&&zone.Contains(transform.position)&&Peace()&&
                MasterIdolActivity.Near(transform.position,MasterIdolResidentSceneRules.ObserverRadius);
        }
        private static MasterIdolResidentSceneAnimation Component(Character actor)
        {
            if(actor==null||!MasterIdolResidents.Family(actor))return null;
            return actor.GetComponent<MasterIdolResidentSceneAnimation>()??actor.gameObject.AddComponent<MasterIdolResidentSceneAnimation>();
        }
        internal static bool Playing(Character actor)=>actor?.GetComponent<MasterIdolResidentSceneAnimation>()?.Active==true;
        internal static bool HomeActive(MasterIdolEffectZone zone)
        {
            GlobalContext();return zone!=null&&Homes.TryGetValue(zone.Id,out var clock)&&clock.Active!=null&&clock.Active.Active;
        }
        internal static void Stop(Character actor)=>actor?.GetComponent<MasterIdolResidentSceneAnimation>()?.End("priority");
        internal static bool Idle(Character actor,float dt)
        {
            var scene=actor?.GetComponent<MasterIdolResidentSceneAnimation>();
            if(scene==null||!scene.Active||scene.View?.IsOwner()!=true)return false;
            try
            {
                if(!scene.Valid()){scene.End("idle-invalid");return false;}
                scene.Face();return true;
            }
            catch(Exception){scene.End("idle-failed");return false;}
        }
        internal static void Pet(Character actor,Player player)
        {
            var scene=Component(actor);
            if(scene==null||player==null||!ReferenceEquals(player,Player.m_localPlayer)||!scene.Context()||!scene.Source(out _))return;
            var tag=actor.GetComponent<MasterIdolResidentPet>();
            if(!MasterIdolResidentInteraction.PlayerAccess(tag,player)||string.IsNullOrWhiteSpace(scene.Data.GetString(ZDOVars.s_tamedName,"")))return;
            double now=scene.Session.GetTimeSeconds();if(now<scene.NextPetRequest||now<scene.NextPet)return;
            scene.NextPetRequest=now+MasterIdolResidentSceneRules.PetQuiet;
            var packet=new ZPackage();packet.Write(scene.World);MasterIdolResidentWork.WriteUid(packet,scene.Data.m_uid);
            packet.Write((int)scene.Revision);MasterIdolResidentWork.WriteUid(packet,player.m_nview.GetZDO().m_uid);
            packet.Write(ZNet.instance.GetTime().Ticks);packet.Write(Guid.NewGuid().ToString("N"));
            scene.View.InvokeRPC(scene.Owner,PetRpc,packet);
        }
        private bool PlayerSender(Player player,long sender)
        {
            var data=player?.m_nview?.GetZDO();
            if(data==null||data.GetOwner()!=sender||!MasterIdolResidentInteraction.PlayerAccess(Actor.GetComponent<MasterIdolResidentPet>(),player))return false;
            bool local=sender==ZNet.GetUID()&&ReferenceEquals(player,Player.m_localPlayer);
            if(Session.IsServer()&&!local){var peer=Session.GetPeer(sender);return peer?.IsReady()==true&&peer.m_characterID==data.m_uid&&peer.m_playerID==player.GetPlayerID();}
            return local||!Session.IsServer()&&Session.GetServerPeer()?.IsReady()==true;
        }
        private void ReceivePet(long sender,ZPackage packet)
        {
            try
            {
                if(packet==null||packet.Size()>192||!Context()||!View.IsOwner()||!Source(out _))return;
                long world=packet.ReadLong();var actorId=MasterIdolResidentWork.ReadUid(packet);int revision=packet.ReadInt();
                var playerId=MasterIdolResidentWork.ReadUid(packet);long ticks=packet.ReadLong();string nonce=packet.ReadString();
                if(packet.GetPos()!=packet.Size()||world!=World||actorId!=Data.m_uid||revision!=Revision||!MasterIdolResidentSceneRules.Nonce(nonce)||nonce==LastPetRequest||
                    !MasterIdolResidentSceneRules.Window(ticks,Session.GetTime().Ticks,ContextTicks,5)||string.IsNullOrWhiteSpace(Data.GetString(ZDOVars.s_tamedName,"")))return;
                var player=ZNetScene.instance?.FindInstance(playerId)?.GetComponent<Player>();
                if(!PlayerSender(player,sender)||(player.transform.position-transform.position).sqrMagnitude>16||player.InAttack()||player.InPlaceMode())return;
                LastPetRequest=nonce;TryStart(MasterIdolResidentSceneKind.Pet,player,null,null,null,nonce);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("scene.pet-request-rejected");}
        }
        internal static bool OccupiedSeat(Character actor,Chair chair,Character occupant)
        {
            var scene=Component(actor);
            return scene!=null&&scene.TryStart(MasterIdolResidentSceneKind.OccupiedSeat,null,chair,occupant,null,Guid.NewGuid().ToString("N"));
        }
        internal static void Refilled(Character actor,Fireplace fire,string home,string nonce)
        {
            var scene=Component(actor);
            if(scene==null||!scene.Context()||!MasterIdolResidentPolicy.TryHome(actor,out var zone)||zone.Id!=home)return;
            scene.TryStart(MasterIdolResidentSceneKind.Refilled,null,null,null,fire,nonce);
        }
        private bool Admission(MasterIdolEffectZone zone)
        {
            if(zone.Residents.Count<1||zone.Residents.Count>8)return false;
            foreach(var member in zone.Residents)
            {
                if(!MasterIdolNetworkQuota.TryUid(member.Key,out long user,out uint id))return false;
                var resident=ZNetScene.instance?.FindInstance(new ZDOID(user,id))?.GetComponent<Character>();var view=resident?.m_nview;
                if(view?.IsValid()!=true||view.GetZDO().GetOwner()!=Owner||!MasterIdolResidentPolicy.TryHome(resident,out var current)||current.Id!=zone.Id||
                    MasterIdolSocialScenes.Active(resident.GetComponent<MasterIdolSocial>()))return false;
            }
            return true;
        }
        private static HomeClock ClockFor(string home,double now)
        {
            if(Homes.TryGetValue(home,out var clock))return clock;
            if(Homes.Count>=MasterIdolResidentSceneRules.MaxHomeRecords)
            {
                string remove=null;foreach(var pair in Homes)if(pair.Value.Active==null&&pair.Value.Keep<=now){remove=pair.Key;break;}
                if(remove==null)return null;Homes.Remove(remove);
            }
            clock=new HomeClock();Homes.Add(home,clock);return clock;
        }
        private bool Target(MasterIdolResidentSceneKind kind,Player player,Chair chair,Character occupant,Fireplace fire,MasterIdolEffectZone zone,out string slot)
        {
            slot=null;
            if(kind==MasterIdolResidentSceneKind.Pet)return player!=null&&!player.IsDead()&&!player.InAttack()&&!player.InPlaceMode()&&
                MasterIdolResidentInteraction.PlayerAccess(Actor.GetComponent<MasterIdolResidentPet>(),player)&&
                !string.IsNullOrWhiteSpace(Data.GetString(ZDOVars.s_tamedName,""))&&(player.transform.position-transform.position).sqrMagnitude<=16;
            if(kind==MasterIdolResidentSceneKind.OccupiedSeat)
            {
                if(Utils.GetPrefabName(gameObject)!="Greyling"||!MasterIdolSocialSeats.Supported(chair)||occupant==null||occupant==Actor||
                    MasterIdolSocialSeats.Occupant(chair,zone)!=occupant||(chair.m_attachPoint.position-transform.position).sqrMagnitude>16||
                    MasterIdolActivity.Near(chair.m_attachPoint.position,.75f))return false;
                var furniture=chair.GetComponentInParent<ZNetView>();int index=Array.IndexOf(MasterIdolProps.Chairs(furniture),chair);
                if(index<0||index>=16)return false;slot=furniture.GetZDO().m_uid+"/"+index;return true;
            }
            var fireView=fire?.m_nview;
            return MasterIdolResidentPolicy.Shaman(Actor,out _)&&fireView?.IsValid()==true&&ReferenceEquals(ZDOMan.instance.GetZDO(fireView.GetZDO().m_uid),fireView.GetZDO())&&
                zone.Contains(fire.transform.position)&&(fire.transform.position-transform.position).sqrMagnitude<=16&&fireView.GetZDO().GetInt(ZDOVars.s_state,1)!=2;
        }
        private bool TryStart(MasterIdolResidentSceneKind kind,Player player,Chair chair,Character occupant,Fireplace fire,string nonce)
        {
            if(!Context()||!View.IsOwner()||Active||!Source(out var zone)||!MasterIdolResidentSceneRules.Nonce(nonce)||
                kind==MasterIdolResidentSceneKind.Pet&&player==null||kind==MasterIdolResidentSceneKind.OccupiedSeat&&(chair==null||occupant==null)||
                kind==MasterIdolResidentSceneKind.Refilled&&fire==null)return false;
            double now=Session.GetTimeSeconds();if(!MasterIdolResidentSceneRules.Finite(now)||Session.GetTime().Ticks<=ContextTicks+TimeSpan.TicksPerSecond)return false;
            double next=kind==MasterIdolResidentSceneKind.Pet?NextPet:kind==MasterIdolResidentSceneKind.OccupiedSeat?NextSeat:NextRefill;
            if(now<next)return false;
            double quietTime=MasterIdolResidentSceneRules.Quiet(kind,UnityEngine.Random.value);
            if(kind==MasterIdolResidentSceneKind.Pet)NextPet=now+quietTime;else if(kind==MasterIdolResidentSceneKind.OccupiedSeat)NextSeat=now+quietTime;else NextRefill=now+quietTime;
            if(!Admission(zone)||!Target(kind,player,chair,occupant,fire,zone,out var slot))return false;
            var clock=ClockFor(zone.Id,now);if(clock==null||clock.Active!=null||now<clock.Next)return false;
            if(slot!=null&&clock.Slots.TryGetValue(slot,out double quiet)&&now<quiet)return false;
            if(slot!=null&&!clock.Slots.ContainsKey(slot)&&clock.Slots.Count>=MasterIdolResidentSceneRules.MaxSlotRecords)
            {
                string expired=null;foreach(var pair in clock.Slots)if(pair.Value<=now){expired=pair.Key;break;}
                if(expired==null)return false;clock.Slots.Remove(expired);
            }
            if(kind==MasterIdolResidentSceneKind.OccupiedSeat&&!SeatSight(chair))return false;
            if(HasGraphics()&&GraphicsScenes>=MasterIdolResidentSceneRules.MaxGraphicsScenes)return false;
            var target=kind==MasterIdolResidentSceneKind.Pet?player.m_nview:kind==MasterIdolResidentSceneKind.OccupiedSeat?chair.GetComponentInParent<ZNetView>():fire.m_nview;
            var targetData=target.GetZDO();var other=occupant?.m_nview?.GetZDO();long started=Session.GetTime().Ticks;
            var packet=new ZPackage();packet.Write(World);MasterIdolResidentWork.WriteUid(packet,Data.m_uid);packet.Write((int)Revision);
            packet.Write(zone.Id);packet.Write(nonce);packet.Write((int)kind);packet.Write(started);
            MasterIdolResidentWork.WriteUid(packet,targetData.m_uid);packet.Write(targetData.GetOwner());packet.Write((int)targetData.OwnerRevision);
            MasterIdolResidentWork.WriteUid(packet,other?.m_uid??ZDOID.None);packet.Write(other?.GetOwner()??0);packet.Write((int)(other?.OwnerRevision??0));
            packet.Write(chair!=null?Array.IndexOf(MasterIdolProps.Chairs(target),chair):-1);packet.Write((float)quietTime);
            clock.Next=now+MasterIdolResidentSceneRules.Duration(kind)+MasterIdolResidentSceneRules.HomeQuiet;clock.Keep=Math.Max(clock.Keep,clock.Next);
            if(slot!=null){clock.Slots[slot]=now+MasterIdolResidentSceneRules.SeatQuiet;clock.Keep=Math.Max(clock.Keep,now+MasterIdolResidentSceneRules.SeatQuiet);}
            ReceiveCue(Owner,new ZPackage(packet.GetArray())); // local exactly once; native RPC echo is rejected
            if(!Active)return false;
            View.InvokeRPC(ZNetView.Everybody,CueRpc,packet);MasterIdolResidentMetrics.Count("scene.start."+(int)kind);return true;
        }
        private void ReceiveCue(long sender,ZPackage packet)
        {
            try
            {
                if(packet==null||packet.Size()>MasterIdolResidentSceneRules.MaxPacketBytes||!Context()||sender!=Owner||Active||!Source(out var zone))return;
                long world=packet.ReadLong();var actorId=MasterIdolResidentWork.ReadUid(packet);int revision=packet.ReadInt();
                string home=packet.ReadString(),nonce=packet.ReadString();int kind=packet.ReadInt();long started=packet.ReadLong();
                var targetId=MasterIdolResidentWork.ReadUid(packet);long targetOwner=packet.ReadLong();int targetRevision=packet.ReadInt();
                var otherId=MasterIdolResidentWork.ReadUid(packet);long otherOwner=packet.ReadLong();int otherRevision=packet.ReadInt();int slot=packet.ReadInt();float quiet=packet.ReadSingle();
                if(packet.GetPos()!=packet.Size()||world!=World||actorId!=Data.m_uid||revision!=Revision||home.Length>128||home!=zone.Id||
                    !MasterIdolResidentSceneRules.Kind(kind)||!MasterIdolResidentSceneRules.Nonce(nonce)||nonce==LastCue||started<=LastCueTicks||
                    !MasterIdolResidentSceneRules.Window(started,Session.GetTime().Ticks,ContextTicks,MasterIdolResidentSceneRules.Duration((MasterIdolResidentSceneKind)kind)))return;
                var targetRoot=ZNetScene.instance?.FindInstance(targetId);var target=targetRoot?.GetComponent<ZNetView>();var targetData=target?.GetZDO();
                if(target?.IsValid()!=true||targetData.GetOwner()!=targetOwner||targetData.OwnerRevision!=targetRevision||!ReferenceEquals(Manager.GetZDO(targetId),targetData))return;
                var otherRoot=otherId==ZDOID.None?null:ZNetScene.instance?.FindInstance(otherId);var other=otherRoot?.GetComponent<Character>();var otherData=other?.m_nview?.GetZDO();
                if(otherId!=ZDOID.None&&(otherData==null||otherData.GetOwner()!=otherOwner||otherData.OwnerRevision!=otherRevision||!ReferenceEquals(Manager.GetZDO(otherId),otherData)))return;
                var sceneKind=(MasterIdolResidentSceneKind)kind;if(!MasterIdolResidentSceneRules.QuietAccepted(sceneKind,quiet))return;
                var player=targetRoot.GetComponent<Player>();var fire=targetRoot.GetComponent<Fireplace>();Chair chair=null;
                if(sceneKind==MasterIdolResidentSceneKind.OccupiedSeat){var chairs=MasterIdolProps.Chairs(target);if(slot<0||chairs==null||slot>=chairs.Length)return;chair=chairs[slot];}
                else if(slot!=-1||otherId!=ZDOID.None)return;
                if(!Target(sceneKind,player,chair,other,fire,zone,out var seatKey))return;
                if(HasGraphics()&&GraphicsScenes>=MasterIdolResidentSceneRules.MaxGraphicsScenes)return;
                var clock=ClockFor(home,Session.GetTimeSeconds());if(clock==null||clock.Active!=null)return;
                Kind=sceneKind;Home=home;Clock=clock;SlotKey=seatKey;Started=started;TargetData=targetData;TargetOwner=targetOwner;TargetRevision=(ushort)targetRevision;
                OtherData=otherData;OtherOwner=otherOwner;OtherRevision=(ushort)otherRevision;PetPlayer=player;Seat=chair;Occupant=other;Fire=fire;
                float elapsed=MasterIdolResidentSceneRules.Elapsed(started,Session.GetTime().Ticks);double now=Session.GetTimeSeconds();
                double actorNext=now+quiet-elapsed;
                if(sceneKind==MasterIdolResidentSceneKind.Pet)NextPet=Math.Max(NextPet,actorNext);else if(sceneKind==MasterIdolResidentSceneKind.OccupiedSeat)NextSeat=Math.Max(NextSeat,actorNext);else NextRefill=Math.Max(NextRefill,actorNext);
                clock.Next=Math.Max(clock.Next,now+MasterIdolResidentSceneRules.Duration(sceneKind)-elapsed+MasterIdolResidentSceneRules.HomeQuiet);clock.Keep=Math.Max(clock.Keep,clock.Next);
                if(seatKey!=null){clock.Slots[seatKey]=Math.Max(clock.Slots.TryGetValue(seatKey,out double old)?old:0,now+MasterIdolResidentSceneRules.SeatQuiet-elapsed);clock.Keep=Math.Max(clock.Keep,clock.Slots[seatKey]);}
                LastCue=nonce;LastCueTicks=started;Active=true;CastAttempted=BodyStarted=false;clock.Active=this;
                if(HasGraphics()){GraphicsLease=true;GraphicsEpoch=GlobalEpoch;GraphicsScenes++;}
                LastDecision="accepted-"+kind;TickVisual();if(Active&&View.IsOwner())Face();if(Active)MasterIdolResidentMetrics.Count("scene.accepted."+kind);
            }
            catch(Exception){End("packet-rejected");MasterIdolResidentMetrics.Count("scene.packet-rejected");}
        }
        private static bool HasGraphics()=>!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
        private bool SeatSight(Chair chair)
        {
            var target=chair.GetComponentInParent<ZNetView>();
            return !Physics.Linecast(transform.position+Vector3.up*1.5f,chair.m_attachPoint.position+Vector3.up*.5f,out var hit,
                LayerMask.GetMask("piece","static_solid"),QueryTriggerInteraction.Ignore)||hit.collider?.GetComponentInParent<ZNetView>()==target;
        }
        private bool Valid()
        {
            if(!Active||!Context()||!Source(out var zone)||Home!=zone.Id||
                !MasterIdolResidentSceneRules.Window(Started,Session.GetTime().Ticks,ContextTicks,MasterIdolResidentSceneRules.Duration(Kind)))return false;
            if(TargetData==null||!ReferenceEquals(Manager.GetZDO(TargetData.m_uid),TargetData)||TargetData.GetOwner()!=TargetOwner||TargetData.OwnerRevision!=TargetRevision)return false;
            if(OtherData!=null&&(!ReferenceEquals(Manager.GetZDO(OtherData.m_uid),OtherData)||OtherData.GetOwner()!=OtherOwner||OtherData.OwnerRevision!=OtherRevision))return false;
            if(BodyStarted&&(BodyAnimator==null||!ReferenceEquals(BodyAnimator.runtimeAnimatorController,BodyController)||BodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement))return false;
            return Target(Kind,PetPlayer,Seat,Occupant,Fire,zone,out _);
        }
        private void Face()
        {
            Ai.StopMoving();Vector3 look=Kind==MasterIdolResidentSceneKind.Pet?PetPlayer.transform.position:Kind==MasterIdolResidentSceneKind.Refilled?Fire.transform.position:
                MasterIdolResidentSceneRules.SeatPhase(MasterIdolResidentSceneRules.Elapsed(Started,Session.GetTime().Ticks))==1?Occupant.transform.position:Seat.m_attachPoint.position;
            Ai.LookAt(look);
        }
        private void TickVisual()
        {
            if(!GraphicsLease||!MasteryPlugin.Settings.EnablePerkProcVFX.Value)return;
            float elapsed=MasterIdolResidentSceneRules.Elapsed(Started,Session.GetTime().Ticks);
            if(Kind==MasterIdolResidentSceneKind.Refilled)
            {
                if(!CastAttempted&&MasterIdolResidentSceneRules.CastWindow(elapsed))
                {
                    CastAttempted=true;Presentation=GetComponent<MasterIdolResidentPresentation>()??gameObject.AddComponent<MasterIdolResidentPresentation>();
                    bool played=Presentation.BeginAftercare(elapsed-MasterIdolResidentSceneRules.CastDelay,Home);
                    MasterIdolResidentMetrics.Count(played?"scene.aftercare-native-played":"scene.aftercare-native-denied");
                }
                return;
            }
            if(BodyStarted)return;BodyStarted=true;BodyAnimator=GetComponentInChildren<Animator>();BodyController=BodyAnimator?.runtimeAnimatorController;
            if(BodyAnimator==null||BodyController==null||!BodyAnimator.HasState(0,Movement)||BodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement){End("native-idle-unavailable");return;}
            BodyAnimator.Play(Movement,0,0);HoldBlend();MasterIdolResidentMetrics.Count("scene.idle-native-played");
        }
        private void HoldBlend()
        {
            if(!Active||Kind==MasterIdolResidentSceneKind.Refilled||!GraphicsLease||BodyAnimator==null||!ReferenceEquals(BodyAnimator.runtimeAnimatorController,BodyController)||
                BodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement)return;
            BodyAnimator.SetFloat("forward_speed",0);BodyAnimator.SetFloat("sideway_speed",0);BodyAnimator.SetFloat("turn_speed",0);
        }
        private void Update()
        {
            try
            {
                if(Registered==null)Register();if(Data==null&&View?.IsValid()==true&&MasterIdolLoadIdentity.LoadReady)Context();if(!Active)return;
                if(!Valid()){End("expired-or-invalid");return;}if(GraphicsLease&&!MasteryPlugin.Settings.EnablePerkProcVFX.Value){End("vfx-disabled");return;}TickVisual();if(Active&&View.IsOwner())Face();
            }
            catch(Exception){End("update-failed");}
        }
        private void LateUpdate()
        {
            try{if(Active&&MasteryPlugin.Settings.EnablePerkProcVFX.Value)HoldBlend();}
            catch(Exception){End("late-update-failed");}
        }
        private void End(string reason)
        {
            if(!Active&&!GraphicsLease)return;
            Active=false;
            try{Presentation?.StopAftercare();}
            catch(Exception){MasterIdolResidentMetrics.Count("scene.cleanup-failed");}
            finally
            {
                Presentation=null;
                if(Clock!=null&&ReferenceEquals(Clock.Active,this))
                {
                    Clock.Active=null;
                    try{double now=Session?.GetTimeSeconds()??double.NaN;if(MasterIdolResidentSceneRules.Finite(now)){Clock.Next=Math.Max(Clock.Next,now+MasterIdolResidentSceneRules.HomeQuiet);Clock.Keep=Math.Max(Clock.Keep,Clock.Next);}}
                    catch(Exception){MasterIdolResidentMetrics.Count("scene.quiet-clock-unavailable");}
                }
                Clock=null;
                if(GraphicsLease&&GraphicsEpoch==GlobalEpoch)GraphicsScenes=Math.Max(0,GraphicsScenes-1);GraphicsLease=false;
                BodyAnimator=null;BodyController=null;PetPlayer=null;Seat=null;Occupant=null;Fire=null;TargetData=OtherData=null;Home=SlotKey=null;
                LastDecision=reason;MasterIdolResidentMetrics.Count("scene.end."+reason);
            }
        }
        internal string Describe()=>"scene active="+Active+" kind="+(int)Kind+" decision="+LastDecision+" graphics="+GraphicsLease+" globalGraphics="+GraphicsScenes;
        private void OnDisable()=>End("disabled");private void OnDestroy()=>End("destroyed");
    }
    [HarmonyPatch(typeof(BaseAI),"Awake")]
    internal static class MasterIdolResidentSceneAttachPatch
    {
        private static void Postfix(BaseAI __instance)
        {if(MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject))>0&&__instance.GetComponent<MasterIdolResidentSceneAnimation>()==null)__instance.gameObject.AddComponent<MasterIdolResidentSceneAnimation>();}
    }
}
