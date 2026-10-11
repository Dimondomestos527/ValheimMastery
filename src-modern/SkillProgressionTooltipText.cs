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
            string native = PerkLocalization.Localize(skill.m_info.m_description)?.Trim() ?? string.Empty;
            string added = AddedPassive(skill.m_info.m_skill, Ukrainian());
            if (string.IsNullOrWhiteSpace(added)) return native;
            string label = Ukrainian() ? "Додатково від рівня:" : "Additional level effect:";
            return native + (native.Length > 0 ? "\n\n" : string.Empty) +
                "<color=#B9C5CE>" + label + "</color> " + added;
        }

        private static bool Ukrainian() => Localization.instance != null &&
            string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian",
                StringComparison.OrdinalIgnoreCase);

        private static string AddedPassive(Skills.SkillType skill, bool ua)
        {
            switch (skill)
            {
                case Skills.SkillType.Cooking:
                    return ua
                        ? "вищий рівень збільшує шанс додаткового виходу з ферментера."
                        : "higher skill increases the chance of extra fermenter output.";
                case Skills.SkillType.Crafting:
                    return ua
                        ? "вищий рівень збільшує шанс додаткового виходу з переробних станцій."
                        : "higher skill increases the chance of extra processing-station output.";
                case Skills.SkillType.Farming:
                    return ua
                        ? "вищий рівень збільшує шанс додаткового врожаю та додаткового соку під час збору."
                        : "higher skill increases the chance of extra harvest and extra sap when collected.";
                case Skills.SkillType.Pickaxes:
                    return ua
                        ? "вищий рівень збільшує шанс видобути додатковий цінний ресурс із покладу."
                        : "higher skill increases the chance of an extra valuable resource from a deposit.";
                case Skills.SkillType.WoodCutting:
                    return ua
                        ? "вищий рівень збільшує шанс отримати додаткову цінну деревину."
                        : "higher skill increases the chance of extra valuable wood.";
                case Skills.SkillType.Axes:
                    return ua
                        ? "вищий рівень розширює зону влучання сокири й трохи подовжує ближній удар."
                        : "higher skill widens an axe's hit area and slightly extends its melee reach.";
                case Skills.SkillType.Clubs:
                    return ua
                        ? "вищий рівень додатково посилює похитування від ударів булавою."
                        : "higher skill further increases stagger from mace hits.";
                case Skills.SkillType.Knives:
                    return ua
                        ? "вищий рівень додатково посилює удар у спину ножем."
                        : "higher skill further strengthens knife backstabs.";
                case Skills.SkillType.Crossbows:
                    return ua
                        ? "вищий рівень пришвидшує заряджання й подовжує політ лише гака; звичайні арбалети не змінюються."
                        : "higher skill charges and sends only the grappling hook farther; ordinary crossbows are unchanged.";
                case Skills.SkillType.Unarmed:
                    return ua
                        ? "вищий рівень підсилює бонус до адреналіну під час бою кулаками й ефекти стусана, а без зброї — шкоду та швидкість атак."
                        : "higher skill strengthens the adrenaline bonus while fighting with fists and the kick's effects; bare hands also gain damage and attack speed.";
                case Skills.SkillType.ElementalMagic:
                    return ua
                        ? "вищий рівень прискорює базове відновлення ейтру, не змінюючи затримку."
                        : "higher skill improves base eitr regeneration without changing its delay.";
                case Skills.SkillType.BloodMagic:
                    return ua
                        ? "вищий рівень додає лікування до звичайного тіку відновлення."
                        : "higher skill adds healing to the normal regeneration tick.";
                default:
                    return string.Empty;
            }
        }
    }
}

