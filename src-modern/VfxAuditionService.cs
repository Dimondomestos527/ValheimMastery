using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimMastery
{
    internal static class VfxAuditionService
    {
        private static readonly List<GameObject> Candidates = new List<GameObject>();
        private static int Selected;
        private static float Scale = 1f;
        private static float ParticleSize = 1f;
        private static float ParticleSpeed = 1f;
        private static float Lifetime = 1f;
        private static float Emission = 1f;
        private static bool StripCamShaker = true;
        private static bool EnableAudio = false;
        internal static void Register() => new Terminal.ConsoleCommand("vm_vfx", "vm_vfx list <filter> | play <prefab> | recipe <id> | inspect <id> | scale <value> | next | prev | shaker on/off", Execute, false);

        internal static void ExecuteAlias(Terminal.ConsoleEventArgs args)
        {
            if (args.Args.Length < 3) { args.Context.AddString("Usage: vm vfx recipe <id> | inspect <id> | play <prefab>"); return; }
            string action = args.Args[2].ToLowerInvariant();
            if (action == "recipe") { if (args.Args.Length < 4 || !VfxRecipeService.TryGet(args.Args[3], out VfxRecipe recipe)) { args.Context.AddString("Usage: vm vfx recipe <known-id>"); return; } Player player = Player.m_localPlayer; if (player == null) return; VfxRecipeService.Play(recipe.Id, player, player.GetCenterPoint() + player.transform.forward * 3f); args.Context.AddString("Played recipe " + recipe.Id); return; }
            if (action == "inspect") { if (args.Args.Length < 4 || !VfxRecipeService.TryGet(args.Args[3], out VfxRecipe recipe)) { args.Context.AddString("Usage: vm vfx inspect <known-id>"); return; } foreach (VfxLayer layer in recipe.Layers) args.Context.AddString(recipe.Id + ": " + layer.PrefabName + " root=" + layer.RootScale + " size=" + layer.ParticleSizeMultiplier + " speed=" + layer.ParticleSpeedMultiplier); return; }
            if (action == "play") { if (args.Args.Length < 4) { args.Context.AddString("Usage: vm vfx play <prefab>"); return; } GameObject prefab = ZNetScene.instance?.GetPrefab(args.Args[3]); if (prefab == null) { args.Context.AddString("VFX prefab not found: " + args.Args[3]); return; } Play(args, prefab); return; }
            args.Context.AddString("Use vm_vfx list/scale/next/prev for interactive audition.");
        }
        private static void Execute(Terminal.ConsoleEventArgs args)
        {
            if (args.Args.Length < 2) { args.Context.AddString("vm_vfx list <filter> | play <prefab> | scale <0.25-2> | next | prev | shaker on/off"); return; }
            string action = args.Args[1].ToLowerInvariant();
            if (action == "list") { List(args, args.Args.Length >= 3 ? args.Args[2] : ""); return; }
            if (action == "scale" || action == "rootscale") { if (!TryFactor(args, out float value)) return; Scale = value; args.Context.AddString("VFX root scale=" + Scale.ToString("0.##")); return; }
            if (action == "particlesize") { if (!TryFactor(args, out float value)) return; ParticleSize = value; args.Context.AddString("VFX particle size=" + ParticleSize.ToString("0.##")); return; }
            if (action == "speed") { if (!TryFactor(args, out float value)) return; ParticleSpeed = value; args.Context.AddString("VFX particle speed=" + ParticleSpeed.ToString("0.##")); return; }
            if (action == "lifetime") { if (!TryFactor(args, out float value)) return; Lifetime = value; args.Context.AddString("VFX lifetime=" + Lifetime.ToString("0.##")); return; }
            if (action == "emission") { if (!TryFactor(args, out float value)) return; Emission = value; args.Context.AddString("VFX emission=" + Emission.ToString("0.##")); return; }
            if (action == "audio") { EnableAudio = args.Args.Length >= 3 && string.Equals(args.Args[2], "on", StringComparison.OrdinalIgnoreCase); args.Context.AddString("VFX builtin audio=" + (EnableAudio ? "on" : "off")); return; }
            if (action == "shaker") { StripCamShaker = args.Args.Length < 3 || !string.Equals(args.Args[2], "off", StringComparison.OrdinalIgnoreCase); args.Context.AddString("CamShaker stripping=" + (StripCamShaker ? "on" : "off")); return; }
            if (action == "next" || action == "prev") { if (Candidates.Count == 0) { args.Context.AddString("Run vm_vfx list <filter> first."); return; } Selected = (Selected + (action == "next" ? 1 : Candidates.Count - 1)) % Candidates.Count; Play(args, Candidates[Selected]); return; }
            if (action == "play") { if (args.Args.Length < 3) { args.Context.AddString("Usage: vm_vfx play <prefab>"); return; } GameObject prefab = ZNetScene.instance?.GetPrefab(args.Args[2]); if (prefab == null) { args.Context.AddString("VFX prefab not found: " + args.Args[2]); return; } Play(args, prefab); return; }
            if (action == "recipe") { if (args.Args.Length < 3 || !VfxRecipeService.TryGet(args.Args[2], out VfxRecipe recipe)) { args.Context.AddString("Usage: vm_vfx recipe <known-id>"); return; } VfxRecipeService.Play(recipe.Id, Player.m_localPlayer, Player.m_localPlayer.GetCenterPoint() + Player.m_localPlayer.transform.forward * 3f); args.Context.AddString("Played recipe " + recipe.Id + " layers=" + recipe.Layers.Count); return; }
            if (action == "inspect") { if (args.Args.Length < 3 || !VfxRecipeService.TryGet(args.Args[2], out VfxRecipe recipe)) { args.Context.AddString("Usage: vm_vfx inspect <known-id>"); return; } foreach (VfxLayer layer in recipe.Layers) args.Context.AddString(recipe.Id + ": " + layer.PrefabName + " root=" + layer.RootScale + " size=" + layer.ParticleSizeMultiplier + " speed=" + layer.ParticleSpeedMultiplier + " lifetime=" + layer.LifetimeMultiplier + " emission=" + layer.EmissionMultiplier); return; }
            args.Context.AddString("Unknown vm_vfx action: " + action);
        }

        private static bool TryFactor(Terminal.ConsoleEventArgs args, out float value)
        {
            value = 1f;
            if (args.Args.Length < 3 || !float.TryParse(args.Args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
            { args.Context.AddString("Usage: vm_vfx <rootscale|particlesize|speed|lifetime|emission> <0.05-3>"); return false; }
            value = Mathf.Clamp(value, 0.05f, 3f);
            return true;
        }

        private static void List(Terminal.ConsoleEventArgs args, string filter)
        {
            Candidates.Clear();
            List<GameObject> prefabs = ZNetScene.instance?.m_prefabs;
            if (prefabs != null) foreach (GameObject prefab in prefabs) if (prefab != null && prefab.name.IndexOf(filter ?? "", StringComparison.OrdinalIgnoreCase) >= 0) Candidates.Add(prefab);
            Candidates.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase)); Selected = 0;
            args.Context.AddString("VFX matches=" + Candidates.Count + " filter='" + filter + "'");
            foreach (GameObject prefab in Candidates.Take(40)) args.Context.AddString(prefab.name);
            if (Candidates.Count > 40) args.Context.AddString("… use vm_vfx next/prev to audition remaining entries.");
        }

        private static void Play(Terminal.ConsoleEventArgs args, GameObject prefab)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return; }
            Vector3 point = player.transform.position + player.transform.forward * 3f + Vector3.up * 0.15f;
            GameObject instance = VfxRecipeService.Spawn(new VfxLayer { PrefabName = prefab.name, RootScale = Scale, ParticleSizeMultiplier = ParticleSize, ParticleSpeedMultiplier = ParticleSpeed, LifetimeMultiplier = Lifetime, EmissionMultiplier = Emission }, point);
            if (instance == null) { args.Context.AddString("No safe visual preview available: " + prefab.name); return; }
            if (EnableAudio) PerkAudioService.Play("vfx_audition_audio", prefab.name, point, 0.1f);
            int particles = instance.GetComponentsInChildren<ParticleSystem>(true).Length, lights = instance.GetComponentsInChildren<Light>(true).Length, audio = instance.GetComponentsInChildren<AudioSource>(true).Length, shakers = 0;
            foreach (Component component in instance.GetComponentsInChildren<Component>(true)) if (component != null && component.GetType().Name.IndexOf("CamShaker", StringComparison.OrdinalIgnoreCase) >= 0) { shakers++; if (StripCamShaker && component is Behaviour behaviour) behaviour.enabled = false; }
            args.Context.AddString("VFX: " + prefab.name + " scale=" + Scale.ToString("0.##") + " particles=" + particles + " lights=" + lights + " audio=" + audio + " CamShaker=" + shakers + (StripCamShaker ? " (disabled)" : ""));
        }
    }
}
