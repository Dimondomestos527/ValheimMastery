using System;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class PersonalCooking69Correction
    {
        // Root's human-confirmed file decodes to this exact native name and ID.
        // A same-named character with another ID must remain unaffected.
        internal const string TargetName = "SNiiigurd";
        internal const long TargetId = 333872180;
        internal const string Marker = "valheim_mastery.personal.cooking69.20261013.v1";

        internal static bool TryApply(Player player, string targetName, long targetId)
        {
            if (targetId == 0 || string.IsNullOrEmpty(targetName) || player == null ||
                player != Player.m_localPlayer || player.m_nview?.IsOwner() != true ||
                MasteryPlugin.Settings?.Enabled.Value != true) return false;
            PlayerProfile profile = Game.instance?.GetPlayerProfile();
            if (profile == null || profile.GetPlayerID() != targetId || player.GetPlayerID() != targetId ||
                !string.Equals(profile.GetName(), targetName, StringComparison.Ordinal) ||
                player.m_customData.ContainsKey(Marker)) return false;

            Skills skills = player.GetSkills();
            // GetSkill creates a missing entry. Eligibility must only read the
            // loaded character's existing skill state.
            if (skills?.m_skillData == null ||
                !skills.m_skillData.TryGetValue(Skills.SkillType.Cooking, out Skills.Skill cooking) ||
                cooking == null) return false;
            float current = cooking.m_level;
            if (float.IsNaN(current) || float.IsInfinity(current) || current <= 50f) return false;
            if (current >= 69f)
            {
                // Already satisfied is terminal too: a later death must not
                // turn a needless correction into another restoration.
                player.m_customData[Marker] = "satisfied";
                return false;
            }
            // Native CheatRaiseSkill can rebalance other skills when this flag
            // is on. Do not disable it or touch another skill for this correction.
            if (skills.m_useSkillCap) return false;
            float accumulator = cooking.m_accumulator;
            skills.CheatRaiseSkill(nameof(Skills.SkillType.Cooking), 69f - current, false);
            if (cooking.m_level != 69f || cooking.m_accumulator != accumulator) return false;
            // Native character Save owns both skill state and this portable marker.
            // No forced save, XP event, milestone replay, reset, RPC or world key.
            player.m_customData[Marker] = "applied";
            MasteryPlugin.Log.LogInfo("[PersonalCooking69] applied to confirmed local character.");
            return true;
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PersonalCooking69SpawnPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance)
        {
            try { PersonalCooking69Correction.TryApply(__instance,
                PersonalCooking69Correction.TargetName, PersonalCooking69Correction.TargetId); }
            catch (Exception failure)
            {
                if (MasteryPlugin.Settings?.VerboseLogging.Value == true)
                    MasteryPlugin.Log?.LogWarning("[PersonalCooking69] skipped: " + failure.GetType().Name);
            }
        }
    }
}
