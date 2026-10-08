using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Native ravens own spawning, dialogue and the seen-tutorial save state.
    // Keep an unread lesson in ordinary character customData so logging out
    // before listening cannot silently lose the one-shot introduction.
    internal static class MasteryRavenTutorials
    {
        private const string Prefix = "vm.raven.lesson.";
        private static Player Owner;
        private static float Next, NotBefore;
        internal static void Unlock(Player player, Skills.SkillType skill, int milestone)
        {
            if (player != Player.m_localPlayer || player?.m_customData == null) return;
            if (milestone == 35 || milestone == 70)
            {
                Queue(player, "general" + milestone, "general:" + milestone);
                if (skill == Skills.SkillType.ElementalMagic || skill == Skills.SkillType.BloodMagic)
                    Queue(player, "magic" + milestone, "magic:" + milestone);
            }
            else if (milestone == 100)
            {
                Queue(player, "general100", "general:100");
                Queue(player, "gold" + (int)skill, "gold:" + (int)skill);
            }
            NotBefore = Time.time + MasteryMilestonePresentation.Duration + .5f;
        }
        private static void Queue(Player player, string key, string lesson)
        {
            string id = Prefix + key;
            if (!player.HaveSeenTutorial(id)) player.m_customData[id] = lesson;
        }
        internal static void Tick(Player player)
        {
            if (!MagicSkillPassives.OwnerReady(player) || Tutorial.instance == null) return;
            if (Owner != player) { Owner = player; Next = Time.time + 3f; NotBefore = Next; }
            if (Time.time < Next || Time.time < NotBefore || MasteryMilestonePresentation.IsActive ||
                PatronAscensionPresentation.IsPendingOrActive) return;
            Next = Time.time + 3f;
            if (player.IsTeleporting() || player.InCutscene()) return;
            bool ua = string.Equals(Localization.instance?.GetSelectedLanguage(), "Ukrainian", StringComparison.OrdinalIgnoreCase);
            var remove = new List<string>();
            int added = 0;
            foreach (var pair in player.m_customData)
            {
                if (!pair.Key.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                if (player.HaveSeenTutorial(pair.Key)) { remove.Add(pair.Key); continue; }
                // General mechanic first; don't let simultaneous Hugin/Munin
                // lessons compete, including after dictionary reload from save.
                if (pair.Value.StartsWith("magic:", StringComparison.Ordinal) &&
                    !player.HaveSeenTutorial(Prefix + "general" + pair.Value.Substring(6))) continue;
                if (pair.Value.StartsWith("gold:", StringComparison.Ordinal) &&
                    !player.HaveSeenTutorial(Prefix + "general100")) continue;
                if (++added > 29) break; // Five introductions plus at most 24 skill lessons.
                string topic = ua ? "Майстерність" : "Mastery", text = null;
                bool munin = pair.Value.StartsWith("magic:", StringComparison.Ordinal) || pair.Value.EndsWith(":100", StringComparison.Ordinal);
                if (pair.Value == "general:35")
                    text = ua ? "Твоя перша бронзова майстерність! Навичка тепер відкриває окремий прийом, а не лише підсилює знайому дію. Заглянь у майстерність у вікні навичок: там знайдеш, що пробудилося і як цим скористатися." :
                        "Your first bronze mastery! A skill now unlocks a distinct technique, not just a stronger familiar action. Open mastery in the skills window to learn what awakened and how to use it.";
                else if (pair.Value == "general:70")
                    text = ua ? "Срібна майстерність змінює звичні прийоми. Один дар спрацьовує сам, інший потребує твого наказу. Опис у вікні навичок підкаже, як розбудити силу; не кожен дар є звичайним ударом." :
                        "Silver mastery changes familiar techniques. Some gifts answer on their own; others need your command. The skills window explains how to awaken each one—not every gift is an ordinary strike.";
                else if (pair.Value == "magic:35")
                    text = ua ? "Магія впізнала твій почерк. Кожен посох розвиває власне закляття: вогонь визріває, холод сковує, а кров підтримує життя й мертву свиту. Поглянь на фіолетовий опис посоха — там його нове призначення." :
                        "Magic knows your hand. Each staff develops its own spell: fire ripens, cold binds, and blood sustains life and a dead retinue. The purple staff inscription reveals its new calling.";
                else if (pair.Value == "magic:70")
                    text = ua ? "Вища магія відповідає на спецатаку посоха. Пробуджені посохи мають різні великі закляття: перевір їхній фіолетовий опис і дай силі окремий наказ, а не звичайний удар." :
                        "Greater magic answers a staff's secondary attack. Awakened staves carry different greater spells: read the purple inscription and give that power its own command, not an ordinary strike.";
                else if (pair.Value == "general:100")
                    text = ua ? "Золота майстерність привертає погляд богів. Це не просто більша сила: її дар має власне призначення та умови. Прислухайся до пояснення саме своєї навички — божественні дари не однакові." :
                        "Gold mastery draws the gods' attention. Its gift has its own purpose and conditions, not merely greater strength. Listen to your skill's own lesson: divine gifts are not all alike.";
                else if (pair.Value.StartsWith("gold:", StringComparison.Ordinal) &&
                    int.TryParse(pair.Value.Substring(5), out int value))
                {
                    foreach (PerkDefinition perk in PerkCatalog.Get((Skills.SkillType)value))
                        if (perk.Milestone == 100)
                        { topic = PerkLocalization.Localize(perk.NameToken); text = PerkNarrativeService.Description(perk); munin = true; break; }
                }
                if (!string.IsNullOrWhiteSpace(text))
                    Tutorial.instance.SpawnRaven(pair.Key, topic, text, topic, munin);
            }
            foreach (string key in remove) player.m_customData.Remove(key);
        }
    }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class MasteryRavenTutorialTick
    { private static void Postfix(Player __instance) => MasteryRavenTutorials.Tick(__instance); }
}
