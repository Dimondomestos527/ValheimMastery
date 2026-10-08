using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ValheimMastery
{
    internal static partial class PerkLocalization
    {
        private static readonly MethodInfo AddWordMethod = AccessTools.Method(typeof(Localization), "AddWord");

        internal static void Register(Localization localization)
        {
            if (localization == null || AddWordMethod == null)
                return;

            string language = localization.GetSelectedLanguage();
            Dictionary<string, string> words = new Dictionary<string, string>(StringComparer.Ordinal);
            AddCommonWords(words, string.Equals(language, "Ukrainian", StringComparison.OrdinalIgnoreCase));
            AddUkrainianPerkWords(words);
            if (!string.Equals(language, "Ukrainian", StringComparison.OrdinalIgnoreCase))
            {
                words["vm_perk_fists_100_name"] = "Tyr's Oath";
                words["vm_perk_fists_100_desc"] = "Tyr honours a will that does not break under blows. His recognition lets you equip two compatible trinkets together: when the shared adrenaline bar fills, both activate at once.";
                words["vm_perk_fists_100_flavor"] = "Two gifts. One unbroken oath.";
                words["vm_perk_polearms_100_name"] = "The All-Father's Will";
                words["vm_perk_polearms_100_desc"] = "The All-Father saw in your whirlwind a will capable of turning the tide of battle. Only while continuously spinning your atgeir, you reflect hostile arrows and other supported projectiles.";
                words["vm_perk_polearms_100_flavor"] = "A hostile shot does not have the final word.";
                words["vm_perk_crossbows_35_name"] = "Iron Composure";
                words["vm_perk_crossbows_35_desc"] = "While actively reloading a crossbow: +20 armor, +40% current armor and double stagger capacity. Protection ends when the reload ends or is interrupted. Crossbows retain 85% of the native skill-derived damage contribution, not 85% final damage.";
                words["vm_perk_crossbows_35_flavor"] = "A heavy shot rewards the one who survives preparing it.";
                words["vm_perk_crossbows_70_name"] = "Personal Field Turret";
                words["vm_perk_crossbows_70_desc"] = "Deploy one personal turret with crossbow secondary attack. Supply your crossbow and as many compatible bolts as fit in its container. Empty hands + secondary starts or stops it. It searches the forward firing sector for hostile creatures while its owner is alive and online; never players or tames. Retrieve the same weapon and remaining bolts from the stopped container, then clear the empty platform.";
                words["vm_perk_crossbows_70_flavor"] = "A siege engine begins with a weapon worth installing.";
                words["vm_perk_dodge_70_name"] = "Not This Time";
                words["vm_perk_pickaxes_70_desc"] = "A pickaxe strike sometimes damages neighbouring pieces of the same vein. Rarely, a mighty strike crumbles the entire deposit with subterranean thunder.";
                words["vm_perk_dodge_70_desc"] = "A real perfect dodge restores half the stamina spent on that roll. The refund can occur once every 8 seconds.";
                words["vm_perk_dodge_70_flavor"] = "The world tried. It missed.";
                words["vm_perk_crafting_35_name"] = "Waste Not";
                words["vm_perk_crafting_35_desc"] = "Equipment upgrades have a 35% chance to consume no resources. Each interaction loads raw material or fuel up to one quarter of total station capacity, rounded up and including the first item; free space, player resources and valid input type limit the amount.";
                words["vm_perk_crafting_35_flavor"] = "If the hammer struck true, why waste another piece of iron?";
                words["vm_perk_crafting_100_name"] = "Völundr’s Recognition";
                words["vm_perk_crafting_100_desc"] = "Völundr recognizes your mastery and grants Favor. Channel it through Divine Inspiration to create Masterwork equipment. Master Idols may yet have a place in your craft; the Forge of Potential hints at further possibilities.";
                words["vm_perk_crafting_100_desc_pending"] = "Völundr recognizes your mastery. Gold Mastery is being prepared within Phase 100-A; divine actions and Idols are not available yet.";
                words["vm_perk_crafting_100_flavor"] = "Mastery is forged in more than iron.";
                words["vm_item_master_cooked"] = "Jarl's stomach: this meal loses nourishment more slowly";
                words["vm_item_master_mead"] = "Masterfully brewed: sustained effects last 40% longer; recovery mead cooldowns are 40% shorter";
#if MASTERY_CLUBS35_EXPERIMENT
                words["vm_perk_clubs_35_desc"] = "A mace secondary that finishes a small foe hurls the body into the next enemy. A hammer strike leaves a weaker echo in the ground.";
#endif
#if MASTERY_CLUBS70_EXPERIMENT
                words["vm_perk_clubs_70_desc"] = "Hold block with a mace or hammer to gather a charge. A mace secondary gains damage and pushback, and a slain small foe flies faster and deals much greater collision damage; no mace echo. A full hammer charge leaves two weaker echoes at 1.25-second intervals.";
#endif
#if MASTERY_SHIELD35_EXPERIMENT
                words["vm_perk_blocking_35_name"] = "Wrong Address";
                words["vm_perk_blocking_35_desc"] = "Perfect projectile parries return the original damage at increased speed. An unbroken tower-shield block halves projectile stagger and suppresses its status payload instead. Persistent clouds and environmental hazards are not reflected.";
#endif
#if MASTERY_SHIELD_RUSH_EXPERIMENT
                words["vm_perk_blocking_70_name"] = "Shield Rush";
                words["vm_perk_blocking_70_desc"] = "Block and dodge to rush without direct damage or dodge invulnerability. Bucklers move faster and farther; tower shields push a wider corridor with stronger stagger and force. Your stagger pressure strengthens the impact.";
#endif
                words["vm_perk_elementalmagic_35_name"] = "Path of Two Elements";
                words["vm_perk_elementalmagic_35_desc"] = "A fire staff gathers heat: a longer charge strengthens its flames, blast and burning. An ice staff leaves frost that wounds and slows foes. Frostbitten flying prey descends and stays low until the cold fades; bosses resist this pressure.";
                words["vm_perk_elementalmagic_70_name"] = "Spells of the Nine Worlds";
                words["vm_perk_elementalmagic_70_desc"] = "A fire staff summons Zharik, a fiery companion who fights at your side. A stronger staff empowers him; another call brings back and heals the same living companion. An ice staff raises a storm that wounds and binds foes; another call relocates it.";
                words["vm_perk_bloodmagic_35_name"] = "The Dead Keep Pace";
                words["vm_perk_bloodmagic_35_desc"] = "A skeleton staff gathers a retinue of skeletons around you. A protection staff sustains its shield bearer and lets you replenish unbroken shields with your own lifeblood. Each successive second of renewal consumes more health.";
                words["vm_perk_bloodmagic_70_desc"] = "A skeleton staff summons Torba, a hardy bearer of portal-safe cargo who avoids battle and recovers in safety. A protection staff binds a foe in a blood cage; when its bonds burst, the shield's strength punishes the captive.";
                words["vm_perk_spears_70_desc"] = "Your thrown spear attaches to an enemy. With your hand free, secondary attack pulls a small enemy towards you, or pulls you towards a heavy enemy or boss. Kick is disabled while attached; facing the target is unnecessary. Pull speed scales up to +100% at skill100. An obstacle stops movement without breaking the tether: reposition and press again. Failed motion refunds its cooldown; a successful pull has a12-second cooldown. Returning or losing the spear, death of either character, or player teleportation ends the tether. Block returns the same spear.";
                words["vm_perk_bows_70_name"] = "Through and Through";
                words["vm_perk_bows_70_desc"] = "Hold a fully drawn bow until it signals readiness: the empowered arrow strikes harder, flies faster and pierces enemies. A weak-point hit preserves its momentum, while its wake knocks back foes behind the target.";
                words["vm_perk_bows_70_flavor"] = "One line. Many targets.";
                words["vm_perk_swim_35_name"] = "Sea-Hardened";
                words["vm_perk_swim_35_desc"] = "Swimming is 20% faster and costs 25% less stamina. After leaving the water, Wet rapidly counts down to 30 seconds; it is not removed instantly.";
                words["vm_perk_swim_35_flavor"] = "Cold water no longer drags the master down; it only leaves drops on the cloak.";
                words["vm_perk_swim_70_name"] = "Tidal Strength";
                words["vm_perk_swim_70_desc"] = "Swimming is 40% faster overall. After 1 second nearly motionless in water, stamina regenerates at 40% of normal land regeneration; movement interrupts it. Below 15% stamina while swimming, restore 25% of maximum stamina. Reserve cooldown: 3 minutes.";
                words["vm_perk_swim_70_flavor"] = "The sea grants respite to those who learned to ride its waves.";
                words["vm_perk_swim_100_name"] = "Aqua Viking";
                words["vm_perk_swim_100_desc"] = "While in water without movement input, stamina immediately regenerates at 40% of normal. This perk does not enable bow use while swimming.";
                words["vm_perk_swim_100_flavor"] = "The sea wanted unarmed prey. It chose the wrong Viking.";
            }
            foreach (KeyValuePair<string, string> word in words)
                AddWordMethod.Invoke(localization, new object[] { word.Key, word.Value });
        }

        internal static string Localize(string token)
        {
            if (string.Equals(token, "$vm_perk_crafting_100_desc", StringComparison.Ordinal) &&
                !GoldCraftingService.DivineActionsReady)
                token = "$vm_perk_crafting_100_desc_pending";
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        private static void AddCommonWords(Dictionary<string, string> words, bool ukrainian)
        {
            words["vm_mastery_title"] = ukrainian ? "Майстерність" : "Mastery";
            words["vm_mastery_next"] = ukrainian ? "До наступного перка: {0} рів." : "Next perk: {0} levels";
            words["vm_mastery_complete"] = ukrainian ? "Усі перки відкрито" : "All perks unlocked";
            words["vm_mastery_rank_35"] = ukrainian ? "Досвідчений" : "Experienced";
            words["vm_mastery_rank_70"] = ukrainian ? "Адепт" : "Adept";
            words["vm_mastery_rank_100"] = ukrainian ? "Майстер" : "Master";
            words["vm_mastery_unlocked_message"] = "{0}: {1} — {2}";
        }

        static partial void AddUkrainianPerkWords(Dictionary<string, string> words);
    }

    [HarmonyPatch(typeof(Localization), "SetupLanguage")]
    internal static class MasteryLocalizationPatch
    {
        private static void Postfix(Localization __instance) => PerkLocalization.Register(__instance);
    }
}
