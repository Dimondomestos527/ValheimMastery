using UnityEngine;

namespace ValheimMastery
{
    // Marker-only native status slot. SE_Stats has no modifier fields set here.
    internal static class GoldInspirationStatus
    {
        private const string MarkerName = "ValheimMastery Divine Inspiration";

        internal static void Tick()
        {
            Player player = Player.m_localPlayer;
            bool exhausted = GoldCraftingService.ExhaustionRemaining > .5d;
            if (player == null || player.m_seman == null || !GoldCraftingService.CoreEnabled || !GoldCraftingService.AnyUnlocked ||
                (!GoldCraftingService.Armed && !exhausted))
            {
                Hide(player);
                return;
            }

            StatusEffect active = player.m_seman.GetStatusEffect(MarkerName.GetStableHashCode());
            double remaining = exhausted ? GoldCraftingService.ExhaustionRemaining : 0d;
            string timer = exhausted ? FormatTime(remaining) : string.Empty;
            string english = exhausted ? "Divine Inspiration resting · " + timer + " remaining" :
                "Divine Inspiration armed · requires a hammer; unequipping cancels";
            string ukrainian = exhausted ? "Божественне натхнення відновлюється · залишилось " + timer :
                "Божественне натхнення підготовлено · потрібен молот; зняття скасує";
            string tooltip = GoldUiLocalization.Text(english, ukrainian);
            if (active != null)
            {
                active.m_ttl = exhausted ? active.m_time + Mathf.Max(.2f, (float)remaining) : 0f;
                active.m_tooltip = tooltip;
                return;
            }

            SE_Stats marker = ScriptableObject.CreateInstance<SE_Stats>();
            marker.name = MarkerName;
            marker.m_name = GoldUiLocalization.Text("Divine Inspiration", "Божественне натхнення");
            marker.m_tooltip = tooltip;
            marker.m_icon = player.GetSkills()?.GetSkillDef(Skills.SkillType.Crafting)?.m_icon ?? player.m_textIcon;
            marker.m_ttl = exhausted ? Mathf.Max(.2f, (float)remaining) : 0f;
            marker.m_flashIcon = false;
            player.m_seman.AddStatusEffect(marker, false, 0, 0f, 0);
        }

        private static void Hide(Player player)
        {
            if (player == null || player != Player.m_localPlayer || player.m_seman == null) return;
            player.m_seman.RemoveStatusEffect(MarkerName.GetStableHashCode(), true);
        }

        private static string FormatTime(double seconds)
        {
            int total = Mathf.CeilToInt((float)System.Math.Max(0d, seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }
    }
}
