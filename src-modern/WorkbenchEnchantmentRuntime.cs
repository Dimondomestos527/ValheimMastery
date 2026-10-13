using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
namespace ValheimMastery
{
    internal enum WorkbenchEnchantmentAccessFailure
    { None, Runtime, Disabled, Load, StateUnavailable, IndexUnavailable, JournalUnavailable, Session, Actor, NativeRecord, RenderedIdentity, ServerStation, Range, Skill, Ward, MissingMarker, LostMarker, InvalidMarker, DuplicateMarker, Index, Network, Registration, ReloadAlias }
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
        // A dedicated host's valid world record need not have a rendered instance.
        // A present but invalid instance is an identity failure, never a fallback.
        static bool TryStation(ZDO data,out CraftingStation station,out int level,out WorkbenchEnchantmentAccessFailure failure)
        {
            station=null;level=0;failure=WorkbenchEnchantmentAccessFailure.NativeRecord;
            if(data==null||data.GetPrefab()!="piece_workbench".GetStableHashCode()||!ReferenceEquals(Manager?.GetZDO(data.m_uid),data))return false;
            var scene=ZNetScene.instance;if(scene==null)return false;
            var loaded=scene.FindInstance(data.m_uid);
            if(loaded!=null)
            {
                failure=WorkbenchEnchantmentAccessFailure.RenderedIdentity;
                var live=loaded.GetComponent<CraftingStation>();var view=loaded.GetComponent<ZNetView>();
                if(live==null||view?.IsValid()!=true||!ReferenceEquals(view.GetZDO(),data))return false;
                station=live;level=live.GetLevel(false);failure=WorkbenchEnchantmentAccessFailure.None;return true;
            }
            failure=WorkbenchEnchantmentAccessFailure.ServerStation;
            var entry=WorkshopWorldRecords.Find(data.m_uid);var definition=scene.GetPrefab(data.GetPrefab());
            if(entry?.Station==null||entry.Level<1||!ReferenceEquals(entry.Data,data)||!ReferenceEquals(Manager.GetZDO(data.m_uid),data)||
                definition==null||!ReferenceEquals(entry.Definition,definition)||!ReferenceEquals(definition.GetComponent<CraftingStation>(),entry.Station))return false;
            station=entry.Station;level=entry.Level;failure=WorkbenchEnchantmentAccessFailure.None;return true;
        }
        static WorkbenchEnchantmentAccessFailure ActorFailure(WorkshopActor actor,ZDO data,CraftingStation station,bool requireSkill)
        {
            if(!Ready)return WorkbenchEnchantmentAccessFailure.Runtime;
            if(actor?.Available!=true||actor.IsDead()||actor.IsTeleporting())return WorkbenchEnchantmentAccessFailure.Actor;
            if(!TryStation(data,out var current,out _,out var failure))return failure;
            if(station==null||!ReferenceEquals(station,current))return WorkbenchEnchantmentAccessFailure.RenderedIdentity;
            if(Vector3.Distance(actor.Position,data.GetPosition())>5f)return WorkbenchEnchantmentAccessFailure.Range;
            bool skill=actor.Rpc==null?OwnerSkillAuthority.Has(Player.m_localPlayer,Skills.SkillType.Crafting,100):OwnerSkillAuthority.Has(actor.Rpc,actor.CharacterId,actor.GetPlayerID(),Skills.SkillType.Crafting,100);
            if(requireSkill&&!skill)return WorkbenchEnchantmentAccessFailure.Skill;
            return MasterIdolWorldIndex.WardAllows(actor.GetPlayerID(),data.GetPosition())?WorkbenchEnchantmentAccessFailure.None:WorkbenchEnchantmentAccessFailure.Ward;
        }
        static bool ActorValid(WorkshopActor actor,ZDO data,CraftingStation station,bool requireSkill=true)=>ActorFailure(actor,data,station,requireSkill)==WorkbenchEnchantmentAccessFailure.None;
        static WorkbenchEnchantmentAccessFailure ReadinessFailure()
        {
            if(MasteryPlugin.Settings?.Enabled?.Value!=true)return WorkbenchEnchantmentAccessFailure.Disabled;
            if(!MasterIdolLoadIdentity.LoadReady)return WorkbenchEnchantmentAccessFailure.Load;
            if(Failed)return WorkbenchEnchantmentAccessFailure.StateUnavailable;
            if(!MasterIdolWorldIndex.Ready)return WorkbenchEnchantmentAccessFailure.IndexUnavailable;
            if(Service==null||Journal?.Ready!=true)return WorkbenchEnchantmentAccessFailure.JournalUnavailable;
            return Current()?WorkbenchEnchantmentAccessFailure.None:WorkbenchEnchantmentAccessFailure.Session;
        }
        internal static WorkbenchEnchantmentContext Resolve(ZRpc rpc,ZDOID selected,bool allowNew,bool requireSkill=true)
            =>Resolve(rpc,selected,allowNew,requireSkill,out _);
        internal static WorkbenchEnchantmentContext Resolve(ZRpc rpc,ZDOID selected,bool allowNew,bool requireSkill,out WorkbenchEnchantmentAccessFailure failure)
        {
            Tick();failure=ReadinessFailure();if(failure!=WorkbenchEnchantmentAccessFailure.None)return null;var actor=WorkshopActor.Resolve(rpc);var data=Manager.GetZDO(selected);
            if(!TryStation(data,out var station,out int level,out failure))return null;
            failure=ActorFailure(actor,data,station,requireSkill);if(failure!=WorkbenchEnchantmentAccessFailure.None)return null;Index();string id=data.GetString(StationKey,"");
            if(id.Length==0)
            {
                if(!allowNew){failure=WorkbenchEnchantmentAccessFailure.MissingMarker;return null;}string witness=Witness(data);
                // A lost native marker is unresolved, never a second charged bench.
                var all=Journal.Snapshot();if(all.Stations.Values.Any(b=>!b.Removed&&(b.NativeUid==selected.ToString()||b.NativeWitness==witness))){failure=WorkbenchEnchantmentAccessFailure.LostMarker;return null;}
                id=Guid.NewGuid().ToString("N");data.Set(StationKey,id);InvalidateIndex();Index();
            }
            if(!Guid.TryParseExact(id,"N",out _)){failure=WorkbenchEnchantmentAccessFailure.InvalidMarker;return null;}
            if(Duplicates.Contains(id)){failure=WorkbenchEnchantmentAccessFailure.DuplicateMarker;return null;}
            if(!NativeStations.TryGetValue(id,out var current)||!ReferenceEquals(current,data)){failure=WorkbenchEnchantmentAccessFailure.Index;return null;}
            if(!MasterIdolNetworkCache.TryKey(data.GetPosition(),out string network)){failure=WorkbenchEnchantmentAccessFailure.Network;return null;}
            var context=new WorkbenchEnchantmentContext{World=World,Actor=actor.GetPlayerID(),Station=id,NativeUid=selected.ToString(),NativeLevel=level,Network=network};
            var expectedSession=Session;var expectedManager=Manager;ulong expectedGeneration=Generation;
            context.Revalidate=()=>
            {
                if(Session!=expectedSession||Manager!=expectedManager||Generation!=expectedGeneration||!ActorValid(actor,data,station,requireSkill)||data.GetString(StationKey,"")!=id)return false;
                Index();if(Duplicates.Contains(id)||!NativeStations.TryGetValue(id,out var record)||!ReferenceEquals(record,data)||!MasterIdolNetworkCache.TryKey(data.GetPosition(),out var key))return false;
                if(!TryStation(data,out var bound,out int currentLevel,out _)||!ReferenceEquals(bound,station))return false;
                context.NativeLevel=currentLevel;context.Network=key;return true;
            };
            var saved=Journal.ReadStation(id);
            if(saved==null){if(!allowNew||!Service.RegisterSelected(context,Witness(data))){failure=WorkbenchEnchantmentAccessFailure.Registration;return null;}}
            else if(saved.NativeUid!=selected.ToString())
            {
                if(!Service.BindReloadAlias(context,Witness(data),()=>WorkbenchEnchantmentNativeIdentity.Matches(data,saved.Id)&&context.Revalidate())){failure=WorkbenchEnchantmentAccessFailure.ReloadAlias;return null;}
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
                if(!TryStation(data,out _,out int level,out _)||!WorkbenchEnchantmentRules.TryAvailable(WorkbenchEnchantmentService.Choices(b),level,out var available)||!available.Contains(b.Active))continue;
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
        // Natural travel-menu opens only. An over-limit sentinel is denied, never truncated.
        internal static ZDOID[] TravelCandidates()
        {
            if(!Ready||Session?.IsServer()!=true||!MasterIdolWorldIndex.Ensure())return Array.Empty<ZDOID>();
            Index();return NativeStations.Values.Take(4097).Select(data=>data.m_uid).ToArray();
        }
        internal static bool TryTravelEndpoint(ZDOID uid,WorkshopActor actor,out WorkbenchTravelEndpoint endpoint)
        {
            endpoint=null;
            if(!Ready||Session?.IsServer()!=true||actor?.Available!=true||actor.IsDead()||actor.IsTeleporting())return false;
            var data=Manager?.GetZDO(uid);if(data==null)return false;
            string id=data.GetString(StationKey,"");
            if(!Guid.TryParseExact(id,"N",out _)||!UniqueNative(data,id)||Journal.HasPending(id))return false;
            var row=Journal.ReadStation(id);
            if(row==null||row.Removed||row.NativeUid!=uid.ToString()||row.NativeWitness!=Witness(data)||!WorkbenchEnchantmentRules.Known(row.Active)||
                !TryStation(data,out _,out int level,out _)||!WorkbenchEnchantmentRules.TryAvailable(WorkbenchEnchantmentService.Choices(row),level,out var available)||!available.Contains(row.Active))return false;
            var published=MasterIdolEffectZones.Find(uid+"/"+id);
            if(published==null||published.Type!=row.Active||published.Home!=data.GetPosition()||
                !MasterIdolWorldIndex.WardAllows(actor.GetPlayerID(),data.GetPosition()))return false;
            endpoint=new WorkbenchTravelEndpoint(uid,id,row.Active,data.GetPosition(),data.GetRotation());return true;
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
                if(!TryStation(data,out _,out int level,out _)||!WorkbenchEnchantmentRules.TryAvailable(WorkbenchEnchantmentService.Choices(b),level,out var available)||!available.Contains(b.Active)||!MasterIdolNetworkCache.TryKey(data.GetPosition(),out var group))continue;
                if(!groups.TryGetValue(group,out var types))groups[group]=types=new HashSet<string>(StringComparer.Ordinal);if(types.Count>=2||!types.Add(b.Active))continue;
                var circles=MasterIdolNetworkCache.EffectCoverage(data.GetPosition());if(circles.Length==0)continue;
                string home=data.m_uid+"/"+id;
                zones.Add(new MasterIdolEffectZone{Id=home,Type=b.Active,Home=data.GetPosition(),Circles=circles});
            }
            return zones;
        }
    }
}







