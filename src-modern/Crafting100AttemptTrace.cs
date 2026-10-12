using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ValheimMastery
{
    // Read-only, bounded evidence for an explicitly clicked special action.
    // No identities, tokens, saves, inventory writes or native routing decisions.
    internal static class Crafting100AttemptTrace
    {
        private static InventoryGui Gui;
        private static Player Actor;
        private static ZNet Session;
        private static string StationPrefab;
        private static string Last;
        private static int Sequence, Count;
        private static float Until, Next;
        private static bool Armed, ClickSeen, BeginSeen;
        private static float FirstClickAt;
        private static string LastResult = "Не активовано.";
        internal static string Arm()
        {
            Clear();
            if (Application.isBatchMode || Player.m_localPlayer == null || ZNet.instance == null || InventoryGui.instance == null)
                return LastResult = "C100 trace: потрібний активний тестовий клієнт і персонаж.";
            Gui = InventoryGui.instance; Actor = Player.m_localPlayer; Session = ZNet.instance;
            Armed = true; ClickSeen = false; BeginSeen = false; FirstClickAt = 0; Last = null; Count = 0; Sequence++;
            Until = Time.unscaledTime + 30f; Next = 0;
            LastResult = "C100 trace: активовано на 30 секунд для одного натискання. Закрий консоль і натисни кнопку Вьолундра один раз.";
            Event(Gui,"armed");return LastResult;
        }
        internal static string Disarm() { Finish("Зупинено користувачем."); return LastResult; }
        internal static string Status() { CheckLifetime(); return Armed ? "C100 trace: очікує натискання / результату, залишилось " + Math.Max(0,Until-Time.unscaledTime).ToString("F0") + " с." : LastResult; }
        private static void Finish(string reason)
        {
            if (Armed)
            {
                LastResult = "C100 trace: " + reason;
                try { MasteryPlugin.Log?.LogInfo("[C100Attempt] attempt="+Sequence+" result="+reason); } catch { }
            }
            Clear();
        }
        internal static void Start(InventoryGui gui)
        {
            try
            {
                if (!Active(gui)) return;
                if (BeginSeen) { Finish("Друге натискання спеціальної дії; повторно активуй трасування."); return; }
                BeginSeen = true; if (!ClickSeen) { ClickSeen = true; FirstClickAt = Time.unscaledTime; }
                StationPrefab = Actor?.GetCurrentCraftingStation()?.gameObject.name;
                if (StationPrefab != null) StationPrefab = StationPrefab.Replace("(Clone)", "").Trim();
                Event(gui, "special-click");
            }
            catch { Finish("Помилка спостереження; дію не змінено."); }
        }
        private static void Clear() { Gui = null; Actor = null; Session = null; StationPrefab = null; Last = null; Armed = false; ClickSeen = false; BeginSeen = false; }
        internal static void Stop(InventoryGui gui = null)
        {
            if (ReferenceEquals(gui, null) || ReferenceEquals(Gui, gui)) Finish("Вікно закрито або спостереження зупинено.");
        }
        internal static void CheckLifetime()
        {
            if (!Armed) return;
            if (Time.unscaledTime > Until) { Finish(ClickSeen ? "Час завершився після натискання; дивись журнал." : "30 секунд минули без натискання."); return; }
            if (Gui == null || Actor != Player.m_localPlayer || Session != ZNet.instance || Count >= 31) Finish("Межа журналу або зміна клієнтського сеансу.");
        }
        private static bool Active(InventoryGui gui)
        {
            CheckLifetime();
            if (Gui == null || Gui != gui) return false;
            return true;
        }
        private static string Key(UnityEngine.Object value) => value == null ? "none" : value.name;
        private static string UiRole(InventoryGui gui)
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null) return "none";
            if (gui.m_craftButton != null && (selected == gui.m_craftButton.gameObject || selected.transform.IsChildOf(gui.m_craftButton.transform))) return "native";
            var node = selected.transform;
            for(int i=0;i<8&&node!=null;i++,node=node.parent) if(node.gameObject.name=="ValheimMasteryCreationButton") return "volund";
            return "other";
        }
        private static string State(InventoryGui gui)
        {
            var station = Actor?.GetCurrentCraftingStation();
            var recipe = gui.m_selectedRecipe.Recipe;
            var expected = recipe?.GetRequiredStation(1);
            var table = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            var prefab = string.IsNullOrEmpty(StationPrefab) ? null : ZNetScene.instance?.GetPrefab(StationPrefab);
            var piece = prefab?.GetComponent<Piece>();
            return "selectedRole=" + UiRole(gui) + " specialIntent=" + Crafting100ActionController.HasIntent(gui) + " recipe=" + Key(recipe) + " selected=" + Key(recipe?.m_item) +
                " quality=" + (gui.m_selectedRecipe.ItemData?.m_quality ?? 0) +
                " startGate=" + Crafting100ActionController.StartRefusalReason(gui, Actor) +
                " upgrader=" + (station?.m_upgrader == true) +
                " heldHammer=" + GoldCraftingService.HoldsHammer(Actor) +
                " station=" + Key(station) + " required=" + Key(expected) +
                " usable=" + (station != null && Actor != null && station.InUseDistance(Actor) && WorkshopRemoteCraft.StationUsable(station)) +
                " timer=" + gui.m_craftTimer.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                " craft=" + Key(gui.m_craftRecipe) + " buildPrefab=" + (StationPrefab ?? "none") +
                " registered=" + (prefab != null) + " enabled=" + (piece?.m_enabled == true) +
                " hammer=" + (prefab != null && table?.m_pieces.Contains(prefab) == true) +
                " available=" + (piece != null && table?.m_availablePieces.Contains(piece) == true) +
                " known=" + (piece != null && Actor?.m_knownRecipes.Contains(piece.m_name) == true) +
                " permitReady=" + Crafting100PermitService.Ready + " outstanding=" + Crafting100PermitService.Outstanding +
                " craftCD=" + Crafting100PermitService.CraftRemaining.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                " forgeCD=" + Crafting100PermitService.ForgeRemaining.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                " workshopBusy=" + WorkshopRemoteCraft.ClientBusy;
        }
        internal static void Event(InventoryGui gui, string stage, bool uncorrelated = false)
        {
            try
            {
                if (!Active(gui)) return;
                if (stage == "ordinary-start-allowed" || stage == "ordinary-start-blocked")
                {
                    if (ClickSeen && Time.unscaledTime > FirstClickAt + .1f) { Finish("Друге натискання поза першою спробою; повторно активуй трасування."); return; }
                    if (!ClickSeen) { ClickSeen = true; FirstClickAt = Time.unscaledTime; }
                }
                Count++;
                MasteryPlugin.Log?.LogInfo("[C100Attempt] " + (uncorrelated ? "window=" : "attempt=") + Sequence +
                    (uncorrelated ? " uncorrelated=true" : "") + " event=" + stage + " " + State(gui) + (stage == "armed" ? " wire="+NetworkSync.WireIdentity+" peer="+(Session.IsServer()?"local-host":NetworkSync.HasServerSettings?"matching-confirmed":"unconfirmed")+" loadedMvid="+typeof(Crafting100AttemptTrace).Assembly.ManifestModule.ModuleVersionId : ""));
                if (stage == "native-craft-ran") Finish("Спостережено звичайний шлях крафту; дивись intent та selectedRole у журналі.");
                else if (stage.StartsWith("application-",StringComparison.Ordinal)) Finish("Спостережено відкладене застосування, без прив'язки до натискання.");
            }
            catch { } // Diagnostics must never alter action completion.
        }
        internal static void Poll(InventoryGui gui)
        {
            try
            {
                if (!Active(gui) || Time.unscaledTime < Next) return;
                Next = Time.unscaledTime + .25f;
                // Exclude timer and cooldown values; only discrete changes emit.
                string state = State(gui);
                int begin = state.IndexOf(" timer=", StringComparison.Ordinal);
                int end = state.IndexOf(" craft=", StringComparison.Ordinal);
                string stable = begin >= 0 && end > begin ? state.Remove(begin, end - begin) : state;
                begin = stable.IndexOf(" craftCD=", StringComparison.Ordinal);
                end = stable.IndexOf(" workshopBusy=", StringComparison.Ordinal);
                if (begin >= 0 && end > begin) stable = stable.Remove(begin, end - begin);
                if (Last == stable) return;
                Last = stable; Event(gui, "state-change");
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class Crafting100AttemptHide
    {
        // Existing Hide prefix observes cancellation first; release afterwards.
        private static void Postfix(InventoryGui __instance) => Crafting100AttemptTrace.Stop(__instance);
    }
    [HarmonyPatch(typeof(GoldMasterworkButton), "OnDisable")]
    internal static class Crafting100AttemptDisable
    {
        private static void Postfix() => Crafting100AttemptTrace.Stop();
    }
    [HarmonyPatch(typeof(GoldMasterworkButton), "OnDestroy")]
    internal static class Crafting100AttemptButtonDestroy
    {
        private static void Postfix() => Crafting100AttemptTrace.Stop();
    }
    [HarmonyPatch(typeof(MasteryPlugin), "OnDestroy")]
    internal static class Crafting100AttemptShutdown
    {
        private static void Prefix() => Crafting100AttemptTrace.Stop();
    }
    [HarmonyPatch(typeof(MasteryPlugin), "Update")]
    internal static class Crafting100AttemptLifetime
    {
        // Constant-time reference/lifetime checks only; no state snapshots/logs.
        private static void Postfix() => Crafting100AttemptTrace.CheckLifetime();
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class Crafting100AttemptNativeOutcome
    {
        private static void Postfix(InventoryGui __instance, bool __runOriginal)
            => Crafting100AttemptTrace.Event(__instance, __runOriginal ? "native-craft-ran" : "native-craft-skipped");
    }
    [HarmonyPatch(typeof(WorkshopRemoteCraft), nameof(WorkshopRemoteCraft.AllowCraft))]
    internal static class Crafting100AttemptWorkshopOutcome
    {
        private static void Postfix(InventoryGui gui, bool __result)
            => Crafting100AttemptTrace.Event(gui, __result ? "workshop-pass" : "workshop-intercept");
    }
}

