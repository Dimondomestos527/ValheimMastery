using UnityEngine;
namespace ValheimMastery
{
    // Cached cosmetic ground and one aggregate native-simulated snow layer.
    internal sealed class IceTrailVisual : MonoBehaviour
    {
        private const int ZoneCapacity = MistlandsFrostVisualRules.Zones, Spokes = 24;
        private const int VerticesPerZone = 1 + Spokes * 2, IndicesPerZone = Spokes * 9;
        private readonly Vector3[] Points = new Vector3[ZoneCapacity], CachedCenters = new Vector3[ZoneCapacity];
        private readonly Vector3[] Vertices = new Vector3[ZoneCapacity * VerticesPerZone], Normals = new Vector3[ZoneCapacity * VerticesPerZone];
        private readonly Color[] Colors = new Color[ZoneCapacity * VerticesPerZone];
        private readonly Vector2[] Uvs = new Vector2[ZoneCapacity * VerticesPerZone];
        private readonly int[] Indices = new int[ZoneCapacity * IndicesPerZone], Tiers = new int[ZoneCapacity];
        private readonly bool[] VisibleZones = new bool[ZoneCapacity];
        private readonly Plane[] Frustum = new Plane[6];
        private readonly float[] EmitCredits = new float[ZoneCapacity];
        private readonly int[] AirSerial = new int[ZoneCapacity];
        private readonly Vector3[] AirOffsets = new Vector3[MistlandsFrostVisualRules.AirNear], AirVelocity = new Vector3[MistlandsFrostVisualRules.AirNear];
        private readonly ParticleSystem.Particle[] GroundCache = new ParticleSystem.Particle[ZoneCapacity * MistlandsFrostVisualRules.GroundNear];
        private readonly ParticleSystem.Particle[] SnowParticles = new ParticleSystem.Particle[ZoneCapacity * MistlandsFrostVisualRules.GroundNear];
        private Mesh Surface;
        private MeshRenderer Renderer;
        private Material OwnedMaterial;
        private ParticleSystem Flakes, AirborneSnow;
        private Camera Viewer;
        private int CachedZones = -1, LastVisibilityZones = -1;
        private float NextAssetRetry, NextVisibility, NextGroundRefresh, LastAirTick;
        private bool Playing, GroundDirty = true;

        internal static IceTrailVisual Create()
        {
            if (Player.m_localPlayer == null || Application.isBatchMode || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return null;
            return new GameObject("ValheimMastery_NativeIceTrail").AddComponent<IceTrailVisual>();
        }
        private void Awake()
        {
            for (int i = 0; i < AirOffsets.Length; i++)
            {
                float angle = i * 2.39996f, radial = 1.75f * Mathf.Sqrt((i + .5f) / AirOffsets.Length);
                AirOffsets[i] = new Vector3(Mathf.Cos(angle) * radial, 0f, Mathf.Sin(angle) * radial);
                AirVelocity[i] = new Vector3(Mathf.Cos(angle + .9f) * .08f, -.38f - (i % 5) * .03f, Mathf.Sin(angle + .9f) * .08f);
            }
        }
        private bool TryInitialize()
        {
            if (Flakes == null && Time.time >= NextAssetRetry)
                Flakes = NativeSnowVisualAssets.CreateSnow(transform, SnowParticles.Length);
            if (AirborneSnow == null && Time.time >= NextAssetRetry)
            {
                AirborneSnow = NativeSnowVisualAssets.CreateSnow(transform, ZoneCapacity * MistlandsFrostVisualRules.AirNear);
                if (AirborneSnow != null)
                {
                    var noise = AirborneSnow.noise; noise.enabled = true; noise.strength = .075f;
                    noise.frequency = .22f; noise.scrollSpeed = .1f; noise.octaveCount = 1; noise.quality = ParticleSystemNoiseQuality.Low;
                    var color = AirborneSnow.colorOverLifetime; color.enabled = true;
                    var gradient = new Gradient();
                    gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .15f), new GradientAlphaKey(1f, .75f), new GradientAlphaKey(0f, 1f) });
                    color.color = gradient;
                    Playing = false;
                }
            }
            if (Surface == null)
            {
                Surface = new Mesh { name = "VM_FeatheredCaveIce" };
                gameObject.AddComponent<MeshFilter>().sharedMesh = Surface;
                Renderer = gameObject.AddComponent<MeshRenderer>();
                Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Renderer.receiveShadows = false; Renderer.enabled = false;
            }
            if (OwnedMaterial == null && Time.time >= NextAssetRetry)
            {
                NextAssetRetry = Time.time + .25f;
                OwnedMaterial = NativeSnowVisualAssets.CreateIceSurfaceMaterial();
                if (OwnedMaterial != null) Renderer.sharedMaterial = OwnedMaterial;
            }
            return Renderer != null;
        }
        private void Suspend()
        {
            if (Renderer != null) Renderer.enabled = false;
            if (!Playing) return;
            if (Flakes != null) Flakes.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (AirborneSnow != null) AirborneSnow.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            Playing = false; GroundDirty = true; LastAirTick = Time.time;
        }
        private bool Visibility(int zones)
        {
            if (Time.time < NextVisibility && zones == LastVisibilityZones)
            {
                for (int z = 0; z < zones; z++) if (VisibleZones[z]) return true;
                return false;
            }
            NextVisibility = Time.time + MistlandsFrostVisualRules.VisibilityInterval; LastVisibilityZones = zones;
            if (Viewer == null) Viewer = Camera.main;
            bool frustum = Viewer != null;
            if (frustum) GeometryUtility.CalculateFrustumPlanes(Viewer, Frustum);
            Vector3 eye = frustum ? Viewer.transform.position : Player.m_localPlayer.transform.position;
            bool any = false;
            for (int z = 0; z < zones; z++)
            {
                int tier = MistlandsFrostVisualRules.Tier((Points[z] - eye).sqrMagnitude, Tiers[z]);
                bool visible = tier < 3 && (!frustum || GeometryUtility.TestPlanesAABB(Frustum, new Bounds(Points[z] + Vector3.up, new Vector3(5f, 5f, 5f))));
                if (visible != VisibleZones[z] || tier != Tiers[z]) GroundDirty = true;
                VisibleZones[z] = visible; Tiers[z] = tier; any |= visible;
            }
            return any;
        }
        private void LateUpdate()
        {
            if (Application.isBatchMode || Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value)
            { Suspend(); return; }
            int zones = Mathf.Clamp(IceStaff35Trail.FillZonePoints(Points), 0, ZoneCapacity);
            if (zones == 0 || !Visibility(zones)) { Suspend(); return; }
            if (!TryInitialize()) return;
            Renderer.enabled = OwnedMaterial != null;
            bool changed = zones != CachedZones;
            for (int z = 0; z < zones; z++) if (Points[z] != CachedCenters[z]) changed = true;
            if (changed)
            {
                BuildSurface(zones); CacheGround(zones); GroundDirty = true;
                if (AirborneSnow != null) AirborneSnow.Clear(false);
                for (int z = 0; z < ZoneCapacity; z++) EmitCredits[z] = 0f;
                Playing = false;
            }
            if (!Playing)
            {
                if (Flakes != null) Flakes.Play(false);
                if (AirborneSnow != null) { AirborneSnow.Play(false); PrimeAir(zones); }
                Playing = true; GroundDirty = true; LastAirTick = Time.time;
            }
            if (GroundDirty || Time.time >= NextGroundRefresh) RefreshGround(zones);
            if (Time.time - LastAirTick >= MistlandsFrostVisualRules.EmitInterval)
            {
                float elapsed = Time.time - LastAirTick; LastAirTick = Time.time;
                if (AirborneSnow != null)
                    for (int z = 0; z < zones; z++)
                    {
                        int population = VisibleZones[z] ? MistlandsFrostVisualRules.Air(Tiers[z]) : 0;
                        int count = MistlandsFrostVisualRules.EmitCount(ref EmitCredits[z], population, elapsed);
                        for (int i = 0; i < count; i++) EmitAir(z, MistlandsFrostVisualRules.AirLifetime);
                    }
            }
        }
        private void BuildSurface(int zones)
        {
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
        }
        private void CacheGround(int zones)
        {
            for (int z = 0; z < zones; z++)
                for (int i = 0; i < MistlandsFrostVisualRules.GroundNear; i++)
                {
                    int index = z * MistlandsFrostVisualRules.GroundNear + i;
                    float radial = Mathf.Sqrt((i + .5f) / MistlandsFrostVisualRules.GroundNear);
                    GroundCache[index].position = Vector3.Lerp(Vertices[z * VerticesPerZone], Vertices[z * VerticesPerZone + 1 + Spokes + i % Spokes], radial) + Vector3.up * .05f;
                    GroundCache[index].velocity = Vector3.zero;
                    GroundCache[index].startSize = .035f + (i % 3) * .012f;
                    GroundCache[index].startColor = new Color(.78f, .9f, 1f, .55f);
                    GroundCache[index].startLifetime = GroundCache[index].remainingLifetime = 30f;
                }
        }
        private void RefreshGround(int zones)
        {
            if (Flakes == null) return; // Keep dirty while the native material loads.
            int count = 0;
            for (int z = 0; z < zones; z++)
            {
                int budget = VisibleZones[z] ? MistlandsFrostVisualRules.Ground(Tiers[z]) : 0;
                for (int i = 0; i < budget; i++)
                    SnowParticles[count++] = GroundCache[z * MistlandsFrostVisualRules.GroundNear + (budget == 32 ? i * 2 : i)];
            }
            Flakes.SetParticles(SnowParticles, count);
            GroundDirty = false; NextGroundRefresh = Time.time + 10f;
        }
        private void PrimeAir(int zones)
        {
            for (int z = 0; z < zones; z++)
            {
                int count = VisibleZones[z] ? MistlandsFrostVisualRules.Air(Tiers[z]) : 0;
                for (int i = 0; i < count; i++) EmitAir(z, .25f + (i + .5f) / count * 3.75f);
            }
        }
        private void EmitAir(int zone, float life)
        {
            int sample = AirSerial[zone]++ % AirOffsets.Length;
            // Deterministic cosmetic samples; never use gameplay UnityEngine.Random.
            var emit = new ParticleSystem.EmitParams
            {
                position = Points[zone] + AirOffsets[sample] + Vector3.up * (.24f + life * .5f),
                velocity = AirVelocity[sample], startLifetime = life,
                startSize = .044f + (sample % 4) * .008f,
                startColor = new Color(.84f, .92f, 1f, .38f), randomSeed = (uint)(sample + 1 + zone * AirOffsets.Length)
            };
            AirborneSnow.Emit(emit, 1);
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
