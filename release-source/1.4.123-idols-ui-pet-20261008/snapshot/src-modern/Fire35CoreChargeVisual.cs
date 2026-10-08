using UnityEngine;

namespace ValheimMastery
{
    // Visual-only: borrows native fire ember particles, never their prefab
    // scripts, audio, lights, network view or full-screen bubble.
    internal sealed class Fire35CoreChargeVisual : MonoBehaviour
    {
        private const int Capacity = MagicPresentationRules.FireParticleCapacity;
        private readonly ParticleSystem.Particle[] Particles = new ParticleSystem.Particle[Capacity];
        private ParticleSystem Effect;
        private Vector3 ShaftStart, ShaftEnd;
        private bool Ending;

        internal static Fire35CoreChargeVisual Create(Transform weapon)
        {
            if (weapon == null || !ResolveShaft(weapon, out Vector3 start, out Vector3 end)) return null;
            ParticleSystem source = null;
            foreach (string name in new[] { "Surtling", "fx_fireball_staff_explosion", "vfx_surtling_attack" })
            {
                var native = NativePerkAssetResolver.Resolve(name);
                if (native == null) continue;
                foreach (var renderer in native.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (renderer.renderMode != ParticleSystemRenderMode.Mesh && renderer.sharedMaterial != null)
                    {
                        string label = (renderer.name + " " + renderer.sharedMaterial.name).ToLowerInvariant();
                        if (label.Contains("smoke") || label.Contains("trail") || label.Contains("glow")) continue;
                        if (source == null) source = renderer.GetComponent<ParticleSystem>();
                        if (label.Contains("spark") || label.Contains("ember"))
                        { source = renderer.GetComponent<ParticleSystem>(); break; }
                    }
                if (source != null) break;
            }
            if (source == null) return null;
            ParticleSystemRenderer original = source.GetComponent<ParticleSystemRenderer>();
            var root = new GameObject("VM_Fire35_CoreOrbit");
            root.transform.SetParent(weapon, false);
            var visual = root.AddComponent<Fire35CoreChargeVisual>();
            visual.ShaftStart = start; visual.ShaftEnd = end;
            visual.Effect = root.AddComponent<ParticleSystem>();
            visual.Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = visual.Effect.main;
            main.loop = true; main.maxParticles = Capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startSpeed = 0f;
            var emission = visual.Effect.emission; emission.enabled = false;
            var shape = visual.Effect.shape; shape.enabled = false;
            var particles = root.GetComponent<ParticleSystemRenderer>();
            particles.sharedMaterial = original.sharedMaterial;
            particles.renderMode = ParticleSystemRenderMode.Billboard;
            particles.alignment = original.alignment;
            var streams = new System.Collections.Generic.List<ParticleSystemVertexStream>();
            original.GetActiveVertexStreams(streams); particles.SetActiveVertexStreams(streams);
            NativeSnowVisualAssets.CopySheet(source, visual.Effect);
            particles.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particles.receiveShadows = false;
            visual.Effect.Play();
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Fire35Core] native=" + source.name + " material=" + original.sharedMaterial.name +
                    " shaft=" + start + " -> " + end);
            return visual;
        }
        private static bool ResolveShaft(Transform weapon, out Vector3 start, out Vector3 end)
        {
            start = end = Vector3.zero;
            MeshFilter[] meshes = weapon.GetComponentsInChildren<MeshFilter>(true);
            Vector3 axis = Vector3.zero, center = Vector3.zero;
            float longest = .01f;
            // Read the longest native mesh direction after its child rotation and
            // scale. Never assume the equipped staff points along world/local up.
            foreach (MeshFilter mesh in meshes)
            {
                if (mesh.sharedMesh == null || mesh.name.StartsWith("VM_", System.StringComparison.Ordinal)) continue;
                Bounds bounds = mesh.sharedMesh.bounds;
                Vector3 localAxis = bounds.extents.y >= bounds.extents.x && bounds.extents.y >= bounds.extents.z ? Vector3.up :
                    bounds.extents.x >= bounds.extents.z ? Vector3.right : Vector3.forward;
                float extent = Vector3.Dot(bounds.extents, localAxis);
                Vector3 a = weapon.InverseTransformPoint(mesh.transform.TransformPoint(bounds.center - localAxis * extent));
                Vector3 b = weapon.InverseTransformPoint(mesh.transform.TransformPoint(bounds.center + localAxis * extent));
                Vector3 span = b - a;
                if (span.sqrMagnitude <= longest) continue;
                longest = span.sqrMagnitude; axis = span.normalized; center = (a + b) * .5f;
            }
            if (axis == Vector3.zero) return false;
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (MeshFilter mesh in meshes)
            {
                if (mesh.sharedMesh == null || mesh.name.StartsWith("VM_", System.StringComparison.Ordinal)) continue;
                Bounds bounds = mesh.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    Vector3 point = weapon.InverseTransformPoint(mesh.transform.TransformPoint(local));
                    float distance = Vector3.Dot(point - center, axis);
                    minimum = Mathf.Min(minimum, distance); maximum = Mathf.Max(maximum, distance);
                }
            }
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || maximum - minimum < .1f) return false;
            start = center + axis * minimum; end = center + axis * maximum;
            // A named native core gives the direction of energy flow. Otherwise
            // the farther end from the hand attachment is the existing tip fallback.
            Vector3 head = start.sqrMagnitude > end.sqrMagnitude ? start : end;
            foreach (Transform child in weapon.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith("VM_", System.StringComparison.Ordinal)) continue;
                string name = child.name.ToLowerInvariant();
                if (!name.Contains("surtling") && !name.Contains("core") && !name.Contains("gem")) continue;
                head = weapon.InverseTransformPoint(child.position); break;
            }
            if ((head - start).sqrMagnitude < (head - end).sqrMagnitude)
            { Vector3 swap = start; start = end; end = swap; }
            return true;
        }
        internal void Draw(float held)
        {
            if (Ending || Effect == null) return;
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) { Effect.Clear(); return; }
            Vector3 axis = (ShaftEnd - ShaftStart).normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < .9f ? Vector3.up : Vector3.right;
            Vector3 side = Vector3.Cross(axis, reference).normalized, normal = Vector3.Cross(axis, side);
            float charge = MagicPresentationRules.FireCharge(held);
            int count = MagicPresentationRules.FireParticles(held);
            for (int i = 0; i < count; i++)
            {
                float along = MagicPresentationRules.FireOrbitPosition(i, Time.time, held);
                float direction = (i & 1) == 0 ? 1f : -1f;
                float angle = i * 2.39996f + direction * Time.time * (.8f + charge * 4f) +
                    along * Mathf.PI * (2.5f + charge * 2f) + Mathf.Sin(Time.time * 1.3f + i) * .3f;
                float radius = MagicPresentationRules.FireOrbitRadius(held) *
                    (.85f + .15f * Mathf.Sin(Time.time * 1.7f + i * .91f));
                // Two irregular counter-turning streams along the whole body.
                // Small separated embers keep the charge readable beside the core.
                Particles[i].position = Vector3.Lerp(ShaftStart, ShaftEnd, along) +
                    (side * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * radius;
                Particles[i].velocity = Vector3.zero;
                Particles[i].startSize = MagicPresentationRules.FireOrbitSize(held);
                Particles[i].startColor = new Color(1f, .4f + charge * .5f, .08f + charge * .28f,
                    MagicPresentationRules.FireParticleAlpha(held));
                Particles[i].startLifetime = Particles[i].remainingLifetime = .25f;
            }
            Effect.SetParticles(Particles, count);
        }
        internal void Finish()
        {
            if (Ending) return;
            Ending = true;
            Destroy(gameObject, .3f);
        }
    }
}

