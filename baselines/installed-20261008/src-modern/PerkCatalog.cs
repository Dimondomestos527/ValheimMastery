using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    internal sealed class PerkDefinition
    {
        internal readonly Skills.SkillType Skill;
        internal readonly int Milestone;
        internal readonly string Id;
        internal readonly string NameToken;
        internal readonly string DescriptionToken;
        internal readonly string FlavorToken;

        internal PerkDefinition(Skills.SkillType skill, int milestone, string id)
        {
            Skill = skill;
            Milestone = milestone;
            Id = id;
            NameToken = "$vm_perk_" + id + "_name";
            DescriptionToken = "$vm_perk_" + id + "_desc";
            FlavorToken = "$vm_perk_" + id + "_flavor";
        }
    }

    internal static class PerkCatalog
    {
        private static readonly Dictionary<Skills.SkillType, List<PerkDefinition>> BySkill = new Dictionary<Skills.SkillType, List<PerkDefinition>>();

        static PerkCatalog()
        {
            Add(Skills.SkillType.Cooking, "cooking");
            Add(Skills.SkillType.Crafting, "crafting");
            Add(Skills.SkillType.Farming, "farming");
            Add(Skills.SkillType.Fishing, "fishing");
            Add(Skills.SkillType.Jump, "jump");
            Add(Skills.SkillType.Pickaxes, "pickaxes");
            Add(Skills.SkillType.Ride, "ride");
            Add(Skills.SkillType.Run, "run");
            Add(Skills.SkillType.Sneak, "sneak");
            Add(Skills.SkillType.Swim, "swim");
            Add(Skills.SkillType.WoodCutting, "woodcutting");
            Add(Skills.SkillType.Swords, "swords");
            Add(Skills.SkillType.Axes, "axes");
            Add(Skills.SkillType.Clubs, "clubs");
            Add(Skills.SkillType.Knives, "knives");
            Add(Skills.SkillType.Spears, "spears");
            Add(Skills.SkillType.Polearms, "polearms");
            Add(Skills.SkillType.Bows, "bows");
            Add(Skills.SkillType.Crossbows, "crossbows");
            Add(Skills.SkillType.Unarmed, "fists");
            Add(Skills.SkillType.Blocking, "blocking");
            Add(Skills.SkillType.Dodge, "dodge");
            Add(Skills.SkillType.ElementalMagic, "elementalmagic");
            Add(Skills.SkillType.BloodMagic, "bloodmagic");
        }

        internal static bool Contains(Skills.SkillType skill) => BySkill.ContainsKey(skill);

        internal static IReadOnlyList<PerkDefinition> Get(Skills.SkillType skill)
        {
            return BySkill.TryGetValue(skill, out List<PerkDefinition> perks) ? perks : Array.Empty<PerkDefinition>();
        }

        internal static PerkDefinition Get(Skills.SkillType skill, int milestone)
        {
            if (!BySkill.TryGetValue(skill, out List<PerkDefinition> perks))
                return null;
            return perks.Find(perk => perk.Milestone == milestone);
        }

        private static void Add(Skills.SkillType skill, string id)
        {
            BySkill[skill] = new List<PerkDefinition>
            {
                new PerkDefinition(skill, 35, id + "_35"),
                new PerkDefinition(skill, 70, id + "_70"),
                new PerkDefinition(skill, 100, id + "_100")
            };
        }
    }

    internal static class PerkStateService
    {
        internal static bool IsUnlocked(Player player, Skills.SkillType skill, int milestone)
        {
            return PerkRuntimeService.GetActualSkillLevel(player, skill) >= milestone;
        }

        internal static int GetNextMilestone(Player player, Skills.SkillType skill)
        {
            foreach (PerkDefinition perk in PerkCatalog.Get(skill))
                if (!IsUnlocked(player, skill, perk.Milestone))
                    return perk.Milestone;
            return 0;
        }

        internal static List<int> GetUnlockedMilestones(Player player, Skills.SkillType skill)
        {
            List<int> result = new List<int>();
            foreach (PerkDefinition perk in PerkCatalog.Get(skill))
                if (IsUnlocked(player, skill, perk.Milestone))
                    result.Add(perk.Milestone);
            return result;
        }

        internal static bool WasEverUnlocked(Player player, Skills.SkillType skill, int milestone)
        {
            return player != null && player.m_customData.ContainsKey(Key(skill, milestone));
        }

        internal static bool MarkEverUnlocked(Player player, Skills.SkillType skill, int milestone)
        {
            if (player == null || WasEverUnlocked(player, skill, milestone))
                return false;
            player.m_customData[Key(skill, milestone)] = "1";
            return true;
        }

        internal static string Key(Skills.SkillType skill, int milestone)
        {
            return "valheim_mastery.milestone." + skill + "." + milestone;
        }
    }
}
