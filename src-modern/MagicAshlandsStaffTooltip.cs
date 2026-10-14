using System;
using HarmonyLib;

namespace ValheimMastery
{
    // Ashlands item descriptions are separate from the shared milestone catalog.
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(int) })]
    internal static class MagicAshlandsStaffTooltip
    {
        private const string UaMarker = "<color=#BB88EE>Майстерність магії</color>";
        private const string EnMarker = "<color=#BB88EE>Magic Mastery</color>";

        private static void Postfix(ItemDrop.ItemData __instance, ref string __result)
        {
            if (__instance?.m_shared == null || string.IsNullOrEmpty(__result)) return;
            string prefab = PerkRuntimeService.ItemPrefabName(__instance);
            bool troll = prefab == "StaffRedTroll";
            if (!troll && prefab != "StaffClusterbomb" && prefab != "StaffLightning" && prefab != "StaffGreenRoots") return;
            Skills.SkillType skill = troll ? Skills.SkillType.BloodMagic : Skills.SkillType.ElementalMagic;
            if (__instance.m_shared.m_skillType != skill || __result.Contains(UaMarker) || __result.Contains(EnMarker)) return;

            bool ua = Localization.instance != null &&
                string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", StringComparison.OrdinalIgnoreCase);
            string description;
            switch (prefab)
            {
                case "StaffClusterbomb":
                    description = ua ?
                        "Заряджена патериця ламкості розсіює більше уламків із потужнішими й ширшими вибухами, ціною більшого ейтру." :
                        "A charged fracturing staff scatters more fragments with stronger, wider blasts, at greater eitr cost.";
                    break;
                case "StaffLightning":
                    description = ua ?
                        "Якщо більшість залпу Дандра влучає в одного ворога, від нього розлітаються додаткові снаряди до найближчих інших видимих ворогів." :
                        "If most of a Dundr volley hits one foe, extra projectiles scatter from that foe toward the nearest other unobstructed foes.";
                    break;
                case "StaffGreenRoots":
                    description = ua ?
                        "Кожна прикликана лоза після загибелі чи в’янення відростає лише раз і ненадовго б’є сильніше." :
                        "Each summoned vine regrows only once after death or withering and briefly strikes harder.";
                    break;
                default:
                    description = ua ?
                        "Посох тролів прикликає тимчасового союзника, який у цей час вразливіший; згодом його покора зникає. Загибель троля повторює вибух прикликання, небезпечний для всіх поруч." :
                        "The troll staff summons a temporary ally who is more vulnerable during that time; its allegiance then ends. Its death repeats the summoning blast, endangering everyone nearby.";
                    break;
            }

            bool unlocked = PerkRuntimeService.HasPerk(Player.m_localPlayer, skill, 35);
            string state = unlocked ? "" : (ua ? "ще дрімає — " : "dormant — ");
            __result += "\n\n" + (ua ? UaMarker : EnMarker) +
                "\n<color=#BB88EE>• 35 — " + state + description + "</color>";
        }
    }
}
