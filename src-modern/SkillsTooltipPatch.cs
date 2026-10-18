using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    /// <summary>
    /// Rebinds mastery text both when the Skills dialog is built and while it remains open.
    /// Valheim refreshes/recycles skill rows after Setup, so Setup-only binding loses hover text.
    /// </summary>
    [HarmonyPatch(typeof(SkillsDialog), "Setup")]
    internal static class SkillsTooltipPatch
    {
        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(SkillsDialog), "m_elements");
        private static bool _hierarchyLogged;
        private static bool _fallbackWarningLogged;

        private static void Postfix(SkillsDialog __instance, Player player) => Apply(__instance, player);

        internal static void Apply(SkillsDialog dialog, Player player)
        {
            if (player == null || dialog == null || !MasteryPlugin.Settings.EnableMasterySkillUI.Value)
                return;
            try
            {
                List<Skills.Skill> skills = player.GetSkills().GetSkillList();
                List<GameObject> elements = ElementsField?.GetValue(dialog) as List<GameObject>;
                if (elements == null)
                    throw new InvalidOperationException("SkillsDialog.m_elements was not available.");

                if (MasteryPlugin.Settings.UIDebugLogging.Value && !_hierarchyLogged)
                {
                    _hierarchyLogged = true;
                    MasteryPlugin.Log.LogInfo("Skills UI: detected " + elements.Count + " rows for " + skills.Count + " skills.");
                    if (elements.Count > 0)
                        LogHierarchy(elements[0].transform, "");
                }

                int count = Mathf.Min(skills.Count, elements.Count);
                for (int i = 0; i < count; i++)
                {
                    Skills.Skill skill = skills[i];
                    if (skill == null || skill.m_info == null || !PerkCatalog.Contains(skill.m_info.m_skill))
                        continue;
                    UITooltip tooltip = elements[i].GetComponentInChildren<UITooltip>(true);
                    if (tooltip == null)
                        continue;
                    if (tooltip.GetComponent<MasteryHudTooltipTag>() == null)
                        tooltip.gameObject.AddComponent<MasteryHudTooltipTag>();

                    string vanillaTopic = string.IsNullOrWhiteSpace(tooltip.m_topic) ? skill.m_info.m_skill.ToString() : tooltip.m_topic;
                    tooltip.Set(vanillaTopic + " — " + Mathf.FloorToInt(skill.m_level), BuildText(player, skill), dialog.m_tooltipAnchor, Vector2.zero);
                }
            }
            catch (Exception exception)
            {
                if (_fallbackWarningLogged)
                    return;
                _fallbackWarningLogged = true;
                MasteryPlugin.Log.LogWarning("Mastery Skills UI could not be attached; the vanilla Skills window remains unchanged. " + exception.Message);
            }
        }

        internal static string BuildText(Player player, Skills.Skill skill)
        {
            // Mastery has its own window; native skill hover shows the same concise skill description.
            if (skill?.m_info == null) return string.Empty;
            return SkillProgressionTooltipText.Describe(skill.m_info.m_skill,
                PerkLocalization.Localize(skill.m_info.m_description), player);
        }

        private static void LogHierarchy(Transform transform, string indent)
        {
            MasteryPlugin.Log.LogInfo("Skills UI hierarchy: " + indent + transform.name + " [" + transform.GetType().Name + "]");
            for (int i = 0; i < transform.childCount; i++)
                LogHierarchy(transform.GetChild(i), indent + "  ");
        }
    }

    [HarmonyPatch(typeof(SkillsDialog), "Update")]
    internal static class SkillsTooltipRefreshPatch
    {
        private static void Postfix(SkillsDialog __instance)
        {
            // Once per ~0.25 sec avoids hammering tooltip layout while still surviving vanilla row refreshes.
            if (Time.frameCount % 15 == 0)
                SkillsTooltipPatch.Apply(__instance, Player.m_localPlayer);
        }
    }
}
