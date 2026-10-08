using System;

namespace ValheimMastery
{
    internal static class CookingMeadTiming
    {
        // Production metadata is authoritative here. Eater/current online cook skills
        // cannot qualify an unmarked drink for the new authored-mead timing bonus.
        internal static bool HasAuthoredMastery(ItemDrop.ItemData item) => item != null && item.m_crafterID != 0L &&
            item.m_customData != null && item.m_customData.TryGetValue(Cooking35Service.CookMasteryKey, out string text) &&
            int.TryParse(text, out int tier) && (tier == 35 || tier == 70);

        internal static bool IsRecoveryCooldown(StatusEffect effect)
        {
            if (!(effect is SE_Stats stats) || !effect.m_cooldownIcon || effect.m_ttl <= 0f) return false;
            string category = effect.m_category;
            if (category != "healthpotion" && category != "staminapotion" && category != "eitrpotion") return false;
            // A full-lifetime restoration/unknown-duration channel is not a short
            // recovery+reuse lockout, even when another channel heals upfront.
            if (LongRecovery(stats.m_healthOverTime, stats.m_healthOverTimeDuration, effect.m_ttl) ||
                LongRecovery(stats.m_staminaOverTime, stats.m_staminaOverTimeDuration, effect.m_ttl) ||
                LongRecovery(stats.m_eitrOverTime, stats.m_eitrOverTimeDuration, effect.m_ttl)) return false;
            return stats.m_healthUpFront > 0f || stats.m_staminaUpFront > 0f || stats.m_eitrUpFront > 0f ||
                ShortRecovery(stats.m_healthOverTime, stats.m_healthOverTimeDuration, effect.m_ttl) ||
                ShortRecovery(stats.m_staminaOverTime, stats.m_staminaOverTimeDuration, effect.m_ttl) ||
                ShortRecovery(stats.m_eitrOverTime, stats.m_eitrOverTimeDuration, effect.m_ttl);
        }

        private static bool ShortRecovery(float amount, float duration, float ttl) => amount > 0f && duration > 0f && duration < ttl;
        private static bool LongRecovery(float amount, float duration, float ttl) => amount > 0f && (duration <= 0f || duration >= ttl);

        internal static float Lifetime(StatusEffect template, float baseTtl, bool mastery, bool swamp, bool mountains)
        {
            if (!IsRecoveryCooldown(template))
                return baseTtl * (1f + (mastery ? .4f : 0f) + (swamp ? .3f : 0f));
            // Swamp increases sustained beneficial duration, never the reuse lockout.
            float ttl = baseTtl * (mastery ? .6f : 1f) * (mountains ? .5f : 1f);
            var stats = (SE_Stats)template;
            float delivery = Math.Max(stats.m_healthOverTime > 0f ? stats.m_healthOverTimeDuration : 0f,
                Math.Max(stats.m_staminaOverTime > 0f ? stats.m_staminaOverTimeDuration : 0f,
                    stats.m_eitrOverTime > 0f ? stats.m_eitrOverTimeDuration : 0f));
            // All inspected vanilla channels finish by10s, versus36s minimum reuse.
            // Unsupported long custom delivery fails closed instead of truncating it.
            return delivery < ttl ? ttl : baseTtl;
        }
    }
}
