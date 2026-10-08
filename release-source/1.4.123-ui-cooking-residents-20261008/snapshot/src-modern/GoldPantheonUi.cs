using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    internal static class GoldUiLocalization
    {
        internal static string Text(string english, string ukrainian)
        {
            bool isUkrainian = Localization.instance != null &&
                string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", System.StringComparison.OrdinalIgnoreCase);
            return isUkrainian ? ukrainian : english;
        }
    }

    // The Pantheon lives inside SkillsDialog, beside the existing Mastery button.
    // No gameplay-HUD widget or global keyboard shortcut is installed here.
    internal static class GoldPantheonUi
    {
        private static readonly List<GoldPantheonUiController> Controllers = new List<GoldPantheonUiController>();
        private static bool _testEnabled;
        private static float _testFavor = 250f;
        private static Player _testPlayer;
        private static ZNet _testSession;

        // Local presentation fixture only: no patron unlock, RPC, receipt or save state.
        internal static bool TestVisible
        {
            get
            {
                if (!MasteryPlugin.Settings.UIDebugLogging.Value || !GoldCraftingService.Enabled ||
                    !GoldCraftingService.Unlocked || Player.m_localPlayer == null || ZNet.instance == null ||
                    _testPlayer != Player.m_localPlayer || _testSession != ZNet.instance)
                    ResetTest();
                return _testEnabled;
            }
        }

        internal static float TestFavor => _testFavor;

        private static void ResetTest()
        {
            _testEnabled = false; _testFavor = 250f; _testPlayer = null; _testSession = null;
        }

        internal static void Debug(Terminal.ConsoleEventArgs args)
        {
            if (!MasteryPlugin.Settings.UIDebugLogging.Value) return;
            if (args.Args.Length != 4 || args.Args[2] != "test")
            { args.Context.AddString("vm pantheon test on|off|<0..1000> (local UI only)"); return; }
            string value = args.Args[3].ToLowerInvariant();
            if (value == "off") ResetTest();
            else
            {
                if (!GoldCraftingService.Enabled || !GoldCraftingService.Unlocked ||
                    Player.m_localPlayer == null || ZNet.instance == null)
                { args.Context.AddString("Open an unlocked Pantheon with Gold enabled first."); return; }
                float favor = 250f;
                if (value != "on" && (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out favor) ||
                    !GoldFavorModel.Finite(favor) || favor < 0f || favor > 1000f))
                { args.Context.AddString("Test Favor must be a finite value from 0 to 1000."); return; }
                _testPlayer = Player.m_localPlayer; _testSession = ZNet.instance;
                _testFavor = favor; _testEnabled = true;
            }
            foreach (GoldPantheonUiController controller in Controllers)
                if (controller != null) controller.Refresh();
            args.Context.AddString(_testEnabled
                ? "Tyr UI test enabled; synthetic Favor=" + _testFavor.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "/1000. Real Favor and saves unchanged."
                : "Pantheon UI test off; original layout restored.");
        }

        internal static void Ensure(SkillsDialog dialog, Player player)
        {
            if (dialog == null || player == null || player != Player.m_localPlayer) return;
            GoldPantheonUiController controller = dialog.GetComponent<GoldPantheonUiController>();
            if (controller == null) controller = dialog.gameObject.AddComponent<GoldPantheonUiController>();
            if (!Controllers.Contains(controller)) Controllers.Add(controller);
            controller.Initialize(dialog, player, FindFont(dialog));
        }

        internal static void Hide(SkillsDialog dialog)
        {
            if (dialog == null) return;
            dialog.GetComponent<GoldPantheonUiController>()?.HidePanel();
        }

        // Called by GoldCraftingService at its bounded 0.2 second UI cadence.
        internal static void Tick()
        {
            GoldInspirationStatus.Tick();
            for (int i = Controllers.Count - 1; i >= 0; i--)
            {
                GoldPantheonUiController controller = Controllers[i];
                if (controller == null) { Controllers.RemoveAt(i); continue; }
                controller.Refresh();
            }
        }

        private static TMP_FontAsset FindFont(SkillsDialog dialog)
        {
            TextMeshProUGUI source = dialog.GetComponentInChildren<TextMeshProUGUI>(true);
            if (source != null && source.font != null) return source.font;
            return MessageHud.instance?.m_messageCenterText?.font;
        }

        internal static void Forget(GoldPantheonUiController controller) => Controllers.Remove(controller);
    }

    internal sealed class GoldPantheonUiController : MonoBehaviour
    {
        private static readonly Color Gold = new Color(.96f, .66f, .20f, 1f);
        private SkillsDialog _dialog;
        private Player _player;
        private GameObject _button, _panel, _testPatron;
        private RectTransform _walletContent;
        private TMP_FontAsset _font;
        private ZNet _uiSession;
        private GoldPantheonNativeStyle _nativeStyle;
        private readonly Dictionary<string, PatronCard> _walletCards = new Dictionary<string, PatronCard>();
        private sealed class PatronCard
        {
            internal GameObject Root;
            internal Image Fill;
            internal TextMeshProUGUI Amount;
            internal float Display, LastRefresh;
        }
        private Image _testFill, _buttonImage, _buttonIcon;
        private TextMeshProUGUI _testFavorText, _buttonText;
        private Outline _buttonOutline, _panelOutline;
        private float _lastFavor = -1f, _favorPulseUntil;
        private bool _built;
        internal void Initialize(SkillsDialog dialog, Player player, TMP_FontAsset font)
        {
            if (_player != player || _uiSession != ZNet.instance) ResetPresentation();
            _dialog = dialog; _player = player;
            _uiSession = ZNet.instance;
            if (!_built) { _nativeStyle = new GoldPantheonNativeStyle(dialog); Build(font); }
            Refresh();
        }

        internal void Refresh()
        {
            if (_dialog == null || _player == null || !_built) return;
            bool testVisible = GoldPantheonUi.TestVisible &&
                !(GoldCraftingService.PatronWallets.TryGetValue("Tyr", out var tyr) && tyr.Unlocked);
            _testPatron.SetActive(testVisible);
            RefreshWalletCards(testVisible);
            bool unlocked = GoldCraftingService.AnyUnlocked && _player == Player.m_localPlayer;
            _button.SetActive(unlocked);
            if (!unlocked)
            {
                _panel.SetActive(false);
                return;
            }

            if (_panel.activeSelf && IsMasteryPanelOpen()) _panel.SetActive(false);
            if (_panel.activeSelf) RefreshPanel();
            if (_buttonOutline != null)
            {
                float pulse = Time.unscaledTime < _favorPulseUntil
                    ? .5f + .5f * Mathf.Sin(Time.unscaledTime * 13f) : 0f;
                float full = GoldCraftingService.Favor >= GoldCraftingService.MaxFavor ?
                    .12f + .08f * Mathf.Sin(Time.unscaledTime * 2f) : 0f;
                _buttonOutline.effectColor = Color.Lerp(new Color(.50f, .31f, .10f, 1f), Gold,
                    Mathf.Max(pulse * .35f, full));
            }
            if (_buttonIcon != null && _buttonIcon.sprite == null)
                _buttonIcon.sprite = _player.m_textIcon;
            float favor = Mathf.Clamp(GoldCraftingService.Favor, 0f, GoldCraftingService.MaxFavor);
            if (_lastFavor >= 0f && favor > _lastFavor + .01f) _favorPulseUntil = Time.unscaledTime + .7f;
            _lastFavor = favor;
        }

        private void Build(TMP_FontAsset font)
        {
            _button = new GameObject("ValheimMastery_PantheonButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            _button.transform.SetParent(_dialog.transform, false);
            RectTransform buttonRect = _button.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.anchoredPosition = new Vector2(-16f, 18f); // Clear of the centered Skills/Mastery tabs.
            buttonRect.sizeDelta = new Vector2(120f, 42f);
            _buttonImage = _button.GetComponent<Image>();
            _buttonImage.color = new Color(.24f, .15f, .055f, .98f);
            _buttonImage.raycastTarget = true;
            _buttonOutline = _button.GetComponent<Outline>();
            _buttonOutline.effectColor = new Color(.50f, .31f, .10f, 1f);
            _buttonOutline.effectDistance = new Vector2(1.5f, -1.5f);
            Button button = _button.GetComponent<Button>();
            button.targetGraphic = _buttonImage;
            button.onClick.AddListener(TogglePanel);
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .87f, .64f);
            colors.pressedColor = new Color(.72f, .56f, .36f); colors.selectedColor = colors.normalColor;
            button.colors = colors;
            _buttonIcon = AddImage(_button.transform, "NativePantheonIcon", null, new Vector2(28f, 28f), new Vector2(-43f, 0f));
            _buttonIcon.color = new Color(1f, .88f, .68f, 1f);
            _buttonText = AddText(_button.transform, GoldUiLocalization.Text("PANTHEON", "ПАНТЕОН"), font, 15f, TextAlignmentOptions.MidlineLeft,
                new Vector2(36f, 0f), new Vector2(-3f, 0f), Vector2.zero, Vector2.one);
            _button.SetActive(GoldCraftingService.AnyUnlocked);

            _panel = new GameObject("ValheimMastery_PantheonPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panel.transform.SetParent(_dialog.transform, false);
            RectTransform panelRect = _panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, .5f);
            panelRect.pivot = new Vector2(0f, .5f);
            panelRect.anchoredPosition = new Vector2(330f, 0f);
            panelRect.sizeDelta = new Vector2(470f, 480f);
            Image panelImage = _panel.GetComponent<Image>(); panelImage.color = new Color(.055f, .038f, .022f, .97f);
            _panelOutline = _panel.GetComponent<Outline>(); _panelOutline.effectColor = new Color(.52f, .33f, .11f, 1f);
            _panelOutline.effectDistance = new Vector2(2f, -2f);
            _nativeStyle.Frame(panelImage);
            panelImage.raycastTarget = true;

            TextMeshProUGUI heading = AddText(_panel.transform, GoldUiLocalization.Text("FAVOR OF THE GODS", "ПРИХИЛЬНІСТЬ БОГІВ"), font, 23f,
                TextAlignmentOptions.Center, new Vector2(18f, -17f), new Vector2(-54f, -57f),
                new Vector2(0f, 1f), new Vector2(1f, 1f));
            heading.color = Gold;
            GameObject close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_panel.transform, false);
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f); closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-10f, -10f); closeRect.sizeDelta = new Vector2(32f, 30f);
            close.GetComponent<Image>().color = new Color(.30f, .13f, .055f, .98f);
            close.GetComponent<Button>().onClick.AddListener(() => _panel.SetActive(false));
            AddText(close.transform, "×", font, 22f, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one).raycastTarget = false;

            _font = font;
            GameObject viewport = new GameObject("PatronViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(_panel.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(12f, 14f); viewportRect.offsetMax = new Vector2(-12f, -62f);
            viewport.GetComponent<Image>().color = Color.clear;
            GameObject content = new GameObject("PatronWallets", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _walletContent = content.GetComponent<RectTransform>();
            _walletContent.anchorMin = new Vector2(0f, 1f); _walletContent.anchorMax = Vector2.one;
            _walletContent.pivot = new Vector2(.5f, 1f); _walletContent.sizeDelta = new Vector2(0f, 410f);
            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect; scroll.content = _walletContent; scroll.horizontal = false;
            scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            BuildTestPatron(font);
            _panel.SetActive(false);
            _built = true;
        }

        private void BuildTestPatron(TMP_FontAsset font)
        {
            _testPatron = new GameObject("TyrUiTestPatron", typeof(RectTransform), typeof(Image));
            _testPatron.transform.SetParent(_walletContent, false);
            RectTransform card = _testPatron.GetComponent<RectTransform>();
            card.anchorMin = card.anchorMax = new Vector2(.5f, 1f); card.pivot = new Vector2(.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -470f); card.sizeDelta = new Vector2(390f, 132f);
            Image background = _testPatron.GetComponent<Image>();
            background.color = new Color(.105f, .073f, .038f, .95f); background.raycastTarget = false;
            AddText(_testPatron.transform, GoldUiLocalization.Text("TÝR · UI TEST", "ТЮР · ТЕСТ ІНТЕРФЕЙСУ"),
                font, 18f, TextAlignmentOptions.Center, new Vector2(8f, -36f), new Vector2(-8f, -8f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)).color = Gold;
            GameObject bar = new GameObject("TestFavorBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(_testPatron.transform, false);
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(.5f, 1f); barRect.pivot = new Vector2(.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -44f); barRect.sizeDelta = new Vector2(350f, 22f);
            bar.GetComponent<Image>().color = new Color(.14f, .12f, .09f, 1f);
            bar.GetComponent<Image>().raycastTarget = false;
            GameObject fill = new GameObject("TestFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bar.transform, false);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f); fillRect.offsetMax = new Vector2(-2f, -2f);
            _testFill = fill.GetComponent<Image>(); _testFill.type = Image.Type.Simple;
            _testFill.color = new Color(1f, .47f, .10f, .95f); _testFill.raycastTarget = false;
            _testFavorText = AddText(_testPatron.transform, "250 / 1000", font, 17f, TextAlignmentOptions.Center,
                new Vector2(8f, -94f), new Vector2(-8f, -68f), new Vector2(0f, 1f), new Vector2(1f, 1f));
            AddText(_testPatron.transform, GoldUiLocalization.Text("LOCAL UI ONLY · NO REWARDS OR SAVES",
                "ЛИШЕ ТЕСТ UI · БЕЗ НАГОРОД І ЗБЕРЕЖЕНЬ"), font, 12f, TextAlignmentOptions.Center,
                new Vector2(8f, -124f), new Vector2(-8f, -96f), new Vector2(0f, 1f), new Vector2(1f, 1f));
            _nativeStyle.Frame(background);
            _nativeStyle.Bar(bar.GetComponent<Image>(), _testFill);
            _testPatron.SetActive(false);
        }

        private void TogglePanel()
        {
            if (!GoldCraftingService.AnyUnlocked) return;
            bool show = !_panel.activeSelf;
            if (show) HideMasteryPanel();
            _panel.SetActive(show);
            if (show) RefreshPanel();
        }

        internal void HidePanel()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private bool IsMasteryPanelOpen()
        {
            MasteryWindowController existing = _dialog != null ? _dialog.GetComponent<MasteryWindowController>() : null;
            return existing != null && existing.MasteryVisible;
        }

        private void HideMasteryPanel()
        {
            MasteryWindowController existing = _dialog != null ? _dialog.GetComponent<MasteryWindowController>() : null;
            existing?.ShowSkills();
        }

        private void RefreshPanel()
        {
            if (_panel == null || !_panel.activeSelf) return;
            RefreshWalletCards(_testPatron.activeSelf);
            if (_testPatron.activeSelf)
            {
                float amount = Mathf.Clamp01(GoldPantheonUi.TestFavor / GoldCraftingService.MaxFavor);
                SetFill(_testFill, amount);
                _testFavorText.text = Mathf.RoundToInt(GoldPantheonUi.TestFavor).ToString("N0") + " / 1000";
            }
        }
        private void RefreshWalletCards(bool testVisible)
        {
            float height = 0f;
            foreach (var card in _walletCards.Values) card.Root.SetActive(false);
            var keys = new List<string>(GoldCraftingService.PatronWallets.Keys);
            keys.Sort((a, b) => a == b ? 0 : a == GoldCooldownPolicy.LegacyPatronId ? -1 :
                b == GoldCooldownPolicy.LegacyPatronId ? 1 : System.StringComparer.Ordinal.Compare(a, b));
            foreach (string patron in keys)
            {
                var wallet = GoldCraftingService.PatronWallets[patron];
                if (!wallet.Unlocked) continue;
                if (!_walletCards.TryGetValue(patron, out var card))
                { card = BuildWalletCard(patron); _walletCards.Add(patron, card); }
                card.Root.SetActive(true);
                card.Root.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -height);
                float now = Time.unscaledTime;
                float elapsed = card.LastRefresh > 0f ? Mathf.Clamp(now - card.LastRefresh, 0f, .5f) : .2f;
                card.LastRefresh = now;
                card.Display = Mathf.Lerp(card.Display, wallet.Favor, 1f - Mathf.Exp(-5f * elapsed));
                SetFill(card.Fill, Mathf.Clamp01(card.Display / GoldCraftingService.MaxFavor));
                card.Amount.text = wallet.Favor.ToString("0.#") + " / 1000" +
                    (wallet.Held > 0 ? GoldUiLocalization.Text(" · AVAILABLE ", " · ДОСТУПНО ") +
                        Mathf.Max(0f, wallet.Favor - wallet.Held).ToString("0.#") : "");
                height += 210f;
            }
            _testPatron.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -height);
            if (testVisible) height += 132f;
            _walletContent.sizeDelta = new Vector2(0f, height);
        }

        private static void SetFill(Image fill, float amount)
        {
            if (fill.sprite != null) fill.fillAmount = amount;
            else
            {
                fill.rectTransform.anchorMax = new Vector2(amount, 1f);
                // Keep tiny fallback fills inside the two-pixel track inset without negative widths.
                fill.rectTransform.offsetMax = new Vector2(2f - 4f * amount, -2f);
            }
            fill.gameObject.SetActive(amount > .001f);
        }

        private static string EarningMethods(string patron)
        {
            if (patron == GoldCooldownPolicy.LegacyPatronId)
                return GoldUiLocalization.Text(
                    "GAIN FAVOR\n• Craft and improve items\n• Build structures\n• Complete processing of your materials in a workshop",
                    "ОТРИМАННЯ ПРИХИЛЬНОСТІ\n• Створюйте й поліпшуйте предмети\n• Будуйте споруди\n• Завершуйте переробку вашої сировини в майстерні");
            return GoldUiLocalization.Text("Favor earning for this patron is not implemented yet.",
                "Отримання прихильності цього покровителя ще не реалізовано.");
        }

        private PatronCard BuildWalletCard(string patron)
        {
            GameObject root = new GameObject(patron + "WalletCard", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(_walletContent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f); rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(390f, 198f);
            Image background = root.GetComponent<Image>();
            background.color = new Color(.105f, .073f, .038f, .95f);
            _nativeStyle.Frame(background);
            TextMeshProUGUI title = AddText(root.transform, patron == GoldCooldownPolicy.LegacyPatronId ? "VÖLUNDR" : patron,
                _font, 21f, TextAlignmentOptions.Center, new Vector2(14f, -40f), new Vector2(-14f, -10f),
                new Vector2(0f, 1f), Vector2.one);
            title.richText = false; title.color = Gold;
            GameObject bar = new GameObject("FavorBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(root.transform, false);
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(.5f, 1f); barRect.pivot = new Vector2(.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -46f); barRect.sizeDelta = new Vector2(350f, 24f);
            Image frame = bar.GetComponent<Image>(); frame.color = new Color(.14f, .12f, .09f, 1f);
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bar.transform, false);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f); fillRect.offsetMax = new Vector2(-2f, -2f);
            Image image = fill.GetComponent<Image>(); image.color = Gold;
            _nativeStyle.Bar(frame, image);
            TextMeshProUGUI amount = AddText(bar.transform, "", _font, 15f, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            amount.color = new Color(1f, .95f, .82f);
            var shadow = amount.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0f, 0f, 0f, .9f);
            shadow.effectDistance = new Vector2(1f, -1f);
            TextMeshProUGUI sources = AddText(root.transform, EarningMethods(patron), _font, 14f,
                TextAlignmentOptions.TopLeft, new Vector2(20f, -184f), new Vector2(-20f, -84f),
                new Vector2(0f, 1f), Vector2.one);
            sources.color = new Color(.87f, .82f, .70f);
            return new PatronCard { Root = root, Fill = image, Amount = amount };
        }
        private void ResetPresentation()
        {
            _lastFavor = -1f; _favorPulseUntil = 0;
            if (_panel != null) _panel.SetActive(false);
            foreach (var card in _walletCards.Values)
                if (card.Root != null) { card.Root.SetActive(false); card.Display = card.LastRefresh = 0; }
            _walletContent?.GetComponentInParent<ScrollRect>()?.StopMovement();
            if (_walletContent != null) _walletContent.anchoredPosition = Vector2.zero;
        }

        private static Image AddImage(Transform parent, string name, Sprite sprite, Vector2 size, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = position;
            Image image = go.GetComponent<Image>(); image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI AddText(Transform parent, string value, TMP_FontAsset font, float size,
            TextAlignmentOptions alignment, Vector2 offsetMin, Vector2 offsetMax, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = new Vector2(.5f, .5f);
            // Fixed top anchors use negative offsets: bottom must be the more
            // negative Y. These text rows previously had negative heights.
            if (Mathf.Approximately(anchorMin.y, anchorMax.y) && offsetMin.y > offsetMax.y)
            { float bottom = offsetMax.y; offsetMax.y = offsetMin.y; offsetMin.y = bottom; }
            rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.alignment = alignment; text.text = value;
            text.enableAutoSizing = true; text.fontSizeMin = Mathf.Max(10f, size - 4f); text.fontSizeMax = size;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.Normal; text.raycastTarget = false;
            return text;
        }

        private void Update()
        {
            if (_uiSession != ZNet.instance || _player != Player.m_localPlayer || !GoldCraftingService.AnyUnlocked)
            {
                ResetPresentation(); if (_button != null) _button.SetActive(false); _uiSession = ZNet.instance;
            }
            // GoldCraftingService stops ticking while disabled or without a player.
            // The open dialog must still remove an invalidated local fixture.
            if (_testPatron != null && _testPatron.activeSelf && !GoldPantheonUi.TestVisible)
            {
                _testPatron.SetActive(false);
                if (_panel != null) _panel.GetComponent<RectTransform>().sizeDelta = new Vector2(470f, 480f);
            }
        }

        private void OnDisable()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private void OnDestroy() => GoldPantheonUi.Forget(this);
    }

    [HarmonyPatch(typeof(SkillsDialog), "Setup")]
    internal static class GoldPantheonUiSetupPatch
    { private static void Postfix(SkillsDialog __instance, Player player) => GoldPantheonUi.Ensure(__instance, player); }
}
