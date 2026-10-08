using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Trusted weather particles only, not a weather controller. The clone retains
    // vanilla shader bindings, meshes, UV streams, gradients, velocity and noise.
    // It never installs an environment, changes fog, or creates a network object.
    internal sealed class NativeStormWeatherVisual : MonoBehaviour
    {
        internal const int ParticleBudget = 448;
        private const int FlakeBudget = 352, ClusterBudget = 96;
        private static GameObject Staging;
        private static float NextTextureWarning;
        private ParticleSystem[] Systems;
        private readonly ParticleSystem.Particle[] Buffer = new ParticleSystem.Particle[ParticleBudget];
        private float Radius, NextBoundsCheck;

        internal static NativeStormWeatherVisual Create(Transform parent, float radius)
        {
            if (Application.isBatchMode || Player.m_localPlayer == null || parent == null ||
                !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return null;
            GameObject source = FindSource();
            if (source == null) return null; // Pending/invalid native texture: no square-card fallback.
            ParticleSystem[] originals = source.GetComponentsInChildren<ParticleSystem>(true);
            int flake = FindEmitter(originals, "snow_flake", -1);
            int cluster = FindEmitter(originals, "snow_cluster", flake);
            if (flake < 0 && cluster < 0) return null;
            if (Staging == null)
            { Staging = new GameObject("VM_InactiveNativeWeather"); Staging.SetActive(false); }
            GameObject clone = UnityEngine.Object.Instantiate(source, Staging.transform, false);
            clone.SetActive(false);
            ParticleSystem[] copies = clone.GetComponentsInChildren<ParticleSystem>(true);
            if (copies.Length != originals.Length || !StripControllers(clone))
            { UnityEngine.Object.DestroyImmediate(clone); return null; }
            // A live environment reference may itself be the particle-system
            // root. Use an owned wrapper so an emitter is never parented to itself.
            GameObject root = new GameObject("VM_Storm70_NativeWeather");
            root.transform.SetParent(Staging.transform, false); root.SetActive(false);
            clone.transform.SetParent(root.transform, false);

            List<ParticleSystem> selected = new List<ParticleSystem>(2);
            bool two = flake >= 0 && cluster >= 0;
            for (int i = 0; i < copies.Length; i++)
            {
                ParticleSystem copy = copies[i];
                copy.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = copy.main; main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
                var collision = copy.collision; collision.enabled = false;
                var trigger = copy.trigger; trigger.enabled = false;
                var subEmitters = copy.subEmitters; subEmitters.enabled = false;
                ParticleSystemRenderer renderer = copy.GetComponent<ParticleSystemRenderer>();
                if (i != flake && i != cluster)
                {
                    var disabled = copy.emission; disabled.enabled = false;
                    main.maxParticles = 0;
                    if (renderer != null) renderer.enabled = false;
                    continue;
                }
                CopyPropertyBlocks(originals[i].GetComponent<ParticleSystemRenderer>(), renderer);
                int budget = !two ? ParticleBudget : i == flake ? FlakeBudget : ClusterBudget;
                main.maxParticles = Mathf.Min(Mathf.Max(1, main.maxParticles), budget);
                main.loop = true;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                copy.transform.SetParent(root.transform, false);
                copy.transform.localPosition = Vector3.up * 4f;
                copy.transform.localRotation = originals[i].transform.rotation;
                copy.transform.localScale = Vector3.one;
                copy.gameObject.SetActive(true);
                var shape = copy.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
                shape.position = Vector3.zero; shape.rotation = Vector3.zero;
                // Fill the control volume, not a thin ceiling whose flakes may
                // leave the radius before they reach the player's eye level.
                shape.scale = new Vector3(radius * 1.9f, 8f, radius * 1.9f);
                var emission = copy.emission; emission.enabled = true;
                // Keep native emission where already modest, bound spawn work as
                // well as live particle count. Native lifetime/motion stay intact.
                float nativeRate = emission.rateOverTime.constantMax;
                emission.rateOverTime = Mathf.Clamp(nativeRate, budget / 3f, budget / 1.5f);
                emission.rateOverDistance = 0f;
                renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                selected.Add(copy);
            }
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            NativeStormWeatherVisual visual = root.AddComponent<NativeStormWeatherVisual>();
            visual.Systems = selected.ToArray(); visual.Radius = radius;
            root.SetActive(true);
            foreach (ParticleSystem particles in visual.Systems) particles.Play(false);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Storm70Native] isolated source=" + source.name +
                    " emitters=" + selected.Count + " aggregate particle cap=" + ParticleBudget);
            return visual;
        }

        private static GameObject FindSource()
        {
            GameObject source = NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/weather/SnowStorm.prefab");
            if (HasSnow(source)) return source;
            // Live native renderers may hold shader property blocks not serialized
            // on the prefab. Prefer the actual Mountain environment references.
            var biomes = EnvMan.instance?.m_biomes;
            if (biomes != null)
                foreach (var biome in biomes)
                    if (biome.m_biome == Heightmap.Biome.Mountain)
                        foreach (var entry in biome.m_environments)
                        {
                            EnvSetup environment = entry.m_env;
                            if (environment == null) continue;
                            foreach (GameObject root in environment.m_psystems ?? Array.Empty<GameObject>())
                                if (HasSnow(root)) return root;
                        }
            if (source != null && MasteryPlugin.Settings.VerboseLogging.Value && Time.time >= NextTextureWarning)
            {
                NextTextureWarning = Time.time + 10f;
                List<string> bindings = new List<string>();
                foreach (var renderer in source.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || bindings.Count >= 8) continue;
                        foreach (string property in material.GetTexturePropertyNames())
                        {
                            if (bindings.Count >= 8) break;
                            bindings.Add(material.name + "/" + property + "=" + (material.GetTexture(property)?.name ?? "null"));
                        }
                    }
                MasteryPlugin.Log.LogWarning("[Storm70Native] Native weather has no verified textured snow emitter; skipped, not replaced by cards. Bindings=" + string.Join(",", bindings));
            }
            return null;
        }

        private static bool HasSnow(GameObject root)
        {
            if (root == null) return false;
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            return FindEmitter(systems, "snow_flake", -1) >= 0 || FindEmitter(systems, "snow_cluster", -1) >= 0;
        }

        private static int FindEmitter(ParticleSystem[] systems, string materialName, int exclude)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (i == exclude) continue;
                ParticleSystemRenderer renderer = systems[i].GetComponent<ParticleSystemRenderer>();
                if (renderer == null) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || !material.name.StartsWith(materialName, StringComparison.OrdinalIgnoreCase)) continue;
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block, materialIndex);
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = block.GetTexture(property) ?? material.GetTexture(property);
                        if (texture != null && texture != Texture2D.whiteTexture && texture != Texture2D.blackTexture)
                            return i;
                    }
                    renderer.GetPropertyBlock(block);
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = block.GetTexture(property);
                        if (texture != null && texture != Texture2D.whiteTexture && texture != Texture2D.blackTexture)
                            return i;
                    }
                }
            }
            return -1;
        }

        private static void CopyPropertyBlocks(ParticleSystemRenderer source, ParticleSystemRenderer target)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            source.GetPropertyBlock(block); target.SetPropertyBlock(block);
            for (int i = 0; i < source.sharedMaterials.Length; i++)
            { block.Clear(); source.GetPropertyBlock(block, i); target.SetPropertyBlock(block, i); }
        }

        private static bool StripControllers(GameObject clone)
        {
            foreach (MonoBehaviour script in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && !(script is ZNetView)) UnityEngine.Object.DestroyImmediate(script);
            foreach (ZNetView view in clone.GetComponentsInChildren<ZNetView>(true)) UnityEngine.Object.DestroyImmediate(view);
            if (clone.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) return false;
            foreach (AudioSource audio in clone.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(audio);
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
            foreach (ParticleSystemForceField field in clone.GetComponentsInChildren<ParticleSystemForceField>(true)) UnityEngine.Object.DestroyImmediate(field);
            foreach (Light light in clone.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
            foreach (Camera camera in clone.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(camera);
            foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                if (!(renderer is ParticleSystemRenderer)) renderer.enabled = false;
            return true;
        }

        private void Update()
        {
            if (Time.time < NextBoundsCheck) return;
            NextBoundsCheck = Time.time + .15f;
            // Native emitters retain their smooth native motion. Only trim escaped
            // flakes; no per-frame manual particle lattice/ring or global fog.
            foreach (ParticleSystem particles in Systems)
            {
                int count = particles.GetParticles(Buffer), kept = 0;
                for (int i = 0; i < count; i++)
                {
                    Vector3 point = transform.InverseTransformPoint(particles.transform.TransformPoint(Buffer[i].position));
                    if (point.y < -.2f || point.y > 8.3f || point.x * point.x + point.z * point.z > Radius * Radius) continue;
                    Buffer[kept++] = Buffer[i];
                }
                if (kept != count) particles.SetParticles(Buffer, kept);
            }
        }

        internal void Stop()
        {
            foreach (ParticleSystem particles in Systems ?? Array.Empty<ParticleSystem>())
                if (particles != null) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false); Destroy(gameObject);
        }
    }
}
