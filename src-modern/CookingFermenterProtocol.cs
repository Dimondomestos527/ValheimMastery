using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CookingFermenterProtocol
    {
        private const string RpcName="VM_CookingProducerV2", JournalKey="vm.cooking.input.journal_v2", CounterKey="vm.cooking.input.sequence_v2";
        private enum Message { Hello, HelloReply, Offer, Reserved, Commit, Result, Query, Cancel, Tap }
        private sealed class Client
        { internal Fermenter Station; internal ItemDrop.ItemData Item; internal CookingFermenterReceipts.Input Receipt; internal string Token, ItemBytes, BeforeBytes, AfterBytes; internal bool Debited, Recovered, OwnerRouted, Refunded; internal float Last, Started; }
        private sealed class NativeProbe
        {internal Fermenter Station;internal ItemDrop.ItemData Item;internal long Actor,Owner;internal string Nonce;internal float Started,Last;}
        private sealed class NativeOwnerProof {internal long Owner;internal float At;}
        private const string NativeProbeRpc="VM_CookingNativeProbeV1",NativeReplyRpc="VM_CookingNativeReplyV1";
        private sealed class SessionState
        {internal long World;internal bool HasWorld;internal Client Pending;internal ZRpc Server, CompatibleServer;internal float Next;internal readonly HashSet<ZRpc> Peers=new HashSet<ZRpc>();
            internal NativeProbe NativePending;internal ConditionalWeakTable<Fermenter,NativeOwnerProof> NativeProofs=new ConditionalWeakTable<Fermenter,NativeOwnerProof>();}
        private static readonly ConditionalWeakTable<ZNet,SessionState> Sessions=new ConditionalWeakTable<ZNet,SessionState>();
        private static SessionState State
        {
            get
            {
                var net=ZNet.instance;
                // NetworkSync.Register runs before Connect has assigned the native world.
                // A detached getter has no durable capability or pending transaction.
                if(net==null)return new SessionState();
                var state=Sessions.GetOrCreateValue(net);bool hasWorld=ZNet.m_world!=null;
                long world=hasWorld?net.GetWorldUID():0L;
                if(state.HasWorld!=hasWorld || state.World!=world)
                {state.HasWorld=hasWorld;state.World=world;state.Pending=null;state.Server=null;state.CompatibleServer=null;state.NativePending=null;state.NativeProofs=new ConditionalWeakTable<Fermenter,NativeOwnerProof>();state.Peers.Clear();state.Next=0;}
                return state;
            }
        }
        private static Client Pending {get=>State.Pending;set=>State.Pending=value;}
        private static ZRpc SupportedServer {get=>State.Server;set=>State.Server=value;}
        private static HashSet<ZRpc> SupportedPeers=>State.Peers;
        [ThreadStatic] internal static float TapFactor=-1f;
        [ThreadStatic] internal static CookingProductRecord ActiveProduct;
        [ThreadStatic] internal static long ActiveTapActor;
        internal static bool NativeTapAllowed=>ActiveTapActor!=0;
        [ThreadStatic] private static string ActiveRequest;
        internal static bool NativeInputAllowed=>ActiveProduct!=null;
        internal static bool NativeInputRollback => !Cooking35Service.EffectsEnabled;
        private static bool NativeSessionReady()
        {
            var net=ZNet.instance;
            if(net==null || ZNet.m_world==null)return false;
            if(net.IsServer())return true;
            var rpc=net.GetServerRPC();
            return rpc!=null && NetworkSync.HasServerSettings && State.CompatibleServer==rpc;
        }
        private static bool NativeInputPrerequisites(Fermenter f,Humanoid user)
        {
            // Refuse before native RemoveOneItem. Saved escrow is untouched.
            return user==Player.m_localPlayer && Player.m_localPlayer!=null && Pending==null &&
                !Player.m_localPlayer.m_customData.ContainsKey(Journal) && NativeSessionReady() &&
                f?.m_nview?.IsValid()==true && CookingFermenterOutput.Removable(f);
        }
        private static bool NativeRecipientReady(Fermenter f)
        {
            if(f.m_nview.IsOwner())return true;
            long owner=f.m_nview.GetZDO().GetOwner();
            return owner!=0 && State.NativeProofs.TryGetValue(f,out var proof) && proof.Owner==owner &&
                Time.realtimeSinceStartup>=proof.At && Time.realtimeSinceStartup-proof.At<=1f;
        }
        internal static bool CanBeginNativeInput(Fermenter f,Humanoid user)=>
            NativeInputPrerequisites(f,user) && NativeRecipientReady(f);
        internal static bool RouteNativeInput(Fermenter f,Humanoid user,ItemDrop.ItemData item,ref bool result)
        {
            result=false;
            TickNativeProbe(f); // Expired/unloaded pending must not block another station.
            if(!NativeInputPrerequisites(f,user) || item?.m_dropPrefab==null || item.m_stack<1 ||
                !Player.m_localPlayer.GetInventory().GetAllItems().Contains(item) || f.GetContent()!=0 ||
                f.GetItemConversion(item.m_dropPrefab.name.GetStableHashCode())==null)return false;
            var state=State;
            if(state.NativePending!=null)
            {
                if(state.NativePending.Station!=f || !ReferenceEquals(state.NativePending.Item,item))return false;
                if(!NativeRecipientReady(f)){result=true;return false;}
                state.NativePending=null; // A repeated click fulfills this same intent once.
            }
            if(NativeRecipientReady(f))return true;
            state.NativePending=new NativeProbe{Station=f,Item=item,Actor=Player.m_localPlayer.GetPlayerID(),
                Nonce=Guid.NewGuid().ToString("N"),Started=Time.realtimeSinceStartup};
            result=true;SendNativeProbe(state.NativePending);return false;
        }
        internal static void RegisterNativeProbe(Fermenter f)
        {
            if(f?.m_nview?.IsValid()!=true)return;
            f.m_nview.Register<ZPackage>(OwnerRequestRpc,(sender,packet)=>ReceiveOwnerRequest(f,sender,packet));
            f.m_nview.Register<ZPackage>(OwnerReplyRpc,(sender,packet)=>ReceiveOwnerReply(f,sender,packet));
            f.m_nview.Register<string>(NativeProbeRpc,(sender,nonce)=>ReceiveNativeProbe(f,sender,nonce));
            f.m_nview.Register<string>(NativeReplyRpc,(sender,reply)=>ReceiveNativeReply(f,sender,reply));
        }
        private static void SendNativeProbe(NativeProbe probe)
        {
            probe.Owner=probe.Station.m_nview.GetZDO().GetOwner();probe.Last=Time.realtimeSinceStartup;
            if(probe.Owner!=0)probe.Station.m_nview.InvokeRPC(NativeProbeRpc,probe.Nonce);
        }
        private static void ReceiveNativeProbe(Fermenter f,long sender,string nonce)
        {
            if(!NativeInputRollback || !NativeSessionReady() || sender==0 || nonce?.Length!=32 ||
                !Guid.TryParseExact(nonce,"N",out Guid id) || id==Guid.Empty || f.m_nview?.IsOwner()!=true ||
                !CookingFermenterOutput.Removable(f))return;
            f.m_nview.InvokeRPC(sender,NativeReplyRpc,nonce+";"+NetworkSync.WireIdentity);
        }
        private static bool NativeProbeItemValid(NativeProbe probe)
        {
            var player=Player.m_localPlayer;
            return player!=null && player.GetPlayerID()==probe.Actor && !player.IsDead() && !player.IsTeleporting() &&
                probe.Station!=null && probe.Station.m_nview?.IsValid()==true && probe.Item?.m_dropPrefab!=null && probe.Item.m_stack>0 &&
                player.GetInventory().GetAllItems().Contains(probe.Item) &&
                Vector3.Distance(player.transform.position,probe.Station.transform.position)<=5f &&
                WorkshopWorldRecords.WardAllows(player.GetPlayerID(),probe.Station.transform.position);
        }
        private static void ReceiveNativeReply(Fermenter f,long sender,string reply)
        {
            if(!NativeSessionReady() || reply==null || reply.Length>200)return;
            var state=State;var probe=state.NativePending;
            if(probe==null || probe.Station!=f || sender!=probe.Owner || sender!=f.m_nview.GetZDO().GetOwner() ||
                Time.realtimeSinceStartup-probe.Started>6f)return;
            var fields=reply.Split(';');
            if(fields.Length!=2 || fields[0]!=probe.Nonce || fields[1]!=NetworkSync.WireIdentity)return;
            state.NativePending=null; // Duplicate replies never invoke native twice.
            if(!NativeProbeItemValid(probe))return;
            state.NativeProofs.Remove(f);state.NativeProofs.Add(f,new NativeOwnerProof{Owner=sender,At=Time.realtimeSinceStartup});
            try{f.AddItem(Player.m_localPlayer,probe.Item);}catch(Exception error){Trace("native-input:"+error.Message);}
        }
        private static void TickNativeProbe(Fermenter f)
        {
            var state=State;var probe=state.NativePending;
            if(probe==null)return;
            if(Time.realtimeSinceStartup-probe.Started>6f || !NativeSessionReady() || !NativeProbeItemValid(probe))
            {state.NativePending=null;Trace("native-probe:cancelled-without-debit");return;}
            if(probe.Station!=f)return;
            if(Time.realtimeSinceStartup-probe.Last>=2f)
            {probe.Nonce=Guid.NewGuid().ToString("N");SendNativeProbe(probe);}
        }
        internal static void ObserveCompatibleHello(ZRpc rpc,string serverVersion,string manifest)
        {
            var net=ZNet.instance;
            if(net==null || ZNet.m_world==null || net.IsServer() || rpc==null || rpc!=net.GetServerRPC())return;
            // Static HasServerSettings can survive reconnect. Bind the current
            // successfully applied canonical manifest to this RPC/world session.
            State.CompatibleServer=NetworkSync.HasServerSettings && serverVersion==NetworkSync.WireIdentity &&
                string.Equals(manifest,MasteryRuntime.Manifest(),StringComparison.Ordinal)?rpc:null;
        }
        private const string OwnerRequestRpc="VM_CookingProducerOwnerV1", OwnerReplyRpc="VM_CookingProducerOwnerReplyV1";
        [ThreadStatic] private static Fermenter RoutedStation;
        [ThreadStatic] private static long RoutedSender;
        private sealed class Loader
        {
            internal WorkshopActor Legacy;
            internal Player Player;
            internal long Sender;
            internal bool Available=>Legacy!=null?Legacy.Available:
                Player!=null && Player.m_nview?.IsValid()==true && Player.m_nview.GetZDO().GetOwner()==Sender;
            internal Vector3 Position=>Legacy!=null?Legacy.Position:Player.transform.position;
            internal long GetPlayerID()=>Legacy!=null?Legacy.GetPlayerID():Player.GetPlayerID();
            internal bool IsDead()=>Legacy!=null?Legacy.IsDead():Player.IsDead();
            internal bool IsTeleporting()=>Legacy!=null?Legacy.IsTeleporting():Player.IsTeleporting();
            internal static Loader Resolve(ZRpc rpc,long sender,bool routed)
            {
                if(!routed){var actor=WorkshopActor.Resolve(rpc);return actor==null?null:new Loader{Legacy=actor};}
                long id=ProcessingStationProgressionService.ResolveSender(sender);
                if(id==0)return null;
                if(ZNet.instance.IsServer())
                {
                    var peer=ZNet.instance.GetPeer(sender);
                    if(peer?.m_rpc!=null && peer.m_uid==sender)
                    {
                        var actor=WorkshopActor.Resolve(peer.m_rpc);
                        if(actor!=null && actor.Available && actor.GetPlayerID()==id)return new Loader{Legacy=actor};
                    }
                }
                foreach(var player in Player.GetAllPlayers())
                    if(player!=null && player.GetPlayerID()==id && player.m_nview?.GetZDO()?.GetOwner()==sender)
                        return new Loader{Player=player,Sender=sender};
                return null; // No owner-player fallback when a remote character is absent.
            }
        }
        private static ZPackage OwnerEnvelope(ZPackage packet)
        {var envelope=new ZPackage();envelope.Write(NetworkSync.WireIdentity);envelope.Write(ZNet.instance.GetWorldUID());envelope.Write(packet.GetArray());return envelope;}
        private static ZPackage ReadOwnerEnvelope(ZPackage packet)
        {
            if(packet==null || packet.Size()>34000 || packet.ReadString()!=NetworkSync.WireIdentity || packet.ReadLong()!=ZNet.instance.GetWorldUID())return null;
            byte[] bytes=packet.ReadByteArray();
            return packet.GetPos()==packet.Size() && bytes.Length<=32768?new ZPackage(bytes):null;
        }
        private static void ReceiveOwnerRequest(Fermenter f,long sender,ZPackage envelope)
        {
            if(!NativeSessionReady() || f?.m_nview?.IsOwner()!=true || sender==0)return;
            var previous=RoutedStation;long previousSender=RoutedSender;
            try
            {
                var packet=ReadOwnerEnvelope(envelope);if(packet==null)return;
                RoutedStation=f;RoutedSender=sender;Receive(null,packet);
            }
            catch(Exception error){Fault(error);}
            finally{RoutedStation=previous;RoutedSender=previousSender;}
        }
        private static void ReceiveOwnerReply(Fermenter f,long sender,ZPackage envelope)
        {
            if(!NativeSessionReady() || f?.m_nview?.IsValid()!=true || sender!=f.m_nview.GetZDO().GetOwner() ||
                Pending?.OwnerRouted!=true || Pending.Station!=f)return;
            try{var packet=ReadOwnerEnvelope(envelope);if(packet!=null)ClientReceive(packet);}
            catch(Exception error){Fault(error);}
        }
        private static bool Ready=>ZNet.instance!=null && ZNet.m_world!=null && (ZNet.instance.IsServer() || SupportedServer==ZNet.instance.GetServerRPC());
        private static long Now=>DateTime.UtcNow.Ticks;
        private static string Journal=>JournalKey+"."+(ZNet.instance!=null && ZNet.m_world!=null?ZNet.instance.GetWorldUID():0L);
        private static void EnsureSession(){if(ZNet.instance!=null){var state=State;}}

        internal static bool Owner(Fermenter f)=>ZNet.instance!=null && ZNet.m_world!=null && f?.m_nview?.IsValid()==true && f.m_nview.IsOwner();
        private static string ActorKey(long actor)=>CookingFermenterReceipts.ActorPrefix+actor.ToString(CultureInfo.InvariantCulture);
        private static CookingFermenterReceipts.Input Read(ZDO zdo,long actor)=>CookingFermenterReceipts.DecodeInput(zdo.GetString(ActorKey(actor),""));
        private static void Save(ZDO zdo,CookingFermenterReceipts.Input row)=>zdo.Set(ActorKey(row.Actor),CookingFermenterReceipts.Encode(row));
        private static bool Capacity(Fermenter f,int stage)
        {
            return CookingFermenterOutput.CleanupCapacity(f) && (stage==2?MeadXpTransport.CanAcceptLoad(f):MeadXpTransport.CanAcceptTap(f));
        }
        internal static bool CanTap(Fermenter f)=>Owner(f) && Capacity(f,3) &&
            (CookingFermenterOutput.Pending(f)==null || CookingFermenterOutput.Pending(f).Released);
        internal static void Register(ZNetPeer peer)
        {
            EnsureSession();
            if(peer?.m_rpc==null)return;
            peer.m_rpc.Register<ZPackage>(RpcName,Receive);
        }
        private static ZPackage Packet(Message mode,Fermenter f,CookingFermenterReceipts.Input row=null,string token="")
        {
            var p=new ZPackage();p.Write(2);p.Write((int)mode);p.Write(f?.m_nview?.GetZDO()?.m_uid??ZDOID.None);p.Write(token??"");
            p.Write(mode==Message.Tap?Player.m_localPlayer?.GetSkillFactor(Skills.SkillType.Cooking)??0f:0f);
            p.Write(row==null?"":CookingFermenterReceipts.Encode(row));return p;
        }
        private static void Send(Message mode,Fermenter f,CookingFermenterReceipts.Input row=null,string token="")
        {
            var data=Packet(mode,f,row,token);
            if(f!=null && (mode==Message.Tap || Pending?.OwnerRouted==true))
            {if(NativeSessionReady() && f.m_nview?.IsValid()==true)f.m_nview.InvokeRPC(OwnerRequestRpc,OwnerEnvelope(data));return;}
            if(ZNet.instance.IsServer())Receive(null,new ZPackage(data.GetArray()));else ZNet.instance.GetServerRPC()?.Invoke(RpcName,data);
        }
        private static void Reply(ZRpc rpc,Message mode,Fermenter f,CookingFermenterReceipts.Input row,string token)
        {
            var data=Packet(mode,f,row,token);
            if(RoutedStation!=null)
            {if(RoutedStation.m_nview.IsOwner())RoutedStation.m_nview.InvokeRPC(RoutedSender,OwnerReplyRpc,OwnerEnvelope(data));return;}
            if(rpc==null)ClientReceive(new ZPackage(data.GetArray()));else rpc.Invoke(RpcName,data);
        }
        private static Fermenter Station(ZDOID uid)=>ZNetScene.instance?.FindInstance(uid)?.GetComponent<Fermenter>();
        private static void Fault(Exception error)=>MasteryPlugin.Log.LogWarning("[CookingProducer] "+error.Message);
        // Temporary bounded admission diagnostics; never log item/journal payloads.
        private static void Trace(string point)
        {try{MasteryPlugin.Log.LogInfo("[CookingProducerTrace] "+point);}catch(Exception){}}
        private static bool Refuse(string point){Trace(point);return false;}

        private static void Receive(ZRpc rpc,ZPackage data)
        {
            try
            {
                EnsureSession();
                // Registration is allowed before a world; gameplay messages are not.
                if(ZNet.instance==null || ZNet.m_world==null){Trace("receive:no-world");return;}
                if(data==null || data.Size()>32768 || data.ReadInt()!=2)return;
                var mode=(Message)data.ReadInt();var uid=data.ReadZDOID();string token=data.ReadString();float tapFactor=data.ReadSingle();string raw=data.ReadString();
                if(RoutedStation==null && ZNet.instance?.IsServer()!=true)
                {
                    if(rpc!=ZNet.instance?.GetServerRPC())return;
                    ClientReceive(new ZPackage(PacketFrom(mode,uid,token,raw,tapFactor).GetArray()));return;
                }
                if(mode==Message.Hello){Trace("hello:server-received");if(rpc!=null)SupportedPeers.Add(rpc);Reply(rpc,Message.HelloReply,null,null,"");return;}
                bool routed=RoutedStation!=null;
                var actor=Loader.Resolve(rpc,RoutedSender,routed);var f=routed?RoutedStation:Station(uid);
                if(f?.m_nview?.GetZDO()?.m_uid!=uid)return;
                if(data.GetPos()!=data.Size())return;
                if(actor==null){Trace("server:no-actor");return;}
                if(!actor.Available){Trace("server:actor-unavailable");return;}
                if((!routed || mode==Message.Offer || mode==Message.Tap) && actor.IsDead()){Trace("server:actor-dead");return;}
                if((!routed || mode==Message.Offer || mode==Message.Tap) && actor.IsTeleporting()){Trace("server:actor-teleporting");return;}
                if(f?.m_nview?.IsValid()!=true){Trace("server:station-unavailable");return;}
                if((!routed || mode==Message.Offer || mode==Message.Tap) && Vector3.Distance(actor.Position,f.transform.position)>5f){Trace("server:too-far");return;}
                if((!routed || mode==Message.Offer || mode==Message.Tap) && !WorkshopWorldRecords.WardAllows(actor.GetPlayerID(),f.transform.position)){Trace("server:ward-refused");return;}
                if(!routed && rpc!=null && !SupportedPeers.Contains(rpc)){Trace("server:peer-unconfirmed");return;}
                var zdo=f.m_nview.GetZDO();
                // Station ownership is acquired before any grant permits client debit.
                if(!f.m_nview.IsOwner())return; // Never transfer ownership to obtain admission.
                if(!MeadStationIdentity.TryEnsure(zdo,ZNet.instance.GetWorldUID(),out string saved)){Trace("server:station-identity-refused");return;}
                if(token.Length>0 && token!=saved){Trace("server:station-token-mismatch");return;}
                if(mode==Message.Tap)
                {
                    if(float.IsNaN(tapFactor)||float.IsInfinity(tapFactor)||tapFactor<0f||tapFactor>1f)return;
                    if(!CanTap(f))return;
                    var tap=MeadStageExperience.BeforeTap(f);if(tap==null || tap.Blocked || tap.Serial<=0)return;
                    float previousFactor=TapFactor;long previousActor=ActiveTapActor;
                    TapFactor=tapFactor;ActiveTapActor=actor.GetPlayerID();
                    try{using(MeadStageExperience.BeginAcceptedActor(f,MeadStationIdentity.LedgerKey(ZNet.instance.GetWorldUID(),saved),tap.Serial,3,ActiveTapActor))
                        f.RPC_Tap(routed?RoutedSender:rpc==null?ZNet.GetUID():ZNet.instance.GetPeer(rpc).m_uid);}
                    finally{TapFactor=previousFactor;ActiveTapActor=previousActor;}
                    return;
                }
                var request=CookingFermenterReceipts.DecodeInput(raw);
                if(request==null || request.Actor!=actor.GetPlayerID()){Trace("server:request-actor-refused");return;}
                var source=CookingProductRecord.Decode(request.Source);
                if(f.GetItemConversion(request.Hash)?.m_from==null ||
                    GatheringProgressionService.Normalize(f.GetItemConversion(request.Hash).m_from.gameObject.name)!=source.Prefab){Trace("server:input-source-mismatch");return;}
                var old=Read(zdo,request.Actor);
                if(mode==Message.Query){Reconcile(f,old);Reply(rpc,Message.Result,f,old,saved);return;}
                if(mode==Message.Cancel && old?.Same(request)==true && old.State==CookingFermenterReceipts.Phase.Reserved)
                {old.State=CookingFermenterReceipts.Phase.Rejected;Save(zdo,old);Reply(rpc,Message.Result,f,old,saved);return;}
                if(mode==Message.Offer)
                {
                    if((NativeInputRollback || !routed) && old?.Same(request)!=true){Trace("offer:temporary-native-rollback");return;}
                    if(old!=null && old.State==CookingFermenterReceipts.Phase.Reserved && old.Deadline<Now)
                    {old.State=CookingFermenterReceipts.Phase.Rejected;Save(zdo,old);}
                    var output=CookingFermenterOutput.Pending(f);
                    bool inputSafe=CookingFermenterOutput.CanAcceptInput(f),capacity=Capacity(f,2);
                    var phase=CookingFermenterReceipts.Reserve(old,request,inputSafe && (output==null || output.Released),capacity,Now);
                    if(old?.Same(request)==true){Reply(rpc,old.State==CookingFermenterReceipts.Phase.Reserved?Message.Reserved:Message.Result,f,old,saved);return;}
                    if(old!=null && request.Sequence<=old.Sequence){Trace("offer:stale-sequence");return;}
                    // One reservation per station: all actor rows are bounded and persisted.
                    string[] actors=zdo.GetString("vm.cooking.fermenter.actors_v1","").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries);
                    foreach(string a in actors)
                    {
                        var held=Read(zdo,long.Parse(a,CultureInfo.InvariantCulture));
                        if(held!=null && (held.State==CookingFermenterReceipts.Phase.Committing || held.State==CookingFermenterReceipts.Phase.Uncertain ||
                            held.State==CookingFermenterReceipts.Phase.Reserved && held.Deadline>=Now))phase=CookingFermenterReceipts.Phase.Rejected;
                    }
                    string actorName=request.Actor.ToString(CultureInfo.InvariantCulture);
                    if(!actors.Contains(actorName))
                    {
                        if(actors.Length>=CookingFermenterReceipts.MaxActors){Trace("offer:actor-capacity");return;}
                        zdo.Set("vm.cooking.fermenter.actors_v1",string.Join(";",actors.Concat(new[]{actorName})));
                    }
                    request.State=phase;request.Deadline=Now+TimeSpan.FromSeconds(15).Ticks;Save(zdo,request);
                    Trace("offer:"+phase+" inputSafe="+inputSafe+" capacity="+capacity);Reply(rpc,phase==CookingFermenterReceipts.Phase.Reserved?Message.Reserved:Message.Result,f,request,saved);return;
                }
                if(mode!=Message.Commit || old?.Same(request)!=true)return;
                if(old.State==CookingFermenterReceipts.Phase.Accepted || old.State==CookingFermenterReceipts.Phase.Rejected)
                {Reply(rpc,Message.Result,f,old,saved);return;}
                if(old.State==CookingFermenterReceipts.Phase.Committing || old.State==CookingFermenterReceipts.Phase.Uncertain)
                {Reconcile(f,old);Reply(rpc,Message.Result,f,old,saved);return;}
                if(!CookingFermenterReceipts.CanCommit(old,request,Now) || f.GetContent()!=0 || !Capacity(f,2))
                {old.State=old.Deadline<Now?CookingFermenterReceipts.Phase.Rejected:CookingFermenterReceipts.Phase.Uncertain;
                    Save(zdo,old);Reply(rpc,Message.Result,f,old,saved);return;}
                old.XpSerial=MeadStageExperience.NextLoadSerial(f);if(old.XpSerial<=0)return;
                old.State=CookingFermenterReceipts.Phase.Committing;Save(zdo,old);
                // Write original immutable input lineage before native content mutation.
                zdo.Set(CookingFermenterReceipts.SourceKey,old.Source);
                zdo.Set(CookingFermenterReceipts.BatchKey,old.Request);
                zdo.Set("vm.cooking.fermenter.cheated",old.Cheated);
                var previousProduct=ActiveProduct;var previousRequest=ActiveRequest;
                ActiveProduct=source;ActiveRequest=old.Request;
                try{using(MeadStageExperience.BeginAcceptedActor(f,MeadStationIdentity.LedgerKey(ZNet.instance.GetWorldUID(),saved),old.XpSerial,2,old.Actor))
                    f.RPC_AddItem(routed?RoutedSender:rpc==null?ZNet.GetUID():ZNet.instance.GetPeer(rpc).m_uid,old.Hash,old.Cheated);}
                finally{ActiveProduct=previousProduct;ActiveRequest=previousRequest;}
                if(!Owner(f))return;
                old.State=f.GetContent()==old.Hash && zdo.GetString(CookingFermenterReceipts.BatchKey,"")==old.Request?
                    CookingFermenterReceipts.Phase.Accepted:CookingFermenterReceipts.Phase.Uncertain;
                Save(zdo,old);Reconcile(f,old);Reply(rpc,Message.Result,f,old,saved);
            }
            catch(Exception error){Fault(error);}
        }
        private static void Reconcile(Fermenter f,CookingFermenterReceipts.Input row)
        {
            if(row==null || row.XpSerial<=0 || !Owner(f))return;
            var zdo=f.m_nview.GetZDO();
            if(zdo.GetString(CookingFermenterReceipts.BatchKey,"")!=row.Request ||
                zdo.GetString(CookingFermenterReceipts.SourceKey,"")!=row.Source || f.GetContent()!=row.Hash)return;
            if(row.State!=CookingFermenterReceipts.Phase.Accepted && row.State!=CookingFermenterReceipts.Phase.Committing && row.State!=CookingFermenterReceipts.Phase.Uncertain)return;
            if(!MeadStageExperience.ConfirmAcceptedInput(f,MeadStageExperience.EventIdentity(f,row.XpSerial,2),row.XpSerial,row.Hash,row.Actor))return;
            row.State=CookingFermenterReceipts.Phase.Accepted;Save(zdo,row);
        }
        private static ZPackage PacketFrom(Message mode,ZDOID uid,string token,string raw,float tapFactor)
        {var p=new ZPackage();p.Write(2);p.Write((int)mode);p.Write(uid);p.Write(token);p.Write(tapFactor);p.Write(raw);return p;}
        private static void ClientReceive(ZPackage data)
        {
            if(data.ReadInt()!=2)return;var mode=(Message)data.ReadInt();var uid=data.ReadZDOID();string token=data.ReadString();float tapFactor=data.ReadSingle();string raw=data.ReadString();
            if(mode==Message.HelloReply){SupportedServer=ZNet.instance.GetServerRPC();Trace("hello:client-confirmed");return;}
            var state=Pending;if(state==null || state.Station?.m_nview?.GetZDO()?.m_uid!=uid || !MeadStationIdentity.ValidToken(token))return;
            var receipt=CookingFermenterReceipts.DecodeInput(raw);
            if(data.GetPos()!=data.Size() || receipt==null || !state.Receipt.Same(receipt))return;
            if(state.Token!=null && state.Token!=token)return;
            state.Token=token;
            Player player=Player.m_localPlayer;
            if(player==null || player.GetPlayerID()!=state.Receipt.Actor)return;
            if(state.Recovered)
            {
                // Ordinary native saves may capture this RAM journal before or
                // after debit. Restored state is never proof for inventory replay.
                Trace("recovery:"+receipt.State+":no-inventory-replay");
                if(receipt.State==CookingFermenterReceipts.Phase.Accepted || receipt.State==CookingFermenterReceipts.Phase.Rejected)
                {player.m_customData.Remove(Journal);Pending=null;return;}
                if(receipt.State==CookingFermenterReceipts.Phase.Reserved)
                    Send(Message.Cancel,state.Station,state.Receipt,token);
                return; // Committing/Uncertain remains fail-closed, query only.
            }
            if(mode==Message.Reserved && !state.Debited)
            {
                if(player.IsDead() || player.IsTeleporting() || state.Station==null ||
                    Vector3.Distance(player.transform.position,state.Station.transform.position)>5f ||
                    !WorkshopWorldRecords.WardAllows(player.GetPlayerID(),state.Station.transform.position) ||
                    state.Item==null || !player.GetInventory().GetAllItems().Contains(state.Item) || state.Item.m_stack<1 ||
                    CookingProductRecord.Capture(state.Item).Encode()!=state.Receipt.Source)
                {Send(Message.Cancel,state.Station,state.Receipt,token);return;}
                state.ItemBytes=ItemBytes(state.Item);
                state.BeforeBytes=InventoryBytes(player.GetInventory());
                var expected=new Inventory("Cooking debit proof",null,player.GetInventory().GetWidth(),player.GetInventory().GetHeight());
                expected.Load(new ZPackage(Convert.FromBase64String(state.BeforeBytes)));
                var expectedItem=expected.GetItemAt(state.Item.m_gridPos.x,state.Item.m_gridPos.y);
                if(expectedItem==null || !expected.RemoveOneItem(expectedItem))throw new InvalidOperationException("Exact debit proof could not be prepared.");
                state.AfterBytes=InventoryBytes(expected);WriteJournal(state,false);
                if(!player.GetInventory().RemoveOneItem(state.Item))
                {player.m_customData.Remove(Journal);Send(Message.Cancel,state.Station,state.Receipt,token);return;}
                state.Debited=true;CommitLive(state);return;
            }
            if(mode==Message.Reserved && state.Debited){CommitLive(state);return;}
            if(mode!=Message.Result)return;
            Trace("client-result:"+receipt.State);
            if(receipt.State==CookingFermenterReceipts.Phase.Reserved)
            {if(state.Debited)CommitLive(state);else Send(Message.Cancel,state.Station,state.Receipt,token);return;}
            if(receipt.State==CookingFermenterReceipts.Phase.Accepted)
            {player.m_customData.Remove(Journal);Pending=null;return;}
            if(receipt.State!=CookingFermenterReceipts.Phase.Rejected)return; // Unknown acceptance never authorizes blind refund.
            if(state.Debited)
            {
                if(!state.Refunded)
                {
                    var item=RestoreItem(state.ItemBytes);
                    if(!CookingInventoryCompatibility.Fits(player.GetInventory(),item,1) || !player.GetInventory().AddItem(item))return;
                    state.Refunded=true;
                }
                if(state.OwnerRouted)
                {
                    WriteJournal(state,true); // Live refund bookkeeping; no profile save is forced.
                }
            }
            player.m_customData.Remove(Journal);Pending=null;
        }
        private static void CommitLive(Client state)
        {
            if(!state.Debited || state.Recovered)return;
            // Explicit performance policy: live debit is authoritative locally;
            // ordinary native saves alone persist character state. No cloud wait.
            WriteJournal(state,true);
            if(state.Refunded)
            {Player.m_localPlayer.m_customData.Remove(Journal);Pending=null;return;}
            Trace("debit:live-commit");
            Send(Message.Commit,state.Station,state.Receipt,state.Token);
        }
        internal static bool Begin(Fermenter f,Humanoid user,ItemDrop.ItemData item)
        {
            if(user!=Player.m_localPlayer)return Refuse("begin:not-local-player");
            if(item?.m_dropPrefab==null)return Refuse("begin:no-input-prefab");
            if(user.IsDead())return Refuse("begin:dead");
            if(f.GetContent()!=0)return Refuse("begin:nonempty");
            if(f.GetItemConversion(item.m_dropPrefab.name.GetStableHashCode())==null)return Refuse("begin:unsupported-input");
            if(!NativeSessionReady())return Refuse("begin:session-not-ready");
            if(Pending!=null)return Refuse("begin:pending-request");
            if(Player.m_localPlayer.m_customData.ContainsKey(Journal))return Refuse("begin:saved-journal");
            try
            {
            var player=Player.m_localPlayer;long sequence=0;
            string key=CounterKey+"."+ZNet.instance.GetWorldUID();
            if(player.m_customData.TryGetValue(key,out string raw) && !long.TryParse(raw,out sequence))return false;
            if(sequence==long.MaxValue)return false;sequence++;player.m_customData[key]=sequence.ToString(CultureInfo.InvariantCulture);
            var row=new CookingFermenterReceipts.Input{Actor=player.GetPlayerID(),Sequence=sequence,Request=Guid.NewGuid().ToString("N"),Hash=item.m_dropPrefab.name.GetStableHashCode(),
                Source=CookingProductRecord.Capture(item).Encode(),Cheated=item.m_cheated,State=CookingFermenterReceipts.Phase.Reserved};
            CookingFermenterReceipts.Encode(row);
            Pending=new Client{Station=f,Item=item,Receipt=row,Last=Time.time,Started=Time.time,OwnerRouted=true};Trace("begin:offer-dispatch");Send(Message.Offer,f,row);return true;
            }catch(Exception error){Fault(error);return false;}
        }
        internal static void RecordAccepted(Fermenter f)
        {
            if(!Owner(f) || ActiveProduct==null)return;
            f.m_nview.GetZDO().Set(CookingFermenterReceipts.SourceKey,ActiveProduct.Encode());
            f.m_nview.GetZDO().Set(CookingFermenterReceipts.BatchKey,ActiveRequest);
        }
        internal static bool Tap(Fermenter f,Humanoid user)
        {if(user!=Player.m_localPlayer || !NativeSessionReady())return false;Send(Message.Tap,f);return true;}
        private static string InventoryBytes(Inventory inventory)
        {var data=new ZPackage();inventory.Save(data);return Convert.ToBase64String(data.GetArray());}
        private static string ItemBytes(ItemDrop.ItemData source)
        {
            var inv=new Inventory("Cooking input escrow",null,1,1);var item=source.Clone();item.m_stack=1;item.m_gridPos=new Vector2i(0,0);
            if(!inv.AddItem(item))throw new InvalidOperationException("Input escrow could not capture exact item.");
            var data=new ZPackage();inv.Save(data);return Convert.ToBase64String(data.GetArray());
        }
        private static ItemDrop.ItemData RestoreItem(string bytes)
        {var inv=new Inventory("Cooking input escrow",null,1,1);inv.Load(new ZPackage(Convert.FromBase64String(bytes)));return inv.GetAllItems().Single();}
        private static void WriteJournal(Client state,bool debited)
        {
            var data=new ZPackage();data.Write(state.OwnerRouted?4:2);data.Write(ZNet.instance.GetWorldUID());data.Write(state.Token);
            data.Write(CookingFermenterReceipts.Encode(state.Receipt));data.Write(state.ItemBytes);data.Write(state.BeforeBytes??"");data.Write(state.AfterBytes??"");data.Write(debited);if(state.OwnerRouted)data.Write(state.Refunded);
            Player.m_localPlayer.m_customData[Journal]=Convert.ToBase64String(data.GetArray());
        }
        private static float Next {get=>State.Next;set=>State.Next=value;}
        internal static void Tick(Fermenter f)
        {
            var net=ZNet.instance;if(net==null)return;
            EnsureSession();
            // Clear transient session state at teardown without touching saved journals.
            if(ZNet.m_world==null)return;
            TickNativeProbe(f);
            if(Owner(f))
            {
                foreach(string a in f.m_nview.GetZDO().GetString("vm.cooking.fermenter.actors_v1","").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries).Take(CookingFermenterReceipts.MaxActors))
                    if(long.TryParse(a,out long actor)){try{Reconcile(f,Read(f.m_nview.GetZDO(),actor));}catch(Exception error){Fault(error);}}
            }
            var player=Player.m_localPlayer;
            if(player!=null && Pending==null && player.m_customData.TryGetValue(Journal,out string saved))
            {
                try
                {
                    var data=new ZPackage(Convert.FromBase64String(saved));
                    int schema=data.ReadInt();if((schema!=2 && schema!=3 && schema!=4) || data.ReadLong()!=net.GetWorldUID())return;
                    string token=data.ReadString();var row=CookingFermenterReceipts.DecodeInput(data.ReadString());string bytes=data.ReadString(),before=data.ReadString(),after=data.ReadString();bool debited=data.ReadBool();bool refunded=schema>=3 && data.ReadBool();
                    if(data.GetPos()!=data.Size() || row==null || row.Actor!=player.GetPlayerID())return;
                    // No before/after inventory inference for restored journals:
                    // stale/native/cloud snapshots have no per-input durability.
                    if(MeadStationIdentity.TryGet(f.m_nview.GetZDO(),net.GetWorldUID(),out string found) && found==token)
                        // Retain exact identity for query/cancel only. No inventory replay,
                        // Commit or refund is authorized by a restored journal.
                        Pending=new Client{Station=f,Token=token,Receipt=row,ItemBytes=bytes,BeforeBytes=before,AfterBytes=after,Debited=debited,Recovered=true,OwnerRouted=true,Refunded=refunded,Last=-10,Started=Time.time};
                }
                catch(Exception error){Fault(error);}
            }
            if(Time.time<Next)return;Next=Time.time+2f;
            if(!net.IsServer() && !Ready){Trace("hello:client-dispatch");Send(Message.Hello,null);}
            if(Pending!=null && Player.m_localPlayer?.GetPlayerID()!=Pending.Receipt.Actor)Pending=null;
            if(Pending!=null && !Pending.Debited && !player.m_customData.ContainsKey(Journal) && Time.time-Pending.Started>6f)
            {Trace("retry:predebit-timeout-cancel");Send(Message.Cancel,Pending.Station,Pending.Receipt,Pending.Token);Pending=null;}
            if(Pending!=null && (Pending.OwnerRouted?NativeSessionReady():Ready) && Time.time-Pending.Last>=2f)
            {Pending.Last=Time.time;if(Pending.Debited && !Pending.Recovered)CommitLive(Pending);else Send(Message.Query,Pending.Station,Pending.Receipt,Pending.Token);}
        }
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class CookingProducerNetworkRegistrationPatch
    {private static void Postfix(ZNetPeer peer)=>CookingFermenterProtocol.Register(peer);}
    [HarmonyPatch(typeof(NetworkSync),"OnHello")]
    internal static class CookingProducerCompatibilityPatch
    {
        private static void Postfix(ZRpc rpc,string serverVersion,string manifest)=>
            CookingFermenterProtocol.ObserveCompatibleHello(rpc,serverVersion,manifest);
    }
    [HarmonyPatch(typeof(Fermenter),"Awake")]
    internal static class CookingProducerNativeProbeRegistrationPatch
    {private static void Postfix(Fermenter __instance)=>CookingFermenterProtocol.RegisterNativeProbe(__instance);}
    [HarmonyPatch(typeof(Fermenter),"AddItem")]
    internal static class CookingProducerInputPatch
    {
        [HarmonyPriority(Priority.First)]private static bool Prefix(Fermenter __instance,Humanoid user,ItemDrop.ItemData item,ref bool __result)
        {
            if (CookingFermenterProtocol.NativeInputRollback)
            {
                return CookingFermenterProtocol.RouteNativeInput(__instance,user,item,ref __result);
            }
            if(!MasteryPlugin.Settings.Enabled.Value){if(CookingFermenterOutput.Removable(__instance))return true;__result=false;return false;}__result=CookingFermenterProtocol.Begin(__instance,user,item);return false;
        }
    }
    [HarmonyPatch(typeof(Fermenter),"RPC_AddItem")]
    internal static class CookingProducerNativeInputGuardPatch
    {
        [HarmonyPriority(Priority.First)]private static bool Prefix(Fermenter __instance,out int __state)
        {
            __state=__instance.GetContent();
            return CookingFermenterProtocol.NativeInputRollback ?
                (CookingFermenterProtocol.NativeInputAllowed || CookingFermenterOutput.Removable(__instance)) :
                (!MasteryPlugin.Settings.Enabled.Value?CookingFermenterOutput.Removable(__instance):CookingFermenterProtocol.NativeInputAllowed);
        }
        [HarmonyPriority(Priority.First)]private static void Postfix(Fermenter __instance,int __1,int __state)
        {
            if(CookingFermenterProtocol.NativeInputRollback && !CookingFermenterProtocol.NativeInputAllowed && __instance.m_nview?.IsOwner()==true &&
                __state==0 && __instance.GetContent()==__1 && __1!=0)
            {
                // Native RPC carries no immutable item lineage. A fresh accepted
                // batch must not inherit the previous batch's author/mastery.
                var from=__instance.GetItemConversion(__1)?.m_from;
                if(from!=null)
                {
                    var unknown=new CookingProductRecord{Prefab=GatheringProgressionService.Normalize(from.gameObject.name),AuthorName="",Data=new Dictionary<string,string>()};
                    var zdo=__instance.m_nview.GetZDO();
                    zdo.Set(CookingFermenterReceipts.SourceKey,unknown.Encode());
                    zdo.Set(FermenterBonusService.CookingTierKey,0);
                    zdo.Set(FermenterBonusService.AuthorKey,0L);
                }
                return;
            }
            CookingFermenterProtocol.RecordAccepted(__instance);
        }
    }
    [HarmonyPatch(typeof(Fermenter),"SlowUpdate")]
    internal static class CookingProducerRetryPatch
    {private static void Postfix(Fermenter __instance)=>CookingFermenterProtocol.Tick(__instance);}
    [HarmonyPatch(typeof(Fermenter),"Interact")]
    internal static class CookingProducerTapRoutePatch
    {
        [HarmonyPriority(Priority.First)]private static bool Prefix(Fermenter __instance,Humanoid user,bool hold,ref bool __result)
        {
            if(!MasteryPlugin.Settings.Enabled.Value)
            {if(CookingFermenterOutput.Removable(__instance))return true;__result=false;return false;}
            // Extraction is a native Valheim interaction. The custom tap request
            // could fail closed without any response and made ready batches inert.
            // Durable pending output remains protected by the RPC/output guards.
            return true;
        }
    }
    // Keep native profile/escrow durability; only these synchronous checkpoints
    // skip the HUD profile-save indicator. Other save observers and world saves run normally.
    internal static class CookingCharacterCheckpoint
    {
        [ThreadStatic] private static bool _suppressProfileHud;
        internal static bool SuppressProfileHud => _suppressProfileHud;
        internal static bool Persist(Player player)
        {
            bool previous = _suppressProfileHud;
            _suppressProfileHud = true;
            try { return GoldCharacterSave.Persist(player); }
            finally { _suppressProfileHud = previous; }
        }
    }
    [HarmonyPatch(typeof(Hud), "ProfileSaveStarted")]
    internal static class CookingCharacterCheckpointStartedPatch
    {
        private static bool Prefix() => !CookingCharacterCheckpoint.SuppressProfileHud;
    }
    [HarmonyPatch(typeof(Hud), "ProfileSaveFinished")]
    internal static class CookingCharacterCheckpointFinishedPatch
    {
        private static bool Prefix() => !CookingCharacterCheckpoint.SuppressProfileHud;
    }

}

