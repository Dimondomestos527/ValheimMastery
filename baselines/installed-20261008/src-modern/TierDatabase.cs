using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Vanilla 1.0 progression tiers. A value of one is a deliberate safe fallback for modded or unknown prefabs.
    internal static class TierDatabase
    {
        private sealed class TargetData
        {
            internal readonly int Tier;
            internal readonly float DodgeThreat;
            internal TargetData(int tier, float dodgeThreat) { Tier = tier; DodgeThreat = dodgeThreat; }
        }

        private static readonly Dictionary<string, int> ItemTiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, TargetData> Targets = new Dictionary<string, TargetData>(StringComparer.OrdinalIgnoreCase);

        static TierDatabase()
        {
            // Meadows / Black Forest / Swamp / Mountains / Plains / Mistlands / Ashlands.
            AddItems(1, "Club", "KnifeFlint", "SpearFlint", "Bow", "AxeStone", "AxeFlint", "PickaxeAntler", "ShieldWood", "ShieldWoodTower", "ShieldBoneTower", "ShieldBronzeBuckler");
            AddItems(2, "SwordBronze", "AxeBronze", "PickaxeBronze", "MaceBronze", "AtgeirBronze", "SpearBronze", "KnifeCopper", "SpearChitin", "BowFineWood", "ShieldBanded", "ShieldIronSquare", "ShieldIronTower");
            AddItems(3, "SwordIron", "AxeIron", "PickaxeIron", "MaceIron", "SledgeIron", "AtgeirIron", "BattleaxeIron", "KnifeIron", "SpearElderBark", "BowHuntsman", "ShieldRoot");
            AddItems(4, "SwordSilver", "AxeSilver", "PickaxeStone", "MaceSilver", "SledgeSilver", "KnifeSilver", "SpearWolfFang", "BowDraugrFang", "ShieldSilver", "ShieldSerpentscale", "ShieldIronBuckler");
            AddItems(5, "SwordBlackmetal", "AxeBlackMetal", "PickaxeBlackMetal", "MaceNeedle", "AtgeirBlackmetal", "KnifeBlackMetal", "SpearCarapace", "BowSpineSnap", "ShieldBlackmetal", "ShieldBlackmetalTower");
            AddItems(6, "SwordMistwalker", "SwordKrom", "AxeJotunBane", "PickaxeJotunBane", "MaceJotunBane", "AtgeirHimminAfl", "KnifeSkollAndHati", "CrossbowArbalest", "ShieldCarapace", "ShieldCarapaceBuckler", "ShieldCarapaceTower");
            AddItems(7, "SwordNiedhogg", "SwordDyrnwyn", "THSwordSlayer", "AxeBlood", "AxeStorm", "PickaxeAshlands", "MaceEldner", "AtgeirGold", "SpearGold", "SpearSplitner", "KnifeVoid", "BowAshlands", "CrossbowRipper", "CrossbowRipperLightning", "ShieldFlametal", "ShieldFlametalTower", "ShieldBlood", "ShieldAshlands");

            // Common aliases retained because prefab names changed across the 1.0 content passes.
            AddItems(4, "AxeJotunBane", "ShieldSilverBuckler");
            AddItems(5, "SpearCarapace", "CrossbowArbalest");

            // Exact current prefab aliases for every physical weapon and shield family.
            AddItems(1, "AtgeirWood", "AxeEarly", "AxeWood", "BattleaxeWood", "Bow", "Club", "KnifeWood", "MaceWood", "PickaxeAntler", "SledgeWood", "SpearWood", "SwordWood", "THSwordWood", "ShieldWood", "ShieldWoodTower", "ShieldBronzeBuckler", "PlayerUnarmed");
            AddItems(2, "AtgeirBronze", "AxeBronze", "Battleaxe", "BowFineWood", "KnifeChitin", "MaceBronze", "PickaxeBronze", "SpearBronze", "SwordBronze", "ShieldBanded", "ShieldIronSquare", "ShieldIronTower");
            AddItems(3, "AtgeirIron", "AxeIron", "BattleaxeCrystal", "BowHuntsman", "MaceIron", "PickaxeIron", "SledgeIron", "SledgeStagbreaker", "SpearElderbark", "SwordIron", "SwordIronFire", "ShieldRoots");
            AddItems(4, "AxeJotunBane", "BowDraugrFang", "FistFenrirClaw", "KnifeSilver", "MaceSilver", "SpearWolfFang", "SwordSilver", "ShieldSilver", "ShieldSerpentscale", "ShieldIronBuckler");
            AddItems(5, "AtgeirBlackmetal", "AxeBlackMetal", "BattleaxeBlackmetal", "BowSpineSnap", "KnifeBlackMetal", "MaceNeedle", "PickaxeBlackMetal", "SwordBlackmetal", "FistBjornClaw", "ShieldBlackmetal", "ShieldBlackmetalTower");
            AddItems(6, "AtgeirHimminAfl", "CrossbowArbalest", "FistBjornUndeadClaw", "KnifeSkollAndHati", "SledgeDemolisher", "SpearCarapace", "SwordMistwalker", "THSwordKrom", "ShieldCarapace", "ShieldCarapaceBuckler");
            AddItems(7, "AtgeirGold", "AtgeirGold_BloodLightning", "AtgeirGold_FrostFire", "AxeGold", "AxeGold_BloodLightning", "AxeGold_FrostFire", "AxeBerzerkr", "AxeBerzerkrBlood", "AxeBerzerkrLightning", "AxeBerzerkrNature", "BattleaxeGold", "BattleaxeGold_BloodLightning", "BattleaxeGold_FrostFire", "BowAshlands", "BowAshlandsBlood", "BowAshlandsRoot", "BowAshlandsStorm", "CrossbowGold", "CrossbowGold_BloodLightning", "CrossbowGold_FrostFire", "CrossbowRipper", "CrossbowRipperBlood", "CrossbowRipperLightning", "CrossbowRipperNature", "FistGold", "FistGold_BloodLightning", "FistGold_FrostFire", "KnifeGold", "KnifeGold_BloodLightning", "KnifeGold_FrostFire", "KnifeVoid", "MaceEldner", "MaceEldnerBlood", "MaceEldnerLightning", "MaceEldnerNature", "MaceGold", "MaceGold_BloodLightning", "MaceGold_FrostFire", "SledgeGold", "SledgeGold_BloodLightning", "SledgeGold_FrostFire", "SpearGold", "SpearGold_BloodLightning", "SpearGold_FrostFire", "SpearSplitner", "SpearSplitner_Blood", "SpearSplitner_Lightning", "SpearSplitner_Nature", "SwordDyrnwyn", "SwordNiedhogg", "SwordNiedhoggBlood", "SwordNiedhoggLightning", "SwordNiedhoggNature", "THSwordSlayer", "THSwordSlayerBlood", "THSwordSlayerLightning", "THSwordSlayerNature", "ShieldFlametal", "ShieldFlametalTower", "ShieldGold", "ShieldGoldBuckler", "ShieldGoldTower", "ShieldKnight");
            // Creature tier is biome progression; DodgeThreat is deliberately separate and models danger, not just biome.
            AddTargets(1, 1, "Boar", "Deer", "Neck", "Greyling", "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman", "Eikthyr", "Skeleton", "Ghost", "Beech", "Birch", "FirTree", "PineTree", "Oak", "MineRock_Stone", "MineRock_Tin", "MineRock_Copper");
            AddTargets(2, 2, "Troll", "Troll_Frost", "gd_king", "Draugr", "Draugr_Elite", "Draugr_Ranged", "Leech", "Wraith", "Surtling", "Abomination", "MineRock_Iron", "SwampTree1", "SwampTree2", "SwampTree3", "AncientTree");
            AddTargets(3, 3, "Wolf", "Fenring", "Ulv", "Cultist", "Bat", "Hatchling", "Dragon", "StoneGolem", "MineRock_Obsidian", "MineRock_Silver", "Obsidian", "Rock_Obsidian", "FirTree_oldLog", "PineTreeOLD");
            AddTargets(4, 4, "Goblin", "GoblinArcher", "GoblinBrute", "GoblinShaman", "Deathsquito", "Lox", "Growth", "GoblinKing", "Unbjorn", "Bjorn", "MineRock_Blackmetal", "BlackMetalScrap");
            AddTargets(5, 5, "Seeker", "SeekerBrood", "Tick", "Dverger", "DvergerMage", "DvergerMageFire", "DvergerMageSupport", "SeekerBrute", "Gjall", "SeekerQueen", "YggdrasilRoot", "YggdrasilWood", "MineRock_Softtissue", "MineRock_BlackMarble");
            AddTargets(6, 6, "Charred", "Charred_Melee", "Charred_Archer", "Charred_Mage", "Charred_Twitcher", "Charred_Warrior", "Charred_Marksmann", "Morgen", "Volture", "Askvin", "Fader", "MineRock_Flametal", "FlametalOre", "Flametal");

            // Variant, cave, ocean, quest and location prefabs use the tier of their actual combat encounter.
            AddTargets(1, 1, "Boar_piggy", "Boar_spiritcaller", "Deer_White", "Chicken", "Hen", "Hare", "Greyling", "Greydwarf_Frozen", "Greydwarf_Shaman_Frozen", "Skeleton_Meadows", "Skeleton_Meadows_noarcher");
            AddTargets(2, 2, "Blob", "BlobAspect", "BlobElite", "BlobFrost", "Draugr_sleeping", "Draugr_Elite_sleeping", "Draugr_Ranged_sleeping", "Leech_cave", "Skeleton_Swamps", "Skeleton_Swamps_noarcher", "Skeleton_Poison", "Ghost_old", "Ghost_sleeping", "Kvastur", "BogWitchKvastur");
            AddTargets(3, 3, "Bat_Swamp", "Fenring_Cultist", "Fenring_Cultist_Hildir", "Fenring_Cultist_Hildir_nochest", "Skeleton_Mountains", "Skeleton_Mountains_noarcher", "Wolf_cub", "Wolf_spiritcaller", "FrostWisp", "FrostWisp_Storm");
            AddTargets(4, 4, "BlobTar", "Blob_Tar", "Goblin_Gem", "GoblinBrute_Hildir", "GoblinBruteBros", "GoblinBruteBros_nochest", "GoblinShaman_Hildir", "GoblinShaman_Hildir_nochest", "GoblinShaman_Staff_Hildir", "Lox_Calf", "HildirsLox", "Halstein", "Bjorn_spiritcaller");
            AddTargets(5, 5, "DvergerMageIce", "DvergerAshlands", "Tick", "SeekerBrood", "Elaking", "ElakingMole", "Frysling", "Writhan");
            AddTargets(6, 6, "Asksvin_hatchling", "Morgen_NonSleeping", "FallenValkyrie", "FallenWarrior", "BonemawSerpent", "BlobLava", "BlobLava_explosion", "BlobMork", "BlobMorkMini");
            AddTargets(8, 8, "DvergerDeepNorth", "GoblinDeepNorth", "Skeleton_DeepNorth", "JotunWarrior", "JotunWarriorDualWield", "JotunWitch", "FrozenKing", "FrozenKing_p2", "FrozenKing_p3", "Frysling", "Writhan", "Elaking", "ElakingMole");
            AddTargets(4, 4, "Serpent", "Leviathan");
            // Elite threats and bosses are intentionally worth more dodge XP than ordinary biome inhabitants.
            SetDodgeThreat(6, "StoneGolem", "SeekerBrute", "Gjall", "Morgen", "Charred_Warrior", "Unbjorn", "Bjorn");
            SetDodgeThreat(3, "gd_king");
            AddTargets(3, 6, "Bonemass");
            SetDodgeThreat(9, "Dragon");
            SetDodgeThreat(12, "GoblinKing");
            SetDodgeThreat(15, "SeekerQueen");
            SetDodgeThreat(18, "Fader");
        }

        internal static int GetItemTier(ItemDrop.ItemData item)
        {
            if (item == null) return 1;
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared?.m_name;
            return LookupItem(prefab);
        }

        internal static int GetTargetTier(Component target) => target == null ? 1 : GetTargetTier(target.gameObject.name);
        internal static int GetTargetTier(Character target) => target == null ? 1 : GetTargetTier(target.gameObject.name);
        internal static int GetTargetTier(string raw) => Targets.TryGetValue(Normalize(raw), out TargetData data) ? data.Tier : 1;
        internal static float GetDodgeThreatMultiplier(Character target) => target != null && Targets.TryGetValue(Normalize(target.gameObject.name), out TargetData data) ? data.DodgeThreat : 1f;
        internal static int GetActionTier(Skills.SkillType skill) => 1;

        internal static float GetResourceValue(ItemDrop.ItemData item, int amount)
        {
            if (item == null || amount <= 0) return 0f;
            string key = Normalize(item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared?.m_name);
            int tier = GetResourceTier(key);
            return amount * (1f + 0.50f * (tier - 1));
        }

        internal static int GetResourceTier(string key)
        {
            if (key.IndexOf("Flametal", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Askvin", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Morgen", StringComparison.OrdinalIgnoreCase) >= 0) return 7;
            if (key.IndexOf("Carapace", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("BlackMarble", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Yggdrasil", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Sap", StringComparison.OrdinalIgnoreCase) >= 0) return 6;
            if (key.IndexOf("BlackMetal", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Linen", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Needle", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Lox", StringComparison.OrdinalIgnoreCase) >= 0) return 5;
            if (key.IndexOf("Silver", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Obsidian", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Wolf", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("FreezeGland", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (key.IndexOf("Iron", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("AncientBark", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Guck", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Entrails", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (key.IndexOf("Copper", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Tin", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Bronze", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("FineWood", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("CoreWood", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 1;
        }

        internal static string ItemKey(ItemDrop.ItemData item) => item == null ? "unarmed" : Normalize(item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared?.m_name);
        internal static string TargetKey(Component target) => target == null ? "action" : Normalize(target.gameObject.name);

        private static void AddItems(int tier, params string[] names)
        {
            foreach (string name in names) ItemTiers[Normalize(name)] = tier;
        }
        private static void AddTargets(int tier, float dodgeThreat, params string[] names)
        {
            foreach (string name in names) Targets[Normalize(name)] = new TargetData(tier, dodgeThreat);
        }
        private static void SetDodgeThreat(float threat, params string[] names)
        {
            foreach (string name in names)
            {
                string key = Normalize(name);
                if (Targets.TryGetValue(key, out TargetData old)) Targets[key] = new TargetData(old.Tier, threat);
                else Targets[key] = new TargetData(1, threat);
            }
        }
        private static int LookupItem(string raw)
        {
            string key = Normalize(raw);
            if (key.IndexOf("DeepNorth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("FrozenKing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("JotunWarrior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Frysling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Writhan", StringComparison.OrdinalIgnoreCase) >= 0) return 8;
            if (ItemTiers.TryGetValue(key, out int tier)) return tier;
            foreach (KeyValuePair<string, int> pair in ItemTiers)
                if (key.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0) return pair.Value;
            return 1;
        }
        private static string Normalize(string value) => (value ?? string.Empty).Replace("(Clone)", string.Empty).Trim();
    }
}
