using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal enum VfxAnchor { WorldPosition, PlayerRoot, PlayerHands, Weapon, Projectile, HitPoint, TargetCenter, TargetFeet, TargetBone, StaticAnchor, Ground }

    internal sealed class VfxLayer
    {
        internal string PrefabName;
        internal string NativeLandingCue;
        internal string NativeMovementCue;
        internal bool LegacyRunPresentation;
        internal VfxAnchor Anchor = VfxAnchor.WorldPosition;
        internal Vector3 Offset;
        internal Quaternion RotationOffset = Quaternion.identity;
        internal float Delay;
        internal float RootScale = 1f;
        internal float ParticleSizeMultiplier = 1f;
        internal float ParticleSpeedMultiplier = 1f;
        internal float LifetimeMultiplier = 1f;
        internal float EmissionMultiplier = 1f;
        internal float LightIntensityMultiplier = 1f;
        internal bool DisableCamShaker = true;
        internal bool DisableBuiltinAudio = true;
    }

    internal sealed class VfxRecipe
    {
        internal string Id;
        internal readonly List<VfxLayer> Layers = new List<VfxLayer>();
        internal string AudioPrefab;
        internal float AudioThrottle = 0.12f;
        internal float AudioVolume = 1f;
    }

    internal static class VfxRecipeService
    {
        private static readonly Dictionary<string, VfxRecipe> Recipes = new Dictionary<string, VfxRecipe>(StringComparer.Ordinal);
        static VfxRecipeService()
        {
            Add("axes_35_stack1", "vfx_HitSparks", 0.55f, 0.65f, 0.72f, 0.55f, "sfx_axe_hit");
            Add("axes_35_stack2", "vfx_HitSparks", 0.95f, 1.05f, 0.78f, 0.90f, "sfx_axe_hit");
            Add("axes_35_stack3", "vfx_BloodHit", 1.15f, 1.20f, 0.82f, 1.00f, "sfx_axe_hit");
            Add("spears_35_launch", "vfx_HitSparks", 0.28f, 0.35f, 0.55f, 0.35f, "sfx_spear_throw");
            Add("spears_35_impact", "vfx_arrowhit", 0.48f, 0.55f, 0.70f, 0.45f, "sfx_spear_hit");
            Add("spears_70_pin", "vfx_arrowhit", 0.60f, 0.62f, 0.72f, 0.55f, "sfx_spear_hit");
            Add("spears_70_hook_start", "vfx_arrowhit", 0.28f, 0.30f, 0.35f, 0.35f, "sfx_spear_throw");
            Add("polearms_35_windup", "vfx_HitSparks", 0.40f, 0.40f, 0.30f, 0.35f, "sfx_atgeir_attack");
            Add("woodcutting_35", "vfx_tree_fall_hit", 1.0f, 1.15f, 0.82f, 1.25f, "sfx_tree_fall");
            Add("swords_70_windup", "vfx_HitSparks", 1.25f, 1.30f, 0.85f, 1.15f, "sfx_sword_swing");
            Add("clubs_35_counter", "vfx_HitSparks", 0.55f, 0.55f, 0.65f, 0.48f, "sfx_club_hit");
            Add("clubs_35_counter_stagger", "vfx_HitSparks", 0.35f, 0.34f, 0.45f, 0.30f, "sfx_club_hit");
            Add("clubs_35", "vfx_HitSparks", 0.90f, 1.05f, 1f, 0.90f, "sfx_club_hit");
            Add("clubs_70_windup", "vfx_HitSparks", 0.68f, 0.70f, 0.72f, 0.60f, "sfx_club_swing");
            Add("clubs_70", "vfx_HitSparks", 1.60f, 1.70f, 0.90f, 1.35f, "sfx_club_hit");
            Add("clubs_70_crit", "vfx_HitSparks", 2.15f, 2.25f, 0.95f, 1.65f, "sfx_smelter_produce");
            Add("knives_35_ready", "fx_perfectdodge", 0.28f, 0.32f, 0.55f, 0.24f, "sfx_knife_swing");
            Add("knives_35_step", "fx_perfectdodge", 0.72f, 0.68f, 1.10f, 0.52f, "sfx_dodge");
            Add("knives_35_step_end", "fx_perfectdodge", 0.32f, 0.36f, 0.65f, 0.28f, "sfx_dodge");
            Add("blocking_70", "vfx_perfectblock", 0.60f, 0.60f, 0.65f, 0.50f, "sfx_metal_shield_blocked");
            Add("axes_35_ready", "vfx_HitSparks", 1.15f, 1.25f, 0.85f, 1.35f, "sfx_axe_swing");
            Add("axes_35_strike", "vfx_BloodHit", 1.35f, 1.40f, 0.95f, 1.45f, "sfx_axe_hit");
            Add("axes_70_ready", "vfx_BloodHit", 0.48f, 0.52f, 0.58f, 0.38f, "sfx_axe_hit");
            Add("axes_70_windup", "vfx_HitSparks", 0.88f, 0.95f, 1.10f, 0.82f, "sfx_axe_swing");
            Add("axes_70", "vfx_BloodHit", 1.20f, 1.30f, 1.15f, 1.10f, "sfx_axe_hit");
            Add("blocking_35_pressure", "vfx_perfectblock", 0.90f, 1.00f, 0.65f, 0.95f, "sfx_perfectblock");
            Add("blocking_35_reflect", "vfx_perfectblock", 0.82f, 0.88f, 0.70f, 0.86f, "sfx_perfectblock");
            Add("blocking_70_block", "vfx_perfectblock", 0.78f, 0.82f, 0.70f, 0.80f, "sfx_metal_shield_blocked");
            Add("blocking_70_parry", "vfx_perfectblock", 0.90f, 0.95f, 0.75f, 0.85f, "sfx_perfectblock");
            Add("blocking_70_projectile", "vfx_perfectblock", 0.85f, 0.90f, 0.72f, 0.90f, "sfx_perfectblock");
            Add("knives_35_blink_hit", "vfx_HitSparks", 0.48f, 0.55f, 1.25f, 0.44f, "sfx_knife_swing");
            Add("knives_35_blink", "fx_perfectdodge", 0.62f, 0.65f, 1.15f, 0.48f, "sfx_dodge");
            Add("polearms_70", "vfx_HitSparks", 1.20f, 1.35f, 0.95f, 1.25f, "sfx_atgeir_attack_secondary");
            Add("crafting_70", "vfx_Place_forge", 1.35f, 1.35f, 0.80f, 1.20f, "sfx_gui_craftitem_forge");
            Add("cooking_35", "vfx_MeadSwimmer", 1.05f, 1.15f, 0.80f, 1.10f, "sfx_eat");
            Add("cooking_70", "vfx_MeadSwimmer", 1.10f, 1.15f, 0.85f, 1.20f, "sfx_eat");

            // Physical ore fracture with one electrical accent; full collapse gets a distinct
            // native Eikthyr shockwave and thunder. Secondary chips never repeat the thunder.
            Add("pickaxes_70", "vfx_RockHit", 1.25f, 1f, 1f, 1f, "sfx_rock_destroyed");
            Layer("pickaxes_70", "fx_lightningweapon_hit", 0.50f, new Vector3(0f, 0.15f, 0f));
            Add("pickaxes_70_collapse", "vfx_RockDestroyed_large", 0.80f, 1f, 1f, 1f, "sfx_thunder");
            Recipes["pickaxes_70_collapse"].AudioThrottle = 0.8f;
            Recipes["pickaxes_70_collapse"].AudioVolume = 0.65f;
            Layer("pickaxes_70_collapse", "fx_eikthyr_stomp", 0.42f, Vector3.zero);
            Layer("pickaxes_70_collapse", "VM_ThunderFlash", 1f, new Vector3(0f, 4f, 0f));
            Layer("pickaxes_70_collapse", "fx_lightningweapon_hit", 1.1f, new Vector3(0f, 0.35f, 0f));
            Add("pickaxes_70_secondary", "vfx_RockHit", 0.55f, 1f, 1f, 0.7f, null);

            // These existing triggers use their own game's material language, not a generic
            // shield flash or shower of blood for every unrelated non-combat action.
            Add("crafting_35", "vfx_Place_workbench", 0.65f, 1f, 1f, 0.65f, "sfx_gui_craftitem_workbench_end");
            Add("farming_35", "vfx_pickable_pick", 0.85f, 1f, 1f, 1f, null);
            Add("woodcutting_35_small", "vfx_tree_fall_hit", 0.75f, 1f, 1f, 1.5f, "sfx_axe_hit");
            Add("swim_35", "fx_float_hitwater", 0.35f, 0.7f, 1f, 0.45f, "sfx_land_water");
            Add("swim_70", "fx_float_hitwater", 0.65f, 1f, 1f, 0.8f, "sfx_land_water");
            Add("fishing_35", "fx_float_hitwater", 0.8f, 1f, 1f, 1f, "sfx_land_water");
            Add("fishing_70", "fx_float_hitwater", 0.5f, 1f, 1f, 1f, null);
            Add("sneak_35", "vfx_bush_leaf_puff_heath", 0.65f, 1f, 1f, 0.55f, "sfx_dodge");
            Add("dodge_35", "fx_perfectdodge", .65f, .8f, 1f, .65f, "sfx_perfect_dodge");
            Add("dodge_70", "vfx_HitSparks", .65f, .85f, 1f, .65f, "sfx_perfect_dodge");
            // Restore the approved 1.4.105 phase-only Run appearance. The native
            // character effect owns color/scaling/cap; no sustained wind wake.
            Add("run_35", "fx_land", 0.9f, 1f, 1.1f, 0.75f, "sfx_dodge");
            Recipes["run_35"].Layers[0].LegacyRunPresentation = true;
            AddLegacyRun("run_70_stack1", .35f, .8f, 1.1f, 1f);
            AddLegacyRun("run_70_stack2", .5f, .9f, 1.2f, 1.75f);
            AddLegacyRun("run_70_stack3", .7f, 1f, 1.4f, 2.5f);
            Recipes["run_70_stack1"].AudioVolume = .65f;
            Recipes["run_70_stack2"].AudioVolume = .8f;
            Recipes["run_70_stack3"].AudioVolume = 1f;
            Add("run_70_water_start", "fx_float_hitwater", .7f, 1f, 1f, .8f, "sfx_land_water");
            Add("run_70_water_end", "fx_float_hitwater", .5f, 1f, 1f, .7f, "sfx_land_water");
            Add("run_70_water_step", "fx_float_hitwater", .23f, .65f, .8f, .3f, null);
            AddMovement("jump_70_air", 1.15f, .6f, .25f);
            Add("jump_70_save", "fx_land", .95f, 1f, 1.3f, .85f, "sfx_dodge");
            Add("sneak_70_veil", "fx_perfectdodge", .45f, .6f, .5f, .6f, "sfx_dodge");
            Recipes["sneak_70_veil"].AudioVolume = .35f;
            Recipes["jump_70_air"].AudioVolume = .45f;
        }

        private static void Layer(string id, string prefab, float scale, Vector3 offset)
        {
            Recipes[id].Layers.Add(new VfxLayer { PrefabName = prefab, RootScale = scale, Offset = offset });
        }

        private static void AddMovement(string id, float scale, float emission, float height)
        {
            Add(id, "fx_perfectdodge", scale, 1f, 1f, emission, "sfx_dodge");
            Recipes[id].Layers[0].NativeMovementCue = id;
            Recipes[id].Layers[0].Offset = Vector3.up * height;
        }

        private static void AddLegacyRun(string id, float scale, float size, float speed, float emission)
        {
            Add(id, "fx_land", scale, size, speed, emission, "sfx_dodge");
            Recipes[id].Layers[0].LegacyRunPresentation = true;
        }

        private static void Add(string id, string prefab, float scale, float size, float speed, float emission, string audio)
        {
            VfxRecipe recipe = new VfxRecipe { Id = id, AudioPrefab = audio };
            recipe.Layers.Add(new VfxLayer { PrefabName = prefab, NativeLandingCue = prefab == "fx_land" ? id : null,
                RootScale = scale, ParticleSizeMultiplier = size, ParticleSpeedMultiplier = speed, EmissionMultiplier = emission });
            Recipes[id] = recipe;
        }

        internal static bool TryGet(string id, out VfxRecipe recipe) => Recipes.TryGetValue(id, out recipe);
        internal static IEnumerable<string> GetIds() => Recipes.Keys;

        internal static bool Play(string id, Player player, Vector3 position)
        {
            if (!Recipes.TryGetValue(id, out VfxRecipe recipe)) return false;
            if (MasteryPlugin.Settings.EnablePerkProcVFX.Value)
            {
                int runPhase = id == "run_70_stack1" ? 1 : id == "run_70_stack2" ? 2 : id == "run_70_stack3" ? 3 : 0;
                if (runPhase > 0) RoadRhythmPhaseVisual.Set(player, runPhase);
                else foreach (VfxLayer layer in recipe.Layers) Spawn(layer, ResolveAnchor(layer.Anchor, player, position), ResolveRotation(layer, player));
            }
            if (!string.IsNullOrEmpty(recipe.AudioPrefab))
                PerkAudioService.Play(id, recipe.AudioPrefab, position, recipe.AudioThrottle, volumeScale: recipe.AudioVolume);
            return true;
        }

        internal static Vector3 ResolveAnchor(VfxAnchor anchor, Player player, Vector3 fallback)
        {
            if (player == null) return fallback;
            if (anchor == VfxAnchor.PlayerRoot) return player.transform.position + Vector3.up * 0.9f;
            // Hands/weapon intentionally resolve from player space: item models vary across all axe types.
            if (anchor == VfxAnchor.PlayerHands || anchor == VfxAnchor.Weapon) return player.GetCenterPoint() + player.transform.forward * 0.35f;
            if (anchor == VfxAnchor.TargetFeet || anchor == VfxAnchor.Ground) return fallback + Vector3.down * 0.6f;
            return fallback;
        }

        private static Quaternion ResolveRotation(VfxLayer layer, Player player)
        {
            Quaternion baseRotation = player != null ? Quaternion.LookRotation(player.transform.forward, Vector3.up) : Quaternion.identity;
            return baseRotation * (layer?.RotationOffset ?? Quaternion.identity);
        }

        internal static GameObject Spawn(VfxLayer layer, Vector3 position, Quaternion rotation = default)
        {
            if (layer == null || string.IsNullOrWhiteSpace(layer.PrefabName) ||
                !MasteryPlugin.Settings.EnablePerkProcVFX.Value || Player.m_localPlayer == null) return null;
            GameObject prefab = layer.LegacyRunPresentation ? NativePerkAssetResolver.ResolveLegacyRun() : NativePerkAssetResolver.Resolve(layer.PrefabName);
            if (prefab == null)
            {
                NativeVfxSafeFrame.Request(layer.LegacyRunPresentation ? "fx_perfectdodge" : layer.PrefabName,
                    loaded => SpawnResolved(layer, loaded, position, rotation) != null);
                return null;
            }
            return SpawnResolved(layer, prefab, position, rotation);
        }
        private static GameObject SpawnResolved(VfxLayer layer, GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value || Player.m_localPlayer == null) return null;
            if (rotation == default) rotation = Quaternion.identity;
            float leaseSeconds = Mathf.Max(0.5f, 1.2f * layer.LifetimeMultiplier + layer.Delay);
            GameObject instance = VfxPool.Spawn(prefab, position + layer.Offset, rotation, leaseSeconds);
            Tune(instance, layer);
            if (layer.LegacyRunPresentation)
            {
                // Replay the native105 scheduler only after sanitation. Do not
                // race a baseline auto-restart against a manually guessed burst.
                NativeLandingBurst.Schedule(instance, prefab, layer.EmissionMultiplier, layer.NativeLandingCue, true);
                instance?.SetActive(true);
                return instance;
            }
            if (!string.IsNullOrEmpty(layer.NativeMovementCue))
            {
                NativeMovementPulse.Schedule(instance, prefab, layer.EmissionMultiplier, layer.NativeMovementCue);
                instance?.SetActive(true);
                return instance;
            }
            instance?.SetActive(true);
            instance?.GetComponent<VfxPoolBaseline>()?.RestartParticles();
            if (!string.IsNullOrEmpty(layer.NativeLandingCue))
                NativeLandingBurst.Schedule(instance, prefab, layer.EmissionMultiplier, layer.NativeLandingCue, layer.LegacyRunPresentation);
            return instance;
        }

        internal static void Tune(GameObject instance, VfxLayer layer)
        {
            if (instance == null || layer == null) return;
            VfxPoolBaseline baseline = instance.GetComponent<VfxPoolBaseline>();
            Transform scaledRoot = layer.LegacyRunPresentation && baseline?.NativeRoot != null ? baseline.NativeRoot : instance.transform;
            if (layer.LegacyRunPresentation && scaledRoot != instance.transform)
            {
                // Old VfxPool assigned world position/rotation directly to the
                // native prefab root. The wrapper now owns those coordinates.
                scaledRoot.localPosition = Vector3.zero; scaledRoot.localRotation = Quaternion.identity;
            }
            scaledRoot.localScale *= Mathf.Max(0.01f, layer.RootScale);
            foreach (ParticleSystem particles in baseline?.Particles ?? instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.startSizeMultiplier *= Mathf.Max(0.01f, layer.ParticleSizeMultiplier);
                main.startSpeedMultiplier *= Mathf.Max(0.01f, layer.ParticleSpeedMultiplier);
                main.startLifetimeMultiplier *= Mathf.Max(0.01f, layer.LifetimeMultiplier);
                ParticleSystem.EmissionModule emission = particles.emission;
                emission.rateOverTimeMultiplier *= Mathf.Max(0f, layer.EmissionMultiplier);
            }
            foreach (Light light in baseline?.Lights ?? instance.GetComponentsInChildren<Light>(true)) light.intensity *= Mathf.Max(0f, layer.LightIntensityMultiplier);
            baseline?.TuneBurstEmission(Mathf.Max(0f, layer.EmissionMultiplier));
            foreach (AudioSource audio in baseline?.Audio ?? instance.GetComponentsInChildren<AudioSource>(true))
                if (audio != null && layer.DisableBuiltinAudio) audio.enabled = false;
            if (layer.DisableCamShaker)
            {
                if (baseline != null)
                {
                    foreach (Behaviour behaviour in baseline.CamShakers) if (behaviour != null) behaviour.enabled = false;
                }
                else
                {
                    foreach (Component component in instance.GetComponentsInChildren<Component>(true))
                        if (component is Behaviour shaker && component.GetType().Name.IndexOf("CamShaker", StringComparison.OrdinalIgnoreCase) >= 0) shaker.enabled = false;
                }
            }
        }
    }
}
