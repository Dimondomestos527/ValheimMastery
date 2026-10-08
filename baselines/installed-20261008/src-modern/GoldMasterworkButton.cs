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
                if (Player.m_localPlayer?.GetCurrentCraftingStation()?.m_upgrader == true)
                    GoldDivineTransactions.BeginForge(_gui);
                else GoldDivineTransactions.BeginMasterwork(_gui);
            });
            // A cloned UI input handler can retain serialized callbacks to normal craft.
            // The native Button itself supplies mouse/controller activation.
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string name = component.GetType().Name;
                if (name == "UIInputHandler" || name == "Localize" || name == "Localization")
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
                    "Create one quality-5 item for 500 Favor, without recipe materials. Requires the usual station type, at any level.",
                    "Створити один предмет якості 5 за 500 прихильності, без матеріалів рецепта. Потрібний звичайний тип станції, будь-якого рівня.");
            }
        }
        private void UpdateState(Player player)
        {
            if (_button == null || _gui == null) return;
            GoldDivineTransactions.RefreshMasterworkIntent(_gui, player);
            bool forge = player?.GetCurrentCraftingStation()?.m_upgrader == true;
            bool visible = InventoryGui.IsVisible() && GoldCraftingService.Enabled && GoldCraftingService.Unlocked &&
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
                GoldUiLocalization.Text("Forge with Völundr · 250 Favor", "Кувати з Вьолундром · 250 прихильності") :
                GoldUiLocalization.Text("Völundr's creation · 500 Favor", "Творіння Вьолундра · 500 Favor");
            _button.interactable = forge ? GoldDivineTransactions.CanStartForge(_gui, player) :
                GoldDivineTransactions.CanStartMasterwork(_gui, player);
            var tip = _button.GetComponent<UITooltip>();
            if (tip != null) tip.m_text = forge ?
                GoldUiLocalization.Text("Raise this item's quality by 3. Costs 250 Favor and the required idol. Hold a Hammer.",
                    "Підвищити якість цього предмета на 3. Вартість: 250 прихильності та потрібний ідол. Тримай молот.") :
                GoldUiLocalization.Text("Create one quality-5 item for 500 Favor, without recipe materials. Requires the usual station type, at any level.",
                    "Створити один предмет якості 5 за 500 прихильності, без матеріалів рецепта. Потрібний звичайний тип станції, будь-якого рівня.");
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
            GoldDivineTransactions.CancelMasterwork(_gui);
            SetVisible(false);
        }
        private void OnDestroy()
        {
            GoldDivineTransactions.CancelMasterwork(_gui);
            ReleaseFocus();
            if (_button != null) Destroy(_button.gameObject);
        }
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftCancelPressed))]
    internal static class GoldMasterworkCancelPatch
    {
        private static void Postfix(InventoryGui __instance) => GoldDivineTransactions.CancelMasterwork(__instance);
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class GoldMasterworkClosePatch
    {
        private static void Prefix(InventoryGui __instance) => GoldDivineTransactions.CancelMasterwork(__instance);
    }
}
