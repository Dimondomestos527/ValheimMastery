using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Replay native landing bursts safely. The approved105 Run route instead
    // retains its source's entire native scheduler (including jump-reference
    // sources with rate/delayed emission). No tint or replacement texture.
    internal sealed class NativeLandingBurst : MonoBehaviour
    {
        private const int ParticleBudget = 64;
        private static readonly Dictionary<string, int> Traces = new Dictionary<string, int>();
        private static readonly HashSet<string> LegacySources = new HashSet<string>();
        private static ZNetScene Scene;
        private ParticleSystem[] Systems;
        private int[] Counts;
        private string Cue;
        private string NativeName;
        private bool PreserveNativeSettings;
        private readonly ParticleSystem.Particle[] Probe = new ParticleSystem.Particle[ParticleBudget];

        internal static GameObject Play(Vector3 position, float scale, float lifetime, string cue)
        {
            if (Player.m_localPlayer == null || ZNetScene.instance == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return null;
            GameObject source = NativePerkAssetResolver.Resolve("fx_land");
            if (source == null)
            {
                NativeVfxSafeFrame.Request("fx_land", loaded => Spawn(loaded, position, scale, lifetime, cue) != null);
                return null;
            }
            return Spawn(source, position, scale, lifetime, cue);
        }

        private static GameObject Spawn(GameObject source, Vector3 position, float scale, float lifetime, string cue)
        {
            if (source == null || source.name != "fx_land" || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return null;
            GameObject root = VfxPool.Spawn(source, position, Quaternion.identity, lifetime);
            if (root == null) return null;
            root.transform.localScale *= Mathf.Clamp(scale, .05f, 4f);
            root.SetActive(true);
            root.GetComponent<VfxPoolBaseline>()?.RestartParticles();
            Schedule(root, source, 1f, cue);
            return root;
        }

        internal static void Schedule(GameObject root, GameObject source, float multiplier, string cue, bool preserveNativeSettings = false)
        {
            if (root == null || source == null || (!preserveNativeSettings && source.name != "fx_land")) return;
            NativeLandingBurst burst = root.GetComponent<NativeLandingBurst>() ?? root.AddComponent<NativeLandingBurst>();
            burst.StopAllCoroutines();
            burst.Cue = string.IsNullOrEmpty(cue) ? "fx_land" : cue;
            burst.NativeName = source.name; burst.PreserveNativeSettings = preserveNativeSettings;
            ParticleSystem[] native = source.GetComponentsInChildren<ParticleSystem>(true);
            burst.Systems = root.GetComponentsInChildren<ParticleSystem>(true);
            burst.Counts = new int[burst.Systems.Length];
            int remaining = ParticleBudget;
            for (int i = 0; i < Mathf.Min(native.Length, burst.Systems.Length); i++)
            {
                int count = NativeCount(native[i], multiplier);
                count = Mathf.Min(count, remaining);
                var main = burst.Systems[i].main;
                if (preserveNativeSettings)
                {
                    // 105 never changed native Local scaling or fx_land cap10.
                    // Preserve its own mode and cap within an aggregate64 safety
                    // ceiling, not t0-only assumptions about an EffectList source.
                    main.scalingMode = native[i].main.scalingMode;
                    // Keep the authored ring even when the first streak system
                    // has a larger native cap than the whole effect budget.
                    int laterEmitters = 0;
                    for (int j = i + 1; j < Mathf.Min(native.Length, burst.Systems.Length); j++)
                        if (native[j].emission.enabled && native[j].main.maxParticles > 0) laterEmitters++;
                    int phaseCap = source.name == "fx_perfectdodge"
                        ? Mathf.CeilToInt(native[i].main.maxParticles * .35f * multiplier)
                        : native[i].main.maxParticles;
                    main.maxParticles = Mathf.Min(main.maxParticles, phaseCap, Mathf.Max(0, remaining - laterEmitters));
                    count = Mathf.Min(count, main.maxParticles);
                    remaining -= main.maxParticles;
                }
                else { main.maxParticles = Mathf.Max(main.maxParticles, count); remaining -= count; }
                burst.Counts[i] = count;
            }
            NativeVfxSafeFrame.AfterReady(root, burst.EmitReady);
        }

        private static int NativeCount(ParticleSystem source, float multiplier)
        {
            if (source == null || !source.emission.enabled) return 0;
            var emission = source.emission;
            float count = 0f;
            for (int i = 0; i < emission.burstCount; i++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(i);
                if (burst.time <= .001f && burst.probability > 0f)
                    count += Mathf.Max(0f, burst.count.Evaluate(0f, .5f)) * burst.probability;
            }
            return Mathf.Clamp(Mathf.CeilToInt(count * Mathf.Clamp(multiplier, 0f, 4f)), 0, ParticleBudget);
        }

        private void EmitReady()
        {
            if (!gameObject.activeInHierarchy || !MasteryPlugin.Settings.EnablePerkProcVFX.Value || Systems == null) return;
            int emitted = 0;
            for (int i = 0; i < Systems.Length; i++)
            {
                ParticleSystem system = Systems[i];
                if (system == null || (!PreserveNativeSettings && Counts[i] <= 0)) continue;
                // Kill a scheduled t=0 auto-burst before emitting the same native
                // count explicitly. Keep the native shape, speed/color/size curves,
                // renderer/texture bindings, and one-shot lifetime intact.
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Clear(false);
                if (PreserveNativeSettings)
                {
                    // Keep tuned native Burst curves, emission rates and enabled
                    // flags. Zero t0 count does not mean a rate/delayed emitter is
                    // empty. Native Play schedules it exactly as the old pool did.
                    system.Play(false); emitted += Counts[i]; continue;
                }
                var emission = system.emission; emission.enabled = false;
                if (!PreserveNativeSettings)
                { var main = system.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                system.Play(false);
                system.Emit(Counts[i]); emitted += Counts[i];
            }
            if (Scene != ZNetScene.instance) { Scene = ZNetScene.instance; Traces.Clear(); LegacySources.Clear(); }
            if (PreserveNativeSettings && LegacySources.Add(NativeName))
                MasteryPlugin.Log.LogInfo("[RunAVRestore105] resolvedNative=" + NativeName +
                    " scaling=" + (Systems.Length > 0 ? Systems[0].main.scalingMode.ToString() : "none") +
                    " nativeCap=" + (Systems.Length > 0 ? Systems[0].main.maxParticles : 0) +
                    " appearance=legacy105 playback=native-scheduled-after-ready");
            Traces.TryGetValue(Cue, out int traced);
            if (MasteryPlugin.Settings.VerboseLogging.Value && traced < 4)
            {
                Traces[Cue] = traced + 1;
                MasteryPlugin.Log.LogInfo("[LandingVFX] cue=" + Cue + " native=" + NativeName +
                    (PreserveNativeSettings ? " cappedInstantPrediction=" : " emitted=") + emitted +
                    " position=" + transform.position + " rootScale=" + transform.lossyScale);
                StartCoroutine(TraceVisibleFrame());
            }
        }

        private IEnumerator TraceVisibleFrame()
        {
            // Native size and alpha start at zero; inspect after their authored
            // fade-in has advanced, not immediately after the Emit call.
            yield return new WaitForSeconds(.16f);
            if (!gameObject.activeInHierarchy || Systems == null) yield break;
            foreach (ParticleSystem system in Systems)
            {
                if (system == null) continue;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                int count = system.GetParticles(Probe);
                float size = count > 0 ? Probe[0].GetCurrentSize(system) : 0f;
                float alpha = count > 0 ? Probe[0].GetCurrentColor(system).a / 255f : 0f;
                Bounds bounds = renderer != null ? renderer.bounds : default;
                MasteryPlugin.Log.LogInfo("[LandingVFXTrace] cue=" + Cue + " particles=" + system.particleCount +
                    " playing=" + system.isPlaying + " active=" + system.gameObject.activeInHierarchy +
                    " renderer=" + (renderer != null && renderer.enabled) +
                    " cameraVisible=" + (renderer != null && renderer.isVisible) +
                    " material=" + (renderer?.sharedMaterial != null ? renderer.sharedMaterial.name : "none") +
                    " firstSize=" + size.ToString("F3") + " firstAlpha=" + alpha.ToString("F3") +
                    " bounds=" + bounds.center + "+" + bounds.size);
            }
        }
        private void OnDisable() => StopAllCoroutines();
    }
}
