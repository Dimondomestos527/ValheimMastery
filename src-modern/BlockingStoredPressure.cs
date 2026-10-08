using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    /// <summary>Level 35 Blocking: convert recent incoming guard pressure into a deliberate parry payoff.</summary>
    internal sealed class BlockingStoredPressureState
    {
        internal float Value;
        internal float ExpiresAt;
        internal int FeedbackTier;
    }

    internal static class BlockingStoredPressureService
    {
        private const float WindowSeconds = 3f;

        internal static void ObserveSuccessfulBlock(Player player, Character attacker, bool perfect)
        {
            if (player == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Blocking, 35)) return;

            BlockingStoredPressureState state = MasteryStateStore.GetPlayerState<BlockingStoredPressureState>(player);
            float threshold = Mathf.Max(1f, player.GetStaggerTreshold());
            float pressure = Mathf.Clamp01(player.m_staggerDamage / threshold);
            state.Value = Time.time <= state.ExpiresAt ? Mathf.Max(state.Value, pressure) : pressure;
            state.ExpiresAt = Time.time + WindowSeconds;
            int tier = state.Value >= 0.75f ? 3 : state.Value >= 0.50f ? 2 : state.Value >= 0.25f ? 1 : 0;
            if (tier > state.FeedbackTier) { state.FeedbackTier = tier; PerkFeedbackService.Play(player, "blocking_35_pressure", player.GetCenterPoint(), false); }
            if (!perfect || attacker == null || attacker.IsDead()) return;

            float stored = Mathf.Clamp01(state.Value);
            state.Value = 0f;
            state.ExpiresAt = 0f;
            state.FeedbackTier = 0;
            float staggerFraction = Mathf.Lerp(MasteryPlugin.Settings.Blocking35MinTargetStagger.Value, MasteryPlugin.Settings.Blocking35MaxTargetStagger.Value, stored);
            HitData staggerHit = new HitData();
            staggerHit.m_skill = Skills.SkillType.Blocking;
            staggerHit.m_point = attacker.GetCenterPoint();
            staggerHit.m_dir = (attacker.GetCenterPoint() - player.GetCenterPoint()).normalized;
            staggerHit.SetAttacker(player);
            attacker.AddStaggerDamage(Mathf.Max(1f, attacker.GetStaggerTreshold() * staggerFraction), -staggerHit.m_dir, staggerHit);

            float adrenaline = Mathf.Lerp(MasteryPlugin.Settings.Blocking35MinExtraAdrenaline.Value, MasteryPlugin.Settings.Blocking35MaxExtraAdrenaline.Value, stored);
            player.AddAdrenaline(adrenaline);
            PerkFeedbackService.Play(player, "blocking_35_pressure", attacker.GetCenterPoint(), true);
        }
    }
}
