using System.Globalization;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasteryRuntime
    {
        internal static float TierStep { get; private set; }
        internal static float FirstUniqueMultiplier { get; private set; }
        internal static float DodgeXpCoefficient { get; private set; }
        internal static bool WorldBossXpScalingEnabled { get; private set; }
        internal static float EarlyBossXpBonus { get; private set; }
        internal static float LateBossXpBonus { get; private set; }
        internal static bool LegacyCatchUpEnabled { get; private set; }
        internal static float LegacyCatchUpScale { get; private set; }
        internal static float CombatDamageSkillScalingDefault { get; private set; }
        internal static float CombatDamageSkillScalingBow { get; private set; }
        internal static float CombatDamageSkillScalingClub { get; private set; }
        internal static float ClubStaggerPerSkillLevel { get; private set; }

        internal static void ResetToLocalConfig()
        {
            TierStep = Mathf.Max(0f, MasteryPlugin.Settings.TierStep.Value);
            FirstUniqueMultiplier = Mathf.Max(1f, MasteryPlugin.Settings.FirstUniqueMultiplier.Value);
            DodgeXpCoefficient = Mathf.Max(0f, MasteryPlugin.Settings.DodgeXpCoefficient.Value);
            WorldBossXpScalingEnabled = MasteryPlugin.Settings.EnableWorldBossXpScaling.Value;
            EarlyBossXpBonus = Mathf.Max(0f, MasteryPlugin.Settings.EarlyBossXpBonus.Value);
            LateBossXpBonus = Mathf.Max(0f, MasteryPlugin.Settings.LateBossXpBonus.Value);
            LegacyCatchUpEnabled = MasteryPlugin.Settings.EnableLegacyCatchUp.Value;
            LegacyCatchUpScale = Mathf.Max(0f, MasteryPlugin.Settings.LegacyCatchUpScale.Value);
            CombatDamageSkillScalingDefault = Mathf.Clamp01(MasteryPlugin.Settings.CombatDamageSkillScalingDefault.Value);
            // Existing configs store 0.55; take the newly requested 15 percentage points
            // from that retained VANILLA SKILL BONUS, not from final bow damage.
            CombatDamageSkillScalingBow = Mathf.Clamp01(MasteryPlugin.Settings.CombatDamageSkillScalingBow.Value - .15f);
            CombatDamageSkillScalingClub = Mathf.Clamp01(MasteryPlugin.Settings.CombatDamageSkillScalingClub.Value);
            ClubStaggerPerSkillLevel = Mathf.Max(0f, MasteryPlugin.Settings.ClubStaggerPerSkillLevel.Value);
        }

        internal static void ApplyServerSettings(float tierStep, float firstUniqueMultiplier, float dodgeXpCoefficient, bool worldBossXpScalingEnabled, float earlyBossXpBonus, float lateBossXpBonus, bool legacyCatchUpEnabled, float legacyCatchUpScale, float combatDefault, float combatBow, float combatClub, float clubStaggerPerLevel)
        {
            TierStep = Mathf.Max(0f, tierStep);
            FirstUniqueMultiplier = Mathf.Max(1f, firstUniqueMultiplier);
            DodgeXpCoefficient = Mathf.Max(0f, dodgeXpCoefficient);
            WorldBossXpScalingEnabled = worldBossXpScalingEnabled;
            EarlyBossXpBonus = Mathf.Max(0f, earlyBossXpBonus);
            LateBossXpBonus = Mathf.Max(0f, lateBossXpBonus);
            LegacyCatchUpEnabled = legacyCatchUpEnabled;
            LegacyCatchUpScale = Mathf.Max(0f, legacyCatchUpScale);
            CombatDamageSkillScalingDefault = Mathf.Clamp01(combatDefault);
            CombatDamageSkillScalingBow = Mathf.Clamp01(combatBow);
            CombatDamageSkillScalingClub = Mathf.Clamp01(combatClub);
            ClubStaggerPerSkillLevel = Mathf.Max(0f, clubStaggerPerLevel);
            WorldProgressionXpService.Refresh();
        }

        internal static string Manifest()
        {
            return MasteryPlugin.Version + "|" + TierStep.ToString("R", CultureInfo.InvariantCulture) + "|" +
                FirstUniqueMultiplier.ToString("R", CultureInfo.InvariantCulture) + "|" +
                DodgeXpCoefficient.ToString("R", CultureInfo.InvariantCulture) + "|" +
                WorldBossXpScalingEnabled + "|" + EarlyBossXpBonus.ToString("R", CultureInfo.InvariantCulture) + "|" +
                LateBossXpBonus.ToString("R", CultureInfo.InvariantCulture) + "|" + LegacyCatchUpEnabled + "|" + LegacyCatchUpScale.ToString("R", CultureInfo.InvariantCulture) + "|" +
                CombatDamageSkillScalingDefault.ToString("R", CultureInfo.InvariantCulture) + "|" + CombatDamageSkillScalingBow.ToString("R", CultureInfo.InvariantCulture) + "|" +
                CombatDamageSkillScalingClub.ToString("R", CultureInfo.InvariantCulture) + "|" + ClubStaggerPerSkillLevel.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static string ManifestHash() => Manifest().GetStableHashCode().ToString(CultureInfo.InvariantCulture);
    }
}

