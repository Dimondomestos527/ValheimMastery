using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal enum ForestGuardOwnerAction:byte { MarkQuiescing,Suspend,Queue,PrepareAppearance,ReleaseActive }
    internal enum ForestGuardOwnerOutcome:byte { Applied,Started,Completed,Deferred }
    // The command contains an exact authenticated snapshot row, never a free-form
    // point or owner transfer. The physical owner computes its native safe point.
    internal static class MasterIdolForestGuardLifecycleOwner
    {
        const string CommandRpc="VM_BF_LifecycleOwner_v1",AckRpc="VM_BF_LifecycleOwnerAck_v1";
        const int MaximumBytes=512;
        sealed class Pending
        {internal MasterIdolForestGuardLifecycle.State Row;internal string Epoch;internal ForestGuardOwnerAction Action;internal ForestGuardOwnerOutcome Outcome;internal ZRpc Reply;internal Character Actor;internal bool Terminal,HasOutcome;}
        static readonly Dictionary<ZDOID,Pending> Running=new Dictionary<ZDOID,Pending>();
        static ZNet Net;static ZDOMan Manager;static ulong Generation;static long World;
        static bool Context()
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(Net!=net||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||World!=world)
            {Running.Clear();Net=net;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;World=world;}
            return net!=null&&Manager!=null&&world!=0&&MasterIdolLoadIdentity.LoadReady;
        }
        internal static bool Same(MasterIdolForestGuardLifecycle.State a,MasterIdolForestGuardLifecycle.State b)=>a.Id==b.Id&&a.Family==b.Family&&a.Born==b.Born&&a.Nonce==b.Nonce&&a.Home==b.Home&&a.Owner==b.Owner&&a.OwnerRevision==b.OwnerRevision&&a.Cycle==b.Cycle&&a.IntentRevision==b.IntentRevision&&a.Attempt==b.Attempt&&a.Status==b.Status;
        internal static bool Native(MasterIdolForestGuardLifecycle.State row,ZDO data)=>data!=null&&data.GetLong(MasterIdolForestGuardLifecycle.WorldKey,0)==ZNet.instance?.GetWorldUID()&&data.GetLong(MasterIdolForestGuardLifecycle.CycleKey,0)==row.Cycle&&data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0)==row.IntentRevision&&data.GetString(MasterIdolForestGuardLifecycle.AttemptKey,"")==row.Attempt&&data.GetInt(MasterIdolForestGuardLifecycle.PhaseKey,0)==(int)row.Status;
        static byte[] Encode(MasterIdolForestGuardLifecycle.State row,string epoch,ForestGuardOwnerAction action,ForestGuardOwnerOutcome outcome)
        {
            var body=MasterIdolForestGuardLifecycle.Encode(new MasterIdolForestGuardLifecycle.Snapshot{World=World,Epoch=epoch,Token=row.Attempt,Revision=row.IntentRevision,Rows=new[]{row}});
            if(body.Length+2>MaximumBytes)throw new FormatException();
            var bytes=new byte[body.Length+2];bytes[0]=(byte)action;bytes[1]=(byte)outcome;Buffer.BlockCopy(body,0,bytes,2,body.Length);return bytes;
        }
        static bool Decode(ZPackage packet,out MasterIdolForestGuardLifecycle.State row,out string epoch,out ForestGuardOwnerAction action,out ForestGuardOwnerOutcome outcome)
        {
            row=default;epoch=null;action=default;outcome=default;
            if(packet==null||packet.GetPos()!=0||packet.Size()<6||packet.Size()>MaximumBytes)return false;
            var bytes=packet.GetArray();if(bytes[0]>4||bytes[1]>3)return false;
            var body=new byte[bytes.Length-2];Buffer.BlockCopy(bytes,2,body,0,body.Length);
            var snapshot=MasterIdolForestGuardLifecycle.Decode(body);
            if(snapshot.World!=World||snapshot.Rows.Length!=1)return false;
            row=snapshot.Rows[0];epoch=snapshot.Epoch;action=(ForestGuardOwnerAction)bytes[0];outcome=(ForestGuardOwnerOutcome)bytes[1];
            return snapshot.Token==row.Attempt&&snapshot.Revision==row.IntentRevision;
        }
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>(CommandRpc,Command);peer?.m_rpc?.Register<ZPackage>(AckRpc,Ack);}
        static void Command(ZRpc rpc,ZPackage packet)
        {
            if(!Context()||Net.IsServer()||rpc==null||!ReferenceEquals(rpc,Net.GetServerRPC())||!NetworkSync.HasCurrentServerSettings(rpc))return;
            try{if(Decode(packet,out var row,out var epoch,out var action,out var outcome)&&outcome==ForestGuardOwnerOutcome.Applied)Apply(row,epoch,action,rpc);}
            catch(EndOfStreamException){}catch(IOException){}catch(FormatException){}catch(ArgumentException){}
        }
        static void Ack(ZRpc rpc,ZPackage packet)
        {
            if(!Context()||!Net.IsServer()||rpc==null)return;
            ZNetPeer source=null;foreach(var peer in Net.GetPeers())if(peer?.m_rpc==rpc&&peer.IsReady()&&NetworkSync.HasPeerSettings(peer)){source=peer;break;}
            if(source==null)return;
            try{if(Decode(packet,out var row,out var epoch,out var action,out var outcome)&&row.Owner==source.m_uid)MasterIdolForestGuardLifecycleController.OnOwnerOutcome(row,epoch,action,outcome,source.m_uid);}
            catch(EndOfStreamException){}catch(IOException){}catch(FormatException){}catch(ArgumentException){}
        }
        internal static bool Request(MasterIdolForestGuardLifecycle.State row,ForestGuardOwnerAction action)
        {
            if(!MasterIdolForestGuardDiagnostic.Check(Context(),row,MasterIdolForestGuardDiagnostic.Lane.Request,"context")||!MasterIdolForestGuardDiagnostic.Check(Net.IsServer(),row,MasterIdolForestGuardDiagnostic.Lane.Request,"not-server"))return false;
            var data=Manager.GetZDO(row.Id);
            if(!MasterIdolForestGuardDiagnostic.Check(MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var accepted,out string epoch),row,MasterIdolForestGuardDiagnostic.Lane.Request,"accepted-intent-unavailable")||!MasterIdolForestGuardDiagnostic.Check(Same(accepted,row),row,MasterIdolForestGuardDiagnostic.Lane.Request,"accepted-row-mismatch"))return false;
            // Native owner is respected. No SetOwner, forced lease expiry or factory.
            if(row.Owner==ZNet.GetUID()){MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Request,"local-apply","action="+action,false);Apply(row,epoch,action,null);return true;}
            foreach(var peer in Net.GetPeers())if(peer?.m_uid==row.Owner&&peer.IsReady()&&NetworkSync.HasPeerSettings(peer)&&peer.m_rpc!=null)
            {try{peer.m_rpc.Invoke(CommandRpc,new ZPackage(Encode(row,epoch,action,ForestGuardOwnerOutcome.Applied)));MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Request,"remote-command-sent","action="+action,false);return true;}catch(Exception){MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Request,"remote-send-exception","action="+action,true);return false;}}
            MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Request,"no-ready-owner-peer","action="+action,true);return false;
        }
        static void Reply(Pending pending,ForestGuardOwnerOutcome outcome)
        {
            pending.Outcome=outcome;pending.HasOutcome=true;
            MasterIdolForestGuardDiagnostic.Event(pending.Row,MasterIdolForestGuardDiagnostic.Lane.Reply,"generated","action="+pending.Action+" outcome="+outcome,true);
            if(!MasterIdolForestGuardDiagnostic.Check(Context(),pending.Row,MasterIdolForestGuardDiagnostic.Lane.Reply,"context")||!MasterIdolForestGuardDiagnostic.Check(Running.TryGetValue(pending.Row.Id,out var current),pending.Row,MasterIdolForestGuardDiagnostic.Lane.Reply,"pending-not-found")||!MasterIdolForestGuardDiagnostic.Check(ReferenceEquals(current,pending),pending.Row,MasterIdolForestGuardDiagnostic.Lane.Reply,"pending-replaced"))return;
            var data=Manager.GetZDO(pending.Row.Id);
            if(!MasterIdolForestGuardDiagnostic.Check(MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var row,out string epoch),pending.Row,MasterIdolForestGuardDiagnostic.Lane.Reply,"accepted-intent-unavailable")||MasterIdolForestGuardDiagnostic.Reject(epoch!=pending.Epoch,pending.Row,MasterIdolForestGuardDiagnostic.Lane.Reply,"epoch-changed")||!MasterIdolForestGuardDiagnostic.Check(Same(row,pending.Row),pending.Row,MasterIdolForestGuardDiagnostic.Lane.Reply,"accepted-row-changed"))return;
            pending.Outcome=outcome;
            if(Net.IsServer())MasterIdolForestGuardLifecycleController.OnOwnerOutcome(row,epoch,pending.Action,outcome,ZNet.GetUID());
            else if(ReferenceEquals(pending.Reply,Net.GetServerRPC())&&NetworkSync.HasCurrentServerSettings(pending.Reply))
                try{pending.Reply.Invoke(AckRpc,new ZPackage(Encode(row,epoch,pending.Action,outcome)));}catch(Exception){}
        }
        static void Stamp(MasterIdolForestGuardLifecycle.State row,MasterIdolForestGuardLifecycle.Phase phase,ZDO data)
        {
            // Called only after exact accepted row/physical owner checks. Do not
            // churn unchanged native ZDO values on retry/heartbeat.
            if(data.GetLong(MasterIdolForestGuardLifecycle.WorldKey,0)!=World)data.Set(MasterIdolForestGuardLifecycle.WorldKey,World);
            if(data.GetLong(MasterIdolForestGuardLifecycle.CycleKey,0)!=row.Cycle)data.Set(MasterIdolForestGuardLifecycle.CycleKey,row.Cycle);
            if(data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0)!=row.IntentRevision)data.Set(MasterIdolForestGuardLifecycle.IntentKey,row.IntentRevision);
            if(data.GetString(MasterIdolForestGuardLifecycle.AttemptKey,"")!=row.Attempt)data.Set(MasterIdolForestGuardLifecycle.AttemptKey,row.Attempt);
            if(data.GetInt(MasterIdolForestGuardLifecycle.PhaseKey,0)!=(int)phase)data.Set(MasterIdolForestGuardLifecycle.PhaseKey,(int)phase);
        }
        static bool Allowed(ForestGuardOwnerAction action,MasterIdolForestGuardLifecycle.Phase phase)=>
            action==ForestGuardOwnerAction.MarkQuiescing&&phase==MasterIdolForestGuardLifecycle.Phase.Quiescing||
            action==ForestGuardOwnerAction.Suspend&&phase==MasterIdolForestGuardLifecycle.Phase.Suspended||
            action==ForestGuardOwnerAction.Queue&&phase==MasterIdolForestGuardLifecycle.Phase.Queued||
            (action==ForestGuardOwnerAction.PrepareAppearance||action==ForestGuardOwnerAction.ReleaseActive)&&phase==MasterIdolForestGuardLifecycle.Phase.Appearing;
        static bool NativeRelease(MasterIdolForestGuardLifecycle.State row,ZDO data)
        {var active=row;active.Status=MasterIdolForestGuardLifecycle.Phase.Active;return Native(active,data);}
        static void Apply(MasterIdolForestGuardLifecycle.State row,string epoch,ForestGuardOwnerAction action,ZRpc reply)
        {
            if(!MasterIdolForestGuardDiagnostic.Check(Context(),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"context")||!MasterIdolForestGuardDiagnostic.Check(Allowed(action,row.Status),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"action-phase-not-allowed"))return;
            MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Apply,"entered","action="+action,false);
            var data=Manager.GetZDO(row.Id);
            if(!MasterIdolForestGuardDiagnostic.Check(MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var accepted,out string currentEpoch),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"accepted-intent-unavailable")||MasterIdolForestGuardDiagnostic.Reject(currentEpoch!=epoch,row,MasterIdolForestGuardDiagnostic.Lane.Apply,"epoch-changed")||!MasterIdolForestGuardDiagnostic.Check(Same(row,accepted),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"row-changed")||MasterIdolForestGuardDiagnostic.Reject(data.GetOwner()!=ZNet.GetUID(),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"not-native-owner"))return;
            var instance=ZNetScene.instance?.FindInstance(row.Id);var actor=instance!=null?instance.GetComponent<Character>():null;
            if(MasterIdolForestGuardDiagnostic.Reject(actor==null,row,MasterIdolForestGuardDiagnostic.Lane.Apply,"actor-not-loaded")||MasterIdolForestGuardDiagnostic.Reject(actor.IsDead(),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"actor-dead")||MasterIdolForestGuardDiagnostic.Reject(actor.m_nview?.IsOwner()!=true,row,MasterIdolForestGuardDiagnostic.Lane.Apply,"actor-view-not-owner")||!MasterIdolForestGuardDiagnostic.Check(ReferenceEquals(actor.m_nview.GetZDO(),data),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"actor-zdo-mismatch"))return;
            if(Running.TryGetValue(row.Id,out var old)&&Same(old.Row,row)&&old.Epoch==epoch&&old.Action==action)
            {
                if(!old.HasOutcome){MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Wait,"previous-outcome-in-flight","action="+action,false);return;} // Actual asset preparation is still in flight.
                if(old.Outcome!=ForestGuardOwnerOutcome.Deferred||action==ForestGuardOwnerAction.PrepareAppearance)
                {MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Apply,"cached-outcome","action="+action+" outcome="+old.Outcome,false);Reply(old,old.Outcome);return;}
                // Transient suspend/queue failure must reattempt its side effect.
            }
            foreach(var stale in new List<ZDOID>(Running.Keys))
                if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(Manager.GetZDO(stale),out _,out _))Running.Remove(stale);
            if(MasterIdolForestGuardDiagnostic.Reject(Running.Count>=4&&!Running.ContainsKey(row.Id),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"running-capacity"))return;
            // A delayed Prepare cannot replay after Release stamped phase1.
            if(action==ForestGuardOwnerAction.PrepareAppearance&&MasterIdolForestGuardDiagnostic.Reject(NativeRelease(row,data),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"already-native-active"))return;
            var pending=new Pending{Row=row,Epoch=epoch,Action=action,Reply=reply,Actor=actor,Outcome=ForestGuardOwnerOutcome.Deferred};Running[row.Id]=pending;
            if(action==ForestGuardOwnerAction.MarkQuiescing)
            {MasterIdolResidentBirthPresentation.Stop(actor);Stamp(row,row.Status,data);Reply(pending,ForestGuardOwnerOutcome.Applied);return;}
            if(action==ForestGuardOwnerAction.Suspend||action==ForestGuardOwnerAction.Queue)
            {
                if(MasterIdolForestGuardDiagnostic.Reject(actor.InAttack(),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"actor-attacking")||MasterIdolForestGuardDiagnostic.Reject(actor.InDodge(),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"actor-dodging")||MasterIdolForestGuardDiagnostic.Reject(actor.IsStaggering(),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"actor-staggering")) {Reply(pending,ForestGuardOwnerOutcome.Deferred);return;}
                if(actor.GetBaseAI() is MonsterAI ai)
                {
                    ai.m_targetCreature=null;ai.m_targetStatic=null;
                    // Native SetTargetInfo(None) only clears this flag, but it
                    // dereferences BaseAI's cached view, which may precede Awake
                    // on a persisted hidden body. The subject ZDO is validated above.
                    if(data.GetBool(ZDOVars.s_haveTargetHash,false))data.Set(ZDOVars.s_haveTargetHash,false);
                    if(ai.IsAlerted())
                    {
                        if(ReferenceEquals(ai.m_nview,actor.m_nview)&&ai.m_nview.IsValid()&&ReferenceEquals(ai.m_nview.GetZDO(),data)&&ai.m_character==actor&&ai.m_animator!=null&&ai.m_animator.m_animator!=null&&ReferenceEquals(ai.m_animator.m_nview,actor.m_nview))
                            ai.SetAlerted(false);
                        else
                        {
                            // No native animator/view call before AI initialization.
                            ai.m_alerted=false;
                            if(data.GetBool(ZDOVars.s_alert,false))data.Set(ZDOVars.s_alert,false);
                        }
                    }
                }
                Stamp(row,row.Status,data);
                Reply(pending,MasterIdolForestGuardDiagnostic.Check(MasterIdolForestGuardLifecycleVisibility.Suspend(actor),row,MasterIdolForestGuardDiagnostic.Lane.Stage,"suspend-rejected")?ForestGuardOwnerOutcome.Applied:ForestGuardOwnerOutcome.Deferred);return;
            }
            if(action==ForestGuardOwnerAction.ReleaseActive)
            {Stamp(row,MasterIdolForestGuardLifecycle.Phase.Active,data);Reply(pending,ForestGuardOwnerOutcome.Applied);return;}
            var zone=MasterIdolEffectZones.Find(row.Home)??MasterIdolEffectZones.ResidentHome(row.Id.ToString(),MasterIdolForestRules.Families[row.Family]);
            using var diagnosticPosition=MasterIdolForestGuardDiagnostic.Position(row);
            if(!MasterIdolForestGuardDiagnostic.Check(MasterIdolRecruitment.TryPosition(zone,row.Family,null,out var point,out _),row,MasterIdolForestGuardDiagnostic.Lane.Position,"search-rejected")){Reply(pending,ForestGuardOwnerOutcome.Deferred);return;}
            if(!MasterIdolForestGuardDiagnostic.Check(MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out accepted,out currentEpoch),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"post-position-intent-unavailable")||MasterIdolForestGuardDiagnostic.Reject(currentEpoch!=epoch,row,MasterIdolForestGuardDiagnostic.Lane.Apply,"post-position-epoch-changed")||!MasterIdolForestGuardDiagnostic.Check(Same(row,accepted),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"post-position-row-changed")||!MasterIdolForestGuardDiagnostic.Check(actor.m_nview.IsOwner(),row,MasterIdolForestGuardDiagnostic.Lane.Apply,"post-position-not-owner"))return;
            MasterIdolResidentBirthPresentation.Stop(actor);
            if(!MasterIdolForestGuardDiagnostic.Check(MasterIdolForestGuardLifecycleVisibility.Stage(actor),row,MasterIdolForestGuardDiagnostic.Lane.Stage,"stage-rejected")){Reply(pending,ForestGuardOwnerOutcome.Deferred);return;}
            actor.transform.position=point;data.SetPosition(point);if(actor.m_body!=null){actor.m_body.position=point;actor.m_body.velocity=Vector3.zero;actor.m_body.angularVelocity=Vector3.zero;}
            Stamp(row,row.Status,data);
            if(!MasterIdolResidentBirthPresentation.TryQueueActivation(actor,row.Cycle,row.IntentRevision,row.Attempt,result=>
            {
                MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Cue,"callback","result="+result,true);
                if(result==ForestGuardAppearanceOutcome.Started)Reply(pending,ForestGuardOwnerOutcome.Started);
                else if(result==ForestGuardAppearanceOutcome.Completed){pending.Terminal=true;Reply(pending,ForestGuardOwnerOutcome.Completed);}
                else {pending.Terminal=true;Reply(pending,ForestGuardOwnerOutcome.Deferred);}
            })){MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Cue,"queue-activation-rejected","",true);Reply(pending,ForestGuardOwnerOutcome.Deferred);}
        }
        internal static string Diagnostic(ZDOID id)
        {
            // Observation must not initialize/reset owner execution context.
            if(Net!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation)return "ownerCommand=context-not-started";
            if(!Running.TryGetValue(id,out var pending))return "ownerCommand=none";
            var data=ZDOMan.instance?.GetZDO(id);
            bool current=MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var row,out string epoch)&&epoch==pending.Epoch&&Same(row,pending.Row);
            return "ownerCommand="+pending.Action+" outcome="+(pending.HasOutcome?pending.Outcome.ToString():"pending")+" commandCurrent="+current;
        }
        internal static void Reset(){Running.Clear();Net=null;Manager=null;World=0;Generation=0;}
    }
}
