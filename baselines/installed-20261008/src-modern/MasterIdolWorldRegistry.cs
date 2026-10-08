using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasterIdolWorldRegistry
    {
        internal const string TokenKey = "vm.idol.token.v1", ActiveKey = "vm.idol.active.v1", BoundKey = "vm.idol.bound.v1";
        internal const string QuotaKey="vm.idol.quota.v1";
        internal const string SwitchKey = "vm.idol.switch.v1";
        private static MasterIdolSwitchStore Switches;
        internal static readonly MasterIdolRegistryModel Model = new MasterIdolRegistryModel();
        internal static MasterIdolAdmissionStore Store { get; private set; }
        private static ZNet Session;
        private static uint MirroredTopology;
        private static bool MirroredEnabled;
        private static long SwitchRequestedAt;
        private static bool MirrorPending, Reconciled;
        internal static string SwitchDenial {get;private set;}
        internal static string SwitchFailure {get;private set;}
        private static MasterIdolReadinessRules Readiness = new MasterIdolReadinessRules();
        private static readonly Dictionary<string,string> ProofReasons = new Dictionary<string,string>(StringComparer.Ordinal);
        private static readonly List<string> ProofIds = new List<string>();
        private static int ProofCursor;
        private static HashSet<string> Effective=new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<ZDOID> SendPending=new HashSet<ZDOID>();
        private static uint IndexedRevision;
        private static long World;
        private static readonly Dictionary<int, MasterIdolProfile> Profiles = BuildProfiles();
        private static Dictionary<int, MasterIdolProfile> BuildProfiles()
        {
            var result = new Dictionary<int, MasterIdolProfile>();
            foreach (var profile in MasterIdolProfiles.All) result.Add(profile.Prefab.GetStableHashCode(), profile);
            return result;
        }
        private static readonly Dictionary<string, ZDO> WorldObjects = new Dictionary<string, ZDO>(StringComparer.Ordinal);
        private static readonly HashSet<string> Conflicts = new HashSet<string>(StringComparer.Ordinal);
        internal static void Reset()
        { Reconciled=false;Effective.Clear();MasterIdolNetworkCache.Reset(); Session = null; Store = null; Switches = null; Model.Reset(); WorldObjects.Clear(); Conflicts.Clear(); SendPending.Clear(); MirrorPending=true; SwitchRequestedAt=0; MirroredTopology = uint.MaxValue; World = 0; IndexedRevision = uint.MaxValue; Readiness=new MasterIdolReadinessRules();ProofReasons.Clear();ProofIds.Clear();ProofCursor=0;SwitchFailure=null; }
        internal static void Refresh(bool force = false)
        {
            var net = ZNet.instance;
            if (Session != net || World != (net?.GetWorld() != null ? net.GetWorldUID() : 0)) { Reset(); Session = net; World = net?.GetWorld() != null ? net.GetWorldUID() : 0; }
            if (net?.IsServer() != true || net.GetWorld() == null || ZDOMan.instance == null || ZNetScene.instance == null) return;
            if (Store == null)
            {
                long world = net.GetWorld().m_uid;
                Store = new MasterIdolAdmissionStore(Path.Combine(BepInEx.Paths.ConfigPath, "ValheimMastery", "Idols", world + ".vmi"), world);
                if (!Store.Load()) MasteryPlugin.Log.LogError("[MasterIdols] Admission journal unavailable; placement blocked.");
                Switches = new MasterIdolSwitchStore(Path.Combine(BepInEx.Paths.ConfigPath, "ValheimMastery", "Idols", world + ".switches.vmi"), world);
                if (!Switches.Load()) MasteryPlugin.Log.LogError("[MasterIdols] Switch journal unavailable; idols remain off.");
                if(Store.Ready)Switches.BindLegacy(Store.Records);
            }
            if (!MasterIdolWorldIndex.Ensure()) { Model.Reset(); return; }
            if(!Reconciled&&Store.Ready){ReconcileKnownOutputs();Reconciled=true;}
            bool published = Switches?.Poll() == true;
            if(Switches?.LegacyPending==true && Store.Ready)Switches.BindLegacy(Store.Records); // includes deferred loads before any publication/write
            if(published && SwitchRequestedAt!=0){MasterIdolTiming.Finish("switch.request-to-publication",SwitchRequestedAt);SwitchRequestedAt=0;}
            bool changed = IndexedRevision != MasterIdolWorldIndex.Revision;
            if (changed)
            {
                WorldObjects.Clear();
                foreach (var zdo in MasterIdolWorldIndex.Idols.Values) WorldObjects[zdo.m_uid.ToString()] = zdo;
                RebuildKnown(); IndexedRevision = MasterIdolWorldIndex.Revision;
            }
            bool topology = MirroredTopology != MasterIdolWorldIndex.TopologyRevision || MirroredEnabled != GoldCraftingService.Enabled;
            bool readiness=Readiness.Changed(MasterIdolPlacement.CurrentLedger,Store.Ready,Switches?.Ready==true,Switches?.LoadDeferred==true,Switches?.LegacyPending==true);
            // Probe only already indexed idols, never all world objects. Same-ledger proof changes also invalidate.
            if(Readiness.ProbeDue(Time.unscaledTime))
                for(int i=0;i<Math.Min(16,ProofIds.Count);i++)
                {
                    if(ProofCursor>=ProofIds.Count)ProofCursor=0;string id=ProofIds[ProofCursor++];
                    if(WorldObjects.TryGetValue(id,out var zdo)&&(!ProofReasons.TryGetValue(id,out string prior)||prior!=ConfirmationReason(zdo)))readiness=true;
                }
            if (!force && !published && !changed && !topology && !readiness && !MirrorPending) return;
            MirrorPending=true;
            Mirror();
            MirroredTopology = MasterIdolWorldIndex.TopologyRevision; MirroredEnabled = GoldCraftingService.Enabled;
        }
        private static void Mirror()
        {
            MirrorPending=true;
            RebuildQuota();
            foreach (var pair in WorldObjects)
            {
                var zdo = pair.Value; var profile = Profile(zdo.GetPrefab());
                if(profile==null || !ReferenceEquals(ZDOMan.instance?.GetZDO(zdo.m_uid),zdo)||zdo.m_uid.ToString()!=pair.Key){SendPending.Remove(zdo.m_uid);continue;}
                string reason=ConfirmationReason(zdo);bool active=reason=="ok";
                if(!ProofReasons.TryGetValue(pair.Key,out string previous)||previous!=reason)
                {
                    ProofReasons[pair.Key]=reason;
                    if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogInfo("[MasterIdols] proof output="+pair.Key+" type="+profile.Id+" historical="+MasterIdolLoadIdentity.Output(zdo)+" token="+zdo.GetString(TokenKey,"")+" creator="+zdo.GetLong(ZDOVars.s_creator,0)+" pos="+zdo.GetPosition()+" loadReady="+MasterIdolLoadIdentity.LoadReady+" gate="+reason+" switchReady="+(Switches?.Ready==true)+" desiredState="+(Switches?.State(profile.Id,MasterIdolLoadIdentity.Output(zdo),zdo.GetString(TokenKey,""))??0));
                }
                bool requested=MasterIdolToggleRules.On(Switches?.State(profile.Id,MasterIdolLoadIdentity.Output(zdo),zdo.GetString(TokenKey,""))??0);
                bool paused=requested&&!Effective.Contains(pair.Key);
                bool bound = active && MasterIdolWorldIndex.HasWorkbench(zdo.GetPosition())&&!paused;
                bool dirty=false;
                if (zdo.GetBool(ActiveKey, false) != active) { zdo.Set(ActiveKey, active); dirty=true; }
                if (zdo.GetBool(BoundKey, false) != bound) { zdo.Set(BoundKey, bound); dirty=true; }
                int state = active ? Switches?.State(profile.Id, MasterIdolLoadIdentity.Output(zdo),zdo.GetString(TokenKey,"")) ?? 0 : 0;
                if (zdo.GetInt(SwitchKey, 0) != state) { zdo.Set(SwitchKey, state); dirty=true; }
                if(zdo.GetBool(QuotaKey,false)!=paused){zdo.Set(QuotaKey,paused);dirty=true;}
                if(dirty)SendPending.Add(zdo.m_uid);
                if(SendPending.Contains(zdo.m_uid)){ZDOMan.instance.ForceSendZDO(zdo.m_uid);SendPending.Remove(zdo.m_uid);} // retry failed delivery
            }
            MirrorPending=false;
        }
        internal static MasterIdolProfile Profile(int prefabHash)
        { return Profiles.TryGetValue(prefabHash, out var profile) ? profile : null; }
        internal static bool CanReserve(string type)
        { Refresh(); return MasterIdolWorldIndex.Ensure() && Store?.Capacity == true && MasterIdolProfiles.Find(type)!=null; }
        internal static bool HasTokenObject(string token)
        { Refresh(); if (!MasterIdolWorldIndex.Ensure()) return true; foreach (var record in WorldObjects.Values) if (record.GetString(TokenKey, "") == token) return true; return false; }
        internal static bool Confirm(string token, long player, ZDOID output)
        {
            Refresh(); if (!MasterIdolWorldIndex.Ensure()) return false; var entry = Store?.Find(token); var record = ZDOMan.instance?.GetZDO(output);
            if (entry == null || !Store.Ready || entry.Player != player || entry.Closed || record == null ||
                Profile(record.GetPrefab())?.Id != entry.Type || record.GetString(TokenKey, "") != token ||
                record.GetLong(ZDOVars.s_creator, 0) != player ||
                Vector3.Distance(record.GetPosition(), new Vector3(entry.X, entry.Y, entry.Z)) > .1f) return false;
            bool accepted = Store.Apply(token, player, output.ToString());
            if(accepted && !MasterIdolFavor.Bind(Store.Find(token)))return false;
            if (accepted)
            {
                MasterIdolWorldIndex.Observe(record); WorldObjects[output.ToString()] = record; RebuildKnown(); Mirror();
            }
            return accepted;
        }
        internal static int Component(ZDO idol)
        {
            Refresh();
            return ZNet.instance?.IsServer() == true && GoldCraftingService.Enabled && ConfirmedActive(idol) &&
                MasterIdolWorldIndex.HasWorkbench(idol.GetPosition()) && Effective.Contains(idol.m_uid.ToString()) &&
                MasterIdolToggleRules.On(Switches?.State(Profile(idol.GetPrefab()).Id, MasterIdolLoadIdentity.Output(idol),idol.GetString(TokenKey,"")) ?? 0)
                ? WorkshopWorldRecords.Covered(idol.GetPosition()) : -1;
        }
        // Whole-network geometry for future owned effects; no biome gameplay is implied.
        internal static bool NetworkCovers(ZDO idol,Vector3 target)
        {
            Refresh();
            return GoldCraftingService.Enabled && ConfirmedActive(idol) && MasterIdolWorldIndex.HasWorkbench(idol.GetPosition()) && Effective.Contains(idol.m_uid.ToString()) &&
                MasterIdolToggleRules.On(Switches?.State(Profile(idol.GetPrefab()).Id,MasterIdolLoadIdentity.Output(idol),idol.GetString(TokenKey,""))??0) &&
                MasterIdolNetworkCache.Covers(idol.GetPosition(),target);
        }
        internal static bool SetSwitch(ZDO idol, int expected, bool desired)
        {
            SwitchDenial="Благословення цього ідола ще не закріпилося у світі.";
            MasterIdolWorldIndex.Observe(idol); Refresh();
            SwitchFailure="not-server";if(ZNet.instance?.IsServer()!=true)return false;
            SwitchFailure=ConfirmationReason(idol);if(SwitchFailure!="ok")return false;
            SwitchFailure="switch-journal-not-ready";if(Switches?.Ready!=true)return false;
            SwitchFailure="switch-load-deferred";if(Switches.LoadDeferred)return false;
            SwitchFailure="switch-legacy-pending";if(Switches.LegacyPending)return false;
            SwitchFailure="disabled";if(desired&&!GoldCraftingService.Enabled){SwitchDenial="Вьолундр поки не дарує майстерням своїх благословень.";return false;}
            SwitchFailure="workbench-missing";if(desired&&!MasterIdolWorldIndex.HasWorkbench(idol.GetPosition())){SwitchDenial="Постав ідол поблизу верстака, щоб його благословення знайшло майстерню.";return false;}
            if(desired)
            {
                RebuildQuota();SwitchFailure="network-missing";if(!MasterIdolNetworkCache.TryKey(idol.GetPosition(),out var key))return false;
                int count=0;foreach(string id in Effective)if(id!=idol.m_uid.ToString()&&WorldObjects.TryGetValue(id,out var other)&&MasterIdolNetworkCache.TryKey(other.GetPosition(),out var group)&&group==key)
                {
                    if(Profile(other.GetPrefab())?.Id==Profile(idol.GetPrefab()).Id){SwitchFailure="duplicate-biome";SwitchDenial="Цей край уже дарує майстерні своє благословення. Обери ідол іншого краю.";return false;}
                    count++;
                }
                if(count>=MasterIdolNetworkQuota.Limit){SwitchFailure="network-cap";SwitchDenial="У цій майстерні вже діють два ідоли. Спершу вимкни один із них.";return false;}
            }
            SwitchDenial="Зачекай мить — стан ідола ще узгоджується зі світом.";
            bool saved = Switches.BeginSet(Profile(idol.GetPrefab()).Id, MasterIdolLoadIdentity.Output(idol),idol.GetString(TokenKey,""), expected, desired);
            SwitchFailure=saved?"ok":Switches.State(Profile(idol.GetPrefab()).Id,MasterIdolLoadIdentity.Output(idol),idol.GetString(TokenKey,""))!=expected?"switch-expected-mismatch":"switch-write-unavailable";
            if(saved)SwitchRequestedAt=MasterIdolTiming.Start();
            Mirror(); // pending writes retain old state until durable publication
            return saved;
        }
        private static void RebuildQuota()
        {
            var candidates=new List<MasterIdolQuotaEntry>();
            foreach(var pair in WorldObjects)
            {
                var zdo=pair.Value;var profile=Profile(zdo.GetPrefab());
                if(profile!=null&&ConfirmedActive(zdo)&&MasterIdolWorldIndex.HasWorkbench(zdo.GetPosition())&&
                    MasterIdolToggleRules.On(Switches?.State(profile.Id,MasterIdolLoadIdentity.Output(zdo),zdo.GetString(TokenKey,""))??0)&&MasterIdolNetworkCache.TryKey(zdo.GetPosition(),out var key))
                    candidates.Add(new MasterIdolQuotaEntry(pair.Key,key,profile.Id));
            }
            Effective=MasterIdolNetworkQuota.Select(candidates);
        }
        private static void ReconcileKnownOutputs()
        {
            if(Store?.Ready!=true)return;
            foreach(var record in Store.Records)
            {
                if(!record.Applied||record.Closed)continue;string[] parts=record.Output.Split(':');
                if(parts.Length!=2||!long.TryParse(parts[0],System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out long user)||
                    !uint.TryParse(parts[1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out uint id)||!ZDOID.m_userIDs.Contains(user))continue;
                var zdo=ZDOMan.instance.GetZDO(new ZDOID(user,id));
                if(zdo!=null&&Profile(zdo.GetPrefab())?.Id==record.Type&&zdo.GetString(TokenKey,"")==record.Token&&zdo.GetLong(ZDOVars.s_creator,0)==record.Player)MasterIdolWorldIndex.Observe(zdo);
            }
        }
        private static void RebuildKnown()
        {
            ProofReasons.Clear();ProofIds.Clear();ProofCursor=0;
            var records = new List<MasterIdolRecord>();
            foreach (var pair in WorldObjects)
            { var profile = Profile(pair.Value.GetPrefab()); if (profile != null){records.Add(new MasterIdolRecord(profile.Id, pair.Key));ProofIds.Add(pair.Key);} }
            Model.Rebuild(records);
        }
        internal static void Removed(ZDOID output)
        { SendPending.Remove(output); MasterIdolWorldIndex.Removed(output); WorldObjects.Remove(output.ToString()); RebuildKnown(); Mirror(); }
        private static bool ConfirmedActive(ZDO idol)
        {return ConfirmationReason(idol)=="ok";}
        private static string ConfirmationReason(ZDO idol)
        {
            if(!MasterIdolWorldIndex.Ensure())return "index-not-ready";
            if(Store?.Ready!=true)return "admission-journal-not-ready";
            if(idol==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(idol.m_uid),idol))return "output-not-current";
            var profile=Profile(idol.GetPrefab());if(profile==null)return "profile-missing";
            string id = idol.m_uid.ToString(); var admission = Store.Find(idol.GetString(TokenKey, ""));
            if(admission==null)return "admission-token-missing";
            if(!admission.Applied)return "admission-not-applied";
            if(admission.Closed)return "admission-closed";
            if(admission.Dismantling)return "admission-dismantling";
            if(admission.Type!=profile.Id)return "admission-type-mismatch";
            if(idol.GetLong(ZDOVars.s_creator,0)!=admission.Player)return "creator-mismatch";
            if(!MasterIdolLoadIdentity.Matches(admission,idol))return "admission-output-mismatch";
            var ledger=MasterIdolPlacement.CurrentLedger;if(ledger==null)return "gold-world-not-ready";
            var paid=ledger.FindIdol(World,admission.Player,admission.Token,admission.Type);
            if(paid==null&&admission.RequiresFavor)return "payment-missing";
            if(paid!=null&&paid.Phase!=GoldIdolPhase.Bound)return "payment-phase-"+paid.Phase;
            if(paid!=null&&paid.Output!=admission.Output)return "payment-output-mismatch";
            return MasterIdolFavor.Allows(admission)?"ok":"favor-proof-denied";
        }
        internal static bool EffectsSettled=>MasterIdolWorldIndex.Ready&&Reconciled&&!MirrorPending&&Store?.Ready==true&&Switches?.Ready==true&&!Switches.LoadDeferred&&!Switches.LegacyPending&&MasterIdolPlacement.CurrentLedger!=null&&MasterIdolLoadIdentity.LoadReady;
        internal static List<MasterIdolEffectZone> EffectZones()
        {
            Refresh();var result=new List<MasterIdolEffectZone>();if(!GoldCraftingService.Enabled)return result;
            foreach(string id in Effective)
            {
                if(!WorldObjects.TryGetValue(id,out var zdo)||!ConfirmedActive(zdo)||!zdo.GetBool(BoundKey,false))continue;
                string type=Profile(zdo.GetPrefab()).Id;if(type!="Meadows"&&type!="BlackForest")continue;
                var circles=MasterIdolNetworkCache.EffectCoverage(zdo.GetPosition());if(circles.Length==0)continue;
                result.Add(new MasterIdolEffectZone{Id=id+"/"+zdo.GetString(TokenKey,""),Type=type,Home=zdo.GetPosition(),Circles=circles});
            }
            result.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));return result;
        }
        internal static string Describe()
        {
            Refresh(true); var lines = new List<string>();
            foreach (var profile in MasterIdolProfiles.All)
                {
                    int count=0;foreach(string id in Effective)if(WorldObjects.TryGetValue(id,out var zdo)&&Profile(zdo.GetPrefab())?.Id==profile.Id)count++;
                    lines.Add(profile.Id+": world="+Model.Count(profile.Id)+", effective="+count+", journal-holds="+(Store?.Held(profile.Id)==true));
                }
            foreach(var pair in WorldObjects){var zdo=pair.Value;var profile=Profile(zdo.GetPrefab());if(profile!=null)lines.Add("output="+pair.Key+" type="+profile.Id+" historical="+MasterIdolLoadIdentity.Output(zdo)+" token="+zdo.GetString(TokenKey,"")+" creator="+zdo.GetLong(ZDOVars.s_creator,0)+" pos="+zdo.GetPosition()+" loadReady="+MasterIdolLoadIdentity.LoadReady+" gate="+ConfirmationReason(zdo)+" durableState="+(Switches?.State(profile.Id,MasterIdolLoadIdentity.Output(zdo),zdo.GetString(TokenKey,""))??0)+" mirroredState="+zdo.GetInt(SwitchKey,0)+" workbench="+MasterIdolWorldIndex.HasWorkbench(zdo.GetPosition()));}
            return "Master Idols: active cap2/network; "+MasterIdolWorldIndex.Diagnostics()+"; cached index=" + MasterIdolWorldIndex.Ready + ", startup scans=" + MasterIdolWorldIndex.StartupScans + "; journal=" + (Store?.Ready == true) + "; switchReady="+(Switches?.Ready==true)+" switchDeferred="+(Switches?.LoadDeferred==true)+" switchLegacy="+(Switches?.LegacyPending==true)+" goldCurrent="+(MasterIdolPlacement.CurrentLedger!=null)+" settled="+EffectsSettled+"\n" + string.Join("\n", lines);
        }
    }
}



