#if MASTERY_CLUBS35_EXPERIMENT
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // The originating native area attack, not the later equipped weapon, selects
    // the echo. Never instantiate EffectList.Create (it can spawn gameplay/RPCs).
    internal static class HammerNativeEchoPresentation
    {
        internal sealed class Cue
        {
            internal GameObject Prefab;
            internal Quaternion Rotation;
            internal bool HitOnly;
        }
        internal static Cue[] Capture(Attack attack)
        {
            if (attack?.m_weapon?.m_shared == null) return Array.Empty<Cue>();
            var cues = new List<Cue>(8);
            Quaternion rotation = attack.GetAttackOrigin()?.rotation ?? Quaternion.identity;
            Add(cues, attack.m_weapon.m_shared.m_triggerEffect, rotation, false);
            Add(cues, attack.m_triggerEffect, rotation, false);
            Add(cues, attack.m_weapon.m_shared.m_hitEffect, Quaternion.identity, true);
            Add(cues, attack.m_hitEffect, Quaternion.identity, true);
            return cues.ToArray();
        }
        private static void Add(List<Cue> cues, EffectList list, Quaternion rotation, bool hitOnly)
        {
            foreach (var effect in list?.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>())
            {
                if (effect == null || !effect.m_enabled || effect.m_prefab == null || cues.Count >= 8) continue;
                // A repeated prefab in native trigger/hit lists is one cosmetic
                // cue here; if it can trigger without contact, keep that status.
                Cue existing = cues.Find(c => c.Prefab == effect.m_prefab);
                if (existing != null) { existing.HitOnly &= hitOnly; continue; }
                cues.Add(new Cue { Prefab = effect.m_prefab, HitOnly = hitOnly,
                    Rotation = effect.m_randomRotation ? UnityEngine.Random.rotation : rotation });
            }
        }
        internal static void Play(Cue[] cues, Vector3 center, bool nativeHit, Vector3 hitCenter)
        {
            if (Application.isBatchMode || Player.m_localPlayer == null || ZNetScene.instance == null || cues == null) return;
            foreach (Cue cue in cues)
            {
                if (cue.Prefab == null || (cue.HitOnly && !nativeHit)) continue;
                Vector3 position = cue.HitOnly ? hitCenter : center;
                // Cosmetic failure must not skip echo damage or its next replay.
                try
                {
                    if (MasteryPlugin.Settings.EnablePerkProcVFX.Value)
                    {
                        GameObject root = VfxPool.Spawn(cue.Prefab, position, cue.Rotation, 3f);
                        if (root != null)
                        {
                            root.transform.localScale *= .5f; // same half-radius as echo gameplay
                            foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
                            { var main = particle.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                            root.SetActive(true); // content remains inactive until sanitation
                            NativeVfxSafeFrame.AfterReady(root, () =>
                            {
                                if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value || Player.m_localPlayer == null) return;
                                root.GetComponent<VfxPoolBaseline>()?.RestartParticles();
                            });
                        }
                    }
                    // Copies actual native clips/settings; sanitized VFX has no audio.
                    // Keep original pitch/identity, only reduce volume.
                    if (MasteryPlugin.Settings.EnablePerkSFX.Value)
                        PerkAudioService.PlayPrefab("hammer_native_echo", cue.Prefab, position, .18f, .65f, 1f);
                    if (MasteryPlugin.Settings.VerboseLogging.Value)
                        MasteryPlugin.Log.LogInfo("[HammerNativeEcho] prefab=" + cue.Prefab.name +
                            " hitOnly=" + cue.HitOnly + " scale=0.50 volume=0.65 center=" + position);
                }
                catch (Exception error) { NativeVfxSafeFrame.Warn(cue.Prefab.name, "hammer echo " + error.GetType().Name); }
            }
        }
    }

    // Observe only the native hit-list call in the scoped hammer area attack.
    // Terrain/piece contacts can play hit effects without an enemy Damage call.
    // This patch never changes EffectList arguments/results or other consumers.
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class HammerNativeHitCueCapture
    {
        private static void Prefix(EffectList __instance, Vector3 __0)
        {
            Attack attack = Hammer35Epicenter.Current;
            if (attack?.m_weapon?.m_shared == null) return;
            if (ReferenceEquals(__instance, attack.m_weapon.m_shared.m_hitEffect) ||
                ReferenceEquals(__instance, attack.m_hitEffect))
                Clubs70EchoService.RecordNativeHitCue(attack, __0);
        }
    }
}
#endif
