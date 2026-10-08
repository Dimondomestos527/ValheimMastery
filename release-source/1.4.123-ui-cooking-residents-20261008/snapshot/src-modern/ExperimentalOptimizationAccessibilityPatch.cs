using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace ValheimMastery
{
    // Native Accessibility row. It stages a value until the settings dialog confirms with OK.
    internal sealed class ExperimentalOptimizationAccessibilityControl : MonoBehaviour
    {
        private const string RowName = "ValheimMasteryExperimentalOptimization";
        private static readonly FieldInfo LastToggleField = AccessTools.Field(typeof(AccessibilitySettings), "m_toggleBlockToggle");

        private Toggle _toggle;
        private Toggle _previous;
        private ConfigEntry<bool> _setting;

        internal static ExperimentalOptimizationAccessibilityControl Ensure(AccessibilitySettings owner, bool resetFromCurrent = false)
        {
            if (owner == null || Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            ExperimentalOptimizationAccessibilityControl existing = owner.GetComponentInChildren<ExperimentalOptimizationAccessibilityControl>(true);
            if (existing != null)
            {
                if (resetFromCurrent) existing.ResetFromCurrent();
                return existing;
            }

            Toggle previous = LastToggleField?.GetValue(owner) as Toggle;
            if (previous == null || previous.transform.parent == null) return null;

            GameObject row = UnityEngine.Object.Instantiate(previous.gameObject, previous.transform.parent, false);
            row.name = RowName;
            row.transform.SetSiblingIndex(previous.transform.GetSiblingIndex() + 1);

            Toggle toggle = row.GetComponent<Toggle>();
            if (toggle == null)
            {
                UnityEngine.Object.Destroy(row);
                return null;
            }

            // A cloned native row must not retain the donor's serialized callback.
            toggle.onValueChanged = new Toggle.ToggleEvent();
            SetLabel(row, "Експериментальна оптимізація · цей процес");
            SettingsTooltip tooltip = row.GetComponent<SettingsTooltip>();
            if (tooltip != null)
                tooltip.SetTexts("Valheim Mastery", "Зменшує повторну роботу моду. Діє лише в цьому процесі; виділений сервер налаштовується окремо.");

            ExperimentalOptimizationAccessibilityControl control = row.AddComponent<ExperimentalOptimizationAccessibilityControl>();
            control.Initialize(toggle, previous);
            return control;
        }

        private static void SetLabel(GameObject row, string value)
        {
            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text label = null;
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null) continue;
                if (texts[i].gameObject.name.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    texts[i].gameObject.name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    label = texts[i];
                    break;
                }
            }
            if (label == null && texts.Length > 0) label = texts[0];
            if (label == null) return;
            label.text = value;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.textWrappingMode = TextWrappingModes.Normal;
        }

        private void Initialize(Toggle toggle, Toggle previous)
        {
            _toggle = toggle;
            _previous = previous;
            _toggle.onValueChanged.AddListener(OnPendingValueChanged);
            ResetFromCurrent();
        }

        private void OnPendingValueChanged(bool value)
        {
            // Intentionally staged. Native OK applies; Back/Cancel discards.
        }

        internal void WireNavigation(Button okButton)
        {
            if (_toggle == null || _previous == null || okButton == null) return;
            GuiUtils.SetNavigationDown(_previous, _toggle);
            GuiUtils.SetNavigationUp(_toggle, _previous);
            GuiUtils.SetNavigationDown(_toggle, okButton);
            GuiUtils.SetNavigationUp(okButton, _toggle);
        }

        internal void Apply()
        {
            Bind();
            if (_setting == null || _toggle == null) return;
            bool before = _setting.Value;
            bool requested = _toggle.isOn;
            if (before == requested) return;

            _setting.Value = requested;
            bool current = _setting.Value;
            _toggle.SetIsOnWithoutNotify(current);
            if (current != before)
                MasteryPlugin.Log?.LogInfo("[PilotA] ExperimentalOptimization " + State(before) + " -> " + State(current) +
                    "; persisted/current=" + State(current) + "; scope=local-process.");
        }

        internal void Cancel()
        {
            ResetFromCurrent();
        }

        private void ResetFromCurrent()
        {
            Bind();
            if (_toggle == null) return;
            _toggle.interactable = _setting != null;
            _toggle.SetIsOnWithoutNotify(_setting?.Value == true);
        }

        private void Bind()
        {
            _setting = MasteryPlugin.Settings?.ExperimentalOptimization;
        }

        private static string State(bool value) => value ? "ON" : "OFF";

        private void OnDestroy()
        {
            if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnPendingValueChanged);
            _setting = null;
        }
    }

    [HarmonyPatch(typeof(AccessibilitySettings), nameof(AccessibilitySettings.Initialize))]
    internal static class ExperimentalOptimizationAccessibilityInitializePatch
    {
        private static void Postfix(AccessibilitySettings __instance)
        {
            ExperimentalOptimizationAccessibilityControl.Ensure(__instance, true);
        }
    }

    [HarmonyPatch(typeof(AccessibilitySettings), nameof(AccessibilitySettings.OnTabOpen))]
    internal static class ExperimentalOptimizationAccessibilityOpenPatch
    {
        private static void Postfix(AccessibilitySettings __instance, Button __1)
        {
            ExperimentalOptimizationAccessibilityControl.Ensure(__instance)?.WireNavigation(__1);
        }
    }

    [HarmonyPatch(typeof(AccessibilitySettings), nameof(AccessibilitySettings.OnOkAsync))]
    internal static class ExperimentalOptimizationAccessibilityOkPatch
    {
        private static void Prefix(AccessibilitySettings __instance)
        {
            ExperimentalOptimizationAccessibilityControl.Ensure(__instance)?.Apply();
        }
    }

    [HarmonyPatch(typeof(AccessibilitySettings), nameof(AccessibilitySettings.OnBack))]
    internal static class ExperimentalOptimizationAccessibilityBackPatch
    {
        private static void Prefix(AccessibilitySettings __instance)
        {
            ExperimentalOptimizationAccessibilityControl.Ensure(__instance)?.Cancel();
        }
    }
}
