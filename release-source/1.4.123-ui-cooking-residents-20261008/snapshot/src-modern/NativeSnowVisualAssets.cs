using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Identities verified in the installed game's SoftRef manifest: SnowStorm,
    // snow_flake, Ice_floor / cave_ice_floor / icefloor_d. Read native references;
    // never instantiate a weather, cave, network or gameplay prefab.
    internal static class NativeSnowVisualAssets
    {
        private static ParticleSystem SnowSource;
        private static Material IceSource;
        private static float NextSnowLookup, NextIceLookup;
        private static ZNetScene Scene;

        private static void CheckScene()
        {
            if (Scene == ZNetScene.instance) return;
            Scene = ZNetScene.instance; SnowSource = null; IceSource = null;
            NextSnowLookup = NextIceLookup = 0f;
        }

        internal static ParticleSystem Snow()
        {
            CheckScene();
            if (SnowSource != null) return SnowSource;
            GameObject catalogued = NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/weather/SnowStorm.prefab");
            if (FindSnow(catalogued) != null) return SnowSource;
            if (Time.time < NextSnowLookup) return null;
            NextSnowLookup = Time.time + 3f;
            var environments = EnvMan.instance?.m_environments;
            if (environments != null)
                foreach (var environment in environments)
                {
                    foreach (GameObject root in environment.m_psystems ?? Array.Empty<GameObject>())
                        if (FindSnow(root) != null) return SnowSource;
                    if (FindSnow(environment.m_envObject) != null) return SnowSource;
                }
            // Weather assets are not all registered in ZNetScene. Inspect loaded
            // emitters, including inactive weather templates, by their real material.
            foreach (ParticleSystem candidate in Resources.FindObjectsOfTypeAll<ParticleSystem>())
                if (AcceptSnow(candidate)) return SnowSource;
            return null; // no landing-dust or opaque-card fallback
        }

        private static ParticleSystem FindSnow(GameObject root)
        {
            if (root == null) return null;
            foreach (ParticleSystem candidate in root.GetComponentsInChildren<ParticleSystem>(true))
                if (AcceptSnow(candidate)) return SnowSource;
            return null;
        }

        private static bool AcceptSnow(ParticleSystem candidate)
        {
            var renderer = candidate?.GetComponent<ParticleSystemRenderer>();
            Material material = renderer?.sharedMaterial;
            // Native shaders may not label their snow texture as mainTexture;
            // native flakes may also use a textured quad mesh. Both are valid.
            if (material == null ||
                !material.name.StartsWith("snow_flake", StringComparison.OrdinalIgnoreCase)) return false;
            SnowSource = candidate;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[NativeSnow] source=" + candidate.name + " material=" + material.name +
                    " texture=" + (material.mainTexture != null ? material.mainTexture.name : "native shader property") + " flipbook=" + candidate.textureSheetAnimation.enabled);
            return true;
        }

        internal static ParticleSystem CreateSnow(Transform parent, int capacity)
        {
            ParticleSystem native = Snow();
            Material material = native?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial ??
                NativeSoftVisualAssets.Get<Material>("Assets/Effects/materials/snow_flake.mat");
            if (material == null) return null;
            GameObject root = new GameObject("VM_NativeSnowFlakes"); root.transform.SetParent(parent, false);
            ParticleSystem snow = root.AddComponent<ParticleSystem>();
            snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = snow.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = Mathf.Clamp(capacity, 1, 2048);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f; main.startLifetime = 4f;
            var emission = snow.emission; emission.enabled = false;
            var shape = snow.shape; shape.enabled = false;
            var collision = snow.collision; collision.enabled = false;
            var trigger = snow.trigger; trigger.enabled = false;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            var source = native?.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            if (source != null)
            {
                renderer.renderMode = source.renderMode;
                if (source.renderMode == ParticleSystemRenderMode.Mesh) renderer.mesh = source.mesh;
                renderer.enableGPUInstancing = source.enableGPUInstancing;
                renderer.alignment = source.alignment; renderer.pivot = source.pivot;
                renderer.normalDirection = source.normalDirection;
                renderer.velocityScale = source.velocityScale; renderer.lengthScale = source.lengthScale;
                renderer.minParticleSize = source.minParticleSize; renderer.maxParticleSize = source.maxParticleSize;
                var streams = new List<ParticleSystemVertexStream>();
                source.GetActiveVertexStreams(streams); renderer.SetActiveVertexStreams(streams);
                CopySheet(native, snow);
            }
            else
            {
                // Exact native snow material remains usable even when weather
                // is implemented by another emitter/controller in this game build.
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            snow.Play();
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[NativeSnow] emitter created; material=" + material.name + " source=" + (native != null ? native.name : "catalog material") + " capacity=" + main.maxParticles);
            return snow;
        }

        internal static void CopySheet(ParticleSystem native, ParticleSystem snow)
        {
            var source = native.textureSheetAnimation; var sheet = snow.textureSheetAnimation;
            sheet.enabled = source.enabled;
            if (!source.enabled) return;
            sheet.mode = source.mode; sheet.timeMode = source.timeMode; sheet.fps = source.fps;
            sheet.numTilesX = source.numTilesX; sheet.numTilesY = source.numTilesY;
            sheet.animation = source.animation; sheet.rowMode = source.rowMode; sheet.rowIndex = source.rowIndex;
            sheet.frameOverTime = source.frameOverTime; sheet.startFrame = source.startFrame;
            sheet.cycleCount = source.cycleCount; sheet.uvChannelMask = source.uvChannelMask;
            sheet.speedRange = source.speedRange;
            if (source.mode == ParticleSystemAnimationMode.Sprites)
                for (int i = 0; i < source.spriteCount; i++) sheet.AddSprite(source.GetSprite(i));
        }

        internal static Material CreateIceSurfaceMaterial()
        {
            CheckScene();
            // Start both verified loads together; pending assets are retried cheaply.
            Material catalogued = NativeSoftVisualAssets.Get<Material>("Assets/world/Props/Caverocks/materials/cave_ice_floor.mat");
            if (catalogued != null) AcceptIce(catalogued);
            ParticleSystem snow = Snow();
            Material snowMaterial = snow?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial ??
                NativeSoftVisualAssets.Get<Material>("Assets/Effects/materials/snow_flake.mat");
            if (snowMaterial == null) return null;
            if (IceSource == null && Time.time >= NextIceLookup)
            {
                NextIceLookup = Time.time + 3f;
                GameObject floor = NativePerkAssetResolver.Resolve("Ice_floor");
                if (floor != null)
                    foreach (Renderer renderer in floor.GetComponentsInChildren<Renderer>(true))
                        foreach (Material candidate in renderer.sharedMaterials)
                            if (AcceptIce(candidate)) break;
                if (IceSource == null)
                    foreach (Material candidate in Resources.FindObjectsOfTypeAll<Material>())
                        if (AcceptIce(candidate)) break;
            }
            if (IceSource?.mainTexture == null) return null;
            // Native flake shader supplies texture * vertex alpha; cave-floor
            // diffuse supplies actual ice detail. Only this owned copy is retuned.
            var material = new Material(snowMaterial);
            material.name = "VM_NativeCaveIceSurface";
            material.mainTexture = IceSource.mainTexture;
            material.mainTextureScale = IceSource.mainTextureScale;
            material.mainTextureOffset = IceSource.mainTextureOffset;
            material.DisableKeyword("SOFTPARTICLES_ON");
            if (material.HasProperty("_SoftParticlesEnabled")) material.SetFloat("_SoftParticlesEnabled", 0f);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[NativeIce] material=" + IceSource.name + " texture=" + IceSource.mainTexture.name);
            return material;
        }

        private static bool AcceptIce(Material candidate)
        {
            if (candidate == null || candidate.mainTexture == null ||
                !candidate.name.StartsWith("cave_ice_floor", StringComparison.OrdinalIgnoreCase) ||
                !candidate.mainTexture.name.StartsWith("icefloor_d", StringComparison.OrdinalIgnoreCase)) return false;
            IceSource = candidate;
            return true;
        }
    }
}
