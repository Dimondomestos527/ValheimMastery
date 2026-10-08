#if MASTERY_SHIELD_RUSH_EXPERIMENT
using UnityEngine;

namespace ValheimMastery
{
    internal static class ShieldRushImpactVisual
    {
        internal static void Play(Vector3 point, float radius)
        {
            if (Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            GameObject source = NativePerkAssetResolver.Resolve("vfx_blocked");
            if (source == null)
            { NativeVfxSafeFrame.Request("vfx_blocked", loaded => Spawn(loaded, point, radius)); return; }
            Spawn(source, point, radius);
        }
        private static bool Spawn(GameObject source, Vector3 point, float radius)
        {
            if (Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return true;
            GameObject root = VfxPool.Spawn(source, point, Quaternion.identity, .65f);
            if (root == null) return false;
            NativeBurstPlayback.Mute(root);
            ParticleSystem wave = null;
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
                if (NativeBurstPlayback.HasMaterial(system, "block_wave")) { wave = system; break; }
            if (wave == null) { NativeVfxSafeFrame.Warn("vfx_blocked", "block_wave absent"); root.SetActive(true); return true; }
            var main = wave.main;
            main.startSize = Mathf.Clamp(radius, .5f, 4f) * 2f;
            main.maxParticles = 1;
            var renderer = wave.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.enabled = true;
            NativeVfxSafeFrame.AfterReady(root, () =>
            {
                if (wave == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
                for (Transform node = wave.transform; node != null && node != root.transform; node = node.parent)
                    node.gameObject.SetActive(true);
                NativeBurstPlayback.EmitOnce(wave, 1);
            });
            root.SetActive(true);
            return true;
        }
    }
}
#endif
