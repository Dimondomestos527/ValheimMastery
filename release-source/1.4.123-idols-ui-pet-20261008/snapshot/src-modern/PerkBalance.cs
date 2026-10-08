using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CombatSkillScalingService
    {
        // Verified from local Valheim 1.0.x assembly_valheim.dll:
        // Skills.GetRandomSkillFactor has a skill-zero randomized range of 0.25..0.55.
        private const float VanillaSkillZeroMinFactor = 0.25f;
        private const float VanillaSkillZeroMaxFactor = 0.55f;
        private const float VanillaSkillCenterAtZero = 0.40f;
        private const float VanillaSkillCenterAtHundred = 1.00f;
        private const float VanillaRandomSpread = 0.15f;
        private static readonly HashSet<string> LoggedStates = new HashSet<string>();

        internal static bool IsAffectedCombatSkill(Skills.SkillType skill)
        {
            switch (skill)
            {
                case Skills.SkillType.Swords:
                case Skills.SkillType.Axes:
                case Skills.SkillType.Knives:
                case Skills.SkillType.Spears:
                case Skills.SkillType.Polearms:
                case Skills.SkillType.Crossbows:
                case Skills.SkillType.Unarmed:
                case Skills.SkillType.ElementalMagic:
                case Skills.SkillType.BloodMagic:
                case Skills.SkillType.Pickaxes:
                case Skills.SkillType.WoodCutting:
                case Skills.SkillType.Bows:
                case Skills.SkillType.Clubs:
                    return true;
                default:
                    return false;
            }
        }

        internal static float GetDamageCoefficient(Skills.SkillType skill)
        {
            // Axe mastery keeps 45% of the vanilla skill-derived bonus, not 45% final damage.
            if (skill == Skills.SkillType.Axes) return 0.45f;
            // Retain 85% of the skill-derived contribution, not 85% final damage.
            if (skill == Skills.SkillType.Crossbows) return 0.85f;
            if (skill == Skills.SkillType.Bows) return MasteryRuntime.CombatDamageSkillScalingBow;
            if (skill == Skills.SkillType.Clubs) return MasteryRuntime.CombatDamageSkillScalingClub;
            return MasteryRuntime.CombatDamageSkillScalingDefault;
        }
        internal static float GetWeaponDamageCoefficient(Skills.SkillType skill, ItemDrop.ItemData weapon)
        {
            // Fist weapons retain 40% of the skill-derived bonus. Bare hands
            // retain their approved separate scaling and passive package.
            if (skill == Skills.SkillType.Unarmed && weapon?.m_shared?.m_skillType == Skills.SkillType.Unarmed &&
                !PerkRuntimeService.IsBareHands(weapon)) return 0.40f;
            // Keep the mace adjustment narrow: reduce only its retained Clubs
            // skill-level damage bonus; base item damage and sledge damage stay
            // unchanged. This is a 40% cap on the bonus, not a 40% final hit.
            if (skill == Skills.SkillType.Clubs && weapon?.m_shared?.m_skillType == Skills.SkillType.Clubs)
            {
                string name = PerkRuntimeService.ItemPrefabName(weapon)?.Replace("(Clone)", string.Empty).Trim();
                if (!string.IsNullOrEmpty(name) &&
                    (name.StartsWith("Mace", StringComparison.OrdinalIgnoreCase) || name.Equals("Club", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("Frostner", StringComparison.OrdinalIgnoreCase) || name.Equals("Porcupine", StringComparison.OrdinalIgnoreCase)))
                    return Mathf.Min(0.40f, MasteryRuntime.CombatDamageSkillScalingClub);
            }
            return GetDamageCoefficient(skill);
        }

        internal static float GetClubSkillStaggerBonus(float skillLevel)
        {
            return Mathf.Max(0f, skillLevel) * MasteryRuntime.ClubStaggerPerSkillLevel;
        }

        internal static void ScaleRandomDamageFactor(Skills skills, Skills.SkillType skill, ref float vanillaResult)
        {
            if (skills == null || !IsAffectedCombatSkill(skill)) return;

            float level = Mathf.Clamp(skills.GetSkillLevel(skill), 0f, 100f);
            float normalizedSkill = level / 100f;
            float vanillaCenter = Mathf.Lerp(VanillaSkillCenterAtZero, VanillaSkillCenterAtHundred, normalizedSkill);
            float vanillaMin = Mathf.Clamp01(vanillaCenter - VanillaRandomSpread);
            float vanillaMax = Mathf.Clamp01(vanillaCenter + VanillaRandomSpread);
            float roll = Mathf.InverseLerp(vanillaMin, vanillaMax, vanillaResult);
            float levelZeroResultForSameRoll = Mathf.Lerp(VanillaSkillZeroMinFactor, VanillaSkillZeroMaxFactor, roll);
            float coefficient = GetWeaponDamageCoefficient(skill, skills.m_player?.GetCurrentWeapon());
            float newResult = levelZeroResultForSameRoll + coefficient * (vanillaResult - levelZeroResultForSameRoll);
            vanillaResult = Mathf.Clamp01(newResult);
            LogOnce(skill, level, vanillaCenter, Mathf.Lerp(VanillaSkillZeroMinFactor, VanillaSkillZeroMaxFactor, 0.5f) + coefficient * (vanillaCenter - 0.40f), coefficient);
        }

        internal static void ApplyClubStaggerScaling(Player player, HitData hit)
        {
            if (player == null || hit == null || hit.m_skill != Skills.SkillType.Clubs ||
                PerkRuntimeService.IsPerkGenerated(hit) ||
#if !MASTERY_RELEASE_SAFE || MASTERY_CLUB_PASSIVES
                ClubWeaponClassService.Classify(player.GetCurrentWeapon()) != ClubWeaponClass.Mace ||
#endif
                !PerkRuntimeService.TryMarkApplied(hit, "clubs_skill_stagger"))
                return;

            float skillBonus = GetClubSkillStaggerBonus(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Clubs));
            // Level 35 Counterblow is conditional; keep only the global Club skill stagger scaling.
            hit.m_staggerMultiplier *= 1f + skillBonus;
        }

        private static void LogOnce(Skills.SkillType skill, float level, float vanillaFactor, float newFactor, float coefficient)
        {
            if (MasteryPlugin.Settings == null || !MasteryPlugin.Settings.VerboseLogging.Value) return;
            int roundedLevel = Mathf.RoundToInt(level);
            string key = skill + "|" + roundedLevel + "|" + coefficient.ToString("R");
            if (!LoggedStates.Add(key)) return;

            string message = "[SkillScaling] " + skill + " L" + roundedLevel +
                " VanillaFactor=" + vanillaFactor.ToString("0.###") +
                " NewFactor=" + newFactor.ToString("0.###") +
                " Coef=" + coefficient.ToString("0.##");
            if (skill == Skills.SkillType.Bows) message += " DrawSpeed=Vanilla";
            if (skill == Skills.SkillType.Clubs)
            {
                float stagger = GetClubSkillStaggerBonus(level);
                message += " SkillStagger=+" + (stagger * 100f).ToString("0.#") +
                    "% Perk35=+30% TotalWithPerk35=+" + ((stagger + 0.30f) * 100f).ToString("0.#") + "%";
            }
            MasteryPlugin.Log.LogInfo(message);
        }
    }

    // This is intentionally the narrow vanilla damage-roll hook. GetSkillFactor is untouched,
    // which preserves bow draw/stamina, magic Eitr, and all non-combat skill behaviour.
    [HarmonyPatch(typeof(Skills), nameof(Skills.GetRandomSkillFactor))]
    internal static class CombatSkillRandomDamageFactorPatch
    {
        private static void Postfix(Skills __instance, Skills.SkillType skillType, ref float __result)
        {
            CombatSkillScalingService.ScaleRandomDamageFactor(__instance, skillType, ref __result);
        }
    }
}
