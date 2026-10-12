using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal enum Crafting100ButtonStatus { Action, Pending, Unconfirmed, ForgeUsed, Cooldown }
    // Domain intent/validation/application; shared durable permit transport remains Gold-owned.
    internal static class Crafting100ActionController
    {
        private sealed class Intent
        {
            internal InventoryGui Gui; internal Player Player; internal ZNet Session;
            internal Recipe Recipe; internal ItemDrop.ItemData Target; internal int Kind, Variant, OldQuality;
            internal ZDOID Station;
        }
        private static Intent Current;
        private static bool Configured;
        internal static void Configure()
        {
            if (Configured) return;
            Crafting100PermitService.Configure(ServerDuration, ApplyLocal, MigrationDuration, PrepareLegacy);
            Configured = true;
        }
        internal static bool Eligible(ItemDrop.ItemData item) => GoldDivineTransactions.Eligible(item);
        private static bool Busy => Crafting100PermitService.Outstanding || AnyLegacyReceipt() ||
            WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding;
        private static bool AnyLegacyReceipt(Player actor = null, GoldAction action = null)
        {
            var player = actor ?? Player.m_localPlayer;
            var data = player?.m_customData;
            if (data == null) return false;
            string linkedKey = action != null && ZNet.instance?.GetWorld() != null &&
                Crafting100PermitService.IsLinkedLegacy(player, action)
                ? "VM_Gold_Receipt_" + ZNet.instance.GetWorldUID() : null;
            int checkedKeys = 0;
            foreach (var pair in data)
            {
                if (++checkedKeys > 4096) return true; // Unbounded/unknown profile history is not fresh-action permission.
                if (pair.Key.StartsWith("VM_Gold_Receipt_", StringComparison.Ordinal) && pair.Key != linkedKey) return true;
            }
            return false;
        }
        internal static bool Visible(Player player) => player == Player.m_localPlayer && player != null &&
            MasteryPlugin.Settings.Enabled.Value && PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 100);
        private static GoldAction Build(InventoryGui gui, Player player, bool assignIdentity)
        {
            var recipe = gui?.m_selectedRecipe.Recipe;
            var item = gui?.m_selectedRecipe.ItemData;
            var station = player?.GetCurrentCraftingStation();
            if (recipe?.m_item == null) return null;
            var action = new GoldAction { Kind = station?.m_upgrader == true ? 2 : 1,
                Token = assignIdentity ? Guid.NewGuid().ToString("N") : "", Recipe = recipe.name, Prefab = recipe.m_item.gameObject.name,
                Variant = item?.m_variant ?? gui.m_selectedVariant, OldQuality = item?.m_quality ?? 0,
                NewQuality = item == null ? 5 : item.m_quality + 3,
                Station = station?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None, ItemIdentity = "", Idol = "" };
            if (item != null)
            {
                if (!item.m_customData.TryGetValue(Crafting100ActionApplication.IdentityKey, out action.ItemIdentity))
                {
                    if (assignIdentity) item.m_customData[Crafting100ActionApplication.IdentityKey] = action.ItemIdentity = Guid.NewGuid().ToString("N");
                    else action.ItemIdentity = "";
                }
                if (!string.IsNullOrEmpty(action.ItemIdentity) && !Guid.TryParseExact(action.ItemIdentity, "N", out _)) return null;
                var idol = Crafting100ActionApplication.SelectRequiredIdol(player.GetInventory(), recipe, item.m_quality + 1, item);
                if (idol != null) { action.Idol = TierDatabase.ItemKey(idol); action.IdolQuality = idol.m_quality; }
            }
            return action;
        }
        internal static Crafting100ButtonStatus ButtonStatus(bool forge, ItemDrop.ItemData item)
        {
            if (Current != null || Busy || !string.IsNullOrEmpty(Crafting100PermitService.RecoveryReason))
                return Crafting100ButtonStatus.Pending;
            if (!Crafting100PermitService.Ready) return Crafting100ButtonStatus.Unconfirmed;
            if (forge && (item == null || item.m_customData == null)) return Crafting100ButtonStatus.Unconfirmed;
            if (forge && Crafting100ForgeItemRules.Used(item.m_customData))
                return Crafting100ButtonStatus.ForgeUsed;
            double remaining = forge ? Crafting100PermitService.ForgeRemaining : Crafting100PermitService.CraftRemaining;
            if (!double.IsFinite(remaining) || remaining < 0) return Crafting100ButtonStatus.Unconfirmed;
            return remaining > 0 ? Crafting100ButtonStatus.Cooldown : Crafting100ButtonStatus.Action;
        }
        internal static bool CanStart(InventoryGui gui, Player player)
        {
            if (!Visible(player) || gui == null || gui.m_currentContainer != null || gui.m_craftTimer >= 0 ||
                Current != null || Busy || !Crafting100PermitService.Ready) return false;
            var action = Build(gui, player, false);
            return action != null && Crafting100PermitService.CanStart(action.Kind) &&
                LocalValid(player, action, gui.m_selectedRecipe.Recipe, gui.m_selectedRecipe.ItemData, false);
        }
        // Read-only snapshot for explicitly armed diagnostics, including disabled buttons.
        // CanStart remains the sole admission predicate; this never assigns item identity.
        internal static string StartRefusalReason(InventoryGui gui, Player player)
        {
            if (!Visible(player)) return "perk-or-client-unavailable";
            if (gui == null) return "gui-missing";
            if (gui.m_currentContainer != null) return "container-open";
            if (gui.m_craftTimer >= 0) return "native-timer-busy";
            if (Current != null) return "special-intent-busy";
            if (Busy) return "receipt-or-workshop-busy";
            if (!Crafting100PermitService.Ready) return "permit-unconfirmed";
            var action = Build(gui, player, false);
            if (action == null) return "recipe-or-identity-invalid";
            if (!Crafting100PermitService.CanStart(action.Kind)) return "permit-or-cooldown";
            var target = gui.m_selectedRecipe.ItemData;
            if (action.Kind == 2)
            {
                if (target == null) return "forge-target-missing";
                if (!player.GetInventory().ContainsItem(target)) return "forge-target-not-in-inventory";
                if (Crafting100ForgeItemRules.Used(target.m_customData)) return "forge-target-already-used";
                if (!Crafting100ForgeItemRules.CanUpgrade(target.m_quality, target.m_customData)) return "forge-target-quality-invalid";
                if (string.IsNullOrEmpty(action.Idol)) return "forge-recipe-idol-unavailable";
            }
            return LocalValid(player, action, gui.m_selectedRecipe.Recipe, target, false) ? "ready" : "local-validation";
        }
        internal static void Begin(InventoryGui gui)
        {
            Configure(); var player = Player.m_localPlayer;
            Crafting100AttemptTrace.Start(gui);
            if (!CanStart(gui, player)) { Crafting100AttemptTrace.Event(gui, "start-refused"); return; }
            // Match native OnCraftPressed focus before starting its timer.
            if (gui.m_uiGroups == null || gui.m_uiGroups.Length <= 3 || gui.m_uiGroups[3] == null)
            { Crafting100AttemptTrace.Event(gui, "native-group-missing"); return; }
            gui.SetActiveGroup(gui.m_uiGroups[3], true);
            var item = gui.m_selectedRecipe.ItemData; var recipe = gui.m_selectedRecipe.Recipe;
            Current = new Intent { Gui = gui, Player = player, Session = ZNet.instance, Recipe = recipe, Target = item,
                Kind = item == null ? 1 : 2, Variant = item?.m_variant ?? gui.m_selectedVariant,
                OldQuality = item?.m_quality ?? 0, Station = player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None };
            gui.m_craftRecipe = recipe; gui.m_craftUpgradeItem = item; gui.m_craftVariant = Current.Variant;
            gui.m_multiCrafting = false; gui.m_touchMultiCrafting = false; gui.m_craftTimer = 0;
            Crafting100AttemptTrace.Event(gui, "special-timer-started");
            var intent = Current;
            GoldMasterworkPresentation.Begin(gui, player, recipe, () => ReferenceEquals(Current, intent) && Context(intent));
        }
        private static bool Context(Intent intent) => intent != null && ZNet.instance == intent.Session &&
            Player.m_localPlayer == intent.Player && InventoryGui.IsVisible() &&
            intent.Gui.m_selectedRecipe.Recipe == intent.Recipe && intent.Gui.m_selectedRecipe.ItemData == intent.Target &&
            intent.Gui.m_craftRecipe == intent.Recipe && intent.Gui.m_craftVariant == intent.Variant &&
            (intent.Player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None) == intent.Station &&
            (intent.Target == null ? intent.Gui.m_selectedVariant == intent.Variant :
                intent.Target.m_quality == intent.OldQuality && intent.Target.m_variant == intent.Variant);
        internal static void Refresh(InventoryGui gui)
        {
            Crafting100AttemptTrace.Poll(gui);
            if (Current?.Gui == gui && !Context(Current)) Cancel(gui);
        }
        internal static void Cancel(InventoryGui gui)
        {
            if (Current == null || Current.Gui != gui) return;
            Crafting100AttemptTrace.Event(gui, "special-cancel");
            var intent = Current; Current = null; // Clear before native cancel/Postfix to avoid recursion.
            if (gui != null && gui.m_craftRecipe == intent.Recipe && gui.m_craftTimer >= 0)
                gui.OnCraftCancelPressed();
        }
        internal static bool HasIntent(InventoryGui gui) => Current != null && ReferenceEquals(Current.Gui, gui);
        internal static bool NativeStart(InventoryGui gui)
        {
            bool allowed = Current?.Gui != gui;
            Crafting100AttemptTrace.Event(gui, allowed ? "ordinary-start-allowed" : "ordinary-start-blocked");
            return allowed;
        }
        internal static bool Intercept(InventoryGui gui, Player player)
        {
            if (Current?.Gui != gui) { Crafting100AttemptTrace.Event(gui, "no-special-intent"); return true; }
            Crafting100AttemptTrace.Event(gui, "special-complete-intercept");
            var intent = Current; Current = null;
            if (!Context(intent) || Busy) { Crafting100AttemptTrace.Event(gui, "completion-context-refused"); return false; }
            var action = Build(gui, player, true);
            if (action == null || !LocalValid(player, action, intent.Recipe, intent.Target, true))
            { Crafting100AttemptTrace.Event(gui, "completion-validation-refused"); return false; }
            OwnerSkillAuthority.SendNow();
            Crafting100AttemptTrace.Event(gui, Crafting100PermitService.TryRequest(action) ? "permit-requested" : "permit-request-refused");
            return false; // No vanilla resources, chance roll, extra output or Workshop debit.
        }
        private static Recipe RecipeFor(GoldAction action)
        {
            if (action == null || ObjectDB.instance == null) return null;
            foreach (var recipe in ObjectDB.instance.m_recipes)
                if (recipe != null && recipe.name == action.Recipe && recipe.m_item?.gameObject.name == action.Prefab) return recipe;
            return null;
        }
        private static ItemDrop.ItemData TargetFor(Player player, GoldAction action)
        {
            ItemDrop.ItemData found = null;
            foreach (var item in player.GetInventory().GetAllItems())
                if (item.m_customData.TryGetValue(Crafting100ActionApplication.IdentityKey, out string id) && id == action.ItemIdentity &&
                    TierDatabase.ItemKey(item) == action.Prefab)
                { if (found != null) return null; found = item; }
            return found;
        }
        private static bool LocalValid(Player player, GoldAction action, Recipe recipe, ItemDrop.ItemData target, bool identity)
        {
            if (!Visible(player) || action == null || player.IsDead() || player.IsTeleporting() || recipe?.m_enabled != true ||
                !Eligible(recipe.m_item?.m_itemData) || !player.m_knownRecipes.Contains(recipe.m_item.m_itemData.m_shared.m_name)) return false;
            var shared = recipe.m_item.m_itemData.m_shared;
            if (shared.m_icons == null || action.Variant < 0 || action.Variant >= shared.m_icons.Length ||
                (!string.IsNullOrEmpty(shared.m_dlc) && DLCMan.instance?.IsDLCInstalled(shared.m_dlc) != true)) return false;
            var station = player.GetCurrentCraftingStation();
            if ((station?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None) != action.Station ||
                (station != null && (!station.InUseDistance(player) || !WorkshopRemoteCraft.StationUsable(station)))) return false;
            if (action.Kind == 1) return target == null && GoldMasterworkRules.StationTypeMatches(
                recipe.GetRequiredStation(1)?.m_name, station?.m_name, station?.m_upgrader == true, station?.m_showBasicRecipies == true) &&
                player.GetInventory().CanAddItem(recipe.m_item.gameObject, 1);
            if (action.Kind != 2 || station?.m_upgrader != true || target == null ||
                !player.GetInventory().ContainsItem(target) || target.m_quality != action.OldQuality || target.m_variant != action.Variant ||
                !Crafting100ForgeItemRules.CanUpgrade(target.m_quality, target.m_customData)) return false;
            if (identity && (!target.m_customData.TryGetValue(Crafting100ActionApplication.IdentityKey, out var key) || key != action.ItemIdentity)) return false;
            var idol = Crafting100ActionApplication.SelectRequiredIdol(player.GetInventory(), recipe, action.OldQuality + 1, target);
            return idol != null && TierDatabase.ItemKey(target) == action.Prefab && TierDatabase.ItemKey(idol) == action.Idol &&
                idol.m_quality == action.IdolQuality && idol.m_stack >= 1;
        }
        internal static int RecipeTier(Recipe recipe)
        {
            if (recipe?.m_item?.m_itemData == null) return 0;
            int tier = TierDatabase.GetItemTier(recipe.m_item.m_itemData);
            foreach (var requirement in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
                if (requirement?.m_resItem?.m_itemData != null && requirement.GetAmount(1) > 0 && !requirement.m_upgraderResource)
                    tier = Math.Max(tier, TierDatabase.GetResourceTier(TierDatabase.ItemKey(requirement.m_resItem.m_itemData)));
            return tier >= 1 && tier <= 8 ? tier : 0;
        }
        // Import has no new gameplay effect: server recomputes approved duration from
        // immutable recipe/action, independent of current station/perk availability.
        internal static double MigrationDuration(GoldAction action)
        {
            var recipe = RecipeFor(action);
            if (recipe?.m_item?.m_itemData == null || !Eligible(recipe.m_item.m_itemData)) return 0;
            // Historical import predates the armor discount; do not retime old receipts.
            if (action.Kind == 1) return action.OldQuality == 0 && action.NewQuality == 5 ? PerfectDuration(recipe, true) : 0;
            if (action.Kind != 2 || action.OldQuality < 1 || action.OldQuality > 9997 || action.NewQuality != action.OldQuality + 3) return 0;
            foreach (var requirement in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
                if (requirement?.m_upgraderResource == true && requirement.m_resItem?.gameObject.name == action.Idol &&
                    requirement.GetAmount(action.OldQuality + 1) > 0 && action.IdolQuality >= 0 &&
                    action.IdolQuality <= requirement.m_resItem.m_itemData.m_shared.m_maxQuality)
                    return Crafting100CooldownRules.ForgeSeconds;
            return 0;
        }
        internal static int PrepareLegacy(Player player, GoldAction action, int phase, Func<int, bool> persistLinkedProof)
        {
            if (persistLinkedProof == null) return 0;
            int outcome = phase;
            bool saved = Crafting100LegacyItemApplication.Prepare(player, action, phase, () =>
            {
                // Gold already authenticated the durable loaded snapshot; proof helper
                // succeeded before this callback. Lost CURRENT conditions can reject,
                // while missing/ambiguous inventory proof remains outcome0, never3.
                if (phase == 1 && !DomainApplicationContext(player, action)) outcome = 3;
                return persistLinkedProof(outcome);
            });
            return saved ? outcome : 0;
        }
        private static bool DomainApplicationContext(Player player, GoldAction action)
        {
            var recipe = RecipeFor(action); var target = action?.Kind == 2 ? TargetFor(player, action) : null;
            return ZNet.instance != null && !WorkshopRemoteCraft.ClientBusy && !WorkshopRecovery.Outstanding &&
                InventoryGui.IsVisible() && InventoryGui.instance != null && InventoryGui.instance.m_craftTimer < 0 &&
                InventoryGui.instance.m_selectedRecipe.Recipe == recipe && InventoryGui.instance.m_selectedRecipe.ItemData == target &&
                LocalValid(player, action, recipe, target, true);
        }
        private static double PerfectDuration(Recipe recipe, bool historical = false)
        {
            int tier = RecipeTier(recipe);
            if (tier == 0) return 0;
            if (historical) return Crafting100CooldownRules.PerfectCraftSeconds(tier);
            var type = recipe.m_item.m_itemData.m_shared.m_itemType;
            bool armor = type == ItemDrop.ItemData.ItemType.Helmet || type == ItemDrop.ItemData.ItemType.Chest ||
                type == ItemDrop.ItemData.ItemType.Legs || type == ItemDrop.ItemData.ItemType.Shoulder;
            return Crafting100CooldownRules.PerfectCraftSeconds(tier, armor);
        }
        private static double ServerDuration(WorkshopActor actor, GoldAction action, bool environment)
        {
            if (!MasteryPlugin.Settings.Enabled.Value || actor?.Available != true || actor.IsDead() || actor.IsTeleporting() ||
                !GoldCraftingService.HasGold(actor) || !float.IsFinite(actor.Position.x) || !float.IsFinite(actor.Position.y) || !float.IsFinite(actor.Position.z)) return 0;
            var recipe = RecipeFor(action);
            if (recipe?.m_enabled != true || !Eligible(recipe.m_item?.m_itemData) || action.Variant < 0 ||
                recipe.m_item.m_itemData.m_shared.m_icons == null || action.Variant >= recipe.m_item.m_itemData.m_shared.m_icons.Length) return 0;
            var required = recipe.GetRequiredStation(1);
            if (action.Station == ZDOID.None)
                return action.Kind == 1 && GoldMasterworkRules.StationTypeMatches(required?.m_name, null, false, false)
                    ? PerfectDuration(recipe) : 0;
            var entry = WorkshopWorldRecords.Find(action.Station); var station = entry?.Station;
            if (station == null || Vector3.Distance(actor.Position, entry.Data.GetPosition()) >= station.m_useDistance ||
                !WorkshopWorldRecords.WardAllows(actor.GetPlayerID(), entry.Data.GetPosition()) ||
                (environment && !WorkshopStationProof.Usable(entry.Data, station))) return 0;
            if (action.Kind == 1) return GoldMasterworkRules.StationTypeMatches(required?.m_name, station.m_name,
                station.m_upgrader, station.m_showBasicRecipies) ? PerfectDuration(recipe) : 0;
            if (action.Kind != 2 || !station.m_upgrader) return 0;
            foreach (var requirement in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
                if (requirement?.m_upgraderResource == true && requirement.m_resItem?.gameObject.name == action.Idol &&
                    requirement.GetAmount(action.OldQuality + 1) > 0 && action.IdolQuality >= 0 &&
                    action.IdolQuality <= requirement.m_resItem.m_itemData.m_shared.m_maxQuality) return Crafting100CooldownRules.ForgeSeconds;
            return 0;
        }
        private static int ApplyLocal(Player player, GoldAction action)
        {
            var recipe = RecipeFor(action); var target = action.Kind == 2 ? TargetFor(player, action) : null;
            Func<bool> valid = () => ZNet.instance != null && !AnyLegacyReceipt(player, action) && !WorkshopRemoteCraft.ClientBusy && !WorkshopRecovery.Outstanding &&
                InventoryGui.IsVisible() && InventoryGui.instance != null && InventoryGui.instance.m_craftTimer < 0 &&
                InventoryGui.instance.m_selectedRecipe.Recipe == recipe && InventoryGui.instance.m_selectedRecipe.ItemData == target &&
                LocalValid(player, action, recipe, target, true);
            var result = Crafting100ActionApplication.Apply(player, recipe, target, action.Kind, action.Variant, action.OldQuality,
                action.Token, valid, () => Crafting100PermitService.TrySaveApplied(player, action),
                () => Crafting100PermitService.TrySaveRejected(player, action));
            // A late grant can belong to an earlier click: this is a window observation,
            // never an outcome correlated to the current click/attempt number.
            Crafting100AttemptTrace.Event(InventoryGui.instance, "application-" + result, true);
            if (result == Crafting100ApplicationResult.Applied)
            {
                try { GoldMasterworkPresentation.Play(player, recipe); } catch (Exception) { }
                try { player.Message(MessageHud.MessageType.Center, action.Kind == 1 ? "Творіння Вьолундра готове." : "Вьолундр зміцнив твій виріб. Удруге цей дар йому не потрібен."); } catch (Exception) { }
                return 2; // Optional presentation cannot prevent the durable success acknowledgement.
            }
            return result == Crafting100ApplicationResult.Rejected ? 3 : 0;
        }
    }
    [HarmonyPatch(typeof(MasteryPlugin), "Update")]
    internal static class Crafting100ConfigurePatch { private static void Postfix() => Crafting100ActionController.Configure(); }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftPressed))]
    internal static class Crafting100NativeStartPatch
    { [HarmonyPriority(Priority.First + 200)] private static bool Prefix(InventoryGui __instance) => Crafting100ActionController.NativeStart(__instance); }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class Crafting100NativeCraftPatch
    { [HarmonyPriority(Priority.First + 200)] private static bool Prefix(InventoryGui __instance, Player player) => Crafting100ActionController.Intercept(__instance, player); }
}











