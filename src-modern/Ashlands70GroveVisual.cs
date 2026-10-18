using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    internal sealed class Ashlands70GroveVisual : MonoBehaviour
    {
        private static readonly string[] Donors = { "instanced_meadows_grass_short", "instanced_forest_groundcover", "instanced_ormbunke", "instanced_mistlands_grass_short", "instanced_heathflowers", "instanced_mistlands_rockplant" };
        private readonly InstanceRenderer[] Renderers = new InstanceRenderer[Donors.Length];
        private readonly Vector3[] Positions = new Vector3[128];
        private readonly Quaternion[] Rotations = new Quaternion[128];
        private readonly float[] Sizes = new float[128];
        private Ashlands70Grant Grant;
        private float NextTick, PreviousScale = -1f;
        internal static bool Build(Transform parent, Ashlands70Grant grant)
        {
            var sources = new InstanceRenderer[Donors.Length]; bool ready = true;
            for (int i = 0; i < sources.Length; i++)
            {
                GameObject prefab = NativeSoftVisualAssets.Get<GameObject>("Assets/world/Props/ground_clutter/" + Donors[i] + ".prefab");
                sources[i] = prefab != null ? prefab.GetComponent<InstanceRenderer>() : null;
                if (sources[i]?.m_mesh == null || sources[i].m_material == null) ready = false;
            }
            if (!ready) return false;
            Ashlands70GroveVisual visual = parent.gameObject.AddComponent<Ashlands70GroveVisual>(); visual.Grant = grant;
            var random = new System.Random(grant.Token.GetStableHashCode());
            int mask = LayerMask.GetMask("terrain", "static_solid", "piece", "Default");
            for (int i = 0; i < visual.Renderers.Length; i++)
            {
                var go = new GameObject("VM_NativeGroveClutter"); go.SetActive(false); go.transform.SetParent(parent, false);
                var renderer = go.AddComponent<InstanceRenderer>();
                renderer.m_mesh = sources[i].m_mesh; renderer.m_material = sources[i].m_material; renderer.m_scale = sources[i].m_scale;
                renderer.m_useLod = false; renderer.m_frustumCull = true; renderer.m_shadowCasting = ShadowCastingMode.Off;
                visual.Renderers[i] = renderer; go.SetActive(true);
            }
            for (int i = 0; i < visual.Positions.Length; i++)
            {
                float radius = Ashlands70Rules.GroveRadius * Mathf.Sqrt((i + .5f) / visual.Positions.Length);
                float angle = i * 2.39996323f + (float)random.NextDouble() * .2f;
                Vector3 point = grant.Origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Vector3 normal = Vector3.up;
                if (Physics.Raycast(point + Vector3.up * 4f, Vector3.down, out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore))
                { point = hit.point + Vector3.up * .015f; normal = hit.normal; }
                visual.Positions[i] = point;
                visual.Rotations[i] = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                visual.Sizes[i] = .7f + (float)random.NextDouble() * .5f;
            }
            visual.Redraw(); return true;
        }
        private void Update()
        { if (Time.unscaledTime >= NextTick) { NextTick = Time.unscaledTime + .1f; Redraw(); } }
        private void Redraw()
        {
            if (Grant == null) return;
            float age = (float)((Ashlands70Cast.Now - Grant.Started) / (double)System.TimeSpan.TicksPerSecond);
            float left = (float)((Grant.Until - Ashlands70Cast.Now) / (double)System.TimeSpan.TicksPerSecond);
            float scale = Mathf.Clamp01(age / .8f) * Mathf.Clamp01(left / 1.2f);
            if (Mathf.Abs(scale - PreviousScale) < .01f) return; PreviousScale = scale;
            foreach (InstanceRenderer renderer in Renderers) renderer.Clear();
            for (int i = 0; i < Positions.Length; i++) Renderers[i % Renderers.Length].AddInstance(Positions[i], Rotations[i], Sizes[i] * scale);
        }
        private void OnDestroy() { foreach (InstanceRenderer renderer in Renderers) if (renderer != null) renderer.Clear(); }
    }
}
