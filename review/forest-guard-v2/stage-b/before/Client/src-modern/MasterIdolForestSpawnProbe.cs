using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Client supplies physical placement only; server retains prefab/role/slot creation.
    internal static class MasterIdolForestSpawnProbe
    {
        private const string QueryRpc="VM_BF_SpawnPositionQuery_v1",ProofRpc="VM_BF_SpawnPositionProof_v1";
        internal sealed class Lease
        {
            internal string Nonce,Home,Fingerprint,BeforeUid,BeforeNonce;internal int Family;
            internal long World,PlayerId,BeforeBorn,BeforeDeath,BeforeRespawn;internal ulong Generation;internal ushort PlayerRevision;
            internal ZNet Net;internal ZDOMan Manager;internal ZNetScene Scene;internal ZNetPeer Peer;internal ZDOID CharacterId;
            internal MasterIdolEffectZone Zone;internal ForestBirthClaim Claim;internal MasterIdolForestGuardRecord Before;internal float Issued,Expires;internal bool Answered;internal Vector3 Point,Endpoint;
        }
        private static readonly Dictionary<string,Lease> Pending=new Dictionary<string,Lease>(StringComparer.Ordinal);
        private static readonly Dictionary<int,Lease> InFlight=new Dictionary<int,Lease>();
        private static ZNet Session;private static ZDOMan Manager;private static ZNetScene Scene;private static ulong Generation;private static long World;
        private static ZNet ClientSession;private static ulong ClientGeneration;private static float NextClient;private static long LastPeer;
        private static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)&&Mathf.Abs(p.x)<=100000&&Mathf.Abs(p.y)<=100000&&Mathf.Abs(p.z)<=100000;
        private static void Clear(){foreach(var lease in Pending.Values)MasterIdolForestGuardState.ReleaseBirth(lease.Claim);foreach(var lease in InFlight.Values)MasterIdolForestGuardState.ReleaseBirth(lease.Claim);Pending.Clear();InFlight.Clear();LastPeer=0;}
        private static void Drop(string nonce){if(Pending.TryGetValue(nonce,out var lease)){Pending.Remove(nonce);MasterIdolForestGuardState.ReleaseBirth(lease.Claim);}}
        private static bool Current()
        {
            var net=ZNet.instance;
            if(net?.IsServer()!=true||net.GetWorld()==null||ZDOMan.instance==null||ZNetScene.instance==null||!MasterIdolLoadIdentity.LoadReady||!WorkbenchEnchantmentRuntime.Ready||!MasterIdolForestGuardState.Ready){Clear();return false;}
            if(Session!=net||Manager!=ZDOMan.instance||Scene!=ZNetScene.instance||Generation!=MasterIdolLoadIdentity.Generation||World!=net.GetWorldUID())
            {Clear();Session=net;Manager=ZDOMan.instance;Scene=ZNetScene.instance;Generation=MasterIdolLoadIdentity.Generation;World=net.GetWorldUID();}
            return true;
        }
        private static string Fingerprint(MasterIdolEffectZone zone)
        {
            if(!MasterIdolRecruitment.TrySpawnCircle(zone,out var circle,out var reason)){MasterIdolForestSpawnDiagnostics.Trace("proof",reason);return null;}
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
                {writer.Write(1);writer.Write(circle.Point.x);writer.Write(circle.Point.y);writer.Write(circle.Point.z);writer.Write(circle.Radius);}
                using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }
        private static bool PlayerData(ZNetPeer peer,out ZDO data,out long playerId)
        {
            playerId=0;data=OwnerSkillAuthority.ResolveCharacterData(peer);if(data==null){MasterIdolForestSpawnDiagnostics.Trace("peer","character-binding-not-ready");return false;}
            if(!NetworkSync.HasPeerSettings(peer)){MasterIdolForestSpawnDiagnostics.Trace("peer","settings-unconfirmed");return false;}
            if(peer.m_uid==0||Session.GetPeer(peer.m_rpc)!=peer){MasterIdolForestSpawnDiagnostics.Trace("peer","identity-not-ready");return false;}
            playerId=data.GetLong(ZDOVars.s_playerID,0);
            bool valid=playerId!=0&&!data.GetBool(ZDOVars.s_debugFly,false)&&
                float.IsFinite(data.GetFloat(ZDOVars.s_health,1))&&data.GetFloat(ZDOVars.s_health,1)>0&&Finite(data.GetPosition());
            if(!valid)MasterIdolForestSpawnDiagnostics.Trace("peer","character-proof-failed");return valid;
        }
        private static bool Authorized(Lease lease,out ZDO player)
        {
            player=null;return lease!=null&&lease.Net==Session&&lease.Manager==Manager&&lease.Scene==Scene&&lease.World==World&&lease.Generation==Generation&&
                Time.unscaledTime>=lease.Issued&&Time.unscaledTime<=lease.Expires&&PlayerData(lease.Peer,out player,out var playerId)&&
                lease.Peer.m_characterID==lease.CharacterId&&playerId==lease.PlayerId&&player.OwnerRevision==lease.PlayerRevision;
        }
        private static bool SameSlot(Lease lease)
        {
            var slots=MasterIdolForestGuardState.Slots;if(slots==null)return false;slots.TryGetValue(MasterIdolForestRules.Families[lease.Family],out var current);
            return ReferenceEquals(current,lease.Before)&&(current==null||current.Uid==lease.BeforeUid&&current.Nonce==lease.BeforeNonce&&current.Born==lease.BeforeBorn&&
                current.Death==lease.BeforeDeath&&current.Respawn==lease.BeforeRespawn&&current.Valid()&&current.Death!=0)&&MasterIdolForestGuardState.BirthValid(lease.Claim,lease.Zone,lease.Before);
        }
        private static void Purge()
        {
            var remove=new List<string>(4);foreach(var entry in Pending)if(!Authorized(entry.Value,out _)||!SameSlot(entry.Value))remove.Add(entry.Key);
            foreach(string nonce in remove)Drop(nonce);
        }
        internal static bool Request(MasterIdolEffectZone zone,int family,MasterIdolForestGuardRecord before,ForestBirthClaim claim)
        {
            if(!Current()||zone?.Type!="BlackForest"||family<0||family>3||!MasterIdolForestRules.Home(zone.Id)||claim?.Family!=family||!MasterIdolForestGuardState.BirthValid(claim,zone,before))return false;
            if(!MasterIdolRecruitment.TrySpawnCircle(zone,out var circle,out var reason)){MasterIdolForestSpawnDiagnostics.Trace("request",reason);return false;}
            Purge();if(Pending.Count+InFlight.Count>=4||InFlight.ContainsKey(family))return false;foreach(var item in Pending.Values)if(item.Family==family)return false;
            ZNetPeer selected=null;ZDO character=null;long selectedPlayerId=0;
            foreach(var peer in Session.GetPeers())
            {
                if(!PlayerData(peer,out var data,out var playerId)||!circle.Contains(data.GetPosition(),32))continue;
                if(selected==null||(peer.m_uid>LastPeer)==(selected.m_uid>LastPeer)&&peer.m_uid<selected.m_uid||peer.m_uid>LastPeer&&selected.m_uid<=LastPeer){selected=peer;character=data;selectedPlayerId=playerId;}
            }
            if(selected==null){MasterIdolForestSpawnDiagnostics.Trace("request","no-confirmed-near-peer");return false;}string fingerprint=Fingerprint(zone);if(fingerprint==null)return false;
            var lease=new Lease{Zone=zone,Claim=claim,Nonce=Guid.NewGuid().ToString("N"),Home=zone.Id,Fingerprint=fingerprint,Family=family,World=World,Generation=Generation,Net=Session,Manager=Manager,Scene=Scene,
                Peer=selected,CharacterId=selected.m_characterID,PlayerId=selectedPlayerId,PlayerRevision=character.OwnerRevision,Before=before,BeforeUid=before?.Uid,BeforeNonce=before?.Nonce,
                BeforeBorn=before?.Born??0,BeforeDeath=before?.Death??0,BeforeRespawn=before?.Respawn??0,Issued=Time.unscaledTime,Expires=Time.unscaledTime+6};
            if(!SameSlot(lease))return false;
            var packet=new ZPackage();packet.Write(lease.Nonce);packet.Write(World);packet.Write(lease.Home);packet.Write(family);packet.Write(fingerprint);
            if(packet.Size()>256)return false;Pending.Add(lease.Nonce,lease);LastPeer=selected.m_uid;
            try{selected.m_rpc.Invoke(QueryRpc,packet);MasterIdolForestSpawnDiagnostics.Trace("request","query-sent","family="+family);return true;}catch{Drop(lease.Nonce);MasterIdolForestSpawnDiagnostics.Trace("request","send-failed");return false;}
        }
        internal static bool StillValid(Lease lease,MasterIdolEffectZone zone)
        {
            return Current()&&zone?.Type=="BlackForest"&&zone.Id==lease.Home&&Fingerprint(zone)==lease.Fingerprint&&Authorized(lease,out var player)&&SameSlot(lease)&&
                Finite(lease.Point)&&Finite(lease.Endpoint)&&(player.GetPosition()-lease.Point).sqrMagnitude<=MasterIdolResidentBirthPresentation.SpawnDistanceSquared&&
                MasterIdolRecruitment.CheapPosition(zone,lease.Family,lease.Point,lease.Endpoint);
        }
        internal static bool TransferReady(Lease lease,MasterIdolEffectZone zone,MasterIdolForestGuardRecord record,ZDO data)
        {
            if(!Current()||lease==null||zone?.Type!="BlackForest"||zone.Id!=lease.Home||Fingerprint(zone)!=lease.Fingerprint||
                !InFlight.TryGetValue(lease.Family,out var reserved)||!ReferenceEquals(reserved,lease)||!Authorized(lease,out var player))return false;
            var slots=MasterIdolForestGuardState.Slots;
            return slots!=null&&slots.TryGetValue(MasterIdolForestRules.Families[lease.Family],out var current)&&ReferenceEquals(current,record)&&record.Death==0&&record.Valid()&&
                data?.Persistent==true&&ReferenceEquals(Manager.GetZDO(data.m_uid),data)&&data.GetOwner()==ZNet.GetUID()&&MasterIdolForestGuardState.Matches(record,data)&&
                data.GetFloat(ZDOVars.s_health,1)>0&&float.IsFinite(data.GetFloat(ZDOVars.s_health,1))&&
                (player.GetPosition()-lease.Point).sqrMagnitude<=MasterIdolResidentBirthPresentation.SpawnDistanceSquared&&MasterIdolRecruitment.CheapPosition(zone,lease.Family,lease.Point,lease.Endpoint);
        }
        internal static bool TryTake(MasterIdolEffectZone zone,out Lease lease)
        {
            lease=null;if(!Current())return false;Purge();var discard=new List<string>(4);
            foreach(var entry in Pending)
            {
                var item=entry.Value;if(zone?.Id!=item.Home||Fingerprint(zone)!=item.Fingerprint){discard.Add(entry.Key);continue;}
                if(!item.Answered)continue;if(!StillValid(item,zone)){discard.Add(entry.Key);continue;}
                lease=item;break;
            }
            foreach(string nonce in discard)Drop(nonce);
            if(lease==null)return false;
            // Both operations happen before native factory callbacks; no consumed nonce is reopened.
            Pending.Remove(lease.Nonce);InFlight.Add(lease.Family,lease);return true;
        }
        internal static void Release(Lease lease){if(lease!=null&&InFlight.TryGetValue(lease.Family,out var current)&&ReferenceEquals(current,lease))InFlight.Remove(lease.Family);if(lease!=null)MasterIdolForestGuardState.ReleaseBirth(lease.Claim);}
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>(QueryRpc,Query);peer?.m_rpc?.Register<ZPackage>(ProofRpc,Proof);}
        private static Vector3 ReadPoint(BinaryReader reader)=>new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
        private static void Proof(ZRpc rpc,ZPackage packet)
        {
            try
            {
                if(!Current()||packet==null||packet.Size()>96)return;string nonce;bool success;Vector3 point=default,endpoint=default;
                using(var reader=new BinaryReader(new MemoryStream(packet.GetArray()),Encoding.UTF8))
                {nonce=MasterIdolForestRules.ReadText(reader,32);byte flag=reader.ReadByte();if(flag>1)return;success=flag==1;if(success){point=ReadPoint(reader);endpoint=ReadPoint(reader);}if(reader.BaseStream.Position!=reader.BaseStream.Length)return;}
                if(!MasterIdolForestRules.Nonce(nonce)||!Pending.TryGetValue(nonce,out var lease)||lease.Answered||lease.Peer.m_rpc!=rpc||!Authorized(lease,out _))return;
                if(!success){Drop(nonce);MasterIdolForestSpawnDiagnostics.Trace("proof","client-no-position");return;}if(!Finite(point)||!Finite(endpoint)){Drop(nonce);return;}
                lease.Point=point;lease.Endpoint=endpoint;lease.Answered=true;
                MasterIdolForestSpawnDiagnostics.Trace("proof","answered","family="+lease.Family);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("guard.position-proof-rejected");}
        }
        private static void Query(ZRpc rpc,ZPackage packet)
        {
            try
            {
                var net=ZNet.instance;if(net==null||net.IsServer()||net.GetWorld()==null)return;
                if(net.GetServerPeer()?.IsReady()!=true||net.GetServerPeer().m_rpc!=rpc){MasterIdolForestSpawnDiagnostics.Trace("query","server-peer-not-ready");return;}
                if(!NetworkSync.HasCurrentServerSettings(rpc)){MasterIdolForestSpawnDiagnostics.Trace("query","settings-unconfirmed");return;}
                if(packet==null||packet.Size()>256){MasterIdolForestSpawnDiagnostics.Trace("query","invalid-envelope");return;}
                string nonce,home,fingerprint;long world;int family;
                using(var reader=new BinaryReader(new MemoryStream(packet.GetArray()),Encoding.UTF8))
                {nonce=MasterIdolForestRules.ReadText(reader,32);world=reader.ReadInt64();home=MasterIdolForestRules.ReadText(reader,128);family=reader.ReadInt32();fingerprint=MasterIdolForestRules.ReadText(reader,44);if(reader.BaseStream.Position!=reader.BaseStream.Length)return;}
                if(!MasterIdolForestRules.Nonce(nonce)||!MasterIdolForestRules.Home(home)||family<0||family>3||fingerprint.Length!=44)return;
                if(ClientSession!=net||ClientGeneration!=MasterIdolLoadIdentity.Generation){ClientSession=net;ClientGeneration=MasterIdolLoadIdentity.Generation;NextClient=0;}
                var player=Player.m_localPlayer;var zone=MasterIdolEffectZones.Find(home);Vector3 point=default,endpoint=default;
                bool circleReady=MasterIdolRecruitment.TrySpawnCircle(zone,out var circle,out var circleReason);
                var manager=ZDOMan.instance;var scene=ZNetScene.instance;ulong generation=MasterIdolLoadIdentity.Generation;var view=player?.m_nview;var data=view?.GetZDO();ushort revision=data?.OwnerRevision??0;
                bool eligible=world==net.GetWorldUID()&&MasterIdolLoadIdentity.LoadReady&&MasterIdolEffectZones.Ready&&zone?.Type=="BlackForest"&&circleReady&&Fingerprint(zone)==fingerprint&&
                    player!=null&&!player.IsDead()&&!player.InGhostMode()&&!player.IsDebugFlying()&&player.m_nview?.IsValid()==true&&player.m_nview.IsOwner()&&circle.Contains(player.transform.position,32)&&Time.unscaledTime>=NextClient;
                if(!eligible)MasterIdolForestSpawnDiagnostics.Trace("query",circleReady?"ineligible-world-settings-player-or-fingerprint":circleReason);
                if(eligible)NextClient=Time.unscaledTime+1;
                bool success=eligible&&MasterIdolRecruitment.TryPosition(zone,family,player.transform.position,out point,out endpoint);
                if(eligible&&!success)MasterIdolForestSpawnDiagnostics.Trace("query","physical-search-failed","family="+family);
                if(success)
                {
                    var currentZone=MasterIdolEffectZones.Find(home);
                    success=ZNet.instance==net&&net.GetWorld()!=null&&net.GetWorldUID()==world&&ZDOMan.instance==manager&&ZNetScene.instance==scene&&MasterIdolLoadIdentity.Generation==generation&&
                        MasterIdolLoadIdentity.LoadReady&&net.GetServerPeer()?.IsReady()==true&&net.GetServerPeer().m_rpc==rpc&&NetworkSync.HasCurrentServerSettings(rpc)&&Player.m_localPlayer==player&&player.m_nview==view&&
                        view?.IsValid()==true&&view.IsOwner()&&ReferenceEquals(view.GetZDO(),data)&&data.OwnerRevision==revision&&ReferenceEquals(manager?.GetZDO(data.m_uid),data)&&
                        !player.IsDead()&&!player.InGhostMode()&&!player.IsDebugFlying()&&currentZone?.Type=="BlackForest"&&Fingerprint(currentZone)==fingerprint&&
                        (player.transform.position-point).sqrMagnitude<=MasterIdolResidentBirthPresentation.SpawnDistanceSquared;
                }
                var reply=new ZPackage();reply.Write(nonce);reply.Write(success);if(success){reply.Write(point);reply.Write(endpoint);}rpc.Invoke(ProofRpc,reply);
                MasterIdolForestSpawnDiagnostics.Trace("query",success?"positive-proof-sent":"negative-proof-sent");
            }
            catch(Exception){MasterIdolResidentMetrics.Count("guard.position-query-rejected");}
        }
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class MasterIdolForestSpawnPeerPatch
    {private static void Postfix(ZNetPeer peer)=>MasterIdolForestSpawnProbe.Register(peer);}
}
