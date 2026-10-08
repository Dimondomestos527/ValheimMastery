using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Cosmetic clones only. Instantiate below an inactive parent so vanilla Awake/OnEnable
    // cannot register a network object, play audio, shake the camera or apply an AoE first.
    internal static class PerkNativeFeedback
    {
        private static GameObject Staging;
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static readonly HashSet<int> FailedPrefabs = new HashSet<int>();
        private static ZNetScene Scene;

        internal static GameObject PlayVfx(string prefabName, Vector3 position, float scale = 1f,
            float lifetime = 3f, Quaternion? rotation = null)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value || ZNetScene.instance == null ||
                Player.m_localPlayer == null) return null;
            GameObject prefab = NativePerkAssetResolver.Resolve(prefabName);
            if (prefab == null)
            {
                NativeVfxSafeFrame.Request(prefabName, loaded => PlayResolved(loaded, position, scale, lifetime, rotation ?? Quaternion.identity) != null);
                return null;
            }
            return PlayResolved(prefab, position, scale, lifetime, rotation ?? Quaternion.identity);
        }
        private static GameObject PlayResolved(GameObject prefab, Vector3 position, float scale, float lifetime, Quaternion rotation)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value || Player.m_localPlayer == null || ZNetScene.instance == null) return null;
            GameObject instance = VfxPool.Spawn(prefab, position, rotation, lifetime);
            if (instance == null) return null;
            instance.transform.localScale *= Mathf.Clamp(scale, 0.05f, 4f);
            instance.SetActive(true);
            instance.GetComponent<VfxPoolBaseline>()?.RestartParticles();
            return instance;
        }

        internal static GameObject CreateVisualOnly(GameObject prefab)
        {
            if (prefab == null || (!prefab.name.StartsWith("vfx_", StringComparison.OrdinalIgnoreCase) &&
                !prefab.name.StartsWith("fx_", StringComparison.OrdinalIgnoreCase))) return null;
            if (Scene != ZNetScene.instance)
            { Scene = ZNetScene.instance; FailedPrefabs.Clear(); Missing.Clear(); }
            if (FailedPrefabs.Contains(prefab.GetInstanceID()) || !NativeVfxSafeFrame.CanStage) return null;
            if (Staging == null)
            {
                Staging = new GameObject("ValheimMastery_InactiveVfxStaging");
                Staging.SetActive(false);
            }
            // Callers keep a synchronous, tunable cosmetic root. Native content
            // remains explicitly inactive even when that root is activated or
            // reparented inside a physics/animation callback.
            GameObject wrapper = null;
            try
            {
                wrapper = new GameObject(prefab.name);
                wrapper.SetActive(false); wrapper.transform.SetParent(Staging.transform, false);
                GameObject instance = UnityEngine.Object.Instantiate(prefab, wrapper.transform, false);
                instance.SetActive(false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                CopyRendererBindings(prefab, instance);
                ConfigureParticles(instance);
                ConfigureLights(instance);
                wrapper.AddComponent<DeferredNativeVfx>().Initialize(instance, prefab);
                wrapper.transform.SetParent(null, false);
                return wrapper;
            }
            catch (Exception error)
            {
                Fail(prefab, error.GetType().Name);
                if (wrapper != null) { wrapper.SetActive(false); UnityEngine.Object.Destroy(wrapper); }
                return null;
            }
        }

        // Called ONLY by the safe LateUpdate coordinator, never by a proc's
        // animation/contact callback. Immediate component removal is legal here.
        internal static bool Sanitize(GameObject instance)
        {
            if (!NativeVfxSafeFrame.IsDraining || instance == null || instance.activeInHierarchy) return false;
            // Remove scripts on the INSTANCE while Awake is still deferred. Pure particle,
            // mesh, line and animation components retain the game's materials and textures.
            foreach (MonoBehaviour script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && !(script is ZNetView)) UnityEngine.Object.DestroyImmediate(script);
            // Dependants first: destroying a required ZNetView before its AoE/other owner
            // can fail and leave that view alive for the subsequent activation.
            foreach (ZNetView view in instance.GetComponentsInChildren<ZNetView>(true))
                if (view != null) UnityEngine.Object.DestroyImmediate(view);
            if (instance.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                return false; // caller retains inactive content and destroys its wrapper normally
            foreach (AudioSource audio in instance.GetComponentsInChildren<AudioSource>(true))
                if (audio != null) UnityEngine.Object.DestroyImmediate(audio);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
                if (body != null) UnityEngine.Object.DestroyImmediate(body);
            if (instance.GetComponentsInChildren<AudioSource>(true).Length != 0 ||
                instance.GetComponentsInChildren<Collider>(true).Length != 0 ||
                instance.GetComponentsInChildren<Rigidbody>(true).Length != 0) return false;
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
            { animator.fireEvents = false; animator.applyRootMotion = false; }
            return true;
        }
        private static void ConfigureParticles(GameObject instance)
        {
            foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.stopAction = ParticleSystemStopAction.None; // pool owns lifetime
                main.maxParticles = Mathf.Min(main.maxParticles, 256);
                ParticleSystem.CollisionModule collision = particles.collision;
                collision.enabled = false;
                ParticleSystem.TriggerModule trigger = particles.trigger;
                trigger.enabled = false;
            }
        }
        private static void CopyRendererBindings(GameObject source, GameObject clone)
        {
            Renderer[] originals = source.GetComponentsInChildren<Renderer>(true), copies = clone.GetComponentsInChildren<Renderer>(true);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            for (int i = 0; i < Mathf.Min(originals.Length, copies.Length); i++)
            {
                block.Clear(); originals[i].GetPropertyBlock(block); copies[i].SetPropertyBlock(block);
                for (int material = 0; material < originals[i].sharedMaterials.Length; material++)
                { block.Clear(); originals[i].GetPropertyBlock(block, material); copies[i].SetPropertyBlock(block, material); }
            }
        }
        private static void ConfigureLights(GameObject instance)
        {
            // Safe inactive property tuning precedes the pool's baseline capture,
            // so a reused lease cannot restore the uncapped native light values.
            foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            {
                light.shadows = LightShadows.None;
                light.range = Mathf.Min(light.range, 10f);
                light.intensity = Mathf.Min(light.intensity, 3f);
            }
        }
        internal static void Fail(GameObject prefab, string reason)
        {
            if (prefab != null) FailedPrefabs.Add(prefab.GetInstanceID());
            string name = prefab != null ? prefab.name : "destroyed asset";
            if (Missing.Add("unsafe:" + name)) MasteryPlugin.Log.LogWarning("[NativeVFX] " + name + " skipped: " + reason + "; inactive clone cleaned up.");
        }
    }
}
