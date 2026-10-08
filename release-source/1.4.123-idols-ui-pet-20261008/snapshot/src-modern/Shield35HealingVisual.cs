using UnityEngine;

namespace ValheimMastery
{
    // One bounded cosmetic system per shield recipient. No status or network prefab clones.
    internal sealed class Shield35HealingVisual : MonoBehaviour
    {
        private const int HealingCount = 24;
        private const float ChannelDuration = 1.15f;
        private Character Recipient;
        private ParticleSystem Effect;
        private readonly ParticleSystem.Particle[] Particles = new ParticleSystem.Particle[48];
        private float ChannelUntil;
        private float ChannelStarted;

        // Local cosmetic receipt only; shield state and renewal stay on the owner.
        internal static void Pulse(Character recipient)
        {
            if (recipient == null) return;
            Shield35HealingVisual visual = recipient.GetComponent<Shield35HealingVisual>();
            if (visual == null) visual = recipient.gameObject.AddComponent<Shield35HealingVisual>();
            if (Time.time >= visual.ChannelUntil) visual.ChannelStarted = Time.time;
            visual.ChannelUntil = Time.time + ChannelDuration;
        }

        private void Awake()
        {
            Recipient = GetComponent<Character>();
            Material material = null;
            GameObject asset = NativePerkAssetResolver.Resolve("vfx_StaffShield");
            if (asset != null)
                foreach (var renderer in asset.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (renderer.renderMode != ParticleSystemRenderMode.Mesh && renderer.sharedMaterial != null &&
                        renderer.GetComponent<ParticleSystem>()?.textureSheetAnimation.enabled != true)
                    { material = renderer.sharedMaterial; break; }
            if (material == null) { Destroy(this); return; }
            GameObject root = new GameObject("VM_Shield35_Healing"); root.transform.SetParent(transform, false);
            Effect = root.AddComponent<ParticleSystem>(); Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = Effect.main; main.maxParticles = Particles.Length; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            var emission = Effect.emission; emission.enabled = false;
            var shape = Effect.shape; shape.enabled = false;
            var visual = root.GetComponent<ParticleSystemRenderer>(); visual.sharedMaterial = material;
            visual.renderMode = ParticleSystemRenderMode.Billboard; visual.receiveShadows = false;
            visual.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Effect.Play();
        }
        private void LateUpdate()
        {
            if (Effect == null || Recipient == null) return;
            bool channel = Time.time < ChannelUntil;
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value ||
                (!MagicShield35Service.HasMasterShield(Recipient) && !channel))
            { Effect.SetParticles(Particles, 0); return; }
            Vector3 center = Recipient.GetCenterPoint();
            float radius = Mathf.Clamp(Recipient.GetRadius(), .35f, 1.25f) * .85f;
            // Consecutive one-second receipts extend the same flowing aftercast.
            // Smooth edges also fade the extra motes before dropping back to 24.
            float elapsed = Time.time - ChannelStarted;
            float fade = channel ? Mathf.SmoothStep(0f, 1f, elapsed / .15f) *
                Mathf.SmoothStep(0f, 1f, (ChannelUntil - Time.time) / .15f) : 0f;
            float pulse = .5f - .5f * Mathf.Cos(elapsed * Mathf.PI * 2f);
            float strength = fade * (.25f + .75f * pulse);
            int count = channel ? Particles.Length : HealingCount;
            for (int i = 0; i < count; i++)
            {
                float angle = Time.time * .55f + i * 2.39996f;
                Vector3 healing = new Vector3(Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle * .7f + i) * radius, Mathf.Sin(angle) * radius);
                float flow = Mathf.Repeat(elapsed * .65f + i * .618034f, 1f);
                float height = (flow * 2f - 1f) * .85f;
                float innerRadius = radius * (.72f + .18f * pulse);
                float orbit = angle + elapsed * 1.1f;
                float horizontal = Mathf.Sqrt(1f - height * height) * innerRadius;
                Vector3 renewal = new Vector3(Mathf.Cos(orbit) * horizontal,
                    height * innerRadius, Mathf.Sin(orbit) * horizontal);
                Particles[i].position = center + Vector3.Lerp(healing, renewal, fade);
                Particles[i].velocity = Vector3.zero;
                Particles[i].startSize = .035f + .012f * strength;
                Color color = Color.Lerp(new Color(1f, .22f, .18f, .45f),
                    new Color(1f, .12f, .08f, .35f + .35f * pulse), fade);
                // Every flowing mote fades at its wrap, avoiding a visible jump.
                color.a *= Mathf.Lerp(1f, Mathf.Sin(flow * Mathf.PI), fade);
                if (i >= HealingCount) color.a *= fade;
                Particles[i].startColor = color;
                Particles[i].startLifetime = Particles[i].remainingLifetime = .2f;
            }
            Effect.SetParticles(Particles, count);
        }
        private void OnDestroy() { if (Effect != null) Destroy(Effect.gameObject); }
    }
}
