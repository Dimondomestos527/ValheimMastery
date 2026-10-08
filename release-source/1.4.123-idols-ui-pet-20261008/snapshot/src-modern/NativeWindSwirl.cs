using UnityEngine;

namespace ValheimMastery
{
    // A bounded local emitter borrowing the actual ambient wind texture. Never
    // move or retune the weather emitter; no repeated VFX prefab bursts per spin.
    internal sealed class NativeWindSwirl : MonoBehaviour
    {
        private static Material Template;
        private static float NextLookup;
        private ParticleSystem System;
        private Material Owned;
        private readonly ParticleSystem.Particle[] Particles = new ParticleSystem.Particle[72];
        internal float Radius = 2f, Strength = 1f, Fade = 1f;
        internal static NativeWindSwirl Create(Transform parent)
        {
            if (Template == null && Time.time >= NextLookup)
            { NextLookup = Time.time + 3f; Template = NativePerkAssetResolver.WindMaterial(); }
            if (Template == null) return null;
            var root = new GameObject("Mastery_NativeWind"); root.transform.SetParent(parent, false);
            var effect = root.AddComponent<NativeWindSwirl>(); effect.Initialize(); return effect;
        }
        private void Initialize()
        {
            System = gameObject.AddComponent<ParticleSystem>(); System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = System.main; main.loop = true; main.maxParticles = Particles.Length;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.startSpeed = 0f; main.startLifetime = 2f;
            var emission = System.emission; emission.enabled = false;
            var shape = System.shape; shape.enabled = false;
            var renderer = GetComponent<ParticleSystemRenderer>();
            Owned = new Material(Template); renderer.sharedMaterial = Owned;
            renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .018f; renderer.lengthScale = 2.4f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            System.Play();
        }
        private void LateUpdate()
        {
            if (System == null) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(22f + Strength * 8f), 20, Particles.Length);
            bool enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (!enabled) { System.SetParticles(Particles, 0); return; }
            float speed = 7f + Strength * 1.8f;
            for (int i = 0; i < count; i++)
            {
                float a = Time.time * speed + i * 2.399963f;
                float r = Radius * (.72f + .28f * ((i * 13 % 23) / 22f));
                var point = new Vector3(Mathf.Cos(a) * r, .12f + (i % 4) * .22f, Mathf.Sin(a) * r);
                Particles[i].position = point;
                Particles[i].velocity = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * speed * r;
                Particles[i].startColor = new Color(.86f, .90f, .91f, Mathf.Clamp01((.22f + Strength * .035f) * Fade));
                Particles[i].startSize = .08f + Strength * .014f;
                Particles[i].startLifetime = Particles[i].remainingLifetime = 1f;
            }
            System.SetParticles(Particles, count);
        }
        private void OnDestroy() { if (Owned != null) Destroy(Owned); }
    }
}
