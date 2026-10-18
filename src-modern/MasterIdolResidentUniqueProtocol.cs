using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Unique-action authority only: genuine peer ZRpc -> server -> current actor owner -> trusted server cue.
    internal static class MasterIdolResidentUniqueProtocol
    {
        private sealed class Pending
        {internal ResidentUniqueTicket Ticket;internal double Until;internal bool Active;internal ResidentUniqueOutcome Outcome;}
        private static readonly Dictionary<ZDOID,Pending> Requests=new Dictionary<ZDOID,Pending>();
        private static readonly Dictionary<string,double> Receipts=new Dictionary<string,double>(StringComparer.Ordinal);
        private static readonly Dictionary<ZDOID,double> Quiet=new Dictionary<ZDOID,double>();
        private static readonly Dictionary<ZDOID,byte> Turns=new Dictionary<ZDOID,byte>();
        private static readonly List<ZDOID> StaleActors=new List<ZDOID>(4);private static readonly List<string> StaleReceipts=new List<string>(64);
        private static ZNet Session;private static ZDOMan Manager;private static ulong Generation;private static long World;private static float Next;
        internal static void Register(ZNetPeer peer)
        {
            var rpc=peer?.m_rpc;if(rpc==null)return;
            rpc.Register<ZPackage>(MasterIdolResidentUniqueWire.RequestRpc,(source,packet)=>ReceiveRequest(source,packet,false));
            rpc.Register<ZPackage>(MasterIdolResidentUniqueWire.OwnerRpc,(source,packet)=>ReceiveOwner(source,packet,false));
            rpc.Register<ZPackage>(MasterIdolResidentUniqueWire.CueRpc,(source,packet)=>ReceiveCue(source,packet,false));
        }
        private static bool Context()
        {
            var net=ZNet.instance;var manager=ZDOMan.instance;if(net?.GetWorld()==null||manager==null||!MasterIdolLoadIdentity.LoadReady)return false;
            if(Session!=net||Manager!=manager||Generation!=MasterIdolLoadIdentity.Generation||World!=net.GetWorldUID())
            {Requests.Clear();Receipts.Clear();Quiet.Clear();Turns.Clear();Session=net;Manager=manager;Generation=MasterIdolLoadIdentity.Generation;World=net.GetWorldUID();Next=0;}
            return true;
        }
        private static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        private static bool Living(ZDO data)=>data!=null&&float.IsFinite(data.GetFloat(ZDOVars.s_health,1))&&data.GetFloat(ZDOVars.s_health,1)>0;
        private static bool ServerChannel(ZRpc rpc,bool local)
            =>local?Session.IsServer():!Session.IsServer()&&Session.GetServerPeer()?.IsReady()==true&&Session.GetServerPeer().m_rpc==rpc&&NetworkSync.HasCurrentServerSettings(rpc);
        private static ZNetPeer Peer(ZRpc rpc)
        {
            var peer=Session.GetPeer(rpc);return peer?.IsReady()==true&&peer.m_uid!=0&&peer.m_rpc==rpc&&NetworkSync.HasPeerSettings(peer)?peer:null;
        }
        private static ZDO PlayerData(ZNetPeer peer)
        {
            var data=OwnerSkillAuthority.ResolveCharacterData(peer);
            return data!=null&&ReferenceEquals(Manager.GetZDO(data.m_uid),data)&&data.GetLong(ZDOVars.s_playerID,0)!=0&&Living(data)&&!data.GetBool(ZDOVars.s_debugFly,false)&&Finite(data.GetPosition())?data:null;
        }
        private static ZDO LocalPlayerData()
        {
            var player=Player.m_localPlayer;var view=player?.m_nview;var data=view?.GetZDO();
            return player!=null&&!player.IsDead()&&!player.InGhostMode()&&!player.IsDebugFlying()&&view?.IsValid()==true&&view.IsOwner()&&data!=null&&
                ReferenceEquals(Manager.GetZDO(data.m_uid),data)&&data.GetOwner()==ZNet.GetUID()&&player.GetPlayerID()!=0&&data.GetLong(ZDOVars.s_playerID,0)==player.GetPlayerID()&&Finite(data.GetPosition())?data:null;
        }
        private static ZDO CurrentPlayer(ZDO data)
        {
            if(data==null)return null;
            if(data.GetOwner()==ZNet.GetUID())return ReferenceEquals(LocalPlayerData(),data)?data:null;
            var peer=Session.GetPeer(data.GetOwner());return peer?.IsReady()==true&&NetworkSync.HasPeerSettings(peer)&&ReferenceEquals(PlayerData(peer),data)?data:null;
        }
        private static bool Guard(ZDO data,out int family,out MasterIdolEffectZone home)
        {
            family=-1;home=null;if(!Session.IsServer()||!MasterIdolForestGuardState.Ready||!MasterIdolForestGuardState.ModuleBorn(data)||!Living(data)||!ReferenceEquals(Manager.GetZDO(data.m_uid),data))return false;
            family=MasterIdolForestGuardState.Family(data);if(family<0||!MasterIdolForestGuardState.Slots.TryGetValue(MasterIdolForestRules.Families[family],out var record)||record.Death!=0||!record.Valid()||!MasterIdolForestGuardState.Matches(record,data))return false;
            home=MasterIdolEffectZones.ResidentHome(record.Uid,record.Family);
            return home?.Type=="BlackForest"&&MasterIdolNativeTaming.SameHome(record.Home,home.Id)&&MasterIdolNativeTaming.SameHome(data.GetString(MasterIdolNativeTaming.HomeKey,""),home.Id)&&Finite(data.GetPosition())&&home.Contains(data.GetPosition());
        }
        private static bool PlayerAccess(ZDO actor,ZDO player,MasterIdolEffectZone home)
            =>player!=null&&Living(player)&&!player.GetBool(ZDOVars.s_debugFly,false)&&Finite(player.GetPosition())&&(actor.GetPosition()-player.GetPosition()).sqrMagnitude<=25&&home.Contains(player.GetPosition())&&MasterIdolWorldIndex.WardAllows(player.GetLong(ZDOVars.s_playerID,0),actor.GetPosition());
        private static bool Emote(ZDO player,int expected,out byte choice)
        {
            choice=0;int index=MasterIdolResidentUniqueRules.Gesture(player.GetString(ZDOVars.s_emote,""));
            if(expected<=0||player.GetInt(ZDOVars.s_emoteID,0)!=expected||index<0)return false;choice=(byte)index;return true;
        }
        internal static bool Send(Character actor,Player player,ResidentUniqueKind kind,int emoteId)
        {
            if(!Context()||actor==null||player==null)return false;var data=actor.m_nview?.GetZDO();var pd=player.m_nview?.GetZDO();
            if(!MasterIdolForestGuardState.ModuleBorn(data)||pd==null||!ReferenceEquals(Manager.GetZDO(data.m_uid),data)||!ReferenceEquals(Manager.GetZDO(pd.m_uid),pd))return false;
            if(kind==ResidentUniqueKind.Family&&!ReferenceEquals(LocalPlayerData(),pd)||kind==ResidentUniqueKind.Gesture&&actor.m_nview?.IsOwner()!=true)return false;
            var request=new ResidentUniqueRequest{Kind=kind,World=World,Actor=data.m_uid,ActorRevision=data.OwnerRevision,Player=pd.m_uid,PlayerRevision=pd.OwnerRevision,EmoteId=emoteId,Nonce=Guid.NewGuid().ToString("N"),Stamp=Session.GetTime().Ticks};
            var packet=new ZPackage(MasterIdolResidentUniqueWire.Request(request));
            if(Session.IsServer())ReceiveRequest(null,packet,true);
            else{var server=Session.GetServerPeer();if(server?.IsReady()!=true||!NetworkSync.HasCurrentServerSettings(server.m_rpc))return false;server.m_rpc.Invoke(MasterIdolResidentUniqueWire.RequestRpc,packet);}
            return true;
        }
        private static void ReceiveRequest(ZRpc rpc,ZPackage packet,bool local)
        {
            try
            {
                if(!Context()||!Session.IsServer()||packet==null)return;var request=MasterIdolResidentUniqueWire.Request(packet.GetArray());
                if(request.World!=World||!MasterIdolResidentUniqueRules.Fresh(request.Stamp,Session.GetTime().Ticks,MasterIdolResidentUniqueRules.RequestSeconds))return;
                var actor=Manager.GetZDO(request.Actor);var player=Manager.GetZDO(request.Player);
                if(!Guard(actor,out int family,out var home)||actor.OwnerRevision!=request.ActorRevision||player==null||player.OwnerRevision!=request.PlayerRevision||!ReferenceEquals(CurrentPlayer(player),player)||!PlayerAccess(actor,player,home))return;
                var peer=local?null:Peer(rpc);long sender=local?ZNet.GetUID():peer?.m_uid??0;if(sender==0)return;
                if(request.Kind==ResidentUniqueKind.Family){if(sender!=player.GetOwner()||local&&!ReferenceEquals(LocalPlayerData(),player))return;}
                else if(sender!=actor.GetOwner())return;
                Tick();double now=Session.GetTimeSeconds();string receipt=actor.m_uid+":"+request.Nonce;
                if(Receipts.ContainsKey(receipt)||Receipts.Count>=MasterIdolResidentUniqueRules.MaximumReceipts)return;
                Receipts.Add(receipt,now+MasterIdolResidentUniqueRules.ReceiptSeconds);
                byte choice=0;if(request.Kind==ResidentUniqueKind.Gesture&&!Emote(player,request.EmoteId,out choice))return;
                if(request.Kind==ResidentUniqueKind.Gesture&&!MasterIdolResidentUniqueRules.MotionApproved(family,request.Kind,choice))return;
                if(request.Kind==ResidentUniqueKind.Family){Turns.TryGetValue(actor.m_uid,out byte start);if(!MasterIdolResidentUniqueRules.TryApprovedFamilyChoice(family,start,out choice))return;}
                var ticket=new ResidentUniqueTicket{Kind=request.Kind,Choice=choice,World=World,Actor=actor.m_uid,ActorOwner=actor.GetOwner(),ActorRevision=actor.OwnerRevision,ActorNonce=actor.GetString(MasterIdolForestGuardState.NonceKey,""),Born=actor.GetLong(MasterIdolForestGuardState.BornKey,0),Home=home.Id,
                    Player=player.m_uid,PlayerOwner=player.GetOwner(),PlayerRevision=player.OwnerRevision,PlayerId=player.GetLong(ZDOVars.s_playerID,0),EmoteId=request.EmoteId,Nonce=request.Nonce,Stamp=Session.GetTime().Ticks};
                if(Requests.ContainsKey(actor.m_uid)||Requests.Count>=MasterIdolResidentUniqueRules.MaximumPending||Quiet.TryGetValue(actor.m_uid,out double until)&&now<until){Denied(ticket,1);return;}
                if(ticket.Kind==ResidentUniqueKind.Family)Turns[actor.m_uid]=(byte)((choice+1)%10);
                Requests.Add(actor.m_uid,new Pending{Ticket=ticket,Until=now+MasterIdolResidentUniqueRules.DecisionSeconds});Quiet[actor.m_uid]=now+MasterIdolResidentUniqueRules.ManualQuiet;
                var grant=new ZPackage(MasterIdolResidentUniqueWire.Ticket(0,ticket));
                if(ticket.ActorOwner==ZNet.GetUID())ReceiveOwner(null,grant,true);
                else{var owner=Session.GetPeer(ticket.ActorOwner);if(owner?.IsReady()!=true||!NetworkSync.HasPeerSettings(owner)){Requests.Remove(actor.m_uid);Denied(ticket,2);return;}owner.m_rpc.Invoke(MasterIdolResidentUniqueWire.OwnerRpc,grant);}
            }
            catch(Exception){MasterIdolResidentMetrics.Count("unique.request-denied");}
        }
        internal static bool Current(ResidentUniqueTicket ticket,bool serverProof,out Character actor,out Player player,bool requireEmote=true)
        {
            actor=null;player=null;if(!Context()||ticket==null||ticket.World!=World)return false;
            var data=Manager.GetZDO(ticket.Actor);var pd=Manager.GetZDO(ticket.Player);
            if(!MasterIdolForestGuardState.ModuleBorn(data)||!MasterIdolResidentUniqueRules.MotionApproved(MasterIdolForestGuardState.Family(data),ticket.Kind,ticket.Choice)||!Living(data)||data.GetOwner()!=ticket.ActorOwner||data.OwnerRevision!=ticket.ActorRevision||data.GetString(MasterIdolForestGuardState.NonceKey,"")!=ticket.ActorNonce||data.GetLong(MasterIdolForestGuardState.BornKey,0)!=ticket.Born||
                !MasterIdolNativeTaming.SameHome(data.GetString(MasterIdolNativeTaming.HomeKey,""),ticket.Home)||pd==null||pd.GetOwner()!=ticket.PlayerOwner||pd.OwnerRevision!=ticket.PlayerRevision||pd.GetLong(ZDOVars.s_playerID,0)!=ticket.PlayerId)return false;
            MasterIdolEffectZone home;
            if(serverProof){if(!Guard(data,out _,out home)||!ReferenceEquals(CurrentPlayer(pd),pd))return false;}
            else home=MasterIdolEffectZones.ResidentHome(ticket.Actor.ToString(),MasterIdolForestRules.Families[MasterIdolForestGuardState.Family(data)]);
            if(home==null||home.Id!=ticket.Home||!PlayerAccess(data,pd,home)||requireEmote&&ticket.Kind==ResidentUniqueKind.Gesture&&(!Emote(pd,ticket.EmoteId,out _)||MasterIdolResidentUniqueRules.Gesture(pd.GetString(ZDOVars.s_emote,""))!=ticket.Choice))return false;
            actor=ZNetScene.instance?.FindInstance(ticket.Actor)?.GetComponent<Character>();player=ZNetScene.instance?.FindInstance(ticket.Player)?.GetComponent<Player>();
            return true; // Server authorization uses current ZDOs even when neither body is instantiated locally.
        }
        private static void ReceiveOwner(ZRpc rpc,ZPackage packet,bool local)
        {
            try
            {
                if(!Context()||packet==null)return;var ticket=MasterIdolResidentUniqueWire.Ticket(packet.GetArray(),out byte op,out var outcome,out byte reason,out long stamp);
                if(op==0)
                {
                    if(!ServerChannel(rpc,local)||outcome!=ResidentUniqueOutcome.Visual||reason!=0||stamp!=0||!MasterIdolResidentUniqueRules.Fresh(ticket.Stamp,Session.GetTime().Ticks,MasterIdolResidentUniqueRules.DecisionSeconds)||ticket.ActorOwner!=ZNet.GetUID())return;
                    outcome=MasterIdolResidentUniqueActions.Accept(ticket);Decision(ticket,outcome,0,false);return;
                }
                if(!Session.IsServer()||!Requests.TryGetValue(ticket.Actor,out var pending)||!MasterIdolResidentUniqueWire.Same(ticket,pending.Ticket)||Session.GetTimeSeconds()>pending.Until)return;
                var peer=local?null:Peer(rpc);long sender=local?ZNet.GetUID():peer?.m_uid??0;
                if(sender!=ticket.ActorOwner||stamp!=0||!Current(ticket,true,out _,out _,!pending.Active))return;
                int family=MasterIdolForestGuardState.Family(Manager.GetZDO(ticket.Actor));
                if(outcome==ResidentUniqueOutcome.HealRequested&&(ticket.Kind!=ResidentUniqueKind.Family||family!=3))return;
                if(op==2){if(pending.Active&&outcome==pending.Outcome){Publish(2,ticket,pending.Outcome,reason,Session.GetTime().Ticks);Requests.Remove(ticket.Actor);}return;}
                if(pending.Active)return;
                if(outcome==ResidentUniqueOutcome.Denied){Requests.Remove(ticket.Actor);Denied(ticket,reason);return;}
                pending.Active=true;pending.Outcome=outcome;pending.Until=Session.GetTimeSeconds()+MasterIdolResidentUniqueRules.Seconds(MasterIdolForestGuardState.Family(Manager.GetZDO(ticket.Actor)),ticket.Kind,ticket.Choice)+1;
                Publish(0,ticket,outcome,0,Session.GetTime().Ticks);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("unique.owner-denied");}
        }
        internal static void Decision(ResidentUniqueTicket ticket,ResidentUniqueOutcome outcome,byte reason,bool ended)
        {
            if(!Context()||ticket==null||ticket.ActorOwner!=ZNet.GetUID())return;
            var packet=new ZPackage(MasterIdolResidentUniqueWire.Ticket(ended?(byte)2:(byte)1,ticket,outcome,reason));
            if(Session.IsServer())ReceiveOwner(null,packet,true);
            else if(Session.GetServerPeer()?.IsReady()==true&&NetworkSync.HasCurrentServerSettings(Session.GetServerPeer().m_rpc))Session.GetServerPeer().m_rpc.Invoke(MasterIdolResidentUniqueWire.OwnerRpc,packet);
        }
        private static void Publish(byte op,ResidentUniqueTicket ticket,ResidentUniqueOutcome outcome,byte reason,long stamp)
        {
            var bytes=MasterIdolResidentUniqueWire.Ticket(op,ticket,outcome,reason,stamp);ReceiveCue(null,new ZPackage(bytes),true);
            foreach(var peer in Session.GetPeers())if(peer?.IsReady()==true&&NetworkSync.HasPeerSettings(peer))peer.m_rpc.Invoke(MasterIdolResidentUniqueWire.CueRpc,new ZPackage(bytes));
        }
        private static void Denied(ResidentUniqueTicket ticket,byte reason)
        {
            var packet=new ZPackage(MasterIdolResidentUniqueWire.Ticket(1,ticket,ResidentUniqueOutcome.Denied,reason,Session.GetTime().Ticks));
            if(ticket.PlayerOwner==ZNet.GetUID())ReceiveCue(null,packet,true);
            else{var peer=Session.GetPeer(ticket.PlayerOwner);if(peer?.IsReady()==true&&NetworkSync.HasPeerSettings(peer))peer.m_rpc.Invoke(MasterIdolResidentUniqueWire.CueRpc,packet);}
        }
        private static void ReceiveCue(ZRpc rpc,ZPackage packet,bool local)
        {
            try
            {
                if(!Context()||!ServerChannel(rpc,local)||packet==null)return;var ticket=MasterIdolResidentUniqueWire.Ticket(packet.GetArray(),out byte op,out var outcome,out byte reason,out long stamp);
                if(!MasterIdolResidentUniqueRules.Fresh(stamp,Session.GetTime().Ticks,2)||op==0&&reason!=0||op==1&&outcome!=ResidentUniqueOutcome.Denied)return;
                if(op==1)
                {if(ticket.PlayerOwner==ZNet.GetUID()&&ReferenceEquals(LocalPlayerData(),Manager.GetZDO(ticket.Player)))Player.m_localPlayer?.Message(MessageHud.MessageType.Center,GoldUiLocalization.Text("Not right now.","Зараз не вийде."));return;}
                if(op==2){MasterIdolResidentUniqueActions.EndCue(ticket);return;}
                if(outcome==ResidentUniqueOutcome.Denied||!Current(ticket,Session.IsServer(),out var actor,out var player)||actor==null||player==null||outcome==ResidentUniqueOutcome.HealRequested&&(ticket.Kind!=ResidentUniqueKind.Family||MasterIdolForestGuardState.Family(Manager.GetZDO(ticket.Actor))!=3))return;
                MasterIdolResidentUniqueActions.BeginCue(actor,player,ticket,outcome,stamp);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("unique.cue-denied");}
        }
        internal static void Tick()
        {
            if(!Context()||!Session.IsServer()||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.25f;double now=Session.GetTimeSeconds();
            StaleActors.Clear();foreach(var pair in Requests)if(now>pair.Value.Until||!Current(pair.Value.Ticket,true,out _,out _,!pair.Value.Active))StaleActors.Add(pair.Key);
            foreach(var id in StaleActors){var pending=Requests[id];Requests.Remove(id);if(pending.Active)Publish(2,pending.Ticket,pending.Outcome,3,Session.GetTime().Ticks);else Denied(pending.Ticket,3);}
            StaleActors.Clear();foreach(var pair in Quiet)if(now>=pair.Value)StaleActors.Add(pair.Key);foreach(var id in StaleActors)Quiet.Remove(id);
            StaleActors.Clear();foreach(var pair in Turns)if(!Guard(Manager.GetZDO(pair.Key),out _,out _))StaleActors.Add(pair.Key);foreach(var id in StaleActors)Turns.Remove(id);
            StaleReceipts.Clear();foreach(var pair in Receipts)if(now>=pair.Value)StaleReceipts.Add(pair.Key);foreach(string key in StaleReceipts)Receipts.Remove(key);
        }
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class MasterIdolResidentUniqueRegisterPatch{private static void Postfix(ZNetPeer peer)=>MasterIdolResidentUniqueProtocol.Register(peer);}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolResidentUniqueTickPatch{private static void Postfix()=>MasterIdolResidentUniqueProtocol.Tick();}
}
