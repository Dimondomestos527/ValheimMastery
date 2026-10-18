using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class Crafting100ActionPreview
    {
        internal Crafting100ActionPreview(bool visible, bool enabled, bool shift, Crafting100ButtonStatus status,
            string caption, string tooltip, string reason)
        { Visible=visible; Enabled=enabled; Shift=shift; Status=status; Caption=caption; Tooltip=tooltip; Reason=reason; }
        internal bool Visible {get;} internal bool Enabled {get;} internal bool Shift {get;}
        internal Crafting100ButtonStatus Status {get;}
        internal string Caption {get;} internal string Tooltip {get;} internal string Reason {get;}
    }
    internal enum Crafting100ButtonStatus { Action, Pending, Unconfirmed, ForgeUsed, Cooldown }
    // Domain intent/validation/application; shared durable permit transport remains Gold-owned.
    internal static class Crafting100ActionController
    {
        private sealed class Intent
        {
            internal InventoryGui Gui; internal Player Player; internal ZNet Session;
            internal Recipe Recipe; internal ItemDrop.ItemData Target; internal int Kind, Variant, OldQuality;
            internal ZDOID Station; internal bool Shift; internal int Operation,NewQuality,OutputAmount;
        }
        private static Intent Current;
        private static bool Configured;
        internal static void Configure()
        {
            if (Configured) return;
            Crafting100PermitService.Configure(ServerDuration, ApplyLocal, MigrationDuration, PrepareLegacy);
            Configured = true;
        }
        internal static bool Eligible(ItemDrop.ItemData item) => item?.m_shared != null &&
            item.m_shared.m_maxStackSize > 0 && item.m_shared.m_maxQuality > 0;
        private static bool ShiftHeld => ZInput.GetKey(KeyCode.LeftShift,false) || ZInput.GetKey(KeyCode.RightShift,false);
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
        private static GoldAction Build(InventoryGui gui, Player player, bool assignIdentity, bool shift)
        {
            var recipe = gui?.m_selectedRecipe.Recipe;
            var item = gui?.m_selectedRecipe.ItemData;
            var station = player?.GetCurrentCraftingStation();
            if(Crafting100ActionApplication.FoodAction(recipe,station,item==null?1:item.m_quality+1))return null;
            bool forge=station?.m_upgrader==true;
            if (!Crafting100ActionApplication.TryOutcome(recipe,item,forge,shift,out int operation,out int quality,out int amount,out _)) return null;
            var action = new GoldAction { WireVersion=2, Operation=operation, Shift=operation==3?false:shift,
                OutputAmount=amount, Kind = operation==3 ? 2 : 1,
                Token = assignIdentity ? Guid.NewGuid().ToString("N") : "", Recipe = recipe.name, Prefab = recipe.m_item.gameObject.name,
                Variant = item?.m_variant ?? gui.m_selectedVariant, OldQuality = item?.m_quality ?? 0,
                NewQuality = quality,
                Station = station?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None, ItemIdentity = "", Idol = "" };
            if (item != null)
            {
                if (!item.m_customData.TryGetValue(Crafting100ActionApplication.IdentityKey, out action.ItemIdentity))
                {
                    if (assignIdentity) item.m_customData[Crafting100ActionApplication.IdentityKey] = action.ItemIdentity = Guid.NewGuid().ToString("N");
                    else action.ItemIdentity = "";
                }
                if (!string.IsNullOrEmpty(action.ItemIdentity) && !Guid.TryParseExact(action.ItemIdentity, "N", out _)) return null;
                if(operation==3)
                {
                    var idol = Crafting100ActionApplication.SelectRequiredIdol(player.GetInventory(), recipe, item.m_quality + 1, item);
                    if (idol != null) { action.Idol = TierDatabase.ItemKey(idol); action.IdolQuality = idol.m_quality; }
                }
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
            double remaining = Math.Max(Crafting100PermitService.ForgeRemaining,Crafting100PermitService.CraftRemaining);
            if (!double.IsFinite(remaining) || remaining < 0) return Crafting100ButtonStatus.Unconfirmed;
            return remaining > 0 ? Crafting100ButtonStatus.Cooldown : Crafting100ButtonStatus.Action;
        }
        internal static bool CanStart(InventoryGui gui, Player player)
            => Preview(gui,player).Enabled;
        internal static Crafting100ActionPreview Preview(InventoryGui gui,Player player)
        {
            bool shift=ShiftHeld;var recipe=gui?.m_selectedRecipe.Recipe;var item=gui?.m_selectedRecipe.ItemData;
            bool visible=Visible(player)&&gui!=null&&InventoryGui.IsVisible()&&Eligible(recipe?.m_item?.m_itemData)&&gui.m_craftTimer<0&&
                !Crafting100ActionApplication.FoodAction(recipe,player?.GetCurrentCraftingStation(),item==null?1:item.m_quality+1);
            bool forge=player?.GetCurrentCraftingStation()?.m_upgrader==true;
            var status=ButtonStatus(forge,item);string reason="";
            var action=visible?Build(gui,player,false,shift):null;
            if(visible&&action==null)Crafting100ActionApplication.TryOutcome(recipe,item,forge,shift,out _,out _,out _,out reason);
            if(visible&&action!=null&&!LocalValid(player,action,recipe,item,false))reason=GoldUiLocalization.Text("The recipe, station level or selected item does not permit this action.","Рецепт, рівень станції або вибраний виріб не дозволяє цей дар.");
            bool enabled=visible&&gui.m_currentContainer==null&&Current==null&&!Busy&&status==Crafting100ButtonStatus.Action&&
                action!=null&&reason.Length==0&&Crafting100PermitService.CanStart(action.Kind);
            string caption=forge?GoldUiLocalization.Text("Forge with Völundr · +3","Кувати з Вьолундром · +3"):
                item!=null?GoldUiLocalization.Text("Völundr's upgrade","Покращення Вьолундра")+" · +"+(shift?2:1):
                GoldUiLocalization.Text("Völundr's creation","Творіння Вьолундра")+(action!=null?" · "+action.OutputAmount+GoldUiLocalization.Text(" item(s), quality "," шт., якість ")+action.NewQuality:"");
            string tooltip=forge?GoldUiLocalization.Text("Guarantee +3 quality for one required idol, once per item. Shared cooldown: 15 minutes.","Гарантовано підвищити якість на 3 за один потрібний ідол, лише раз на предмет. Спільне відновлення: 15 хвилин."):
                GoldUiLocalization.Text("Perform the selected action without recipe materials. Shift strengthens equipment or creates five full recipe portions. Native item and station limits apply. All Völundr actions share one cooldown.","Виконати вибрану дію без матеріалів рецепта. Shift посилює спорядження або створює п’ять повних порцій рецепта. Діють межі предмета й станції. Усі дії Вьолундра мають спільне відновлення.");
            if(status==Crafting100ButtonStatus.Pending){caption=GoldUiLocalization.Text("Unfinished action","Незавершена дія");tooltip=GoldUiLocalization.Text("The previous action has not been confirmed.","Попередню дію ще не підтверджено.");}
            else if(status==Crafting100ButtonStatus.Unconfirmed){caption=GoldUiLocalization.Text("State not confirmed","Стан не підтверджено");}
            else if(status==Crafting100ButtonStatus.ForgeUsed){caption=GoldUiLocalization.Text("Gift already used","Дар уже використано");tooltip=GoldUiLocalization.Text("This item has already received this gift. Use ordinary forge upgrades.","Вьолундр уже зміцнив цей виріб. Далі покращуй його звичайним способом.");}
            else if(status==Crafting100ButtonStatus.Cooldown)caption+=" · "+Math.Ceiling(Math.Max(Crafting100PermitService.CraftRemaining,Crafting100PermitService.ForgeRemaining)/60d).ToString("0")+GoldUiLocalization.Text(" min"," хв");
            if(reason.Length!=0&&status==Crafting100ButtonStatus.Action)tooltip=reason;
            return new Crafting100ActionPreview(visible,enabled,shift,status,caption,tooltip,reason);
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
            var action = Build(gui, player, false,ShiftHeld);
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
            bool shift=ShiftHeld;var plan=Build(gui,player,false,shift);
            if(plan==null)return;
            // Match native OnCraftPressed focus before starting its timer.
            if (gui.m_uiGroups == null || gui.m_uiGroups.Length <= 3 || gui.m_uiGroups[3] == null)
            { Crafting100AttemptTrace.Event(gui, "native-group-missing"); return; }
            gui.SetActiveGroup(gui.m_uiGroups[3], true);
            var item = gui.m_selectedRecipe.ItemData; var recipe = gui.m_selectedRecipe.Recipe;
            Current = new Intent { Gui = gui, Player = player, Session = ZNet.instance, Recipe = recipe, Target = item,
                Kind = plan.Kind, Shift=shift, Operation=plan.Operation,NewQuality=plan.NewQuality,OutputAmount=plan.OutputAmount, Variant = item?.m_variant ?? gui.m_selectedVariant,
                OldQuality = item?.m_quality ?? 0, Station = player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid ?? ZDOID.None };
            gui.m_craftRecipe = recipe; gui.m_craftUpgradeItem = item; gui.m_craftVariant = Current.Variant;
            gui.m_multiCrafting = false; gui.m_touchMultiCrafting = false; gui.m_craftTimer = 0;
            Crafting100AttemptTrace.Event(gui, "special-timer-started");
            var intent = Current;
            GoldMasterworkPresentation.Begin(gui, player, recipe, () => ReferenceEquals(Current, intent) && Context(intent), plan.Operation);
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
            var action = Build(gui, player, true,intent.Shift);
            if (action == null || action.Operation!=intent.Operation||action.NewQuality!=intent.NewQuality||action.OutputAmount!=intent.OutputAmount||
                !LocalValid(player, action, intent.Recipe, intent.Target, true))
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
            if(action?.WireVersion==2&&Crafting100ActionApplication.FoodAction(recipe,player?.GetCurrentCraftingStation(),action.NewQuality))return false;
            if(action?.WireVersion==1||action?.Operation==3)return LegacyLocalValid(player,action,recipe,target,identity);
            if(!Visible(player)||action?.WireVersion!=2||player.IsDead()||player.IsTeleporting()||recipe?.m_enabled!=true||
                !Eligible(recipe.m_item?.m_itemData)||!player.m_knownRecipes.Contains(recipe.m_item.m_itemData.m_shared.m_name))return false;
            var station=player.GetCurrentCraftingStation();var shared=recipe.m_item.m_itemData.m_shared;
            if(station?.m_upgrader==true||(station?.m_nview?.GetZDO()?.m_uid??ZDOID.None)!=action.Station||
                station!=null&&(!station.InUseDistance(player)||!WorkshopRemoteCraft.StationUsable(station))||
                shared.m_icons==null||action.Variant<0||action.Variant>=shared.m_icons.Length||
                !string.IsNullOrEmpty(shared.m_dlc)&&DLCMan.instance?.IsDLCInstalled(shared.m_dlc)!=true)return false;
            if(!Crafting100ActionApplication.TryOutcome(recipe,target,false,action.Shift,out int operation,out int quality,out int amount,out _)||
                operation!=action.Operation||quality!=action.NewQuality||amount!=action.OutputAmount||
                !player.RequiredCraftingStation(recipe,quality,true))return false;
            if(operation==1)return target==null&&Crafting100ActionApplication.CanFit(player.GetInventory(),recipe,quality,amount);
            return target!=null&&player.GetInventory().ContainsItem(target)&&target.m_stack==1&&target.m_quality==action.OldQuality&&target.m_variant==action.Variant&&
                TierDatabase.ItemKey(target)==action.Prefab&&(!identity||target.m_customData.TryGetValue(Crafting100ActionApplication.IdentityKey,out var key)&&key==action.ItemIdentity);
        }
        private static bool LegacyLocalValid(Player player, GoldAction action, Recipe recipe, ItemDrop.ItemData target, bool identity)
        {
            if (!Visible(player) || action == null || player.IsDead() || player.IsTeleporting() || recipe?.m_enabled != true ||
                !GoldDivineTransactions.Eligible(recipe.m_item?.m_itemData) || !player.m_knownRecipes.Contains(recipe.m_item.m_itemData.m_shared.m_name)) return false;
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
            int tier=LegacyRecipeTier(recipe);string prefab=recipe?.m_item?.gameObject?.name;
            return prefab=="HelmetMage_Ashlands"||prefab=="ArmorMageLegs_Ashlands"||prefab=="ArmorMageChest_Ashlands"?Math.Max(7,tier):tier;
        }
        private static int LegacyRecipeTier(Recipe recipe)
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
            if (action?.WireVersion!=1 || recipe?.m_item?.m_itemData == null || !GoldDivineTransactions.Eligible(recipe.m_item.m_itemData)) return 0;
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
            var recipe = RecipeFor(action); var target = NeedsTarget(action) ? TargetFor(player, action) : null;
            return ZNet.instance != null && !WorkshopRemoteCraft.ClientBusy && !WorkshopRecovery.Outstanding &&
                InventoryGui.IsVisible() && InventoryGui.instance != null && InventoryGui.instance.m_craftTimer < 0 &&
                InventoryGui.instance.m_selectedRecipe.Recipe == recipe && InventoryGui.instance.m_selectedRecipe.ItemData == target &&
                LocalValid(player, action, recipe, target, true);
        }
        private static double PerfectDuration(Recipe recipe, bool historical = false)
        {
            int tier = LegacyRecipeTier(recipe);
            if (tier == 0) return 0;
            if (historical) return Crafting100CooldownRules.PerfectCraftSeconds(tier);
            var type = recipe.m_item.m_itemData.m_shared.m_itemType;
            bool armor = type == ItemDrop.ItemData.ItemType.Helmet || type == ItemDrop.ItemData.ItemType.Chest ||
                type == ItemDrop.ItemData.ItemType.Legs || type == ItemDrop.ItemData.ItemType.Shoulder;
            return Crafting100CooldownRules.PerfectCraftSeconds(tier, armor);
        }
        private static double ServerDuration(WorkshopActor actor, GoldAction action, bool environment)
        {
            if(action?.WireVersion==1)return LegacyServerDuration(actor,action,environment);
            if(action?.WireVersion!=2||!MasteryPlugin.Settings.Enabled.Value||actor?.Available!=true||actor.IsDead()||actor.IsTeleporting()||!GoldCraftingService.HasGold(actor)||
                !float.IsFinite(actor.Position.x)||!float.IsFinite(actor.Position.y)||!float.IsFinite(actor.Position.z))return 0;
            var recipe=RecipeFor(action);var item=recipe?.m_item?.m_itemData;var shared=item?.m_shared;
            if(Crafting100ActionApplication.FoodAction(recipe,null,action.NewQuality)||
                action.Station!=ZDOID.None&&Crafting100ActionApplication.FoodStation(WorkshopWorldRecords.Find(action.Station)?.Station))return 0;
            if(recipe?.m_enabled!=true||!Eligible(item)||shared.m_icons==null||action.Variant<0||action.Variant>=shared.m_icons.Length)return 0;
            if(action.Operation==3)return !action.Shift&&action.Kind==2&&action.OutputAmount==0&&action.NewQuality==(long)action.OldQuality+3?
                LegacyServerDuration(actor,action,environment):0;
            int quality=action.Operation==1?(action.Shift&&Crafting100ActionApplication.Equipment(item)?3:1):action.OldQuality+(action.Shift?2:1);
            if(action.Kind!=1||action.Operation!=1&&action.Operation!=2||quality<1||quality!=action.NewQuality||quality>shared.m_maxQuality||
                action.Operation==2&&(!Crafting100ActionApplication.Equipment(item)||action.OldQuality<1)||
                action.Operation==1&&(!Crafting100ActionApplication.Quantity(recipe,action.Shift,out int quantity)||quantity!=action.OutputAmount)||
                action.Operation==2&&action.OutputAmount!=0)return 0;
            var required=recipe.GetRequiredStation(quality);
            if(action.Station==ZDOID.None)
            {if(required!=null)return 0;}
            else
            {
                var entry=WorkshopWorldRecords.Find(action.Station);var station=entry?.Station;
                if(station==null||station.m_upgrader||Vector3.Distance(actor.Position,entry.Data.GetPosition())>=station.m_useDistance||
                    !WorkshopWorldRecords.WardAllows(actor.GetPlayerID(),entry.Data.GetPosition())||entry.Level<recipe.GetRequiredStationLevel(quality)||
                    !GoldMasterworkRules.StationTypeMatches(required?.m_name,station.m_name,false,station.m_showBasicRecipies)||
                    environment&&!WorkshopStationProof.Usable(entry.Data,station))return 0;
            }
            if(!Crafting100ActionApplication.Equipment(item))return action.Shift?900:300;
            int tier=RecipeTier(recipe);if(tier<1||tier>8)return 0;
            var type=shared.m_itemType;bool armor=type==ItemDrop.ItemData.ItemType.Helmet||type==ItemDrop.ItemData.ItemType.Chest||type==ItemDrop.ItemData.ItemType.Legs||type==ItemDrop.ItemData.ItemType.Shoulder;
            return Crafting100CooldownRules.PerfectCraftSeconds(tier,armor)*(action.Shift?1.5:1);
        }
        private static double LegacyServerDuration(WorkshopActor actor, GoldAction action, bool environment)
        {
            if (!MasteryPlugin.Settings.Enabled.Value || actor?.Available != true || actor.IsDead() || actor.IsTeleporting() ||
                !GoldCraftingService.HasGold(actor) || !float.IsFinite(actor.Position.x) || !float.IsFinite(actor.Position.y) || !float.IsFinite(actor.Position.z)) return 0;
            var recipe = RecipeFor(action);
            if (recipe?.m_enabled != true || !GoldDivineTransactions.Eligible(recipe.m_item?.m_itemData) || action.Variant < 0 ||
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
            var recipe = RecipeFor(action); var target = NeedsTarget(action) ? TargetFor(player, action) : null;
            Func<bool> valid = () => ZNet.instance != null && !AnyLegacyReceipt(player, action) && !WorkshopRemoteCraft.ClientBusy && !WorkshopRecovery.Outstanding &&
                InventoryGui.IsVisible() && InventoryGui.instance != null && InventoryGui.instance.m_craftTimer < 0 &&
                InventoryGui.instance.m_selectedRecipe.Recipe == recipe && InventoryGui.instance.m_selectedRecipe.ItemData == target &&
                LocalValid(player, action, recipe, target, true);
            var result = action.WireVersion==2?Crafting100ActionApplication.ApplyV2(player,recipe,target,action,valid,
                ()=>Crafting100PermitService.TrySaveApplied(player,action),()=>Crafting100PermitService.TrySaveRejected(player,action)):
                Crafting100ActionApplication.Apply(player, recipe, target, action.Kind, action.Variant, action.OldQuality,
                action.Token, valid, () => Crafting100PermitService.TrySaveApplied(player, action),
                () => Crafting100PermitService.TrySaveRejected(player, action));
            // A late grant can belong to an earlier click: this is a window observation,
            // never an outcome correlated to the current click/attempt number.
            Crafting100AttemptTrace.Event(InventoryGui.instance, "application-" + result, true);
            if (result == Crafting100ApplicationResult.Applied)
            {
                try { GoldMasterworkPresentation.Play(player, recipe); } catch (Exception) { }
                try { player.Message(MessageHud.MessageType.Center, !NeedsTarget(action) ? "Творіння Вьолундра готове." : action.WireVersion==2&&action.Operation==2?
                    "Вьолундр зміцнив твій виріб.":"Вьолундр зміцнив твій виріб. Удруге цей дар йому не потрібен."); } catch (Exception) { }
                return 2; // Optional presentation cannot prevent the durable success acknowledgement.
            }
            return result == Crafting100ApplicationResult.Rejected ? 3 : 0;
        }
        private static bool NeedsTarget(GoldAction action)=>action!=null&&(action.WireVersion==1?action.Kind==2:action.Operation!=1);
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
