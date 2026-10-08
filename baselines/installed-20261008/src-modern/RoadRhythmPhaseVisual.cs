using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ValheimMastery
{
    // Selected air-jump donor, at air-jump size. Phase transitions remain short,
    // detached bursts: no ring at phase1; vertical rings at phases2/3.
    internal sealed class RoadRhythmPhaseVisual : MonoBehaviour
    {
        private const float RetryWindow = 1.5f;
        private const float RetryInterval = 0.2f;
        private Player Owner;
        private GameObject LandingPrefab;
        private int PendingStacks;
        private float RetryUntil;
        private float RetryAt;
        private bool Warned;
        private static bool NativeInfoLogged;
        private static int TraceSequence;

        internal static void Set(Player player, int stacks)
        {
            if (player == null) return;
            if (stacks <= 0) { Clear(player); return; }
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;

            RoadRhythmPhaseVisual visual = player.GetComponent<RoadRhythmPhaseVisual>() ??
                player.gameObject.AddComponent<RoadRhythmPhaseVisual>();
            visual.Owner = player;
            visual.PendingStacks = Mathf.Clamp(stacks, 1, 3);
            visual.RetryUntil = Time.time + RetryWindow;
            visual.RetryAt = Time.time;
            visual.TryEmitPending();
        }

        internal static void Clear(Player player)
        {
            RoadRhythmPhaseVisual visual = player != null ? player.GetComponent<RoadRhythmPhaseVisual>() : null;
            if (visual != null) Destroy(visual);
        }

        private void Update()
        {
            if (Owner == null) { Destroy(this); return; }
            if (PendingStacks <= 0) return;
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) { PendingStacks = 0; return; }
            if (Time.time >= RetryAt) TryEmitPending();
            if (PendingStacks > 0 && Time.time >= RetryUntil)
            {
                if (!Warned && MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogWarning("[RunRhythmVFX] Native phase visual could not be resolved or cloned; burst skipped.");
                Warned = true;
                PendingStacks = 0;
            }
        }

        private void TryEmitPending()
        {
            if (PendingStacks <= 0 || Owner == null) return;
            if (LandingPrefab == null) LandingPrefab = ResolveLandingPrefab();
            if (LandingPrefab == null || !EmitPhase(PendingStacks))
            {
                RetryAt = Time.time + RetryInterval;
                return;
            }

            PendingStacks = 0;
        }

        private GameObject ResolveLandingPrefab()
        {
            GameObject prefab = NativePerkAssetResolver.Resolve("fx_perfectdodge");
            if (prefab == null) return null;

            bool hasRenderer = false;
            foreach (ParticleSystem candidate in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystemRenderer candidateRenderer = candidate.GetComponent<ParticleSystemRenderer>();
                if (candidateRenderer == null || candidateRenderer.sharedMaterial == null) continue;
                hasRenderer = true;
                if (!NativeInfoLogged && MasteryPlugin.Settings.VerboseLogging.Value)
                {
                    ParticleSystem.MainModule main = candidate.main;
                    List<ParticleSystemVertexStream> streams = new List<ParticleSystemVertexStream>();
                    candidateRenderer.GetActiveVertexStreams(streams);
                    float life = main.startLifetime.Evaluate(0.5f, 0.5f);
                    MasteryPlugin.Log.LogInfo("[RunRhythmVFX] native=" + prefab.name + " system=" + candidate.name +
                        " render=" + candidateRenderer.renderMode + " material=" + candidateRenderer.sharedMaterial.name +
                        " streams=" + streams.Count + " simulation=" + main.simulationSpace +
                        " duration=" + main.duration.ToString("0.00") + " lifetime~=" + life.ToString("0.00") +
                        " loop=" + main.loop + " playOnAwake=" + main.playOnAwake);
                }
            }
            if (!hasRenderer) return null;
            if (MasteryPlugin.Settings.VerboseLogging.Value) NativeInfoLogged = true;
            return prefab;
        }

        private bool EmitPhase(int stacks)
        {
            if (LandingPrefab == null || Owner == null || !VfxRecipeService.TryGet("jump_70_air", out VfxRecipe reference) || reference.Layers.Count == 0)
                return false;
            Vector3 center = Owner.transform.position + Vector3.up * .95f;
            Quaternion rotation = Quaternion.LookRotation(Owner.transform.forward, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            float lifetime = 2.2f;
            GameObject instance = VfxPool.Spawn(LandingPrefab, center, rotation, lifetime);
            if (instance == null) return false;
            var layer = new VfxLayer { RootScale = reference.Layers[0].RootScale * (stacks < 3 ? .9f : 1f),
                EmissionMultiplier = stacks == 1 ? .4f : .8f };
            VfxRecipeService.Tune(instance, layer);
            NativeMovementPulse.Schedule(instance, LandingPrefab, layer.EmissionMultiplier, "run_70_stack" + stacks);
            instance.SetActive(true);
            // A staged native clone is not yet playable. Keep the landing impulse
            // after sanitation/restart; otherwise a deferred Clear erases this burst.
            NativeVfxSafeFrame.AfterReady(instance, () => EmitReady(instance, stacks, center, lifetime));
            return true;
        }

        private void EmitReady(GameObject instance, int stacks, Vector3 center, float lifetime)
        {
            if (instance == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            // Native scheduler is started only after sanitation; no beige
            // manually generated dust or continuous wind wake.
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[RunRhythmVFX] native phase burst started phase=" + stacks +
                    " prefab=" + LandingPrefab.name + " lifetime=" + lifetime.ToString("0.00"));
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                StartCoroutine(TraceBurstAfterFrame(instance, stacks, ++TraceSequence, 0));
        }

        private IEnumerator TraceBurstAfterFrame(GameObject instance, int phase, int token, int recipeLayer)
        {
            yield return new WaitForEndOfFrame();
            if (!MasteryPlugin.Settings.VerboseLogging.Value) yield break;
            if (instance == null)
            {
                MasteryPlugin.Log.LogInfo("[RunRhythmVFXTrace] phase=" + phase + " token=" + token +
                    " prefab=fx_perfectdodge layer=" + recipeLayer + " instance=destroyed before frame-end");
                yield break;
            }

            StringBuilder systems = new StringBuilder();
            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                if (systems.Length > 0) systems.Append("; ");
                systems.Append(system.name).Append(" count=").Append(system.particleCount)
                    .Append(" active=").Append(system.gameObject.activeInHierarchy);
                if (renderer == null) systems.Append(" renderer=none");
                else
                {
                    Bounds bounds = renderer.bounds;
                    systems.Append(" renderer=").Append(renderer.enabled ? "on" : "off")
                        .Append('/').Append(renderer.isVisible ? "visible" : "not-visible")
                        .Append(" bounds=").Append(bounds.center.ToString("F2"))
                        .Append('+').Append(bounds.size.ToString("F2"))
                        .Append(" layer=").Append(renderer.gameObject.layer)
                        .Append('/').Append(renderer.sortingLayerName).Append(':').Append(renderer.sortingOrder);
                }
            }
            MasteryPlugin.Log.LogInfo("[RunRhythmVFXTrace] phase=" + phase + " token=" + token +
                " prefab=" + instance.name + " active=" + instance.activeInHierarchy + " layer=" + recipeLayer +
                " systems=[" + systems + "]");
        }

    }
}
