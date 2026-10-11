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
        private CraftingStation _lastStation; private int _lastQuality; private bool _lastCrafting;

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
                tip.m_text = GoldUiLocalization.Text(
                    "Create one quality-5 item without recipe materials. Requires the usual station type, at any level. Cooldown depends on item tier.",
                    "Створити один предмет якості 5 без матеріалів рецепта. Потрібний звичайний тип станції будь-якого рівня. Час відновлення залежить від тіру виробу.");
            }
        }
        private void UpdateState(Player player)
        {
            if (_button == null || _gui == null) return;
            if (WorkbenchEnchantmentUi.IsOpen(_gui)) { SetVisible(false); return; }
            Crafting100ActionController.Refresh(_gui);
            var recipe = _gui.m_selectedRecipe.Recipe; var item = _gui.m_selectedRecipe.ItemData;
            var station = player?.GetCurrentCraftingStation(); bool crafting = _gui.m_craftTimer >= 0;
            if (Time.unscaledTime < _nextState && recipe == _lastRecipe && item == _lastItem && station == _lastStation &&
                (item?.m_quality ?? 0) == _lastQuality && crafting == _lastCrafting) return;
            _nextState = Time.unscaledTime + .2f; _lastRecipe = recipe; _lastItem = item;
            _lastStation = station; _lastQuality = item?.m_quality ?? 0; _lastCrafting = crafting;
            bool forge = station?.m_upgrader == true;
            bool visible = InventoryGui.IsVisible() && Crafting100ActionController.Visible(player) &&
                (forge ? _gui.m_selectedRecipe.ItemData != null : _gui.m_selectedRecipe.ItemData == null) &&
                GoldDivineTransactions.Eligible(_gui.m_selectedRecipe.Recipe?.m_item?.m_itemData) &&
                player != null && _gui.m_craftTimer < 0;
            SetVisible(visible);
            if (!visible) return;
            // Follow the actual native rectangle instead of a resolution-specific screen position.
            _rect.anchorMin = _nativeRect.anchorMin; _rect.anchorMax = _nativeRect.anchorMax;
            _rect.pivot = _nativeRect.pivot; _rect.sizeDelta = _nativeRect.sizeDelta;
            _rect.anchoredPosition = _nativeRect.anchoredPosition + Vector2.down * (_nativeRect.rect.height + 6f);
            if (_label != null) _label.text = forge ?
                GoldUiLocalization.Text("Forge with Völundr · +3", "Кувати з Вьолундром · +3") :
                GoldUiLocalization.Text("Völundr's creation · quality 5", "Творіння Вьолундра · якість 5");
            var status = Crafting100ActionController.ButtonStatus(forge, item);
            _button.interactable = status == Crafting100ButtonStatus.Action && Crafting100ActionController.CanStart(_gui, player);
            double remaining = forge ? Crafting100PermitService.ForgeRemaining : Crafting100PermitService.CraftRemaining;
            if (_label != null)
            {
                if (status == Crafting100ButtonStatus.Pending)
                    _label.text = GoldUiLocalization.Text("Unfinished action", "Незавершена дія");
                else if (status == Crafting100ButtonStatus.Unconfirmed)
                    _label.text = GoldUiLocalization.Text("State not confirmed", "Стан не підтверджено");
                else if (status == Crafting100ButtonStatus.ForgeUsed)
                    _label.text = GoldUiLocalization.Text("Gift already used", "Дар уже використано");
                else if (status == Crafting100ButtonStatus.Cooldown)
                    _label.text += " · " + Math.Ceiling(remaining / 60d).ToString("0") + GoldUiLocalization.Text(" min", " хв");
            }
            var tip = _button.GetComponent<UITooltip>();
            if (tip != null)
            {
                if (status == Crafting100ButtonStatus.Pending)
                    tip.m_text = GoldUiLocalization.Text("The previous action has not been confirmed. A new creation is unavailable for now.",
                        "Попередню дію ще не підтверджено. Нове творіння поки недоступне.");
                else if (status == Crafting100ButtonStatus.Unconfirmed)
                    tip.m_text = GoldUiLocalization.Text("The availability of this gift has not been confirmed yet.", "Доступність дару ще не підтверджено.");
                else tip.m_text = forge ?
                    GoldUiLocalization.Text("Guarantee +3 quality for one required idol, once per item. Independent 15-minute cooldown. Hold a Hammer.",
                        "Гарантовано підвищити якість на 3 за один потрібний ідол, лише раз на предмет. Окреме відновлення: 15 хвилин. Тримай молот.") :
                    GoldUiLocalization.Text("Create one quality-5 item without recipe materials. Requires the usual station type, at any level. Cooldown depends on item tier.",
                        "Створити один предмет якості 5 без матеріалів рецепта. Потрібний звичайний тип станції будь-якого рівня. Час відновлення залежить від тіру виробу.");
                if (forge && item?.m_customData != null && Crafting100ForgeItemRules.Used(item.m_customData))
                {
                    string used = GoldUiLocalization.Text("This item has already received this gift. Use ordinary forge upgrades.",
                        "Вьолундр уже зміцнив цей виріб. Далі покращуй його звичайним способом.");
                    tip.m_text = status == Crafting100ButtonStatus.Pending || status == Crafting100ButtonStatus.Unconfirmed
                        ? tip.m_text + "\n" + used : used;
                }
            }
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






