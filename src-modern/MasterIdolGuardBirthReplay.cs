using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Explicit operator-armed, at-most-once BOOT request. Absent file leaves recruitment unchanged.
    internal static class MasterIdolGuardBirthReplay
    {
        private const int Magic=0x564D5231,Schema=1,MaximumBytes=8192;
        private const string FileName="guard-birth-replay.v1.request";
        private sealed class Request
        {
            internal string Nonce,Candidate,Hash,Path;internal long World,Operator;
            internal readonly MasterIdolForestGuardRecord[] Records=new MasterIdolForestGuardRecord[4];
            internal readonly ZDOID[] Uids=new ZDOID[4];
        }
        private static bool Checked,Finished,Consumed,Faulted,Bound;
        private static Request Pending;private static ZNet Session;private static ZDOMan Manager;private static ZNetScene Scene;
        private static ulong Generation;private static float Next;private static string ReplayHome;private static MasterIdolEffectCircle ReplayCircle;
        private static readonly bool[] Queued=new bool[4];
        private static readonly MasterIdolForestGuardRecord[] BirthReady=new MasterIdolForestGuardRecord[4];
        // Proposed Root provider: current accepted replay intent only, never plain file presence.
        internal static ReplayBirthAdmission BirthAdmission(MasterIdolEffectZone zone,int family,MasterIdolForestGuardRecord before,out string replayNonce)
        {
            replayNonce=null;
            if(Pending==null||!Bound||!Current()||zone?.Type!="BlackForest"||family<0||family>3||ReplayHome==null||!MasterIdolNativeTaming.SameHome(zone.Id,ReplayHome))return ReplayBirthAdmission.None;
            var slots=MasterIdolForestGuardState.Slots;
            if(slots!=null&&slots.TryGetValue(MasterIdolForestRules.Families[family],out var current)&&ReferenceEquals(current,before)&&ReferenceEquals(BirthReady[family],before)&&before!=null&&Consumed&&!Faulted){replayNonce=Pending.Nonce;return ReplayBirthAdmission.Ready;}
            return !Finished||Faulted?ReplayBirthAdmission.Hold:ReplayBirthAdmission.None;
        }
        private static readonly ZDOID[] TargetUids=new ZDOID[4];private static readonly ZDO[] TargetObjects=new ZDO[4];private static bool TargetsPinned;private static string TargetReason;
        private static readonly HashSet<string> Messages=new HashSet<string>(StringComparer.Ordinal);
        private static string Hash(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        private static bool Hex(string value)
        {if(value==null||value.Length!=64)return false;foreach(char c in value)if(!(c>='0'&&c<='9')&&!(c>='A'&&c<='F'))return false;return true;}
        private static void Note(string reason)
        {if(Messages.Count<16&&Messages.Add(reason))MasteryPlugin.Log?.LogInfo("[GuardBirthReplay] "+reason+" request="+(Pending?.Nonce??"none"));}
        private static bool Reject(string reason)
        {
            Note(reason);
            if(Consumed){Faulted=true;return false;} // A consumed partial request needs protected backup/manual rebind.
            Finished=true;Pending=null;return true;
        }
        private static Request Read(string path)
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length<32||stream.Length>MaximumBytes)throw new InvalidDataException("Replay size");
                string hash=Hash(stream);stream.Position=0;var request=new Request{Path=path,Hash=hash};
                using(var reader=new BinaryReader(stream,Encoding.UTF8,true))
                {
                    if(reader.ReadInt32()!=Magic||reader.ReadInt32()!=Schema)throw new InvalidDataException("Replay version");
                    request.Nonce=MasterIdolForestRules.ReadText(reader,32);request.Candidate=MasterIdolForestRules.ReadText(reader,64);
                    request.World=reader.ReadInt64();request.Operator=reader.ReadInt64();
                    if(!MasterIdolForestRules.Nonce(request.Nonce)||!Hex(request.Candidate)||request.World==0||request.Operator==0||reader.ReadInt32()!=4)throw new InvalidDataException("Replay header");
                    var uids=new HashSet<string>(StringComparer.Ordinal);var nonces=new HashSet<string>(StringComparer.Ordinal);
                    for(int i=0;i<4;i++)
                    {
                        var record=new MasterIdolForestGuardRecord{Family=MasterIdolForestRules.ReadText(reader,32),Home=MasterIdolForestRules.ReadText(reader,256),
                            Uid=MasterIdolForestRules.ReadText(reader,64),Nonce=MasterIdolForestRules.ReadText(reader,32),Name=MasterIdolForestRules.ReadText(reader,512),
                            Born=reader.ReadInt64(),Death=reader.ReadInt64(),Respawn=reader.ReadInt64()};
                        if(!record.Valid()||record.Family!=MasterIdolForestRules.Families[i]||i>0&&record.Home!=request.Records[0].Home||!uids.Add(record.Uid)||!nonces.Add(record.Nonce)||
                            !MasterIdolNetworkQuota.TryUid(record.Uid,out long user,out uint id))throw new InvalidDataException("Replay record");
                        request.Records[i]=record;request.Uids[i]=new ZDOID(user,id);
                    }
                    if(stream.Position!=stream.Length)throw new InvalidDataException("Replay tail");
                }
                return request;
            }
        }
        private static bool Same(MasterIdolForestGuardRecord a,MasterIdolForestGuardRecord b)
            =>a!=null&&b!=null&&a.Valid()&&a.Family==b.Family&&MasterIdolNativeTaming.SameHome(a.Home,b.Home)&&(a.Uid==b.Uid||MasterIdolForestGuardLoadIdentity.SameReboundRecord(a,b))&&a.Nonce==b.Nonce&&a.Name==b.Name&&a.Born==b.Born&&a.Death==b.Death&&a.Respawn==b.Respawn;
        private static bool Current()=>Session==ZNet.instance&&Manager==ZDOMan.instance&&Scene==ZNetScene.instance&&Generation==MasterIdolLoadIdentity.Generation&&
            Session?.IsServer()==true&&Session.GetWorld()!=null&&Session.GetWorldUID()==Pending.World;
        private static bool SameSlots(IDictionary<string,MasterIdolForestGuardRecord> slots)
        {
            if(slots==null||slots.Count!=4)return false;
            for(int i=0;i<4;i++)if(!slots.TryGetValue(Pending.Records[i].Family,out var record)||!Same(record,Pending.Records[i]))return false;
            return true;
        }
        private static bool Finite(Vector3 position)=>float.IsFinite(position.x)&&float.IsFinite(position.y)&&float.IsFinite(position.z);
        private static bool PinnedHome(List<MasterIdolEffectZone> zones)
        {
            MasterIdolEffectZone home=null;int count=0;
            if(zones!=null)foreach(var zone in zones)if(zone?.Type=="BlackForest"){home=zone;count++;}
            return count==1&&home.Id==ReplayHome&&MasterIdolRecruitment.TrySpawnCircle(home,out var circle,out _)&&
                circle.Point.x==ReplayCircle.Point.x&&circle.Point.y==ReplayCircle.Point.y&&circle.Point.z==ReplayCircle.Point.z&&circle.Radius==ReplayCircle.Radius;
        }
        private static bool OperatorNear(MasterIdolEffectCircle circle)
        {
            int count=0;long found=0;
            foreach(var peer in Session.GetPeers())
            {
                var data=OwnerSkillAuthority.ResolveCharacterData(peer);
                if(data==null||!NetworkSync.HasPeerSettings(peer)||peer.m_uid==0||Session.GetPeer(peer.m_rpc)!=peer||
                    data.GetBool(ZDOVars.s_debugFly,false)||!float.IsFinite(data.GetFloat(ZDOVars.s_health,1))||data.GetFloat(ZDOVars.s_health,1)<=0||
                    !Finite(data.GetPosition())||!circle.Contains(data.GetPosition(),32))continue;
                long playerId=data.GetLong(ZDOVars.s_playerID,0);if(playerId==0)continue;
                count++;found=playerId;
            }
            return count==1&&found==Pending.Operator;
        }
        private static bool Target(int index,ZDO data,out GameObject instance)
        {
            instance=null;var expected=Pending.Records[index];TargetReason="uid-missing";
            if(data==null)return expected.Death!=0; // Explicitly pinned dead slot may already be absent/tombstoned.
            TargetReason="current-data";if(!ReferenceEquals(Manager.GetZDO(data.m_uid),data)||TargetsPinned&&!ReferenceEquals(TargetObjects[index],data))return false;
            TargetReason="birth-identity";if(!MasterIdolForestGuardLoadIdentity.Matches(expected,data))return false;
            TargetReason="home";if(!MasterIdolNativeTaming.SameHome(data.GetString(MasterIdolNativeTaming.HomeKey,""),expected.Home))return false;
            TargetReason="raw-name";if(data.GetString(ZDOVars.s_tamedName,"")!=expected.Name)return false;
            // Native full-health alive actors may omit s_health; this is a predicate, never a health write.
            TargetReason="health";float health=data.GetFloat(ZDOVars.s_health,expected.Death==0?1f:float.NaN);if(!float.IsFinite(health)||(expected.Death==0?health<=0:health>0))return false;
            instance=Scene.FindInstance(data)?.gameObject;
            if(instance==null)return true;
            TargetReason="native-view";var view=instance.GetComponent<ZNetView>();var actor=instance.GetComponent<Character>();
            return view?.IsValid()==true&&ReferenceEquals(view.GetZDO(),data)&&actor!=null&&!(actor is Player)&&Utils.GetPrefabName(instance)==expected.Family;
        }
        private static MasterIdolForestGuardRecord[] Eligible(long now)
        {
            var result=new MasterIdolForestGuardRecord[4];
            for(int i=0;i<4;i++)
            {
                var expected=Pending.Records[i];if(now<=expected.Born||now<=expected.Death||now>DateTime.MaxValue.Ticks||!MasterIdolNativeTaming.SameHome(ReplayHome,expected.Home))return null;
                var copy=expected.Copy();copy.Home=ReplayHome;copy.Death=expected.Death==0?now-1:expected.Death;copy.Respawn=now;
                if(!copy.Valid())return null;result[i]=copy;
            }
            return result;
        }
        private static void Consume()
        {
            string consumed=Pending.Path+".consumed."+Pending.Nonce;
            using(var stream=new FileStream(Pending.Path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {
                if(stream.Length>MaximumBytes||Hash(stream)!=Pending.Hash)throw new InvalidDataException("Replay request changed");
                File.Move(Pending.Path,consumed);Consumed=true;
                using(var readback=new FileStream(consumed,FileMode.Open,FileAccess.Read,FileShare.Read))
                    if(readback.Length>MaximumBytes||Hash(readback)!=Pending.Hash)throw new InvalidDataException("Replay consume readback");
            }
            Note("consumed-before-native-mutation");
        }
        internal static bool BeforeTick(List<MasterIdolEffectZone> zones,Func<bool> publishable)
        {
            var net=ZNet.instance;if(net?.IsServer()!=true||net.GetWorld()==null)return true;
            if(Finished)return true;
            try
            {
                if(!Checked)
                {
                    Checked=true;string path=Path.Combine(BepInEx.Paths.ConfigPath,"ValheimMastery","Idols",FileName);
                    if(!File.Exists(path)){Finished=true;return true;}
                    Pending=Read(path);
                    using(var assembly=new FileStream(typeof(MasterIdolGuardBirthReplay).Assembly.Location,FileMode.Open,FileAccess.Read,FileShare.Read))
                        if(Hash(assembly)!=Pending.Candidate)return Reject("rejected-candidate");
                    if(Pending.World!=net.GetWorldUID())return Reject("rejected-world");
                    Note("boot-request-read-once");
                }
                if(Pending==null)return true;
                if(Pending.World!=net.GetWorldUID())
                {Note("retired-world-changed");Finished=true;Pending=null;return true;}
                if(Faulted)return false;
                if(!MasterIdolLoadIdentity.LoadReady||!WorkbenchEnchantmentRuntime.Ready||!MasterIdolForestGuardState.Ready||ZDOMan.instance==null||ZNetScene.instance==null)return false;
                if(!Bound){Session=net;Manager=ZDOMan.instance;Scene=ZNetScene.instance;Generation=MasterIdolLoadIdentity.Generation;Bound=true;}
                if(!Current())return Reject("rejected-context");
                if(Time.unscaledTime<Next)return false;Next=Time.unscaledTime+.5f;
                var slots=MasterIdolForestGuardState.Slots;
                if(!Consumed&&slots!=null)foreach(var expected in Pending.Records)
                    if(slots.TryGetValue(expected.Family,out var current)&&expected.Uid!=current.Uid)MasterIdolForestGuardLoadIdentity.AdmitReplayAlias(expected,current);
                if(!SameSlots(slots))return Reject("rejected-record-drift");
                if(Consumed&&!PinnedHome(zones))return Reject("fault-pinned-active-home-changed");
                if(!Consumed)
                {
                    MasterIdolEffectZone home=null;int count=0;
                    if(zones!=null)foreach(var zone in zones)if(zone?.Type=="BlackForest"){home=zone;count++;}
                    if(count==0){Note("waiting-active-home-projection");return false;}
                    if(count!=1||!MasterIdolNativeTaming.SameHome(home.Id,Pending.Records[0].Home))return Reject("rejected-active-home");
                    if(!MasterIdolRecruitment.TrySpawnCircle(home,out var circle,out var circleReason))
                    {
                        if(circleReason=="home-circle-missing"){Note("waiting-active-home-circle");return false;}
                        return Reject("rejected-active-home");
                    }
                    if(publishable?.Invoke()!=true||!OperatorNear(circle)){Note("waiting-confirmed-operator-alone-near-home");return false;}
                    // Projection already verifies the current native station/witness. Persist its current UID, same durable token.
                    ReplayHome=home.Id;ReplayCircle=circle;if(ReplayHome!=Pending.Records[0].Home)Note("same-home-token-rebound");
                    if(Eligible(Session.GetTime().Ticks)==null)return Reject("rejected-time");
                    for(int i=0;i<4;i++)
                    {
                        var data=MasterIdolForestGuardLoadIdentity.Resolve(Pending.Records[i]);
                        // A present wrong old UID is a collision, never an absent-dead allowance or alias fallback.
                        if(data==null&&Manager.GetZDO(Pending.Uids[i])!=null){Note("target-"+i+"-old-uid-collision");return Reject("rejected-native-target-binding");}
                        TargetObjects[i]=data;TargetUids[i]=data?.m_uid??Pending.Uids[i];
                        if(!Target(i,data,out _)){Note("target-"+i+"-"+TargetReason);return Reject("rejected-native-target-binding");}
                    }
                    TargetsPinned=true;
                    if(!Current()||!SameSlots(slots)||!OperatorNear(circle)||!PinnedHome(zones))return Reject("rejected-preconsume-drift");
                    // Quiesce all independent inventories before consuming the same one-off intent.
                    // These are current native birth-proven objects, never historical request UIDs.
                    for(int i=0;i<4;i++)
                        if(!MasterIdolResidentCargo.BeforeRemoval(TargetObjects[i],"BirthReplay"))
                        {Note("waiting-cargo-quiescent");return false;}
                    Consume();
                }
                bool gone=true;
                for(int i=0;i<4;i++)
                {
                    var data=Manager.GetZDO(TargetUids[i]);if(data==null){Queued[i]=true;continue;}gone=false;
                    if(Queued[i]){if(!Target(i,data,out _))return Reject("fault-native-target-drift");continue;}
                    if(!Target(i,data,out var instance))return Reject("fault-native-target-drift");
                    data.SetOwner(ZDOMan.GetSessionID());
                    if(!data.IsOwner()||!Current()||!ReferenceEquals(Manager.GetZDO(TargetUids[i]),data))return Reject("fault-native-claim");
                    if(instance!=null)Scene.Destroy(instance);else Manager.DestroyZDO(data);
                    Queued[i]=true;Note("native-destroy-queued");
                }
                if(!gone){Note("waiting-old-uids-gone");return false;}
                var eligible=Eligible(Session.GetTime().Ticks);if(eligible==null||!Current()||!SameSlots(slots))return Reject("fault-final-record-drift");
                // Explicit manual replay only; ordinary death proofs and day respawn code are untouched.
                for(int i=0;i<4;i++){slots[eligible[i].Family]=eligible[i];BirthReady[i]=eligible[i];}
                MasterIdolForestGuardState.Changed();Finished=true;Note("four-slots-eligible-normal-front-birth");return true;
            }
            catch(Exception){return Reject(Consumed?"fault-consumed-request-manual-rebind":"rejected-request-no-native-mutation");}
        }
    }
    [HarmonyPatch(typeof(MasterIdolRecruitment),nameof(MasterIdolRecruitment.Tick))]
    internal static class MasterIdolGuardBirthReplayPatch
    {
        private static bool Prefix(List<MasterIdolEffectZone> zones,Func<bool> publishable)=>MasterIdolGuardBirthReplay.BeforeTick(zones,publishable);
    }
}
