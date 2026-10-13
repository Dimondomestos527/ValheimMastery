using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ValheimMastery
{
    // One native-styled, one-action button per InventoryGui. No persistent/UI global state.
    internal sealed class GoldMasterworkButton : MonoBehaviour
    {
        private InventoryGui _gui;
        private Button _button;
        private RectTransform _rect, _nativeRect;
        private TMP_Text _label;
        private float _nextState; private Recipe _lastRecipe; private ItemDrop.ItemData _lastItem;
        private CraftingStation _lastStation; private int _lastQuality; private bool _lastCrafting, _lastShift;

        internal static void Refresh(InventoryGui gui, Player player)
        {
            if (Application.isBatchMode || gui == null || gui.m_craftButton == null || player == null || player != Player.m_localPlayer) return;
            var controller = gui.GetComponent<GoldMasterworkButton>();
            if (controller == null) controller = gui.gameObject.AddComponent<GoldMasterworkButton>();
            controller.Ensure(gui);
            controller.UpdateState(player);
        }
        private void Ensure(InventoryGui gui)
        {
            _gui = gui;
            if (_button != null) return;
            GameObject copy = Instantiate(gui.m_craftButton.gameObject, gui.m_craftButton.transform.parent, false);
            copy.name = "ValheimMasteryCreationButton";
            _button = copy.GetComponent<Button>();
            _button.onClick = new Button.ButtonClickedEvent();
            _button.onClick.AddListener(() => {
                if (!WorkbenchEnchantmentUi.IsOpen(_gui)) Crafting100ActionController.Begin(_gui);
            });
            // A cloned UI input handler can retain serialized callbacks to normal craft.
            // The native Button itself supplies mouse/controller activation.
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string name = component.GetType().Name;
                if (name == "UIInputHandler" || name == "Localize" || name == "Localization" || component is EventTrigger)
                {
                    if (component is Behaviour behavior) behavior.enabled = false;
                    Destroy(component);
                }
            }
            _rect = copy.GetComponent<RectTransform>();
            _nativeRect = gui.m_craftButton.GetComponent<RectTransform>();
            _label = copy.GetComponentInChildren<TMP_Text>(true);
            if (_label != null)
            {
                _label.color = new Color(.91f, .72f, .36f, 1f);
                _label.enableAutoSizing = true;
                _label.fontSizeMax = Mathf.Max(14f, _label.fontSize);
                _label.fontSizeMin = 12f;
            }
            var tip = copy.GetComponent<UITooltip>();
            if (tip != null)
            {
                tip.m_topic = "";
                tip.m_text = ""; // Filled from the domain preview before becoming visible.
            }
        }
        private void UpdateState(Player player)
        {
            if (_button == null || _gui == null) return;
            if (WorkbenchEnchantmentUi.IsOpen(_gui)) { SetVisible(false); return; }
            Crafting100ActionController.Refresh(_gui);
            var recipe = _gui.m_selectedRecipe.Recipe; var item = _gui.m_selectedRecipe.ItemData;
            var station = player?.GetCurrentCraftingStation(); bool crafting = _gui.m_craftTimer >= 0;
            bool shift = ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false);
            if (Time.unscaledTime < _nextState && recipe == _lastRecipe && item == _lastItem && station == _lastStation &&
                (item?.m_quality ?? 0) == _lastQuality && crafting == _lastCrafting && shift == _lastShift) return;
            _nextState = Time.unscaledTime + .2f; _lastRecipe = recipe; _lastItem = item;
            _lastStation = station; _lastQuality = item?.m_quality ?? 0; _lastCrafting = crafting;
            var preview = Crafting100ActionController.Preview(_gui, player);
            _lastShift = preview.Shift;
            SetVisible(preview.Visible);
            if (!preview.Visible) return;
            // Follow the actual native rectangle instead of a resolution-specific screen position.
            _rect.anchorMin = _nativeRect.anchorMin; _rect.anchorMax = _nativeRect.anchorMax;
            _rect.pivot = _nativeRect.pivot; _rect.sizeDelta = _nativeRect.sizeDelta;
            _rect.anchoredPosition = _nativeRect.anchoredPosition + Vector2.down * (_nativeRect.rect.height + 6f);
            if (_label != null) _label.text = preview.Caption;
            _button.interactable = preview.Enabled;
            var tip = _button.GetComponent<UITooltip>();
            if (tip != null) tip.m_text = preview.Tooltip;
        }

        private void ReleaseFocus()
        {
            var events = EventSystem.current;
            var selected = events?.currentSelectedGameObject;
            if (selected == null || _button == null ||
                (selected != _button.gameObject && !selected.transform.IsChildOf(_button.transform))) return;
            var native = _gui?.m_craftButton;
            events.SetSelectedGameObject(native != null && native.gameObject.activeInHierarchy && native.interactable
                ? native.gameObject : null);
        }
        private void SetVisible(bool visible)
        {
            if (_button == null) return;
            if (!visible) ReleaseFocus();
            _button.gameObject.SetActive(visible);
        }
        private void LateUpdate()
        {
            if (_gui != null) UpdateState(Player.m_localPlayer);
        }
        private void OnDisable()
        {
            Crafting100ActionController.Cancel(_gui);
            SetVisible(false);
        }
        private void OnDestroy()
        {
            Crafting100ActionController.Cancel(_gui);
            ReleaseFocus();
            if (_button != null) Destroy(_button.gameObject);
        }
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftCancelPressed))]
    internal static class GoldMasterworkCancelPatch
    {
        private static void Postfix(InventoryGui __instance) => Crafting100ActionController.Cancel(__instance);
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class GoldMasterworkClosePatch
    {
        private static void Prefix(InventoryGui __instance) => Crafting100ActionController.Cancel(__instance);
    }
}
