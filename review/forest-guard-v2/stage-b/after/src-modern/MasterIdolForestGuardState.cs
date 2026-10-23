using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static partial class MasterIdolForestGuardState
    {
        internal const string Marker="vm.bf.guard.v1",WorldKey="vm.bf.guard.world.v1",BornKey="vm.bf.guard.born.v1",NonceKey="vm.bf.guard.nonce.v1";
        private const string DeathRpc="VM_BF_GuardDeath_v1";
        private static readonly string[] NameKeys={"$vm_bf_guard_greyling","$vm_bf_guard_greydwarf","$vm_bf_guard_elite","$vm_bf_guard_shaman"};
        private static readonly string[] English={"Twiggy Fidget","Mossbeard","Oakbreaker","Resinomancer"};
        private static readonly string[] Ukrainian={"Прутик Непосида","Мохобород","Дуболом","Смоломант"};
        private sealed class Witness{internal ZDO Data;internal long Owner,Seen;internal ushort Revision;internal string Home;}
        private sealed class DeathProof{internal long Sender,World,Born,Death;internal ZDOID Uid;internal int Family,Revision;internal string Home,Nonce;internal float Health;}
        private static readonly Dictionary<string,Witness> Witnesses=new Dictionary<string,Witness>(StringComparer.Ordinal);
        private static readonly List<DeathProof> Pending=new List<DeathProof>(4);
        private static ZNet Session;private static ZDOMan Manager;private static ulong Generation;private static long World;private static float Next,NextSave;
        private static MasterIdolRecruitmentStore Store;private static Dictionary<string,double> LegacyTimers;private static Dictionary<string,HashSet<string>> LegacyMembers;private static bool Dirty;
        internal static IDictionary<string,MasterIdolForestGuardRecord> Slots=>Ensure()?Store.Guards:null;
        internal static bool Ready=>Ensure()&&Store.Ready;
        internal static int Family(ZDO data)
        {if(data==null)return -1;for(int i=0;i<MasterIdolForestRules.Families.Length;i++)if(data.GetPrefab()==MasterIdolForestRules.Families[i].GetStableHashCode())return i;return -1;}
        internal static bool ModuleBorn(ZDO data)
        {
            var net=ZNet.instance;return data?.Persistent==true&&net?.GetWorld()!=null&&data.GetBool(Marker,false)&&
                data.GetLong(WorldKey,0)==net.GetWorldUID()&&data.GetLong(BornKey,0)>0&&MasterIdolForestRules.Nonce(data.GetString(NonceKey,""))&&Family(data)>=0;
        }
        private static bool Ensure()
        {
            var net=ZNet.instance;if(net?.IsServer()!=true||net.GetWorld()==null||ZDOMan.instance==null||!MasterIdolLoadIdentity.LoadReady||!WorkbenchEnchantmentRuntime.Ready)return false;
            if(Session!=net||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||World!=net.GetWorldUID())
            {
                Close();Session=net;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;World=net.GetWorldUID();Witnesses.Clear();Pending.Clear();Next=NextSave=0;Dirty=false;
                Store=new MasterIdolRecruitmentStore(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"ValheimMastery","Idols",World+".residents.vmi"),World);
                LegacyTimers=Store.Load();LegacyMembers=Store.Members.ToDictionary(x=>x.Key,x=>new HashSet<string>(x.Value,StringComparer.Ordinal),StringComparer.Ordinal);
            }
            Store.Poll();if(!Store.Ready)return false;
            foreach(var record in Store.Guards.Values)if(MasterIdolForestGuardLoadIdentity.Rebind(record))Dirty=true;
            return true;
        }
        internal static ZDO Data(string uid)
        {return MasterIdolNetworkQuota.TryUid(uid,out long user,out uint id)?ZDOMan.instance?.GetZDO(new ZDOID(user,id)):null;}
        internal static bool Matches(MasterIdolForestGuardRecord record,ZDO data)
            =>record!=null&&ModuleBorn(data)&&data.m_uid.ToString()==record.Uid&&data.GetLong(BornKey,0)==record.Born&&data.GetString(NonceKey,"")==record.Nonce&&Family(data)==MasterIdolForestRules.Family(record.Family);
        internal static IEnumerable<MasterIdolResidentCandidate> Candidates(MasterIdolEffectZone zone)
        {
            if(!Ready||zone?.Type!="BlackForest")yield break;
            foreach(var record in Store.Guards.Values)
            {
                var data=MasterIdolForestGuardLoadIdentity.Resolve(record);if(record.Death!=0||!MasterIdolNativeTaming.SameHome(record.Home,zone.Id)||!Matches(record,data)||!float.IsFinite(data.GetFloat(ZDOVars.s_health,1))||data.GetFloat(ZDOVars.s_health,1)<=0||!MasterIdolForestGuardLifecycle.CanOperate(data))continue;
                yield return new MasterIdolResidentCandidate(record.Uid,record.Family,true,zone.Id);
            }
        }
        internal static void Changed()=>Dirty=true;
        internal static void Observe(MasterIdolEffectZone zone)
        {
            if(!Ready||zone?.Type!="BlackForest")return;
            foreach(var record in Store.Guards.Values)if(record.Home!=zone.Id&&MasterIdolNativeTaming.SameHome(record.Home,zone.Id)){record.Home=zone.Id;Dirty=true;}
            string token=MasterIdolNativeTaming.Token(zone.Id);
            if(token!=null&&!Store.Quotas.ContainsKey(token)&&Store.Quotas.Count<512&&NativeDay(out int day))
            {Store.Quotas.Add(token,new MasterIdolRevivalQuota{Token=token,Day=day});Dirty=true;}
        }
        internal static void Added(MasterIdolForestGuardRecord record)
        {Store.Guards[record.Family]=record;Dirty=true;Capture(record);}
        internal static void CancelBirth(MasterIdolForestGuardRecord record,MasterIdolForestGuardRecord previous)
        {
            if(record==null||Store==null||!Store.Guards.TryGetValue(record.Family,out var current)||!ReferenceEquals(current,record))return;
            if(previous==null)Store.Guards.Remove(record.Family);else Store.Guards[record.Family]=previous;
            Witnesses.Remove(record.Uid);Dirty=true;
        }
        private static void Capture(MasterIdolForestGuardRecord record)
        {
            var data=Data(record.Uid);if(record.Death!=0||!Matches(record,data))return;
            Witnesses[record.Uid]=new Witness{Data=data,Owner=data.GetOwner(),Revision=data.OwnerRevision,Home=data.GetString(MasterIdolNativeTaming.HomeKey,""),Seen=Session.GetTime().Ticks};
            string name=data.GetString(ZDOVars.s_tamedName,"");if(MasterIdolForestRules.Name(name)&&name!=record.Name){record.Name=name;Dirty=true;}
        }
        internal static void Tick()
        {
            if(!Ensure()||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;
            foreach(var record in Store.Guards.Values)Capture(record);
            for(int i=Pending.Count-1;i>=0;i--)
            {
                if(Confirm(Pending[i])||!MasterIdolForestRules.Window(Pending[i].Death,Session.GetTime().Ticks,Pending[i].Born))Pending.RemoveAt(i);
            }
            if(Dirty&&Time.unscaledTime>=NextSave&&Store.Queue(LegacyTimers,LegacyMembers,Store.Guards,Store.Quotas)){Dirty=false;NextSave=Time.unscaledTime+60;}
        }
        internal static string DefaultKey(string family){int i=MasterIdolForestRules.Family(family);return i<0?"":NameKeys[i];}
        internal static string Display(Character actor,string native)
        {
            var data=actor?.m_nview?.GetZDO();int index=Family(data);if(!ModuleBorn(data)||index<0||data.GetString(ZDOVars.s_tamedName,"")!=NameKeys[index])return native;
            return GoldUiLocalization.Text(English[index],Ukrainian[index]);
        }
        internal static void Stamp(Character actor,MasterIdolForestGuardRecord record)
        {
            var view=actor.m_nview;if(view?.IsValid()!=true||!view.IsOwner())throw new InvalidOperationException("Guard owner");
            var data=view.GetZDO();data.Set(Marker,true);data.Set(WorldKey,World);data.Set(BornKey,record.Born);data.Set(NonceKey,record.Nonce);data.Set(MasterIdolNativeTaming.HomeKey,record.Home);
            data.Set(ZDOVars.s_tamedName,string.IsNullOrEmpty(record.Name)?DefaultKey(record.Family):record.Name);record.Name=data.GetString(ZDOVars.s_tamedName,"");
            MasterIdolForestGuardLifecycleController.InitializeBirth(actor);
            try{MasterIdolResidentBirthPresentation.Attach(actor);MasterIdolForestGuardModelVisual.Refresh(actor);}
            catch(Exception){MasterIdolResidentMetrics.Count("guard.cosmetic-attach-failed");}
        }
        internal static void RehomeOwner(Character actor)
        {
            var view=actor?.m_nview;var data=view?.GetZDO();if(view?.IsValid()!=true||!view.IsOwner()||!ModuleBorn(data)||!MasterIdolEffectZones.Ready)return;
            var zone=MasterIdolEffectZones.ResidentHome(data.m_uid.ToString(),Utils.GetPrefabName(actor.gameObject));if(zone?.Type!="BlackForest"||data.GetString(MasterIdolNativeTaming.HomeKey,"")==zone.Id)return;
            data.Set(MasterIdolNativeTaming.HomeKey,zone.Id);var tag=actor.GetComponent<MasterIdolResidentPet>();
            if(tag!=null&&ReferenceEquals(tag.Actor,actor)&&ReferenceEquals(tag.Data,data)&&tag.Session==ZNet.instance&&tag.World==ZNet.instance.GetWorldUID()&&tag.Manager==ZDOMan.instance&&tag.Generation==MasterIdolLoadIdentity.Generation)tag.Home=zone.Id;
            MasterIdolWorldIndex.Observe(data);
        }
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>(DeathRpc,ReceiveDeath);MasterIdolForestGuests.Register(peer);}
        internal static void OnDeath(Character actor)
        {
            var view=actor?.m_nview;var data=view?.GetZDO();if(view?.IsValid()!=true||!view.IsOwner()||!ModuleBorn(data)||actor.GetHealth()>0||!MasterIdolLoadIdentity.LoadReady)return;
            var net=ZNet.instance;if(net?.GetWorld()==null)return;
            var packet=new ZPackage();packet.Write(net.GetWorldUID());MasterIdolResidentWork.WriteUid(packet,data.m_uid);packet.Write(data.GetString(MasterIdolNativeTaming.HomeKey,""));packet.Write(Family(data));
            packet.Write(data.GetString(NonceKey,""));packet.Write(data.GetLong(BornKey,0));packet.Write((int)data.OwnerRevision);packet.Write(net.GetTime().Ticks);packet.Write(actor.GetHealth());
            if(net.IsServer())Receive(ZNet.GetUID(),packet);else if(net.GetServerPeer()?.IsReady()==true)net.GetServerPeer().m_rpc.Invoke(DeathRpc,packet);
        }
        private static void ReceiveDeath(ZRpc rpc,ZPackage packet)
        {var peer=ZNet.instance?.GetPeer(rpc);if(peer?.IsReady()==true)Receive(peer.m_uid,packet);}
        private static void Receive(long sender,ZPackage packet)
        {
            try
            {
                if(!Ready||packet==null||packet.Size()>256)return;
                DeathProof proof;using(var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(packet.GetArray()),System.Text.Encoding.UTF8))
                {
                    proof=new DeathProof{Sender=sender,World=reader.ReadInt64(),Uid=new ZDOID(reader.ReadInt64(),reader.ReadUInt32()),Home=MasterIdolForestRules.ReadText(reader,128),Family=reader.ReadInt32(),Nonce=MasterIdolForestRules.ReadText(reader,32),Born=reader.ReadInt64(),Revision=reader.ReadInt32(),Death=reader.ReadInt64(),Health=reader.ReadSingle()};
                    if(reader.BaseStream.Position!=reader.BaseStream.Length)return;
                }
                if(proof.World!=World||proof.Family<0||proof.Family>3||proof.Revision<0||proof.Revision>ushort.MaxValue||!MasterIdolForestRules.Home(proof.Home)||!MasterIdolForestRules.Nonce(proof.Nonce)||
                    !MasterIdolForestRules.Window(proof.Death,Session.GetTime().Ticks,proof.Born)||!float.IsFinite(proof.Health)||proof.Health>0)return;
                if(!Authorized(proof))return;if(Confirm(proof))return;if(Pending.Count<4&&!Pending.Any(x=>x.Uid==proof.Uid))Pending.Add(proof);
            }
            catch(Exception){MasterIdolResidentMetrics.Count("guard.death-rejected");}
        }
        private static bool Authorized(DeathProof proof)
        {
            if(!Store.Guards.TryGetValue(MasterIdolForestRules.Families[proof.Family],out var record)||record.Death!=0||record.Uid!=proof.Uid.ToString()||record.Nonce!=proof.Nonce||record.Born!=proof.Born)return false;
            var data=Manager.GetZDO(proof.Uid);if(data!=null)Capture(record);
            return Witnesses.TryGetValue(record.Uid,out var witness)&&witness.Owner==proof.Sender&&witness.Revision==proof.Revision&&MasterIdolNativeTaming.SameHome(witness.Home,proof.Home)&&Session.GetTime().Ticks-witness.Seen<=6*TimeSpan.TicksPerSecond;
        }
        private static bool Confirm(DeathProof proof)
        {
            string family=MasterIdolForestRules.Families[proof.Family];if(!Store.Guards.TryGetValue(family,out var record)||record.Death!=0||record.Uid!=proof.Uid.ToString()||record.Nonce!=proof.Nonce||record.Born!=proof.Born)return false;
            var data=Manager.GetZDO(proof.Uid);if(data!=null)Capture(record);
            if(!Witnesses.TryGetValue(record.Uid,out var witness)||witness.Owner!=proof.Sender||witness.Revision!=proof.Revision||!MasterIdolNativeTaming.SameHome(witness.Home,proof.Home)||Session.GetTime().Ticks-witness.Seen>6*TimeSpan.TicksPerSecond)return false;
            bool zero=Matches(record,data)&&ReferenceEquals(witness.Data,data)&&data.GetFloat(ZDOVars.s_health,1)<=0;
            bool tombstone=data==null&&Manager.m_deadZDOs.ContainsKey(proof.Uid);
            if(!MasterIdolForestRules.Death(proof.Death,Session.GetTime().Ticks,record.Born,proof.Health,zero,tombstone,true))return false;
            if(!MasterIdolForestRules.Deadline(proof.Death,EnvMan.instance?.m_dayLengthSec??0,out long deadline))return false;
            var copy=record.Copy();copy.Death=proof.Death;copy.Respawn=deadline;if(!copy.Valid())return false;
            // Cosmetic name is only freshly captured native state; no rename field/authority in death RPC.
            record.Death=proof.Death;record.Respawn=deadline;Dirty=true;return true;
        }
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class MasterIdolForestPeerPatch{private static void Postfix(ZNetPeer peer)=>MasterIdolForestGuardState.Register(peer);}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolForestTickPatch{private static void Postfix(){MasterIdolForestGuardState.Tick();MasterIdolForestGuests.Tick();}}
    [HarmonyPatch(typeof(Character),"OnDeath")]
    internal static class MasterIdolForestDeathPatch{private static void Prefix(Character __instance)=>MasterIdolForestGuardState.OnDeath(__instance);}
    [HarmonyPatch(typeof(Character),nameof(Character.GetHoverName))]
    internal static class MasterIdolForestNamePatch{private static void Postfix(Character __instance,ref string __result)=>__result=MasterIdolForestGuardState.Display(__instance,__result);}
}
