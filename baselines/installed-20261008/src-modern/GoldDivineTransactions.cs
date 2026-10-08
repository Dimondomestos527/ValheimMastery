using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Separate from Workshop debit: a Masterwork never reserves or consumes chest contents.
    // Character receipts and inventory are saved together using the native profile save.
    internal sealed class GoldAction
    {
        internal string Token, Recipe, Prefab, ItemIdentity, Idol;
        internal int Kind, OldQuality, NewQuality, Variant, IdolQuality;
        internal ZDOID Station;
        internal float Cost => Kind == 1 ? GoldCraftingService.MasterworkCost : GoldCraftingService.UpgradeCost;
        internal string Encode()
        {
            var p = new ZPackage(); p.Write(1); p.Write(Token); p.Write(Kind); p.Write(Recipe); p.Write(Prefab);
            p.Write(ItemIdentity ?? ""); p.Write(OldQuality); p.Write(NewQuality); p.Write(Variant);
            p.Write(Station); p.Write(Idol ?? ""); p.Write(IdolQuality);
            return Convert.ToBase64String(p.GetArray());
        }
        internal static GoldAction Decode(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 1024) throw new FormatException("Gold contract size");
            var p = new ZPackage(Convert.FromBase64String(text));
            if (p.ReadInt() != 1) throw new FormatException("Gold contract version");
            var a = new GoldAction { Token = p.ReadString(), Kind = p.ReadInt(), Recipe = p.ReadString(),
                Prefab = p.ReadString(), ItemIdentity = p.ReadString(), OldQuality = p.ReadInt(), NewQuality = p.ReadInt(),
                Variant = p.ReadInt(), Station = p.ReadZDOID(), Idol = p.ReadString(), IdolQuality = p.ReadInt() };
            if (!Guid.TryParseExact(a.Token, "N", out _) || a.Recipe.Length > 128 || a.Prefab.Length > 128 ||
                a.Recipe.Length == 0 || a.Prefab.Length == 0 || a.Idol.Length > 128 || a.Variant < 0 ||
                (a.Kind != 1 && a.Kind != 2) || a.OldQuality < 0 || a.NewQuality < 1 || a.NewQuality > 10000 ||
                (a.Kind == 1 ? a.OldQuality != 0 || a.NewQuality != 5 || a.ItemIdentity != "" || a.Idol != "" :
                    a.OldQuality < 1 || a.NewQuality != a.OldQuality + 3 ||
                    !Guid.TryParseExact(a.ItemIdentity, "N", out _) || a.Idol.Length == 0 || a.IdolQuality < 0))
                throw new FormatException("Gold contract fields");
            return a;
        }
    }

    internal static class GoldDivineTransactions
    {
        private const string ItemId = "VM_Gold_ItemIdentity", ItemToken = "VM_Gold_Grant";
        private const int Requested = 1, Applied = 2, Rejected = 3;
        private static readonly HashSet<long> Validating = new HashSet<long>();
        private static readonly Queue<Action> Deferred = new Queue<Action>();
        private static ZNet Session;
        private static long SessionWorld;
        private static float NextRecovery;
        private static long QuarantinedPlayer;
        private static string JournalKey => "VM_Gold_Receipt_" + (ZNet.instance?.GetWorldUID() ?? 0);
        private static double Utc => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        private sealed class MasterworkIntent
        {
            internal InventoryGui Gui;
            internal Player Player;
            internal ZNet Session;
            internal Recipe Recipe;
            internal int Variant;
            internal int Kind = 1, OldQuality, ItemVariant;
            internal ItemDrop.ItemData Item;
            internal ZDOID Station;
        }
        private static MasterworkIntent Masterwork;
        internal static bool MasterworkTimerActive => Masterwork != null;
        internal static void CancelMasterwork(InventoryGui gui)
        {
            if (gui == null || Masterwork?.Gui != gui) return;
            gui.m_craftTimer = -1f;
            Masterwork = null;
        }
        internal static void RefreshMasterworkIntent(InventoryGui gui, Player player)
        {
            if (Masterwork == null || Masterwork.Gui != gui) return;
            if (!MasterworkContextValid(Masterwork, gui, player) || gui.m_craftTimer < 0)
            {
                Masterwork = null;
                gui.m_craftTimer = -1f;
            }
        }
        private static bool MasterworkContextValid(MasterworkIntent intent, InventoryGui gui, Player player) =>
            gui != null && intent.Gui == gui && intent.Player == player && player == Player.m_localPlayer &&
            intent.Session == ZNet.instance && player != null && !player.IsDead() && !player.IsTeleporting() &&
            GoldCraftingService.Enabled && ReferenceEquals(gui.m_selectedRecipe.ItemData, intent.Item) &&
            (intent.Kind == 1 ? intent.Item == null : intent.Kind == 2 && intent.Item != null &&
                intent.Item.m_quality == intent.OldQuality && intent.Item.m_variant == intent.ItemVariant &&
                player.GetInventory().GetAllItems().Contains(intent.Item)) &&
            gui.m_selectedRecipe.Recipe == intent.Recipe && gui.m_selectedVariant == intent.Variant &&
            gui.m_craftRecipe == intent.Recipe && ReferenceEquals(gui.m_craftUpgradeItem, intent.Item) &&
            gui.m_craftVariant == intent.Variant &&
            (player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None) == intent.Station;
        internal static bool CanStartMasterwork(InventoryGui gui, Player player)
        {
            Recipe recipe = gui?.m_selectedRecipe.Recipe;
            if (gui == null || player == null || player != Player.m_localPlayer || ZNet.instance == null ||
                gui.m_selectedRecipe.ItemData != null || gui.m_currentContainer != null || gui.m_craftTimer >= 0 || Masterwork != null ||
                !GoldCraftingService.Unlocked || !GoldCraftingService.DivineActionsReady ||
                !GoldCraftingService.CanStart(GoldCraftingService.MasterworkCost) || Outstanding || WorkshopRemoteCraft.ClientBusy ||
                WorkshopRecovery.Outstanding || !Eligible(recipe?.m_item?.m_itemData)) return false;
            var action = new GoldAction { Kind = 1, Prefab = recipe.m_item.gameObject.name,
                Variant = gui.m_selectedVariant, Station = player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None };
            return LocalValid(player, action, recipe, false);
        }
        internal static void BeginMasterwork(InventoryGui gui)
        {
            Player player = Player.m_localPlayer;
            if (!CanStartMasterwork(gui, player)) return;
            Recipe recipe = gui.m_selectedRecipe.Recipe;
            Masterwork = new MasterworkIntent { Gui = gui, Player = player, Session = ZNet.instance,
                Recipe = recipe, Variant = gui.m_selectedVariant,
                Station = player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None };
            gui.m_craftRecipe = recipe; gui.m_craftUpgradeItem = null; gui.m_craftVariant = gui.m_selectedVariant;
            gui.m_multiCrafting = false; gui.m_touchMultiCrafting = false; gui.m_craftTimer = 0;
            MasterworkIntent intent = Masterwork;
            GoldMasterworkPresentation.Begin(gui, player, recipe, () => ReferenceEquals(Masterwork, intent) &&
                MasterworkContextValid(intent, gui, player));
            // Keep native progress/cancel. Completion still waits for durable success.
        }
        internal static bool CanStartForge(InventoryGui gui, Player player)
        {
            Recipe recipe = gui?.m_selectedRecipe.Recipe;
            var item = gui?.m_selectedRecipe.ItemData;
            if (gui == null || player == null || player != Player.m_localPlayer || ZNet.instance == null ||
                item == null || gui.m_currentContainer != null || gui.m_craftTimer >= 0 || Masterwork != null ||
                !GoldCraftingService.Unlocked || !GoldCraftingService.CanStart(GoldCraftingService.UpgradeCost) ||
                Outstanding || WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding ||
                !Eligible(recipe?.m_item?.m_itemData) || !player.GetInventory().GetAllItems().Contains(item)) return false;
            var action = new GoldAction { Kind = 2, Prefab = recipe.m_item.gameObject.name,
                OldQuality = item.m_quality, NewQuality = item.m_quality + 3, Variant = item.m_variant,
                Station = player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None };
            var idol = player.GetFirstRequiredItem(player.GetInventory(), recipe, item.m_quality + 1, out _, out _, 1);
            if (idol == null || TierDatabase.ItemKey(item) != action.Prefab) return false;
            action.Idol = TierDatabase.ItemKey(idol); action.IdolQuality = idol.m_quality;
            return LocalValid(player, action, recipe, true, item);
        }
        internal static void BeginForge(InventoryGui gui)
        {
            Player player = Player.m_localPlayer;
            if (!CanStartForge(gui, player)) return;
            Recipe recipe = gui.m_selectedRecipe.Recipe;
            var item = gui.m_selectedRecipe.ItemData;
            Masterwork = new MasterworkIntent { Gui = gui, Player = player, Session = ZNet.instance,
                Recipe = recipe, Kind = 2, Item = item, OldQuality = item.m_quality, ItemVariant = item.m_variant, Variant = gui.m_selectedVariant,
                Station = player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None };
            gui.m_craftRecipe = recipe; gui.m_craftUpgradeItem = item; gui.m_craftVariant = gui.m_selectedVariant;
            gui.m_multiCrafting = false; gui.m_touchMultiCrafting = false; gui.m_craftTimer = 0;
            MasterworkIntent intent = Masterwork;
            GoldMasterworkPresentation.Begin(gui, player, recipe, () => ReferenceEquals(Masterwork, intent) &&
                MasterworkContextValid(intent, gui, player));
        }
        internal static bool Outstanding => ReadReceipt(Player.m_localPlayer, out _, out _);
        internal static bool Eligible(ItemDrop.ItemData item) => item?.m_shared != null && item.IsEquipable() &&
            item.m_shared.m_maxQuality > 1 && item.m_shared.m_maxStackSize == 1;
        private static Recipe FindRecipe(string name)
        {
            if (ObjectDB.instance == null) return null;
            foreach (var recipe in ObjectDB.instance.m_recipes) if (recipe != null && recipe.name == name) return recipe;
            return null;
        }
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<ZPackage>("VM_Gold_Action", Receive);
            rpc.Register<ZPackage>("VM_Gold_Grant", Grant);
        }
        // Ordinary craft never implicitly enters a Gold transaction.
        internal static bool Start(InventoryGui gui) => gui == null || Masterwork?.Gui != gui;
        private static bool ReadReceipt(Player p, out GoldAction action, out int phase)
        {
            action = null; phase = 0;
            if (p == null || !p.m_customData.TryGetValue(JournalKey, out string raw)) return false;
            try
            {
                int split = raw.IndexOf(':'); phase = int.Parse(raw.Substring(0, split));
                action = GoldAction.Decode(raw.Substring(split + 1));
                return phase >= Requested && phase <= Rejected;
            }
            catch { phase = -1; return true; } // Invalid durable state never admits a new item.
        }
        private static bool Save(Player player)
            => GoldCharacterSave.Persist(player);
        private static bool Receipt(Player p, GoldAction a, int phase)
        {
            bool existed = p.m_customData.TryGetValue(JournalKey, out string old);
            p.m_customData[JournalKey] = phase + ":" + a.Encode();
            if (Save(p)) return true;
            // A memory-only APPLIED/REJECTED marker must never be acknowledged by the retry loop.
            if (existed) p.m_customData[JournalKey] = old; else p.m_customData.Remove(JournalKey);
            return false;
        }
        private static void Tell(string ua, string en)
        {
            try { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, GoldUiLocalization.Text(en, ua)); }
            catch (Exception e) { MasteryPlugin.Log.LogWarning("[Gold100] Optional message failed: " + e.Message); }
        }
        internal static bool Intercept(InventoryGui gui, Player player)
        {
            bool buttonAction = Masterwork?.Gui == gui && gui != null;
            int buttonKind = buttonAction ? Masterwork.Kind : 0;
            if (buttonAction)
            {
                MasterworkIntent intent = Masterwork;
                Masterwork = null;
                if (!MasterworkContextValid(intent, gui, player)) return false;
            }
            else return true;
            Recipe recipe = gui?.m_craftRecipe;
            if (recipe == null || !Eligible(recipe.m_item?.m_itemData)) return !buttonAction;
            CraftingStation station = player.GetCurrentCraftingStation();
            ItemDrop.ItemData item = gui.m_craftUpgradeItem;
            if (buttonKind == 1 ? item != null || station?.m_upgrader == true :
                buttonKind != 2 || item == null || station?.m_upgrader != true) return false;
            if (Outstanding || WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding)
            { Tell("Натхнення: попередня дія ще завершується.", "Inspiration: a previous action is still settling."); return false; }
            var a = new GoldAction { Token = Guid.NewGuid().ToString("N"), Kind = item == null ? 1 : 2,
                Recipe = recipe.name, Prefab = recipe.m_item.gameObject.name, OldQuality = item?.m_quality ?? 0,
                NewQuality = item == null ? 5 : item.m_quality + 3, Variant = item?.m_variant ?? gui.m_craftVariant,
                ItemIdentity = "", Idol = "", Station = station?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None };
            if (item != null)
            {
                if (!item.m_customData.TryGetValue(ItemId, out a.ItemIdentity))
                    item.m_customData[ItemId] = a.ItemIdentity = Guid.NewGuid().ToString("N");
                var idol = player.GetFirstRequiredItem(player.GetInventory(), recipe, item.m_quality + 1, out _, out _, 1);
                if (idol != null) { a.Idol = TierDatabase.ItemKey(idol); a.IdolQuality = idol.m_quality; }
            }
            if (!GoldCraftingService.CanStart(a.Cost) || !LocalValid(player, a, recipe, true))
            { Tell("Натхнення: бракує прихильності або не виконано умов майстерні.", "Inspiration: insufficient Favor or workshop conditions are not met."); return false; }
            if (!Receipt(player, a, Requested))
            { player.m_customData.Remove(JournalKey); Tell("Не вдалося зберегти дію. Предмет і прихильність не витрачено.", "Could not save the action. Item and Favor are unchanged."); return false; }
            OwnerSkillAuthority.SendNow(); Send(a, Requested);
            return false; // No vanilla craft, bonus output, inventory payment or Workshop debit.
        }
        private static ItemDrop.ItemData Target(Player player, GoldAction a)
        {
            foreach (var item in player.GetInventory().GetAllItems())
                if (item.m_customData.TryGetValue(ItemId, out string id) && id == a.ItemIdentity && TierDatabase.ItemKey(item) == a.Prefab) return item;
            return null;
        }
        private static bool LocalValid(Player player, GoldAction a, Recipe recipe, bool requireHammer, ItemDrop.ItemData candidate = null)
        {
            if (player == null || player.IsDead() || player.IsTeleporting() || recipe?.m_item == null || !recipe.m_enabled ||
                !Eligible(recipe.m_item.m_itemData) || recipe.m_item.gameObject.name != a.Prefab ||
                !player.m_knownRecipes.Contains(recipe.m_item.m_itemData.m_shared.m_name) ||
                (a.Kind == 2 && requireHammer && !GoldCraftingService.HoldsHammer(player))) return false;
            var shared = recipe.m_item.m_itemData.m_shared;
            if (shared.m_icons == null || a.Variant < 0 || a.Variant >= shared.m_icons.Length) return false;
            if (!string.IsNullOrEmpty(shared.m_dlc) && DLCMan.instance?.IsDLCInstalled(shared.m_dlc) != true) return false;
            CraftingStation station = player.GetCurrentCraftingStation();
            ZDOID current = station?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None;
            if (current != a.Station) return false;
            if (a.Kind == 1)
            {
                CraftingStation required = recipe.GetRequiredStation(1);
                if (station != null && (station.m_upgrader || !station.InUseDistance(player) || !WorkshopRemoteCraft.StationUsable(station))) return false;
                if (!GoldMasterworkRules.StationTypeMatches(required?.m_name, station?.m_name,
                    station?.m_upgrader == true, station?.m_showBasicRecipies == true)) return false;
                if (!PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 100)) return false;
                return player.GetInventory().CanAddItem(recipe.m_item.gameObject, 1);
            }
            var target = candidate ?? Target(player, a);
            if (station == null || !station.m_upgrader || !station.InUseDistance(player) || !WorkshopRemoteCraft.StationUsable(station) ||
                target == null || !Eligible(target) || target.m_quality != a.OldQuality || target.m_variant != a.Variant) return false;
            var idol = player.GetFirstRequiredItem(player.GetInventory(), recipe, a.OldQuality + 1, out _, out _, 1);
            return idol != null && TierDatabase.ItemKey(idol) == a.Idol && idol.m_quality == a.IdolQuality && idol.m_stack >= 1;
        }
        private static bool ServerValid(WorkshopActor actor, GoldAction a, Recipe recipe, bool environment)
        {
            if (actor == null || !actor.Available || actor.IsDead() || actor.IsTeleporting() || !ServerActionAllowed(actor, a) ||
                recipe?.m_item == null || !recipe.m_enabled || !Eligible(recipe.m_item.m_itemData) || recipe.m_item.gameObject.name != a.Prefab ||
                recipe.m_item.m_itemData.m_shared.m_icons == null || a.Variant >= recipe.m_item.m_itemData.m_shared.m_icons.Length) return false;
            if (a.Kind == 1) return MasterworkStationValid(actor, a, recipe, environment);
            var entry = WorkshopWorldRecords.Find(a.Station);
            if (entry?.Station == null || !entry.Station.m_upgrader ||
                Vector3.Distance(actor.Position, entry.Data.GetPosition()) >= entry.Station.m_useDistance ||
                !WorkshopWorldRecords.WardAllows(actor.GetPlayerID(), entry.Data.GetPosition()) ||
                (environment && !WorkshopStationProof.Usable(entry.Data, entry.Station))) return false;
            foreach (var r in recipe.m_resources)
                if (r?.m_upgraderResource == true && r.m_resItem != null && r.m_resItem.gameObject.name == a.Idol &&
                    a.IdolQuality <= r.m_resItem.m_itemData.m_shared.m_maxQuality) return true;
            return false;
        }
        private static bool ServerActionAllowed(WorkshopActor actor, GoldAction a) =>
            actor != null && GoldCraftingService.HasGold(actor) && GoldMasterworkRules.ActionAllowed(a.Kind,
                false, GoldCraftingService.ServerHammer(actor));

        private static bool MasterworkStationValid(WorkshopActor actor, GoldAction a, Recipe recipe, bool environment)
        {
            CraftingStation required = recipe.GetRequiredStation(1);
            if (a.Station == ZDOID.None)
                return GoldMasterworkRules.StationTypeMatches(required?.m_name, null, false, false);
            var entry = WorkshopWorldRecords.Find(a.Station);
            var station = entry?.Station;
            if (station == null ||
                !GoldMasterworkRules.StationTypeMatches(required?.m_name, station.m_name, station.m_upgrader, station.m_showBasicRecipies) ||
                Vector3.Distance(actor.Position, entry.Data.GetPosition()) >= station.m_useDistance ||
                !WorkshopWorldRecords.WardAllows(actor.GetPlayerID(), entry.Data.GetPosition()) ||
                (environment && !WorkshopStationProof.Usable(entry.Data, station))) return false;
            return true;
        }

        private static void Send(GoldAction action, int phase)
        {
            try
            {
                var p = new ZPackage(); p.Write(phase); p.Write(action.Encode());
                if (ZNet.instance?.IsServer() == true) { p.SetPos(0); Receive(null, p); }
                else ZNet.instance?.GetServerRPC()?.Invoke("VM_Gold_Action", p);
            }
            catch (Exception e)
            {
                // Transport failure AFTER the saved item is not an application failure.
                // Keep its durable receipt; the recovery loop retries without rolling back the item.
                MasteryPlugin.Log.LogWarning("[Gold100] Receipt transport deferred: " + e.Message);
            }
        }
        private static void Reply(ZRpc rpc, int kind, string encoded, string reason = "")
        {
            var p = new ZPackage(); p.Write(kind); p.Write(encoded); p.Write(reason);
            if (rpc == null) { p.SetPos(0); Grant(null, p); } else rpc.Invoke("VM_Gold_Grant", p);
        }
        private static void Receive(ZRpc rpc, ZPackage p)
        {
            ZNet requestSession = ZNet.instance;
            long requestWorld = requestSession?.GetWorld() != null ? requestSession.GetWorldUID() : 0;
            if (p == null || p.Size() > 1600 || !GoldCraftingService.TryGetRecoveryLedger(requestSession, requestWorld, out var ledger)) return;
            var actor = WorkshopActor.Resolve(rpc); if (actor?.Available != true) return;
            try
            {
                int phase = p.ReadInt(); string encoded = p.ReadString(); GoldAction a = GoldAction.Decode(encoded);
                if (ledger?.IsAvailable != true) { Reply(rpc, 4, encoded, "storage"); return; }
                long id = actor.GetPlayerID(); var state = ledger.Get(id);
                if (state.SettledTokens.Contains(a.Token)) { Reply(rpc, 2, encoded); GoldCraftingService.NotifyFavor(id); return; }
                if (state.Pending != null)
                {
                    if (state.Pending.GuidToken != a.Token || state.Pending.StationString != encoded) { Reply(rpc, 0, encoded, "busy"); return; }
                    if (phase == Applied)
                    {
                        if (ledger.Commit(id, a.Token, Utc))
                        { GoldCraftingService.ClearArm(id); Reply(rpc, 2, encoded); GoldCraftingService.NotifyFavor(id); }
                    }
                    else if (phase == Rejected)
                    { if (ledger.Reject(id, a.Token)) Reply(rpc, 2, encoded); }
                    else if (phase == Requested && GoldCraftingService.CraftingEnabled) Reply(rpc, 1, state.Pending.StationString);
                    return;
                }
                if (phase == Rejected) { Reply(rpc, 2, encoded); return; }
                if (phase != Requested) { Reply(rpc, 0, encoded, "missing-receipt"); return; }
                if (!GoldCraftingService.CraftingEnabled) return;
                Recipe recipe = FindRecipe(a.Recipe);
                if (!ServerActionAllowed(actor, a) || state.Unlocked != true ||
                    !ledger.CanStart(id, GoldCooldownPolicy.LegacyPatronId, a.Cost) || !ServerValid(actor, a, recipe, false))
                { Reply(rpc, 0, encoded, "conditions"); return; }
                if (!Validating.Add(id)) return;
                ZNet admittedSession = ZNet.instance;
                WorkshopStationProof.Acquire(actor, a.Token, a.Station, () =>
                {
                    if (!GoldCraftingService.IsServerLedgerCurrent(admittedSession, requestWorld, ledger)) return;
                    Validating.Remove(id);
                    var currentActor = WorkshopActor.Resolve(rpc);
                    if (!GoldCraftingService.Enabled || ledger.IsAvailable != true || currentActor?.Available != true ||
                        currentActor.GetPlayerID() != id || currentActor.CharacterId != actor.CharacterId ||
                        !ServerActionAllowed(currentActor, a) || !ServerValid(currentActor, a, recipe, true) ||
                        !ledger.Reserve(id, new GoldCraftingLedger.PendingGrant { GuidToken = a.Token, Kind = a.Kind,
                            Prefab = a.Prefab, OldQuality = a.OldQuality, NewQuality = a.NewQuality, Variant = a.Variant,
                            Cost = a.Cost, StationString = encoded, ExpiresAt = Utc + 30 }, Utc))
                    { Reply(rpc, 0, encoded, "conditions"); return; }
                    Reply(rpc, 1, encoded);
                }, error =>
                {
                    if (!GoldCraftingService.IsServerLedgerCurrent(admittedSession, requestWorld, ledger)) return;
                    Validating.Remove(id); Reply(rpc, 0, encoded, "station");
                });
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Gold100] Invalid divine request: " + error.Message); }
        }
        private static void Grant(ZRpc rpc, ZPackage p)
        {
            if (Player.m_localPlayer == null || ZNet.instance?.GetWorld() == null || p == null || p.Size() > 1600 ||
                (rpc != null && (ZNet.instance.IsServer() || rpc != ZNet.instance.GetServerRPC()))) return;
            try
            {
                int kind = p.ReadInt(); string encoded = p.ReadString(); p.ReadString();
                GoldAction action = GoldAction.Decode(encoded); Player player = Player.m_localPlayer;
                ZNet grantSession = ZNet.instance; long grantWorld = grantSession.GetWorldUID();
                ZDOID grantCharacter = player.m_nview?.GetZDO()?.m_uid ?? ZDOID.None;
                // Deferred work is bounded, preventing packet re-entry during mutation or save callbacks.
                if (Deferred.Count >= 8) return;
                Deferred.Enqueue(() =>
                {
                    if (player != Player.m_localPlayer || !ReferenceEquals(grantSession, ZNet.instance) ||
                        grantSession.GetWorld() == null || grantWorld != grantSession.GetWorldUID() ||
                        grantCharacter == ZDOID.None || player.m_nview?.GetZDO()?.m_uid != grantCharacter ||
                        QuarantinedPlayer == player.GetPlayerID()) return;
                    bool receipt = ReadReceipt(player, out GoldAction saved, out int phase);
                    GoldReceiptDecision decision = GoldReceiptPolicy.Decide(kind, phase, receipt && saved != null && saved.Encode() == encoded);
                    if (decision == GoldReceiptDecision.Ignore) return;
                    if (decision == GoldReceiptDecision.StorageUnavailable) { GoldCraftingService.SetServerReady(false); return; }
                    if (decision == GoldReceiptDecision.ClearSettled)
                    {
                        string old = player.m_customData[JournalKey]; player.m_customData.Remove(JournalKey);
                        if (!Save(player)) player.m_customData[JournalKey] = old;
                        return;
                    }
                    if (decision == GoldReceiptDecision.ResendReceipt) { Send(saved, phase); return; }
                    if (decision == GoldReceiptDecision.Reject)
                    {
                        if (Receipt(player, saved, Rejected)) Send(saved, Rejected);
                        Tell("Натхнення не використано: умови дії змінилися.", "Inspiration was not spent: action conditions changed."); return;
                    }
                    if (decision == GoldReceiptDecision.ApplyOnce && GoldCraftingService.DivineActionsReady) Apply(player, action);
                });
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Gold100] Invalid grant: " + error.Message); }
        }
        private static void Apply(Player player, GoldAction a)
        {
            Recipe recipe = FindRecipe(a.Recipe);
            if (!LocalValid(player, a, recipe, true))
            { if (Receipt(player, a, Rejected)) Send(a, Rejected); return; }
            Inventory inventory = player.GetInventory();
            ItemDrop.ItemData item = null, idol = null; int oldStack = 0; float oldDurability = 0;
            string oldToken = null; bool hadToken = false, mutated = false, appliedSaved = false;
            try
            {
                if (a.Kind == 1)
                {
                    item = recipe.m_item.m_itemData.Clone(); item.m_dropPrefab = recipe.m_item.gameObject;
                    item.m_quality = 5; item.m_variant = a.Variant; item.m_stack = 1; item.m_worldLevel = Game.m_worldLevel;
                    item.m_crafterID = player.GetPlayerID(); item.m_crafterName = player.GetPlayerName();
                    item.m_customData.Remove("VM_Masterwork"); item.m_customData.Remove("VM_MasterworkPatron");
                    item.m_customData[ItemToken] = a.Token; item.m_customData[ItemId] = Guid.NewGuid().ToString("N");
                    item.m_durability = item.GetMaxDurability();
                    mutated = true; // Native Changed callbacks may throw AFTER insertion.
                    if (!inventory.AddItem(item)) throw new InvalidOperationException("Inventory rejected Masterwork insertion.");
                }
                else
                {
                    item = Target(player, a); oldDurability = item.m_durability;
                    hadToken = item.m_customData.TryGetValue(ItemToken, out oldToken);
                    idol = player.GetFirstRequiredItem(inventory, recipe, a.OldQuality + 1, out _, out _, 1);
                    oldStack = idol.m_stack;
                    mutated = true;
                    if (!inventory.RemoveItem(idol, 1)) throw new InvalidOperationException("Inventory rejected selected idol payment.");
                    item.m_quality = a.NewQuality; item.m_durability = item.GetMaxDurability();
                    item.m_customData[ItemToken] = a.Token; inventory.Changed();
                }
                if (Receipt(player, a, Applied))
                {
                    appliedSaved = true;
                }
            }
            catch (Exception e) { MasteryPlugin.Log.LogError("[Gold100] Divine application rolled back: " + e.Message); }
            if (appliedSaved)
            {
                Send(a, Applied);
                if (a.Kind == 1) GoldMasterworkPresentation.Play(player, recipe);
                Tell(a.Kind == 1 ? "Творіння Вьолундра готове." : "Божественне натхнення зміцнило знайомий предмет.",
                    a.Kind == 1 ? "Völundr has completed your creation." : "Divine inspiration has strengthened your equipment.");
                return;
            }
            if (mutated)
            {
                if (a.Kind == 1) inventory.m_inventory.Remove(item);
                else
                {
                    item.m_quality = a.OldQuality; item.m_durability = oldDurability;
                    if (hadToken) item.m_customData[ItemToken] = oldToken; else item.m_customData.Remove(ItemToken);
                    idol.m_stack = oldStack;
                    if (!inventory.ContainsItem(idol)) inventory.m_inventory.Add(idol);
                }
                // Restore authoritative data first. Presentation callbacks cannot prevent rollback.
                try { inventory.Changed(); }
                catch (Exception e) { MasteryPlugin.Log.LogError("[Gold100] Rolled back data; inventory callback failed: " + e.Message); }
            }
            // No ACK unless the rollback has itself been durably saved. A storage fault leaves
            // the server reservation unresolved instead of turning uncertainty into a free item.
            if (Receipt(player, a, Rejected)) Send(a, Rejected);
            else
            {
                QuarantinedPlayer = player.GetPlayerID();
                Tell("Запис творіння не підтверджено. Перезайди, щоб відновити збережену дію; прихильність поки не списана.",
                    "Creation save is uncertain. Rejoin to recover the saved action; Favor has not been acknowledged as spent.");
            }
        }
        internal static void Tick()
        {
            if (Session != ZNet.instance || SessionWorld != (ZNet.instance?.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0))
            {
                CancelMasterwork(Masterwork?.Gui);
                Deferred.Clear(); Validating.Clear(); Masterwork = null;
                Session = ZNet.instance; SessionWorld = Session?.GetWorld() != null ? Session.GetWorldUID() : 0; NextRecovery = 0; QuarantinedPlayer = 0;
            }
            if (Session?.GetWorld() == null) return;
            if (!GoldCraftingService.CraftingEnabled) CancelMasterwork(Masterwork?.Gui);
            if (Deferred.Count > 0)
            {
                try { Deferred.Dequeue()(); }
                catch (Exception e)
                { QuarantinedPlayer = Player.m_localPlayer?.GetPlayerID() ?? 0; MasteryPlugin.Log.LogError("[Gold100] Receipt quarantined: " + e.Message); }
            }
            if (Time.unscaledTime < NextRecovery) return;
            NextRecovery = Time.unscaledTime + 2;
            if (QuarantinedPlayer != 0) return;
            if (ReadReceipt(Player.m_localPlayer, out GoldAction saved, out int phase) && saved != null && phase > 0 &&
                (phase != Requested || GoldCraftingService.CraftingEnabled) && (Session.IsServer() || NetworkSync.HasServerSettings))
            { OwnerSkillAuthority.SendNow(); Send(saved, phase); }
        }
        internal static void UpdateButton(InventoryGui gui, Player player) { }

    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftPressed))]
    internal static class GoldDivineStartPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(InventoryGui __instance) => GoldDivineTransactions.Start(__instance);
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class GoldDivineCraftPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(InventoryGui __instance, Player player) => GoldDivineTransactions.Intercept(__instance, player);
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
    internal static class GoldDivineButtonPatch
    {
        [HarmonyPriority(Priority.Last - 100)]
        private static void Postfix(InventoryGui __instance, Player player)
        {
            GoldDivineTransactions.UpdateButton(__instance, player);
            GoldMasterworkButton.Refresh(__instance, player);
        }
    }
}
