using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Native Image presentation only; no copied row/dialog controllers.
    internal sealed class GoldPantheonNativeStyle
    {
        private readonly Image _frame, _barFrame, _fill;
        internal GoldPantheonNativeStyle(SkillsDialog dialog)
        {
            float area = 0;
            foreach (var image in dialog.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || image.sprite.border.sqrMagnitude <= 0f || Owned(image.transform)) continue;
                float candidate = Mathf.Abs(image.rectTransform.rect.width * image.rectTransform.rect.height);
                if (candidate > area) { area = candidate; _frame = image; }
            }
            GuiBar bar = dialog.m_elementPrefab?.GetComponentInChildren<GuiBar>(true);
            if (bar == null) return;
            var rect = AccessTools.Field(typeof(GuiBar), "m_bar")?.GetValue(bar) as RectTransform;
            _fill = rect?.GetComponent<Image>();
            foreach (var image in bar.GetComponentsInChildren<Image>(true))
                if (image != _fill && image.sprite != null) { _barFrame = image; break; }
        }
        private static bool Owned(Transform node)
        {
            for (Transform parent = node; parent != null; parent = parent.parent)
                if (parent.name.StartsWith("ValheimMastery", System.StringComparison.Ordinal)) return true;
            return false;
        }
        private static void Copy(Image target, Image source, Color tint)
        {
            if (target == null) return;
            target.raycastTarget = false;
            if (source == null || source.sprite == null) return;
            if (source != null && source.sprite != null)
            {
                target.sprite = source.sprite; target.material = source.material;
                target.type = source.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
                target.fillCenter = true;
                var outline = target.GetComponent<Outline>();
                if (outline != null) outline.enabled = false;
            }
            target.color = tint; target.preserveAspect = false; target.raycastTarget = false;
        }
        internal void Frame(Image target) => Copy(target, _frame, new Color(1f, 1f, 1f, .96f));
        internal void Bar(Image frame, Image fill)
        {
            Copy(frame, _barFrame ?? _frame, new Color(.55f, .50f, .40f, 1f));
            Copy(fill, _fill, new Color(.94f, .65f, .22f, 1f));
            if (fill.sprite != null)
            { fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0; }
        }
    }
}