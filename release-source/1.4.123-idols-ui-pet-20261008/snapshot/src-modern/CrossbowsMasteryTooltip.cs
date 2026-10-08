using System;
using HarmonyLib;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(int) })]
    internal static class CrossbowsMasteryTooltip
    {
        private static void Postfix(ItemDrop.ItemData __instance, ref string __result)
        {
            if (__instance?.m_shared?.m_skillType != Skills.SkillType.Crossbows || string.IsNullOrEmpty(__result)) return;
            bool ua = Localization.instance != null &&
                string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", StringComparison.OrdinalIgnoreCase);
            string marker = ua ? "<color=#D6B47A>Майстерність арбалета</color>" : "<color=#D6B47A>Crossbow Mastery</color>";
            if (__result.Contains("<color=#D6B47A>Майстерність арбалета</color>") ||
                __result.Contains("<color=#D6B47A>Crossbow Mastery</color>")) return;
            bool ready = PerkRuntimeService.HasPerk(Player.m_localPlayer, Skills.SkillType.Crossbows, 35);
            __result += "\n\n" + marker + "\n<color=#D6B47A>35 — " +
                (ready ? "" : (ua ? "ще дрімає — " : "dormant — ")) +
                (ua ? "Поки перезаряджаєш: +20 броні, +40% поточної броні та подвоєна межа оглушення." :
                    "While actively reloading: +20 armor, +40% current armor and double stagger capacity.") + "</color>";
            bool turret = PerkRuntimeService.HasPerk(Player.m_localPlayer, Skills.SkillType.Crossbows, 70);
            __result += "\n<color=#D6B47A>70 — " + (turret ? "" : (ua ? "ще дрімає — " : "dormant — ")) +
                (ua ? "Польова турель. Вторинна атака з арбалетом — платформа. Поклади свій арбалет і запас болтів у контейнер. Порожні руки + вторинна атака — запуск / зупинка. Забери вміст, повтори для прибирання." :
                    "Field turret. Crossbow secondary deploys the platform. Supply your crossbow and a reserve of bolts through its container. Empty hands + secondary starts / stops it. Retrieve cargo, then repeat to clear the platform.") + "</color>";
        }
    }
}
