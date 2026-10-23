using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Admission proof only. The lifecycle controller owns transitions/visibility;
    // cargo owns settlement. Neither is invoked by the pure predicate below.
    internal static class MasterIdolForestGuardLifecycle
    {
        internal const string IntentKey="vm.bf.lifecycle.intent.v1",AttemptKey="vm.bf.lifecycle.attempt.v1";
        internal const string PhaseKey="vm.bf.lifecycle.phase.v1",CycleKey="vm.bf.lifecycle.cycle.v1",WorldKey="vm.bf.lifecycle.world.v1";
        internal const int MaximumRows=4,MaximumBytes=4096;
        internal const float Heartbeat=2f,LeaseSeconds=6f;
        const string AskRpc="VM_BF_LifecycleAsk_v1",SnapshotRpc="VM_BF_LifecycleSnapshot_v1";
        internal enum Phase {Unknown,Active,Quiescing,Suspended,Queued,Appearing}
        internal struct State
        {
            internal ZDOID Id;internal int Family;internal string Nonce,Home;
            internal long Born,Cycle,Owner,IntentRevision;internal string Attempt;internal ushort OwnerRevision;internal Phase Status;
        }
        internal sealed class Snapshot
        {internal long World,Revision;internal string Token,Epoch;internal State[] Rows;}
        static ZNet Session;static ZDOMan Manager;static ulong Generation;static long World,Revision;
        static ZRpc ServerRpc;static string Token,Epoch,AcceptedEpoch;
        static State[] Rows=Array.Empty<State>();static bool Committed,Available;
        static float Verified,NextWork,NextPublish,NextAsk;
        static readonly Dictionary<ZRpc,string> Tokens=new Dictionary<ZRpc,string>();
        static readonly Dictionary<ZRpc,float> AskTimes=new Dictionary<ZRpc,float>();

        static bool Context()
        {
            var net=ZNet.instance;var manager=ZDOMan.instance;
            long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            var rpc=net?.IsServer()==true?null:net?.GetServerRPC();
            if(Session!=net||Manager!=manager||Generation!=MasterIdolLoadIdentity.Generation||World!=world||ServerRpc!=rpc)
            {
                Reset();Session=net;Manager=manager;Generation=MasterIdolLoadIdentity.Generation;World=world;ServerRpc=rpc;
                Token=Guid.NewGuid().ToString("N");Epoch=Guid.NewGuid().ToString("N");Revision=1;
            }
            return net!=null&&manager!=null&&world!=0&&MasterIdolLoadIdentity.LoadReady;
        }
        static bool CurrentProof()
        {
            var net=ZNet.instance;float age=Time.unscaledTime-Verified;
            return Available&&Session==net&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&
                MasterIdolLoadIdentity.LoadReady&&net?.GetWorld()!=null&&World==net.GetWorldUID()&&
                (net.IsServer()||NetworkSync.HasServerSettings&&net.GetServerPeer()?.IsReady()==true&&ServerRpc!=null&&ReferenceEquals(ServerRpc,net.GetServerRPC()))&&
                float.IsFinite(age)&&age>=0&&age<LeaseSeconds;
        }
        static bool NativeIdentity(State row,ZDO body)
        {
            return body!=null&&body.m_uid==row.Id&&ReferenceEquals(ZDOMan.instance?.GetZDO(row.Id),body)&&
                MasterIdolForestGuardState.ModuleBorn(body)&&MasterIdolForestGuardState.Family(body)==row.Family&&
                body.GetLong(MasterIdolForestGuardState.BornKey,0)==row.Born&&
                body.GetString(MasterIdolForestGuardState.NonceKey,"")==row.Nonce&&
                MasterIdolNativeTaming.SameHome(body.GetString(MasterIdolNativeTaming.HomeKey,""),row.Home)&&
                body.GetOwner()==row.Owner&&body.OwnerRevision==row.OwnerRevision&&
                float.IsFinite(body.GetFloat(ZDOVars.s_health,1))&&body.GetFloat(ZDOVars.s_health,1)>0;
        }
        static bool NativePhase(State row,ZDO body)=>body.GetLong(WorldKey,0)==World&&body.GetLong(CycleKey,0)==row.Cycle&&body.GetInt(PhaseKey,0)==(int)row.Status&&body.GetLong(IntentKey,0)==row.IntentRevision&&body.GetString(AttemptKey,"")==row.Attempt;
        internal static bool CanOperate(ZDO body)
        {
            if(MasteryPlugin.Settings?.Enabled?.Value!=true||body==null||!CurrentProof())return false;
            // Owner identity belongs to the subject, never to the querying peer.
            foreach(var row in Rows)if(row.Id==body.m_uid)
                return row.Status==Phase.Active&&row.Cycle>0&&NativeIdentity(row,body)&&NativePhase(row,body);
            return false;
        }
        internal static bool TryReadAcceptedIntent(ZDO body,out State state,out string serverEpoch)
        {
            state=default;serverEpoch=null;
            if(MasteryPlugin.Settings?.Enabled?.Value!=true||body==null||!CurrentProof())return false;
            foreach(var row in Rows)if(row.Id==body.m_uid&&NativeIdentity(row,body))
            {state=row;serverEpoch=AcceptedEpoch;return true;}
            return false;
        }
        static bool Same(State a,State b)=>a.Id==b.Id&&a.Family==b.Family&&a.Nonce==b.Nonce&&a.Home==b.Home&&
            a.Born==b.Born&&a.Cycle==b.Cycle&&a.Owner==b.Owner&&a.OwnerRevision==b.OwnerRevision&&a.Status==b.Status&&a.IntentRevision==b.IntentRevision&&a.Attempt==b.Attempt;
        static bool Same(State[] a,State[] b)
        {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(!Same(a[i],b[i]))return false;return true;}
        static bool Valid(State row)
        {
            return row.Family>=0&&row.Family<4&&row.Born>0&&row.Born<=DateTime.MaxValue.Ticks&&
                MasterIdolForestRules.Nonce(row.Nonce)&&MasterIdolForestRules.Home(row.Home)&&
                row.Home!=null&&Encoding.UTF8.GetByteCount(row.Home)<=128&&
                (int)row.Status>=0&&(int)row.Status<=5&&
                (row.Status==Phase.Unknown?row.Cycle==0&&row.IntentRevision==0&&string.IsNullOrEmpty(row.Attempt):row.Cycle>0&&row.IntentRevision>0&&MasterIdolForestRules.Nonce(row.Attempt))&&
                MasterIdolNetworkQuota.TryUid(row.Id.ToString(),out _,out _);
        }
        // Server/controller-only full commit. Native matching does not grant authority:
        // only the caller's accepted transition/completion may supply Active here.
        internal static bool Commit(State[] desired)
        {
            if(!Context()||!Session.IsServer()||!MasterIdolForestGuardState.Ready||desired==null||desired.Length>MaximumRows)return false;
            var slots=MasterIdolForestGuardState.Slots;if(slots==null)return false;
            var next=new State[desired.Length];
            for(int i=0;i<desired.Length;i++)
            {
                var row=desired[i];if(!Valid(row))return false;
                for(int j=0;j<i;j++)if(next[j].Id==row.Id||next[j].Family==row.Family)return false;
                if(!slots.TryGetValue(MasterIdolForestRules.Families[row.Family],out var record)||record.Death!=0||
                    record.Uid!=row.Id.ToString()||record.Nonce!=row.Nonce||record.Born!=row.Born||!MasterIdolNativeTaming.SameHome(record.Home,row.Home))return false;
                var data=Manager.GetZDO(row.Id);
                if(!NativeIdentity(row,data)||row.Status==Phase.Active&&!NativePhase(row,data))return false;
                next[i]=row;
            }
            if(!Same(Rows,next)||!Committed)
            {Rows=next;NextPublish=0;}
            Committed=Available=true;AcceptedEpoch=Epoch;Verified=Time.unscaledTime;return true;
        }
        internal static byte[] Encode(Snapshot snapshot)
        {
            if(snapshot==null||snapshot.World==0||snapshot.Revision<=0||!MasterIdolForestRules.Nonce(snapshot.Token)||
                !MasterIdolForestRules.Nonce(snapshot.Epoch)||snapshot.Rows==null||snapshot.Rows.Length>MaximumRows)throw new FormatException();
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write(2);writer.Write(snapshot.World);writer.Write(snapshot.Token);writer.Write(snapshot.Epoch);writer.Write(snapshot.Revision);writer.Write(snapshot.Rows.Length);
                for(int i=0;i<snapshot.Rows.Length;i++)
                {
                    var row=snapshot.Rows[i];if(!Valid(row))throw new FormatException();
                    for(int j=0;j<i;j++)if(snapshot.Rows[j].Id==row.Id||snapshot.Rows[j].Family==row.Family)throw new FormatException();
                    writer.Write(row.Id.ToString());writer.Write((byte)row.Family);writer.Write(row.Nonce);writer.Write(row.Born);writer.Write(row.Home);
                    writer.Write(row.Cycle);writer.Write(row.IntentRevision);writer.Write(row.Attempt??"");writer.Write((byte)row.Status);writer.Write(row.Owner);writer.Write(row.OwnerRevision);
                }
                writer.Flush();if(stream.Length>MaximumBytes)throw new FormatException();return stream.ToArray();
            }
        }
        internal static Snapshot Decode(byte[] bytes)
        {
            if(bytes==null||bytes.Length>MaximumBytes||bytes.Length<4)throw new FormatException();
            using(var stream=new MemoryStream(bytes,false))using(var reader=new BinaryReader(stream,Encoding.UTF8))
            {
                if(reader.ReadInt32()!=2)throw new FormatException();
                var snapshot=new Snapshot{World=reader.ReadInt64(),Token=MasterIdolForestRules.ReadText(reader,32),Epoch=MasterIdolForestRules.ReadText(reader,32),Revision=reader.ReadInt64()};
                int count=reader.ReadInt32(); // Bound BEFORE any row allocation, all strings have byte bounds.
                if(snapshot.World==0||snapshot.Revision<=0||!MasterIdolForestRules.Nonce(snapshot.Token)||!MasterIdolForestRules.Nonce(snapshot.Epoch)||count<0||count>MaximumRows)throw new FormatException();
                snapshot.Rows=new State[count];
                for(int i=0;i<count;i++)
                {
                    string id=MasterIdolForestRules.ReadText(reader,64);if(!MasterIdolNetworkQuota.TryUid(id,out long user,out uint number))throw new FormatException();
                    var row=new State{Id=new ZDOID(user,number),Family=reader.ReadByte(),Nonce=MasterIdolForestRules.ReadText(reader,32),Born=reader.ReadInt64(),Home=MasterIdolForestRules.ReadText(reader,128),Cycle=reader.ReadInt64(),IntentRevision=reader.ReadInt64(),Attempt=MasterIdolForestRules.ReadText(reader,32),Status=(Phase)reader.ReadByte(),Owner=reader.ReadInt64(),OwnerRevision=reader.ReadUInt16()};
                    if(!Valid(row))throw new FormatException();
                    for(int j=0;j<i;j++)if(snapshot.Rows[j].Id==row.Id||snapshot.Rows[j].Family==row.Family)throw new FormatException();
                    snapshot.Rows[i]=row;
                }
                if(stream.Position!=stream.Length)throw new FormatException();return snapshot;
            }
        }
        static byte[] PacketBytes(ZPackage packet,int maximum)
        {if(packet==null||packet.Size()>maximum||packet.Size()<4||packet.GetPos()!=0)throw new FormatException();return packet.GetArray();}
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>(AskRpc,Ask);peer?.m_rpc?.Register<ZPackage>(SnapshotRpc,Receive);MasterIdolForestGuardLifecycleOwner.Register(peer);}
        static void Ask(ZRpc rpc,ZPackage packet)
        {
            if(!Context()||!Session.IsServer()||rpc==null)return;
            bool ready=false;foreach(var peer in Session.GetPeers())if(peer?.m_rpc==rpc&&peer.IsReady()&&NetworkSync.HasPeerSettings(peer)){ready=true;break;}if(!ready)return;
            try
            {
                var bytes=PacketBytes(packet,128);using(var stream=new MemoryStream(bytes,false))using(var reader=new BinaryReader(stream,Encoding.UTF8))
                {
                    if(reader.ReadInt32()!=2||reader.ReadInt64()!=World)return;string token=MasterIdolForestRules.ReadText(reader,32);
                    if(stream.Position!=stream.Length||!MasterIdolForestRules.Nonce(token))return;
                    if(AskTimes.TryGetValue(rpc,out float last)&&Time.unscaledTime-last<Heartbeat)return;
                    AskTimes[rpc]=Time.unscaledTime;Tokens[rpc]=token;NextPublish=0;
                }
            }catch(EndOfStreamException){}catch(IOException){}catch(FormatException){}catch(ArgumentException){}
        }
        static void Receive(ZRpc rpc,ZPackage packet)
        {
            if(!Context()||Session.IsServer()||rpc==null||!ReferenceEquals(rpc,ServerRpc)||!NetworkSync.HasCurrentServerSettings(rpc))return;
            try
            {
                var snapshot=Decode(PacketBytes(packet,MaximumBytes));
                if(snapshot.World!=World||snapshot.Token!=Token)return;
                if(AcceptedEpoch!=null&&AcceptedEpoch!=snapshot.Epoch)
                {Available=false;Rows=Array.Empty<State>();AcceptedEpoch=null;Revision=0;Token=Guid.NewGuid().ToString("N");NextAsk=0;return;}
                if(snapshot.Revision<=Revision)return; // Duplicate heartbeats never renew a lease.
                Rows=snapshot.Rows;Revision=snapshot.Revision;AcceptedEpoch=snapshot.Epoch;Available=true;Verified=Time.unscaledTime;
            }catch(EndOfStreamException){}catch(IOException){}catch(FormatException){}catch(ArgumentException){}
        }
        static void Request()
        {
            if(Session.IsServer()||ServerRpc==null||Session.GetServerPeer()?.IsReady()!=true||Time.unscaledTime<NextAsk||!NetworkSync.HasCurrentServerSettings(ServerRpc))return;
            NextAsk=Time.unscaledTime+Heartbeat;
            try
            {
                using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
                {writer.Write(2);writer.Write(World);writer.Write(Token);writer.Flush();ServerRpc.Invoke(AskRpc,new ZPackage(stream.ToArray()));}
            }
            catch(Exception){} // A closing native connection retries; never touches cargo/phase.
        }
        internal static void Tick()
        {
            if(!Context()||Time.unscaledTime<NextWork)return;NextWork=Time.unscaledTime+.25f;
            if(!Session.IsServer()){if(!CurrentProof())Request();return;}
            if(Time.unscaledTime<NextPublish)return;NextPublish=Time.unscaledTime+Heartbeat;
            var peers=Session.GetPeers();var live=new HashSet<ZRpc>();
            foreach(var peer in peers)if(peer?.m_rpc!=null)live.Add(peer.m_rpc);
            foreach(var rpc in new List<ZRpc>(Tokens.Keys))if(!live.Contains(rpc)){Tokens.Remove(rpc);AskTimes.Remove(rpc);}
            // Transport heartbeat is not new authoritative reconciliation. The
            // controller must renew Commit; never perpetuate stale Active intent.
            if(!Committed||!CurrentProof()||!MasterIdolForestGuardState.Ready)return;
            // Per-publication sequence includes unchanged heartbeats. Wrap changes
            // epoch and forces a new request challenge; native ZDOs are not rewritten.
            if(Revision==long.MaxValue){Epoch=Guid.NewGuid().ToString("N");AcceptedEpoch=Epoch;Revision=1;}
            else Revision++;
            foreach(var peer in peers)
            {
                var rpc=peer?.m_rpc;if(rpc==null)continue;
                if(!peer.IsReady()||!NetworkSync.HasPeerSettings(peer)||!Tokens.TryGetValue(rpc,out string token))continue;
                try{rpc.Invoke(SnapshotRpc,new ZPackage(Encode(new Snapshot{World=World,Token=token,Epoch=Epoch,Revision=Revision,Rows=Rows})));}
                catch(Exception){} // One closing peer cannot interrupt other peers or atomic cargo.
            }
        }
        internal static void Reset()
        {
            Rows=Array.Empty<State>();Available=Committed=false;Tokens.Clear();AskTimes.Clear();Session=null;Manager=null;ServerRpc=null;
            World=Revision=0;Generation=0;Token=Epoch=AcceptedEpoch=null;Verified=NextWork=NextAsk=NextPublish=0;
        }
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class MasterIdolForestLifecycleRegisterPatch{static void Postfix(ZNetPeer peer)=>MasterIdolForestGuardLifecycle.Register(peer);}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolForestLifecycleTickPatch{static void Postfix(){MasterIdolForestGuardLifecycleController.Tick();MasterIdolForestGuardLifecycle.Tick();}}
    [HarmonyPatch(typeof(ZNet),"OnDestroy")]
    internal static class MasterIdolForestLifecycleResetPatch{static void Prefix(){MasterIdolForestGuardLifecycleController.Reset();MasterIdolForestGuardLifecycle.Reset();}}
}
