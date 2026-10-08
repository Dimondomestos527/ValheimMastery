using System;
using System.Reflection;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class FeastMeadStrength
    {
        // Only numeric benefits with a known native neutral value. Negative
        // restoration and other drawbacks retain their original magnitude.
        private static readonly string[] Amounts = {
            "m_healthUpFront", "m_healthOverTime", "m_healthPerTick",
            "m_staminaUpFront", "m_staminaOverTime", "m_eitrUpFront", "m_eitrOverTime",
            "m_addArmor", "m_armorMultiplier", "m_addMaxCarryWeight", "m_speedModifier", "m_swimSpeedModifier",
            "m_skillLevelModifier", "m_skillLevelModifier2", "m_raiseSkillModifier" };
        private static readonly string[] Multipliers = {
            "m_healthRegenMultiplier", "m_staminaRegenMultiplier", "m_eitrRegenMultiplier",
            "m_damageModifier" };
        private static readonly string[] Costs = {
            "m_runStaminaDrainModifier", "m_jumpStaminaUseModifier", "m_attackStaminaUseModifier",
            "m_blockStaminaUseModifier", "m_dodgeStaminaUseModifier", "m_swimStaminaUseModifier",
            "m_homeItemStaminaUseModifier", "m_sneakStaminaUseModifier", "m_runStaminaUseModifier" };
        private static readonly FieldInfo[] AmountFields = Fields(Amounts);
        private static readonly FieldInfo[] MultiplierFields = Fields(Multipliers);
        private static readonly FieldInfo[] CostFields = Fields(Costs);

        private static FieldInfo[] Fields(string[] names)
        {
            var result = new FieldInfo[names.Length];
            for (int i = 0; i < names.Length; i++)
                result[i] = typeof(SE_Stats).GetField(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(typeof(SE_Stats).FullName, names[i]);
            return result;
        }
        private static float Value(FieldInfo field, SE_Stats stats) => (float)field.GetValue(stats);
        internal static bool NeedsAlternative(StatusEffect template)
        {
            if (!(template is SE_Stats stats)) return true;
            if (stats.m_mods?.Count > 0 || stats.m_maxMaxFallSpeed > 0f || stats.m_pheromoneTarget != null ||
                stats.m_pheromoneSpawnChanceOverride > 0f || stats.m_pheromoneLevelUpMultiplier > 1f || stats.m_pheromoneFlee)
                return true;
            bool benefit = stats.m_jumpModifier.x > 0f || stats.m_jumpModifier.y > 0f || stats.m_jumpModifier.z > 0f;
            foreach (FieldInfo field in AmountFields) benefit |= Value(field, stats) > 0f;
            foreach (FieldInfo field in MultiplierFields) benefit |= Value(field, stats) > 1f;
            foreach (FieldInfo field in CostFields)
            {
                float value = Value(field, stats);
                if (value * 1.5f < -1f) return true;
                benefit |= value < 0f;
            }
            return !benefit;
        }
        internal static void SetFromTemplate(SE_Stats instance, SE_Stats template, bool strengthen)
        {
            foreach (FieldInfo field in AmountFields)
            {
                float value = Value(field, template);
                field.SetValue(instance, strengthen && value > 0f ? value * 1.5f : value);
            }
            foreach (FieldInfo field in MultiplierFields)
            {
                float value = Value(field, template);
                field.SetValue(instance, strengthen && value > 1f ? 1f + (value - 1f) * 1.5f : value);
            }
            foreach (FieldInfo field in CostFields)
            {
                float value = Value(field, template);
                field.SetValue(instance, strengthen && value < 0f ? value * 1.5f : value);
            }
            var jump = template.m_jumpModifier;
            if (strengthen)
            {
                if (jump.x > 0f) jump.x *= 1.5f;
                if (jump.y > 0f) jump.y *= 1.5f;
                if (jump.z > 0f) jump.z *= 1.5f;
            }
            instance.m_jumpModifier = jump;
            // Never mutate m_mods: native Clone shares that list with its asset.
        }
        internal static bool IsRecoveryCooldown(StatusEffect template)
        {
            if (!(template is SE_Stats stats) || !template.m_cooldownIcon || template.m_ttl <= 0f) return false;
            string category = template.m_category;
            if (category != "healthpotion" && category != "staminapotion" && category != "eitrpotion") return false;
            return ShortRecovery(stats.m_healthOverTime, stats.m_healthOverTimeDuration, template.m_ttl) ||
                ShortRecovery(stats.m_staminaOverTime, stats.m_staminaOverTimeDuration, template.m_ttl) ||
                ShortRecovery(stats.m_eitrOverTime, stats.m_eitrOverTimeDuration, template.m_ttl);
        }
        private static bool ShortRecovery(float amount, float duration, float ttl) => amount > 0f && duration > 0f && duration < ttl;
    }

    internal sealed class FeastMeadConsumption
    {
        [ThreadStatic] internal static FeastMeadConsumption Current;
        internal Player Player;
        internal StatusEffect Template;
        internal bool Strengthen;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    [HarmonyPriority(Priority.First)]
    internal static class FeastMeadConsumptionScopePatch
    {
        private static void Prefix(Player __instance, ItemDrop.ItemData item, out FeastMeadConsumption __state)
        {
            __state = FeastMeadConsumption.Current;
            StatusEffect template = item?.m_shared?.m_consumeStatusEffect;
            FeastMeadConsumption.Current = template != null && item.m_shared.m_isDrink
                ? new FeastMeadConsumption { Player = __instance, Template = template,
                    Strengthen = MasterFeastThemeService.GetTheme(__instance) == FeastTheme.Mountains && !FeastMeadStrength.NeedsAlternative(template) }
                : null;
        }
        private static void Finalizer(FeastMeadConsumption __state) => FeastMeadConsumption.Current = __state;
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new Type[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short) })]
    internal static class FeastMeadPreparedClonePatch
    {
        private static void Prefix(SEMan __instance, ref StatusEffect statusEffect)
        {
            var context = FeastMeadConsumption.Current;
            if (context == null || !context.Strengthen || !ReferenceEquals(__instance.m_character, context.Player) ||
                !ReferenceEquals(statusEffect, context.Template) || !(statusEffect is SE_Stats template)) return;
            var prepared = (SE_Stats)template.Clone();
            FeastMeadStrength.SetFromTemplate(prepared, template, true);
            statusEffect = prepared;
        }
    }

    [HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.ResetTime))]
    internal static class FeastMeadRefreshStrengthPatch
    {
        private static void Prefix(SE_Stats __instance)
        {
            var context = FeastMeadConsumption.Current;
            if (context?.Template is SE_Stats template && ReferenceEquals(__instance.m_character, context.Player) &&
                __instance.NameHash() == template.NameHash())
            {
                FeastMeadStrength.SetFromTemplate(__instance, template, context.Strengthen);
                if (__instance.m_healthOverTime > 0f && __instance.m_healthOverTimeInterval > 0f)
                {
                    float duration = template.m_healthOverTimeDuration > 0f ? template.m_healthOverTimeDuration : template.m_ttl;
                    __instance.m_healthOverTimeTicks = duration / __instance.m_healthOverTimeInterval;
                    __instance.m_healthOverTimeTickHP = __instance.m_healthOverTime / __instance.m_healthOverTimeTicks;
                    __instance.m_healthOverTimeTimer = 0f;
                }
            }
        }
    }
}
