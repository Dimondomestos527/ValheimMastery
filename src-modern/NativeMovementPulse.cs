using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Actual fx_perfectdodge composition: one ring_gradient textured ring and
    // eighty pixel_additive stretched streaks. These are short transitions, not
    // an always-on wind trail. Native material, shape and fade curves survive.
    internal sealed class NativeMovementPulse : MonoBehaviour
    {
        private const int ParticleBudget = 81;
        private static readonly Dictionary<string, int> Traces = new Dictionary<string, int>();
        private static ZNetScene Scene;
        private ParticleSystem[] Systems;
        private int[] Counts;
        private string Cue;
        private readonly ParticleSystem.Particle[] Probe = new ParticleSystem.Particle[ParticleBudget];

        internal static void Preload() => NativeSoftVisualAssets.GetPrefab("fx_perfectdodge");

        internal static void PlayAirJump(Player player)
        {
            if (player == null || !VfxRecipeService.TryGet("jump_70_air", out VfxRecipe recipe) || recipe.Layers.Count == 0) return;
            // AV only: ForceJump, stamina, velocity and the one-jump guard belong
            // to the existing movement hook. Do not replay its audio/HUD here.
            VfxRecipeService.Spawn(recipe.Layers[0], player.transform.position);
        }

        internal static void Schedule(GameObject root, GameObject source, float multiplier, string cue)
        {
            if (root == null || source == null || source.name != "fx_perfectdodge") return;
            NativeMovementPulse pulse = root.GetComponent<NativeMovementPulse>() ?? root.AddComponent<NativeMovementPulse>();
            pulse.StopAllCoroutines(); pulse.Cue = cue;
            ParticleSystem[] native = source.GetComponentsInChildren<ParticleSystem>(true);
            pulse.Systems = root.GetComponentsInChildren<ParticleSystem>(true);
            pulse.Counts = new int[pulse.Systems.Length];
            NativeBurstPlayback.Mute(root);
            int remaining = ParticleBudget;
            int runPhase = cue == "run_70_stack1" ? 1 : cue == "run_70_stack2" ? 2 : cue == "run_70_stack3" ? 3 : 0;
            for (int i = 0; i < Mathf.Min(native.Length, pulse.Systems.Length); i++)
            {
                bool ring = NativeBurstPlayback.HasMaterial(native[i], "ring_gradient");
                bool streaks = NativeBurstPlayback.HasMaterial(native[i], "pixel_additive") &&
                    native[i].GetComponent<ParticleSystemRenderer>()?.renderMode == ParticleSystemRenderMode.Stretch;
                if (!ring && !streaks) continue;
                int count = Mathf.Min(NativeBurstPlayback.NativeCount(native[i], ring ? 1f : multiplier, ParticleBudget), remaining);
                if (runPhase > 0)
                    count = ring ? (runPhase == 1 ? 0 : Mathf.Min(1, count)) : Mathf.Min(runPhase == 1 ? 32 : 64, count);
                pulse.Counts[i] = count; remaining -= count;
                ParticleSystem system = pulse.Systems[i];
                var main = system.main;
                main.maxParticles = Mathf.Max(1, count);
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startLifetimeMultiplier *= ring ? (runPhase == 3 ? 1.8f : 1.5f) : .55f;
                if (ring && main.startColor.mode == ParticleSystemGradientMode.Color)
                {
                    // Keep the native peach/gold hue. Its original .298 alpha was
                    // authored for dodge, not an easily read sprint-stage signal.
                    Color color = main.startColor.color;
                    color.a = runPhase == 3 ? .9f : runPhase == 2 ? .45f : .72f;
                    main.startColor = color;
                }
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.enabled = count > 0;
            }
            NativeVfxSafeFrame.AfterReady(root, pulse.EmitReady);
        }

        private void EmitReady()
        {
            if (!gameObject.activeInHierarchy || !MasteryPlugin.Settings.EnablePerkProcVFX.Value || Systems == null) return;
            int emitted = 0;
            for (int i = 0; i < Systems.Length; i++)
            {
                if (Systems[i] == null || Counts[i] <= 0) continue;
                NativeBurstPlayback.EmitOnce(Systems[i], Counts[i]); emitted += Counts[i];
            }
            if (Scene != ZNetScene.instance) { Scene = ZNetScene.instance; Traces.Clear(); }
            Traces.TryGetValue(Cue, out int traced);
            if (MasteryPlugin.Settings.VerboseLogging.Value && traced < 3)
            {
                Traces[Cue] = traced + 1;
                MasteryPlugin.Log.LogInfo("[MovementPulse] cue=" + Cue + " native=fx_perfectdodge emitted=" + emitted + " scale=" + transform.lossyScale);
                StartCoroutine(TraceFrame());
            }
        }
        private IEnumerator TraceFrame()
        {
            yield return new WaitForSeconds(.12f);
            if (!gameObject.activeInHierarchy || Systems == null) yield break;
            for (int i = 0; i < Systems.Length; i++)
            {
                ParticleSystem system = Systems[i];
                if (system == null || Counts[i] <= 0) continue;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                int count = system.GetParticles(Probe);
                MasteryPlugin.Log.LogInfo("[MovementPulseTrace] cue=" + Cue + " particles=" + count +
                    " material=" + renderer?.sharedMaterial?.name + " renderer=" + (renderer != null && renderer.enabled) +
                    " cameraVisible=" + (renderer != null && renderer.isVisible) +
                    " size=" + (count > 0 ? Probe[0].GetCurrentSize(system).ToString("F3") : "0") +
                    " alpha=" + (count > 0 ? (Probe[0].GetCurrentColor(system).a / 255f).ToString("F3") : "0") +
                    " bounds=" + (renderer != null ? renderer.bounds.size.ToString() : "none"));
            }
        }
        private void OnDisable() => StopAllCoroutines();
    }

    // Selectively replay existing authored t=0 bursts. Never leave the rest of a
    // large effect free to auto-burst when its deferred native child activates.
    internal static class NativeBurstPlayback
    {
        internal static bool HasMaterial(ParticleSystem system, string name)
        {
            var renderer = system?.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return false;
            foreach (Material material in renderer.sharedMaterials)
                if (material != null && material.name == name) return true;
            return false;
        }
        internal static int NativeCount(ParticleSystem source, float multiplier, int budget)
        {
            if (source == null || !source.emission.enabled) return 0;
            var emission = source.emission; float count = 0f;
            for (int i = 0; i < emission.burstCount; i++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(i);
                if (burst.time <= .001f && burst.probability > 0f)
                    count += Mathf.Max(0f, burst.count.Evaluate(0f, .5f)) * burst.probability;
            }
            return Mathf.Clamp(Mathf.CeilToInt(count * Mathf.Clamp01(multiplier)), 0, budget);
        }
        internal static void Mute(GameObject root)
        {
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); system.Clear(false);
                var emission = system.emission; emission.enabled = false;
                var main = system.main; main.playOnAwake = false; main.maxParticles = 1;
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Light light in root.GetComponentsInChildren<Light>(true)) light.intensity = 0f;
        }
        internal static void EmitOnce(ParticleSystem system, int count)
        {
            if (system == null || count <= 0) return;
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); system.Clear(false);
            var emission = system.emission; emission.enabled = false;
            system.Play(false); system.Emit(count);
        }
    }
}
