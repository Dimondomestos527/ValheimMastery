using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
namespace ValheimMastery
{
    // Authoritative geometry projection for vanilla owner-driven farming/AI. No financial data.
    internal static class MasterIdolEffectZones
    {
        private const int MaxBytes=MasterIdolProjectionCodec.MaxBytes;
        private static ZNet Session;private static ZRpc ServerRpc;private static long World,Revision;
        private static float Next,LastVerified,NextAsk;
        private static bool Available;
        private static string Digest;
        private static byte[] Body;private static byte[][] Pages;
        private static ZDOMan Manager;private static ulong Generation;private static long HighestSeen;private static string ClientToken,Epoch,CommittedEpoch;
        private static readonly MasterIdolProjectionCodec Assembly=new MasterIdolProjectionCodec();
        private static readonly Dictionary<ZRpc,string> Tokens=new Dictionary<ZRpc,string>();private static readonly Dictionary<ZRpc,float> AskTimes=new Dictionary<ZRpc,float>();
        private static MasterIdolEffectGeometry Geometry=new MasterIdolEffectGeometry();
        private static readonly Dictionary<ZRpc,long> Sent=new Dictionary<ZRpc,long>();
        private static bool Current()
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            var rpc=net?.IsServer()==true?null:net?.GetServerRPC();
            if(Session!=net||World!=world||ServerRpc!=rpc||!ReferenceEquals(Manager,ZDOMan.instance)||Generation!=MasterIdolLoadIdentity.Generation){Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;ClientToken=Guid.NewGuid().ToString("N");Epoch=Guid.NewGuid().ToString("N");CommittedEpoch=null;HighestSeen=0;Assembly.Reset();Tokens.Clear();AskTimes.Clear();Pages=null;Session=net;ServerRpc=rpc;World=world;Revision=0;Next=LastVerified=NextAsk=0;Digest=null;Body=null;Available=false;Geometry=new MasterIdolEffectGeometry();Sent.Clear();}
            return net!=null&&world!=0;
        }
        internal static MasterIdolEffectGeometry Snapshot => Ready ? Geometry : null;
        internal static string Diagnostics()=>"effectsReady="+Ready+" server="+(Session?.IsServer()==true)+" world="+World+" revision="+Revision+" zones="+Geometry.ById.Count+" residents="+Geometry.ById.Values.Sum(z=>z.Residents.Count)+" caps=Greyling4/Greydwarf2/Elite1/Shaman1 wards="+Geometry.Wards.Count+" proofAge="+Math.Max(0,Time.unscaledTime-LastVerified).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
        internal static bool Ready=>Current()&&Available&&(Session.IsServer()||Time.unscaledTime-LastVerified<6f);
        internal static MasterIdolEffectZone At(string type,Vector3 point)=>Ready?Geometry.At(type,point):null;
        internal static MasterIdolEffectZone Find(string id)=>Ready&&id!=null&&Geometry.ById.TryGetValue(id,out var zone)?zone:null;
        internal static MasterIdolEffectZone ResidentHome(string uid,string family)
        {return Ready&&uid!=null&&Geometry.ResidentHomes.TryGetValue(uid,out var zone)&&zone.Type=="BlackForest"&&zone.Residents.TryGetValue(uid,out string type)&&type==family?zone:null;}
        internal static bool Allows(long player,Vector3 point)=>Ready&&Geometry.Allows(player,point);
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>("VM_Idol_Zones_v3",Receive);peer?.m_rpc?.Register<ZPackage>("VM_Idol_Zones_Ask_v3",Ask);}
        internal static void Tick()
        {
            if(!Current()||Time.unscaledTime<Next)return;Next=Time.unscaledTime+2f;
            if(!Session.IsServer()){if(!Ready)Request();return;}using var aggregate=new MasterIdolPerf.Scope("projection.tick");
            try
            {
                var zones=MasterIdolWorldRegistry.EffectZones();var wards=MasterIdolWorldIndex.EffectWards();
                var assigned=new HashSet<string>(StringComparer.Ordinal);
                var activeHomes=new HashSet<string>(zones.Where(z=>z.Type=="BlackForest").Select(z=>z.Id),StringComparer.Ordinal);
                var previousHomes=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var oldZone in Geometry.ById.Values)if(activeHomes.Contains(oldZone.Id))foreach(string uid in oldZone.Residents.Keys)previousHomes[uid]=oldZone.Id;
                foreach(var zone in zones.Where(z=>z.Type=="BlackForest").OrderBy(z=>z.Id,StringComparer.Ordinal))
                {
                    var previous=Geometry.ById.TryGetValue(zone.Id,out var prior)?new HashSet<string>(prior.Residents.Keys,StringComparer.Ordinal):new HashSet<string>(StringComparer.Ordinal);
                    zone.Residents=MasterIdolResidentRoster.Select(MasterIdolWorldIndex.ResidentCandidates(zone,previous).Concat(MasterIdolRecruitment.Pending(zone)).Where(c=>MasterIdolResidentRoster.MayJoin(c,zone.Id,activeHomes,previousHomes)),previous,assigned);
                }
                MasterIdolRecruitment.Tick(zones,assigned,()=>CanRecruit(zones,wards));
                if(!Available||!Same(zones,wards)){
                    byte[] body=Encode(zones,wards);if(body.Length>MaxBytes)throw new InvalidOperationException("Idol effect snapshot capacity exceeded.");
                    var geometry=Parse(body);foreach(var zone in zones)geometry.ById[zone.Id].Circles=zone.Circles;Body=body;Geometry=geometry;using(var hash=SHA256.Create())Digest=Convert.ToBase64String(hash.ComputeHash(body));
                    Pages=new byte[(body.Length+MasterIdolProjectionCodec.ChunkBytes-1)/MasterIdolProjectionCodec.ChunkBytes][];
                    for(int page=0;page<Pages.Length;page++){int start=page*MasterIdolProjectionCodec.ChunkBytes;Pages[page]=new byte[Math.Min(MasterIdolProjectionCodec.ChunkBytes,body.Length-start)];Buffer.BlockCopy(body,start,Pages[page],0,Pages[page].Length);}
                    MasterIdolPerf.Count("projection.encode");Revision++;Available=true;MasterIdolSeatRosterGuard.ObserveProjection(Geometry);if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogInfo("[IdolTest] projection revision="+Revision+" topology="+MasterIdolWorldIndex.TopologyRevision+" zones="+zones.Count+" circles="+zones.Sum(z=>z.Circles.Length)+" residents="+zones.Sum(z=>z.Residents.Count));}
                LastVerified=Time.unscaledTime;
                var peers=Session.GetPeers();var active=new HashSet<ZRpc>();foreach(var peer in peers)
                {
                    var rpc=peer?.m_rpc;if(rpc==null)continue;active.Add(rpc);
                    if(!Tokens.TryGetValue(rpc,out string token))continue;
                    bool full=!Sent.TryGetValue(rpc,out long revision)||revision!=Revision;
                    if(full)for(int page=0;page<Pages.Length;page++){var update=Header(token,true);update.Write(Body.Length);update.Write(page);update.Write(Pages.Length);update.Write(Pages[page]);rpc.Invoke("VM_Idol_Zones_v3",update);}else rpc.Invoke("VM_Idol_Zones_v3",Header(token,false));
                    Sent[rpc]=Revision;
                }
                foreach(var rpc in Tokens.Keys.Where(rpc=>!active.Contains(rpc)).ToArray()){Sent.Remove(rpc);Tokens.Remove(rpc);AskTimes.Remove(rpc);}
            }
            catch(Exception error){Available=false;Sent.Clear();MasteryPlugin.Log.LogWarning("[MasterIdols] Effects unavailable: "+error.Message);}
        }
        private static byte[] Encode(List<MasterIdolEffectZone> zones,List<MasterIdolEffectWard> wards)
        {
            var packet=new ZPackage();packet.Write(zones.Count);
                foreach(var zone in zones){packet.Write(zone.Id);packet.Write(zone.Type);packet.Write(zone.Home);packet.Write(zone.Circles.Length);foreach(var circle in zone.Circles){packet.Write(circle.Point);packet.Write(circle.Radius);}packet.Write(zone.Residents.Count);foreach(var member in zone.Residents.OrderBy(x=>x.Key,StringComparer.Ordinal)){packet.Write(member.Key);packet.Write(member.Value);}}
                packet.Write(wards.Count);foreach(var ward in wards){packet.Write(ward.Circle.Point);packet.Write(ward.Circle.Radius);packet.Write(ward.Creator);packet.Write(ward.Permitted.Length);foreach(long player in ward.Permitted)packet.Write(player);}
                
            return packet.GetArray();
        }
        private static bool CanRecruit(List<MasterIdolEffectZone> zones,List<MasterIdolEffectWard> wards)
        {
            try{var body=Encode(zones,wards);if(body.Length>MaxBytes-512)return false;Parse(body);return true;}catch{return false;}
        }
        private static void Request()
        {
            if(Session==null||Session.IsServer()||Time.unscaledTime<NextAsk)return;NextAsk=Time.unscaledTime+2f;
            var packet=new ZPackage();packet.Write(World);packet.Write(ClientToken);packet.Write(Revision);Session.GetServerRPC()?.Invoke("VM_Idol_Zones_Ask_v3",packet);
        }
        private static ZPackage Header(string token,bool full){var packet=new ZPackage();packet.Write(World);packet.Write(token);packet.Write(Epoch);packet.Write(Revision);packet.Write(Digest);packet.Write(full);return packet;}
        private static bool Same(List<MasterIdolEffectZone> zones,List<MasterIdolEffectWard> wards)
        {
            if(zones.Count!=Geometry.ById.Count||wards.Count!=Geometry.Wards.Count)return false;
            foreach(var zone in zones){if(!Geometry.ById.TryGetValue(zone.Id,out var old)||old.Type!=zone.Type||(old.Home-zone.Home).sqrMagnitude!=0||!ReferenceEquals(old.Circles,zone.Circles)||old.Residents.Count!=zone.Residents.Count)return false;foreach(var member in zone.Residents)if(!old.Residents.TryGetValue(member.Key,out string family)||family!=member.Value)return false;}
            for(int i=0;i<wards.Count;i++){var a=wards[i];var b=Geometry.Wards[i];if(a.Creator!=b.Creator||a.Circle.Radius!=b.Circle.Radius||(a.Circle.Point-b.Circle.Point).sqrMagnitude!=0||a.Permitted.Length!=b.Permitted.Length)return false;for(int n=0;n<a.Permitted.Length;n++)if(a.Permitted[n]!=b.Permitted[n])return false;}
            return true;
        }
        private static void Ask(ZRpc rpc,ZPackage packet)
        {
            if(!Current()||!Session.IsServer()||rpc==null||Session.GetPeer(rpc)==null||packet==null||packet.Size()>96)return;
            try{long world=packet.ReadLong();string token=packet.ReadString();long known=packet.ReadLong();if(world!=World||!Guid.TryParseExact(token,"N",out _)||known<0||packet.GetPos()!=packet.Size())return;if(AskTimes.TryGetValue(rpc,out float last)&&Time.unscaledTime-last<1.5f)return;AskTimes[rpc]=Time.unscaledTime;if(!Tokens.TryGetValue(rpc,out string prior)||prior!=token||known!=Revision)Sent.Remove(rpc);Tokens[rpc]=token;}catch{}
        }
        private static void Receive(ZRpc rpc,ZPackage packet)
        {
            if(!Current()||Session.IsServer()||rpc!=Session.GetServerRPC()||packet==null||packet.Size()>MasterIdolProjectionCodec.ChunkBytes+256)return;
            try{
                if(packet.ReadLong()!=World||packet.ReadString()!=ClientToken)return;string epoch=packet.ReadString();long revision=packet.ReadLong();string digest=packet.ReadString();bool full=packet.ReadBool();
                if(!Guid.TryParseExact(epoch,"N",out _)||revision<=0||digest.Length!=44)return;
                if((CommittedEpoch??Assembly.SeenEpoch)!=null&&(CommittedEpoch??Assembly.SeenEpoch)!=epoch){Available=false;Assembly.Reset();Revision=0;HighestSeen=0;CommittedEpoch=null;ClientToken=Guid.NewGuid().ToString("N");NextAsk=0;Request();return;}
                if(revision<Revision||revision<HighestSeen||Assembly.SeenEpoch==epoch&&revision<Assembly.SeenRevision)return;HighestSeen=Math.Max(HighestSeen,revision);
                if(!full){if(packet.GetPos()!=packet.Size()||!Available||CommittedEpoch!=epoch||revision!=Revision||digest!=Digest){Request();return;}LastVerified=Time.unscaledTime;return;}
                int length=packet.ReadInt(),index=packet.ReadInt(),count=packet.ReadInt();int bytesAt=packet.GetPos(),declared=packet.ReadInt();if(declared<0||declared>MasterIdolProjectionCodec.ChunkBytes||declared!=packet.Size()-packet.GetPos())throw new FormatException();packet.SetPos(bytesAt);var bytes=packet.ReadByteArray();if(packet.GetPos()!=packet.Size())throw new FormatException();
                var body=Assembly.Add(epoch,revision,digest,length,index,count,bytes,Time.unscaledTime);if(body==null)return;
                var geometry=Parse(body);Geometry=geometry;Revision=revision;Digest=digest;CommittedEpoch=epoch;Available=true;MasterIdolSeatRosterGuard.ObserveProjection(Geometry);LastVerified=Time.unscaledTime;
            }catch{Available=false;Assembly.Reset();Request();}
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static Vector3 Point(ZPackage packet)
        {var point=packet.ReadVector3();if(!Finite(point.x)||!Finite(point.y)||!Finite(point.z)||Math.Abs(point.x)>1000000||Math.Abs(point.z)>1000000)throw new FormatException();return point;}
        private static MasterIdolEffectCircle Circle(ZPackage packet)
        {var point=Point(packet);float radius=packet.ReadSingle();if(!Finite(radius)||radius<=0||radius>1000)throw new FormatException();return new MasterIdolEffectCircle(point,radius);}
        private static MasterIdolEffectGeometry Parse(byte[] body)
        {
            var result=new MasterIdolEffectGeometry();var packet=new ZPackage(body);int count=packet.ReadInt(),total=0;
            if(count<0||count>4096)throw new FormatException();var assigned=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<count;i++)
            {
                string id=packet.ReadString(),type=packet.ReadString();var parts=id.Split('/');if(parts.Length!=2||!MasterIdolNetworkQuota.ValidUid(parts[0])||!Guid.TryParseExact(parts[1],"N",out _)||(type!="Meadows"&&type!="BlackForest"&&type!="Swamp"&&type!="Mountain"&&type!="Plains"&&type!="Mistlands"))throw new FormatException();
                var home=Point(packet);int members=packet.ReadInt();total+=members;if(members<=0||members>4096||total>131072)throw new FormatException();
                var circles=new MasterIdolEffectCircle[members];for(int n=0;n<members;n++)circles[n]=Circle(packet);
                var zone=new MasterIdolEffectZone{Id=id,Type=type,Home=home,Circles=circles};
                int residents=packet.ReadInt();if(residents<0||residents>8||type!="BlackForest"&&residents!=0)throw new FormatException();
                var counts=new Dictionary<string,int>(StringComparer.Ordinal);
                for(int n=0;n<residents;n++)
                {
                    string uid=packet.ReadString(),family=packet.ReadString();counts.TryGetValue(family,out int current);int limit=MasterIdolResidentRoster.Limit(family);
                    if(!MasterIdolNetworkQuota.ValidUid(uid)||limit==0||current>=limit||!assigned.Add(uid))throw new FormatException();
                    counts[family]=current+1;zone.Residents.Add(uid,family);
                }
                if(!result.Add(zone))throw new FormatException();
            }
            count=packet.ReadInt();if(count<0||count>4096)throw new FormatException();int permittedTotal=0;
            for(int i=0;i<count;i++)
            {
                var circle=Circle(packet);long creator=packet.ReadLong();int members=packet.ReadInt();permittedTotal+=members;if(members<0||members>4096||permittedTotal>16384)throw new FormatException();
                var ids=new long[members];for(int n=0;n<members;n++)ids[n]=packet.ReadLong();result.Wards.Add(new MasterIdolEffectWard{Circle=circle,Creator=creator,Permitted=ids});
            }
            if(packet.GetPos()!=packet.Size())throw new FormatException();return result;
        }
    }
}












