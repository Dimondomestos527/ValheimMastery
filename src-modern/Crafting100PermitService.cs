using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Crafting100PermitService
    {
        private const int Version = 2, Requested = 1, Applied = 2, Rejected = 3, Uncertain = 4;
        private static Func<WorkshopActor, GoldAction, bool, double> ValidateServer;
        private static Func<Player, GoldAction, int> ApplyLocal;
        private static Func<GoldAction, double> MigrationDuration;
        private static Func<Player, GoldAction, int, Func<int, bool>, int> PrepareLegacy;
        private static Func<long, string> StoragePath = ignoredWorld => Path.Combine(Paths.ConfigPath, "ValheimMasteryCrafting100", "characters-v1.bin");
        private static ZNet Session;
        private static long World, LocalId;
        private static ZDOID LocalCharacter;
        private static string Nonce;
        private static Crafting100PermitLedger Ledger;
        private static bool SnapshotEnabled, SnapshotEligible, Quarantined;
        private static float SnapshotAt, NextSync, NextFlush;
        private static long SnapshotGeneration = -1;
        private static readonly Dictionary<long, string> Validating = new Dictionary<long, string>();
        private static readonly Queue<Action> Deferred = new Queue<Action>();
        // Uncertain native save survives world switches until a fresh persistent profile load is proven.
        private static readonly HashSet<long> FaultedPlayers = new HashSet<long>();
        private static readonly Dictionary<long, long> FaultEpochs = new Dictionary<long, long>();
        private static readonly Dictionary<long, long> KnownEpochs = new Dictionary<long, long>();
        private sealed class ApplicationContext
        { internal ZNet Session; internal long World, Player; internal ZDOID Character; internal string Nonce, Encoded, OriginRaw; }
        private static ApplicationContext Applying;
        private static ApplicationContext Preparing;
        private sealed class ClientChannel { internal string Nonce; internal ZRpc Rpc; internal ZDOID Character; internal float At; }
        private static readonly Dictionary<long, ClientChannel> ClientChannels = new Dictionary<long, ClientChannel>();
        private sealed class Clock { internal float At; internal ZDOID Character; }
        private static Dictionary<long, Clock> Clocks = new Dictionary<long, Clock>();
        internal static double CraftRemaining { get; private set; }
        internal static double ForgeRemaining { get; private set; }
        internal static string PendingToken { get; private set; } = "";
        private static string JournalKey => "VM_C100_Receipt_v1_" + World;
        private static string LinkKey => "VM_C100_LegacyLink_v2_" + World;
        private static string LegacyKey => "VM_Gold_Receipt_" + World;
        internal static string RecoveryReason { get; private set; } = "";
        private static bool Enabled => GoldCraftingService.CraftingEnabled;
        private static bool CurrentLocal => Session != null && ReferenceEquals(Session, ZNet.instance) && Session.GetWorld() != null &&
            World == Session.GetWorldUID() && Player.m_localPlayer != null && LocalId == Player.m_localPlayer.GetPlayerID() &&
            LocalCharacter != ZDOID.None && LocalCharacter == Player.m_localPlayer.m_nview?.GetZDO()?.m_uid;
        internal static bool Ready => Enabled && CurrentLocal && SnapshotEnabled && SnapshotEligible && !Quarantined &&
            ApplyLocal != null && Time.unscaledTime >= SnapshotAt && Time.unscaledTime - SnapshotAt <= 8f &&
            (Session.IsServer() || NetworkSync.HasServerSettings);
        internal static bool Outstanding => ReadReceipt(Player.m_localPlayer, out _, out _) || !string.IsNullOrEmpty(PendingToken);
        internal static bool CanStart(int kind) => Ready && !Outstanding && !GoldDivineTransactions.Outstanding &&
            !WorkshopRemoteCraft.ClientBusy && !WorkshopRecovery.Outstanding &&
            CraftRemaining == 0 && ForgeRemaining == 0 && Crafting100CooldownRules.CanStart((Crafting100CooldownChannel)kind, CraftRemaining, ForgeRemaining, false);
        internal static bool ServerBlocksLegacy(long id) => !CurrentLedger(ZNet.instance, World, Ledger) || Validating.ContainsKey(id) || Ledger.Get(id)?.Pending != null;
        private static bool LegacySafe(WorkshopActor actor) => actor?.Available == true &&
            GoldCraftingService.TryGetRecoveryLedger(Session, World, out var oldLedger) &&
            oldLedger.Get(actor.GetPlayerID()) is GoldCraftingLedger.PlayerState oldState && oldState.Pending == null && oldState.Claims.Count == 0;
        private static bool OldBinding(WorkshopActor actor, GoldAction action, string encoded, out GoldCraftingLedger oldLedger, out GoldCraftingLedger.PlayerState oldState)
        {
            oldLedger = null; oldState = null;
            if (actor?.Available != true || !GoldCraftingService.TryGetRecoveryLedger(Session, World, out oldLedger)) return false;
            oldState = oldLedger.Get(actor.GetPlayerID());
            return oldState?.Pending != null && oldState.Pending.GuidToken == action.Token && oldState.Pending.StationString == encoded && !oldState.SettledTokens.Contains(action.Token) && oldState.Claims.Count == 0;
        }
        private static string MigrationNonce(WorkshopActor actor)
        {
            if (actor.Rpc == null) return CurrentLocal && actor.GetPlayerID() == LocalId && actor.CharacterId == LocalCharacter ? Nonce : null;
            return ClientChannels.TryGetValue(actor.GetPlayerID(), out var channel) && channel.Rpc == actor.Rpc && channel.Character == actor.CharacterId &&
                Time.unscaledTime - channel.At >= 0 && Time.unscaledTime - channel.At <= 8f ? channel.Nonce : null;
        }
        // Returns false ONLY for an already settled saved terminal that may use the old replay ACK.
        // Every unfinished legacy request is fenced from old Commit/Apply under this patch.
        internal static bool HandleLegacy(ZRpc rpc, WorkshopActor actor, GoldAction action, string encoded, int phase)
        {
            if (action?.WireVersion != 1 || !CurrentLedger(ZNet.instance, World, Ledger) || MigrationDuration == null || PrepareLegacy == null || actor?.Available != true) return true;
            string nonce = MigrationNonce(actor); if (nonce == null) return true;
            var state = Ledger.Get(actor.GetPlayerID());
            if (state.Results.TryGetValue(action.Token, out var terminal))
            {
                if (terminal.World == World && terminal.Encoded == encoded && terminal.OriginPhase != 0 && FinishMigration(actor, action, terminal))
                    Reply(rpc, actor, nonce, 2, encoded, terminal.OriginRaw);
                else Reply(rpc, actor, nonce, 8, encoded, "", "legacy-terminal-conflict");
                Snapshot(rpc, actor, nonce); return true;
            }
            if (!OldBinding(actor, action, encoded, out var oldLedger, out var oldState))
            {
                if (oldState?.SettledTokens.Contains(action.Token) == true && (phase == Applied || phase == Rejected)) return false;
                Reply(rpc, actor, nonce, 8, encoded, "", "legacy-proof-unresolved"); return true;
            }
            if (phase < 1 || phase > 3) return true;
            if (state.Pending != null)
            {
                var pending = state.Pending;
                if (pending.Token != action.Token || pending.Encoded != encoded || pending.World != World || pending.OriginPhase == 0)
                { Reply(rpc, actor, nonce, 8, encoded, "", "legacy-pending-conflict"); return true; }
                Reply(rpc, actor, nonce, pending.Admitted ? 1 : 5, encoded, pending.OriginRaw); return true;
            }
            Reply(rpc, actor, nonce, 5, encoded, phase + ":" + encoded); return true;
        }
        private static bool FinishMigration(WorkshopActor actor, GoldAction action, Crafting100PermitLedger.Terminal terminal)
        {
            if (terminal?.OriginPhase == 0 || terminal == null || terminal.World != World || terminal.Encoded != action.Encode() ||
                !GoldCraftingService.TryGetRecoveryLedger(Session, World, out var oldLedger)) return false;
            var old = oldLedger.Get(actor.GetPlayerID());
            if (old == null || old.SettledTokens.Contains(action.Token)) return false;
            if (old.Pending == null) return true; // Only a durable migration terminal proves prior exact cleanup.
            if (old.Pending.GuidToken != action.Token || old.Pending.StationString != terminal.Encoded) return false;
            float before = old.Favor;
            if (!oldLedger.Reject(actor.GetPlayerID(), action.Token)) return false;
            var after = oldLedger.Get(actor.GetPlayerID());
            if (after?.Pending != null || after == null || after.Favor != before) return false;
            GoldCraftingService.NotifyFavor(actor.GetPlayerID()); return true;
        }
        private static void SendMigrationProof(GoldAction action, int originPhase, string originRaw, int outcome)
        {
            if (!CurrentLocal) return;
            try
            {
                var p = Header(); p.Write(10); p.Write(action.Encode()); p.Write(originPhase); p.Write(originRaw); p.Write(outcome);
                if (Session.IsServer()) { p.SetPos(0); Receive(null, p); } else Session.GetServerRPC()?.Invoke("VM_C100_Request", p);
            }
            catch (Exception e) { Log("Migration transport deferred", e); }
        }
        private static void ReceiveMigrationProof(ZRpc rpc, WorkshopActor actor, string nonce, GoldAction action, string encoded, int originPhase, string originRaw, int outcome)
        {
            if (action?.WireVersion != 1 || MigrationNonce(actor) != nonce) return;
            long id = actor.GetPlayerID(); var state = Ledger.Get(id);
            if (OriginPhase(originRaw, encoded) != originPhase || originPhase == 0 || outcome < 1 || outcome > 3 ||
                (originPhase == 2 ? outcome != 2 : originPhase == 3 ? outcome != 3 : outcome == 2)) return;
            if (state.Results.TryGetValue(action.Token, out var terminal))
            {
                if (terminal.World == World && terminal.Encoded == encoded && terminal.OriginPhase == originPhase && terminal.OriginRaw == originRaw &&
                    terminal.Phase == outcome && FinishMigration(actor, action, terminal)) Reply(rpc, actor, nonce, 2, encoded, originRaw);
                Snapshot(rpc, actor, nonce); return;
            }
            if (!OldBinding(actor, action, encoded, out _, out _)) { Reply(rpc, actor, nonce, 8, encoded, originRaw, "legacy-binding-lost"); return; }
            if (outcome == Rejected)
            {
                if (Ledger.RecordMigrationRejected(id, action.Token, encoded, World, originPhase, originRaw) &&
                    FinishMigration(actor, action, Ledger.Get(id).Results[action.Token])) Reply(rpc, actor, nonce, 2, encoded, originRaw);
                Snapshot(rpc, actor, nonce); return;
            }
            double duration = MigrationDuration?.Invoke(action) ?? 0;
            if (!ValidDuration(duration)) { Reply(rpc, actor, nonce, 8, encoded, originRaw, "legacy-recipe-unresolved"); return; }
            if (state.Pending != null && (state.Pending.Token != action.Token || state.Pending.Encoded != encoded || state.Pending.World != World || state.Pending.OriginPhase != originPhase || state.Pending.OriginRaw != originRaw)) return;
            if (state.Pending == null && !Ledger.PrepareMigration(id, new Crafting100PermitLedger.Permit { Token = action.Token, Encoded = encoded,
                World = World, Kind = action.Kind, Duration = duration, OriginPhase = originPhase, OriginRaw = originRaw, Admitted = false })) return;
            if (outcome == Applied)
            {
                if (Ledger.Settle(id, action.Token, encoded, World, Applied) && FinishMigration(actor, action, Ledger.Get(id).Results[action.Token])) Reply(rpc, actor, nonce, 2, encoded, originRaw);
                Snapshot(rpc, actor, nonce); return;
            }
            RequestMigrated(rpc, actor, nonce, action, encoded);
        }
        private static void RequestMigrated(ZRpc rpc, WorkshopActor actor, string nonce, GoldAction action, string encoded)
        {
            var pending = Ledger.Get(actor.GetPlayerID())?.Pending;
            if (pending?.OriginPhase != 1 || pending.Encoded != encoded || pending.Token != action.Token || pending.World != World ||
                !OldBinding(actor, action, encoded, out _, out _) || !Enabled || ValidateServer == null) return;
            var state = Ledger.Get(actor.GetPlayerID());
            if (!pending.Admitted && !Crafting100CooldownRules.CanStart((Crafting100CooldownChannel)pending.Kind, state.Craft, state.Forge, false))
            { Snapshot(rpc, actor, nonce); return; } // Wait for existing clock, never overwrite it.
            if (!ValidDuration(ValidateServer(actor, action, false))) { Reply(rpc, actor, nonce, 0, encoded, pending.OriginRaw); return; }
            long id = actor.GetPlayerID(); if (Validating.ContainsKey(id)) return; Validating.Add(id, encoded);
            ZNet session = Session; long world = World; var ledger = Ledger;
            try
            {
                WorkshopStationProof.Acquire(actor, action.Token, action.Station, () =>
                {
                    if (!CurrentLedger(session, world, ledger)) return; RemoveValidation(id, encoded);
                    var current = WorkshopActor.Resolve(rpc);
                    if (current?.Available != true || current.CharacterId != actor.CharacterId || current.GetPlayerID() != id || !Enabled ||
                        !OldBinding(current, action, encoded, out _, out _) || !ValidDuration(ValidateServer(current, action, true)))
                    { if (current?.Available == true) Reply(rpc, current, nonce, 0, encoded, pending.OriginRaw); return; }
                    if (ledger.ActivateMigration(id, action.Token, encoded, world)) Reply(rpc, current, nonce, 1, encoded, pending.OriginRaw);
                    Snapshot(rpc, current, nonce);
                }, error => { if (!CurrentLedger(session, world, ledger)) return; RemoveValidation(id, encoded); Reply(rpc, actor, nonce, 0, encoded, pending.OriginRaw); });
            }
            catch (Exception e) { RemoveValidation(id, encoded); Log("Migration proof deferred", e); }
        }
        internal static void Configure(Func<WorkshopActor, GoldAction, bool, double> validateServer, Func<Player, GoldAction, int> applyLocal,
            Func<GoldAction, double> migrationDuration = null, Func<Player, GoldAction, int, Func<int, bool>, int> prepareLegacy = null)
        { ValidateServer = validateServer; ApplyLocal = applyLocal; MigrationDuration = migrationDuration; PrepareLegacy = prepareLegacy; }
        internal static bool IsLinkedLegacy(Player player, GoldAction action)
        {
            var context = Preparing ?? Applying;
            if (context == null || !ContextCurrent(context) || player != Player.m_localPlayer || context.Encoded != action?.Encode() || string.IsNullOrEmpty(context.OriginRaw)) return false;
            return player.m_customData.TryGetValue(LegacyKey, out string old) && old == context.OriginRaw;
        }
        private static bool ContextCurrent(ApplicationContext context) => context != null && CurrentLocal && context.Session == Session &&
            context.World == World && context.Player == LocalId && context.Character == LocalCharacter && context.Nonce == Nonce;
        private static int OriginPhase(string raw, string encoded)
        { for (int phase = 1; phase <= 3; phase++) if (raw == phase + ":" + encoded) return phase; return 0; }
        // Root-approved same-host character store; world-bound pending permits are distinct.
        // Cross-host timer authenticity/transfer is not implemented by this local store.
        internal static void ConfigureStorage(Func<long, string> storagePath)
        { if (Ledger != null || Session?.GetWorld() != null) throw new InvalidOperationException("Configure Crafting100 storage before session"); StoragePath = storagePath; }
        internal static void Register(ZRpc rpc)
        { rpc.Register<ZPackage>("VM_C100_Request", Receive); rpc.Register<ZPackage>("VM_C100_Grant", Grant); rpc.Register<ZPackage>("VM_C100_State", State); }
        internal static bool TryRequest(GoldAction action)
        {
            if (action?.WireVersion != 2 || !CanStart(action.Kind)) return false;
            try
            {
                string encoded = action.Encode(); GoldAction.Decode(encoded);
                if (!WriteReceipt(Player.m_localPlayer, action, Requested)) return false;
                OwnerSkillAuthority.SendNow(); Send(action, Requested); return true;
            }
            catch (Exception e) { Log("Request retained/fail closed", e); return false; }
        }
        internal static bool TrySaveApplied(Player player, GoldAction action) => ApplicationCurrent(action) && SaveOutcome(player, action, Applied);
        internal static bool TrySaveRejected(Player player, GoldAction action) => SaveOutcome(player, action, Rejected);
        private static bool SaveOutcome(Player player, GoldAction action, int phase)
        {
            if (!CurrentLocal || player != Player.m_localPlayer || Quarantined || !ReadReceipt(player, out var saved, out int old) ||
                saved == null || saved.Encode() != action?.Encode() || (old != Requested && !(old == Uncertain && action.WireVersion == 2)) || Applying != null && !ApplicationCurrent(action)) return false;
            return WriteReceipt(player, action, phase);
        }
        private static bool ApplicationCurrent(GoldAction action) => ContextCurrent(Applying) && Applying.Encoded == action?.Encode() &&
            (string.IsNullOrEmpty(Applying.OriginRaw) || Linked(Player.m_localPlayer, Applying.Encoded, Applying.OriginRaw));
        // Private V2 RAM fence, never a server phase or another profile checkpoint.
        // Ordinary saves may capture this unresolved receipt with a partial batch.
        private static bool FenceV2Application(Player player, GoldAction action)
        {
            if (action?.WireVersion != 2 || !ApplicationCurrent(action) || player != Player.m_localPlayer ||
                !ReadReceipt(player, out var saved, out int phase) || phase != Requested || saved?.Encode() != action.Encode()) return false;
            player.m_customData[JournalKey] = Uncertain + ":" + action.Encode();
            return true;
        }
        private static bool ReadReceipt(Player player, out GoldAction action, out int phase)
        {
            action = null; phase = 0;
            if (player == null || !player.m_customData.TryGetValue(JournalKey, out string raw)) return false;
            try
            { int split = raw.IndexOf(':'); phase = int.Parse(raw.Substring(0, split)); action = GoldAction.Decode(raw.Substring(split + 1)); if (phase < Requested || (phase > Rejected && !(phase == Uncertain && action.WireVersion == 2))) throw new FormatException(); return true; }
            catch { phase = -1; action = null; return true; }
        }
        private static bool WriteReceipt(Player player, GoldAction action, int phase)
        {
            string key = JournalKey; ZNet session = Session; long world = World, id = LocalId; ZDOID character = LocalCharacter; string nonce = Nonce;
            bool existed = player.m_customData.TryGetValue(key, out string old);
            player.m_customData[key] = phase + ":" + action.Encode();
            bool persisted = false;
            try
            {
                persisted = GoldCharacterSave.Persist(player) && CurrentLocal && session == Session && world == World && id == LocalId &&
                    character == LocalCharacter && nonce == Nonce && (Applying == null || ApplicationCurrent(action));
                return persisted;
            }
            finally { if (!persisted) { if (existed) player.m_customData[key] = old; else player.m_customData.Remove(key); } }
        }
        private static ZPackage Header()
        { var p = new ZPackage(); p.Write(Version); p.Write(World); p.Write(LocalId); p.Write(LocalCharacter); p.Write(Nonce); return p; }
        private static void Send(GoldAction action, int phase)
        {
            if (!CurrentLocal || string.IsNullOrEmpty(Nonce) || action != null && (phase < Requested || phase > Rejected)) return;
            try
            {
                var p = Header(); p.Write(phase); p.Write(action?.Encode() ?? "");
                if (Session.IsServer()) { p.SetPos(0); Receive(null, p); } else Session.GetServerRPC()?.Invoke("VM_C100_Request", p);
            }
            catch (Exception e) { Log("Transport deferred", e); }
        }
        private static bool CurrentLedger(ZNet expected, long world, Crafting100PermitLedger ledger) => expected != null && ReferenceEquals(expected, Session) &&
            ReferenceEquals(expected, ZNet.instance) && expected.IsServer() && expected.GetWorld() != null && expected.GetWorldUID() == world && world == World &&
            ReferenceEquals(ledger, Ledger) && ledger?.IsAvailable == true;
        private static void Receive(ZRpc rpc, ZPackage p)
        {
            if (p == null || p.Size() > 4096 || !CurrentLedger(ZNet.instance, World, Ledger)) return;
            var actor = WorkshopActor.Resolve(rpc); if (actor?.Available != true) return;
            bool ownsValidation = false;
            string ownedEncoded = null;
            try
            {
                if (p.ReadInt() != Version || p.ReadLong() != World || p.ReadLong() != actor.GetPlayerID() || p.ReadZDOID() != actor.CharacterId) return;
                string nonce = p.ReadString(); if (!Guid.TryParseExact(nonce, "N", out _)) return;
                int phase = p.ReadInt(); string encoded = p.ReadString(); long id = actor.GetPlayerID();
                if (phase == 10)
                {
                    int originPhase = p.ReadInt(); string originRaw = p.ReadString(); int outcome = p.ReadInt();
                    if (p.GetPos() != p.Size()) return;
                    ReceiveMigrationProof(rpc, actor, nonce, GoldAction.Decode(encoded), encoded, originPhase, originRaw, outcome); return;
                }
                if (p.GetPos() != p.Size()) return;
                if (phase == 0)
                {
                    ClientChannels[id] = new ClientChannel { Nonce = nonce, Rpc = rpc, Character = actor.CharacterId, At = Time.unscaledTime };
                    Snapshot(rpc, actor, nonce); return;
                }
                GoldAction a = GoldAction.Decode(encoded); var state = Ledger.Get(id);
                if (state.Results.TryGetValue(a.Token, out var terminal))
                {
                    // A contradictory late terminal never clears the other saved outcome.
                    if (terminal.World == World && terminal.Encoded == encoded && (phase == Requested || phase == terminal.Phase) &&
                        (terminal.OriginPhase == 0 || FinishMigration(actor, a, terminal)))
                        Reply(rpc, actor, nonce, 2, encoded, terminal.OriginRaw);
                    Snapshot(rpc, actor, nonce); return;
                }
                if (state.Pending != null && (state.Pending.Token != a.Token || state.Pending.Encoded != encoded || state.Pending.World != World))
                { Reply(rpc, actor, nonce, 0, encoded); return; }
                if (phase == Applied || phase == Rejected)
                {
                    if (state.Pending != null && Ledger.Settle(id, a.Token, encoded, World, phase))
                    {
                        var result = Ledger.Get(id).Results[a.Token];
                        // Observer feedback is optional and follows the first durable normal settlement.
                        if (phase == Applied && result.OriginPhase == 0)
                        { try { ObserverCueNetwork.PublishCraftingApplied(actor, a); } catch { } }
                        if (result.OriginPhase == 0 || FinishMigration(actor, a, result)) Reply(rpc, actor, nonce, 2, encoded, result.OriginRaw);
                    }
                    else if (phase == Rejected && state.Pending == null)
                    {
                        // Only a matching in-flight proof needs a durable cancel fence.
                        // Unsolicited unknown rejection cannot inflate the persistent replay store.
                        bool inFlight = Validating.TryGetValue(id, out string validating) && validating == encoded;
                        if (!inFlight || Ledger.RejectUnadmitted(id, a.Token, encoded, World)) Reply(rpc, actor, nonce, 2, encoded);
                    }
                    Snapshot(rpc, actor, nonce); return;
                }
                if (phase != Requested || !Enabled || ValidateServer == null || !GoldCraftingService.HasGold(actor) || actor.IsDead() || actor.IsTeleporting()) return;
                if (state.Pending?.OriginPhase != 0 && state.Pending != null) { RequestMigrated(rpc, actor, nonce, a, encoded); return; }
                // V1 is recovery only. A fresh quality-5 request cannot enter the new design.
                if (a.WireVersion == 1 && (state.Pending?.Admitted != true || state.Pending.Token != a.Token || state.Pending.Encoded != encoded || state.Pending.World != World))
                { Reply(rpc, actor, nonce, 0, encoded); return; }
                if (!LegacySafe(actor)) { Reply(rpc, actor, nonce, 0, encoded); return; }
                double duration = ValidateServer(actor, a, false);
                if (!ValidDuration(duration) || state.Pending == null && (state.SharedRemaining != 0 || !Crafting100CooldownRules.CanStart((Crafting100CooldownChannel)a.Kind, state.Craft, state.Forge, false)))
                { Reply(rpc, actor, nonce, 0, encoded); return; }
                if (Validating.ContainsKey(id)) return;
                Validating.Add(id, encoded);
                ownsValidation = true;
                ownedEncoded = encoded;
                ZNet session = Session; long world = World; var ledger = Ledger;
                WorkshopStationProof.Acquire(actor, a.Token, a.Station, () =>
                {
                    if (!CurrentLedger(session, world, ledger)) return; RemoveValidation(id, encoded);
                    var current = WorkshopActor.Resolve(rpc);
                    if (!Enabled || current?.Available != true || current.CharacterId != actor.CharacterId || current.GetPlayerID() != id ||
                        current.IsDead() || current.IsTeleporting() || !GoldCraftingService.HasGold(current)) return;
                    if (!LegacySafe(current)) { Reply(rpc, current, nonce, 0, encoded); return; }
                    double approved = ValidateServer?.Invoke(current, a, true) ?? 0;
                    var latest = ledger.Get(id);
                    if (!ValidDuration(approved) || (latest.Pending != null && (latest.Pending.Token != a.Token || latest.Pending.Encoded != encoded || latest.Pending.World != world)))
                    { Reply(rpc, current, nonce, 0, encoded); return; }
                    // Existing reservation keeps the originally admitted duration, never a new tier/config.
                    var permit = latest.Pending ?? new Crafting100PermitLedger.Permit { Token = a.Token, Encoded = encoded, World = world, Kind = a.Kind, Duration = approved };
                    if (ledger.Admit(id, permit))
                    {
                        // Existing pending/recovery reservations never acquire a fresh cosmetic stamp.
                        if (latest.Pending == null)
                        { try { ObserverCueNetwork.MarkCraftingAdmitted(current, a); } catch { } }
                        Reply(rpc, current, nonce, 1, encoded);
                    }
                    Snapshot(rpc, current, nonce);
                }, error =>
                { if (!CurrentLedger(session, world, ledger)) return; RemoveValidation(id, encoded); Reply(rpc, actor, nonce, 0, encoded); });
            }
            catch (Exception e) { if (ownsValidation) RemoveValidation(actor.GetPlayerID(), ownedEncoded); Log("Invalid request", e); }
        }
        private static void RemoveValidation(long id, string encoded)
        { if (Validating.TryGetValue(id, out string current) && current == encoded) Validating.Remove(id); }
        private static bool ValidDuration(double duration) => !double.IsNaN(duration) && !double.IsInfinity(duration) && duration > 0 && duration <= Crafting100CooldownRules.MaximumSeconds;
        private static ZPackage ReplyHeader(WorkshopActor actor, string nonce)
        { var p = new ZPackage(); p.Write(Version); p.Write(World); p.Write(actor.GetPlayerID()); p.Write(actor.CharacterId); p.Write(nonce); return p; }
        private static void Reply(ZRpc rpc, WorkshopActor actor, string nonce, int kind, string encoded, string originRaw = "", string reason = "")
        {
            var p = ReplyHeader(actor, nonce); p.Write(kind); p.Write(encoded); p.Write(originRaw ?? ""); p.Write(reason ?? "");
            try { if (rpc == null) { p.SetPos(0); Grant(null, p); } else rpc.Invoke("VM_C100_Grant", p); }
            catch (Exception e) { Log("Grant transport deferred", e); }
        }
        private static void Snapshot(ZRpc rpc, WorkshopActor actor, string nonce)
        {
            var state = Ledger?.Get(actor.GetPlayerID()); if (state == null) return;
            var p = ReplyHeader(actor, nonce); p.Write(Enabled && ValidateServer != null && LegacySafe(actor)); p.Write(GoldCraftingService.HasGold(actor));
            p.Write(state.Generation); p.Write(state.SharedRemaining); p.Write(state.SharedRemaining); p.Write(state.Pending?.Token ?? "");
            try { if (rpc == null) { p.SetPos(0); State(null, p); } else rpc.Invoke("VM_C100_State", p); }
            catch (Exception e) { Log("Snapshot deferred", e); }
        }
        private static bool ReplyValid(ZRpc rpc, ZPackage p) => CurrentLocal && p != null && p.Size() <= 4096 &&
            (rpc == null ? Session.IsServer() : !Session.IsServer() && rpc == Session.GetServerRPC()) &&
            p.ReadInt() == Version && p.ReadLong() == World && p.ReadLong() == LocalId && p.ReadZDOID() == LocalCharacter && p.ReadString() == Nonce;
        private static void State(ZRpc rpc, ZPackage p)
        {
            try
            {
                if (!ReplyValid(rpc, p)) return;
                bool enabled = p.ReadBool(), eligible = p.ReadBool(); long generation = p.ReadLong(); double craft = p.ReadDouble(), forge = p.ReadDouble(); string pending = p.ReadString();
                if (p.GetPos() != p.Size() || generation < 0 || generation < SnapshotGeneration || !Crafting100CooldownRules.ValidRemaining(craft) || !Crafting100CooldownRules.ValidRemaining(forge) ||
                    pending != "" && !Guid.TryParseExact(pending, "N", out _)) return;
                SnapshotEnabled = enabled; SnapshotEligible = eligible; SnapshotGeneration = generation; CraftRemaining = craft; ForgeRemaining = forge; PendingToken = pending; SnapshotAt = Time.unscaledTime;
            }
            catch (Exception e) { Log("Invalid snapshot", e); }
        }
        private static void Grant(ZRpc rpc, ZPackage p)
        {
            try
            {
                if (!ReplyValid(rpc, p) || Deferred.Count >= 8) return;
                int kind = p.ReadInt(); string encoded = p.ReadString(); GoldAction action = GoldAction.Decode(encoded);
                string originRaw = p.ReadString(), reason = p.ReadString();
                if (p.GetPos() != p.Size()) return;
                ZNet session = Session; long world = World, id = LocalId; ZDOID character = LocalCharacter; string nonce = Nonce;
                Deferred.Enqueue(() =>
                {
                    try
                    {
                    if (!CurrentLocal || Quarantined || session != Session || world != World || id != LocalId || character != LocalCharacter || nonce != Nonce) return;
                    Player player = Player.m_localPlayer;
                    if (kind == 8) { RecoveryReason = reason; FaultActor(id); return; }
                    if (kind == 5) { PrepareMigration(player, action, originRaw); return; }
                    if (!string.IsNullOrEmpty(originRaw) && !Linked(player, encoded, originRaw)) { RecoveryReason = "legacy-link-mismatch"; FaultActor(id); return; }
                    if (!ReadReceipt(player, out var saved, out int phase) || saved == null || saved.Encode() != encoded) return;
                    GoldReceiptDecision decision = GoldReceiptPolicy.Decide(kind, phase, true);
                    if (decision == GoldReceiptDecision.ClearSettled)
                    {
                        // Terminal ACK is only allowed to clear a saved terminal; Requested ACK would erase uncertainty.
                        if (phase != Applied && phase != Rejected) return;
                        string key = JournalKey, old = player.m_customData[key];
                        string linkKey = LinkKey, legacyKey = LegacyKey;
                        bool linked = !string.IsNullOrEmpty(originRaw);
                        player.m_customData.Remove(key);
                        if (linked) { player.m_customData.Remove(linkKey); player.m_customData.Remove(legacyKey); }
                        bool cleared = false;
                        try { cleared = GoldCharacterSave.Persist(player) && CurrentLocal && session == Session && world == World && id == LocalId && character == LocalCharacter && nonce == Nonce; }
                        finally
                        {
                        if (!cleared)
                        {
                            player.m_customData[key] = old;
                            if (linked) { player.m_customData[linkKey] = originRaw; player.m_customData[legacyKey] = originRaw; }
                            FaultActor(id);
                        }
                        }
                        NextSync = 0; return;
                    }
                    if (decision == GoldReceiptDecision.ResendReceipt) { Send(saved, phase); return; }
                    if (decision == GoldReceiptDecision.Reject)
                    { if (TrySaveRejected(player, saved)) Send(saved, Rejected); else FaultActor(id); return; }
                    if (decision != GoldReceiptDecision.ApplyOnce || !Enabled || ApplyLocal == null) return;
                    var context = new ApplicationContext { Session = session, World = world, Player = id, Character = character, Nonce = nonce, Encoded = encoded, OriginRaw = originRaw };
                    KnownEpochs[id] = Crafting100LegacySaveProof.RecordedEpoch(player);
                    Applying = context; int result;
                    try
                    {
                        if (action.WireVersion == 2 && !FenceV2Application(player, action)) { FaultActor(id); return; }
                        result = ApplyLocal(player, action);
                    }
                    finally { if (ReferenceEquals(Applying, context)) Applying = null; }
                    if (CurrentLocal && session == Session && world == World && id == LocalId && character == LocalCharacter && nonce == Nonce &&
                        (result == Applied || result == Rejected) && ReadReceipt(player, out var outcome, out int outcomePhase) &&
                        outcome != null && outcome.Encode() == encoded && outcomePhase == result) Send(outcome, result);
                    else FaultActor(id);
                    }
                    catch (Exception e)
                    { FaultActor(id); Log("Originating action outcome uncertain", e); }
                });
            }
            catch (Exception e) { Log("Invalid grant", e); }
        }
        private static bool Linked(Player player, string encoded, string originRaw) => OriginPhase(originRaw, encoded) != 0 &&
            player.m_customData.TryGetValue(LinkKey, out string link) && link == originRaw &&
            player.m_customData.TryGetValue(LegacyKey, out string old) && old == originRaw;
        private static void FaultActor(long id)
        {
            FaultedPlayers.Add(id);
            if (!FaultEpochs.ContainsKey(id)) FaultEpochs[id] = KnownEpochs.TryGetValue(id, out long epoch) ? epoch :
                Player.m_localPlayer?.GetPlayerID() == id ? Crafting100LegacySaveProof.RecordedEpoch(Player.m_localPlayer) : long.MaxValue;
            Quarantined = FaultedPlayers.Contains(LocalId);
        }
        private static void PrepareMigration(Player player, GoldAction action, string originRaw)
        {
            if (action?.WireVersion != 1) { FaultActor(player.GetPlayerID()); return; }
            string encoded = action.Encode(); int originPhase = OriginPhase(originRaw, encoded);
            if (originPhase == 0 || PrepareLegacy == null || !player.m_customData.TryGetValue(LegacyKey, out string original) || original != originRaw) return;
            if (ReadReceipt(player, out var existing, out int existingPhase))
            {
                if (existing != null && existing.Encode() == encoded && Linked(player, encoded, originRaw) &&
                    (originPhase == 1 ? existingPhase == 1 || existingPhase == 3 : existingPhase == originPhase))
                    SendMigrationProof(action, originPhase, originRaw, existingPhase);
                else { RecoveryReason = "legacy-existing-journal-conflict"; FaultActor(LocalId); }
                return;
            }
            if (GoldDivineTransactions.IsReceiptQuarantined(player) || !Crafting100LegacySaveProof.Matches(player, action, originPhase))
            { RecoveryReason = "legacy-persistent-load-proof-missing"; FaultActor(LocalId); return; }
            var context = new ApplicationContext { Session = Session, World = World, Player = LocalId, Character = LocalCharacter,
                Nonce = Nonce, Encoded = encoded, OriginRaw = originRaw };
            Preparing = context;
            KnownEpochs[context.Player] = Crafting100LegacySaveProof.RecordedEpoch(player);
            int outcome;
            try
            {
                outcome = PrepareLegacy(player, action, originPhase, phase => SaveLinkedProof(player, action, originRaw, originPhase, phase, context));
            }
            finally { if (ReferenceEquals(Preparing, context)) Preparing = null; }
            if (ContextCurrent(context) && outcome >= 1 && outcome <= 3 && Linked(player, encoded, originRaw) &&
                ReadReceipt(player, out var saved, out int phaseSaved) && saved?.Encode() == encoded && phaseSaved == outcome)
                SendMigrationProof(action, originPhase, originRaw, outcome);
            else { RecoveryReason = "legacy-action-proof-unresolved"; FaultActor(context.Player); }
        }
        private static bool SaveLinkedProof(Player player, GoldAction action, string originRaw, int originPhase, int phase, ApplicationContext context)
        {
            if (!ContextCurrent(context) || player != Player.m_localPlayer ||
                (originPhase == 1 ? phase != 1 && phase != 3 : phase != originPhase) ||
                !player.m_customData.TryGetValue(LegacyKey, out string original) || original != originRaw || ReadReceipt(player, out _, out _)) return false;
            string key = JournalKey, linkKey = LinkKey;
            bool hadLink = player.m_customData.TryGetValue(linkKey, out string oldLink);
            if (hadLink) return false;
            player.m_customData[key] = phase + ":" + action.Encode(); player.m_customData[linkKey] = originRaw;
            bool persisted = false;
            try { persisted = GoldCharacterSave.Persist(player); }
            finally
            {
                if (!persisted || !ContextCurrent(context) || !player.m_customData.TryGetValue(LegacyKey, out string after) || after != originRaw)
                { player.m_customData.Remove(key); if (hadLink) player.m_customData[linkKey] = oldLink; else player.m_customData.Remove(linkKey); persisted = false; }
            }
            return persisted;
        }
        internal static void Tick()
        {
            long world = ZNet.instance?.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
            if (Session != ZNet.instance || World != world)
            {
                Shutdown(); Session = ZNet.instance; World = world;
                if (Session?.IsServer() == true && Session.GetWorld() != null && StoragePath != null)
                { try { Ledger = new Crafting100PermitLedger(StoragePath(World)); if (!Ledger.Load()) MasteryPlugin.Log.LogError("[Crafting100] Store unavailable; actions closed"); } catch (Exception e) { Log("Storage unavailable", e); } }
            }
            if (Session?.GetWorld() == null) return;
            Player local = Player.m_localPlayer; long id = local?.GetPlayerID() ?? 0; ZDOID character = local?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None;
            if (id != LocalId || character != LocalCharacter)
            {
                LocalId = id; LocalCharacter = character; Nonce = Guid.NewGuid().ToString("N"); SnapshotEnabled = SnapshotEligible = false; Quarantined = FaultedPlayers.Contains(id);
                RecoveryReason = ""; // Presentation belongs to this local identity; persistent fault sets remain intact.
                SnapshotGeneration = -1; CraftRemaining = ForgeRemaining = 0; PendingToken = ""; NextSync = 0; Deferred.Clear();
            }
            if (FaultedPlayers.Contains(id) && FaultEpochs.TryGetValue(id, out long faultEpoch) &&
                Crafting100LegacySaveProof.RecordedEpoch(local) > faultEpoch)
            {
                long attemptedEpoch = Crafting100LegacySaveProof.RecordedEpoch(local);
                FaultEpochs[id] = attemptedEpoch; // Evaluate each fresh load once, even when its proof fails.
                if (Crafting100LegacySaveProof.CurrentEpoch(local, out long epoch) && epoch == attemptedEpoch)
                { FaultedPlayers.Remove(id); FaultEpochs.Remove(id); Quarantined = false; RecoveryReason = ""; }
            }
            if (Session.IsServer() && Ledger?.IsAvailable == true)
            {
                float now = Time.time; var online = new Dictionary<long, Clock>();
                TrackClock(WorkshopActor.Resolve(null), now, online);
                foreach (var peer in Session.GetPeers()) if (peer?.m_rpc != null) TrackClock(WorkshopActor.Resolve(peer.m_rpc), now, online);
                Clocks = online;
                if (Time.unscaledTime >= NextFlush) { NextFlush = Time.unscaledTime + 5f; Ledger.Flush(); }
            }
            if (Deferred.Count > 0) { try { Deferred.Dequeue()(); } catch (Exception e) { FaultActor(LocalId); Log("Outcome uncertain", e); } }
            if (!CurrentLocal || Time.unscaledTime < NextSync || !Session.IsServer() && !NetworkSync.HasServerSettings) return;
            NextSync = Time.unscaledTime + 2f; OwnerSkillAuthority.SendNow(); Send(null, 0);
            if (ReadReceipt(local, out var receipt, out int phase) && receipt != null)
            {
                if (phase == Uncertain) { RecoveryReason = "v2-outcome-unresolved"; FaultActor(id); }
                else if (!Quarantined && phase >= Requested && phase <= Rejected && (phase != Requested || Enabled)) Send(receipt, phase);
            }
        }
        private static void TrackClock(WorkshopActor actor, float now, Dictionary<long, Clock> online)
        {
            // Clock belongs to authenticated online gameplay, not current perk eligibility.
            // Losing skill levels, death or teleport does not erase/freeze an existing timer.
            if (actor?.Available != true || Session.GetPeers().Count == 0 && Game.IsPaused()) return;
            long id = actor.GetPlayerID(); if (online.ContainsKey(id)) return;
            if (Clocks.TryGetValue(id, out var previous) && previous.Character == actor.CharacterId)
            {
                double seconds = now - previous.At;
                // A frame stall/suspend is not evidence of active gameplay; never subtract its gap.
                if (seconds > 0 && seconds <= 1d) Ledger.Elapse(id, seconds);
            }
            online.Add(id, new Clock { At = now, Character = actor.CharacterId });
        }
        internal static void Shutdown()
        {
            Ledger?.Flush(); Ledger = null; Session = null; World = LocalId = 0; LocalCharacter = ZDOID.None; Nonce = null;
            Clocks.Clear(); Validating.Clear(); Deferred.Clear(); ClientChannels.Clear(); Preparing = Applying = null; SnapshotEnabled = SnapshotEligible = Quarantined = false;
            RecoveryReason = "";
            SnapshotGeneration = -1; CraftRemaining = ForgeRemaining = 0; PendingToken = ""; NextSync = NextFlush = 0;
        }
        private static void Log(string message, Exception error) => MasteryPlugin.Log?.LogWarning("[Crafting100] " + message + ": " + error.Message);
    }
}
