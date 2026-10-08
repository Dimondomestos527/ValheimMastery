using UnityEngine;
using System.Collections.Generic;

namespace ValheimMastery
{
    // One native-textured, terrain-conforming surface for at most eight patches.
    // Transparent vertex edges avoid billboard cards and a hard geometric rim.
    internal sealed class IceTrailVisual : MonoBehaviour
    {
        private const int ZoneCapacity = 8, Spokes = 24;
        private const int VerticesPerZone = 1 + Spokes * 2, IndicesPerZone = Spokes * 9;
        private readonly Vector3[] Points = new Vector3[ZoneCapacity];
        private readonly Vector3[] CachedCenters = new Vector3[ZoneCapacity];
        private readonly Vector3[] Vertices = new Vector3[ZoneCapacity * VerticesPerZone];
        private readonly Vector3[] Normals = new Vector3[ZoneCapacity * VerticesPerZone];
        private readonly Color[] Colors = new Color[ZoneCapacity * VerticesPerZone];
        private readonly Vector2[] Uvs = new Vector2[ZoneCapacity * VerticesPerZone];
        private readonly int[] Indices = new int[ZoneCapacity * IndicesPerZone];
        private Mesh Surface;
        private MeshRenderer Renderer;
        private Material OwnedMaterial;
        private int CachedZones = -1;
        private float NextAssetRetry;
        private const int FlakesPerZone = 150, AirbornePerZone = 128;
        private ParticleSystem Flakes;
        private ParticleSystem AirborneSnow;
        private readonly ParticleSystem.Particle[] SnowParticles = new ParticleSystem.Particle[ZoneCapacity * FlakesPerZone];
        private readonly ParticleSystem.Particle[] AirborneParticles = new ParticleSystem.Particle[ZoneCapacity * AirbornePerZone];


        internal static IceTrailVisual Create()
        {
            if (Player.m_localPlayer == null || Application.isBatchMode) return null;
            GameObject root = new GameObject("ValheimMastery_NativeIceTrail");
            IceTrailVisual visual = root.AddComponent<IceTrailVisual>();
            visual.TryInitialize();
            return visual;
        }

        private bool TryInitialize()
        {
            if (Flakes == null && Time.time >= NextAssetRetry)
                Flakes = NativeSnowVisualAssets.CreateSnow(transform, ZoneCapacity * FlakesPerZone);
            if (AirborneSnow == null && Time.time >= NextAssetRetry)
                AirborneSnow = NativeSnowVisualAssets.CreateSnow(transform, ZoneCapacity * AirbornePerZone);
            if (Surface == null)
            {
                Surface = new Mesh { name = "VM_FeatheredCaveIce" };
                gameObject.AddComponent<MeshFilter>().sharedMesh = Surface;
                Renderer = gameObject.AddComponent<MeshRenderer>();
                Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Renderer.receiveShadows = false; Renderer.enabled = false;
            }
            if (OwnedMaterial != null || Time.time < NextAssetRetry) return true;
            NextAssetRetry = Time.time + .25f;
            Material material = NativeSnowVisualAssets.CreateIceSurfaceMaterial();
            if (material == null) return true; // Native floor flakes must not wait for a separate cave material.
            OwnedMaterial = material;
            Renderer.sharedMaterial = material;
            return true;
        }

        private void LateUpdate()
        {
            // The first patch remains registered while asynchronous native assets
            // load, so the player need not cast a second patch to see it.
            if (!TryInitialize() || Renderer == null) return;
            int zones = Mathf.Clamp(IceStaff35Trail.FillZonePoints(Points), 0, ZoneCapacity);
            bool visible = zones > 0 && MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            Renderer.enabled = visible && OwnedMaterial != null;
            if (!visible)
            {
                if (Flakes != null) Flakes.Clear(); if (AirborneSnow != null) AirborneSnow.Clear();
                return;
            }
            bool changed = zones != CachedZones;
            for (int z = 0; z < zones; z++) if (Points[z] != CachedCenters[z]) changed = true;
            if (!changed) { DrawFlakes(zones); DrawAirborneSnow(zones); return; }
            int mask = LayerMask.GetMask("terrain", "static_solid", "piece", "Default");
            for (int z = 0; z < zones; z++)
            {
                if (CachedZones >= 0 && z < CachedZones && Points[z] == CachedCenters[z]) continue;
                CachedCenters[z] = Points[z];
                int start = z * VerticesPerZone, index = z * IndicesPerZone;
                Project(start, Points[z], .52f, mask);
                for (int p = 0; p < Spokes; p++)
                {
                    float angle = p * Mathf.PI * 2f / Spokes;
                    float edge = 1.65f * (.88f + .12f * Mathf.Sin(p * 2.39996f + z * .7f));
                    Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Project(start + 1 + p, Points[z] + direction * edge * .74f, .44f, mask);
                    Project(start + 1 + Spokes + p, Points[z] + direction * edge, 0f, mask);
                    int next = (p + 1) % Spokes;
                    int inner = start + 1 + p, innerNext = start + 1 + next;
                    int outer = inner + Spokes, outerNext = innerNext + Spokes;
                    Indices[index++] = start; Indices[index++] = innerNext; Indices[index++] = inner;
                    Indices[index++] = inner; Indices[index++] = innerNext; Indices[index++] = outerNext;
                    Indices[index++] = inner; Indices[index++] = outerNext; Indices[index++] = outer;
                }
            }
            Surface.Clear();
            int count = zones * VerticesPerZone;
            Surface.SetVertices(Vertices, 0, count); Surface.SetNormals(Normals, 0, count);
            Surface.SetColors(Colors, 0, count); Surface.SetUVs(0, Uvs, 0, count);
            Surface.SetTriangles(Indices, 0, zones * IndicesPerZone, 0, true);
            CachedZones = zones;
            DrawFlakes(zones);
            DrawAirborneSnow(zones);
        }

        private void DrawFlakes(int zones)
        {
            if (Flakes == null) return;
            int count = 0;
            for (int z = 0; z < zones; z++)
                for (int i = 0; i < FlakesPerZone; i++)
                {
                    int sector = i % Spokes;
                    float radial = Mathf.Sqrt((i + .5f) / FlakesPerZone);
                    // Reuse projected ground vertices: no particle collision,
                    // no per-frame terrain rays, no floating opaque ice cards.
                    Vector3 ground = Vector3.Lerp(Vertices[z * VerticesPerZone],
                        Vertices[z * VerticesPerZone + 1 + Spokes + sector], radial);
                    SnowParticles[count].position = ground + Vector3.up * (.04f + .015f * Mathf.Sin(Time.time * .8f + i));
                    SnowParticles[count].velocity = Vector3.zero;
                    SnowParticles[count].startSize = .035f + (i % 3) * .012f;
                    SnowParticles[count].startColor = new Color(.78f, .9f, 1f, .6f + .15f * Mathf.Sin(Time.time + i));
                    SnowParticles[count].startLifetime = SnowParticles[count].remainingLifetime = 2f;
                    count++;
                }
            Flakes.SetParticles(SnowParticles, count);
        }

        private void DrawAirborneSnow(int zones)
        {
            if (AirborneSnow == null) return;
            int count = 0;
            for (int z = 0; z < zones; z++)
                for (int i = 0; i < AirbornePerZone; i++)
                {
                    float age = Mathf.Repeat(Time.time * .19f + i * .618034f + z * .137f, 1f);
                    float radial = 1.85f * Mathf.Sqrt((i + .5f) / AirbornePerZone);
                    float angle = i * 2.39996f + Time.time * (.22f + (i % 5) * .025f);
                    float drift = Mathf.Sin(Time.time * .55f + i * .71f + z) * .12f;
                    AirborneParticles[count].position = Points[z] + new Vector3(
                        Mathf.Cos(angle) * radial + drift,
                        .24f + (1f - age) * 1.7f,
                        Mathf.Sin(angle) * radial);
                    AirborneParticles[count].velocity = new Vector3(-Mathf.Sin(angle) * .12f, .08f, Mathf.Cos(angle) * .12f);
                    AirborneParticles[count].startSize = .044f + (i % 4) * .008f;
                    AirborneParticles[count].startColor = new Color(.84f, .92f, 1f, .26f + .18f * Mathf.Sin(age * Mathf.PI));
                    AirborneParticles[count].randomSeed = (uint)(i + 1 + z * AirbornePerZone);
                    AirborneParticles[count].startLifetime = 5f;
                    AirborneParticles[count].remainingLifetime = 5f * (1f - age);
                    count++;
                }
            AirborneSnow.SetParticles(AirborneParticles, count);
        }

        private void Project(int index, Vector3 point, float alpha, int mask)
        {
            Vector3 normal = Vector3.up;
            if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 5f, mask))
            { point = ground.point; normal = ground.normal; }
            Vertices[index] = point + normal * .03f;
            Normals[index] = normal;
            Colors[index] = new Color(.78f, .90f, 1f, alpha);
            Uvs[index] = new Vector2(point.x * .45f, point.z * .45f);
        }

        private void OnDestroy()
        {
            if (Flakes != null) Destroy(Flakes.gameObject);
            if (AirborneSnow != null) Destroy(AirborneSnow.gameObject);
            if (OwnedMaterial != null) Destroy(OwnedMaterial);
            if (Surface != null) Destroy(Surface);
        }
    }
}
