using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal enum ObserverCueKind:byte { EnchantLearn=1,EnchantActivate=2,CraftingWork=3,CraftingStop=4,CraftingApplied=5 }
    // Fixed semantic events only. This data cannot select a prefab or execute a gameplay action.
    internal sealed class ObserverCueEvent
    {
        internal ObserverCueKind Kind;internal long World,Issued,Until,Player,Owner,Revision;
        internal ushort OwnerRevision;internal int Operation;
        internal string Session,Id,Marker,Choice;
        internal ZDOID Actor,Station;internal Vector3 Position;
        internal string Key=>Session+"/"+(byte)Kind+"/"+Id;
        internal string WorkKey=>Session+"/work/"+Player+"/"+Id;
    }
    internal static class ObserverCueNetwork
    {
        internal const string EventRpc="VM_ObserverCue_v1",WorkRpc="VM_ObserverWork_v1";
        private const int PacketCap=512,CacheCap=256,AdmissionCap=128;
        private static ZNet Net;private static ZDOMan Manager;private static ZNetScene Scene;private static ZRpc ServerRpc;
        private static long World;private static ulong Generation;private static string Session;
        private static Action<ObserverCueEvent> Present;
        private static readonly Dictionary<string,double> Sent=new Dictionary<string,double>(),Seen=new Dictionary<string,double>();
        private static readonly Dictionary<string,double> ClosedWork=new Dictionary<string,double>();
        private static readonly List<string> Expired=new List<string>(CacheCap);
        private sealed class Stamp
        {
            internal string Encoded,Id;internal ZRpc Rpc;internal ZNetPeer Peer;internal ZDO Data;
            internal ZDOID Actor,Station;internal long Player,Owner;internal ushort Revision;internal int Operation;internal double Until;
        }
        private static readonly Dictionary<string,Stamp> Admissions=new Dictionary<string,Stamp>(StringComparer.Ordinal);
        private static readonly Dictionary<long,Stamp> Work=new Dictionary<long,Stamp>();
        private static readonly Dictionary<long,double> NextWork=new Dictionary<long,double>();
        private static readonly List<long> OldWorkers=new List<long>(128);
        private sealed class LocalWork
        {internal ZNet Net;internal ZRpc Rpc;internal ZDO Data;internal ZDOID Actor,Station;internal long World,Player;internal ushort Revision;internal string Id;internal int Operation;}
        private static LocalWork Local;
        internal static void Configure(Action<ObserverCueEvent> present){Present=present;}
        internal static void RegisterPeer(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>(EventRpc,ReceiveEvent);peer?.m_rpc?.Register<ZPackage>(WorkRpc,ReceiveWork);}
        internal static void Reset()
        {Sent.Clear();Seen.Clear();ClosedWork.Clear();Admissions.Clear();Work.Clear();NextWork.Clear();Expired.Clear();OldWorkers.Clear();Local=null;Net=null;Manager=null;Scene=null;ServerRpc=null;World=0;Generation=0;Session=null;}
        private static bool Context()
        {
            var net=ZNet.instance;var manager=ZDOMan.instance;var scene=ZNetScene.instance;
            if(net?.GetWorld()==null||manager==null||scene==null||MasteryPlugin.Instance?.isActiveAndEnabled!=true||MasteryPlugin.Settings?.Enabled?.Value!=true){Reset();return false;}
            var rpc=net.IsServer()?null:net.GetServerRPC();
            // A temporary handshake gap must not erase consumed IDs and permit an in-context replay.
            if(!net.IsServer()&&!NetworkSync.HasCurrentServerSettings(rpc))return false;
            if(Net!=net||Manager!=manager||Scene!=scene||World!=net.GetWorldUID()||Generation!=MasterIdolLoadIdentity.Generation||ServerRpc!=rpc)
            {Reset();Net=net;Manager=manager;Scene=scene;World=net.GetWorldUID();Generation=MasterIdolLoadIdentity.Generation;ServerRpc=rpc;Session=net.IsServer()?Guid.NewGuid().ToString("N"):null;}
            return World!=0;
        }
        private static bool PeerCurrent(ZRpc rpc)
        {if(rpc==null)return Net.IsServer()&&Player.m_localPlayer!=null;var peer=Net.GetPeer(rpc);return peer?.m_rpc==rpc&&NetworkSync.HasPeerSettings(peer);}
        private static ZDO ActorData(WorkshopActor actor)
        {
            if(actor?.Available!=true||!PeerCurrent(actor.Rpc))return null;
            var data=Manager.GetZDO(actor.CharacterId);
            long owner=actor.Rpc==null?ZNet.GetUID():Net.GetPeer(actor.Rpc).m_uid;
            return data!=null&&data.GetOwner()==owner&&data.GetLong(ZDOVars.s_playerID,0)==actor.GetPlayerID()?data:null;
        }
        private static void Prune(Dictionary<string,double> cache)
        {Expired.Clear();double now=Time.unscaledTimeAsDouble;foreach(var pair in cache)if(now>=pair.Value)Expired.Add(pair.Key);foreach(var key in Expired)cache.Remove(key);}
        private static bool Once(Dictionary<string,double> cache,string key)
        {Prune(cache);if(cache.ContainsKey(key)||cache.Count>=CacheCap)return false;cache.Add(key,Time.unscaledTimeAsDouble+30);return true;}
        private static string AdmissionKey(long player,string token)=>player+"/"+token;
        private static void PruneAdmissions()
        {
            Expired.Clear();double now=Time.unscaledTimeAsDouble;
            foreach(var pair in Admissions)if(now>=pair.Value.Until||!PeerCurrent(pair.Value.Rpc))Expired.Add(pair.Key);
            foreach(var key in Expired)Admissions.Remove(key);
        }
        private static Stamp Capture(WorkshopActor actor,ZDO data,string id,ZDOID station,string encoded,double duration)
            =>new Stamp{Id=id,Encoded=encoded,Rpc=actor.Rpc,Peer=actor.Rpc==null?null:Net.GetPeer(actor.Rpc),Data=data,Actor=actor.CharacterId,Station=station,Player=actor.GetPlayerID(),Owner=data.GetOwner(),Revision=data.OwnerRevision,Until=Time.unscaledTimeAsDouble+duration};
        private static bool Current(Stamp stamp,WorkshopActor actor,ZDO data)
            =>stamp!=null&&Time.unscaledTimeAsDouble<stamp.Until&&stamp.Rpc==actor.Rpc&&(stamp.Rpc==null||ReferenceEquals(stamp.Peer,Net.GetPeer(stamp.Rpc)))&&stamp.Actor==actor.CharacterId&&stamp.Player==actor.GetPlayerID()&&
                ReferenceEquals(stamp.Data,data)&&stamp.Owner==data.GetOwner()&&stamp.Revision==data.OwnerRevision;
        internal static void MarkCraftingAdmitted(WorkshopActor actor,GoldAction action)
        {
            try
            {
                if(!Context()||!Net.IsServer()||action==null||!Guid.TryParseExact(action.Token,"N",out _))return;
                var data=ActorData(actor);if(data==null)return;PruneAdmissions();
                string key=AdmissionKey(actor.GetPlayerID(),action.Token);
                if(Admissions.ContainsKey(key)||Admissions.Count>=AdmissionCap)return;
                Admissions.Add(key,Capture(actor,data,action.Token,action.Station,action.Encode(),30));
            }catch(Exception){ }
        }
        internal static void PublishCraftingApplied(WorkshopActor actor,GoldAction action)
        {
            try
            {
                if(!Context()||!Net.IsServer()||action==null||actor==null)return;
                string key=AdmissionKey(actor.GetPlayerID(),action.Token);
                if(!Admissions.TryGetValue(key,out var stamp))return;
                Admissions.Remove(key); // Consume before validation/callback; never retry a cosmetic success.
                var data=ActorData(actor);
                if(data==null||!Current(stamp,actor,data)||stamp.Encoded!=action.Encode()||stamp.Station!=action.Station)return;
                Publish(Create(actor,data,ObserverCueKind.CraftingApplied,action.Token,action.Station,"","",0,action.Operation),true);
            }catch(Exception){ }
        }
        internal static void PublishEnchantmentCommitted(WorkshopActor actor,ZDOID station,string marker,string choice,string eventId,long revision,bool firstLearn)
        {
            try
            {
                if(!Context()||!Net.IsServer()||!WorkbenchEnchantmentRules.Known(choice)||!Guid.TryParseExact(marker,"N",out _)||revision<0)return;
                var data=ActorData(actor);var bench=Manager.GetZDO(station);
                if(data==null||bench==null||bench.GetPrefab()!="piece_workbench".GetStableHashCode()||bench.GetString(WorkbenchEnchantmentRuntime.StationKey,"")!=marker)return;
                Publish(Create(actor,data,firstLearn?ObserverCueKind.EnchantLearn:ObserverCueKind.EnchantActivate,eventId,station,marker,choice,revision,0),false);
            }catch(Exception){ }
        }
        private static ObserverCueEvent Create(WorkshopActor actor,ZDO data,ObserverCueKind kind,string id,ZDOID station,string marker,string choice,long revision,int operation)
        {
            long now=Net.GetTime().Ticks;var bench=station==ZDOID.None?null:Manager.GetZDO(station);
            return new ObserverCueEvent{Kind=kind,World=World,Session=Session,Id=id,Actor=actor.CharacterId,Player=actor.GetPlayerID(),Owner=data.GetOwner(),OwnerRevision=data.OwnerRevision,
                Station=station,Marker=marker,Choice=choice,Revision=revision,Operation=operation,Issued=now,Until=now+TimeSpan.TicksPerSecond*(kind==ObserverCueKind.CraftingWork?5:3),
                Position=kind==ObserverCueKind.EnchantLearn||kind==ObserverCueKind.EnchantActivate?bench.GetPosition():actor.Position};
        }
        private static void Publish(ObserverCueEvent cue,bool excludeActor)
        {
            if(!Valid(cue,Net.GetTime().Ticks)||!Once(Sent,cue.Key))return;
            var packet=Encode(cue);
            if(!excludeActor||Player.m_localPlayer?.GetPlayerID()!=cue.Player)Dispatch(cue);
            foreach(var peer in Net.GetPeers())
                if(peer?.m_rpc!=null&&NetworkSync.HasPeerSettings(peer)&&(!excludeActor||peer.m_uid!=cue.Owner))
                    try{peer.m_rpc.Invoke(EventRpc,new ZPackage(packet.GetArray()));}catch(Exception){ }
        }
        private static void Dispatch(ObserverCueEvent cue)
        {
            if(Application.isBatchMode||Player.m_localPlayer==null)return;
            Prune(ClosedWork);
            if(cue.Kind==ObserverCueKind.CraftingWork&&(ClosedWork.ContainsKey(cue.WorkKey)||ClosedWork.Count>=128))return;
            if(cue.Kind==ObserverCueKind.CraftingStop&&!ClosedWork.ContainsKey(cue.WorkKey)&&ClosedWork.Count<128)
                ClosedWork.Add(cue.WorkKey,Time.unscaledTimeAsDouble+30);
            if(!Once(Seen,cue.Key))return;
            try{Present?.Invoke(cue);}catch(Exception){ }
        }
        private static void ReceiveEvent(ZRpc rpc,ZPackage packet)
        {
            try
            {
                if(!Context()||Net.IsServer()||rpc!=ServerRpc||!NetworkSync.HasCurrentServerSettings(rpc)||!Decode(packet,out var cue)||cue.World!=World||!Valid(cue,Net.GetTime().Ticks))return;
                if(Session==null)Session=cue.Session;else if(Session!=cue.Session)return;
                Dispatch(cue);
            }catch(Exception){ }
        }
        // Cosmetic intent only: no recipe/permit/item/debit is created by this RPC.
        internal static void BeginCrafting100Work(Player actor,ZDOID station,string cueId,int operation)
        {
            try
            {
                if(!Context()||actor==null||actor!=Player.m_localPlayer||actor.m_nview?.IsValid()!=true||!actor.m_nview.IsOwner()||!Guid.TryParseExact(cueId,"N",out _))return;
                if(Local!=null)EndCrafting100Work(Local.Id);
                var data=actor.m_nview.GetZDO();
                Local=new LocalWork{Net=Net,Rpc=ServerRpc,World=World,Data=data,Actor=data.m_uid,Revision=data.OwnerRevision,Player=actor.GetPlayerID(),Station=station,Id=cueId,Operation=operation};
                SendWork(Local,true);
            }catch(Exception){ }
        }
        internal static void EndCrafting100Work(string cueId)
        {
            var old=Local;if(old==null||old.Id!=cueId)return;Local=null;
            try{if(!Context()||old.Net!=Net||old.Rpc!=ServerRpc||old.World!=World||Player.m_localPlayer?.GetPlayerID()!=old.Player)return;
                var data=Player.m_localPlayer?.m_nview?.GetZDO();if(!ReferenceEquals(data,old.Data)||data.OwnerRevision!=old.Revision)return;SendWork(old,false);}catch(Exception){ }
        }
        private static void SendWork(LocalWork work,bool start)
        {
            var p=new ZPackage();p.Write(1);p.Write(work.World);p.Write(work.Actor);p.Write(work.Player);p.Write(work.Id);p.Write(work.Station);p.Write(work.Operation);p.Write(start);p.Write(Net.GetTime().Ticks);
            if(Net.IsServer()){p.SetPos(0);ReceiveWork(null,p);}else ServerRpc.Invoke(WorkRpc,p);
        }
        private static void ReceiveWork(ZRpc rpc,ZPackage packet)
        {
            try
            {
                if(!Context()||!Net.IsServer()||!PeerCurrent(rpc)||packet==null||packet.Size()>160)return;
                int version=packet.ReadInt();long world=packet.ReadLong();var uid=packet.ReadZDOID();long player=packet.ReadLong();string id=packet.ReadString();var station=packet.ReadZDOID();int operation=packet.ReadInt();bool start=packet.ReadBool();long issued=packet.ReadLong(),now=Net.GetTime().Ticks;
                if(version!=1||world!=World||packet.GetPos()!=packet.Size()||!Guid.TryParseExact(id,"N",out _)||operation<1||operation>3||issued<=0||issued>now+TimeSpan.TicksPerSecond||now-issued>TimeSpan.TicksPerSecond)return;
                var actor=WorkshopActor.Resolve(rpc);var data=ActorData(actor);
                if(data==null||actor.CharacterId!=uid||actor.GetPlayerID()!=player)return;
                if(!start)
                {
                    if(!Work.TryGetValue(player,out var old)||old.Id!=id||old.Station!=station||old.Operation!=operation||!Current(old,actor,data))return;
                    Work.Remove(player);Publish(Create(actor,data,ObserverCueKind.CraftingStop,id,station,"","",0,operation),true);return;
                }
                // Authentication/authority is checked before a cosmetic hint is admitted.
                if(actor.IsDead()||actor.IsTeleporting()||!GoldCraftingService.HasGold(actor))return;
                if(station!=ZDOID.None)
                {
                    var entry=WorkshopWorldRecords.Find(station);var native=entry?.Station;
                    if(native==null||Vector3.Distance(actor.Position,entry.Data.GetPosition())>=native.m_useDistance||!WorkshopWorldRecords.WardAllows(player,entry.Data.GetPosition())||!WorkshopStationProof.Usable(entry.Data,native))return;
                }
                else if(operation!=1)return;
                double monotonic=Time.unscaledTimeAsDouble;
                OldWorkers.Clear();foreach(var pair in Work)if(monotonic>=pair.Value.Until||!PeerCurrent(pair.Value.Rpc))OldWorkers.Add(pair.Key);
                foreach(var oldPlayer in OldWorkers){Work.Remove(oldPlayer);NextWork.Remove(oldPlayer);}
                OldWorkers.Clear();foreach(var pair in NextWork)if(monotonic>=pair.Value)OldWorkers.Add(pair.Key);
                foreach(var oldPlayer in OldWorkers)NextWork.Remove(oldPlayer);
                if(Work.TryGetValue(player,out var active)&&monotonic<active.Until||NextWork.TryGetValue(player,out var next)&&monotonic<next)return;
                if(!Work.ContainsKey(player)&&Work.Count>=128||!NextWork.ContainsKey(player)&&NextWork.Count>=128)return;
                if(!Once(Sent,"work-admit/"+player+"/"+id))return;
                Work[player]=Capture(actor,data,id,station,"",5);Work[player].Operation=operation;NextWork[player]=monotonic+1;
                Publish(Create(actor,data,ObserverCueKind.CraftingWork,id,station,"","",0,operation),true);
            }catch(Exception){ }
        }
        private static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)&&Mathf.Abs(p.x)<=100000&&Mathf.Abs(p.y)<=100000&&Mathf.Abs(p.z)<=100000;
        private static bool Valid(ObserverCueEvent e,long now)
        {
            if(e==null||e.Kind<ObserverCueKind.EnchantLearn||e.Kind>ObserverCueKind.CraftingApplied||e.World==0||!Guid.TryParseExact(e.Session,"N",out _)||
                string.IsNullOrEmpty(e.Id)||e.Id.Length>80||e.Actor==ZDOID.None||e.Player==0||e.Owner==0||!Finite(e.Position)||e.Revision<0||e.Issued<=0||e.Issued>DateTime.MaxValue.Ticks-10*TimeSpan.TicksPerSecond||
                e.Until<=e.Issued||e.Until>DateTime.MaxValue.Ticks||e.Until-e.Issued!=TimeSpan.TicksPerSecond*(e.Kind==ObserverCueKind.CraftingWork?5:3)||now<e.Issued-TimeSpan.TicksPerSecond||now>=e.Until)return false;
            if(e.Kind==ObserverCueKind.EnchantLearn||e.Kind==ObserverCueKind.EnchantActivate)
                return e.Station!=ZDOID.None&&Guid.TryParseExact(e.Marker,"N",out _)&&WorkbenchEnchantmentRules.Known(e.Choice)&&e.Operation==0;
            return e.Marker==""&&e.Choice==""&&e.Operation>=1&&e.Operation<=3&&Guid.TryParseExact(e.Id,"N",out _);
        }
        private static void Uid(BinaryWriter w,ZDOID id){w.Write(id.UserID);w.Write(id.ID);}
        private static ZDOID Uid(BinaryReader r)=>new ZDOID(r.ReadInt64(),r.ReadUInt32());
        private static ZPackage Encode(ObserverCueEvent e)
        {
            using(var stream=new MemoryStream())
            {
                using(var w=new BinaryWriter(stream,Encoding.UTF8,true))
                {w.Write((byte)1);w.Write((byte)e.Kind);w.Write(e.World);w.Write(e.Session);w.Write(e.Id);Uid(w,e.Actor);w.Write(e.Player);w.Write(e.Owner);w.Write(e.OwnerRevision);Uid(w,e.Station);w.Write(e.Marker);w.Write(e.Choice);w.Write(e.Revision);w.Write(e.Operation);w.Write(e.Issued);w.Write(e.Until);w.Write(e.Position.x);w.Write(e.Position.y);w.Write(e.Position.z);}
                if(stream.Length>PacketCap)throw new InvalidDataException("Observer event cap");return new ZPackage(stream.ToArray());
            }
        }
        private static bool Decode(ZPackage packet,out ObserverCueEvent cue)
        {
            cue=null;
            try
            {
                if(packet==null||packet.Size()>PacketCap)return false;
                using(var r=new BinaryReader(new MemoryStream(packet.GetArray()),Encoding.UTF8))
                {
                    if(r.ReadByte()!=1)return false;
                    var e=new ObserverCueEvent{Kind=(ObserverCueKind)r.ReadByte(),World=r.ReadInt64(),Session=MasterIdolForestRules.ReadText(r,32),Id=MasterIdolForestRules.ReadText(r,80),Actor=Uid(r),Player=r.ReadInt64(),Owner=r.ReadInt64(),OwnerRevision=r.ReadUInt16(),Station=Uid(r),Marker=MasterIdolForestRules.ReadText(r,32),Choice=MasterIdolForestRules.ReadText(r,32),Revision=r.ReadInt64(),Operation=r.ReadInt32(),Issued=r.ReadInt64(),Until=r.ReadInt64(),Position=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle())};
                    if(r.BaseStream.Position!=r.BaseStream.Length)return false;cue=e;return true;
                }
            }catch(Exception){return false;}
        }
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class ObserverCueRegisterPatch
    {private static void Postfix(ZNetPeer peer)=>ObserverCueNetwork.RegisterPeer(peer);}
}
