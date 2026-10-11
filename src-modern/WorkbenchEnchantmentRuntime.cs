using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
namespace ValheimMastery
{
    internal static class WorkbenchEnchantmentRuntime
    {
        internal const string StationKey="vm.enchant.station.v1";
        static string DirectoryPath;static ZNet Session;static ZDOMan Manager;static long World;static ulong Generation;
        internal static WorkbenchEnchantmentJournal Journal {get;private set;}
        internal static WorkbenchEnchantmentService Service {get;private set;}
        static readonly Dictionary<string,ZDO> NativeStations=new Dictionary<string,ZDO>(StringComparer.Ordinal);
        static readonly HashSet<string> Duplicates=new HashSet<string>(StringComparer.Ordinal);
        static uint IndexedRevision=uint.MaxValue;static bool IndexDirty=true,Failed;
        // Root owns the independent authenticated cooldown protocol lifecycle.
        static IWorkbenchEnchantmentCooldownAuthority Authority;
        internal static void Initialize(string directory){WorkbenchEnchantmentNetwork.Shutdown();Initialize(directory,WorkbenchEnchantmentCooldownProtocol.Authority);WorkbenchEnchantmentUiBinding.Initialize();}
        internal static void RegisterPeer(ZNetPeer peer)=>WorkbenchEnchantmentNetwork.RegisterPeer(peer);
        internal static void Initialize(string directory,IWorkbenchEnchantmentCooldownAuthority authority)
        {DirectoryPath=Path.GetFullPath(directory);Authority=authority;ShutdownSession();}
        internal static void Shutdown(){try{WorkbenchEnchantmentUiBinding.Shutdown();}finally{ShutdownSession();WorkbenchEnchantmentNetwork.Shutdown();DirectoryPath=null;Authority=null;}}
        static void ShutdownSession()
        {
            try{WorkbenchEnchantmentBenchVisual.Clear();}
            catch(Exception error){try{MasteryPlugin.Log?.LogWarning("[WorkbenchEnchantments] Optional visual cleanup: "+error.Message);}catch(Exception){}}
            finally{Journal=null;Service=null;Session=null;Manager=null;World=0;NativeStations.Clear();Duplicates.Clear();IndexedRevision=uint.MaxValue;IndexDirty=true;Failed=false;}
        }
        internal static bool Ready=>MasteryPlugin.Settings?.Enabled?.Value==true&&Service!=null&&Journal?.Ready==true&&Current();
        static bool Current()=>ReferenceEquals(Session,ZNet.instance)&&ReferenceEquals(Manager,ZDOMan.instance)&&World!=0&&Session?.GetWorldUID()==World&&Generation==MasterIdolLoadIdentity.Generation&&MasterIdolLoadIdentity.LoadReady;
        internal static void Tick()
        {
            WorkbenchEnchantmentNetwork.Tick();
            var net=ZNet.instance;var manager=ZDOMan.instance;
            if(net?.IsServer()!=true||manager==null||net.GetWorld()==null||DirectoryPath==null)return;
            if(Session!=net||Manager!=manager||World!=net.GetWorldUID()||Generation!=MasterIdolLoadIdentity.Generation)
            {ShutdownSession();Session=net;Manager=manager;World=net.GetWorldUID();Generation=MasterIdolLoadIdentity.Generation;}
            if(Failed||!MasterIdolLoadIdentity.LoadReady||!MasterIdolWorldIndex.Ensure())return;
            Index();if(Journal!=null)return;
            try
            {
                // One completeness-aware indexed marker pass per initial load.
                bool priorIdentity=NativeStations.Count!=0||Duplicates.Count!=0;
                string file=Path.Combine(DirectoryPath,"workbench-enchantments-"+World.ToString(CultureInfo.InvariantCulture)+".bin");
                Journal=new WorkbenchEnchantmentJournal(file,World,true,priorIdentity);Service=new WorkbenchEnchantmentService(Journal,()=>Authority??WorkbenchEnchantmentCooldownProtocol.Authority);
            }
            catch(Exception e){Failed=true;MasteryPlugin.Log.LogWarning("[WorkbenchEnchantments] State unavailable: "+e.Message);}
        }
        internal static void InvalidateIndex()=>IndexDirty=true;
        static void Index()
        {
            if(!IndexDirty&&IndexedRevision==MasterIdolWorldIndex.TopologyRevision)return;NativeStations.Clear();Duplicates.Clear();
            foreach(var data in MasterIdolWorldIndex.Stations.Values)
            {
                if(data.GetPrefab()!="piece_workbench".GetStableHashCode()||!ReferenceEquals(Manager?.GetZDO(data.m_uid),data))continue;
                string id=data.GetString(StationKey,"");if(!Guid.TryParseExact(id,"N",out _))continue;
                if(NativeStations.ContainsKey(id))Duplicates.Add(id);else NativeStations.Add(id,data);
            }
            IndexedRevision=MasterIdolWorldIndex.TopologyRevision;IndexDirty=false;
        }
        internal static string Witness(ZDO data)
        {var p=data.GetPosition();return data.GetPrefab()+"/"+data.GetLong(ZDOVars.s_creator,0)+"/"+p.x.ToString("R",CultureInfo.InvariantCulture)+"/"+p.y.ToString("R",CultureInfo.InvariantCulture)+"/"+p.z.ToString("R",CultureInfo.InvariantCulture);}
        static bool ActorValid(WorkshopActor actor,ZDO data,CraftingStation station,bool requireSkill=true)
        {
            if(!Ready||actor?.Available!=true||actor.IsDead()||actor.IsTeleporting()||data==null||station==null||data.GetPrefab()!="piece_workbench".GetStableHashCode()||!ReferenceEquals(Manager.GetZDO(data.m_uid),data)||station.GetComponent<ZNetView>()?.GetZDO()!=data||Vector3.Distance(actor.Position,data.GetPosition())>5f)return false;
            bool skill=actor.Rpc==null?OwnerSkillAuthority.Has(Player.m_localPlayer,Skills.SkillType.Crafting,100):OwnerSkillAuthority.Has(actor.Rpc,actor.CharacterId,actor.GetPlayerID(),Skills.SkillType.Crafting,100);
            return (!requireSkill||skill)&&MasterIdolWorldIndex.WardAllows(actor.GetPlayerID(),data.GetPosition());
        }
        internal static WorkbenchEnchantmentContext Resolve(ZRpc rpc,ZDOID selected,bool allowNew,bool requireSkill=true)
        {
            Tick();if(!Ready)return null;var actor=WorkshopActor.Resolve(rpc);var data=Manager.GetZDO(selected);var station=ZNetScene.instance?.FindInstance(selected)?.GetComponent<CraftingStation>();
            if(!ActorValid(actor,data,station,requireSkill))return null;Index();string id=data.GetString(StationKey,"");
            if(id.Length==0)
            {
                if(!allowNew)return null;string witness=Witness(data);
                // A lost native marker is unresolved, never a second charged bench.
                var all=Journal.Snapshot();if(all.Stations.Values.Any(b=>!b.Removed&&(b.NativeUid==selected.ToString()||b.NativeWitness==witness)))return null;
                id=Guid.NewGuid().ToString("N");data.Set(StationKey,id);InvalidateIndex();Index();
            }
            if(!Guid.TryParseExact(id,"N",out _)||Duplicates.Contains(id)||!NativeStations.TryGetValue(id,out var current)||!ReferenceEquals(current,data)||!MasterIdolNetworkCache.TryKey(data.GetPosition(),out string network))return null;
            var context=new WorkbenchEnchantmentContext{World=World,Actor=actor.GetPlayerID(),Station=id,NativeUid=selected.ToString(),NativeLevel=station.GetLevel(false),Network=network};
            var expectedSession=Session;var expectedManager=Manager;ulong expectedGeneration=Generation;
            context.Revalidate=()=>
            {
                if(Session!=expectedSession||Manager!=expectedManager||Generation!=expectedGeneration||!ActorValid(actor,data,station,requireSkill)||data.GetString(StationKey,"")!=id)return false;
                Index();if(Duplicates.Contains(id)||!NativeStations.TryGetValue(id,out var record)||!ReferenceEquals(record,data)||!MasterIdolNetworkCache.TryKey(data.GetPosition(),out var key))return false;
                context.NativeLevel=station.GetLevel(false);context.Network=key;return true;
            };
            var saved=Journal.ReadStation(id);
            if(saved==null){if(!allowNew||!Service.RegisterSelected(context,Witness(data)))return null;}
            else if(saved.NativeUid!=selected.ToString())
            {
                if(!Service.BindReloadAlias(context,Witness(data),()=>WorkbenchEnchantmentNativeIdentity.Matches(data,saved.Id)&&context.Revalidate()))return null;
            }
            return context;
        }
        // Existing topology cache; no entire-world enumeration per choice/row.
        internal static bool NetworkAllows(string selected,string choice,WorkbenchEnchantmentState state)
        {
            if(choice.Length==0)return true;Index();if(!NativeStations.TryGetValue(selected,out var source)||!MasterIdolNetworkCache.TryKey(source.GetPosition(),out var group))return false;
            int count=0;foreach(var b in state.Stations.Values)
            {
                if(b.Id==selected||b.Removed||b.Active.Length==0||Journal.HasPending(b.Id)||!NativeStations.TryGetValue(b.Id,out var data)||Duplicates.Contains(b.Id)||!MasterIdolNetworkCache.TryKey(data.GetPosition(),out var other)||other!=group)continue;
                var native=ZNetScene.instance.FindInstance(data.m_uid)?.GetComponent<CraftingStation>();if(native==null||!WorkbenchEnchantmentRules.TryAvailable(WorkbenchEnchantmentService.Choices(b),native.GetLevel(false),out var available)||!available.Contains(b.Active))continue;
                if(b.Active==choice)return false;count++;
            }
            return count<2;
        }
        internal static WorkbenchCooldownContext CooldownContext(WorkbenchEnchantmentContext domain,ZRpc rpc)
        {
            var clock=WorkbenchEnchantmentCooldownProtocol.CreateContext(domain,rpc);
            if(clock==null)return null;
            var identity=clock.IdentityCurrent;
            clock.OriginalNativeIdentity=original=>
            {
                if(identity?.Invoke()!=true||!Journal.VerifyDisk())return false;
                var b=Journal.ReadStation(domain.Station);
                if(b==null||b.NativeUid!=domain.NativeUid||b.NativeWitness!=clock.NativeWitness)return false;
                bool stored=b.ReloadAliases.Contains(original)||b.Grants.Any(g=>g.Payer==clock.Character&&g.FreeToken.Length!=0&&g.OriginalNativeUid==original&&g.FreeWitness==clock.NativeWitness)||
                    Journal.Snapshot().Pending.Values.Any(p=>p.Actor==clock.Character&&p.Station==domain.Station&&p.Kind=="FreeLearn"&&p.OriginalNativeUid==original&&p.FreeWitness==clock.NativeWitness);
                return stored&&NativeStations.TryGetValue(domain.Station,out var native)&&ReferenceEquals(Manager.GetZDO(native.m_uid),native)&&WorkbenchEnchantmentNativeIdentity.Matches(native,domain.Station);
            };
            return clock;
        }
        internal static bool UniqueNative(ZDO data,string id)
        {
            if(!Ready||data==null||!MasterIdolWorldIndex.Ensure()||!ReferenceEquals(Manager.GetZDO(data.m_uid),data))return false;
            Index();return !Duplicates.Contains(id)&&NativeStations.TryGetValue(id,out var selected)&&ReferenceEquals(selected,data)&&data.GetString(StationKey,"")==id;
        }
        // No player opening is needed to certify a loaded bench's exact identity.
        internal static bool BindLoadedNative(ZDO data)
        {
            if(!Ready||data==null||!ReferenceEquals(Manager.GetZDO(data.m_uid),data))return false;
            string id=data.GetString(StationKey,"");var b=Journal.ReadStation(id);string witness=Witness(data);
            if(b==null||b.Removed||b.NativeWitness!=witness||!UniqueNative(data,id))return false;
            var net=Session;var manager=Manager;ulong generation=Generation;
            return Service.BindNativeLoadedAlias(World,id,data.m_uid.ToString(),witness,()=>Current()&&Session==net&&Manager==manager&&Generation==generation&&ReferenceEquals(manager.GetZDO(data.m_uid),data)&&UniqueNative(data,id)&&WorkbenchEnchantmentNativeIdentity.Matches(data,id)&&Witness(data)==witness);
        }
        internal static void ReconcilePersisted(ZRpc rpc,WorkbenchEnchantmentPending pending)
        {
            var actor=WorkshopActor.Resolve(rpc);var b=Journal.ReadStation(pending.Station);
            if(actor?.Available!=true||pending.Actor!=actor.GetPlayerID()||b==null||b.NativeWitness!=pending.FreeWitness)return;
            var net=Session;var manager=Manager;ulong generation=Generation;var character=actor.CharacterId;
            string id=b.Id,uid=b.NativeUid,witness=b.NativeWitness;
            Func<bool> archive=()=>
            {
                if(!Current()||Session!=net||Manager!=manager||Generation!=generation||!Journal.VerifyDisk())return false;
                var current=WorkshopActor.Resolve(rpc);var record=Journal.ReadStation(id);
                return current?.Available==true&&current.GetPlayerID()==pending.Actor&&current.CharacterId==character&&record!=null&&record.Id==id&&record.NativeUid==uid&&record.NativeWitness==witness;
            };
            var domain=new WorkbenchEnchantmentContext{World=World,Actor=pending.Actor,Station=id,NativeUid=uid,Revalidate=archive};
            var permit=WorkbenchEnchantmentCooldownProtocol.ReadOriginal(rpc,pending.Operation,pending.Choice);
            if(permit==null||permit.World!=World||permit.Character!=pending.Actor||permit.Station!=id||permit.Operation!=pending.Operation||permit.Choice!=pending.Choice||permit.Token!=pending.FreeToken||permit.NativeUid!=pending.OriginalNativeUid||permit.NativeWitness!=pending.FreeWitness)return;
            if(!Service.VerifyPersistedOperation(permit)&&!Service.PrepareRetiredNoGrant(domain,permit,WorkbenchEnchantmentCooldownProtocol.IsRetiredPending))return;
            Func<bool> result=()=>archive()&&Service.VerifyPersistedOperation(permit)&&archive();
            var clock=WorkbenchEnchantmentCooldownProtocol.CreateSettlementContext(permit,rpc,result);
            if(clock!=null)Service.RecoverPersistedResult(domain,clock,permit);
        }
        // Bounded candidates: six character permits plus six unconfirmed world results.
        internal static void ReconcileRemoved(ZRpc rpc)
        {
            if(!Ready)return;
            var actor=WorkshopActor.Resolve(rpc);if(actor?.Available!=true)return;
            var permits=WorkbenchEnchantmentCooldownProtocol.ReadPending(rpc);
            if(permits==null||permits.Length>6)return;
            var candidates=new List<WorkbenchEnchantmentPending>(12);
            foreach(var permit in permits)candidates.Add(new WorkbenchEnchantmentPending{Operation=permit.Operation,Station=permit.Station,Choice=permit.Choice,Actor=permit.Character,FreeToken=permit.Token,OriginalNativeUid=permit.NativeUid,FreeWitness=permit.NativeWitness});
            candidates.AddRange(Journal.ReadUnconfirmed(actor.GetPlayerID()));var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var permit in candidates)
            {
                var b=Journal.ReadStation(permit.Station);
                if(b==null||b.NativeWitness!=permit.FreeWitness||permit.Actor!=actor.GetPlayerID()||!seen.Add(permit.Operation))continue;
                if(!b.Removed){ReconcilePersisted(rpc,permit);continue;}
                if(b.RemovalKind!="Destroy")continue;
                string removal=b.RemovalOperation,uid=b.NativeUid,witness=b.NativeWitness,id=b.Id;
                var net=Session;var manager=Manager;ulong generation=Generation;
                Func<bool> removed=()=>
                {
                    if(!Current()||Session!=net||Manager!=manager||Generation!=generation||!Journal.VerifyDisk())return false;
                    var record=Journal.ReadStation(id);
                    return record?.Removed==true&&record.RemovalKind=="Destroy"&&record.RemovalOperation==removal&&record.NativeUid==uid&&record.NativeWitness==witness;
                };
                var domain=new WorkbenchEnchantmentContext{World=World,Actor=actor.GetPlayerID(),Station=id,NativeUid=uid,Revalidate=removed};
                var clock=WorkbenchEnchantmentCooldownProtocol.CreateRecoveryContext(domain,rpc,removed);
                if(clock==null)continue;
                var identity=clock.IdentityCurrent;
                clock.OriginalNativeIdentity=original=>identity?.Invoke()==true&&removed()&&
                    (original==uid||b.ReloadAliases.Contains(original)||b.Grants.Any(g=>g.Operation==permit.Operation&&g.Payer==permit.Actor&&g.FreeToken==permit.FreeToken&&g.OriginalNativeUid==original&&g.FreeWitness==witness)||
                    Journal.Snapshot().Pending.TryGetValue(permit.Operation,out var p)&&p.Kind=="FreeLearn"&&p.Actor==permit.Actor&&p.FreeToken==permit.FreeToken&&p.OriginalNativeUid==original&&p.FreeWitness==witness);
                Service.RecoverRemoved(domain,clock,permit.Operation,permit.Choice);
            }
        }
        // Called by root inside the existing server projection tick, not new RPC.
        internal static List<MasterIdolEffectZone> ProjectCommittedZones()
        {
            var zones=new List<MasterIdolEffectZone>();if(!Ready)return zones;Index();
            var groups=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
            foreach(string id in NativeStations.Keys.OrderBy(x=>x,StringComparer.Ordinal))
            {
                if(Duplicates.Contains(id)||Journal.HasPending(id))continue;var b=Journal.ReadStation(id);var data=NativeStations[id];
                if(b==null||b.Removed||b.NativeUid!=data.m_uid.ToString()||b.Active.Length==0)continue;
                var native=ZNetScene.instance.FindInstance(data.m_uid)?.GetComponent<CraftingStation>();if(native==null||!WorkbenchEnchantmentRules.TryAvailable(WorkbenchEnchantmentService.Choices(b),native.GetLevel(false),out var available)||!available.Contains(b.Active)||!MasterIdolNetworkCache.TryKey(data.GetPosition(),out var group))continue;
                if(!groups.TryGetValue(group,out var types))groups[group]=types=new HashSet<string>(StringComparer.Ordinal);if(types.Count>=2||!types.Add(b.Active))continue;
                var circles=MasterIdolNetworkCache.EffectCoverage(data.GetPosition());if(circles.Length==0)continue;
                string home=data.m_uid+"/"+id;
                zones.Add(new MasterIdolEffectZone{Id=home,Type=b.Active,Home=data.GetPosition(),Circles=circles});
            }
            return zones;
        }
    }
}







