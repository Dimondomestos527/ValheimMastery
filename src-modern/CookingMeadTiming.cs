using System;

namespace ValheimMastery
{
    internal static class CookingMeadTiming
    {
        internal sealed class Plan
        {
            internal StatusEffect Template;
            internal float Ttl;
            internal bool Mountains;
            internal void Apply(StatusEffect active)
            {
                if (active == null || ReferenceEquals(active, Template) || active.NameHash() != Template.NameHash()) return;
                active.m_ttl = Ttl;
                FeastMeadRetaliation.Register(active, Template, Mountains);
            }
        }
        internal static Plan Prepare(ItemDrop.ItemData item, FeastTheme theme)
        {
            var template = item?.m_shared?.m_consumeStatusEffect;
            if (item?.m_shared?.m_isDrink != true || template == null || template.m_ttl <= 0f ||
                float.IsNaN(template.m_ttl) || float.IsInfinity(template.m_ttl)) return null;
            return new Plan { Template = template,
                Ttl = Lifetime(template, template.m_ttl, HasAuthoredMastery(item), theme == FeastTheme.Swamp,
                    theme == FeastTheme.Mountains && IsRecoveryCooldown(template)),
                Mountains = theme == FeastTheme.Mountains };
        }
        // Production metadata is authoritative here. Eater/current online cook skills
        // cannot qualify an unmarked drink for the new authored-mead timing bonus.
        internal static bool HasAuthoredMastery(ItemDrop.ItemData item)
        {
            if (!Cooking35Service.EffectsEnabled) return false;
            if (CookingProductIdentity.Fresh(item))
                return CookingProductIdentity.TryTier(item, out int freshTier) && (freshTier == 35 || freshTier == 70);
            // Historical stored grants remain valid; never infer mastery from the eater.
            return item != null && item.m_crafterID != 0L && item.m_customData != null &&
                item.m_customData.TryGetValue(Cooking35Service.CookMasteryKey, out string text) &&
                int.TryParse(text, out int tier) && (tier == 35 || tier == 70);
        }

        internal static string MasteredTooltipKey(ItemDrop.ItemData item)
        {
            if (!Cooking35Service.IsTimedMead(item) || !HasAuthoredMastery(item)) return null;
            StatusEffect effect = item.m_shared.m_consumeStatusEffect;
            if (float.IsNaN(effect.m_ttl) || float.IsInfinity(effect.m_ttl)) return null;
            // Use the same delivery safeguard as consumption: unsupported custom
            // recovery must not promise a cooldown reduction that will be rejected.
            float adjusted = Lifetime(effect, effect.m_ttl, true, false, false);
            if (adjusted == effect.m_ttl || float.IsNaN(adjusted) || float.IsInfinity(adjusted)) return null;
            return IsRecoveryCooldown(effect) ? "$vm_item_master_mead_recovery" : "$vm_item_master_mead_duration";
        }

        private static bool IsRecoveryCategory(StatusEffect effect) => effect != null && effect.m_cooldownIcon &&
            (effect.m_category == "healthpotion" || effect.m_category == "staminapotion" || effect.m_category == "eitrpotion");

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
            mastery = mastery && Cooking35Service.EffectsEnabled;
            if (!IsRecoveryCooldown(template))
            {
                // An unsupported recovery channel is still a reuse lockout, not a
                // sustained benefit. Do not lengthen it or advertise a false bonus.
                if (IsRecoveryCategory(template)) return baseTtl;
                return baseTtl * (1f + (mastery ? .4f : 0f) + (swamp ? .3f : 0f));
            }
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
