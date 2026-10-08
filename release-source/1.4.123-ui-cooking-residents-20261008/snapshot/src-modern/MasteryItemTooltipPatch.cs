using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(int) })]
    internal static class MasteryItemTooltipPatch
    {
        private static void Postfix(ItemDrop.ItemData __instance, ref string __result)
        {
            if (__instance?.m_shared == null || string.IsNullOrEmpty(__result)) return;
            AppendMagicStaffMastery(__instance, ref __result);
            if (Cooking35Service.IsTimedMead(__instance))
            {
                if (Cooking35Service.HasCookMastery(Player.m_localPlayer, __instance, 35))
                    __result += "\n<color=#8FD6A3>" + PerkLocalization.Localize("$vm_item_master_mead") + "</color>";
                return;
            }
            if (Cooking35Service.IsPreparedMeal(__instance))
            {
                if (Cooking35Service.HasCookMastery(Player.m_localPlayer, __instance, 35))
                    __result += "\n<color=#8FD6A3>" + PerkLocalization.Localize("$vm_item_master_cooked") + "</color>";
                return;
            }
            if (!IsWeaponOrTool(__instance.m_shared.m_itemType)) return;
            if (!IsRelevant(__instance.m_shared.m_skillType)) return;
            int tier = TierDatabase.GetItemTier(__instance);
            Skills.SkillType skill = __instance.m_shared.m_skillType;
            int combatPercent = Mathf.RoundToInt(MasteryRuntime.TierStep * (tier - 1) * 100f);
            int gatheringPercent = Mathf.RoundToInt(GatheringProgressionService.ToolTierStep * (tier - 1) * 100f);
            if (skill == Skills.SkillType.Pickaxes)
                __result += "\n<color=#E7B85C>" + string.Format(PerkLocalization.Localize("$vm_item_gathering_xp_contribution"), gatheringPercent) + "</color>";
            else if (skill == Skills.SkillType.Axes)
                __result += "\n<color=#E7B85C>" + string.Format(PerkLocalization.Localize("$vm_item_combat_xp_contribution"), combatPercent) +
                    "</color>\n<color=#8FD6A3>" + string.Format(PerkLocalization.Localize("$vm_item_gathering_xp_contribution"), gatheringPercent) + "</color>";
            else
                __result += "\n<color=#E7B85C>" + string.Format(PerkLocalization.Localize("$vm_item_combat_xp_contribution"), combatPercent) + "</color>";
            __result += "\n<color=#A8A8A8>" + string.Format(PerkLocalization.Localize("$vm_item_tier"), tier) + "</color>";
        }

        private static void AppendMagicStaffMastery(ItemDrop.ItemData item, ref string tooltip)
        {
            string prefab = PerkRuntimeService.ItemPrefabName(item);
            if (prefab != "StaffFireball" && prefab != "StaffIceShards" &&
                prefab != "StaffSkeleton" && prefab != "StaffShield") return;
            Player player = Player.m_localPlayer;
            bool ua = Localization.instance != null &&
                string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", System.StringComparison.OrdinalIgnoreCase);
            string marker = ua ? "<color=#BB88EE>Майстерність магії</color>" : "<color=#BB88EE>Magic Mastery</color>";
            if (tooltip.Contains("<color=#BB88EE>Майстерність магії</color>") || tooltip.Contains("<color=#BB88EE>Magic Mastery</color>")) return;
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("\n\n").Append(marker);
            if (prefab == "StaffFireball")
            {
                AddMagicLine(text, player, Skills.SkillType.ElementalMagic, 35, ua,
                    ua ? "Полум’я визріває в замаху: довший заряд несе ширший вибух і довше горіння. Блок розсіює заряд, але вже витрачений ейтр не повертається." :
                    "Fire ripens in the wind-up: a longer charge brings a wider blast and lasting flames. Block cancels the charge without returning spent Eitr.");
#if MASTERY_MAGIC70_EXPERIMENT
                AddMagicLine(text, player, Skills.SkillType.ElementalMagic, 70, ua,
                    ua ? "Вогняний посох прикликає Жарика — вогняного супутника, який б’ється за тебе. Сильніший посох підсилює його; новий поклик повертає й лікує живого Жарика. Після його загибелі наступний поклик ненадовго стає дорожчим." :
                    "A fire staff summons Zharik, a fiery companion who fights for you. A stronger staff empowers him; another call brings back and heals living Zharik. After his death, the next call briefly costs more.");
#else
                AddUnavailableLine(text, ua ? "Закляття недоступне в цій збірці." : "Spell is unavailable in this build.");
#endif
            }
            else if (prefab == "StaffIceShards")
            {
                AddMagicLine(text, player, Skills.SkillType.ElementalMagic, 35, ua,
                    ua ? "Слід криги поволі ранить і сковує ворогів, а мороз прибиває летючу здобич до землі." :
                    "Lingering frost wounds and binds foes, driving flying prey close to the ground.");
#if MASTERY_MAGIC70_EXPERIMENT
                AddMagicLine(text, player, Skills.SkillType.ElementalMagic, 70, ua,
                    ua ? "Крижаний посох здіймає бурю, яка ранить і сковує ворогів, не шкодячи союзникам. Новий поклик переносить її." :
                    "An ice staff raises a storm that wounds and binds foes without harming allies. Another call relocates it.");
#else
                AddUnavailableLine(text, ua ? "Закляття недоступне в цій збірці." : "Spell is unavailable in this build.");
#endif
            }
            else if (prefab == "StaffSkeleton")
            {
                AddMagicLine(text, player, Skills.SkillType.BloodMagic, 35, ua,
                    ua ? "Кістяний посох прикликає скелетів і поповнює твою свиту, коли в ній звільняється місце." :
                    "A skeleton staff summons skeletons and replenishes your retinue when it has room.");
#if MASTERY_CARRIER70_EXPERIMENT
                AddMagicLine(text, player, Skills.SkillType.BloodMagic, 70, ua,
                    ua ? "Кістяний посох прикликає Торбу: він несе твої портальні речі, уникає бою й відновлюється в безпеці. Його тіло змінюється, а поклажа залишається з тобою." :
                    "A skeleton staff summons Torba: he carries your portal-safe belongings, avoids battle and recovers in safety. His body changes; the cargo stays with you.");
#else
                AddUnavailableLine(text, ua ? "Торба-носій поки недоступний." : "Torba the carrier is not available yet.");
#endif
            }
            else
            {
                AddMagicLine(text, player, Skills.SkillType.BloodMagic, 35, ua,
                    ua ? "Щит підтримує життя свого носія. Власною кров’ю можна підживлювати ще цілі щити побратимів; кожна наступна секунда забирає більше здоров’я." :
                    "The shield sustains its bearer. Your lifeblood can renew comrades' unbroken barriers; each successive second consumes more health.");
#if MASTERY_MAGIC70_EXPERIMENT
                AddMagicLine(text, player, Skills.SkillType.BloodMagic, 70, ua,
                    ua ? "Кров замикає одного ворога в клітці. Коли пута лускають, сила щита стає карою для бранця." :
                    "Blood binds one foe in a cage. When its bonds burst, the shield's strength becomes the captive's punishment.");
#else
                AddUnavailableLine(text, ua ? "Кривава клітка недоступна в цій збірці." : "The blood cage is unavailable in this build.");
#endif
            }
            tooltip += text.ToString();
        }

        private static void AddMagicLine(System.Text.StringBuilder text, Player player, Skills.SkillType skill, int level, bool ukrainian, string description)
        {
            bool unlocked = PerkRuntimeService.HasPerk(player, skill, level);
            string state = unlocked ? "" : (ukrainian ? "ще дрімає — " : "dormant — ");
            text.Append("\n<color=#BB88EE>• ").Append(level).Append(" — ").Append(state)
                .Append(description).Append("</color>");
        }

        private static void AddUnavailableLine(System.Text.StringBuilder text, string description)
        {
            text.Append("\n<color=#BB88EE>• ").Append(description).Append("</color>");
        }
        private static bool IsWeaponOrTool(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Shield:
                    return true;
                default: return false;
            }
        }
        private static bool IsRelevant(Skills.SkillType skill)
        {
            switch (skill)
            {
                case Skills.SkillType.Swords: case Skills.SkillType.Axes: case Skills.SkillType.Clubs:
                case Skills.SkillType.Knives: case Skills.SkillType.Spears: case Skills.SkillType.Polearms:
                case Skills.SkillType.Bows: case Skills.SkillType.Crossbows: case Skills.SkillType.Pickaxes:
                case Skills.SkillType.Blocking: return true;
                default: return false;
            }
        }
    }
}
