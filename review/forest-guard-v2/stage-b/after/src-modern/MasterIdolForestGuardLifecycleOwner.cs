using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal enum ForestGuardOwnerAction:byte { ApplyDesired }
    internal enum ForestGuardOwnerOutcome:byte { Applied,Deferred }
    // Physical owner application only. No visual clock, cargo item writer,
    // authority transfer, birth factory or persistent-life writer exists here.
    internal static class MasterIdolForestGuardLifecycleOwner
    {
        const string CommandRpc="VM_BF_LifecycleOwner_v2",AckRpc="VM_BF_LifecycleOwnerAck_v2";const int MaximumBytes=512;
        sealed class Pending {internal MasterIdolForestGuardLifecycle.State Row;internal string Epoch;internal Character Actor;internal ZRpc Reply;internal bool Applied;}
        static readonly Dictionary<ZDOID,Pending> Running=new Dictionary<ZDOID,Pending>();
        static ZNet Net;static ZDOMan Manager;static ulong Generation;static long World;
        static bool Context()
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(Net!=net||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||World!=world)
            {Running.Clear();Net=net;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;World=world;}
            return net!=null&&Manager!=null&&world!=0&&MasterIdolLoadIdentity.LoadReady;
        }
        internal static bool Same(MasterIdolForestGuardLifecycle.State a,MasterIdolForestGuardLifecycle.State b)=>MasterIdolForestGuardLifecycle.SameIntent(a,b);
        internal static bool Native(MasterIdolForestGuardLifecycle.State row,ZDO data)=>data!=null&&data.GetLong(MasterIdolForestGuardLifecycle.WorldKey,0)==ZNet.instance?.GetWorldUID()&&
            data.GetLong(MasterIdolForestGuardLifecycle.CycleKey,0)==row.Cycle&&data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0)==row.IntentRevision&&
            data.GetString(MasterIdolForestGuardLifecycle.AttemptKey,"")==row.Attempt&&data.GetInt(MasterIdolForestGuardLifecycle.PhaseKey,0)==(int)row.Status;
        static byte[] Encode(MasterIdolForestGuardLifecycle.State row,string epoch,ForestGuardOwnerOutcome outcome)
        {
            var body=MasterIdolForestGuardLifecycle.Encode(new MasterIdolForestGuardLifecycle.Snapshot{World=World,Epoch=epoch,Token=row.Attempt,Revision=row.IntentRevision,Rows=new[]{row}});
            if(body.Length+2>MaximumBytes)throw new FormatException();var bytes=new byte[body.Length+2];bytes[0]=0;bytes[1]=(byte)outcome;Buffer.BlockCopy(body,0,bytes,2,body.Length);return bytes;
        }
        static bool Decode(ZPackage packet,out MasterIdolForestGuardLifecycle.State row,out string epoch,out ForestGuardOwnerOutcome outcome)
        {
            row=default;epoch=null;outcome=default;if(packet==null||packet.GetPos()!=0||packet.Size()<6||packet.Size()>MaximumBytes)return false;
            var bytes=packet.GetArray();if(bytes[0]!=0||bytes[1]>1)return false;var body=new byte[bytes.Length-2];Buffer.BlockCopy(bytes,2,body,0,body.Length);
            var snapshot=MasterIdolForestGuardLifecycle.Decode(body);if(snapshot.World!=World||snapshot.Rows.Length!=1)return false;
            row=snapshot.Rows[0];epoch=snapshot.Epoch;outcome=(ForestGuardOwnerOutcome)bytes[1];return snapshot.Token==row.Attempt&&snapshot.Revision==row.IntentRevision;
        }
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>(CommandRpc,Command);peer?.m_rpc?.Register<ZPackage>(AckRpc,Ack);}
        static void Command(ZRpc rpc,ZPackage packet)
        {
            if(!Context()||Net.IsServer()||rpc==null||!ReferenceEquals(rpc,Net.GetServerRPC())||!NetworkSync.HasCurrentServerSettings(rpc))return;
            try{if(Decode(packet,out var row,out var epoch,out var outcome)&&outcome==ForestGuardOwnerOutcome.Applied)Apply(row,epoch,rpc);}
            catch(EndOfStreamException){}catch(IOException){}catch(FormatException){}catch(ArgumentException){}
        }
        static void Ack(ZRpc rpc,ZPackage packet)
        {
            if(!Context()||!Net.IsServer()||rpc==null)return;
            ZNetPeer source=null;foreach(var peer in Net.GetPeers())if(peer?.m_rpc==rpc&&peer.IsReady()&&NetworkSync.HasPeerSettings(peer)){source=peer;break;}if(source==null)return;
            try{if(Decode(packet,out var row,out var epoch,out var outcome)&&row.Owner==source.m_uid)MasterIdolForestGuardLifecycleController.OnOwnerOutcome(row,epoch,ForestGuardOwnerAction.ApplyDesired,outcome,source.m_uid);}
            catch(EndOfStreamException){}catch(IOException){}catch(FormatException){}catch(ArgumentException){}
        }
        internal static bool Request(MasterIdolForestGuardLifecycle.State row,ForestGuardOwnerAction action)
        {
            if(!Context()||!Net.IsServer()||action!=ForestGuardOwnerAction.ApplyDesired)return false;
            var data=Manager.GetZDO(row.Id);
            if(row.Owner==0||!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var accepted,out var epoch)||!Same(accepted,row))return false;
            if(row.Owner==ZNet.GetUID()){Apply(row,epoch,null);return true;}
            foreach(var peer in Net.GetPeers())if(peer?.m_uid==row.Owner&&peer.IsReady()&&NetworkSync.HasPeerSettings(peer)&&peer.m_rpc!=null)
            {try{peer.m_rpc.Invoke(CommandRpc,new ZPackage(Encode(row,epoch,ForestGuardOwnerOutcome.Applied)));return true;}catch(Exception){return false;}}
            return false;
        }
        static void Report(Pending pending,ForestGuardOwnerOutcome outcome,string reason)
        {
            MasterIdolForestGuardDiagnostic.Event(pending.Row,MasterIdolForestGuardDiagnostic.Lane.Reply,reason,"outcome="+outcome,true,outcome==ForestGuardOwnerOutcome.Deferred);
            if(!Context()||!Running.TryGetValue(pending.Row.Id,out var current)||!ReferenceEquals(current,pending))return;
            var data=Manager.GetZDO(pending.Row.Id);
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var row,out var epoch)||epoch!=pending.Epoch||!Same(row,pending.Row))return;
            pending.Applied=outcome==ForestGuardOwnerOutcome.Applied;
            if(Net.IsServer())MasterIdolForestGuardLifecycleController.OnOwnerOutcome(row,epoch,ForestGuardOwnerAction.ApplyDesired,outcome,ZNet.GetUID());
            else if(ReferenceEquals(pending.Reply,Net.GetServerRPC())&&NetworkSync.HasCurrentServerSettings(pending.Reply))
                try{pending.Reply.Invoke(AckRpc,new ZPackage(Encode(row,epoch,outcome)));}catch(Exception){}
        }
        static void Stamp(MasterIdolForestGuardLifecycle.State row,ZDO data)
        {
            if(data.GetLong(MasterIdolForestGuardLifecycle.WorldKey,0)!=World)data.Set(MasterIdolForestGuardLifecycle.WorldKey,World);
            if(data.GetLong(MasterIdolForestGuardLifecycle.CycleKey,0)!=row.Cycle)data.Set(MasterIdolForestGuardLifecycle.CycleKey,row.Cycle);
            if(data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0)!=row.IntentRevision)data.Set(MasterIdolForestGuardLifecycle.IntentKey,row.IntentRevision);
            if(data.GetString(MasterIdolForestGuardLifecycle.AttemptKey,"")!=row.Attempt)data.Set(MasterIdolForestGuardLifecycle.AttemptKey,row.Attempt);
            if(data.GetInt(MasterIdolForestGuardLifecycle.PhaseKey,0)!=(int)row.Status)data.Set(MasterIdolForestGuardLifecycle.PhaseKey,(int)row.Status);
        }
        static bool CurrentApplication(Pending pending,ZDO data)
        {
            return Net==ZNet.instance&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&Net?.GetWorld()!=null&&Net.GetWorldUID()==World&&
                ReferenceEquals(Manager.GetZDO(pending.Row.Id),data)&&pending.Actor?.m_nview?.IsOwner()==true&&ReferenceEquals(pending.Actor.m_nview.GetZDO(),data)&&
                ReferenceEquals(ZNetScene.instance?.FindInstance(pending.Row.Id)?.GetComponent<Character>(),pending.Actor)&&
                MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var row,out var epoch)&&epoch==pending.Epoch&&Same(row,pending.Row);
        }
        static void Apply(MasterIdolForestGuardLifecycle.State row,string epoch,ZRpc reply)
        {
            if(!Context()||row.Status!=MasterIdolForestGuardLifecycle.Phase.Active&&row.Status!=MasterIdolForestGuardLifecycle.Phase.Suspended)return;
            var data=Manager.GetZDO(row.Id);
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var accepted,out var currentEpoch)||epoch!=currentEpoch||!Same(row,accepted)||data.GetOwner()!=ZNet.GetUID())return;
            var instance=ZNetScene.instance?.FindInstance(row.Id);var actor=instance!=null?instance.GetComponent<Character>():null;
            if(actor==null||actor.IsDead()||actor.m_nview?.IsOwner()!=true||!ReferenceEquals(actor.m_nview.GetZDO(),data))
            {MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Apply,"native-body-not-loaded","no death/recreation",true,true);return;}
            if(!MasterIdolForestGuardLifecycleVisibility.NativeReady(actor))
            {MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Apply,"native-cache-not-ready","no partial Awake mutation",true,true);return;}
            if(Running.Count>=4&&!Running.ContainsKey(row.Id))
            {foreach(var key in new List<ZDOID>(Running.Keys))if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(Manager.GetZDO(key),out _,out _))Running.Remove(key);if(Running.Count>=4)return;}
            var pending=new Pending{Row=row,Epoch=epoch,Actor=actor,Reply=reply};Running[row.Id]=pending;
            try
            {
                bool active=row.Status==MasterIdolForestGuardLifecycle.Phase.Active;
                if(Native(row,data)&&active&&!MasterIdolForestGuardLifecycleVisibility.IsHeld(actor))
                {Report(pending,ForestGuardOwnerOutcome.Applied,"idempotent-native-active");return;}
                if(!active)
                {
                    if(actor.InAttack()||actor.InDodge()||actor.IsStaggering()){Report(pending,ForestGuardOwnerOutcome.Deferred,"native-action-in-flight");return;}
                    if(!MasterIdolForestGuardLifecycleVisibility.Suspend(actor)){Report(pending,ForestGuardOwnerOutcome.Deferred,"dormant-claim-pending");return;}
                    if(!CurrentApplication(pending,data))return;
                    Stamp(row,data);Report(pending,ForestGuardOwnerOutcome.Applied,"dormant-applied");return;
                }
                var zone=MasterIdolEffectZones.Find(row.Home);
                using var diagnosticPosition=MasterIdolForestGuardDiagnostic.Position(row);
                if(!MasterIdolRecruitment.TryPosition(zone,row.Family,null,out var point,out _))
                {Report(pending,ForestGuardOwnerOutcome.Deferred,"physical-position-pending");return;}
                if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out accepted,out currentEpoch)||currentEpoch!=epoch||!Same(row,accepted)||!actor.m_nview.IsOwner())return;
                actor.transform.position=point;data.SetPosition(point);if(actor.m_body!=null){actor.m_body.position=point;actor.m_body.velocity=Vector3.zero;actor.m_body.angularVelocity=Vector3.zero;}
                // Authorization from exact desired intent/owner; NOT CanOperate,
                // whose receipt is intentionally absent until restore succeeds.
                if(!MasterIdolForestGuardLifecycleVisibility.ReleaseActiveForOwner(actor,row))
                {Report(pending,ForestGuardOwnerOutcome.Deferred,"active-restore-pending");return;}
                if(!CurrentApplication(pending,data))return;
                Stamp(row,data);Report(pending,ForestGuardOwnerOutcome.Applied,"active-applied");
                // No callback into controller. Cosmetics cannot undo the receipt.
                try{MasterIdolResidentBirthPresentation.NotifyActive(actor,row);}
                catch(Exception){MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Cue,"cosmetic-notify-failed","ACTIVE receipt retained",true,true);}
            }
            catch(Exception error){Report(pending,ForestGuardOwnerOutcome.Deferred,"runtime-exception-"+error.GetType().Name);}
        }
        internal static string Diagnostic(ZDOID id)
        {return Net!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation?"ownerCommand=context-not-started":Running.TryGetValue(id,out var pending)?"ownerCommand=ApplyDesired applied="+pending.Applied:"ownerCommand=none";}
        internal static void Reset(){Running.Clear();Net=null;Manager=null;World=0;Generation=0;}
    }
}
