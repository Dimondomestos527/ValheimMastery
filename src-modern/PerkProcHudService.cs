using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    // One small reusable, non-interactive HUD. No world canvases or toast-per-tick allocations.
    internal sealed class PerkProcHudService : MonoBehaviour
    {
        private static PerkProcHudService _instance;
        private CanvasGroup _group;
        private RectTransform _panel;
        private Image _icon, _frame;
        private TextMeshProUGUI _label;
        private float _shownAt = -10f;
        private string _last;
        internal static void Show(Player player, string id, Skills.SkillType skill, int level)
        {
            if (player != Player.m_localPlayer || player == null || Hud.instance == null || !MasteryPlugin.Settings.EnablePerkProcMessages.Value) return;
            if (_instance == null)
            {
                var root = new GameObject("Mastery_CompactProcHud", typeof(RectTransform));
                root.transform.SetParent(Hud.instance.transform, false);
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2200;
                var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
                _instance = root.AddComponent<PerkProcHudService>(); _instance.Build();
            }
            var ui = _instance;
            if (ui._last == id && Time.unscaledTime - ui._shownAt < 2f) return;
            ui._last = id; ui._shownAt = Time.unscaledTime;
            ui._panel.anchoredPosition = id == "sneak_70" ? new Vector2(0, 230) : new Vector2(0, -95);
            var accent = level >= 70 ? new Color(.8f, .87f, .93f) : new Color(.78f, .46f, .24f);
            ui._icon.sprite = player.GetSkills().GetSkillDef(skill)?.m_icon;
            ui._icon.enabled = ui._icon.sprite != null;
            ui._frame.color = accent;
            ui._label.text = PerkLocalization.Localize("$vm_perk_" + id + "_name");
            ui._label.color = accent;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.sizeDelta = size; rt.anchoredPosition = pos;
            return rt;
        }
        private void Build()
        {
            _group = gameObject.AddComponent<CanvasGroup>(); _group.blocksRaycasts = false; _group.interactable = false;
            _panel = Rect("Proc", transform, new Vector2(300, 44), new Vector2(0, -95));
            _frame = Rect("BronzeSilver", _panel, new Vector2(38, 38), new Vector2(-127, 0)).gameObject.AddComponent<Image>();
            var back = Rect("IconBacking", _frame.transform, new Vector2(34, 34), Vector2.zero).gameObject.AddComponent<Image>(); back.color = new Color(.07f, .06f, .04f, .85f);
            _icon = Rect("SkillIcon", back.transform, new Vector2(30, 30), Vector2.zero).gameObject.AddComponent<Image>(); _icon.preserveAspect = true;
            _label = Rect("Name", _panel, new Vector2(260, 46), new Vector2(30, 0)).gameObject.AddComponent<TextMeshProUGUI>();
            _label.fontSize = 21; _label.alignment = TextAlignmentOptions.MidlineLeft;
            if (MessageHud.instance?.m_messageCenterText?.font != null) _label.font = MessageHud.instance.m_messageCenterText.font;
            foreach (var graphic in GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        }
        private void Update()
        {
            float age = Time.unscaledTime - _shownAt;
            _group.alpha = Player.m_localPlayer == null || Player.m_localPlayer.IsDead() ? 0f : Mathf.Clamp01(age / .12f) * Mathf.Clamp01((1.6f - age) / .45f);
            _panel.localScale = Vector3.one * Mathf.Lerp(.88f, 1f, Mathf.Clamp01(age / .15f)) * (_last == "sneak_70" ? 1.4f : 1f);
        }
        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
