using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Separate readable panel, deliberately parented to SkillsDialog so it can never remain over another vanilla menu.
    internal sealed class MasteryWindowController : MonoBehaviour
    {
        private Player _player;
        private GameObject _panel;
        private TextMeshProUGUI _header;
        private TextMeshProUGUI _body;
        private Skills.SkillType _selectedSkill;
        private float _nextCooldownRefresh;
        private GameObject _returnFocus;

        internal void Initialize(Player player, TMP_FontAsset uiFont)
        {
            _player = player;
            if (_panel == null) Build(uiFont);
            if (_selectedSkill == 0 && player != null) _selectedSkill = Skills.SkillType.Swords;
            Refresh();
        }

        internal void Toggle()
        {
            if (_panel == null) return;
            if (_panel.activeSelf) { Close(); return; }
            CaptureReturnFocus();
            _panel.SetActive(true);
            Refresh();
        }

        internal void Show()
        {
            if (_panel == null) return;
            if (!_panel.activeSelf) CaptureReturnFocus();
            _panel.SetActive(true);
            Refresh();
        }

        private void CaptureReturnFocus()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && (_panel == null || !selected.transform.IsChildOf(_panel.transform))) _returnFocus = selected;
        }

        private void Close()
        {
            if (_panel == null) return;
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            bool panelOwnedFocus = selected != null && selected.transform.IsChildOf(_panel.transform);
            _panel.SetActive(false);
            if (events != null && (panelOwnedFocus || selected == null || !selected.activeInHierarchy))
                events.SetSelectedGameObject(_returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null;
        }

        private void OnDisable()
        {
            if (_panel != null && _panel.activeSelf) Close();
        }

        internal void Select(Skills.SkillType skill)
        {
            _selectedSkill = skill;
            if (_panel != null && _panel.activeSelf) Refresh();
        }

        private void Build(TMP_FontAsset uiFont)
        {
            _panel = new GameObject("ValheimMasteryWindow", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            RectTransform panel = _panel.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.anchoredPosition = new Vector2(330f, 0f);
            panel.sizeDelta = new Vector2(470f, 570f);
            Image frame = _panel.GetComponent<Image>();
            frame.color = new Color(0.16f, 0.105f, 0.045f, 0.97f);

            GameObject title = MakeText(_panel.transform, "Header", uiFont, 22, TextAlignmentOptions.Center, new Vector2(18, -16), new Vector2(-18, -54));
            _header = title.GetComponent<TextMeshProUGUI>();
            _header.color = new Color(0.96f, 0.72f, 0.25f);            GameObject close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_panel.transform, false);
            RectTransform closeRect = close.GetComponent<RectTransform>(); closeRect.anchorMin = new Vector2(1f, 1f); closeRect.anchorMax = new Vector2(1f, 1f); closeRect.pivot = new Vector2(1f, 1f); closeRect.anchoredPosition = new Vector2(-12f, -10f); closeRect.sizeDelta = new Vector2(34f, 30f);
            close.GetComponent<Image>().color = new Color(0.42f, 0.16f, 0.08f, 0.95f); close.GetComponent<Button>().onClick.AddListener(Close);
            GameObject closeText = MakeText(close.transform, "X", uiFont, 20, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            RectTransform closeTextRect = closeText.GetComponent<RectTransform>(); closeTextRect.anchorMin = Vector2.zero; closeTextRect.anchorMax = Vector2.one; closeTextRect.pivot = new Vector2(0.5f, 0.5f); closeTextRect.offsetMin = closeTextRect.offsetMax = Vector2.zero; closeText.GetComponent<TextMeshProUGUI>().text = "×";

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(_panel.transform, false);
            RectTransform vp = viewport.GetComponent<RectTransform>();
            vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one;
            vp.offsetMin = new Vector2(18, 18); vp.offsetMax = new Vector2(-18, -62);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            GameObject content = MakeText(viewport.transform, "Content", uiFont, 17, TextAlignmentOptions.TopLeft, new Vector2(12, -8), new Vector2(-12, -8));
            RectTransform cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f); cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0.5f, 1f); cr.sizeDelta = new Vector2(0f, 700f);
            _body = content.GetComponent<TextMeshProUGUI>();
            _body.textWrappingMode = TextWrappingModes.Normal;
            _body.color = new Color(0.95f, 0.91f, 0.80f);
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = _panel.AddComponent<ScrollRect>();
            scroll.viewport = vp; scroll.content = cr; scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 30f;
            _panel.SetActive(false);
        }

        private void Update()
        {
            if (_panel == null || !_panel.activeSelf || Time.unscaledTime < _nextCooldownRefresh) return;
            _nextCooldownRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }
        private void Refresh()
        {
            if (_player == null || !PerkCatalog.Contains(_selectedSkill) || _header == null || _body == null) return;
            float level = PerkRuntimeService.GetActualSkillLevel(_player, _selectedSkill);
            _header.text = "МАЙСТЕРНІСТЬ — " + LocalSkillName(_selectedSkill) + "  <color=#E7B85C>" + Mathf.FloorToInt(level) + "</color>";
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append(PerkNarrativeService.SkillPassive(_player, _selectedSkill));
            foreach (PerkDefinition perk in PerkCatalog.Get(_selectedSkill))
            {
                bool unlocked = PerkStateService.IsUnlocked(_player, _selectedSkill, perk.Milestone);
                text.Append("<color=").Append(unlocked ? "#E7B85C" : "#9A958A").Append(">")
                    .Append(unlocked ? "◆ " : "◇ ").Append(perk.Milestone).Append(" — ")
                    .Append(PerkLocalization.Localize(perk.NameToken)).Append("</color>\n")
                    .Append(PerkNarrativeService.Description(perk));
                double remaining = PerkCooldownStateService.GetRemainingSeconds(_player, perk.Id == "run_70" ? "run_70_water" : perk.Id);
                if (remaining > 0d)
                    text.Append("\n<color=#E7B85C>Перезарядка: ").Append(FormatCooldown(remaining)).Append("</color>");
                text.Append("\n\n");
            }
            _body.text = text.ToString();
        }

        private static string FormatCooldown(double seconds)
        {
            int total = Mathf.CeilToInt((float)seconds);
            return (total / 60).ToString("0") + ":" + (total % 60).ToString("00");
        }
        private static GameObject MakeText(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment, Vector2 min, Vector2 max)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f); rect.offsetMin = min; rect.offsetMax = max;
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.alignment = alignment; text.raycastTarget = false;
            return go;
        }

        private static string LocalSkillName(Skills.SkillType skill) => PerkLocalization.Localize("$skill_" + skill.ToString().ToLowerInvariant());
    }

    internal static class MasteryWindowService
    {
        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(SkillsDialog), "m_elements");
        private static readonly Dictionary<SkillsDialog, MasteryWindowController> Controllers = new Dictionary<SkillsDialog, MasteryWindowController>();

        internal static void Ensure(SkillsDialog dialog, Player player)
        {
            if (dialog == null || player == null || player != Player.m_localPlayer) return;
            PerkUiIconService.Capture(dialog, player);
            if (!Controllers.TryGetValue(dialog, out MasteryWindowController controller) || controller == null)
            {
                controller = dialog.gameObject.AddComponent<MasteryWindowController>();
                Controllers[dialog] = controller;
                CreateButton(dialog, controller, FindVanillaFont(dialog));
            }
            controller.Initialize(player, FindVanillaFont(dialog));
        }

        internal static void Select(SkillsDialog dialog, GameObject selected)
        {
            if (dialog == null || selected == null || !Controllers.TryGetValue(dialog, out MasteryWindowController controller)) return;
            List<GameObject> rows = ElementsField?.GetValue(dialog) as List<GameObject>;
            List<Skills.Skill> skills = Player.m_localPlayer?.GetSkills().GetSkillList();
            if (rows == null || skills == null) return;
            for (int i = 0; i < Mathf.Min(rows.Count, skills.Count); ++i)
                if (rows[i] != null && (selected == rows[i] || selected.transform.IsChildOf(rows[i].transform)) && skills[i]?.m_info != null)
                { controller.Select(skills[i].m_info.m_skill); controller.Show(); return; }
        }

        private static TMP_FontAsset FindVanillaFont(SkillsDialog dialog)
        {
            TextMeshProUGUI source = dialog.GetComponentInChildren<TextMeshProUGUI>(true);
            return source != null ? source.font : null;
        }

        private static void CreateButton(SkillsDialog dialog, MasteryWindowController controller, TMP_FontAsset font)
        {
            GameObject buttonGo = new GameObject("ValheimMasteryButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(dialog.transform, false);
            RectTransform r = buttonGo.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(1f, 0f); r.anchorMax = new Vector2(1f, 0f); r.pivot = new Vector2(1f, 0f);
            r.anchoredPosition = new Vector2(-16f, 18f); r.sizeDelta = new Vector2(170f, 42f);
            buttonGo.GetComponent<Image>().color = new Color(0.35f, 0.22f, 0.08f, 0.95f);
            buttonGo.GetComponent<Button>().onClick.AddListener(controller.Toggle);
            GameObject label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(buttonGo.transform, false);
            RectTransform lr = label.GetComponent<RectTransform>(); lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
            TextMeshProUGUI t = label.GetComponent<TextMeshProUGUI>();
            t.font = font; t.alignment = TextAlignmentOptions.Center; t.fontSize = 18; t.raycastTarget = false; t.text = "Майстерність";
        }
    }

    [HarmonyPatch(typeof(SkillsDialog), "Setup")]
    internal static class MasteryWindowSetupPatch { private static void Postfix(SkillsDialog __instance, Player player) => MasteryWindowService.Ensure(__instance, player); }
    [HarmonyPatch(typeof(SkillsDialog), "SkillClicked")]
    internal static class MasteryWindowSelectionPatch { private static void Postfix(SkillsDialog __instance, GameObject selectedObject) => MasteryWindowService.Select(__instance, selectedObject); }
}


