using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Reuses Valheim's own skill-row sprites. No custom assets and no raven fallback unless a row was unavailable.
    internal static class PerkUiIconService
    {
        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(SkillsDialog), "m_elements");
        private static readonly Dictionary<Skills.SkillType, Sprite> Icons = new Dictionary<Skills.SkillType, Sprite>();

        internal static void Capture(SkillsDialog dialog, Player player)
        {
            if (dialog == null || player == null) return;
            List<GameObject> rows = ElementsField?.GetValue(dialog) as List<GameObject>;
            List<Skills.Skill> skills = player.GetSkills().GetSkillList();
            if (rows == null || skills == null) return;
            for (int i = 0; i < Mathf.Min(rows.Count, skills.Count); ++i)
            {
                Skills.Skill skill = skills[i];
                if (skill?.m_info == null || rows[i] == null) continue;
                Image[] images = rows[i].GetComponentsInChildren<Image>(true);
                foreach (Image image in images)
                    if (image != null && image.sprite != null && image.gameObject.name.ToLowerInvariant().Contains("icon"))
                    {
                        Icons[skill.m_info.m_skill] = image.sprite;
                        break;
                    }
            }
        }

        internal static Sprite ForPerk(string perkId, Sprite fallback)
        {
            Skills.SkillType skill;
            if (!TryMap(perkId, out skill)) return fallback;
            Sprite sprite;
            return Icons.TryGetValue(skill, out sprite) && sprite != null ? sprite : fallback;
        }

        private static bool TryMap(string perkId, out Skills.SkillType skill)
        {
            skill = Skills.SkillType.None;
            if (string.IsNullOrEmpty(perkId)) return false;
            string id = perkId;
            if (id.EndsWith("_35") || id.EndsWith("_70")) id = id.Substring(0, id.Length - 3);
            else if (id.EndsWith("_100")) id = id.Substring(0, id.Length - 4);
            switch (id)
            {
                case "cooking": skill = Skills.SkillType.Cooking; break;
                case "crafting": skill = Skills.SkillType.Crafting; break;
                case "farming": skill = Skills.SkillType.Farming; break;
                case "fishing": skill = Skills.SkillType.Fishing; break;
                case "jump": skill = Skills.SkillType.Jump; break;
                case "pickaxes": skill = Skills.SkillType.Pickaxes; break;
                case "ride": skill = Skills.SkillType.Ride; break;
                case "run": skill = Skills.SkillType.Run; break;
                case "sneak": skill = Skills.SkillType.Sneak; break;
                case "swim": skill = Skills.SkillType.Swim; break;
                case "woodcutting": skill = Skills.SkillType.WoodCutting; break;
                case "swords": skill = Skills.SkillType.Swords; break;
                case "axes": skill = Skills.SkillType.Axes; break;
                case "clubs": skill = Skills.SkillType.Clubs; break;
                case "knives": skill = Skills.SkillType.Knives; break;
                case "spears": skill = Skills.SkillType.Spears; break;
                case "polearms": skill = Skills.SkillType.Polearms; break;
                case "bows": skill = Skills.SkillType.Bows; break;
                case "crossbows": skill = Skills.SkillType.Crossbows; break;
                case "fists": skill = Skills.SkillType.Unarmed; break;
                case "blocking": skill = Skills.SkillType.Blocking; break;
                case "dodge": skill = Skills.SkillType.Dodge; break;
                case "elementalmagic": skill = Skills.SkillType.ElementalMagic; break;
                case "bloodmagic": skill = Skills.SkillType.BloodMagic; break;
                default: return false;
            }
            return true;
        }
    }
}

