#if MASTERY_CLUBS70_EXPERIMENT
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    // One local panel, reused across charges. No toast-per-tick or world canvas.
    internal sealed class Clubs70ChargeHud : MonoBehaviour
    {
        private static Clubs70ChargeHud Instance;
        private Image Fill;
        private TextMeshProUGUI Label;
        private CanvasGroup Group;
        private float Seen;
        internal static void Show(Player player, float reserve, float capacity, bool charging, float expiry)
        {
            if (player != Player.m_localPlayer || Hud.instance == null) return;
            Clubs70ChargePresentation.Tick(player);
            if (reserve <= 0f) { if (Instance != null) Instance.Group.alpha = 0f; return; }
            if (Instance == null)
            {
                var root = new GameObject("VM_ClubsChargeHud", typeof(RectTransform));
                root.transform.SetParent(Hud.instance.transform, false);
                Instance = root.AddComponent<Clubs70ChargeHud>(); Instance.Build();
            }
            Instance.Seen = Time.unscaledTime;
            float fraction = Mathf.Clamp01(reserve / Mathf.Max(1f, capacity));
            var rect = Instance.Fill.rectTransform; rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(fraction, 1f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            bool ua = Localization.instance?.GetSelectedLanguage() == "Ukrainian";
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, expiry - Time.time));
            Instance.Label.text = charging
                ? (ua ? "Заряд · " : "Charge · ") + Mathf.RoundToInt(fraction * 100f) + "%"
                : (ua ? "Готово · " : "Ready · ") + seconds + (ua ? " с" : " s");
            Instance.Fill.color = charging ? new Color(.78f, .57f, .30f, .9f) : new Color(1f, .76f, .32f, 1f);
            Instance.Label.color = charging ? new Color(.85f, .78f, .60f) : new Color(1f, .83f, .47f);
            float fade = !charging ? Mathf.Clamp01((expiry - Time.time) / 3f) : 1f;
            Instance.Group.alpha = .45f + .55f * fade;
        }
        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2100;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            Group = gameObject.AddComponent<CanvasGroup>(); Group.blocksRaycasts = false; Group.interactable = false;
            var bar = new GameObject("Reserve", typeof(RectTransform), typeof(Image)); bar.transform.SetParent(transform, false);
            var rect = bar.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0f, -220f); rect.sizeDelta = new Vector2(200f, 12f);
            Image background = bar.GetComponent<Image>();
            background.color = new Color(.10f, .08f, .05f, .95f);
            // Borrow Valheim's current HUD resources, never edit the native HUD.
            Image nativeFrame = Hud.instance.m_staminaBar2Root != null
                ? Hud.instance.m_staminaBar2Root.GetComponent<Image>() : null;
            if (nativeFrame?.sprite == null && Hud.instance.m_staminaBar2Root != null)
                foreach (Image image in Hud.instance.m_staminaBar2Root.GetComponentsInChildren<Image>(true))
                    if (image.sprite != null && image.name.IndexOf("border", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { nativeFrame = image; break; }
            if (nativeFrame?.sprite != null)
            { background.sprite = nativeFrame.sprite; background.type = nativeFrame.type; background.material = nativeFrame.material; }
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(bar.transform, false);
            Fill = fill.GetComponent<Image>(); Fill.color = new Color(.78f, .57f, .30f, .9f);
            Image nativeFill = Hud.instance.m_staminaBar2Fast != null
                ? Hud.instance.m_staminaBar2Fast.GetComponentInChildren<Image>(true) : null;
            if (nativeFill?.sprite != null)
            { Fill.sprite = nativeFill.sprite; Fill.type = Image.Type.Sliced; Fill.material = nativeFill.material; }
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(bar.transform, false);
            var textRect = label.GetComponent<RectTransform>(); textRect.anchoredPosition = new Vector2(0f, 16f); textRect.sizeDelta = new Vector2(240f, 26f);
            Label = label.GetComponent<TextMeshProUGUI>(); Label.fontSize = 17f; Label.alignment = TextAlignmentOptions.Center;
            Label.color = new Color(.85f, .78f, .60f);
            if (MessageHud.instance?.m_messageCenterText?.font != null) Label.font = MessageHud.instance.m_messageCenterText.font;
            foreach (var graphic in GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        }
        private void Update()
        {
            if (!MagicSkillPassives.OwnerReady(Player.m_localPlayer) || Time.unscaledTime - Seen > .25f) Group.alpha = 0f;
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
#endif
