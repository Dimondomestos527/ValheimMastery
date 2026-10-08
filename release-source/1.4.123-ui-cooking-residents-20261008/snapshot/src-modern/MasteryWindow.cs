using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimMastery
{
    // One asset-backed Skills/Mastery surface. Native skill data remains authoritative;
    // the native rows are hidden and restored rather than destroyed or rewritten.
    internal sealed class MasteryWindowController : MonoBehaviour
    {
        private sealed class SkillRow
        {
            internal Skills.SkillType Skill;
            internal Button Button;
            internal UITooltip Tooltip;
            internal Image Backing;
            internal Image Icon;
            internal TextMeshProUGUI Name;
            internal TextMeshProUGUI Level;
            internal TextMeshProUGUI Xp;
            internal RectTransform Fill;
        }

        private sealed class PerkCard
        {
            internal GameObject Root;
            internal LayoutElement Layout;
            internal Image Icon;
            internal TextMeshProUGUI Milestone;
            internal TextMeshProUGUI Title;
            internal TextMeshProUGUI Description;
            internal TextMeshProUGUI State;
        }

        private static readonly Skills.SkillType[] SkillOrder =
        {
            Skills.SkillType.Cooking, Skills.SkillType.Crafting, Skills.SkillType.Farming,
            Skills.SkillType.Fishing, Skills.SkillType.Jump, Skills.SkillType.Pickaxes,
            Skills.SkillType.Ride, Skills.SkillType.Run, Skills.SkillType.Sneak,
            Skills.SkillType.Swim, Skills.SkillType.WoodCutting, Skills.SkillType.Swords,
            Skills.SkillType.Axes, Skills.SkillType.Clubs, Skills.SkillType.Knives,
            Skills.SkillType.Spears, Skills.SkillType.Polearms, Skills.SkillType.Bows,
            Skills.SkillType.Crossbows, Skills.SkillType.Unarmed, Skills.SkillType.Blocking,
            Skills.SkillType.Dodge, Skills.SkillType.ElementalMagic, Skills.SkillType.BloodMagic
        };

        private static readonly Color Cream = new Color(.94f, .89f, .77f, 1f);
        private static readonly Color Muted = new Color(.67f, .61f, .50f, 1f);
        private static readonly Color Gold = new Color(.78f, .53f, .18f, 1f);
        private static readonly Color Selected = new Color(.27f, .19f, .09f, .96f);
        private static readonly Color Resting = new Color(.08f, .055f, .025f, .18f);
        private static readonly FieldInfo TooltipPrefabField = AccessTools.Field(typeof(UITooltip), "m_tooltipPrefab");
        private static readonly FieldInfo TooltipDelayField = AccessTools.Field(typeof(UITooltip), "m_showDelay");
        private static readonly MethodInfo HideTooltipMethod = AccessTools.Method(typeof(UITooltip), "HideTooltip");

        private readonly List<SkillRow> _skillRows = new List<SkillRow>();
        private readonly List<PerkCard> _perkCards = new List<PerkCard>();
        private readonly Dictionary<Graphic, bool> _nativeGraphicStates = new Dictionary<Graphic, bool>();
        private SkillsDialog _dialog;
        private Player _player;
        private ZNet _uiSession;
        private TMP_FontAsset _font;
        private GameObject _panel;
        private GameObject _skillsView;
        private GameObject _masteryView;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _skillsSummary;
        private TextMeshProUGUI _masteryHeading;
        private TextMeshProUGUI _passive;
        private RectTransform _masteryContent;
        private ScrollRect _skillsScroll;
        private ScrollRect _masteryScroll;
        private Button _footerButton;
        private TextMeshProUGUI _footerLabel;
        private Button _closeButton;
        private RectTransform _nativeListRoot;
        private ScrollRect _nativeScroll;
        private Scrollbar _nativeScrollbar;
        private TMP_Text _nativeTotal;
        private RectTransform _nativeTooltip;
        private Skills.SkillType _selectedSkill = Skills.SkillType.Swords;
        private GameObject _focusBeforeOpen;
        private float _nextRefresh;
        private float _nextControllerStep;
        private bool _customVisible;
        private bool _masteryVisible;
        private bool _nativeStateCaptured;
        private bool _listWasActive;
        private bool _scrollWasEnabled;
        private bool _scrollbarWasActive;
        private bool _totalWasActive;
        private bool _tooltipWasActive;
        private bool _nativePresentationCaptured;
        private bool _assetFailed;

        internal bool CustomVisible => _customVisible && _panel != null && _panel.activeSelf;
        internal bool MasteryVisible => CustomVisible && _masteryVisible;

        internal void Initialize(SkillsDialog dialog, Player player, TMP_FontAsset uiFont,
            RectTransform listRoot, ScrollRect nativeScroll, Scrollbar nativeScrollbar,
            TMP_Text nativeTotal, RectTransform nativeTooltip)
        {
            _dialog = dialog;
            _player = player;
            _uiSession = ZNet.instance;
            _font = uiFont;
            _nativeListRoot = listRoot;
            _nativeScroll = nativeScroll;
            _nativeScrollbar = nativeScrollbar;
            _nativeTotal = nativeTotal;
            _nativeTooltip = nativeTooltip;
            if (_panel == null && !_assetFailed) Build();
            if (_panel == null) return; // Exact artwork unavailable: keep the native UI intact.

            CaptureFocusBeforeOpen();
            CaptureNativeState();
            CaptureNativePresentation();
            ApplyNativeVisibility(false);
            ApplyNativePresentation(false);
            _customVisible = true;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            ApplyAdaptiveScale();
            ShowSkills(false);
            RaisePantheon();
            RefreshAll();
        }

        internal void Toggle()
        {
            if (!CustomVisible) return;
            if (MasteryVisible) ShowSkills();
            else ShowMastery();
        }

        internal void Show() => ShowMastery();

        internal void ShowMastery()
        {
            if (!CanPresent()) return;
            HideActiveTooltip();
            GoldPantheonUi.Hide(_dialog);
            _masteryVisible = true;
            _skillsView.SetActive(false);
            _masteryView.SetActive(true);
            _title.text = "МАЙСТЕРНІСТЬ";
            _footerLabel.text = "Навички";
            RefreshMastery();
            SelectForInput(_footerButton != null ? _footerButton.gameObject : null);
        }

        internal void ShowSkills() => ShowSkills(true);

        private void ShowSkills(bool restoreFocus)
        {
            if (!CanPresent()) return;
            _masteryVisible = false;
            _skillsView.SetActive(true);
            _masteryView.SetActive(false);
            _title.text = "НАВИЧКИ";
            _footerLabel.text = "Майстерність";
            RefreshSkills();
            if (restoreFocus) SelectSelectedRow();
        }

        internal void Select(Skills.SkillType skill)
        {
            if (!PerkCatalog.Contains(skill)) return;
            HideActiveTooltip();
            _selectedSkill = skill;
            RefreshSkills();
            ShowMastery();
        }

        private bool CanPresent()
        {
            return MasteryWindowService.Ready && _panel != null && _player != null &&
                _player == Player.m_localPlayer && _uiSession == ZNet.instance;
        }

        private void CaptureFocusBeforeOpen()
        {
            if (_focusBeforeOpen != null || EventSystem.current == null) return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && (_panel == null || !selected.transform.IsChildOf(_panel.transform)))
                _focusBeforeOpen = selected;
        }

        private void CaptureNativeState()
        {
            if (_nativeStateCaptured) return;
            _nativeStateCaptured = true;
            _listWasActive = _nativeListRoot != null && _nativeListRoot.gameObject.activeSelf;
            _scrollWasEnabled = _nativeScroll != null && _nativeScroll.enabled;
            _scrollbarWasActive = _nativeScrollbar != null && _nativeScrollbar.gameObject.activeSelf;
            _totalWasActive = _nativeTotal != null && _nativeTotal.gameObject.activeSelf;
            _tooltipWasActive = _nativeTooltip != null && _nativeTooltip.gameObject.activeSelf;
        }

        private void ApplyNativeVisibility(bool visible)
        {
            if (_nativeListRoot != null) _nativeListRoot.gameObject.SetActive(visible && _listWasActive);
            if (_nativeScroll != null) _nativeScroll.enabled = visible && _scrollWasEnabled;
            if (_nativeScrollbar != null) _nativeScrollbar.gameObject.SetActive(visible && _scrollbarWasActive);
            if (_nativeTotal != null) _nativeTotal.gameObject.SetActive(visible && _totalWasActive);
            if (_nativeTooltip != null) _nativeTooltip.gameObject.SetActive(visible && _tooltipWasActive);
        }

        private void RestoreNativeState()
        {
            if (!_nativeStateCaptured) return;
            ApplyNativeVisibility(true);
            _nativeStateCaptured = false;
        }

        private void CaptureNativePresentation()
        {
            if (_nativePresentationCaptured || _dialog == null) return;
            _nativeGraphicStates.Clear();
            foreach (Graphic graphic in _dialog.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || (_panel != null && (graphic.gameObject == _panel ||
                    graphic.transform.IsChildOf(_panel.transform))) ||
                    IsPantheonGraphic(graphic.transform)) continue;
                _nativeGraphicStates[graphic] = graphic.enabled;
            }
            _nativePresentationCaptured = true;
        }

        private void ApplyNativePresentation(bool visible)
        {
            if (!_nativePresentationCaptured) return;
            foreach (KeyValuePair<Graphic, bool> pair in _nativeGraphicStates)
                if (pair.Key != null) pair.Key.enabled = visible && pair.Value;
        }

        private void RestoreNativePresentation()
        {
            if (!_nativePresentationCaptured) return;
            foreach (KeyValuePair<Graphic, bool> pair in _nativeGraphicStates)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            _nativeGraphicStates.Clear();
            _nativePresentationCaptured = false;
        }

        private void Build()
        {
            Sprite wood = MasteryHudArtwork.Art("mastery_window_wood.png");
            if (wood == null)
            {
                _assetFailed = true;
                MasteryPlugin.Log?.LogWarning("Mastery window artwork missing; retaining native Skills UI.");
                return;
            }

            Transform parent = _dialog != null ? _dialog.transform : transform;
            _panel = new GameObject("ValheimMastery_AssetWindow", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(parent, false);
            RectTransform panelRect = _panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = new Vector2(.5f, .5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = Vector2.zero;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image frame = _panel.GetComponent<Image>();
            frame.sprite = wood;
            frame.material = null;
            frame.type = Image.Type.Simple;
            frame.preserveAspect = false;
            frame.color = Color.white;
            frame.raycastTarget = true;

            _title = CreateText(_panel.transform, "DynamicTitle", 29f, TextAlignmentOptions.Center, Cream);
            SetAnchored(_title.rectTransform, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -35f), new Vector2(350f, 44f), new Vector2(.5f, .5f));

            _skillsView = CreateStretchRoot(_panel.transform, "SkillsView", new Vector2(52f, 67f), new Vector2(-52f, -82f));
            BuildSkillsView();
            _masteryView = CreateStretchRoot(_panel.transform, "MasteryView", new Vector2(52f, 67f), new Vector2(-52f, -82f));
            BuildMasteryView();
            BuildFooter();
            BuildCloseButton();
            LinkSkillNavigation();
            _panel.SetActive(false);
        }

        private void BuildSkillsView()
        {
            _skillsSummary = CreateText(_skillsView.transform, "SkillCount", 16f, TextAlignmentOptions.Center, Cream);
            SetAnchored(_skillsSummary.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -16f), new Vector2(0f, 28f), new Vector2(.5f, .5f));

            RectTransform viewport = CreateViewport(_skillsView.transform, "SkillsViewport", new Vector2(0f, 0f), new Vector2(0f, -38f));
            RectTransform content = CreateVerticalContent(viewport, "SkillRows", 4f, new RectOffset(2, 2, 2, 4));
            _skillsScroll = _skillsView.AddComponent<ScrollRect>();
            ConfigureScroll(_skillsScroll, viewport, content, 34f);
            foreach (Skills.SkillType skill in SkillOrder) _skillRows.Add(CreateSkillRow(content, skill));
        }

        private SkillRow CreateSkillRow(Transform parent, Skills.SkillType skill)
        {
            GameObject root = new GameObject("Skill_" + skill, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 54f;
            Image backing = root.GetComponent<Image>();
            backing.color = Resting;
            backing.raycastTarget = true;
            Button button = root.GetComponent<Button>();
            button.targetGraphic = backing;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, .82f, .48f, 1f);
            colors.pressedColor = new Color(.78f, .55f, .24f, 1f);
            colors.selectedColor = new Color(1f, .82f, .48f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => Select(skill));
            UITooltip tooltip = CreateNativeTooltip(root);

            Image icon = CreateImage(root.transform, "Icon", null, Color.white);
            SetAnchored(icon.rectTransform, new Vector2(0f, .5f), new Vector2(0f, .5f),
                new Vector2(25f, 0f), new Vector2(40f, 40f), new Vector2(.5f, .5f));
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TextMeshProUGUI name = CreateText(root.transform, "Name", 17f, TextAlignmentOptions.MidlineLeft, Cream);
            SetStretch(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(50f, 5f), new Vector2(-91f, -5f));

            TextMeshProUGUI level = CreateText(root.transform, "Level", 20f, TextAlignmentOptions.TopRight, Cream);
            SetAnchored(level.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-7f, -5f), new Vector2(78f, 25f), new Vector2(1f, 1f));

            TextMeshProUGUI xp = CreateText(root.transform, "Xp", 9.5f, TextAlignmentOptions.BottomRight, Muted);
            SetAnchored(xp.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-7f, 5f), new Vector2(92f, 17f), new Vector2(1f, 0f));

            Image track = CreateImage(root.transform, "XpTrack", MasteryHudArtwork.Art("track.png"), new Color(.25f, .18f, .08f, .92f));
            SetAnchored(track.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-7f, 3f), new Vector2(86f, 5f), new Vector2(1f, 0f));
            track.type = Image.Type.Sliced;
            track.raycastTarget = false;
            Image fill = CreateImage(track.transform, "Fill", MasteryHudArtwork.Art("fill.png"), Gold);
            fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, .5f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            fill.type = Image.Type.Simple;
            fill.raycastTarget = false;

            return new SkillRow
            {
                Skill = skill, Button = button, Tooltip = tooltip, Backing = backing, Icon = icon,
                Name = name, Level = level, Xp = xp, Fill = fill.rectTransform
            };
        }

        private void BuildMasteryView()
        {
            RectTransform viewport = CreateViewport(_masteryView.transform, "MasteryViewport", Vector2.zero, Vector2.zero);
            _masteryContent = CreateVerticalContent(viewport, "MasteryContent", 5f, new RectOffset(4, 4, 1, 10));
            _masteryScroll = _masteryView.AddComponent<ScrollRect>();
            ConfigureScroll(_masteryScroll, viewport, _masteryContent, 30f);

            _masteryHeading = CreateText(_masteryContent, "SelectedSkill", 21f, TextAlignmentOptions.Center, Cream);
            LayoutElement headingLayout = _masteryHeading.gameObject.AddComponent<LayoutElement>();
            headingLayout.minHeight = headingLayout.preferredHeight = 34f;
            _passive = CreateText(_masteryContent, "PassiveNarrative", 13.5f, TextAlignmentOptions.TopLeft, Muted);
            _passive.textWrappingMode = TextWrappingModes.Normal;
            _passive.overflowMode = TextOverflowModes.Overflow;
            _passive.margin = new Vector4(5f, 0f, 5f, 0f);
            _passive.gameObject.AddComponent<LayoutElement>().minHeight = 20f;

            for (int i = 0; i < 3; ++i) _perkCards.Add(CreatePerkCard(_masteryContent, i));
        }

        private PerkCard CreatePerkCard(Transform parent, int index)
        {
            GameObject root = new GameObject("PerkCard_" + index, typeof(RectTransform), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 116f;

            Image icon = CreateImage(root.transform, "Icon", null, Color.white);
            SetAnchored(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -31f), new Vector2(48f, 48f), new Vector2(.5f, .5f));
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TextMeshProUGUI milestone = CreateText(root.transform, "Milestone", 15f, TextAlignmentOptions.Center, Gold);
            SetAnchored(milestone.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -64f), new Vector2(52f, 21f), new Vector2(.5f, .5f));

            TextMeshProUGUI title = CreateText(root.transform, "PerkTitle", 18f, TextAlignmentOptions.TopLeft, Cream);
            title.textWrappingMode = TextWrappingModes.Normal;
            title.overflowMode = TextOverflowModes.Overflow;
            SetAnchored(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(62f, -7f), new Vector2(-67f, 27f), new Vector2(0f, 1f));

            TextMeshProUGUI description = CreateText(root.transform, "Description", 13.5f, TextAlignmentOptions.TopLeft, Cream);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Overflow;
            SetAnchored(description.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(62f, -34f), new Vector2(-67f, 50f), new Vector2(0f, 1f));

            TextMeshProUGUI state = CreateText(root.transform, "UnlockState", 11.5f, TextAlignmentOptions.TopLeft, Muted);
            state.overflowMode = TextOverflowModes.Overflow;
            SetAnchored(state.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(62f, -88f), new Vector2(-67f, 20f), new Vector2(0f, 1f));

            Image separator = CreateImage(root.transform, "Separator", MasteryHudArtwork.Art("fill.png"), new Color(.63f, .40f, .13f, .58f));
            separator.rectTransform.anchorMin = new Vector2(0f, 0f);
            separator.rectTransform.anchorMax = new Vector2(1f, 0f);
            separator.rectTransform.pivot = new Vector2(.5f, 0f);
            separator.rectTransform.offsetMin = new Vector2(0f, 0f);
            separator.rectTransform.offsetMax = new Vector2(0f, 1.5f);
            separator.raycastTarget = false;

            return new PerkCard
            {
                Root = root, Layout = layout, Icon = icon, Milestone = milestone,
                Title = title, Description = description, State = state
            };
        }

        private void BuildFooter()
        {
            Button native = FindNativeButton();
            GameObject root = new GameObject("ValheimMastery_FooterSwitch", typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(_panel.transform, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            SetAnchored(rect, new Vector2(.5f, 0f), new Vector2(.5f, 0f),
                new Vector2(0f, 19f), new Vector2(176f, 39f), new Vector2(.5f, 0f));
            Image image = root.GetComponent<Image>();
            CopyButtonStyle(image, native != null ? native.targetGraphic as Image : null);
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            if (native != null)
            {
                button.transition = native.transition;
                button.colors = native.colors;
                button.spriteState = native.spriteState;
            }
            button.onClick.AddListener(Toggle);
            _footerButton = button;
            _footerLabel = CreateText(root.transform, "Label", 17f, TextAlignmentOptions.Center, Cream);
            SetStretch(_footerLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private void BuildCloseButton()
        {
            Button native = FindNativeCloseButton();
            GameObject root = new GameObject("ValheimMastery_Close", typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(_panel.transform, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            SetAnchored(rect, Vector2.one, Vector2.one,
                new Vector2(-24f, -24f), new Vector2(38f, 38f), Vector2.one);
            Image image = root.GetComponent<Image>();
            CopyButtonStyle(image, native != null ? native.targetGraphic as Image : null);
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            if (native != null)
            {
                button.transition = native.transition;
                button.colors = native.colors;
                button.spriteState = native.spriteState;
            }
            button.onClick.AddListener(Close);
            _closeButton = button;
            TextMeshProUGUI label = CreateText(root.transform, "Label", 24f, TextAlignmentOptions.Center, Cream);
            label.text = "×";
            SetStretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private UITooltip CreateNativeTooltip(GameObject target)
        {
            if (_dialog == null || target == null || TooltipPrefabField == null) return null;
            UITooltip donor = null;
            foreach (UITooltip candidate in _dialog.GetComponentsInChildren<UITooltip>(true))
            {
                if (candidate == null || (_panel != null && candidate.transform.IsChildOf(_panel.transform))) continue;
                if (TooltipPrefabField.GetValue(candidate) != null)
                {
                    donor = candidate;
                    break;
                }
            }
            if (donor == null) return null;

            UITooltip tooltip = target.AddComponent<UITooltip>();
            TooltipPrefabField.SetValue(tooltip, TooltipPrefabField.GetValue(donor));
            if (TooltipDelayField != null) TooltipDelayField.SetValue(tooltip, TooltipDelayField.GetValue(donor));
            if (tooltip.GetComponent<MasteryHudTooltipTag>() == null)
                tooltip.gameObject.AddComponent<MasteryHudTooltipTag>();
            return tooltip;
        }

        private void RefreshAll()
        {
            RefreshSkills();
            RefreshMastery();
        }

        private void RefreshSkills()
        {
            if (_player == null) return;
            Skills skills = _player.GetSkills();
            if (skills == null) return;
            _skillsSummary.text = "Навички · " + SkillOrder.Length;
            foreach (SkillRow row in _skillRows)
            {
                Skills.Skill source = FindExistingSkill(skills, row.Skill);
                Skills.SkillDef definition = source?.m_info ?? skills.GetSkillDef(row.Skill);
                float level = source != null ? source.m_level : 0f;
                float currentXp = source != null ? Mathf.Max(0f, source.m_accumulator) : 0f;
                float requiredXp = source != null ? Mathf.Max(0f, source.GetNextLevelRequirement()) : 0f;
                float fraction = requiredXp > .001f ? Mathf.Clamp01(currentXp / requiredXp) : 0f;
                IReadOnlyList<PerkDefinition> perks = PerkCatalog.Get(row.Skill);
                string firstId = perks.Count > 0 ? perks[0].Id : null;
                Sprite nativeIcon = definition != null ? definition.m_icon : null;
                row.Icon.sprite = firstId != null ? PerkUiIconService.ForPerk(firstId, nativeIcon) : nativeIcon;
                row.Name.text = LocalSkillName(row.Skill);
                row.Level.text = Mathf.FloorToInt(level).ToString();
                row.Xp.text = requiredXp > .001f
                    ? "XP " + FormatNumber(currentXp) + " / " + FormatNumber(requiredXp)
                    : "XP " + FormatNumber(currentXp);
                row.Fill.anchorMax = new Vector2(fraction, 1f);
                bool selected = row.Skill == _selectedSkill;
                row.Backing.color = selected ? Selected : Resting;
                row.Name.color = selected ? Color.white : Cream;
                if (row.Tooltip != null && source?.m_info != null)
                {
                    RectTransform anchor = _nativeTooltip != null ? _nativeTooltip : _panel.GetComponent<RectTransform>();
                    row.Tooltip.Set(row.Name.text + " — " + Mathf.FloorToInt(level),
                        SkillsTooltipPatch.BuildText(_player, source), anchor, Vector2.zero);
                }
            }
        }

        private void RefreshMastery()
        {
            if (_player == null || !PerkCatalog.Contains(_selectedSkill) || _masteryHeading == null) return;
            float level = PerkRuntimeService.GetActualSkillLevel(_player, _selectedSkill);
            _masteryHeading.text = LocalSkillName(_selectedSkill) + "  ·  <color=#D7A244>" + Mathf.FloorToInt(level) + "</color>";
            _passive.text = PerkNarrativeService.SkillPassive(_player, _selectedSkill).Trim();
            float passiveHeight = Mathf.Max(24f, _passive.GetPreferredValues(_passive.text, 344f, 0f).y + 8f);
            LayoutElement passiveLayout = _passive.GetComponent<LayoutElement>();
            passiveLayout.preferredHeight = passiveHeight;

            IReadOnlyList<PerkDefinition> perks = PerkCatalog.Get(_selectedSkill);
            Skills skills = _player.GetSkills();
            Sprite nativeIcon = skills?.GetSkillDef(_selectedSkill)?.m_icon;
            for (int i = 0; i < _perkCards.Count; ++i)
            {
                PerkCard card = _perkCards[i];
                bool exists = i < perks.Count;
                card.Root.SetActive(exists);
                if (!exists) continue;
                PerkDefinition perk = perks[i];
                bool unlocked = PerkStateService.IsUnlocked(_player, _selectedSkill, perk.Milestone);
                double remaining = PerkCooldownStateService.GetRemainingSeconds(_player,
                    perk.Id == "run_70" ? "run_70_water" : perk.Id);
                card.Icon.sprite = PerkUiIconService.ForPerk(perk.Id, nativeIcon);
                card.Icon.color = unlocked ? Color.white : new Color(.60f, .58f, .53f, .82f);
                card.Milestone.text = perk.Milestone.ToString();
                card.Title.text = PerkLocalization.Localize(perk.NameToken);
                card.Title.color = unlocked ? Cream : Muted;
                card.Description.text = PerkNarrativeService.Description(perk);
                card.Description.color = unlocked ? Cream : new Color(.72f, .68f, .60f, 1f);
                card.State.text = unlocked
                    ? remaining > 0d ? "Перезарядка: " + FormatCooldown(remaining) : "Відкрито"
                    : "Відкриється на рівні " + perk.Milestone;
                card.State.color = unlocked ? Gold : Muted;
                float titleHeight = Mathf.Max(24f, card.Title.GetPreferredValues(card.Title.text, 285f, 0f).y);
                card.Title.rectTransform.sizeDelta = new Vector2(-67f, titleHeight);
                float descriptionTop = 10f + titleHeight;
                float descriptionHeight = Mathf.Max(34f, card.Description.GetPreferredValues(card.Description.text, 285f, 0f).y);
                card.Description.rectTransform.anchoredPosition = new Vector2(62f, -descriptionTop);
                card.Description.rectTransform.sizeDelta = new Vector2(-67f, descriptionHeight);
                float stateTop = descriptionTop + descriptionHeight + 5f;
                card.State.rectTransform.anchoredPosition = new Vector2(62f, -stateTop);
                card.Layout.preferredHeight = Mathf.Max(104f, stateTop + 30f);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_masteryContent);
        }

        private static Skills.Skill FindExistingSkill(Skills skills, Skills.SkillType type)
        {
            if (skills?.m_skillData == null) return null;
            if (skills.m_skillData.TryGetValue(type, out Skills.Skill found) && found?.m_info?.m_skill == type)
                return found;
            foreach (KeyValuePair<Skills.SkillType, Skills.Skill> pair in skills.m_skillData)
                if (pair.Value?.m_info?.m_skill == type) return pair.Value;
            return null;
        }

        private void LinkSkillNavigation()
        {
            for (int i = 0; i < _skillRows.Count; ++i)
            {
                Navigation navigation = Navigation.defaultNavigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp = i > 0 ? _skillRows[i - 1].Button : _footerButton;
                navigation.selectOnDown = i + 1 < _skillRows.Count ? _skillRows[i + 1].Button : _footerButton;
                _skillRows[i].Button.navigation = navigation;
            }
            if (_footerButton != null && _skillRows.Count > 0)
            {
                Navigation footer = Navigation.defaultNavigation;
                footer.mode = Navigation.Mode.Explicit;
                footer.selectOnUp = _skillRows[_skillRows.Count - 1].Button;
                footer.selectOnDown = _skillRows[0].Button;
                _footerButton.navigation = footer;
            }
        }

        private void SelectSelectedRow()
        {
            foreach (SkillRow row in _skillRows)
                if (row.Skill == _selectedSkill)
                {
                    SelectForInput(row.Button.gameObject);
                    return;
                }
            SelectForInput(_footerButton != null ? _footerButton.gameObject : null);
        }

        private void Update()
        {
            if (!MasteryWindowService.Ready || _player == null ||
                _player != Player.m_localPlayer || _uiSession != ZNet.instance)
            {
                InvalidateSession();
                return;
            }
            if (!CustomVisible) return;
            ApplyAdaptiveScale();
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .25f;
                if (MasteryVisible) RefreshMastery();
                else RefreshSkills();
            }
            if (!ZInput.IsExclusiveGamepadActive() || ZInput.IsTouchActive()) return;
            ScrollRect active = MasteryVisible ? _masteryScroll : _skillsScroll;
            float axis = ZInput.GetJoyRightStickY();
            if (active != null && Mathf.Abs(axis) > .1f)
                active.verticalNormalizedPosition = Mathf.Clamp01(
                    active.verticalNormalizedPosition + axis * Time.unscaledDeltaTime * 1.5f);
            if (ZInput.GetButtonDown("TabLeft")) ShowSkills();
            else if (ZInput.GetButtonDown("TabRight") && !MasteryVisible) ShowMastery();
            if (ZInput.GetButtonDown("JoyButtonB"))
            {
                Close();
                return;
            }
            if (Time.unscaledTime < _nextControllerStep || active == null || !MasteryVisible) return;
            float step = 0f;
            if (ZInput.GetButtonDown("JoyDPadUp")) step = .12f;
            else if (ZInput.GetButtonDown("JoyDPadDown")) step = -.12f;
            if (Mathf.Abs(step) > 0f)
            {
                active.verticalNormalizedPosition = Mathf.Clamp01(active.verticalNormalizedPosition + step);
                _nextControllerStep = Time.unscaledTime + .12f;
            }
        }

        private void InvalidateSession()
        {
            bool ownedFocus = OwnsFocus();
            HideActiveTooltip();
            _customVisible = false;
            _masteryVisible = false;
            if (_panel != null) _panel.SetActive(false);
            RestoreNativeState();
            RestoreNativePresentation();
            if (ownedFocus) RestoreFocus();
        }

        internal void RestoreForDisabled()
        {
            InvalidateSession();
            if (_panel != null) Destroy(_panel);
            _panel = null;
            _skillsView = null;
            _masteryView = null;
            _title = null;
            _skillsSummary = null;
            _masteryHeading = null;
            _passive = null;
            _masteryContent = null;
            _skillsScroll = null;
            _masteryScroll = null;
            _footerButton = null;
            _footerLabel = null;
            _closeButton = null;
            _skillRows.Clear();
            _perkCards.Clear();
            _assetFailed = false;
        }

        private void OnDisable()
        {
            bool ownedFocus = OwnsFocus();
            HideActiveTooltip();
            _customVisible = false;
            _masteryVisible = false;
            if (_panel != null) _panel.SetActive(false);
            RestoreNativeState();
            RestoreNativePresentation();
            if (ownedFocus) RestoreFocus();
        }

        private bool OwnsFocus()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && _panel != null && selected.transform.IsChildOf(_panel.transform);
        }

        private void RestoreFocus()
        {
            if (EventSystem.current == null) return;
            if (_focusBeforeOpen != null && _focusBeforeOpen.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(_focusBeforeOpen);
            else EventSystem.current.SetSelectedGameObject(null);
            _focusBeforeOpen = null;
        }

        private void OnDestroy()
        {
            RestoreNativePresentation();
            MasteryWindowService.Forget(_dialog, this);
        }

        private void RaisePantheon()
        {
            if (_dialog == null) return;
            Transform panel = _dialog.transform.Find("ValheimMastery_PantheonPanel");
            Transform button = _dialog.transform.Find("ValheimMastery_PantheonButton");
            if (panel != null) panel.SetAsLastSibling();
            if (button != null) button.SetAsLastSibling();
        }

        private void ApplyAdaptiveScale()
        {
            if (_panel == null) return;
            // The native dialog already owns resolution/UI-scale adaptation. Matching its full
            // RectTransform avoids a second scale pass that produced the tiny nested live panel.
            _panel.transform.localScale = Vector3.one;
        }

        private Button FindNativeButton()
        {
            if (_dialog == null) return null;
            foreach (Button candidate in _dialog.GetComponentsInChildren<Button>(true))
                if (candidate != null && !candidate.name.StartsWith("ValheimMastery", StringComparison.Ordinal)) return candidate;
            return null;
        }

        private Button FindNativeCloseButton()
        {
            if (_dialog == null) return null;
            foreach (Button candidate in _dialog.GetComponentsInChildren<Button>(true))
                if (candidate != null && candidate.name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            return null;
        }

        private static bool IsPantheonGraphic(Transform transform)
        {
            for (Transform cursor = transform; cursor != null; cursor = cursor.parent)
                if (cursor.name.StartsWith("ValheimMastery_Pantheon", StringComparison.Ordinal)) return true;
            return false;
        }

        private void Close()
        {
            HideActiveTooltip();
            _dialog?.OnClose();
        }

        private static void HideActiveTooltip()
        {
            try { HideTooltipMethod?.Invoke(null, null); }
            catch { }
        }

        private static RectTransform CreateViewport(Transform parent, string name, Vector2 min, Vector2 max)
        {
            GameObject viewport = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(parent, false);
            RectTransform rect = viewport.GetComponent<RectTransform>();
            SetStretch(rect, Vector2.zero, Vector2.one, min, max);
            Image image = viewport.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            return rect;
        }

        private static RectTransform CreateVerticalContent(Transform parent, string name, float spacing, RectOffset padding)
        {
            GameObject content = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(parent, false);
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            VerticalLayoutGroup group = content.GetComponent<VerticalLayoutGroup>();
            group.padding = padding;
            group.spacing = spacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static void ConfigureScroll(ScrollRect scroll, RectTransform viewport, RectTransform content, float sensitivity)
        {
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = sensitivity;
        }

        private static GameObject CreateStretchRoot(Transform parent, string name, Vector2 min, Vector2 max)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            SetStretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, min, max);
            return root;
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, float size,
            TextAlignmentOptions alignment, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static Image CreateImage(Transform parent, string name, Sprite sprite, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return image;
        }

        private static void CopyButtonStyle(Image target, Image source)
        {
            target.color = new Color(.27f, .17f, .07f, .98f);
            target.raycastTarget = true;
            if (source == null || source.sprite == null) return;
            target.sprite = source.sprite;
            target.material = source.material;
            target.type = source.type;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            target.fillCenter = source.fillCenter;
        }

        private static void SetStretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SelectForInput(GameObject target)
        {
            if (target == null || !target.activeInHierarchy || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(target);
        }

        private static string FormatCooldown(double seconds)
        {
            int total = Mathf.CeilToInt((float)seconds);
            return (total / 60).ToString("0") + ":" + (total % 60).ToString("00");
        }

        private static string FormatNumber(float value)
        {
            if (value >= 1000f) return value.ToString("0");
            if (value >= 100f) return value.ToString("0.#");
            return value.ToString("0.##");
        }

        private static string LocalSkillName(Skills.SkillType skill) =>
            PerkLocalization.Localize("$skill_" + skill.ToString().ToLowerInvariant());
    }

    internal static class MasteryWindowService
    {
        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(SkillsDialog), "m_elements");
        private static readonly FieldInfo ListRootField = AccessTools.Field(typeof(SkillsDialog), "m_listRoot");
        private static readonly FieldInfo ScrollField = AccessTools.Field(typeof(SkillsDialog), "skillListScrollRect");
        private static readonly FieldInfo ScrollbarField = AccessTools.Field(typeof(SkillsDialog), "scrollbar");
        private static readonly FieldInfo TotalField = AccessTools.Field(typeof(SkillsDialog), "m_totalSkillText");
        private static readonly FieldInfo TooltipField = AccessTools.Field(typeof(SkillsDialog), "m_tooltipAnchor");
        private static readonly Dictionary<SkillsDialog, MasteryWindowController> Controllers =
            new Dictionary<SkillsDialog, MasteryWindowController>();

        internal static bool Ready => MasteryPlugin.Settings.Enabled.Value && !Application.isBatchMode &&
            SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && MasteryHudArtwork.Ready;

        internal static void Ensure(SkillsDialog dialog, Player player)
        {
            if (!Ready)
            {
                if (dialog != null && Controllers.TryGetValue(dialog, out MasteryWindowController disabledController))
                    disabledController?.RestoreForDisabled();
                return;
            }
            if (dialog == null || player == null || player != Player.m_localPlayer) return;
            PerkUiIconService.Capture(dialog, player);
            if (!Controllers.TryGetValue(dialog, out MasteryWindowController controller) || controller == null)
            {
                controller = dialog.gameObject.AddComponent<MasteryWindowController>();
                Controllers[dialog] = controller;
            }
            controller.Initialize(dialog, player, FindVanillaFont(dialog),
                ListRootField?.GetValue(dialog) as RectTransform,
                ScrollField?.GetValue(dialog) as ScrollRect,
                ScrollbarField?.GetValue(dialog) as Scrollbar,
                TotalField?.GetValue(dialog) as TMP_Text,
                TooltipField?.GetValue(dialog) as RectTransform);
        }

        // Retained for compatibility with the native click patch; custom rows call Select directly.
        internal static void Select(SkillsDialog dialog, GameObject selected)
        {
            if (dialog == null || selected == null || !Controllers.TryGetValue(dialog, out MasteryWindowController controller)) return;
            List<GameObject> rows = ElementsField?.GetValue(dialog) as List<GameObject>;
            List<Skills.Skill> skills = Player.m_localPlayer?.GetSkills().GetSkillList();
            if (rows == null || skills == null) return;
            for (int i = 0; i < Mathf.Min(rows.Count, skills.Count); ++i)
                if (rows[i] != null && (selected == rows[i] || selected.transform.IsChildOf(rows[i].transform)) && skills[i]?.m_info != null)
                {
                    controller.Select(skills[i].m_info.m_skill);
                    return;
                }
        }

        internal static bool AllowNativeUpdate(SkillsDialog dialog)
        {
            if (dialog == null || !Controllers.TryGetValue(dialog, out MasteryWindowController controller) ||
                controller == null) return true;
            if (!Ready)
            {
                controller.RestoreForDisabled();
                return true;
            }
            return !controller.CustomVisible;
        }

        internal static void Forget(SkillsDialog dialog, MasteryWindowController controller)
        {
            if (dialog != null && Controllers.TryGetValue(dialog, out MasteryWindowController current) && current == controller)
                Controllers.Remove(dialog);
        }

        private static TMP_FontAsset FindVanillaFont(SkillsDialog dialog)
        {
            TextMeshProUGUI source = dialog.GetComponentInChildren<TextMeshProUGUI>(true);
            return source != null ? source.font : null;
        }
    }

    [HarmonyPatch(typeof(SkillsDialog), "Setup")]
    internal static class MasteryWindowSetupPatch
    {
        private static void Postfix(SkillsDialog __instance, Player player) => MasteryWindowService.Ensure(__instance, player);
    }

    [HarmonyPatch(typeof(SkillsDialog), "SkillClicked")]
    internal static class MasteryWindowSelectionPatch
    {
        private static void Postfix(SkillsDialog __instance, GameObject selectedObject) => MasteryWindowService.Select(__instance, selectedObject);
    }

    [HarmonyPatch(typeof(SkillsDialog), "Update")]
    internal static class MasteryWindowUpdatePatch
    {
        private static bool Prefix(SkillsDialog __instance) => MasteryWindowService.AllowNativeUpdate(__instance);
    }
}
