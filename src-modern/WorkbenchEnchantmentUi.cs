using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Native visual/input adapter only. Snapshot and operations are certified by the domain binding.
    internal sealed class WorkbenchEnchantmentUi : MonoBehaviour
    {
        private static Func<Player, CraftingStation, WorkbenchEnchantmentSnapshot> Read;
        private static Func<Player, CraftingStation, WorkbenchEnchantmentSnapshot, WorkbenchOperationKind, string, Action<bool, string>, bool> Request;
        private static Func<string, Sprite> Icon;
        private static Func<string, string> Detail;
        private static readonly string[] Choices = { "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands" };
        private InventoryGui _gui;
        private Button _tab;
        private GameObject _panel, _priorSelection;
        private TMP_Text _detail;
        private Image _detailIcon, _requirementIcon;
        private TMP_Text _requirementText;
        private Color _requirementBaseColor;
        private bool _requirementMissing;
        private ZNet _requirementSession;
        private ScrollRect _detailScroll;
        private RectTransform _detailViewport;
        private Button _learn, _switch;
        private readonly List<Button> _choices = new List<Button>();
        private readonly Dictionary<GameObject, bool> _hidden = new Dictionary<GameObject, bool>();
        private struct TabLayout
        { internal Vector2 Min, Max, Pivot, Position, Size; internal bool AutoSize, Wrap; internal float FontMin, FontMax; }
        private TabLayout _craftLayout, _upgradeLayout;
        private bool _tabLayoutOwned;
        private WorkbenchEnchantmentSnapshot _snapshot;
        private Player _player;
        private CraftingStation _station;
        private string _selected = "Meadows", _message = "";
        private bool _open, _pending;
        private bool _nativeTabsOwned, _priorCraftTabInteractable, _priorUpgradeTabInteractable;
        private UIGroupHandler _priorUiGroup, _ownedUiGroup;
        private static int _traceClicks, _traceLines;
        private static bool _traceBound;
        private int _epoch;
        private long _revision = -1;
        private float _nextRefresh;

        internal static void Configure(Func<Player, CraftingStation, WorkbenchEnchantmentSnapshot> read,
            Func<Player, CraftingStation, WorkbenchEnchantmentSnapshot, WorkbenchOperationKind, string, Action<bool, string>, bool> request,
            Func<string, Sprite> icon, Func<string, string> detail)
        { Read = read; Request = request; Icon = icon; Detail = detail; }
        internal static void Shutdown()
        {
            Read = null; Request = null; Icon = null; Detail = null;
            var ui = InventoryGui.instance?.GetComponent<WorkbenchEnchantmentUi>();
            if (ui != null) { ui.enabled = false; ui.Close(); if (ui._tab != null) ui._tab.gameObject.SetActive(false); Destroy(ui); }
        }
        private static string Text(string ua, string en) => GoldUiLocalization.Text(en, ua);
        private static bool Contains(string[] values, string value) => values != null && Array.IndexOf(values, value) >= 0;
        private static WorkbenchEnchantmentChoiceCooldown[] CopyCooldowns(WorkbenchEnchantmentChoiceCooldown[] values)
        {
            if (values == null || values.Length != Choices.Length) return null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var copy = new WorkbenchEnchantmentChoiceCooldown[Choices.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                var entry = values[i];
                if (entry == null || Array.IndexOf(Choices, entry.Choice) < 0 || !seen.Add(entry.Choice) ||
                    !double.IsFinite(entry.RemainingSeconds) || entry.RemainingSeconds < 0 ||
                    (entry.Ready && (entry.Pending || entry.RemainingSeconds != 0))) return null;
                copy[i] = new WorkbenchEnchantmentChoiceCooldown { Choice = entry.Choice, Ready = entry.Ready,
                    Pending = entry.Pending, RemainingSeconds = entry.RemainingSeconds };
            }
            return copy;
        }
        private WorkbenchEnchantmentChoiceCooldown SelectedCooldown()
        {
            if (_snapshot?.Cooldowns == null) return null;
            foreach (var entry in _snapshot.Cooldowns) if (entry.Choice == _selected) return entry;
            return null;
        }
        private static bool CooldownReady(WorkbenchEnchantmentChoiceCooldown value) => value != null &&
            value.Ready && !value.Pending && value.RemainingSeconds == 0;
        private string CooldownText()
        {
            if (Contains(_snapshot?.Learned, _selected)) return "";
            var value = SelectedCooldown();
            if (value == null) return Text("Кулдаун не підтверджено.", "Cooldown not confirmed.");
            if (value.Pending) return Text("Очікує підтвердження.", "Awaiting confirmation.");
            if (CooldownReady(value)) return "";
            if (value.RemainingSeconds > 0)
            {
                double seconds = Math.Ceiling(value.RemainingSeconds);
                return Text("Відновлення: ", "Cooldown: ") + Math.Floor(seconds / 60).ToString("0") + ":" + (seconds % 60).ToString("00");
            }
            return Text("Кулдаун не підтверджено.", "Cooldown not confirmed.");
        }
        private static string ReasonText(string reason)
        {
            switch (reason)
            {
                case "pending": return Text("Незавершена дія. Дочекайся підтвердження.", "Unfinished action. Await confirmation.");
                case "materials": case "insufficient-materials": return Text("Бракує матеріалів.", "Not enough materials.");
                case "capacity": return Text("Потрібний вищий рівень верстака.", "A higher workbench level is required.");
                case "network": return Text("Це зачарування зараз недоступне в мережі майстерні.", "This enchantment is unavailable in the workshop network.");
                case "world-type-busy": return Text("Цей дар уже пробуджено в іншому верстаку цього світу.", "This gift is already awakened at another workbench in this world.");
                case "access": return Text("Немає доступу до верстака.", "No access to this workbench.");
                case "unconfirmed": return Text("Стан не підтверджено.", "State not confirmed.");
                default: return Text("Дія поки недоступна.", "Action unavailable for now.");
            }
        }
        private static string CapacityReason(WorkbenchEnchantmentSnapshot snapshot, string selected, bool blocked)
        {
            if (blocked || snapshot?.Learned == null || snapshot.NativeLevel < 1 ||
                Contains(snapshot.Learned, selected) || snapshot.Learned.Length < WorkbenchEnchantmentRules.Capacity(snapshot.NativeLevel)) return "";
            return Text("Щоб відкрити ще один дар, підвищ рівень верстака.", "Upgrade the workbench to unlock another gift.");
        }
        private static bool Bench(CraftingStation station) => station != null && station.m_name == "$piece_workbench";
        private static string ChoiceLabel(string id)
        {
            switch (id)
            {
                case "Meadows": return Text("Щедрість землі", "Earth's Bounty");
                case "BlackForest": return Text("Варта пралісу", "Forest Watch");
                case "Swamp": return Text("Тиха гавань", "Quiet Haven");
                case "Mountain": return Text("Подих горна", "Forge's Breath");
                case "Plains": return Text("Щедрий бенкет", "Bountiful Feast");
                case "Mistlands": return Text("Дотик імли", "Mist's Touch");
                default: return "";
            }
        }
        private static bool ValidSnapshot(WorkbenchEnchantmentSnapshot value)
        {
            if (value == null || value.Revision < 0 || value.NativeLevel < 1 || value.Learned == null || value.Available == null ||
                value.Learned.Length > 6 || value.Available.Length > WorkbenchEnchantmentRules.Capacity(value.NativeLevel) || (value.Reason?.Length ?? 0) > 160) return false;
            var learned = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in value.Learned) if (Array.IndexOf(Choices, id) < 0 || !learned.Add(id)) return false;
            var available = new HashSet<string>(StringComparer.Ordinal);
            if (value.Available.Length != Math.Min(value.Learned.Length, WorkbenchEnchantmentRules.Capacity(value.NativeLevel))) return false;
            for (int i = 0; i < value.Available.Length; ++i)
                if (value.Available[i] != value.Learned[i] || !available.Add(value.Available[i])) return false;
            return string.IsNullOrEmpty(value.Active) || available.Contains(value.Active);
        }
        internal static bool IsOpen(InventoryGui gui) => gui != null && gui.GetComponent<WorkbenchEnchantmentUi>()?._open == true;
        internal static void Refresh(InventoryGui gui)
        {
            if (Application.isBatchMode || gui == null || gui.m_tabUpgrade == null || gui.m_crafting == null) return;
            var ui = gui.GetComponent<WorkbenchEnchantmentUi>();
            if (!WorkbenchEnchantmentNetwork.Eligible(Player.m_localPlayer)) { ui?.HideIneligible(); return; }
            if (ui == null) ui = gui.gameObject.AddComponent<WorkbenchEnchantmentUi>();
            ui.Ensure(gui); ui.Tick();
        }
        private static void Sanitize(Button button)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            foreach (var component in button.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string name = component.GetType().Name;
                if (name == "UIInputHandler" || name == "Localize" || name == "Localization" || component is EventTrigger)
                { if (component is Behaviour b) b.enabled = false; Destroy(component); }
            }
        }
        private void Ensure(InventoryGui gui)
        {
            _gui = gui;
            if (_tab != null) return;
            _tab = Instantiate(gui.m_tabUpgrade, gui.m_tabUpgrade.transform.parent, false);
            _tab.name = "VM_WorkbenchEnchantmentsTab"; Sanitize(_tab);
            _tab.interactable = true;
            var rt = _tab.GetComponent<RectTransform>(); var original = gui.m_tabUpgrade.GetComponent<RectTransform>();
            rt.anchoredPosition = original.anchoredPosition + Vector2.right * (original.rect.width + 4f);
            _tab.GetComponentInChildren<TMP_Text>(true).text = Text("ЧАРИ", "ENCHANT");
            _tab.GetComponentInChildren<TMP_Text>(true).enableWordWrapping = false;
            _tab.onClick.AddListener(() => { if (_open) Close(); else Open(); });
            _panel = new GameObject("VM_WorkbenchEnchantmentPanel", typeof(RectTransform), typeof(RectMask2D));
            var panel = _panel.GetComponent<RectTransform>(); panel.SetParent(gui.m_crafting, false);
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(12, 12); panel.offsetMax = new Vector2(-12, -90);
            for (int i = 0; i < Choices.Length; ++i)
            {
                string choice = Choices[i];
                var button = NewButton(panel, choice, Vector2.zero, Vector2.zero);
                var row = button.GetComponent<RectTransform>();
                row.anchorMin = new Vector2(0, 1f - (i + 1) / 6f); row.anchorMax = new Vector2(.40f, 1f - i / 6f);
                row.offsetMin = new Vector2(0, 3); row.offsetMax = new Vector2(-6, -3);
                var rowLabel = button.GetComponentInChildren<TMP_Text>(true);
                rowLabel.enableAutoSizing = true; rowLabel.fontSizeMin = 10; rowLabel.fontSizeMax = 16;
                rowLabel.rectTransform.offsetMin = new Vector2(42, 2); rowLabel.rectTransform.offsetMax = new Vector2(-4, -2);
                var image = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(button.transform, false); image.raycastTarget = false; image.preserveAspect = true;
                image.material = gui.m_recipeIcon.material; image.color = Color.white; image.type = Image.Type.Simple;
                var imageRect = image.rectTransform; imageRect.anchorMin = imageRect.anchorMax = new Vector2(0, .5f);
                imageRect.sizeDelta = new Vector2(32, 32); imageRect.anchoredPosition = new Vector2(20, 0);
                button.onClick.AddListener(() => { _selected = choice; Render(); if (_detailScroll != null) _detailScroll.verticalNormalizedPosition = 1f; }); _choices.Add(button);
            }
            var viewportObject = new GameObject("VM_EnchantmentDetailViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            _detailViewport = viewportObject.GetComponent<RectTransform>(); _detailViewport.SetParent(panel, false);
            _detailViewport.anchorMin = new Vector2(.43f, 0); _detailViewport.anchorMax = Vector2.one;
            _detailViewport.offsetMin = new Vector2(0, 98); _detailViewport.offsetMax = new Vector2(-6, -102);
            viewportObject.GetComponent<Image>().color = Color.clear;
            _detail = Instantiate(gui.m_recipeDecription, _detailViewport, false);
            _detail.gameObject.SetActive(true); _detail.enabled = true;
            _detail.name = "VM_EnchantmentDescription"; _detail.enableAutoSizing = false; _detail.fontSize = 16;
            _detail.enableWordWrapping = true; _detail.overflowMode = TextOverflowModes.Overflow;
            _detail.raycastTarget = false; _detail.alignment = TextAlignmentOptions.TopLeft;
            var d = _detail.rectTransform; d.anchorMin = new Vector2(0, 1); d.anchorMax = Vector2.one;
            d.pivot = new Vector2(.5f, 1); d.anchoredPosition = Vector2.zero; d.sizeDelta = new Vector2(-8, 1);
            _detailScroll = viewportObject.GetComponent<ScrollRect>(); _detailScroll.viewport = _detailViewport; _detailScroll.content = d;
            _detailScroll.horizontal = false; _detailScroll.vertical = true; _detailScroll.inertia = false;
            _detailScroll.movementType = ScrollRect.MovementType.Clamped; _detailScroll.scrollSensitivity = 24f;
            _detailIcon = Instantiate(gui.m_recipeIcon, panel, false); _detailIcon.name = "VM_EnchantmentDetailIcon";
            _detailIcon.gameObject.SetActive(true); _detailIcon.enabled = true;
            _detailIcon.preserveAspect = true; _detailIcon.raycastTarget = false;
            _detailIcon.color = Color.white; _detailIcon.type = Image.Type.Simple;
            var iconRect = _detailIcon.rectTransform; iconRect.anchorMin = iconRect.anchorMax = new Vector2(.7f, 1);
            iconRect.pivot = new Vector2(.5f, 1); iconRect.sizeDelta = new Vector2(64, 64); iconRect.anchoredPosition = new Vector2(0, -30);
            CreateRequirement(panel);
            _learn = NewButton(panel, Text("Зачарувати", "Enchant"), new Vector2(190, -320), new Vector2(145, 32));
            _switch = NewButton(panel, Text("Активувати", "Activate"), new Vector2(190, -358), new Vector2(145, 32));
            PlaceAction(_learn, 70); PlaceAction(_switch, 32);
            _learn.onClick.AddListener(() => ActionClick(WorkbenchOperationKind.Learn));
            _switch.onClick.AddListener(() => ActionClick(_snapshot?.Active == _selected ? WorkbenchOperationKind.Disable : WorkbenchOperationKind.Switch));
            if (!_traceBound) { _traceBound = true; ActionTrace(0, "actions-bound"); }
            _panel.SetActive(false);
        }
        private void CreateRequirement(RectTransform panel)
        {
            var nativeIcon = _gui.m_minStationLevelIcon;
            var nativeText = _gui.m_minStationLevelText;
            if (nativeIcon == null || nativeText == null || nativeIcon.sprite == null) return;
            var size = nativeIcon.rectTransform.rect.size;
            var field = AccessTools.Field(typeof(InventoryGui), "m_minStationLevelBasecolor");
            if (size.x <= 0 || size.y <= 0 || !(field?.GetValue(_gui) is Color color)) return;
            _requirementBaseColor = color;
            _requirementIcon = new GameObject("VM_EnchantmentRequiredLevel", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            var rect = _requirementIcon.rectTransform; rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = Vector2.one;
            rect.sizeDelta = size; rect.anchoredPosition = new Vector2(-8, -30);
            _requirementIcon.sprite = nativeIcon.sprite; _requirementIcon.material = nativeIcon.material;
            _requirementIcon.color = nativeIcon.color; _requirementIcon.type = nativeIcon.type;
            _requirementIcon.preserveAspect = nativeIcon.preserveAspect; _requirementIcon.raycastTarget = false;
            _requirementText = Instantiate(nativeText, rect, false);
            _requirementText.name = "VM_EnchantmentRequiredLevelText";
            foreach (var component in _requirementText.GetComponentsInChildren<Component>(true))
                if (component != null && (component.GetType().Name == "Localize" || component.GetType().Name == "Localization"))
                { if (component is Behaviour behavior) behavior.enabled = false; Destroy(component); }
            _requirementText.enabled = true; _requirementText.gameObject.SetActive(true);
            _requirementText.raycastTarget = false; _requirementText.alignment = TextAlignmentOptions.Center;
            var label = _requirementText.rectTransform; label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one;
            label.offsetMin = label.offsetMax = Vector2.zero;
            _requirementIcon.gameObject.SetActive(false);
        }
        private static int RequiredLearnLevel(WorkbenchEnchantmentSnapshot snapshot, string selected, bool blocked)
        {
            if (blocked || snapshot?.Learned == null || snapshot.NativeLevel < 1 ||
                snapshot.Learned.Length >= 6 || Array.IndexOf(Choices, selected) < 0 ||
                Contains(snapshot.Learned, selected)) return 0;
            return Math.Max(1, snapshot.Learned.Length);
        }
        private void RenderRequirement(bool blocked)
        {
            if (_requirementIcon == null || _requirementText == null) return;
            int required = RequiredLearnLevel(_snapshot, _selected, blocked ||
                !ReferenceEquals(_requirementSession, ZNet.instance) || _snapshot?.World != Run70MapMarkerState.World);
            _requirementIcon.gameObject.SetActive(required > 0);
            _requirementMissing = required > 0 && _snapshot.NativeLevel < required && CooldownReady(SelectedCooldown());
            if (required > 0) _requirementText.text = required.ToString();
            UpdateRequirementBlink();
        }
        private void UpdateRequirementBlink()
        {
            if (_requirementText == null) return;
            if (!ReferenceEquals(_requirementSession, ZNet.instance) || _snapshot?.World != Run70MapMarkerState.World)
            {
                _requirementMissing = false;
                if (_requirementIcon != null) _requirementIcon.gameObject.SetActive(false);
            }
            bool flash = _requirementMissing && Mathf.Sin(Time.time * 10f) > 0f &&
                ZoneSystem.instance != null && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);
            _requirementText.color = flash ? Color.red : _requirementBaseColor;
        }
        private static void PlaceAction(Button button, float bottom)
        {
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.43f, 0); rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(-6, 32);
            rect.anchoredPosition = new Vector2(-3, bottom);
        }
        private static TabLayout CaptureLayout(RectTransform rect)
        {
            var label = rect.GetComponentInChildren<TMP_Text>(true);
            return new TabLayout { Min = rect.anchorMin, Max = rect.anchorMax, Pivot = rect.pivot,
                Position = rect.anchoredPosition, Size = rect.sizeDelta, AutoSize = label.enableAutoSizing,
                Wrap = label.enableWordWrapping, FontMin = label.fontSizeMin, FontMax = label.fontSizeMax };
        }
        private static void RestoreLayout(RectTransform rect, TabLayout value)
        {
            rect.anchorMin = value.Min; rect.anchorMax = value.Max; rect.pivot = value.Pivot;
            rect.anchoredPosition = value.Position; rect.sizeDelta = value.Size;
            var label = rect.GetComponentInChildren<TMP_Text>(true);
            label.enableAutoSizing = value.AutoSize; label.enableWordWrapping = value.Wrap;
            label.fontSizeMin = value.FontMin; label.fontSizeMax = value.FontMax;
        }
        private static void TabBounds(RectTransform rect, Transform parent, out Vector2 lower, out Vector2 upper)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            lower = parent.InverseTransformPoint(corners[0]); upper = parent.InverseTransformPoint(corners[2]);
        }
        private void TabLayoutVisible(bool visible)
        {
            var craft = _gui.m_tabCraft.GetComponent<RectTransform>(); var upgrade = _gui.m_tabUpgrade.GetComponent<RectTransform>();
            if (visible && !_tabLayoutOwned)
            {
                _craftLayout = CaptureLayout(craft); _upgradeLayout = CaptureLayout(upgrade); _tabLayoutOwned = true;
                var parent = craft.parent as RectTransform;
                if (parent == null || upgrade.parent != parent) return;
                TabBounds(craft, parent, out Vector2 craftMin, out Vector2 craftMax);
                TabBounds(upgrade, parent, out Vector2 upgradeMin, out Vector2 upgradeMax);
                float gap = Mathf.Max(0, upgradeMin.x - craftMax.x);
                float span = upgradeMax.x - craftMin.x;
                float width = (span - 2 * gap) / 3f;
                if (width <= 20) return;
                var third = _tab.GetComponent<RectTransform>();
                var tabs = new[] { craft, upgrade, third };
                for (int i = 0; i < tabs.Length; ++i)
                {
                    tabs[i].anchorMin = tabs[i].anchorMax = new Vector2(0, 1); tabs[i].pivot = new Vector2(0, 1);
                    tabs[i].sizeDelta = new Vector2(width, craftMax.y - craftMin.y);
                    tabs[i].anchoredPosition = new Vector2(craftMin.x - parent.rect.xMin + i * (width + gap), craftMax.y - parent.rect.yMax);
                    var label = tabs[i].GetComponentInChildren<TMP_Text>(true);
                    label.enableAutoSizing = true; label.fontSizeMin = 10; label.fontSizeMax = 16;
                    label.enableWordWrapping = false;
                }
            }
            else if (!visible && _tabLayoutOwned)
            {
                RestoreLayout(craft, _craftLayout); RestoreLayout(upgrade, _upgradeLayout); _tabLayoutOwned = false;
            }
        }
        private Button NewButton(Transform parent, string label, Vector2 position, Vector2 size)
        {
            var button = Instantiate(_gui.m_tabUpgrade, parent, false); Sanitize(button);
            // The native upgrade tab is legitimately inactive/disabled in basic crafting.
            // A visual clone must not inherit that native tab state.
            button.gameObject.SetActive(true); button.enabled = true; button.interactable = true;
            button.GetComponentInChildren<TMP_Text>(true).text = label;
            button.GetComponentInChildren<TMP_Text>(true).enabled = true;
            var rt = button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1); rt.anchoredPosition = position; rt.sizeDelta = size;
            return button;
        }
        private void Open()
        {
            var player = Player.m_localPlayer; var station = player?.GetCurrentCraftingStation();
            if (!WorkbenchEnchantmentNetwork.Eligible(player)) { HideIneligible(); return; }
            if (!InventoryGui.IsVisible() || !Bench(station) || _gui.m_craftTimer >= 0) return;
            _player = player; _station = station; _epoch++; _revision = -1; _snapshot = null;
            _requirementSession = ZNet.instance;
            _priorSelection = EventSystem.current?.currentSelectedGameObject;
            ActivateCraftingGroup();
            _open = true; _pending = false; _message = ""; SelectOwnedTab(); _panel.SetActive(true);
            HideNative(); _nextRefresh = 0; Tick();
            EventSystem.current?.SetSelectedGameObject(_choices[0].gameObject);
        }
        private void Hide(GameObject target)
        {
            if (target == null) return;
            if (!_hidden.ContainsKey(target)) _hidden[target] = target.activeSelf;
            target.SetActive(false);
        }
        private void HideNative()
        {
            Hide(_gui.m_recipeListRoot?.gameObject); Hide(_gui.m_recipeName?.gameObject);
            Hide(_gui.m_recipeDecription?.gameObject); Hide(_gui.m_recipeIcon?.gameObject);
            Hide(_gui.m_craftButton?.gameObject); Hide(_gui.m_craftCancelButton?.gameObject);
            Hide(_gui.m_craftProgressPanel?.gameObject);
            Hide(_gui.m_variantButton?.gameObject); Hide(_gui.m_qualityPanel?.gameObject);
            Hide(_gui.m_upgradeItemQuality?.gameObject); Hide(_gui.m_upgradeItemQualityArrow);
            Hide(_gui.m_upgradeItemNextQuality?.gameObject); Hide(_gui.m_itemCraftType?.gameObject);
            Hide(_gui.m_minStationLevelIcon?.gameObject); Hide(_gui.m_minStationLevelText?.gameObject);
            Hide(_gui.m_craftButton?.transform.parent.Find("ValheimMasteryCreationButton")?.gameObject);
            foreach (var item in _gui.m_recipeRequirementList) Hide(item);
        }
        private void Tick()
        {
            var player = Player.m_localPlayer; var station = player?.GetCurrentCraftingStation();
            bool visible = WorkbenchEnchantmentNetwork.Eligible(player) && InventoryGui.IsVisible() && Bench(station) && _gui.m_currentContainer == null;
            _tab.GetComponentInChildren<TMP_Text>(true).text = Text("ЧАРИ", "ENCHANT");
            TabLayoutVisible(visible);
            _tab.gameObject.SetActive(visible);
            if (!_open) return;
            if (!visible || player != _player || station != _station || _gui.m_craftTimer >= 0) { Close(); return; }
            HideNative(); SelectOwnedTab();
            UpdateRequirementBlink();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .2f;
            try
            {
                var snapshot = Read?.Invoke(player, station);
                if (!ValidSnapshot(snapshot) || snapshot.World != Run70MapMarkerState.World || string.IsNullOrEmpty(snapshot.Station) ||
                    snapshot.Revision < _revision || snapshot.NativeLevel != station.GetLevel()) _snapshot = null;
                else
                {
                    _snapshot = new WorkbenchEnchantmentSnapshot { World = snapshot.World, Revision = snapshot.Revision,
                        Station = snapshot.Station, NativeLevel = snapshot.NativeLevel, Active = snapshot.Active, Reason = snapshot.Reason,
                        Learned = (string[])snapshot.Learned.Clone(), Available = (string[])snapshot.Available.Clone(),
                        Cooldowns = CopyCooldowns(snapshot.Cooldowns) };
                    _revision = snapshot.Revision;
                }
            }
            catch { _snapshot = null; }
            Render();
        }
        private Sprite ChoiceIcon(string choice)
        { try { return (Icon != null ? Icon(choice) : Run70MapMarkerPresentation.EnchantmentIcon(choice)) ?? _gui.m_craftingStationIcon?.sprite; } catch { return _gui.m_craftingStationIcon?.sprite; } }
        private void Render()
        {
            if (!_open) return;
            if (!WorkbenchEnchantmentNetwork.Eligible(_player)) { HideIneligible(); return; }
            bool known = _snapshot != null;
            bool blocked = _pending || !known || Request == null || !string.IsNullOrEmpty(_snapshot.Reason);
            RenderRequirement(blocked || !string.IsNullOrEmpty(_message));
            _learn.GetComponentInChildren<TMP_Text>(true).text = Contains(_snapshot?.Learned, _selected) ?
                Text("Вже відкрито", "Already unlocked") : Text("Зачарувати", "Enchant");
            _switch.GetComponentInChildren<TMP_Text>(true).text = _snapshot?.Active == _selected ? Text("Вимкнути", "Disable") : Text("Активувати", "Activate");
            for (int i = 0; i < Choices.Length; i++)
            {
                string id = Choices[i]; bool learned = Contains(_snapshot?.Learned, id), available = Contains(_snapshot?.Available, id);
                string state = learned ? " ✓" : "";
                if (learned && !available) state += Text(" · дрімає", " · dormant");
                if (_snapshot?.Active == id) state += Text(" · активне", " · active");
                _choices[i].GetComponentInChildren<TMP_Text>(true).text = ChoiceLabel(id) + state;
                // Native ButtonTextColor owns label tint; semantic state stays in text and icons.
                var image = _choices[i].transform.Find("Icon")?.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = ChoiceIcon(id);
                    image.rectTransform.sizeDelta = id == _selected ? new Vector2(48, 48) : new Vector2(32, 32);
                    image.color = learned && !available ? new Color(.55f, .55f, .55f, 1f) : Color.white;
                }
            }
            _detailIcon.sprite = ChoiceIcon(_selected);
            string reason = _pending ? Text("Дію ще не підтверджено.", "Action not confirmed yet.") : !known || Request == null ?
                Text("Стан не підтверджено.", "State not confirmed.") : !string.IsNullOrEmpty(_snapshot.Reason) ?
                ReasonText(_snapshot.Reason) : _message;
            if (string.IsNullOrEmpty(reason)) reason = CapacityReason(_snapshot, _selected, blocked);
            string description = "";
            try
            {
                description = Detail?.Invoke(_selected) ?? "";
            }
            catch { }
            string cooldown = CooldownText();
            _detail.text = ChoiceLabel(_selected) + "\n" + description;
            if (!string.IsNullOrEmpty(cooldown)) _detail.text += "\n" + cooldown;
            if (!string.IsNullOrEmpty(reason)) _detail.text += "\n" + reason;
            float width = Mathf.Max(1f, _detailViewport.rect.width - 8f);
            _detail.rectTransform.sizeDelta = new Vector2(-8f,
                Mathf.Max(_detailViewport.rect.height, _detail.GetPreferredValues(_detail.text, width, 0).y + 8f));
            _learn.interactable = !blocked && CooldownReady(SelectedCooldown()) && !Contains(_snapshot.Learned, _selected) &&
                (_snapshot.Learned?.Length ?? 0) < WorkbenchEnchantmentRules.Capacity(_snapshot.NativeLevel);
            _switch.interactable = !blocked && Contains(_snapshot.Available, _selected);
        }
        // First four natural action clicks only; never polls or changes admission.
        private void ActionClick(WorkbenchOperationKind kind)
        {
            int attempt = !Application.isBatchMode && _traceClicks < 4 ? ++_traceClicks : 0;
            ActionTrace(attempt, "click-" + kind);
            Begin(kind, attempt);
        }
        private void ActionTrace(int attempt, string stage)
        {
            if (Application.isBatchMode || (attempt == 0 && stage != "actions-bound") || _traceLines >= 24) return;
            _traceLines++;
            try { MasteryPlugin.Log?.LogInfo("[EnchantUI] attempt=" + attempt + " stage=" + stage +
                " open=" + _open + " pending=" + _pending + " snapshot=" + (_snapshot != null) +
                " request=" + (Request != null) + " blockedReason=" + !string.IsNullOrEmpty(_snapshot?.Reason)); }
            catch { } // Diagnostic failure must not alter input or completion.
        }
        private void Begin(WorkbenchOperationKind kind, int attempt)
        {
            if (!WorkbenchEnchantmentNetwork.Eligible(_player)) { HideIneligible(); return; }
            if (!_open || _pending || _snapshot == null || Request == null || !string.IsNullOrEmpty(_snapshot.Reason))
            { ActionTrace(attempt, "begin-refused"); return; }
            if (kind == WorkbenchOperationKind.Learn && (!CooldownReady(SelectedCooldown()) ||
                Contains(_snapshot.Learned, _selected) || _snapshot.Learned.Length >= WorkbenchEnchantmentRules.Capacity(_snapshot.NativeLevel)))
            { ActionTrace(attempt, "learn-refused"); return; }
            var snapshot = _snapshot; var player = _player; var station = _station; int epoch = _epoch;
            _pending = true; Render(); ActionTrace(attempt, "pending"); bool queued = false;
            try
            {
                queued = Request(player, station, snapshot, kind, _selected, (committed, reason) =>
                {
                    if (!_open || _epoch != epoch || _player != player || _station != station)
                    { ActionTrace(attempt, "callback-discarded"); return; }
                    ActionTrace(attempt, committed ? "callback-committed" : "callback-refused");
                    _pending = false; _snapshot = null; _nextRefresh = 0;
                    // An authenticated idempotent success may retain the same durable revision.
                    // Still reread the authoritative snapshot; never grant optimistic UI state.
                    if (committed) _revision = snapshot.Revision;
                    _message = committed ? "" : ReasonText(reason);
                    Render();
                });
            }
            catch (Exception error) { ActionTrace(attempt, "request-exception-" + error.GetType().Name); }
            ActionTrace(attempt, queued ? "request-queued" : "request-not-queued");
            if (!queued) { _pending = false; _snapshot = null; _message = Text("Дію не підтверджено.", "Action not confirmed."); Render(); }
        }
        private void SelectOwnedTab()
        {
            if (!_nativeTabsOwned)
            {
                _priorCraftTabInteractable = _gui.m_tabCraft.interactable;
                _priorUpgradeTabInteractable = _gui.m_tabUpgrade.interactable;
                _nativeTabsOwned = true;
            }
            _gui.m_tabCraft.interactable = true; _gui.m_tabUpgrade.interactable = true;
            _tab.interactable = false; // Native tabs use disabled state for the selected tab.
        }
        private void RestoreNativeTabs()
        {
            if (!_nativeTabsOwned) return;
            if (_gui != null)
            {
                _gui.m_tabCraft.interactable = _priorCraftTabInteractable;
                _gui.m_tabUpgrade.interactable = _priorUpgradeTabInteractable;
            }
            if (_tab != null) _tab.interactable = true;
            _nativeTabsOwned = false;
        }
        internal static bool TryNativeCraftTab(InventoryGui gui, out bool result)
        {
            result = false;
            var ui = gui != null ? gui.GetComponent<WorkbenchEnchantmentUi>() : null;
            if (ui == null || !ui._open || !ui._nativeTabsOwned) return false;
            // Preserve native recipe mode while the custom tab owns visual selection.
            result = !ui._priorCraftTabInteractable && gui.m_tabCraft.gameObject.activeSelf;
            return true;
        }
        private void ActivateCraftingGroup()
        {
            var groups = _gui != null ? _gui.m_uiGroups : null;
            if (groups == null || groups.Length <= 3 || groups[3] == null) return;
            int prior = _gui.m_activeGroup;
            _priorUiGroup = prior >= 0 && prior < groups.Length ? groups[prior] : null;
            _ownedUiGroup = groups[3];
            // Match native crafting-tab input ownership, without a new presentation cue.
            _gui.SetActiveGroup(_ownedUiGroup, false);
        }
        private void RestoreCraftingGroup()
        {
            var groups = _gui != null ? _gui.m_uiGroups : null;
            if (groups != null && _priorUiGroup != null && _ownedUiGroup != null &&
                _gui.m_activeGroup >= 0 && _gui.m_activeGroup < groups.Length &&
                groups[_gui.m_activeGroup] == _ownedUiGroup && Array.IndexOf(groups, _priorUiGroup) >= 0)
                _gui.SetActiveGroup(_priorUiGroup, false);
            _priorUiGroup = null; _ownedUiGroup = null;
        }
        private void HideIneligible()
        {
            Close();
            if (_tab != null) { TabLayoutVisible(false); _tab.gameObject.SetActive(false); }
        }
        internal void Close()
        {
            if (!_open) return;
            _open = false; _epoch++; _pending = false; _snapshot = null;
            _requirementMissing = false;
            _requirementSession = null;
            if (_requirementIcon != null) _requirementIcon.gameObject.SetActive(false);
            if (_requirementText != null) _requirementText.color = _requirementBaseColor;
            if (_panel != null) _panel.SetActive(false);
            foreach (var pair in _hidden) if (pair.Key != null) pair.Key.SetActive(pair.Value);
            _hidden.Clear();
            RestoreNativeTabs(); RestoreCraftingGroup();
            var system = EventSystem.current; var selected = system?.currentSelectedGameObject;
            bool owned = selected != null && (selected == _tab?.gameObject || (_panel != null && selected.transform.IsChildOf(_panel.transform)));
            if (owned)
            {
                GameObject restore = _priorSelection != null && _priorSelection.activeInHierarchy && _priorSelection != _tab?.gameObject ?
                    _priorSelection : _gui?.m_tabCraft?.gameObject;
                system.SetSelectedGameObject(restore != null && restore.activeInHierarchy ? restore : null);
            }
            _priorSelection = null; _player = null; _station = null;
        }
        private void OnDisable() { Close(); if (_gui != null && _tab != null) TabLayoutVisible(false); }
        private void LateUpdate()
        {
            if (_gui != null && _tab != null) Tick();
            if (_open && _detailScroll != null && ZInput.IsExclusiveGamepadActive() && !ZInput.IsTouchActive())
            {
                float axis = ZInput.GetJoyRightStickY();
                if (Mathf.Abs(axis) > .1f) _detailScroll.verticalNormalizedPosition = Mathf.Clamp01(
                    _detailScroll.verticalNormalizedPosition + axis * Time.unscaledDeltaTime * 1.5f);
            }
        }
        private void OnDestroy() { OnDisable(); if (_tab != null) Destroy(_tab.gameObject); if (_panel != null) Destroy(_panel); }
    }
    [HarmonyPatch(typeof(InventoryGui), "InCraftTab")]
    internal static class WorkbenchEnchantmentNativeTabModePatch
    {
        private static bool Prefix(InventoryGui __instance, ref bool __result)
        {
            if (!WorkbenchEnchantmentUi.TryNativeCraftTab(__instance, out bool mode)) return true;
            __result = mode; return false;
        }
    }
    [HarmonyPatch(typeof(InventoryGui), "UpdateCraftingPanel")]
    internal static class WorkbenchEnchantmentRefreshPatch
    { private static void Postfix(InventoryGui __instance) => WorkbenchEnchantmentUi.Refresh(__instance); }
    [HarmonyPatch(typeof(InventoryGui), "OnTabCraftPressed")]
    internal static class WorkbenchEnchantmentCraftTabPatch
    { private static void Prefix(InventoryGui __instance) => __instance.GetComponent<WorkbenchEnchantmentUi>()?.Close(); }
    [HarmonyPatch(typeof(InventoryGui), "OnTabUpgradePressed")]
    internal static class WorkbenchEnchantmentUpgradeTabPatch
    { private static void Prefix(InventoryGui __instance) => __instance.GetComponent<WorkbenchEnchantmentUi>()?.Close(); }
    [HarmonyPatch(typeof(InventoryGui), "Hide")]
    internal static class WorkbenchEnchantmentHidePatch
    { private static void Prefix(InventoryGui __instance) => __instance.GetComponent<WorkbenchEnchantmentUi>()?.Close(); }
    [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
    internal static class WorkbenchEnchantmentBlockCraftPatch
    { private static bool Prefix(InventoryGui __instance) => !WorkbenchEnchantmentUi.IsOpen(__instance); }
    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class WorkbenchEnchantmentBlockCraftExecutionPatch
    { private static bool Prefix(InventoryGui __instance) => !WorkbenchEnchantmentUi.IsOpen(__instance); }
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeGamepadInput")]
    internal static class WorkbenchEnchantmentBlockRecipeNavigationPatch
    { private static bool Prefix(InventoryGui __instance) => !WorkbenchEnchantmentUi.IsOpen(__instance); }
    // UpdateRecipe normally runs after UpdateCraftingPanel and rewrites native detail/quality controls.
    // No native craft is running when this section opens; normal update resumes on close.
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class WorkbenchEnchantmentBlockRecipeDetailPatch
    { private static bool Prefix(InventoryGui __instance) => !WorkbenchEnchantmentUi.IsOpen(__instance); }
    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class WorkbenchEnchantmentCancelPatch
    {
        private static bool Prefix(InventoryGui __instance)
        {
            if (!WorkbenchEnchantmentUi.IsOpen(__instance) || !ZInput.GetKeyDown(KeyCode.Escape, true)) return true;
            __instance.GetComponent<WorkbenchEnchantmentUi>()?.Close();
            return false; // Only consume the close key while the owned section is open.
        }
    }
}
