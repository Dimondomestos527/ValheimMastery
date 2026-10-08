using System;
using UnityEngine;

namespace ValheimMastery
{
    internal static class GatheringProgressionService
    {
        internal const float ResourceTierStep = 0.10f;
        internal const float ToolTierStep = 0.10f;
        internal const float FarmingTierStep = 0.20f;

        internal static bool IsGatheringSkill(Skills.SkillType skill) =>
            skill == Skills.SkillType.Farming || skill == Skills.SkillType.WoodCutting || skill == Skills.SkillType.Pickaxes;

        internal static float GetTierMultiplier(Skills.SkillType skill, int resourceTier, int toolTier)
        {
            resourceTier = Mathf.Max(1, resourceTier);
            toolTier = Mathf.Max(1, toolTier);
            if (skill == Skills.SkillType.Farming)
                return 1f + FarmingTierStep * (resourceTier - 1);
            if (skill == Skills.SkillType.WoodCutting || skill == Skills.SkillType.Pickaxes)
                return 1f + ResourceTierStep * (resourceTier - 1) + ToolTierStep * (toolTier - 1);
            return 1f;
        }

        internal static int GetResourceTier(Component source, Skills.SkillType skill)
        {
            string key = source?.gameObject != null ? source.gameObject.name : string.Empty;
            return GetResourceTier(key, skill);
        }

        internal static int GetResourceTier(ItemDrop.ItemData item, Skills.SkillType skill)
        {
            string key = item?.m_dropPrefab != null ? item.m_dropPrefab.name : item?.m_shared?.m_name;
            return GetResourceTier(key, skill);
        }

        internal static int GetResourceTier(string raw, Skills.SkillType skill)
        {
            string key = Normalize(raw);

            // Deep North must remain its own eighth progression step. Exact prefab names
            // are augmented by these stable content-family markers from the current build.
            if (Has(key, "deepnorth", "deep_north", "frozenking", "jotunwarrior", "jotunwitch", "frysling", "writhan", "elaking")) return 8;

            if (skill == Skills.SkillType.Farming)
            {
                if (Has(key, "ashvine", "vineberry", "smokepuff", "fiddlehead", "ashlands")) return 7;
                if (Has(key, "magecap", "jotunpuff", "mistlands")) return 6;
                if (Has(key, "barley", "flax", "cloudberry", "plains")) return 5;
                if (Has(key, "onion", "mountain")) return 4;
                if (Has(key, "turnip", "swamp")) return 3;
                if (Has(key, "carrot", "blueberr", "thistle", "blackforest", "black_forest")) return 2;
                return 1;
            }

            if (skill == Skills.SkillType.WoodCutting)
            {
                if (Has(key, "ashwood", "blackwood", "ashlands")) return 7;
                if (Has(key, "yggdrasil", "mistlands")) return 6;
                if (Has(key, "plains")) return 5;
                if (Has(key, "mountain", "oldlog")) return 4;
                if (Has(key, "ancientbark", "elderbark", "swamptree", "ancienttree", "swamp")) return 3;
                if (Has(key, "finewood", "corewood", "roundlog", "birch", "oak", "pine")) return 2;
                return 1;
            }

            if (skill == Skills.SkillType.Pickaxes)
            {
                if (Has(key, "flametal", "ashlands")) return 7;
                if (Has(key, "blackmarble", "softtissue", "mistlands")) return 6;
                if (Has(key, "blackmetal", "plains")) return 5;
                if (Has(key, "silver", "obsidian", "mountain")) return 4;
                if (Has(key, "iron", "scrapiron", "swamp")) return 3;
                if (Has(key, "copper", "tin", "bronze", "blackforest", "black_forest")) return 2;
                return 1;
            }

            return 1;
        }

        internal static bool IsPlainWood(string raw) => string.Equals(Normalize(raw), "wood", StringComparison.OrdinalIgnoreCase);
        internal static bool IsPlainStone(string raw) => string.Equals(Normalize(raw), "stone", StringComparison.OrdinalIgnoreCase);

        private static bool Has(string value, params string[] fragments)
        {
            foreach (string fragment in fragments)
                if (value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        internal static string Normalize(string value)
        {
            string key = (value ?? string.Empty).Replace("(Clone)", string.Empty).Trim();
            if (key.Length > 0 && key[0] == '$') key = key.Substring(1);
            return key;
        }
    }
}
