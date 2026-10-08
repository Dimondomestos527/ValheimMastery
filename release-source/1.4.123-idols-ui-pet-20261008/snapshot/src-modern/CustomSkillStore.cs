using System;
using System.Globalization;

namespace ValheimMastery
{
    // Vanilla owns its native skills. These values live in Player.m_customData so they survive profile saves without changing vanilla serialization.
    internal enum CustomSkillType { Cooking, Farming, Crafting }

    internal static class CustomSkillStore
    {
        private const string Prefix = "valheim_mastery.custom.";

        internal static float GetLevel(Player player, CustomSkillType skill)
        {
            float level;
            return TryRead(player, skill, out level, out _) ? level : 0f;
        }

        internal static void Award(Player player, CustomSkillType skill, float baseXp)
        {
            if (player == null || baseXp <= 0f)
                return;
            float level;
            float accumulator;
            TryRead(player, skill, out level, out accumulator);
            if (level >= 100f)
                return;

            accumulator += baseXp;
            float needed = (float)Math.Pow(Math.Floor(level + 1f), 1.5) * 0.5f + 0.5f;
            while (accumulator >= needed && level < 100f)
            {
                accumulator -= needed;
                level += 1f;
                needed = (float)Math.Pow(Math.Floor(level + 1f), 1.5) * 0.5f + 0.5f;
            }
            player.m_customData[Prefix + skill] = level.ToString("R", CultureInfo.InvariantCulture) + "|" + accumulator.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool TryRead(Player player, CustomSkillType skill, out float level, out float accumulator)
        {
            level = 0f;
            accumulator = 0f;
            if (player == null || !player.m_customData.TryGetValue(Prefix + skill, out string raw))
                return false;
            string[] parts = raw.Split('|');
            return parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out level) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out accumulator);
        }
    }
}
