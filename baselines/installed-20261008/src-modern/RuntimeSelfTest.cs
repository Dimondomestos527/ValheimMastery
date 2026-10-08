using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class RuntimeSelfTest
    {
        private static bool _done;
        private static readonly string[] RequiredRecipes =
        {
            "axes_35_stack1", "axes_35_stack2", "axes_35_stack3", "spears_35_launch", "spears_35_impact", "spears_70_pin",
            "polearms_35_windup",
            "run_35", "run_70_stack1", "run_70_stack2", "run_70_stack3", "woodcutting_35",
            "run_70_water_start", "run_70_water_step", "run_70_water_end", "jump_70_air", "jump_70_save", "sneak_70_veil",
            "clubs_35_counter", "clubs_35_counter_stagger", "clubs_70_windup", "clubs_70", "clubs_70_crit", "knives_35_ready", "knives_35_step", "knives_35_step_end",
            "axes_35_ready", "axes_35_strike", "axes_70_ready", "axes_70_windup", "axes_70", "blocking_35_pressure",
            "blocking_70_block", "blocking_70_parry", "blocking_70_projectile", "knives_35_blink", "knives_35_blink_hit",
            "polearms_70", "crafting_35", "crafting_70", "cooking_35", "cooking_70",
            "pickaxes_70", "pickaxes_70_collapse", "pickaxes_70_secondary"
        };

        private static readonly string[] RequiredPrefabs =
        {
            "sfx_arrow_hit", "sfx_atgeir_attack", "sfx_atgeir_attack_secondary", "sfx_axe_hit", "sfx_axe_swing",
            "sfx_bow_fire", "sfx_club_hit", "sfx_club_swing", "sfx_dodge", "sfx_eat",
            "sfx_land_water", "sfx_gui_craftitem_forge", "sfx_knife_swing", "sfx_metal_shield_blocked",
            "sfx_perfect_dodge", "sfx_perfectblock", "sfx_pickable_pick", "sfx_Potion_eitr_minor",
            "sfx_rock_destroyed", "sfx_smelter_produce", "sfx_spear_hit", "sfx_sword_hit", "sfx_tree_fall",
            "sfx_unarmed_hit", "fx_bloodweapon_hit", "sfx_fenring_jump_start", "sfx_fenring_jump_trigger",
            "sfx_fenring_claw_hit", "sfx_frozenking_punchaoe_punch_third",
            "vfx_arrowhit", "vfx_BloodHit", "vfx_Burning", "vfx_bush_leaf_puff_heath",
            "fx_perfectdodge", "vfx_ForgeAddFuel", "vfx_Frost", "vfx_HitSparks", "fx_Lightning",
            "vfx_MeadSwimmer", "vfx_perfectblock", "vfx_pickable_pick", "vfx_Place_forge", "vfx_Poison",
            "vfx_Potion_eitr_minor", "vfx_RockDestroyed", "vfx_RockHit", "vfx_tree_fall_hit",
            "fx_float_hitwater", "fx_WaterImpact_Big", "fx_land", "fx_goblinbrute_groundslam",
            "fx_fallenfalkyrie_spin", "fx_lightningweapon_hit", "fx_eikthyr_stomp", "vfx_RockDestroyed_large",
            "sfx_thunder", "vfx_ShadowPerson_hit", "vfx_ShadowPerson_death", "vfx_odin_despawn",
            "Wraith", "vfx_wraith_hit", "vfx_wraith_death", "sfx_wraith_attack", "sfx_wraith_attack_hit", "sfx_wraith_death"
        };

        internal static void Tick()
        {
            if (_done || ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null || ZNetScene.instance.m_prefabs.Count < 100) return;
            _done = true;
            Run();
        }

        private static void Run()
        {
            int skills = 0;
            int perks = 0;
            int missingLocalization = 0;
            foreach (Skills.SkillType skill in Enum.GetValues(typeof(Skills.SkillType)))
            {
                if (!PerkCatalog.Contains(skill)) continue;
                skills++;
                foreach (PerkDefinition perk in PerkCatalog.Get(skill))
                {
                    perks++;
                    missingLocalization += IsMissing(perk.NameToken) ? 1 : 0;
                    missingLocalization += IsMissing(perk.DescriptionToken) ? 1 : 0;
                    missingLocalization += IsMissing(perk.FlavorToken) ? 1 : 0;
                }
            }

            List<string> missingPrefabs = new List<string>();
            foreach (string name in RequiredPrefabs)
                if (NativePerkAssetResolver.Resolve(name) == null) missingPrefabs.Add(name);

            int harmonyMethods = Harmony.GetAllPatchedMethods().Count(method =>
            {
                Patches patches = Harmony.GetPatchInfo(method);
                return patches != null && patches.Owners.Contains(MasteryPlugin.Guid);
            });

            List<string> missingRecipes = RequiredRecipes.Where(id => !VfxRecipeService.TryGet(id, out VfxRecipe ignored)).ToList();

            bool catalogPass = skills == 24 && perks == 72 && missingLocalization == 0;
            bool prefabPass = missingPrefabs.Count == 0;
            bool harmonyPass = harmonyMethods >= 25;
            MasteryPlugin.Log.LogInfo("[SelfTest] Catalog=" + (catalogPass ? "PASS" : "FAIL") +
                " Skills=" + skills + " Perks=" + perks + " MissingLocalization=" + missingLocalization);
            MasteryPlugin.Log.LogInfo("[SelfTest] Harmony=" + (harmonyPass ? "PASS" : "FAIL") + " PatchedMethods=" + harmonyMethods);
            MasteryPlugin.Log.LogInfo("[SelfTest] VFX/SFX=" + (prefabPass ? "PASS" : "FAIL") +
                " Required=" + RequiredPrefabs.Length + " Missing=" + (missingPrefabs.Count == 0 ? "none" : string.Join(",", missingPrefabs)));
            MasteryPlugin.Log.LogInfo("[SelfTest] VFX recipes=" + (missingRecipes.Count == 0 ? "PASS" : "FAIL") +
                " Required=" + RequiredRecipes.Length + " Missing=" + (missingRecipes.Count == 0 ? "none" : string.Join(",", missingRecipes)));
            if (missingPrefabs.Count > 0) LogCandidates();
            MasteryPlugin.Log.LogInfo("[SelfTest] CombatScaling Default=" + MasteryRuntime.CombatDamageSkillScalingDefault.ToString("0.##") +
                " Bow=" + MasteryRuntime.CombatDamageSkillScalingBow.ToString("0.##") +
                " Club=" + MasteryRuntime.CombatDamageSkillScalingClub.ToString("0.##") +
                " ClubStaggerPerLevel=" + MasteryRuntime.ClubStaggerPerSkillLevel.ToString("0.####"));
        }

        private static void LogCandidates()
        {
            string[] tokens = { "arrow", "atgeir", "swim", "water", "blood", "bow", "dodge", "lightning" };
            foreach (string token in tokens)
            {
                string[] names = ZNetScene.instance.m_prefabs.Where(prefab => prefab != null &&
                    prefab.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(prefab => prefab.name).Distinct().OrderBy(name => name).Take(40).ToArray();
                MasteryPlugin.Log.LogInfo("[SelfTest] Candidates " + token + "=" + (names.Length == 0 ? "none" : string.Join(",", names)));
            }
        }
        private static bool IsMissing(string token)
        {
            if (Localization.instance == null) return true;
            string localized = Localization.instance.Localize(token);
            return string.IsNullOrEmpty(localized) || localized == token;
        }
    }
}
