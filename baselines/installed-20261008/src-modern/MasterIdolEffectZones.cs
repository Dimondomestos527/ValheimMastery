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
        private const int MaxBytes=262144;
        private static ZNet Session;private static ZRpc ServerRpc;private static long World,Revision;
        private static float Next,LastVerified,NextAsk;
        private static bool Available;
        private static string Digest;
        private static byte[] Body;
        private static MasterIdolEffectGeometry Geometry=new MasterIdolEffectGeometry();
        private static readonly Dictionary<ZRpc,long> Sent=new Dictionary<ZRpc,long>();
        private static bool Current()
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            var rpc=net?.IsServer()==true?null:net?.GetServerRPC();
            if(Session!=net||World!=world||ServerRpc!=rpc){Session=net;ServerRpc=rpc;World=world;Revision=0;Next=LastVerified=NextAsk=0;Digest=null;Body=null;Available=false;Geometry=new MasterIdolEffectGeometry();Sent.Clear();}
            return net!=null&&world!=0;
        }
        internal static string Diagnostics()=>"effectsReady="+Ready+" server="+(Session?.IsServer()==true)+" world="+World+" revision="+Revision+" zones="+Geometry.ById.Count+" residents="+Geometry.ById.Values.Sum(z=>z.Residents.Count)+" caps=Greyling4/Greydwarf2/Elite1/Shaman1 wards="+Geometry.Wards.Count+" proofAge="+Math.Max(0,Time.unscaledTime-LastVerified).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
        internal static bool Ready=>Current()&&Available&&(Session.IsServer()||Time.unscaledTime-LastVerified<6f);
        internal static MasterIdolEffectZone At(string type,Vector3 point)=>Ready?Geometry.At(type,point):null;
        internal static MasterIdolEffectZone Find(string id)=>Ready&&id!=null&&Geometry.ById.TryGetValue(id,out var zone)?zone:null;
        internal static MasterIdolEffectZone ResidentHome(string uid,string family)
        {return Ready&&uid!=null&&Geometry.ResidentHomes.TryGetValue(uid,out var zone)&&zone.Type=="BlackForest"&&zone.Residents.TryGetValue(uid,out string type)&&type==family?zone:null;}
        internal static bool Allows(long player,Vector3 point)=>Ready&&Geometry.Allows(player,point);
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>("VM_Idol_Zones_v2",Receive);peer?.m_rpc?.Register<ZPackage>("VM_Idol_Zones_Ask_v2",Ask);}
        internal static void Tick()
        {
            if(!Current()||Time.unscaledTime<Next)return;Next=Time.unscaledTime+2f;
            if(!Session.IsServer()){if(!Ready)Request();return;}
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
                byte[] body=Encode(zones,wards);if(body.Length>MaxBytes)throw new InvalidOperationException("Idol effect snapshot capacity exceeded.");
                string digest;using(var hash=SHA256.Create())digest=Convert.ToBase64String(hash.ComputeHash(body));
                if(Digest!=digest||!Available){var geometry=Parse(body);Body=body;Geometry=geometry;Digest=digest;Revision++;Available=true;MasterIdolSeatRosterGuard.ObserveProjection(Geometry);if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogInfo("[IdolTest] projection revision="+Revision+" topology="+MasterIdolWorldIndex.TopologyRevision+" zones="+zones.Count+" circles="+zones.Sum(z=>z.Circles.Length)+" residents="+zones.Sum(z=>z.Residents.Count));}
                LastVerified=Time.unscaledTime;
                var peers=Session.GetPeers();var active=new HashSet<ZRpc>();foreach(var peer in peers)
                {
                    var rpc=peer?.m_rpc;if(rpc==null)continue;active.Add(rpc);
                    bool full=!Sent.TryGetValue(rpc,out long revision)||revision!=Revision;
                    var update=new ZPackage();update.Write(World);update.Write(Revision);update.Write(full);if(full)update.Write(Body);
                    rpc.Invoke("VM_Idol_Zones_v2",update);Sent[rpc]=Revision;
                }
                foreach(var rpc in Sent.Keys.Where(rpc=>!active.Contains(rpc)).ToArray())Sent.Remove(rpc);
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
            var packet=new ZPackage();packet.Write(World);Session.GetServerRPC()?.Invoke("VM_Idol_Zones_Ask_v2",packet);
        }
        private static void Ask(ZRpc rpc,ZPackage packet)
        {if(!Current()||!Session.IsServer()||rpc==null||Session.GetPeer(rpc)==null||packet==null||packet.Size()!=8)return;try{if(packet.ReadLong()==World)Sent.Remove(rpc);}catch{}}
        private static void Receive(ZRpc rpc,ZPackage packet)
        {
            if(!Current()||Session.IsServer()||rpc!=Session.GetServerRPC()||packet==null||packet.Size()>MaxBytes+32)return;
            try
            {
                if(packet.ReadLong()!=World)return;long revision=packet.ReadLong();bool full=packet.ReadBool();if(revision<=0||revision<Revision)return;
                if(full){var body=packet.ReadByteArray();if(body.Length>MaxBytes)throw new FormatException();var geometry=Parse(body);if(packet.GetPos()!=packet.Size())throw new FormatException();Geometry=geometry;Revision=revision;Available=true;MasterIdolSeatRosterGuard.ObserveProjection(Geometry);}
                else if(packet.GetPos()!=packet.Size()||!Available||revision!=Revision){Request();return;}
                LastVerified=Time.unscaledTime;
            }
            catch{Available=false;Request();}
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static Vector3 Point(ZPackage packet)
        {var point=packet.ReadVector3();if(!Finite(point.x)||!Finite(point.y)||!Finite(point.z)||Math.Abs(point.x)>1000000||Math.Abs(point.z)>1000000)throw new FormatException();return point;}
        private static MasterIdolEffectCircle Circle(ZPackage packet)
        {var point=Point(packet);float radius=packet.ReadSingle();if(!Finite(radius)||radius<=0||radius>1000)throw new FormatException();return new MasterIdolEffectCircle(point,radius);}
        private static MasterIdolEffectGeometry Parse(byte[] body)
        {
            var result=new MasterIdolEffectGeometry();var packet=new ZPackage(body);int count=packet.ReadInt(),total=0;
            if(count<0||count>128)throw new FormatException();var assigned=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<count;i++)
            {
                string id=packet.ReadString(),type=packet.ReadString();var parts=id.Split('/');if(parts.Length!=2||!MasterIdolNetworkQuota.ValidUid(parts[0])||!Guid.TryParseExact(parts[1],"N",out _)||(type!="Meadows"&&type!="BlackForest"))throw new FormatException();
                var home=Point(packet);int members=packet.ReadInt();total+=members;if(members<=0||members>4096||total>8192)throw new FormatException();
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
            count=packet.ReadInt();if(count<0||count>256)throw new FormatException();int permittedTotal=0;
            for(int i=0;i<count;i++)
            {
                var circle=Circle(packet);long creator=packet.ReadLong();int members=packet.ReadInt();permittedTotal+=members;if(members<0||members>4096||permittedTotal>16384)throw new FormatException();
                var ids=new long[members];for(int n=0;n<members;n++)ids[n]=packet.ReadLong();result.Wards.Add(new MasterIdolEffectWard{Circle=circle,Creator=creator,Permitted=ids});
            }
            if(packet.GetPos()!=packet.Size())throw new FormatException();return result;
        }
    }
}








