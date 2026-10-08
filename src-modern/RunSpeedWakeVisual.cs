using UnityEngine;

namespace ValheimMastery
{
    // Real particle lifetimes avoid resetting every streak to its starting point.
    // Native world-wind material/render geometry, no dodge-effect fallback.
    internal sealed class RunSpeedWakeVisual : MonoBehaviour
    {
        private const int Capacity = 22;
        private readonly ParticleSystem.Particle[] Particles = new ParticleSystem.Particle[Capacity];
        private ParticleSystem Effect;
        private Material OwnedMaterial;
        private float EmissionCredit;
        private float Strength;
        internal Player Owner;

        internal static RunSpeedWakeVisual Create(Player player)
        {
            ParticleSystem native = NativePerkAssetResolver.WindParticles();
            ParticleSystemRenderer nativeRenderer = native?.GetComponent<ParticleSystemRenderer>();
            Material source = nativeRenderer?.sharedMaterial;
            if (source == null) return null;
            GameObject root = new GameObject("ValheimMastery_RunSpeedWake");
            root.transform.SetParent(player.transform, false);
            RunSpeedWakeVisual visual = root.AddComponent<RunSpeedWakeVisual>();
            visual.Owner = player;
            visual.Effect = root.AddComponent<ParticleSystem>();
            visual.Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = visual.Effect.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Capacity;
            main.startSpeed = 0f;
            main.startLifetime = .7f;
            var emission = visual.Effect.emission;
            emission.enabled = false;
            var shape = visual.Effect.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
            visual.OwnedMaterial = new Material(source);
            renderer.sharedMaterial = visual.OwnedMaterial;
            renderer.renderMode = nativeRenderer.renderMode;
            renderer.mesh = nativeRenderer.mesh;
            renderer.alignment = nativeRenderer.alignment;
            renderer.velocityScale = nativeRenderer.velocityScale;
            renderer.lengthScale = nativeRenderer.lengthScale;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            visual.Effect.Play();
            return visual;
        }

        private void LateUpdate()
        {
            if (Owner == null || Effect == null) return;
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) { Effect.SetParticles(Particles, 0); return; }
            if (Owner.m_body == null) { Effect.SetParticles(Particles, 0); return; }
            Vector3 movement = Owner.m_body.linearVelocity;
            movement.y = 0f;
            float speed = movement.magnitude;
            int stacks = Stride70Service.GetStacks(Owner);
            bool emergency = Stride70Service.State(Owner).EmergencyUntil > Time.time;
            float target = speed >= 1.8f ? Mathf.Clamp01(stacks / 3f + (emergency ? .5f : 0f)) : 0f;
            Strength = Mathf.MoveTowards(Strength, target, Time.deltaTime * 1.2f);
            EmissionCredit = Mathf.Min(2f, EmissionCredit + Time.deltaTime * (4f + Strength * 6f) * Strength);
            // Emit only a handful of lines behind the torso, then let them travel
            // and fade naturally in world space instead of snapping on a modulo.
            Vector3 back = speed > .1f ? -movement / speed : -Owner.transform.forward;
            while (EmissionCredit >= 1f)
            {
                EmissionCredit -= 1f;
                Vector3 side = Vector3.Cross(Vector3.up, back);
                var emit = new ParticleSystem.EmitParams
                {
                    position = Owner.transform.position + Vector3.up * Random.Range(.55f, 1.35f) +
                        back * .25f + side * Random.Range(-.32f, .32f),
                    velocity = back * Random.Range(.35f, .65f) + Vector3.up * .015f,
                    startLifetime = .7f,
                    startSize = Random.Range(.06f, .085f),
                    startColor = new Color(.9f, .94f, 1f, 0f)
                };
                Effect.Emit(emit, 1);
            }
            int count = Effect.GetParticles(Particles);
            for (int i = 0; i < count; i++)
            {
                float age = 1f - Particles[i].remainingLifetime / Mathf.Max(.01f, Particles[i].startLifetime);
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .2f)) *
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - age) / .45f));
                Particles[i].startColor = new Color(.9f, .94f, 1f, fade * (.22f + Strength * .16f));
            }
            Effect.SetParticles(Particles, count);
        }

        private void OnDestroy()
        {
            if (OwnedMaterial != null) Destroy(OwnedMaterial);
        }
    }
}
