using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Selection only. The transport owns nonce, access and native teleport execution.
    internal static class WorkbenchEnchantmentTravelMap
    {
        internal const int MaxDestinations = 4096, PageSize = 128;
        internal static int CancelFrame = -1;
        private static WorkbenchTravelDestination[] Items;
        private static readonly List<int> Visible = new List<int>();
        private static readonly Dictionary<Minimap.PinData, int> Pins = new Dictionary<Minimap.PinData, int>();
        private static readonly List<int> Near = new List<int>();
        private static Minimap Map;
        private static Minimap.MapMode PriorMode;
        private static Player Player;
        private static ZNet Session;
        private static long World;
        private static GameObject Controls, PriorSelection;
        private static Button Previous, Next;
        private static TMP_Text Count;
        private static YesNoPopup Popup;
        private static Action<ZDOID> Choose;
        private static Action CancelAction;
        private static Rect LastViewport;
        private static int Page, NearIndex, Revision;
        private static float NextDraw;
        private static bool Active, Closing, HasViewport, Dirty;

        private static string Text(string ua, string en) => GoldUiLocalization.Text(en, ua);
        internal static int PageCount(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
        internal static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        private static bool Current() => Active && Map != null && Map == Minimap.instance &&
            Session != null && ReferenceEquals(Session, ZNet.instance) && World != 0 && Session.GetWorldUID() == World &&
            Player != null && Player == global::Player.m_localPlayer && !Player.IsDead() && !Player.IsTeleporting() &&
            !Game.m_noMap && Map.m_mode == Minimap.MapMode.Large;
        private static string Label(WorkbenchTravelDestination item) => Text("Верстак ", "Workbench ") +
            item.Ordinal + " · " + MasterIdolPlayerBuffs.DisplayName(item.EnchantmentId);

        internal static WorkbenchTravelDestination[] Freeze(IReadOnlyList<WorkbenchTravelDestination> destinations)
        {
            if (destinations == null || destinations.Count == 0 || destinations.Count > MaxDestinations)
                throw new ArgumentException("Invalid bounded destination list.");
            var frozen = new WorkbenchTravelDestination[destinations.Count];
            var identities = new HashSet<ZDOID>(); var ordinals = new HashSet<int>();
            for (int i = 0; i < frozen.Length; ++i)
            {
                var item = destinations[i];
                if (item == null || item.Ordinal < 1 || item.Ordinal > MaxDestinations || !Finite(item.Position) ||
                    !identities.Add(item.Id) || !ordinals.Add(item.Ordinal) ||
                    string.IsNullOrEmpty(MasterIdolPlayerBuffs.DisplayName(item.EnchantmentId)))
                    throw new ArgumentException("Invalid immutable destination.");
                frozen[i] = item;
            }
            return frozen;
        }

        internal static void Show(IReadOnlyList<WorkbenchTravelDestination> destinations, Action<ZDOID> choose, Action cancel)
        {
            Close();
            if (destinations == null || destinations.Count == 0 || destinations.Count > MaxDestinations ||
                choose == null || cancel == null || Application.isBatchMode || Game.m_noMap ||
                Minimap.instance == null || global::Player.m_localPlayer == null || ZNet.instance?.GetWorld() == null)
            { cancel?.Invoke(); return; }
            WorkbenchTravelDestination[] frozen;
            try { frozen = Freeze(destinations); }
            catch { cancel(); return; }
            Items = frozen; Choose = choose; CancelAction = cancel;
            Map = Minimap.instance; PriorMode = Map.m_mode; Player = global::Player.m_localPlayer;
            Session = ZNet.instance; World = Session.GetWorldUID();
            PriorSelection = EventSystem.current?.currentSelectedGameObject;
            Active = true; Page = 0; Revision++; Dirty = true; HasViewport = false; NextDraw = 0;
            try
            {
                Map.SetMapMode(Minimap.MapMode.Large);
                if (!Current()) { Cancel(); return; }
                CreateControls(); Draw();
            }
            catch { Cancel(); }
        }

        private static Button MakeButton(Transform parent, string label, float x)
        {
            var donor = InventoryGui.instance?.m_tabUpgrade;
            if (donor == null) throw new InvalidOperationException("Native map page-button donor unavailable.");
            var button = UnityEngine.Object.Instantiate(donor, parent, false);
            button.onClick = new Button.ButtonClickedEvent();
            foreach (var component in button.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string name = component.GetType().Name;
                if (name == "UIInputHandler" || name == "Localize" || name == "Localization" ||
                    component is EventTrigger || component is UITooltip)
                { if (component is Behaviour behaviour) behaviour.enabled = false; UnityEngine.Object.Destroy(component); }
            }
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.enabled = true; button.interactable = true; button.gameObject.SetActive(true);
            var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = new Vector2(x, 32); rect.sizeDelta = new Vector2(160, 32);
            var text = button.GetComponentInChildren<TMP_Text>(true); text.enabled = true; text.text = label;
            text.enableAutoSizing = true; text.fontSizeMin = 10; text.fontSizeMax = 16;
            return button;
        }
        private static void CreateControls()
        {
            if (Map.m_mapImageLarge == null || InventoryGui.instance?.m_recipeName == null)
                throw new InvalidOperationException("Native map layout unavailable.");
            Controls = new GameObject("VM_WorkbenchTravelPages", typeof(RectTransform));
            var root = Controls.GetComponent<RectTransform>(); root.SetParent(Map.m_mapImageLarge.transform, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            Previous = MakeButton(root, Text("Попередня", "Previous"), -86);
            Next = MakeButton(root, Text("Наступна", "Next"), 86);
            Previous.onClick.AddListener(() => ChangePage(-1)); Next.onClick.AddListener(() => ChangePage(1));
            Count = UnityEngine.Object.Instantiate(InventoryGui.instance.m_recipeName, root, false);
            Count.gameObject.SetActive(true); Count.enabled = true; Count.raycastTarget = false;
            var label = Count.rectTransform; label.anchorMin = label.anchorMax = new Vector2(.5f, 0);
            label.pivot = new Vector2(.5f, .5f); label.anchoredPosition = new Vector2(0, 61); label.sizeDelta = new Vector2(380, 24);
            Count.alignment = TextAlignmentOptions.Center; Count.enableAutoSizing = true; Count.fontSizeMin = 10; Count.fontSizeMax = 16;
        }
        private static void ChangePage(int step)
        {
            if (!Current() || Popup != null) return;
            Page = Mathf.Clamp(Page + step, 0, PageCount(Visible.Count) - 1); Dirty = true;
            try { Draw(); } catch { Cancel(); }
        }
        private static void ClearPins()
        {
            try
            {
                foreach (var pin in Pins.Keys)
                {
                    if (Map == null) continue;
                    try { Map.RemovePin(pin); }
                    catch { Map.m_pins.Remove(pin); } // Exact owned reference only; continue cleaning remaining pins.
                }
            }
            finally { Pins.Clear(); }
        }
        private static void Draw()
        {
            if (!Current()) return;
            Rect viewport = Map.m_mapImageLarge.uvRect;
            if (!HasViewport || viewport != LastViewport)
            {
                Visible.Clear();
                for (int i = 0; i < Items.Length; ++i)
                {
                    Map.WorldToMapPoint(Items[i].Position, out float x, out float y);
                    if (viewport.Contains(new Vector2(x, y))) Visible.Add(i);
                }
                Page = 0; LastViewport = viewport; HasViewport = true;
            }
            Page = Mathf.Clamp(Page, 0, PageCount(Visible.Count) - 1);
            ClearPins();
            // None has no user-facing icon filter. Do not let AddPin toggle a user's ordinary filter.
            int type = (int)Minimap.PinType.None;
            if (Map.m_visibleIconTypes == null || type >= Map.m_visibleIconTypes.Length || !Map.m_visibleIconTypes[type])
                throw new InvalidOperationException("Native travel-pin visibility unavailable.");
            int end = Math.Min(Visible.Count, (Page + 1) * PageSize);
            for (int slot = Page * PageSize; slot < end; ++slot)
            {
                int index = Visible[slot]; var item = Items[index];
                var icon = ApprovedUiAssetBank.Enchantment(item.EnchantmentId);
                if (icon == null) throw new InvalidOperationException("Approved travel rune unavailable.");
                var pin = Map.AddPin(item.Position, Minimap.PinType.None, Label(item), false, false);
                Pins.Add(pin, index); pin.m_icon = icon;
            }
            int pages = PageCount(Visible.Count);
            Previous.gameObject.SetActive(pages > 1); Next.gameObject.SetActive(pages > 1);
            Previous.interactable = Page > 0; Next.interactable = Page + 1 < pages;
            Count.text = Visible.Count == 0 ? Text("Перемісти мапу до потрібного верстака.", "Move the map to the desired workbench.") :
                Text("Верстаки у видимій області: ", "Workbenches in view: ") + Visible.Count +
                (pages > 1 ? " · " + (Page + 1) + " / " + pages : "");
            Dirty = false; NextDraw = Time.unscaledTime + .15f;
        }

        // Vanilla GetClosestPin deliberately ignores unsaved pins. Never mark travel pins as saved.
        private static bool OwnHit(out Vector3 point)
        {
            point = Vector3.zero;
            if (!Current() || Pins.Count == 0) return false;
            point = Map.ScreenToWorldPoint(ZInput.pointerPosition);
            float radius = Map.PinInteractRadius, nearest = radius;
            bool found = false;
            foreach (var pin in Pins.Keys)
            {
                if (pin.m_uiElement == null || !pin.m_uiElement.gameObject.activeInHierarchy) continue;
                float distance = Utils.DistanceXZ(point, pin.m_pos);
                if (distance < nearest) { nearest = distance; found = true; }
            }
            if (!found) return false;
            var foreign = Map.GetClosestPin(point, radius);
            return foreign == null || Utils.DistanceXZ(point, foreign.m_pos) > nearest;
        }
        internal static bool LeftClick(Minimap map)
        {
            if (!Active || Map != map || !OwnHit(out Vector3 point)) return true;
            if (Popup != null || UnifiedPopup.instance?.popupStack.Count > 0) return false;
            Near.Clear(); NearIndex = 0;
            foreach (int index in Visible)
                if (Utils.DistanceXZ(point, Items[index].Position) < Map.PinInteractRadius) Near.Add(index);
            if (Near.Count == 0) return false;
            try { Confirm(); } catch { Cancel(); }
            return false;
        }
        private static void Confirm()
        {
            if (!Current() || Near.Count == 0 || UnifiedPopup.instance == null) { Cancel(); return; }
            var item = Items[Near[NearIndex]]; int revision = ++Revision;
            Popup = new YesNoPopup(Text("Подорож між верстаками", "Workbench travel"),
                Label(item) + "\n" + Text("Подорожувати сюди?", "Travel here?"),
                () =>
                {
                    if (!Active || Revision != revision) return;
                    if (!Current()) { Cancel(); return; }
                    var action = Choose; ZDOID identity = item.Id;
                    try { Close(); } finally { action?.Invoke(identity); }
                },
                () =>
                {
                    if (!Active || Revision != revision) return;
                    if (Near.Count <= 1) { Cancel(); return; }
                    PopOwned(); NearIndex = (NearIndex + 1) % Near.Count;
                    try { Confirm(); } catch { Cancel(); }
                }, true, true);
            UnifiedPopup.Push(Popup);
        }
        internal static void Labels(YesNoPopup popup)
        {
            if (!ReferenceEquals(popup, Popup) || UnifiedPopup.instance == null) return;
            UnifiedPopup.instance.buttonRightText.text = Text("Подорожувати", "Travel");
            UnifiedPopup.instance.buttonLeftText.text = Near.Count > 1 ? Text("Наступний поруч", "Next nearby") : Text("Скасувати", "Cancel");
        }
        private static void PopOwned()
        {
            if (Popup == null || UnifiedPopup.instance == null) { Popup = null; return; }
            var ui = UnifiedPopup.instance;
            if (ui.popupStack.Count > 0 && ReferenceEquals(ui.popupStack.Peek(), Popup))
            { UnifiedPopup.Pop(); Popup = null; return; }
            var displaced = new List<PopupBase>();
            while (ui.popupStack.Count > 0)
            {
                var entry = ui.popupStack.Pop(); if (ReferenceEquals(entry, Popup)) break; displaced.Add(entry);
            }
            for (int i = displaced.Count - 1; i >= 0; --i) ui.popupStack.Push(displaced[i]);
            Popup = null;
        }
        internal static void Cancel()
        {
            if (!Active) return;
            var action = CancelAction; CancelFrame = Time.frameCount;
            try { Close(); } finally { action?.Invoke(); }
        }
        internal static void Close()
        {
            if (Closing) return;
            Closing = true; Active = false; Revision++; Choose = null; CancelAction = null;
            var system = EventSystem.current; var selected = system?.currentSelectedGameObject;
            bool focusOwned = selected != null && Controls != null && selected.transform.IsChildOf(Controls.transform);
            if (Popup != null && UnifiedPopup.instance != null && UnifiedPopup.instance.popupStack.Count > 0 &&
                ReferenceEquals(UnifiedPopup.instance.popupStack.Peek(), Popup)) focusOwned = true;
            try
            {
                try { PopOwned(); } catch { }
                try { ClearPins(); } catch { }
                try { if (Controls != null) { Controls.SetActive(false); UnityEngine.Object.Destroy(Controls); } } catch { }
                try { if (Map != null && Map == Minimap.instance && Map.m_mode == Minimap.MapMode.Large) Map.SetMapMode(PriorMode); } catch { }
                try
                {
                    if (focusOwned && system != null) system.SetSelectedGameObject(
                        PriorSelection != null && PriorSelection.activeInHierarchy ? PriorSelection : null);
                }
                catch { }
            }
            finally
            {
                Map = null; Player = null; Session = null; World = 0; Items = null;
                Visible.Clear(); Near.Clear(); Controls = null; PriorSelection = null;
                Previous = Next = null; Count = null; HasViewport = Dirty = false; Closing = false;
            }
        }
        internal static void ModeChanged(Minimap map)
        { if (Active && !Closing && Map == map && map.m_mode != Minimap.MapMode.Large) Cancel(); }
        internal static bool RemoveOwnPin(Minimap map) => !Active || Map != map || !OwnHit(out _);
        internal static void Tick()
        {
            if (!Active) return;
            try
            {
                if (!Current() || Controls == null) { Cancel(); return; }
                if (Popup != null)
                {
                    var ui = UnifiedPopup.instance;
                    if (ui == null || !ui.popupStack.Contains(Popup)) { Cancel(); return; }
                    if (ReferenceEquals(ui.popupStack.Peek(), Popup) &&
                        (ZInput.GetKeyDown(KeyCode.Escape, true) || ZInput.GetButtonDown("JoyButtonB")))
                    { ZInput.ResetButtonStatus("JoyButtonB"); Cancel(); }
                    return;
                }
                if (!HasViewport || Map.m_mapImageLarge.uvRect != LastViewport) Dirty = true;
                if (Dirty && Time.unscaledTime >= NextDraw) Draw();
            }
            catch { Cancel(); }
        }
    }
    [HarmonyPatch(typeof(Minimap), "OnMapLeftClick")]
    internal static class WorkbenchTravelMapClickPatch
    { private static bool Prefix(Minimap __instance) => WorkbenchEnchantmentTravelMap.LeftClick(__instance); }
    [HarmonyPatch(typeof(Minimap), "OnMapDblClick")]
    internal static class WorkbenchTravelMapDoubleClickPatch
    { private static bool Prefix(Minimap __instance) => WorkbenchEnchantmentTravelMap.LeftClick(__instance); }
    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    internal static class WorkbenchTravelMapRemovePatch
    { private static bool Prefix(Minimap __instance) => WorkbenchEnchantmentTravelMap.RemoveOwnPin(__instance); }
    [HarmonyPatch(typeof(Minimap), "SetMapMode")]
    internal static class WorkbenchTravelMapModePatch
    { private static void Postfix(Minimap __instance) => WorkbenchEnchantmentTravelMap.ModeChanged(__instance); }
    [HarmonyPatch(typeof(MasteryPlugin), "Update")]
    internal static class WorkbenchTravelMapTickPatch
    { private static void Postfix() => WorkbenchEnchantmentTravelMap.Tick(); }
    [HarmonyPatch(typeof(MasteryPlugin), "OnDestroy")]
    internal static class WorkbenchTravelMapCleanupPatch
    { private static void Prefix() => WorkbenchEnchantmentTravelMap.Close(); }
    [HarmonyPatch(typeof(UnifiedPopup), "ShowYesNo")]
    internal static class WorkbenchTravelMapLabelsPatch
    { private static void Postfix(YesNoPopup __0) => WorkbenchEnchantmentTravelMap.Labels(__0); }
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class WorkbenchTravelMapCancelFramePatch
    { private static bool Prefix() => Time.frameCount != WorkbenchEnchantmentTravelMap.CancelFrame; }
}
