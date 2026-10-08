using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    /// <summary>
    /// One-time personal restoration for an already progressed world. The key is held in the
    /// character save, scopes itself to the vanilla world UID and never assigns raw skill levels.
    /// </summary>
    internal static class LegacyCatchUpService
    {
        private const int Version = 3;
        private const float MinimumEligibleLevel = 10f;
        private static readonly HashSet<long> QueuedPlayers = new HashSet<long>();
        private static readonly Dictionary<long, float> EligibleAt = new Dictionary<long, float>();

        internal static void Queue(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
                return;
            long id = player.GetPlayerID();
            QueuedPlayers.Add(id);
            EligibleAt[id] = Time.time + 3f;
        }

        internal static void Tick(Player player)
        {
            if (player == null || player != Player.m_localPlayer || !MasteryRuntime.LegacyCatchUpEnabled)
                return;
            ZNet net = ZNet.instance;
            if (net == null || net.GetWorld() == null)
                return;

            // Global keys are vanilla-synchronised to every connected client. Do not block the one-time
            // restoration behind our optional custom RPC handshake; this is essential for platform clients.
            WorldProgressionXpService.Refresh();

            long id = player.GetPlayerID();
            if (!QueuedPlayers.Contains(id) || !EligibleAt.TryGetValue(id, out float at) || Time.time < at)
                return;
            QueuedPlayers.Remove(id);
            EligibleAt.Remove(id);
            Apply(player, net.GetWorldUID());
        }

        private static void Apply(Player player, long worldUid)
        {
            string key = "valheim_mastery.catchup." + Version + "." + worldUid;
            if (player.m_customData.ContainsKey(key))
                return;

            int target = GetTargetLevel(WorldProgressionXpService.GetCurrentProgress());
            List<string> summary = new List<string>();
            if (target > 0)
            {
                foreach (Skills.SkillType skillType in EnumerateSupportedSkills())
                {
                    Skills.Skill skill = player.GetSkills().GetSkill(skillType);
                    if (skill == null || skill.m_level <= MinimumEligibleLevel || skill.m_level >= target)
                        continue;

                    int before = Mathf.FloorToInt(skill.m_level);
                    float rawMissingXp = MissingXpToTarget(skill, target);
                    float awardedXp = rawMissingXp * MasteryRuntime.LegacyCatchUpScale;
                    AddRawXp(skill, awardedXp);
                    int after = Mathf.FloorToInt(skill.m_level);
                    if (after > before)
                        summary.Add(LocalSkillName(skillType) + ": " + before + " -> " + after);
                }
            }

            // Mark even if no skill qualified: the character has received this world's one-time evaluation.
            player.m_customData[key] = player.GetPlayerID().ToString();
            player.UpdateStats();

            if (summary.Count > 0)
            {
                StringBuilder message = new StringBuilder("Досвід минулих пригод відновлено");
                int shown = Mathf.Min(5, summary.Count);
                for (int i = 0; i < shown; i++)
                    message.Append("\n").Append(summary[i]);
                if (summary.Count > shown)
                    message.Append("\n+ ").Append(summary.Count - shown).Append(" навичок");
                player.Message(MessageHud.MessageType.Center, message.ToString(), 0, player.m_textIcon);
            }

            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("Legacy catch-up: world=" + worldUid + ", player=" + player.GetPlayerID() +
                    ", target=" + target + ", changed=" + summary.Count + ".");
        }

        private static int GetTargetLevel(BossProgressSnapshot progress)
        {
            if (progress == null)
                return 0;
            if (progress.IsDefeated(BossId.Fader)) return 70;
            if (progress.IsDefeated(BossId.Queen)) return 60;
            int early = 0;
            foreach (BossId boss in new[] { BossId.Eikthyr, BossId.Elder, BossId.Bonemass, BossId.Moder, BossId.Yagluth })
                if (progress.IsDefeated(boss)) early++;
            switch (early)
            {
                case 1: return 15;
                case 2: return 25;
                case 3: return 35;
                case 4: return 42;
                case 5: return 50;
                default: return 0;
            }
        }

        private static IEnumerable<Skills.SkillType> EnumerateSupportedSkills()
        {
            foreach (Skills.SkillType skill in new[] {
                Skills.SkillType.Cooking, Skills.SkillType.Crafting, Skills.SkillType.Farming, Skills.SkillType.Fishing,
                Skills.SkillType.Jump, Skills.SkillType.Pickaxes, Skills.SkillType.Ride, Skills.SkillType.Run,
                Skills.SkillType.Sneak, Skills.SkillType.Swim, Skills.SkillType.WoodCutting, Skills.SkillType.Swords,
                Skills.SkillType.Axes, Skills.SkillType.Clubs, Skills.SkillType.Knives, Skills.SkillType.Spears,
                Skills.SkillType.Polearms, Skills.SkillType.Bows, Skills.SkillType.Crossbows, Skills.SkillType.Unarmed,
                Skills.SkillType.Blocking, Skills.SkillType.Dodge, Skills.SkillType.ElementalMagic, Skills.SkillType.BloodMagic })
                yield return skill;
        }

        private static float Compensation(Skills.SkillType skill)
        {
            switch (skill)
            {
                case Skills.SkillType.Cooking:
                case Skills.SkillType.Crafting:
                case Skills.SkillType.Farming:
                case Skills.SkillType.Swim:
                case Skills.SkillType.Fishing:
                case Skills.SkillType.Ride: return 1.00f;
                case Skills.SkillType.Pickaxes:
                case Skills.SkillType.WoodCutting:
                case Skills.SkillType.Sneak: return 0.80f;
                case Skills.SkillType.Run:
                case Skills.SkillType.Jump:
                case Skills.SkillType.Blocking:
                case Skills.SkillType.Dodge: return 0.60f;
                case Skills.SkillType.BloodMagic: return 0.60f;
                case Skills.SkillType.ElementalMagic: return 0.75f;
                default: return 0.50f;
            }
        }

        private static float MissingXpToTarget(Skills.Skill skill, int target)
        {
            float total = 0f;
            float level = skill.m_level;
            float accumulator = skill.m_accumulator;
            while (level < target)
            {
                float requirement = Mathf.Pow(Mathf.Floor(level + 1f), 1.5f) * 0.5f;
                total += Mathf.Max(0f, requirement - accumulator);
                level += 1f;
                accumulator = 0f;
            }
            return total;
        }

        private static void AddRawXp(Skills.Skill skill, float rawXp)
        {
            const float epsilon = 0.0001f;
            if (skill == null || rawXp <= 0f || skill.m_info == null || skill.m_info.m_increseStep <= 0f || Game.m_skillGainRate <= 0f)
                return;
            float perFactor = skill.m_info.m_increseStep * Game.m_skillGainRate;
            while (rawXp > epsilon && skill.m_level < 100f)
            {
                float room = Mathf.Max(0f, skill.GetNextLevelRequirement() - skill.m_accumulator);
                float spent = Mathf.Min(rawXp, room);
                if (spent <= epsilon)
                    break;
                bool completesLevel = spent >= room - epsilon;
                skill.Raise((spent + (completesLevel ? epsilon : 0f)) / perFactor);
                rawXp -= spent;
            }
        }

        private static string LocalSkillName(Skills.SkillType skill)
        {
            return PerkLocalization.Localize("$skill_" + skill.ToString().ToLowerInvariant());
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class LegacyCatchUpSpawnPatch
    {
        private static void Postfix(Player __instance) => LegacyCatchUpService.Queue(__instance);
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class LegacyCatchUpTickPatch
    {
        private static void Postfix(Player __instance) => LegacyCatchUpService.Tick(__instance);
    }
}
