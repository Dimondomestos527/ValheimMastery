using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Phase100-A authority. Character-side actions use a separate durable receipt protocol.
    internal static class GoldCraftingService
    {
        internal const float MaxFavor = 1000, MasterworkCost = 500, UpgradeCost = 250;
        internal static bool CoreReady => CoreEnabled && (ZNet.instance?.IsServer() == true ?
            TryGetServerLedger(ZNet.instance, CurrentWorld(ZNet.instance), out _) : LocalStateCurrent && ServerCoreReady);
        internal static bool DivineActionsReady => CraftingEnabled && CoreReady &&
            (ZNet.instance?.IsServer() == true || ServerReady);
        private static bool LocalStateCurrent => Session != null && ReferenceEquals(Session, ZNet.instance) &&
            Session.GetWorld() != null && SessionWorld == Session.GetWorldUID() && FavorStateReceived &&
            Player.m_localPlayer != null && LocalPlayer == Player.m_localPlayer.GetPlayerID() &&
            Time.unscaledTime - LastStateAt >= 0f && Time.unscaledTime - LastStateAt <= 8f;
        internal static bool Unlocked { get; private set; }
        internal static float Favor { get; private set; }
        internal static double ExhaustionRemaining { get; private set; }
        internal static string ExhaustedPatron { get; private set; } = string.Empty;
        private static Dictionary<string, GoldPatronWallet> LocalWallets = new Dictionary<string, GoldPatronWallet>(StringComparer.Ordinal);
        private static readonly List<KeyValuePair<string, float>> LocalClaims = new List<KeyValuePair<string, float>>();
        internal static IReadOnlyDictionary<string, GoldPatronWallet> PatronWallets => LocalWallets;
        internal static float AvailableFavor => LocalWallets.TryGetValue(GoldCooldownPolicy.LegacyPatronId, out var wallet) ? wallet.Available : 0f;
        internal static bool AnyUnlocked
        {
            get { if (!CoreReady || !LocalStateCurrent) return false; foreach (var wallet in LocalWallets.Values) if (wallet.Unlocked) return true; return false; }
        }
        internal static bool CooldownAllows(float cost)
        {
            if (!GoldCooldownPolicy.CanStart(GoldCooldownPolicy.LegacyPatronId, cost, ExhaustedPatron, ExhaustionRemaining)) return false;
            foreach (var claim in LocalClaims)
                if (claim.Key == GoldCooldownPolicy.LegacyPatronId || claim.Value > 250f && cost > 250f) return false;
            return true;
        }
        internal static bool CanStart(float cost) => DivineActionsReady && Unlocked && AvailableFavor >= cost && CooldownAllows(cost);
        private static void ResetLocalState()
        {
            Unlocked = Armed = ServerReady = ServerCoreReady = FavorStateReceived = false;
            Favor = 0; ExhaustionRemaining = 0; ExhaustedPatron = string.Empty;
            LocalWallets.Clear(); LocalClaims.Clear();
            GoldInspirationStatus.Tick();
        }
        internal static bool Armed { get; private set; }
        private static ConfigEntry<bool> DevelopmentEnabled, PantheonEnabled;
        private static readonly Dictionary<long, bool> ServerArmed = new Dictionary<long, bool>();
        private static readonly HashSet<long> ServerHolding = new HashSet<long>();
        private static bool ServerReady, ServerCoreReady;
        private static ZNet Session; // Keep the Idol adapter's private Session contract.
        private static long SessionWorld, LoadedWorld;
        private static GoldCraftingLedger Ledger;
        private static long CurrentWorld(ZNet net) => net?.GetWorld() != null ? net.GetWorldUID() : 0;
        // Compatibility getter is current-world recovery storage, never a fresh-action authorization.
        internal static GoldCraftingLedger ServerLedger =>
            TryGetRecoveryLedger(ZNet.instance, CurrentWorld(ZNet.instance), out var ledger) ? ledger : null;
        internal static bool TryGetRecoveryLedger(ZNet expectedSession, long expectedWorld, out GoldCraftingLedger ledger)
        {
            ledger = null;
            if (expectedSession == null || !ReferenceEquals(expectedSession, ZNet.instance) ||
                !ReferenceEquals(expectedSession, Session) || !expectedSession.IsServer() || expectedSession.GetWorld() == null ||
                expectedWorld != expectedSession.GetWorldUID() || expectedWorld != SessionWorld || expectedWorld != LoadedWorld ||
                Ledger?.IsAvailable != true) return false;
            ledger = Ledger; return true;
        }
        internal static bool TryGetServerLedger(ZNet expectedSession, long expectedWorld, out GoldCraftingLedger ledger)
        {
            ledger = null;
            return CoreEnabled && TryGetRecoveryLedger(expectedSession, expectedWorld, out ledger);
        }
        internal static bool IsServerLedgerCurrent(ZNet expectedSession, long expectedWorld, GoldCraftingLedger expectedLedger) =>
            TryGetRecoveryLedger(expectedSession, expectedWorld, out var ledger) && ReferenceEquals(ledger, expectedLedger);
        internal static bool ServerIsArmed(long id) => ServerArmed.ContainsKey(id);
        internal static void ClearArm(long id) => ServerArmed.Remove(id);
        internal static void SetServerReady(bool ready) { if (!ready) ResetLocalState(); else ServerReady = true; }
        internal static bool ServerHammer(WorkshopActor actor)
        {
            if (actor.Rpc == null) return HoldsHammer(Player.m_localPlayer);
            ZDO zdo = ZDOMan.instance?.GetZDO(actor.CharacterId);
            int right = zdo?.GetInt(ZDOVars.s_rightItem, 0) ?? -1;
            // The owner retains the logically equipped Hammer when vanilla station UI hides hands.
            // A different visible weapon always overrides that owner inventory snapshot.
            return ServerHolding.Contains(actor.GetPlayerID()) && (right == 0 || right == "Hammer".GetStableHashCode());
        }
        private static float NextUi, NextSync, NextFlush, NextCooldown;
        private static long LocalPlayer;
        private static string TelemetrySummary = "No Crafting100 XP samples yet.";
        private static Terminal DebugTerminal;
        private static bool FavorStateReceived;
        private static float LastStateAt;
        private const string SeenKey = "VM_Gold_Crafting_Ascended";
        internal static bool CoreEnabled => PantheonEnabled?.Value == true && MasteryPlugin.Settings.Enabled.Value;
        internal static bool CraftingEnabled => CoreEnabled && DevelopmentEnabled?.Value == true;
        internal static bool Enabled => CraftingEnabled; // Existing crafting-only consumers keep their gate.
        internal static void Initialize(ConfigFile config)
        {
            PantheonEnabled = config.Bind("Pantheon", "EnableGoldCore", true,
                "Enable shared level-100 Pantheon actions. Previously admitted terminal receipts can still settle when disabled.");
            DevelopmentEnabled = config.Bind("GoldCrafting", "EnableCrafting100", true,
                "Phase100-A Völundr mastery. Requires the same enabled client/server release. Later idol phases remain locked pending live approval.");
            GoldFavorService.Initialize(config);
            MasteryExtendedEventBus.SkillXp += OnXp;
        }
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<ZPackage>("VM_Gold_Request", Receive);
            rpc.Register<ZPackage>("VM_Gold_State", State);
            rpc.Register<ZDOID>("VM_Gold_Ascend", Ascend);
            rpc.Register<ZDOID>("VM_Gold_CeremonyPreview", PreviewCeremony);
            rpc.Register<string>("VM_Gold_DebugReply", DebugReply);
            GoldDivineTransactions.Register(rpc);
            GoldFavorService.Register(rpc);
        }
        internal static bool HoldsHammer(Player player)
        {
            if (player == null || player.IsDead() || player.IsTeleporting()) return false;
            ItemDrop.ItemData item = player.GetRightItem();
            if (item == null && player.GetCurrentCraftingStation() != null) item = player.m_hiddenRightItem;
            return item?.m_shared?.m_buildPieces != null && TierDatabase.ItemKey(item) == "Hammer";
        }
        private static void Send(ZPackage package)
        {
            if (Player.m_localPlayer == null || Session == null || !ReferenceEquals(Session, ZNet.instance) ||
                Session.GetWorld() == null || Session.GetWorldUID() != SessionWorld) return;
            if (ZNet.instance.IsServer()) { package.SetPos(0); Receive(null, package); }
            else ZNet.instance.GetServerRPC()?.Invoke("VM_Gold_Request", package);
        }
        private static void Sync()
        {
            var package = new ZPackage(); package.Write(0); package.Write(HoldsHammer(Player.m_localPlayer));
            package.Write(Player.m_localPlayer.m_customData.ContainsKey(SeenKey)); Send(package);
        }
        internal static void ToggleArm()
        {
            if (!Enabled || !DivineActionsReady || !Unlocked || (!Armed && !HoldsHammer(Player.m_localPlayer))) return;
            bool desired = !Armed;
            OwnerSkillAuthority.SendNow(); Sync();
            var package = new ZPackage(); package.Write(1); package.Write(desired); Send(package);
        }
        internal static void Tick()
        {
            if (Session != ZNet.instance || SessionWorld != CurrentWorld(ZNet.instance))
            {
                Shutdown(); Session = ZNet.instance; SessionWorld = CurrentWorld(Session); ServerArmed.Clear(); ServerHolding.Clear(); ServerReady = false;
                GoldFavorTelemetry.Reset(); Unlocked = Armed = false; Favor = 0; ExhaustionRemaining = 0;
                FavorStateReceived = false;
                ResetLocalState();
                LocalPlayer = 0; NextSync = NextUi = NextFlush = NextCooldown = 0;
            }
            if (Session == null || Session.GetWorld() == null) { ResetLocalState(); return; }
            if (!CraftingEnabled) { ServerArmed.Clear(); ServerHolding.Clear(); Armed = false; }
            if (Ledger == null && Session.IsServer() && Session.GetWorld() != null && ObjectDB.instance != null)
            {
                LoadedWorld = Session.GetWorldUID();
                Ledger = new GoldCraftingLedger(Path.Combine(Paths.ConfigPath, "ValheimMasteryGold", Session.GetWorldUID() + ".bin"));
                if (!Ledger.Load()) MasteryPlugin.Log.LogError("[Gold100] Persistent ledger unavailable; Gold actions fail closed.");
            }
            GoldDivineTransactions.Tick(); GoldFavorService.Tick();
            float now = Time.unscaledTime;
            if (!Session.IsServer() && FavorStateReceived && now - LastStateAt > 8f) ResetLocalState();
            if (Ledger?.IsAvailable == true)
            {
                float cooldownNow = Time.time; // Native Guardian/Forsaken cooldown uses game dt, not offline UTC.
                if (cooldownNow >= NextCooldown)
                {
                    double elapsed = NextCooldown == 0 ? 0 : Math.Max(0, cooldownNow - NextCooldown + 1);
                    NextCooldown = cooldownNow + 1;
                    var online = new HashSet<long>();
                    if (Player.m_localPlayer != null) online.Add(Player.m_localPlayer.GetPlayerID());
                    foreach (var peer in Session.GetPeers())
                    { long id = OwnerSkillAuthority.ResolvePlayerId(peer); if (id != 0) online.Add(id); }
                    foreach (long id in online) Ledger.Elapse(id, elapsed);
                    foreach (long id in new List<long>(ServerArmed.Keys))
                        if (!online.Contains(id) || !Ledger.CooldownAllows(id, GoldCooldownPolicy.LegacyPatronId, UpgradeCost)) ServerArmed.Remove(id);
                    ServerHolding.RemoveWhere(id => !online.Contains(id));
                }
                if (now >= NextFlush) { NextFlush = now + 15; Ledger.Flush(); }
            }
            Player player = Player.m_localPlayer;
            if (player == null) { ResetLocalState(); LocalPlayer = 0; return; }
            if (LocalPlayer != player.GetPlayerID())
            { LocalPlayer = player.GetPlayerID(); ResetLocalState(); NextSync = 0; }
            if (Armed && !HoldsHammer(player))
            { var cancel = new ZPackage(); cancel.Write(1); cancel.Write(false); Send(cancel); Armed = false; }
            if (now >= NextUi) { NextUi = now + .2f; GoldPantheonUi.Tick(); }
            if (now >= NextSync && (Session.IsServer() || NetworkSync.HasServerSettings))
            { NextSync = now + 2; OwnerSkillAuthority.SendNow(); Sync(); }
        }
        internal static void Shutdown()
        {
            Ledger?.Flush(); Ledger = null; LoadedWorld = 0;
        }
        internal static bool HasGold(WorkshopActor actor) => actor != null && actor.Available &&
            (actor.Rpc == null ? OwnerSkillAuthority.Has(Player.m_localPlayer, Skills.SkillType.Crafting, 100) :
                OwnerSkillAuthority.Has(actor.Rpc, actor.CharacterId, actor.GetPlayerID(), Skills.SkillType.Crafting, 100));
        private static void Receive(ZRpc rpc, ZPackage package)
        {
            if (package == null || !TryGetRecoveryLedger(ZNet.instance, CurrentWorld(ZNet.instance), out var ledger)) return;
            WorkshopActor actor = WorkshopActor.Resolve(rpc);
            if (actor == null || !actor.Available || package.Size() > 1024) return;
            long id = actor.GetPlayerID();
                        try
            {
                int operation = package.ReadInt();
                bool newUnlock = false, previouslySeen = true;
                if (operation == 0)
                {
                    bool holding = package.ReadBool(); previouslySeen = package.ReadBool();
                    holding = holding && CraftingEnabled;
                    if (holding) ServerHolding.Add(id); else ServerHolding.Remove(id);
                    if (CraftingEnabled && HasGold(actor) && ledger.Get(id)?.Unlocked != true) newUnlock = ledger.Unlock(id);
                    if (!holding || actor.IsDead() || actor.IsTeleporting()) ServerArmed.Remove(id);
                }
                else if (operation == 1)
                {
                    bool arm = package.ReadBool();
                    bool hammer = ServerHammer(actor);
                    if (arm && DivineActionsReady && HasGold(actor) && hammer && !actor.IsDead() && !actor.IsTeleporting() &&
                        Ledger.CooldownAllows(id, GoldCooldownPolicy.LegacyPatronId, UpgradeCost)) ServerArmed[id] = true;
                    else ServerArmed.Remove(id);
                }
                else if (operation == 3)
                {
                    if (!CraftingEnabled) return;
                    // Even debug mutations need an authenticated admin and server debug mode.
                    bool admin = rpc == null || Session.IsAdmin(Session.GetPeer(rpc)?.m_socket?.GetHostName() ?? "");
                    if (!admin || !MasteryPlugin.Settings.UIDebugLogging.Value)
                    { SendDebugReply(rpc, "Favor commands require an authenticated admin and UIDebugLogging."); return; }
                    string mode = package.ReadString(); float value = package.ReadSingle();
                    if (mode != "show" && mode != "add" && mode != "set" && mode != "trace" && mode != "ceremony") return;
                    if (mode == "ceremony")
                    {
                        // Cosmetic audition never clears unlock/seen state or changes
                        // Favor, XP, cooldown or the live first-unlock acceptance gate.
                        if (rpc == null) PreviewCeremony(null, actor.CharacterId);
                        foreach (var peer in Session.GetPeers()) peer?.m_rpc?.Invoke("VM_Gold_CeremonyPreview", actor.CharacterId);
                        SendDebugReply(rpc, "Ceremony preview only; progression and first-unlock state unchanged.");
                        return;
                    }
                    if (mode == "add") Ledger.Gain(id, value);
                    if (mode == "set") Ledger.SetFavor(id, value);
                    if (mode == "trace") GoldFavorTelemetry.Trace = value > 0;
                    SendDebugReply(rpc, "Volundr Favor=" + Ledger.Get(id).Favor.ToString("0.0") + "/1000; " + GoldFavorTelemetry.Describe(id));
                }
                else return;
                Reply(rpc, id);
                if (newUnlock && !previouslySeen)
                {
                    if (Player.m_localPlayer?.m_nview?.GetZDO()?.m_uid == actor.CharacterId) Ascend(null, actor.CharacterId);
                    foreach (var peer in Session.GetPeers())
                        if (peer?.m_rpc != null) peer.m_rpc.Invoke("VM_Gold_Ascend", actor.CharacterId);
                }
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Gold100] Request rejected: " + error.Message); }
        }
        private static void Reply(ZRpc rpc, long id)
        {
            if (!TryGetRecoveryLedger(ZNet.instance, CurrentWorld(ZNet.instance), out var ledger)) return;
            var state = ledger.Get(id);
            var package = new ZPackage(); package.Write(state.Unlocked); package.Write(state.Favor);
            package.Write(state.ExhaustionRemaining); package.Write(ServerArmed.ContainsKey(id));
            package.Write(CraftingEnabled); // Legacy header remains crafting readiness.
            package.Write(GoldFavorTelemetry.Describe(id));
            package.Write(2); package.Write(SessionWorld); package.Write(id);
            package.Write(state.ExhaustedPatron); package.Write(state.Wallets.Count);
            foreach (var pair in state.Wallets)
            { package.Write(pair.Key); package.Write(pair.Value.Unlocked); package.Write(pair.Value.Favor); package.Write(pair.Value.Held); }
            package.Write(state.Claims.Count + (state.Pending == null ? 0 : 1));
            if (state.Pending != null) { package.Write(GoldCooldownPolicy.LegacyPatronId); package.Write(state.Pending.Cost); }
            foreach (var claim in state.Claims.Values) { package.Write(claim.Patron); package.Write(claim.Cost); }
            package.Write(CoreEnabled); // V2 shared readiness independent of Crafting100.
            if (rpc == null) { package.SetPos(0); State(null, package); }
            else rpc.Invoke("VM_Gold_State", package);
        }
        internal static void NotifyFavor(long id)
        {
            if (!TryGetRecoveryLedger(ZNet.instance, CurrentWorld(ZNet.instance), out _)) return;
            if (Player.m_localPlayer?.GetPlayerID() == id) Reply(null, id);
            foreach (var peer in Session.GetPeers())
                if (peer?.m_rpc != null && OwnerSkillAuthority.ResolvePlayerId(peer) == id) Reply(peer.m_rpc, id);
        }
        private static void State(ZRpc rpc, ZPackage package)
        {
            if (Player.m_localPlayer == null || Session == null || !ReferenceEquals(Session, ZNet.instance) ||
                Session.GetWorld() == null || SessionWorld != Session.GetWorldUID() || package == null || package.Size() > 8192 ||
                (rpc != null && (Session.IsServer() || rpc != Session.GetServerRPC()))) return;
            try
            {
                bool unlocked = package.ReadBool(); float favor = package.ReadSingle();
                double exhaustion = package.ReadDouble(); bool armed = package.ReadBool();
                bool ready = package.ReadBool(); bool coreReady = ready; string telemetry = package.ReadString();
                if (!GoldFavorModel.Finite(favor) || favor < 0 || favor > MaxFavor || !GoldFavorModel.Finite(exhaustion) ||
                    exhaustion < 0 || exhaustion > GoldCooldownPolicy.LegacySeconds || telemetry.Length > 2048) throw new InvalidDataException("Invalid Gold state.");
                var wallets = new Dictionary<string, GoldPatronWallet>(StringComparer.Ordinal);
                var claims = new List<KeyValuePair<string, float>>();
                string origin = exhaustion > 0 ? GoldCooldownPolicy.LegacyPatronId : string.Empty;
                if (package.GetPos() < package.Size())
                {
                    int version = package.ReadInt();
                    if (version != 1 && version != 2) throw new InvalidDataException("Unsupported patron snapshot.");
                    long world = package.ReadLong(), recipient = package.ReadLong();
                    if (world != Session.GetWorldUID() || recipient != Player.m_localPlayer.GetPlayerID()) return; // delayed old-character state
                    origin = package.ReadString();
                    int count = package.ReadInt();
                    if (count < 1 || count > GoldCraftingLedger.MaxPatrons) throw new InvalidDataException("Invalid wallet count.");
                    for (int i = 0; i < count; i++)
                    {
                        string patron = package.ReadString();
                        var wallet = new GoldPatronWallet { Unlocked = package.ReadBool(), Favor = package.ReadSingle(), Held = package.ReadSingle() };
                        if (!GoldCraftingLedger.ValidPatronId(patron) || wallets.ContainsKey(patron) || !GoldFavorModel.Finite(wallet.Favor) ||
                            !GoldFavorModel.Finite(wallet.Held) || wallet.Favor < 0 || wallet.Favor > MaxFavor || wallet.Held < 0 ||
                            wallet.Held > wallet.Favor || !wallet.Unlocked && (wallet.Favor != 0 || wallet.Held != 0)) throw new InvalidDataException("Invalid wallet.");
                        wallets.Add(patron, wallet);
                    }
                    if (!wallets.TryGetValue(GoldCooldownPolicy.LegacyPatronId, out var legacy) || legacy.Unlocked != unlocked || legacy.Favor != favor)
                        throw new InvalidDataException("Legacy wallet mismatch.");
                    int pendingCount = package.ReadInt();
                    if (pendingCount < 0 || pendingCount > GoldCraftingLedger.MaxPatrons) throw new InvalidDataException("Invalid claims.");
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < pendingCount; i++)
                    {
                        string patron = package.ReadString(); float cost = package.ReadSingle();
                        if (!seen.Add(patron) || !wallets.TryGetValue(patron, out var wallet) || !wallet.Unlocked ||
                            !GoldFavorModel.Finite(cost) || cost < 0 || cost > MaxFavor || cost != wallet.Held) throw new InvalidDataException("Invalid claim.");
                        claims.Add(new KeyValuePair<string, float>(patron, cost));
                    }
                    foreach (var pair in wallets) if (pair.Value.Held > 0 && !seen.Contains(pair.Key)) throw new InvalidDataException("Orphan held funds.");
                    if (version == 2) coreReady = package.ReadBool();
                    if (ready && !coreReady) throw new InvalidDataException("Crafting ready without core.");
                    if (package.GetPos() != package.Size()) throw new InvalidDataException("Trailing Gold state.");
                }
                else wallets.Add(GoldCooldownPolicy.LegacyPatronId, new GoldPatronWallet { Unlocked = unlocked, Favor = favor });
                if (exhaustion > 0 ? !wallets.TryGetValue(origin, out var exhausted) || !exhausted.Unlocked : origin != string.Empty)
                    throw new InvalidDataException("Invalid cooldown origin.");
                bool filled = FavorStateReceived && Unlocked && Favor < MaxFavor && unlocked && favor >= MaxFavor;
                LocalWallets = wallets; LocalClaims.Clear(); LocalClaims.AddRange(claims); ExhaustedPatron = origin;
                Unlocked = unlocked; Favor = Mathf.Clamp(favor, 0, MaxFavor);
                ExhaustionRemaining = Math.Max(0, exhaustion); Armed = CraftingEnabled && armed;
                ServerReady = ready; ServerCoreReady = coreReady; TelemetrySummary = telemetry;
                LocalPlayer = Player.m_localPlayer.GetPlayerID();
                FavorStateReceived = true;
                LastStateAt = Time.unscaledTime;
                if (Unlocked) PerkStateService.MarkEverUnlocked(Player.m_localPlayer, Skills.SkillType.Crafting, 100);
                if (filled)
                {
                    try
                    {
                        Player player = Player.m_localPlayer;
                        player.Message(MessageHud.MessageType.Center,
                            GoldUiLocalization.Text("VÖLUNDR'S FAVOR IS FULL", "ПРИХИЛЬНІСТЬ ВЬОЛУНДРА СПОВНЕНА"), 0,
                            player.GetSkills()?.GetSkillDef(Skills.SkillType.Crafting)?.m_icon);
                        PerkAudioService.Play("gold_favor_full", "sfx_gui_craftitem_forge", player.GetCenterPoint(), 1f, .7f, 1.05f);
                    }
                    catch (Exception error) { MasteryPlugin.Log.LogWarning("[Gold100] Favor feedback unavailable: " + error.Message); }
                }
            }
            catch (Exception error) { ResetLocalState(); MasteryPlugin.Log.LogWarning("[Gold100] Invalid state: " + error.Message); }
        }
        private static void Ascend(ZRpc rpc, ZDOID id)
        {
            if (!Enabled || Session == null || (rpc != null && (Session.IsServer() || rpc != Session.GetServerRPC()))) return;
            Player local = Player.m_localPlayer;
            Player target = ZNetScene.instance?.FindInstance(id)?.GetComponent<Player>();
            if (local == null || target == null || (local.transform.position - target.transform.position).sqrMagnitude > 1600) return;
            bool recognition = target == local;
            if (recognition)
            {
                if (local.m_customData.ContainsKey(SeenKey)) return;
                local.m_customData[SeenKey] = "Volundr";
                var profile = Game.instance?.GetPlayerProfile();
                if (profile != null) { profile.SavePlayerData(local); if (!profile.Save()) MasteryPlugin.Log.LogWarning("[Gold100] Character recognition save failed; server unlock remains durable."); }
                foreach (PerkDefinition perk in PerkCatalog.Get(Skills.SkillType.Crafting))
                    if (perk.Milestone == 100) { MasteryMilestonePresentation.Show(local, Skills.SkillType.Crafting, 100, perk, false); break; }
            }
            GoldAscensionVisual.Play(target, recognition);
        }
        private static void OnXp(SkillXpEvent value)
        {
            if (!Enabled || !Unlocked || value?.Player != Player.m_localPlayer || value.Skill != Skills.SkillType.Crafting) return;
            GoldFavorService.Observe(value);
        }
        private static void PreviewCeremony(ZRpc rpc, ZDOID id)
        {
            if (!Enabled || Session == null || (rpc != null && (Session.IsServer() || rpc != Session.GetServerRPC()))) return;
            Player local = Player.m_localPlayer;
            Player target = ZNetScene.instance?.FindInstance(id)?.GetComponent<Player>();
            if (local == null || target == null || (local.transform.position - target.transform.position).sqrMagnitude > 1600) return;
            if (local == target)
                foreach (PerkDefinition perk in PerkCatalog.Get(Skills.SkillType.Crafting))
                    if (perk.Milestone == 100) { MasteryMilestonePresentation.Show(local, Skills.SkillType.Crafting, 100, perk, false); break; }
            GoldAscensionVisual.Play(target, local == target);
        }
        private static void SendDebugReply(ZRpc rpc, string text)
        { if (rpc == null) DebugReply(null, text); else rpc.Invoke("VM_Gold_DebugReply", text); }
        private static void DebugReply(ZRpc rpc, string text)
        {
            if (Session == null || text == null || text.Length > 1024 ||
                (rpc != null && (Session.IsServer() || rpc != Session.GetServerRPC()))) return;
            DebugTerminal?.AddString(text);
        }
        internal static void Debug(Terminal.ConsoleEventArgs args)
        {
            if (!Enabled) { args.Context.AddString("Crafting100 is disabled by configuration."); return; }
            DebugTerminal = args.Context;
            if (args.Args.Length == 3 && args.Args[2] == "ceremony")
            { var p = new ZPackage(); p.Write(3); p.Write("ceremony"); p.Write(0f); Send(p); }
            else if (args.Args.Length >= 4 && args.Args[2] == "trace")
            { var p = new ZPackage(); p.Write(3); p.Write("trace"); p.Write(args.Args[3] == "on" ? 1f : 0f); Send(p); }
            else if (args.Args.Length >= 5 && (args.Args[2] == "add" || args.Args[2] == "set") && args.Args[3] == "volundr" &&
                float.TryParse(args.Args[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
            { var p = new ZPackage(); p.Write(3); p.Write(args.Args[2]); p.Write(value); Send(p); }
            else if (args.Args.Length == 2 || args.Args.Length == 3 && args.Args[2] == "show")
            { var p = new ZPackage(); p.Write(3); p.Write("show"); p.Write(0f); Send(p); }
            else args.Context.AddString("vm favor show | set volundr <0..1000> | add volundr <amount> | trace on|off | ceremony");
        }
    }
}
