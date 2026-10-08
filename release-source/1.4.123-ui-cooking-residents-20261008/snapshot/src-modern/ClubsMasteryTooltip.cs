#if MASTERY_CLUBS35_EXPERIMENT || MASTERY_CLUBS70_EXPERIMENT
using System.Text;
using HarmonyLib;

namespace ValheimMastery
{
    // Club content only. Keep the shared item tooltip and magic owner's staff
    // descriptions untouched; unknown clubs/tools get no speculative perk text.
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(int) })]
    internal static class ClubsMasteryTooltip
    {
        private const string Color = "#D6B47A";
        private static void Postfix(ItemDrop.ItemData __instance, ref string __result)
        {
            if (__instance?.m_shared == null || string.IsNullOrEmpty(__result)) return;
            ClubWeaponClass kind = ClubWeaponClassService.Classify(__instance);
            if (kind != ClubWeaponClass.Mace && kind != ClubWeaponClass.SledgeHammer) return;
            bool ua = Localization.instance != null &&
                string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", System.StringComparison.OrdinalIgnoreCase);
            if (__result.Contains("<color=" + Color + ">Майстерність булав і молотів</color>") ||
                __result.Contains("<color=" + Color + ">Club Mastery</color>")) return;
            var text = new StringBuilder();
            text.Append("\n\n<color=").Append(Color).Append('>').Append(ua ? "Майстерність булав і молотів" : "Club Mastery").Append("</color>");
            bool mace = kind == ClubWeaponClass.Mace;
#if MASTERY_CLUBS35_EXPERIMENT
            AddLine(text, 35, ua, mace
                ? (ua ? "Сильний удар, що добиває ворога, кидає його тіло в інших ворогів." :
                    "A finishing secondary strike hurls the fallen body into other foes.")
                : (ua ? "Через 1,25 с удар відлунює слабшим поштовхом у центрі зони удару." :
                    "After 1.25s, a weaker shock echoes at the heart of the strike's impact area."));
#endif
#if MASTERY_CLUBS70_EXPERIMENT
            AddLine(text, 70, ua, mace
                ? (ua ? "Міць, накопичена під блоком, посилює оглушення. Сильний удар додає шкоду й відкидання; добитий ворог летить швидше та завдає значно більше шкоди при зіткненні." :
                    "Might stored while blocking strengthens stagger. A secondary strike gains damage and pushback; a fallen foe flies faster and deals much greater collision damage.")
                : (ua ? "Міць, накопичена під блоком, посилює удар, оглушення й відкидання. Повний заряд залишає два повторні поштовхи." :
                    "Might stored while blocking strengthens damage, stagger and pushback. A full charge leaves two delayed shocks."));
#endif
            __result += text.ToString();
        }
        private static void AddLine(StringBuilder text, int level, bool ua, string description)
        {
            bool unlocked = PerkRuntimeService.HasPerk(Player.m_localPlayer, Skills.SkillType.Clubs, level);
            text.Append("\n<color=").Append(Color).Append(">• ").Append(level).Append(" — ")
                .Append(unlocked ? "" : (ua ? "ще дрімає — " : "dormant — ")).Append(description).Append("</color>");
        }
    }
}
#endif
