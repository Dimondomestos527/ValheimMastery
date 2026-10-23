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
            if(!Context()||!Net.IsServer())return false;
            var data=Manager.GetZDO(row.Id);
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var accepted,out string epoch)||!Same(accepted,row))return false;
            // Native owner is respected. No SetOwner, forced lease expiry or factory.
            if(row.Owner==ZNet.GetUID()){Apply(row,epoch,action,null);return true;}
            foreach(var peer in Net.GetPeers())if(peer?.m_uid==row.Owner&&peer.IsReady()&&NetworkSync.HasPeerSettings(peer)&&peer.m_rpc!=null)
            {try{peer.m_rpc.Invoke(CommandRpc,new ZPackage(Encode(row,epoch,action,ForestGuardOwnerOutcome.Applied)));return true;}catch(Exception){return false;}}
            return false;
        }
        static void Reply(Pending pending,ForestGuardOwnerOutcome outcome)
        {
            pending.Outcome=outcome;pending.HasOutcome=true;
            if(!Context()||!Running.TryGetValue(pending.Row.Id,out var current)||!ReferenceEquals(current,pending))return;
            var data=Manager.GetZDO(pending.Row.Id);
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var row,out string epoch)||epoch!=pending.Epoch||!Same(row,pending.Row))return;
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
            if(!Context()||!Allowed(action,row.Status))return;
            var data=Manager.GetZDO(row.Id);
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var accepted,out string currentEpoch)||currentEpoch!=epoch||!Same(row,accepted)||data.GetOwner()!=ZNet.GetUID())return;
            var instance=ZNetScene.instance?.FindInstance(row.Id);var actor=instance!=null?instance.GetComponent<Character>():null;
            if(actor==null||actor.IsDead()||actor.m_nview?.IsOwner()!=true||!ReferenceEquals(actor.m_nview.GetZDO(),data))return;
            if(Running.TryGetValue(row.Id,out var old)&&Same(old.Row,row)&&old.Epoch==epoch&&old.Action==action)
            {
                if(!old.HasOutcome)return; // Actual asset preparation is still in flight.
                if(old.Outcome!=ForestGuardOwnerOutcome.Deferred||action==ForestGuardOwnerAction.PrepareAppearance)
                {Reply(old,old.Outcome);return;}
                // Transient suspend/queue failure must reattempt its side effect.
            }
            foreach(var stale in new List<ZDOID>(Running.Keys))
                if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(Manager.GetZDO(stale),out _,out _))Running.Remove(stale);
            if(Running.Count>=4&&!Running.ContainsKey(row.Id))return;
            // A delayed Prepare cannot replay after Release stamped phase1.
            if(action==ForestGuardOwnerAction.PrepareAppearance&&NativeRelease(row,data))return;
            var pending=new Pending{Row=row,Epoch=epoch,Action=action,Reply=reply,Actor=actor,Outcome=ForestGuardOwnerOutcome.Deferred};Running[row.Id]=pending;
            if(action==ForestGuardOwnerAction.MarkQuiescing)
            {MasterIdolResidentBirthPresentation.Stop(actor);Stamp(row,row.Status,data);Reply(pending,ForestGuardOwnerOutcome.Applied);return;}
            if(action==ForestGuardOwnerAction.Suspend||action==ForestGuardOwnerAction.Queue)
            {
                if(actor.InAttack()||actor.InDodge()||actor.IsStaggering()) {Reply(pending,ForestGuardOwnerOutcome.Deferred);return;}
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
                Reply(pending,MasterIdolForestGuardLifecycleVisibility.Suspend(actor)?ForestGuardOwnerOutcome.Applied:ForestGuardOwnerOutcome.Deferred);return;
            }
            if(action==ForestGuardOwnerAction.ReleaseActive)
            {Stamp(row,MasterIdolForestGuardLifecycle.Phase.Active,data);Reply(pending,ForestGuardOwnerOutcome.Applied);return;}
            var zone=MasterIdolEffectZones.Find(row.Home)??MasterIdolEffectZones.ResidentHome(row.Id.ToString(),MasterIdolForestRules.Families[row.Family]);
            if(!MasterIdolRecruitment.TryPosition(zone,row.Family,null,out var point,out _)){Reply(pending,ForestGuardOwnerOutcome.Deferred);return;}
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out accepted,out currentEpoch)||currentEpoch!=epoch||!Same(row,accepted)||!actor.m_nview.IsOwner())return;
            MasterIdolResidentBirthPresentation.Stop(actor);
            if(!MasterIdolForestGuardLifecycleVisibility.Stage(actor)){Reply(pending,ForestGuardOwnerOutcome.Deferred);return;}
            actor.transform.position=point;data.SetPosition(point);if(actor.m_body!=null){actor.m_body.position=point;actor.m_body.velocity=Vector3.zero;actor.m_body.angularVelocity=Vector3.zero;}
            Stamp(row,row.Status,data);
            if(!MasterIdolResidentBirthPresentation.TryQueueActivation(actor,row.Cycle,row.IntentRevision,row.Attempt,result=>
            {
                if(result==ForestGuardAppearanceOutcome.Started)Reply(pending,ForestGuardOwnerOutcome.Started);
                else if(result==ForestGuardAppearanceOutcome.Completed){pending.Terminal=true;Reply(pending,ForestGuardOwnerOutcome.Completed);}
                else {pending.Terminal=true;Reply(pending,ForestGuardOwnerOutcome.Deferred);}
            }))Reply(pending,ForestGuardOwnerOutcome.Deferred);
        }
        internal static void Reset(){Running.Clear();Net=null;Manager=null;World=0;Generation=0;}
    }
}
