using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Process-local control: a remote client never claims to change the dedicated server's flag.
    internal sealed class ExperimentalOptimizationToggle : MonoBehaviour
    {
        private Toggle _toggle;
        private TextMeshProUGUI _scope;
        private ConfigEntry<bool> _setting;
        private bool _syncing;
        private bool _pendingSync;

        internal static ExperimentalOptimizationToggle Create(Transform parent, TMP_FontAsset font)
        {
            if (parent == null || Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            var existing = parent.GetComponentInChildren<ExperimentalOptimizationToggle>(true);
            if (existing != null) return existing;

            GameObject row = new GameObject("ExperimentalOptimization", typeof(RectTransform), typeof(Image), typeof(Toggle));
            row.transform.SetParent(parent, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f); rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(.5f, 0f); rect.offsetMin = new Vector2(18f, 16f); rect.offsetMax = new Vector2(-18f, 62f);

            Image background = row.GetComponent<Image>();
            background.color = new Color(.24f, .15f, .055f, .98f);
            ApplyNativeFrame(parent, background);

            GameObject labelObject = MakeText(row.transform, "Label", font, 16f, TextAlignmentOptions.MidlineLeft);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, .42f); labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(12f, 0f); labelRect.offsetMax = new Vector2(-58f, -2f);
            labelObject.GetComponent<TextMeshProUGUI>().text = "Експериментальна оптимізація";

            GameObject scopeObject = MakeText(row.transform, "Scope", font, 12f, TextAlignmentOptions.MidlineLeft);
            RectTransform scopeRect = scopeObject.GetComponent<RectTransform>();
            scopeRect.anchorMin = new Vector2(0f, 0f); scopeRect.anchorMax = new Vector2(1f, .44f);
            scopeRect.offsetMin = new Vector2(12f, 2f); scopeRect.offsetMax = new Vector2(-58f, 0f);
            TextMeshProUGUI scope = scopeObject.GetComponent<TextMeshProUGUI>();
            scope.color = new Color(.72f, .68f, .58f);

            GameObject mark = new GameObject("Radio", typeof(RectTransform), typeof(Image));
            mark.transform.SetParent(row.transform, false);
            RectTransform markRect = mark.GetComponent<RectTransform>();
            markRect.anchorMin = markRect.anchorMax = new Vector2(1f, .5f); markRect.pivot = new Vector2(1f, .5f);
            markRect.anchoredPosition = new Vector2(-11f, 0f); markRect.sizeDelta = new Vector2(32f, 28f);
            Image markFrame = mark.GetComponent<Image>(); markFrame.color = new Color(.11f, .07f, .025f, 1f);
            ApplyNativeFrame(parent, markFrame);

            GameObject selectedObject = MakeText(mark.transform, "Selected", font, 19f, TextAlignmentOptions.Center);
            RectTransform selectedRect = selectedObject.GetComponent<RectTransform>();
            selectedRect.anchorMin = Vector2.zero; selectedRect.anchorMax = Vector2.one;
            selectedRect.offsetMin = selectedRect.offsetMax = Vector2.zero;
            TextMeshProUGUI selected = selectedObject.GetComponent<TextMeshProUGUI>();
            selected.text = "◆"; selected.color = new Color(.96f, .72f, .25f);

            Toggle toggle = row.GetComponent<Toggle>();
            toggle.targetGraphic = background; toggle.graphic = selected; toggle.navigation = Navigation.defaultNavigation;
            ColorBlock colors = toggle.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.12f, 1.08f, .94f, 1f);
            colors.pressedColor = new Color(.78f, .72f, .58f, 1f); colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(.45f, .45f, .45f, .65f); colors.colorMultiplier = 1f;
            toggle.colors = colors;

            var control = row.AddComponent<ExperimentalOptimizationToggle>();
            control._toggle = toggle; control._scope = scope;
            toggle.onValueChanged.AddListener(control.SetValue);
            control.Bind(); control.Sync();
            return control;
        }

        private static GameObject MakeText(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.alignment = alignment; text.raycastTarget = false;
            text.color = new Color(.95f, .91f, .80f);
            return go;
        }

        private static void ApplyNativeFrame(Transform ownPanel, Image target)
        {
            SkillsDialog dialog = ownPanel != null ? ownPanel.GetComponentInParent<SkillsDialog>() : null;
            if (dialog == null || target == null) return;
            foreach (Button button in dialog.GetComponentsInChildren<Button>(true))
            {
                if (button == null || button.transform.IsChildOf(ownPanel)) continue;
                Image source = button.targetGraphic as Image ?? button.GetComponent<Image>();
                if (source == null || source.sprite == null) continue;
                target.sprite = source.sprite; target.type = source.type; target.material = source.material;
                target.preserveAspect = source.preserveAspect; target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
                return;
            }
        }

        private void OnEnable()
        {
            Bind(); Sync();
        }

        private void OnDisable()
        {
            Unbind();
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && (selected == gameObject || selected.transform.IsChildOf(transform))) events.SetSelectedGameObject(null);
        }

        private void OnDestroy()
        {
            Unbind();
            if (_toggle != null) _toggle.onValueChanged.RemoveListener(SetValue);
        }

        private void Update()
        {
            if (!_pendingSync) return;
            _pendingSync = false; Sync();
        }

        private void Bind()
        {
            ConfigEntry<bool> next = MasteryPlugin.Settings?.ExperimentalOptimization;
            if (ReferenceEquals(_setting, next)) return;
            Unbind(); _setting = next;
            if (_setting != null) _setting.SettingChanged += SettingChanged;
        }

        private void Unbind()
        {
            if (_setting != null) _setting.SettingChanged -= SettingChanged;
            _setting = null;
        }

        private void SettingChanged(object sender, EventArgs args)
        {
            _pendingSync = true;
        }

        private void SetValue(bool value)
        {
            if (_syncing) return;
            Bind();
            if (_setting != null && _setting.Value != value) _setting.Value = value;
            Sync();
        }

        private void Sync()
        {
            if (_toggle == null || _scope == null) return;
            Bind(); _syncing = true;
            bool available = _setting != null;
            _toggle.interactable = available;
            _toggle.SetIsOnWithoutNotify(available && _setting.Value);
            bool remoteClient = ZNet.instance != null && !ZNet.instance.IsServer();
            _scope.text = !available ? "Недоступно" : (_toggle.isOn ? "Увімкнено" : "Вимкнено") + (remoteClient ? " — лише цей клієнт" : " — цей процес");
            _syncing = false;
        }
    }
}
