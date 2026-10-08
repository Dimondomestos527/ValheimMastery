using System;
using UnityEngine;

namespace ValheimMastery
{
    // Recognition-only particle donors. Never activate an item/environment controller.
    internal static class Combat100RecognitionParticles
    {
        internal static GameObject Clone(ParticleSystem donor, Transform parent, int budget, float lifetime)
        {
            if (donor == null || parent == null || Application.isBatchMode || !NativeVfxSafeFrame.CanStage) return null;
            GameObject wrapper = new GameObject("Mastery_PatronNativeParticles");
            wrapper.SetActive(false);
            try
            {
                GameObject content = UnityEngine.Object.Instantiate(donor.gameObject, wrapper.transform, false);
                content.SetActive(false);
                content.transform.localPosition = Vector3.zero;
                content.transform.localRotation = Quaternion.identity;
                // Meshes on a trinket donor are not part of the particle gift.
                foreach (Renderer renderer in content.GetComponentsInChildren<Renderer>(true))
                    if (!(renderer is ParticleSystemRenderer)) renderer.enabled = false;
                foreach (Light light in content.GetComponentsInChildren<Light>(true)) light.enabled = false;
                foreach (Animator animator in content.GetComponentsInChildren<Animator>(true))
                { animator.enabled = false; animator.fireEvents = false; animator.applyRootMotion = false; }
                foreach (Animation animation in content.GetComponentsInChildren<Animation>(true)) animation.enabled = false;
                var originals = donor.gameObject.GetComponentsInChildren<ParticleSystemRenderer>(true);
                var copies = content.GetComponentsInChildren<ParticleSystemRenderer>(true);
                var block = new MaterialPropertyBlock();
                for (int i = 0; i < Mathf.Min(originals.Length, copies.Length); i++)
                {
                    block.Clear(); originals[i].GetPropertyBlock(block); copies[i].SetPropertyBlock(block);
                    for (int slot = 0; slot < originals[i].sharedMaterials.Length; slot++)
                    { block.Clear(); originals[i].GetPropertyBlock(block, slot); copies[i].SetPropertyBlock(block, slot); }
                }
                var particles = content.GetComponentsInChildren<ParticleSystem>(true);
                if (particles.Length == 0 || particles.Length > 12)
                { UnityEngine.Object.Destroy(wrapper); return null; }
                foreach (ParticleSystem particle in particles)
                {
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = particle.main;
                    main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None; main.useUnscaledTime = true;
                    main.maxParticles = Mathf.Min(main.maxParticles, Mathf.Max(1, budget / particles.Length));
                    main.startLifetimeMultiplier = Mathf.Min(main.startLifetimeMultiplier, lifetime);
                    var collision = particle.collision; collision.enabled = false;
                    var trigger = particle.trigger; trigger.enabled = false;
                    var sub = particle.subEmitters; sub.enabled = false;
                    var lights = particle.lights; lights.enabled = false;
                    var shape = particle.shape;
                    if ((shape.meshRenderer != null && !shape.meshRenderer.transform.IsChildOf(content.transform)) ||
                        (shape.skinnedMeshRenderer != null && !shape.skinnedMeshRenderer.transform.IsChildOf(content.transform)) ||
                        (shape.spriteRenderer != null && !shape.spriteRenderer.transform.IsChildOf(content.transform)))
                        throw new InvalidOperationException("external native emission renderer");
                    // Do not leave the owned emitter attached to a donor simulation transform.
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                    {
                        if (main.customSimulationSpace == null || !main.customSimulationSpace.IsChildOf(content.transform))
                            throw new InvalidOperationException("external native simulation transform");
                    }
                }
                // The existing safe-frame coordinator strips scripts/audio/network/colliders
                // while content is inactive, before any native Awake/OnEnable can run.
                wrapper.AddComponent<DeferredNativeVfx>().Initialize(content, donor.gameObject);
                wrapper.transform.SetParent(parent, false);
                return wrapper;
            }
            catch (Exception error)
            {
                wrapper.SetActive(false); UnityEngine.Object.Destroy(wrapper);
                NativeVfxSafeFrame.Warn(donor.name, "recognition clone " + error.GetType().Name);
                return null;
            }
        }
    }
}
