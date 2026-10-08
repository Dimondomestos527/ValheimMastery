using UnityEngine;

namespace ValheimMastery
{
    // Some local environment/character effects are not registered in ZNetScene.
    // Resolve their real prefab references rather than guessing a scene name.
    internal static class NativePerkAssetResolver
    {
        // 1.4.105's approved Run route used this native character reference when
        // the local jump VFX was not registered in ZNetScene. Do not silently
        // replace it with a different catalog effect or the old dodge fallback.
        internal static GameObject ResolveLegacyRun()
        {
            // Native Player jump effects are audio-only in this installation.
            // The old fallback had visible gold streaks/ring; current soft-loaded
            // fx_land instead produced nearly invisible dust (confirmed logs).
            return ZNetScene.instance?.GetPrefab("fx_perfectdodge") ??
                NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/fx_perfectdodge.prefab");
        }

        internal static GameObject Resolve(string name)
        {
            var scene = ZNetScene.instance;
            var prefab = scene?.GetPrefab(name);
            if (prefab != null) return prefab;
            if (name == "fx_land")
            {
                // Exact catalog identity; an unloaded landing asset is pending,
                // not permission to substitute an arbitrary jump/dodge effect.
                return NativeSoftVisualAssets.Get<GameObject>("Assets/Characters/character_effects/land/fx_land.prefab");
            }
            if (name == "fx_perfectdodge")
                return NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/fx_perfectdodge.prefab");
            if (name == "sfx_thunder" || name == "VM_ThunderFlash")
            {
                var envs = EnvMan.instance?.m_environments;
                if (envs != null)
                    foreach (var env in envs)
                    {
                        var thunder = env.m_envObject?.GetComponentInChildren<Thunder>(true);
                        var list = name == "VM_ThunderFlash" ? thunder?.m_flashEffect : thunder?.m_thunderEffect;
                        if (list?.m_effectPrefabs == null) continue;
                        foreach (var effect in list.m_effectPrefabs)
                            if (effect.m_enabled && effect.m_prefab != null) return effect.m_prefab;
                    }
                // The Eikthyr stomp has native thunder clips and is known present in
                // this installation. Audio playback copies clips, never an attack.
                return scene?.GetPrefab("fx_eikthyr_stomp");
            }
            return NativeSoftVisualAssets.GetPrefab(name);
        }

        internal static Material WindMaterial() => WindParticles()?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;

        internal static ParticleSystem WindParticles()
        {
            foreach (var wind in Object.FindObjectsOfType<GlobalWind>())
            {
                if (wind?.m_ps == null || wind.name.IndexOf("wind", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var material = wind.m_ps.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
                if (material != null) return wind.m_ps;
            }
            var envs = EnvMan.instance?.m_environments;
            if (envs != null)
                foreach (var env in envs)
                    foreach (var root in env.m_psystems ?? System.Array.Empty<GameObject>())
                    {
                        if (root == null) continue;
                        foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            if (root.name.IndexOf("wind", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                                particle.name.IndexOf("wind", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                            var material = particle.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
                            if (material != null) return particle;
                        }
                    }
            return null;
        }
    }
}
