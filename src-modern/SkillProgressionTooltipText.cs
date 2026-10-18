using System;

namespace ValheimMastery
{
    // Skill-row hover deliberately excludes 35/70/100 mastery content. The native
    // description stays authoritative for vanilla effects; this helper adds only
    // direct skill-level passives implemented by Valheim Mastery.
    internal static class SkillProgressionTooltipText
    {
        internal static string Build(Skills.Skill skill)
        {
            if (skill?.m_info == null) return string.Empty;
            return Describe(skill.m_info.m_skill, PerkLocalization.Localize(skill.m_info.m_description), Player.m_localPlayer);
        }

        internal static string Describe(Skills.SkillType skill, string nativeDescription, Player player)
        {
            bool ua = Ukrainian();
            string lead = PerkNarrativeService.SkillLead(skill, ua);
            string native = nativeDescription?.Trim() ?? "";
            string passive = Passive(skill, ua, player);
            string text = lead;
            if (native.Length > 0)
            {
                text += (text.Length > 0 ? " " : "") + native;
                char last = native[native.Length - 1];
                if (passive.Length > 0 && last != '.' && last != '!' && last != '?') text += ".";
            }
            if (passive.Length > 0) text += (text.Length > 0 ? " " : "") + passive;
            return text;
        }

        private static bool Ukrainian() => Localization.instance != null &&
            string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian",
                StringComparison.OrdinalIgnoreCase);

        internal static string Passive(Skills.SkillType skill, bool ua, Player player)
        {
            switch (skill)
            {
                case Skills.SkillType.Cooking:
                    return ua
                        ? "Досвідчений кухар частіше отримує зайву пляшку під час розливу готової медовиці."
                        : "An experienced cook more often gains an extra bottle when tapping finished mead.";
                case Skills.SkillType.Crafting:
                    return ua
                        ? "З досвідом переробка завантаженої тобою сировини частіше дає зайвий виріб."
                        : "With experience, processing the materials you load more often yields extra output.";
                case Skills.SkillType.Farming:
                    return ua
                        ? "З досвідом земля частіше дарує зайвий врожай, а збірник — додатковий сік."
                        : "With experience, the land more often yields extra crops, and the extractor extra sap.";
                case Skills.SkillType.Pickaxes:
                    return ua
                        ? "Досвідчений рудокоп частіше знаходить у покладі зайвий цінний ресурс."
                        : "An experienced miner more often finds extra valuable resources in a deposit.";
                case Skills.SkillType.WoodCutting:
                    return ua
                        ? "Досвід допомагає частіше добувати зайву цінну деревину."
                        : "Experience helps you gather extra valuable wood more often.";
                case Skills.SkillType.Axes:
                    return ua
                        ? "З досвідом замах сокири охоплює ширшу зону й сягає трохи далі."
                        : "With experience, an axe swing covers a wider area and reaches slightly farther.";
                case Skills.SkillType.Clubs:
                    return ua
                        ? "Умілі удари булави сильніше збивають ворога з рівноваги."
                        : "Skilled mace strikes knock enemies further off balance.";
                case Skills.SkillType.Knives:
                    return ua
                        ? "Майстерність ножів підсилює звичайні удари в спину й з іншою зброєю."
                        : "Knife mastery also strengthens ordinary backstabs with other weapons.";
                case Skills.SkillType.Crossbows:
                    return CrossbowsHookSecretText.Progression(player, ua);
                case Skills.SkillType.Unarmed:
                    return ua
                        ? "Досвід кулачного бою додає адреналіну й сили стусану; голі руки б’ють швидше та сильніше. Кулачна зброя отримує стриманий бонус навички до шкоди."
                        : "Experience in fist fighting adds adrenaline and power to your kick; bare hands strike faster and harder. Fist weapons gain a restrained skill-derived damage bonus.";
                case Skills.SkillType.ElementalMagic:
                    return ua
                        ? "Досвід прискорює базове відновлення ейтру, але не скорочує паузу перед ним."
                        : "Experience improves base eitr regeneration without shortening the pause before it.";
                case Skills.SkillType.BloodMagic:
                    return ua
                        ? "Досвід посилює звичайне відновлення здоров’я."
                        : "With experience, each normal regeneration tick restores more health.";
                default:
                    return string.Empty;
            }
        }
    }
}
