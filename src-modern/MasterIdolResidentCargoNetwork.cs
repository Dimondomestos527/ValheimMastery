using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static partial class MasterIdolResidentCargo
    {
        private const string RequestRpc="VM_GuardCargoRequest1",ConsentRpc="VM_GuardCargoConsent1",AckRpc="VM_GuardCargoAck1",GrantRpc="VM_GuardCargoGrant1",DoneRpc="VM_GuardCargoDone1",LinkRpc="VM_GuardCargoLink1",CloseRpc="VM_GuardCargoClose1";
        private enum Kind:byte { Open, Pickup, Deposit, Cancel, Reclaim }
        private sealed class Operation
        {
            internal Kind Kind;internal string Nonce,Epoch,Home;internal ZDO Actor,Cargo,Target,Player;
            internal long Requester,ActorOwner,Executor,ExpectedOwner;internal ushort ActorRevision,ExpectedRevision;
            internal float Until,AskAt;internal ZDOID Asked;internal uint Saved;internal bool Acked,Granted,PrepareAsked,Prepared,Done;internal uint PrepareSaved,DoneSaved;
        }
        private sealed class Closing { internal ZDOID Cargo;internal long Peer;internal uint Revision;internal float Until; }
        private sealed class ManualLease
        {
            internal ZDOID Actor;internal long Peer;internal string Nonce;
            internal float Until,AskAt;internal bool Recovering,Acked;
            internal long ExpectedOwner;internal ushort ExpectedRevision;internal uint Saved;
        }
        private static readonly Dictionary<ZDOID,Operation> Pending=new Dictionary<ZDOID,Operation>();
        private static readonly Dictionary<ZDOID,Closing> ClosingBoxes=new Dictionary<ZDOID,Closing>();
        private static readonly Dictionary<ZDOID,ManualLease> ManualLeases=new Dictionary<ZDOID,ManualLease>();
        private static readonly Dictionary<ZDOID,float> ConsentLocks=new Dictionary<ZDOID,float>();
        private static readonly Dictionary<int,float> ActorRates=new Dictionary<int,float>();
        private static readonly Dictionary<string,float> SeenRequests=new Dictionary<string,float>();
        private static readonly HashSet<ZRpc> Registered=new HashSet<ZRpc>();
        private static readonly Dictionary<ZDOID,float> LinkTimes=new Dictionary<ZDOID,float>();
        private static float NextNetwork;
        private static void ResetNetwork(){ResidentCargoOpenTrace.Reset();Pending.Clear();ClosingBoxes.Clear();ManualLeases.Clear();ConsentLocks.Clear();ActorRates.Clear();SeenRequests.Clear();Registered.Clear();LinkTimes.Clear();NextNetwork=0;ResetExecutor();}
        internal static void Register(ZNetPeer peer)
        {
            if(peer?.m_rpc==null)return;Context();if(!Registered.Add(peer.m_rpc))return;
            peer.m_rpc.Register<ZPackage>(RequestRpc,ReceiveRequest);peer.m_rpc.Register<ZPackage>(ConsentRpc,ReceiveConsent);peer.m_rpc.Register<ZPackage>(AckRpc,ReceiveAck);
            peer.m_rpc.Register<ZPackage>(GrantRpc,ReceiveGrant);peer.m_rpc.Register<ZPackage>(DoneRpc,ReceiveDone);peer.m_rpc.Register<ZPackage>(LinkRpc,ReceiveLink);peer.m_rpc.Register<ZPackage>(CloseRpc,ReceiveClose);
        }
        private static ZPackage Header(){var packet=new ZPackage();packet.Write(World);return packet;}
        private static bool ReadHeader(ZPackage packet,int limit=1024)=>Context()&&packet!=null&&packet.Size()<=limit&&packet.ReadLong()==World;
        private static bool TrustedServer(ZRpc rpc)=>rpc!=null&&NetworkSync.HasCurrentServerSettings(rpc);
        private static ZNetPeer Peer(long uid)=>Session?.GetPeer(uid);
        private static bool PeerReady(ZNetPeer peer)=>peer?.IsReady()==true&&NetworkSync.HasPeerSettings(peer);
        private static bool Owner(ZDO data)=>data!=null&&ReferenceEquals(Manager.GetZDO(data.m_uid),data)&&data.GetOwner()==ZNet.GetUID();
        private static bool Reached(uint current,uint expected)=>ResidentCargoRules.RevisionReached(current,expected);
        private static bool InRange(ZDO a,ZDO b,float reach)=>a!=null&&b!=null&&(a.GetPosition()-b.GetPosition()).sqrMagnitude<=reach*reach;
        private static bool CurrentPlayer(ZDO player,long owner)=>player!=null&&ReferenceEquals(Manager.GetZDO(player.m_uid),player)&&player.GetOwner()==owner&&player.GetLong(ZDOVars.s_playerID,0)!=0&&ResidentCargoRules.Living(player.GetFloat(ZDOVars.s_health,1));
        internal static bool PendingFor(ZDOID actor)=>Pending.ContainsKey(actor);
        private static void CancelServer(ZDOID actor){Pending.Remove(actor);}
        private static void SendLink(ZDO actor,ZDO cargo)
        {
            if(actor.GetZDOID(Link)==cargo.m_uid&&cargo.GetZDOID(BodyKey)==actor.m_uid&&cargo.GetString(NonceKey,"")==actor.GetString(MasterIdolForestGuardState.NonceKey,"")&&cargo.GetLong(BornKey,0)==actor.GetLong(MasterIdolForestGuardState.BornKey,0)&&!Opening(actor)&&(actor.GetPosition()-cargo.GetPosition()).sqrMagnitude<.25f)return;
            if(LinkTimes.TryGetValue(actor.m_uid,out float at)&&Time.unscaledTime<at)return;LinkTimes[actor.m_uid]=Time.unscaledTime+1;
            var p=Header();p.Write(actor.m_uid);p.Write((int)actor.OwnerRevision);p.Write(actor.GetLong(MasterIdolForestGuardState.BornKey,0));p.Write(actor.GetString(MasterIdolForestGuardState.NonceKey,""));p.Write(cargo.m_uid);
            if(actor.GetOwner()!=ZNet.GetUID()&&PeerReady(Peer(actor.GetOwner())))Peer(actor.GetOwner()).m_rpc.Invoke(LinkRpc,p);
            if(cargo.GetOwner()!=ZNet.GetUID()&&cargo.GetOwner()!=actor.GetOwner()&&PeerReady(Peer(cargo.GetOwner())))Peer(cargo.GetOwner()).m_rpc.Invoke(LinkRpc,p);
        }
        private static void ReceiveLink(ZRpc rpc,ZPackage p)
        {
            try
            {
                if(!TrustedServer(rpc)||!ReadHeader(p,256))return;var id=p.ReadZDOID();int revision=p.ReadInt();long born=p.ReadLong();string nonce=p.ReadString();var cargo=p.ReadZDOID();if(p.GetPos()!=p.Size()||!MasterIdolForestRules.Nonce(nonce))return;
                var actor=Manager.GetZDO(id);var store=Manager.GetZDO(cargo);
                if(!MasterIdolForestGuardState.ModuleBorn(actor)||actor.OwnerRevision!=revision||actor.GetLong(MasterIdolForestGuardState.BornKey,0)!=born||actor.GetString(MasterIdolForestGuardState.NonceKey,"")!=nonce||store==null||store.GetPrefab()!=Prefab.GetStableHashCode()||store.GetLong(WorldKey,0)!=World||store.GetInt(FamilyKey,-1)!=MasterIdolForestGuardState.Family(actor))return;
                if(Owner(store)&&store.GetLong(OpenPeerKey,0)==0&&store.GetInt(ZDOVars.s_inUse,0)==0)
                {store.Set(BodyKey,id);store.Set(NonceKey,nonce);store.Set(BornKey,born);store.SetPosition(actor.GetPosition());var box=ContainerFor(store);if(box!=null)box.transform.position=actor.GetPosition();Manager.ForceSendZDO(store.m_uid);}
                if(Owner(actor)&&store.GetZDOID(BodyKey)==id&&store.GetString(NonceKey,"")==nonce){actor.Set(Link,cargo);actor.Set(OpeningKey,0L);Bound(ActorFor(actor));Manager.ForceSendZDO(id);}
            }
            catch(Exception){MasterIdolResidentMetrics.Count("cargo.link-rejected");}
        }
        internal static bool TryOpen(Character actor,Player player)
        {
            var data=actor?.m_nview?.GetZDO();if(!MasterIdolForestGuardState.ModuleBorn(data))return false;
            if(player==null||player!=Player.m_localPlayer){ResidentCargoOpenTrace.Write(true,"client-player",null);return true;}
            if(!Context()){ResidentCargoOpenTrace.Write(true,"client-context",null);return true;}
            if(player.IsDead()||player.IsTeleporting()){ResidentCargoOpenTrace.Write(true,"client-lifecycle",null);return true;}
            if(!MasterIdolEffectZones.Allows(player.GetPlayerID(),actor.transform.position)){ResidentCargoOpenTrace.Write(true,"client-projection",null);return true;}
            if(!InRange(data,player.m_nview?.GetZDO(),5)){ResidentCargoOpenTrace.Write(true,"client-range",null);return true;}
            if(LocalOpenNonce!=null){ResidentCargoOpenTrace.Write(true,"client-pending",LocalOpenNonce);return true;}LocalOpenNonce=Guid.NewGuid().ToString("N");LocalOpenUntil=Time.unscaledTime+8;
            ResidentCargoOpenTrace.Write(true,"client-request",LocalOpenNonce);if(!Request(Kind.Open,data,ZDOID.None,LocalOpenNonce)){ResidentCargoOpenTrace.Write(true,"client-send-unavailable",LocalOpenNonce);LocalOpenNonce=null;}return true;
        }
        internal static bool RequestPickup(ZDO actor,ItemDrop item,string nonce)=>Request(Kind.Pickup,actor,item.m_nview.GetZDO().m_uid,nonce);
        internal static bool RequestDeposit(ZDO actor,Container box,string nonce)=>Request(Kind.Deposit,actor,box.m_nview.GetZDO().m_uid,nonce);
        private static bool Request(Kind kind,ZDO actor,ZDOID target,string nonce)
        {
            if(!Context()||actor==null)return false;var p=Header();p.Write((byte)kind);p.Write(nonce);p.Write(actor.m_uid);p.Write((int)actor.OwnerRevision);p.Write(actor.GetString(EpochKey,""));p.Write(target);p.Write(Session.GetTime().Ticks);
            if(Session.IsServer())Admit(ZNet.GetUID(),kind==Kind.Open?Player.m_localPlayer?.m_nview?.GetZDO():null,p);
            else {var rpc=Session.GetServerRPC();if(!TrustedServer(rpc))return false;rpc.Invoke(RequestRpc,p);}return true;
        }
        private static void ReceiveRequest(ZRpc rpc,ZPackage p)
        {if(!Context()||!Session.IsServer())return;var peer=Session.GetPeer(rpc);if(PeerReady(peer))Admit(peer.m_uid,OwnerSkillAuthority.ResolveCharacterData(peer),p);}
        private static void Admit(long sender,ZDO player,ZPackage p)
        {
            try
            {
                if(!ReadHeader(p,384))return;var kind=(Kind)p.ReadByte();string nonce=p.ReadString();var id=p.ReadZDOID();int revision=p.ReadInt();string epoch=p.ReadString();var target=p.ReadZDOID();long ticks=p.ReadLong();
                if(p.GetPos()!=p.Size()||!Guid.TryParseExact(nonce,"N",out _)||epoch.Length>32||kind>Kind.Cancel||Math.Abs(Session.GetTime().Ticks-ticks)>3*TimeSpan.TicksPerSecond)return;
                ResidentCargoOpenTrace.Write(kind==Kind.Open,"server-parsed",nonce);var actor=Manager.GetZDO(id);if(!TryBinding(actor,out var binding,kind==Kind.Open)){ResidentCargoOpenTrace.Write(kind==Kind.Open,"server-binding",nonce);return;}
                if(kind==Kind.Cancel){if(actor.GetOwner()==sender&&actor.OwnerRevision==revision)CancelServer(id);return;}
                if(ManualLeases.TryGetValue(binding.Cargo.m_uid,out var recovering)&&recovering.Recovering){ResidentCargoOpenTrace.Write(kind==Kind.Open,"server-recovering",nonce);return;}
                if(SeenRequests.ContainsKey(nonce)||SeenRequests.Count>=64){ResidentCargoOpenTrace.Write(kind==Kind.Open,"server-replay-capacity",nonce);return;}
                if(kind==Kind.Open)
                {
                    if(!CurrentPlayer(player,sender)||!InRange(player,actor,5)||!MasterIdolWorldIndex.WardAllows(player.GetLong(ZDOVars.s_playerID,0),actor.GetPosition())){ResidentCargoOpenTrace.Write(kind==Kind.Open,"server-player-range-ward",nonce);return;}
                    if(Pending.TryGetValue(id,out var prior)&&(prior.Kind==Kind.Open||prior.Kind==Kind.Reclaim)){ResidentCargoOpenTrace.Write(kind==Kind.Open,"server-pending",nonce);return;}
                }
                else if(Pending.ContainsKey(id)||ActorRates.TryGetValue(binding.Family,out float next)&&Time.unscaledTime<next)return;
                if(actor.OwnerRevision!=revision||actor.GetString(EpochKey,"")!=epoch||binding.Cargo.GetLong(OpenPeerKey,0)!=0||binding.Cargo.GetInt(ZDOVars.s_inUse,0)!=0){ResidentCargoOpenTrace.Write(kind==Kind.Open,"server-stale-or-inuse",nonce);return;}
                ZDO destination=null;
                if(kind==Kind.Open){if(!CurrentPlayer(player,sender)||!InRange(player,actor,5)||!MasterIdolWorldIndex.WardAllows(player.GetLong(ZDOVars.s_playerID,0),actor.GetPosition()))return;}
                else {if(actor.GetOwner()!=sender)return;destination=Manager.GetZDO(target);if(!ValidTarget(binding,destination,kind))return;}
                if(kind==Kind.Pickup)foreach(var operation in Pending.Values)if(operation.Kind==Kind.Pickup&&operation.Target?.m_uid==target)return;
                ActorRates[binding.Family]=Time.unscaledTime+.15f;
                SeenRequests[nonce]=Time.unscaledTime+12;
                Pending[id]=new Operation{Kind=kind,Nonce=nonce,Epoch=epoch,Actor=actor,Cargo=binding.Cargo,Target=destination,Player=player,Requester=sender,Executor=kind==Kind.Open?sender:actor.GetOwner(),ActorOwner=actor.GetOwner(),ActorRevision=actor.OwnerRevision,Home=binding.Home,Until=Time.unscaledTime+8};
            }
            catch(Exception){MasterIdolResidentMetrics.Count("cargo.request-rejected");}
        }
        private static bool ValidTarget(ResidentCargoBinding binding,ZDO target,Kind kind)
        {
            if(target==null||!ReferenceEquals(Manager.GetZDO(target.m_uid),target)||!InRange(binding.Actor,target,3)||!MasterIdolNetworkCache.Covers(binding.Bench.GetPosition(),target.GetPosition())||!MasterIdolWorldIndex.WardAllows(binding.Principal,target.GetPosition())||!MasterIdolWorldIndex.WardAllows(binding.Principal,binding.Actor.GetPosition()))return false;
            var definition=ZNetScene.instance.GetPrefab(target.GetPrefab());if(definition==null)return false;
            if(kind==Kind.Pickup)return definition.GetComponent<ItemDrop>()!=null;
            var box=definition.GetComponentInChildren<Container>(true);return definition.GetComponent<Incinerator>()!=null&&box!=null&&target.GetInt(ZDOVars.s_inUse,0)==0;
        }
        private static bool Valid(Operation op,out ResidentCargoBinding binding)
        {
            binding=default;if(Time.unscaledTime>op.Until||!ReferenceEquals(Manager.GetZDO(op.Actor.m_uid),op.Actor)||op.Actor.GetOwner()!=op.ActorOwner||op.Actor.OwnerRevision!=op.ActorRevision)return false;
            bool epochReady=op.Kind!=Kind.Open||op.Prepared&&Reached(op.Actor.DataRevision,op.PrepareSaved);
            if(epochReady&&op.Actor.GetString(EpochKey,"")!=op.Epoch)return false;
            if(op.Kind==Kind.Reclaim)return MasterIdolForestGuardState.ModuleBorn(op.Actor)&&op.Cargo.Persistent;
            if(!TryBinding(op.Actor,out binding,op.Kind==Kind.Open)||!ReferenceEquals(binding.Cargo,op.Cargo)||binding.Home!=op.Home)return false;
            if(op.Kind==Kind.Open)return CurrentPlayer(op.Player,op.Requester)&&InRange(op.Player,op.Actor,5)&&MasterIdolWorldIndex.WardAllows(op.Player.GetLong(ZDOVars.s_playerID,0),op.Actor.GetPosition());
            return ValidTarget(binding,op.Target,op.Kind);
        }
        // ZDO-only on dedicated. Loaded current owner alone decides native busy,
        // saves and yields ownership; this host never streams a remote base.
        private static bool Acquire(Operation op,ZDO data)
        {
            if(data.GetOwner()==op.Executor)
            {
                if(!ResidentCargoRules.ConsentReady(data.GetOwner(),op.Executor,op.Asked==data.m_uid,op.Acked,data.DataRevision,op.Saved))return false;
                return data.GetInt(ZDOVars.s_inUse,0)==0;
            }
            if(data.GetInt(ZDOVars.s_inUse,0)!=0)return false;
            if(op.Asked!=ZDOID.None&&op.Asked!=data.m_uid)return false;
            if(data.GetOwner()==ZNet.GetUID())
            {
                var local=ContainerFor(data);if(local!=null){if(local.IsInUse()||local.GetComponentInParent<Incinerator>()?.isInUse==true)return false;local.Load();local.Save();}
                Manager.ForceSendZDO(op.Executor,data.m_uid);data.SetOwner(op.Executor);Manager.ForceSendZDO(op.Executor,data.m_uid);return true;
            }
            if(Time.unscaledTime<op.AskAt)return false;op.AskAt=Time.unscaledTime+.75f;
            var peer=Peer(data.GetOwner());if(!PeerReady(peer))return false;
            if(op.Asked==ZDOID.None){op.Asked=data.m_uid;op.ExpectedOwner=data.GetOwner();op.ExpectedRevision=data.OwnerRevision;op.Acked=false;}
            if(data.GetOwner()!=op.ExpectedOwner||data.OwnerRevision!=op.ExpectedRevision)return false;
            var p=Header();p.Write(op.Nonce);p.Write(op.Actor.m_uid);p.Write(data.m_uid);p.Write((int)op.ExpectedRevision);p.Write(op.Executor);p.Write(Session.GetTime().Ticks);p.Write(false);peer.m_rpc.Invoke(ConsentRpc,p);return false;
        }
        private static void ReceiveConsent(ZRpc rpc,ZPackage p)
        {
            try
            {
                if(!TrustedServer(rpc)||!ReadHeader(p,256))return;string nonce=p.ReadString();var actorId=p.ReadZDOID();var id=p.ReadZDOID();int revision=p.ReadInt();long executor=p.ReadLong(),ticks=p.ReadLong();bool recovery=p.ReadBool();
                if(p.GetPos()!=p.Size()||!Guid.TryParseExact(nonce,"N",out _)||Math.Abs(Session.GetTime().Ticks-ticks)>3*TimeSpan.TicksPerSecond)return;
                var data=Manager.GetZDO(id);var box=ContainerFor(data);var actor=Manager.GetZDO(actorId);
                if(recovery)
                {
                    var current=InventoryGui.instance?.m_currentContainer;
                    if(executor!=Session.GetServerPeer().m_uid||!Owner(data)||data.OwnerRevision!=revision||!data.Persistent||data.GetPrefab()!=Prefab.GetStableHashCode()||data.GetLong(WorldKey,0)!=World||data.GetZDOID(BodyKey)!=actorId||ExecutorLocked(id)||data.GetInt(ZDOVars.s_inUse,0)!=0||current!=null&&current.m_nview?.GetZDO()?.m_uid==id)return;
                    // Unloaded native containers already saved into their ZDO.
                    // Prove absence locally; never instantiate or rewrite items.
                    if(box==null){if(ZNetScene.instance.FindInstance(id)!=null)return;}
                    else {if(!IsCargo(box)||box.IsInUse())return;box.Load();box.Save();}
                    ConsentLocks[id]=Time.unscaledTime+3;data.Set(OpenPeerKey,0L);data.Set(OpenPlayerKey,0L);uint recoverySaved=data.DataRevision;
                    Manager.ForceSendZDO(executor,id);data.SetOwner(executor);Manager.ForceSendZDO(executor,id);
                    var answer=Header();answer.Write(nonce);answer.Write(actorId);answer.Write(id);answer.Write(recoverySaved);answer.Write("");rpc.Invoke(AckRpc,answer);return;
                }
                if(id==actorId){PrepareActor(nonce,actor,revision,rpc);return;}
                if(!Owner(data)||data.OwnerRevision!=revision||box==null||!MasterIdolForestGuardState.ModuleBorn(actor)||box.IsInUse()||data.GetInt(ZDOVars.s_inUse,0)!=0)return;
                if(IsCargo(box)){if(data.GetZDOID(BodyKey)!=actorId||data.GetLong(WorldKey,0)!=World||data.GetLong(OpenPeerKey,0)!=0)return;}
                else if(box.GetComponentInParent<Incinerator>()==null||box.GetComponentInParent<Incinerator>().isInUse)return;
                ConsentLocks[id]=Time.unscaledTime+3;box.Load();box.Save();uint saved=data.DataRevision;
                Manager.ForceSendZDO(executor,id);Manager.ForceSendZDO(Session.GetServerPeer().m_uid,id);data.SetOwner(executor);Manager.ForceSendZDO(executor,id);
                var ack=Header();ack.Write(nonce);ack.Write(actorId);ack.Write(id);ack.Write(saved);ack.Write("");rpc.Invoke(AckRpc,ack);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("cargo.consent-rejected");}
        }
        private static void ReceiveAck(ZRpc rpc,ZPackage p)
        {
            try
            {
                if(!Context()||!Session.IsServer()||!ReadHeader(p,256))return;var peer=Session.GetPeer(rpc);if(!PeerReady(peer))return;
                ApplyAck(peer.m_uid,p);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("cargo.ack-rejected");}
        }
        private static void ApplyAck(long sender,ZPackage p)
        {
            string nonce=p.ReadString();var actor=p.ReadZDOID();var target=p.ReadZDOID();uint revision=p.ReadUInt();string epoch=p.ReadString();
            if(p.GetPos()!=p.Size()||epoch.Length>32)return;
            if(ManualLeases.TryGetValue(target,out var lease)&&lease.Recovering&&lease.Actor==actor&&lease.Nonce==nonce&&lease.ExpectedOwner==sender)
            {lease.Acked=true;lease.Saved=revision;return;}
            if(!Pending.TryGetValue(actor,out var op)||op.Nonce!=nonce)return;
            if(target==actor&&op.Kind==Kind.Open&&op.PrepareAsked&&op.ActorOwner==sender&&Guid.TryParseExact(epoch,"N",out _))
            {op.Prepared=true;op.PrepareSaved=revision;op.Epoch=epoch;return;}
            if(op.Asked!=target||op.ExpectedOwner!=sender)return;op.Acked=true;op.Saved=revision;
        }
        private static void PrepareActor(string nonce,ZDO actor,int revision,ZRpc server)
        {
            var body=ActorFor(actor);if(!Owner(actor)||actor.OwnerRevision!=revision||!MasterIdolForestGuardState.ModuleBorn(actor))return;
            // Only reached through current-server consent or server-admitted Open.
            // Raw interaction never cancels a resident scene.
            var pin=new DancePin(body);if(!pin.Current())return;
            MasterIdolResidentDance.Cancel(body,"cargo.open");
            if(!pin.Current()||!Owner(actor)||actor.OwnerRevision!=revision||!ReferenceEquals(ActorFor(actor),body)||!Peace(body))return;
            if(actor.GetString(OpeningNonce,"")!=nonce||!Opening(actor))
            {body.GetComponent<MasterIdolResidentCargoAgent>()?.Stop();CancelExecutor(actor.m_uid);actor.Set(EpochKey,Guid.NewGuid().ToString("N"));actor.Set(OpeningNonce,nonce);actor.Set(OpeningKey,Session.GetTime().Ticks+8*TimeSpan.TicksPerSecond);Manager.ForceSendZDO(actor.m_uid);}
            var ack=Header();ack.Write(nonce);ack.Write(actor.m_uid);ack.Write(actor.m_uid);ack.Write(actor.DataRevision);ack.Write(actor.GetString(EpochKey,""));
            if(Session.IsServer()){if(ReadHeader(ack,256))ApplyAck(ZNet.GetUID(),ack);}else server.Invoke(AckRpc,ack);
        }
        private static bool PrepareOpen(Operation op)
        {
            if(op.Prepared)return Reached(op.Actor.DataRevision,op.PrepareSaved)&&op.Actor.GetString(EpochKey,"")==op.Epoch;
            if(Time.unscaledTime<op.AskAt)return false;op.AskAt=Time.unscaledTime+.5f;op.PrepareAsked=true;
            if(op.ActorOwner==ZNet.GetUID())PrepareActor(op.Nonce,op.Actor,op.ActorRevision,null);
            else if(PeerReady(Peer(op.ActorOwner)))
            {var p=Header();p.Write(op.Nonce);p.Write(op.Actor.m_uid);p.Write(op.Actor.m_uid);p.Write((int)op.ActorRevision);p.Write(op.Executor);p.Write(Session.GetTime().Ticks);p.Write(false);Peer(op.ActorOwner).m_rpc.Invoke(ConsentRpc,p);}
            return false;
        }
        private static void RequestReclaim(ZDO actor,ZDO cargo)
        {
            if(Pending.TryGetValue(actor.m_uid,out var prior)&&prior.Kind==Kind.Reclaim)return;Pending.Remove(actor.m_uid);
            Pending[actor.m_uid]=new Operation{Kind=Kind.Reclaim,Nonce=Guid.NewGuid().ToString("N"),Epoch=actor.GetString(EpochKey,""),Actor=actor,Cargo=cargo,ActorOwner=actor.GetOwner(),ActorRevision=actor.OwnerRevision,Executor=ZNet.GetUID(),Until=Time.unscaledTime+8};
        }
        private static void TickNetwork()
        {
            if(Time.unscaledTime<NextNetwork)return;NextNetwork=Time.unscaledTime+.1f;
            if(Session.IsServer())
            {
                foreach(var pair in new List<KeyValuePair<ZDOID,Operation>>(Pending))
                {
                    var op=pair.Value;
                    try
                    {
                        if(!Valid(op,out var binding)){ResidentCargoOpenTrace.Write(op.Kind==Kind.Open,"server-operation-invalid",op.Nonce);Pending.Remove(pair.Key);continue;}
                        if(op.Granted){if(op.Done&&Reached(op.Cargo.DataRevision,op.DoneSaved))Pending.Remove(pair.Key);continue;}
                        if(op.Kind==Kind.Open&&!PrepareOpen(op)){ResidentCargoOpenTrace.Write(true,"server-prepare-wait",op.Nonce);continue;}
                        if(!Acquire(op,op.Cargo)){ResidentCargoOpenTrace.Write(op.Kind==Kind.Open,"server-cargo-acquire-wait",op.Nonce);continue;}
                        if(op.Asked==op.Cargo.m_uid){op.Asked=ZDOID.None;op.Acked=false;}
                        if(op.Kind==Kind.Reclaim){ManualLeases.Remove(op.Cargo.m_uid);Pending.Remove(pair.Key);continue;}
                        if(op.Kind==Kind.Deposit&&!Acquire(op,op.Target))continue;
                        if(op.Kind==Kind.Open)ManualLeases[op.Cargo.m_uid]=new ManualLease{Actor=op.Actor.m_uid,Peer=op.Executor,Nonce=op.Nonce,Until=Time.unscaledTime+10};
                        op.Granted=true;op.Until=Time.unscaledTime+8;ResidentCargoOpenTrace.Write(op.Kind==Kind.Open,"server-grant",op.Nonce);SendGrant(op,binding);
                    }
                    catch(Exception error){Pending.Remove(pair.Key);MasteryPlugin.Log?.LogWarning("[GuardCargo] admission retained: "+error.Message);}
                }
                foreach(var pair in new List<KeyValuePair<ZDOID,Closing>>(ClosingBoxes))
                {
                    var close=pair.Value;var data=Manager.GetZDO(close.Cargo);
                    if(data==null||data.GetOwner()!=close.Peer||Time.unscaledTime>close.Until){ClosingBoxes.Remove(pair.Key);continue;}
                    if(!Reached(data.DataRevision,close.Revision)||data.GetInt(ZDOVars.s_inUse,0)!=0)continue;
                    data.SetOwner(ZNet.GetUID());data.Set(OpenPeerKey,0L);data.Set(OpenPlayerKey,0L);Manager.ForceSendZDO(data.m_uid);ClosingBoxes.Remove(pair.Key);
                    if(ManualLeases.TryGetValue(data.m_uid,out var lease)){if(Pending.TryGetValue(lease.Actor,out var active)&&active.Nonce==lease.Nonce)Pending.Remove(lease.Actor);ManualLeases.Remove(data.m_uid);}
                }
                foreach(var pair in new List<KeyValuePair<ZDOID,ManualLease>>(ManualLeases))
                    RecoverClosedLease(pair.Key,pair.Value);
            }
            TickExecutor();
            foreach(var id in new List<ZDOID>(ConsentLocks.Keys))if(ConsentLocks[id]<Time.unscaledTime)ConsentLocks.Remove(id);
            foreach(var nonce in new List<string>(SeenRequests.Keys))if(SeenRequests[nonce]<Time.unscaledTime)SeenRequests.Remove(nonce);
            if(LinkTimes.Count>8)foreach(var id in new List<ZDOID>(LinkTimes.Keys))if(Manager.GetZDO(id)==null)LinkTimes.Remove(id);
        }
        private static void RecoverClosedLease(ZDOID id,ManualLease lease)
        {
            var data=Manager.GetZDO(id);
            if(data==null){ManualLeases.Remove(id);return;}
            if(Time.unscaledTime<lease.Until||Pending.ContainsKey(lease.Actor))return;
            // Timeout is permission to ask the actual owner, never permission
            // to steal an inventory which could still be open on that peer.
            if(data.GetOwner()==ZNet.GetUID())
            {
                if(lease.Recovering&&(!lease.Acked||!Reached(data.DataRevision,lease.Saved)))return;
                var box=ContainerFor(data);
                if(data.GetInt(ZDOVars.s_inUse,0)!=0||box!=null&&(box.IsInUse()||InventoryGui.instance?.m_currentContainer==box)||ExecutorLocked(id))return;
                // A disconnected owner may be reassigned by native ZDOMan;
                // connected remote owners require their saved-data receipt.
                if(!lease.Recovering&&lease.Peer!=ZNet.GetUID()&&PeerReady(Peer(lease.Peer)))return;
                data.Set(OpenPeerKey,0L);data.Set(OpenPlayerKey,0L);Manager.ForceSendZDO(id);ManualLeases.Remove(id);return;
            }
            if(Time.unscaledTime<lease.AskAt)return;lease.AskAt=Time.unscaledTime+.75f;
            var owner=Peer(data.GetOwner());if(!PeerReady(owner))return;
            if(!lease.Recovering||lease.ExpectedOwner!=data.GetOwner()||lease.ExpectedRevision!=data.OwnerRevision)
            {lease.Recovering=true;lease.Acked=false;lease.ExpectedOwner=data.GetOwner();lease.ExpectedRevision=data.OwnerRevision;}
            Manager.ForceSendZDO(owner.m_uid,id);
            var p=Header();p.Write(lease.Nonce);p.Write(lease.Actor);p.Write(id);p.Write((int)lease.ExpectedRevision);p.Write(ZNet.GetUID());p.Write(Session.GetTime().Ticks);p.Write(true);owner.m_rpc.Invoke(ConsentRpc,p);
        }
        internal static void Closed(Container box)
        {
            if(!IsCargo(box)||!box.IsOwner()||box.IsInUse()||!Context())return;box.Save();var data=box.m_nview.GetZDO();if(data.GetLong(OpenPeerKey,0)!=ZNet.GetUID())return;
            var p=Header();p.Write(data.m_uid);p.Write(data.DataRevision);if(Session.IsServer())QueueClose(ZNet.GetUID(),p);else Session.GetServerRPC()?.Invoke(CloseRpc,p);
            CloseRetries[data.m_uid]=new CloseRetry{Revision=data.DataRevision,Until=Time.unscaledTime+8,Next=Time.unscaledTime+.5f};Manager.ForceSendZDO(data.m_uid);
        }
        private static void ReceiveClose(ZRpc rpc,ZPackage p){if(!Context()||!Session.IsServer())return;var peer=Session.GetPeer(rpc);if(PeerReady(peer))QueueClose(peer.m_uid,p);}
        private static void QueueClose(long sender,ZPackage p)
        {
            try
            {
                if(!ReadHeader(p,64))return;var id=p.ReadZDOID();uint revision=p.ReadUInt();var data=Manager.GetZDO(id);
                bool admitted=ManualLeases.TryGetValue(id,out var lease)&&lease.Peer==sender;
                if(p.GetPos()!=p.Size()||data==null||data.GetPrefab()!=Prefab.GetStableHashCode()||data.GetOwner()!=sender||!admitted&&data.GetLong(OpenPeerKey,0)!=sender)return;
                ClosingBoxes[id]=new Closing{Cargo=id,Peer=sender,Revision=revision,Until=Time.unscaledTime+8};
            }
            catch(Exception){MasterIdolResidentMetrics.Count("cargo.close-rejected");}
        }
        internal static bool Locked(Container box)
        {
            if(box?.m_nview?.IsValid()!=true)return false;var id=box.m_nview.GetZDO().m_uid;
            if(ConsentLocks.TryGetValue(id,out float until)&&Time.unscaledTime<until)return true;
            foreach(var op in Pending.Values)if(Time.unscaledTime<op.Until&&(op.Cargo.m_uid==id||op.Target?.m_uid==id))return true;
            return ExecutorLocked(id);
        }
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class ResidentCargoPeerPatch
    {private static void Postfix(ZNetPeer peer)=>MasterIdolResidentCargo.Register(peer);}
}
