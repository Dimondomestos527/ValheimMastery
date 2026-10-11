using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    internal static class EnemyStaggerHud
    {
        private static readonly int Key = "vm_enemy_stagger_fraction".GetStableHashCode();
        private sealed class Sync { internal float Next, Last = -1f; }
        private static readonly ConditionalWeakTable<Character, Sync> Clocks = new ConditionalWeakTable<Character, Sync>();
        internal static void Publish(Character target)
        {
            if (target.IsPlayer() || target.m_nview?.IsOwner() != true || target.m_staggerDamageFactor <= 0f) return;
            float value = Mathf.Round(Mathf.Clamp01(target.GetStaggerPercentage()) * 100f) / 100f;
            if (value <= 0f && !Clocks.TryGetValue(target, out _)) return;
            var clock = Clocks.GetOrCreateValue(target);
            if (Time.time < clock.Next || value == clock.Last) return;
            clock.Next = Time.time + .2f; clock.Last = value;
            target.m_nview.GetZDO()?.Set(Key, value);
        }
        internal static void Draw(EnemyHud hud)
        {
            foreach (var pair in hud.m_huds)
            {
                var target = pair.Key; var data = pair.Value;
                if (target == null || target.IsPlayer() || data?.m_gui == null || data.m_healthFast == null || target.m_staggerDamageFactor <= 0f) continue;
                var bar = data.m_gui.GetComponent<EnemyStaggerBar>();
                if (bar == null)
                { bar = data.m_gui.AddComponent<EnemyStaggerBar>(); bar.Build(data.m_healthFast.GetComponent<RectTransform>()); }
                float value = target.m_nview?.IsOwner() == true ? target.GetStaggerPercentage() : target.m_nview?.GetZDO()?.GetFloat(Key, 0f) ?? 0f;
                bar.Set(Mathf.Clamp01(value), MasteryPlugin.Settings.Enabled.Value);
            }
        }
    }
    internal sealed class EnemyStaggerBar : MonoBehaviour
    {
        private RectTransform Root, Fill;
        internal void Build(RectTransform health)
        {
            if (health == null) return;
            var go = new GameObject("Mastery_Stagger", typeof(RectTransform), typeof(Image));
            Root = (RectTransform)go.transform; Root.SetParent(health.parent, false);
            Root.anchorMin = health.anchorMin; Root.anchorMax = health.anchorMax; Root.pivot = health.pivot;
            // Match the HP bar's horizontal geometry, including its offsets. Copying only
            // sizeDelta makes stretched bars span the whole parent instead of the HP bar.
            Root.offsetMin = new Vector2(health.offsetMin.x, 0f);
            Root.offsetMax = new Vector2(health.offsetMax.x, 3f);
            Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 3f);
            Root.anchoredPosition = new Vector2(health.anchoredPosition.x,
                health.anchoredPosition.y - (health.rect.height * .5f + 7f));
            var background = go.GetComponent<Image>(); background.color = new Color(.08f, .065f, .015f, .85f); background.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            Fill = (RectTransform)fill.transform; Fill.SetParent(Root, false);
            Fill.anchorMin = Vector2.zero; Fill.anchorMax = Vector2.one; Fill.offsetMin = Fill.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>(); image.color = new Color(1f, .76f, .12f, .95f); image.raycastTarget = false;
            MasteryHudArtwork.Fill(image);
        }
        internal void Set(float fraction, bool enabled)
        {
            if (Root == null) return;
            Root.gameObject.SetActive(enabled);
            Fill.anchorMax = new Vector2(fraction, 1f);
        }
        private void OnDestroy() { if (Root != null) Destroy(Root.gameObject); }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.UpdateStagger))]
    internal static class EnemyStaggerSyncPatch
    { private static void Postfix(Character __instance) => EnemyStaggerHud.Publish(__instance); }
    [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
    internal static class EnemyStaggerDrawPatch
    { private static void Postfix(EnemyHud __instance) => EnemyStaggerHud.Draw(__instance); }
}
