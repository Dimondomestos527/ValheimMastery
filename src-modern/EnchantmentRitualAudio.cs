using System;
using UnityEngine;

namespace ValheimMastery
{
    // Additive fixed MIX1 path. Existing random perk sound playback is unchanged.
    internal static class EnchantmentRitualAudio
    {
        private const string MistPath = "Assets/GameElements/StatusEffects/effects/demister/sfx/sfx_demister_start.prefab";
        private const string CharmPath = "Assets/Characters/character_effects/fx_creature_tamed.prefab";
        private static int leases;

        internal static void Preload()
        {
            PreloadClip(MistPath, "Demister_On_01");
            PreloadClip(CharmPath, "Magic_Spell_Charm");
        }

        private static void PreloadClip(string path, string name)
        {
            AudioSource donor; AudioClip clip;
            if (Find(path, name, out donor, out clip) && clip.loadState == AudioDataLoadState.Unloaded)
                try { clip.LoadAudioData(); } catch (Exception) { } // Never wait/retry a committed cue.
        }

        // Called exactly once by the committed-action consumer, even with VFX off.
        internal static IDisposable TryPlay(CraftingStation station)
        {
            if (Application.isBatchMode || station == null || !station.isActiveAndEnabled ||
                Player.m_localPlayer == null || ZNet.instance == null || ZNet.instance.GetWorld() == null ||
                ZNet.instance.GetWorldUID() == 0 || ZNetScene.instance == null ||
                !MasteryPlugin.Settings.EnablePerkSFX.Value || leases >= 4 || PerkAudioService.ActiveSounds > 30)
                return null;
            AudioSource mist, charm;
            AudioClip mistClip, charmClip;
            if (!Find(MistPath, "Demister_On_01", out mist, out mistClip) ||
                !Find(CharmPath, "Magic_Spell_Charm", out charm, out charmClip) ||
                mistClip.loadState != AudioDataLoadState.Loaded || charmClip.loadState != AudioDataLoadState.Loaded) return null;

            // Reserve both delayed/immediate voices before allocating any native object.
            leases++;
            PerkAudioService.ActiveSounds += 2;
            GameObject root = null;
            RitualAudioLease lease = null;
            try
            {
                root = new GameObject("ValheimMastery_EnchantmentMIX1");
                root.transform.position = station.transform.position;
                lease = root.AddComponent<RitualAudioLease>();
                lease.Arm(station, Player.m_localPlayer, ZNet.instance, ZNetScene.instance);
                AudioSource first = Copy(root, mist, mistClip, .95f, .65f);
                AudioSource second = Copy(root, charm, charmClip, 1.05f, .22f);
                lease.SetOutputs(first, second, Mathf.Max(mistClip.length / .95f, .8f + charmClip.length / 1.05f) + .2f);
                first.Play();
                // The second voice starts in Update after the same context checks.
                return lease;
            }
            catch (Exception)
            {
                if (lease != null) lease.Dispose();
                else
                {
                    Release();
                    if (root != null) try { UnityEngine.Object.Destroy(root); } catch (Exception) { }
                }
                return null;
            }
        }

        internal static void Release()
        {
            leases = Math.Max(0, leases - 1);
            PerkAudioService.ActiveSounds = Math.Max(0, PerkAudioService.ActiveSounds - 2);
        }

        private static bool Find(string path, string name, out AudioSource donor, out AudioClip clip)
        {
            try { return FindCore(path, name, out donor, out clip); }
            catch (Exception) { donor = null; clip = null; return false; }
        }

        private static bool FindCore(string path, string name, out AudioSource donor, out AudioClip clip)
        {
            donor = null; clip = null;
            GameObject prefab = NativeSoftVisualAssets.Get<GameObject>(path);
            if (prefab == null) return false;
            foreach (AudioSource source in prefab.GetComponentsInChildren<AudioSource>(true))
            {
                if (source == null || !source.enabled) continue;
                bool active = true;
                for (Transform node = source.transform; node != null && node != prefab.transform; node = node.parent)
                    if (!node.gameObject.activeSelf) { active = false; break; }
                if (!active) continue;
                ZSFX settings = source.GetComponent<ZSFX>();
                if (settings == null || settings.m_audioClips == null) continue;
                foreach (AudioClip candidate in settings.m_audioClips)
                {
                    if (candidate == null || candidate.name != name) continue;
                    if (clip != null) return false; // Ambiguous donors never pick a random substitute.
                    donor = source; clip = candidate;
                }
            }
            return donor != null && clip != null && !float.IsNaN(clip.length) &&
                !float.IsInfinity(clip.length) && clip.length > 0f && clip.length <= 8f;
        }

        private static AudioSource Copy(GameObject root, AudioSource donor, AudioClip clip, float pitch, float gain)
        {
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false; audio.loop = false; audio.clip = clip;
            audio.outputAudioMixerGroup = donor.outputAudioMixerGroup;
            audio.priority = donor.priority; audio.spatialBlend = donor.spatialBlend;
            audio.rolloffMode = donor.rolloffMode; audio.minDistance = donor.minDistance;
            audio.maxDistance = donor.maxDistance; audio.spread = donor.spread;
            audio.dopplerLevel = donor.dopplerLevel; audio.reverbZoneMix = donor.reverbZoneMix;
            audio.bypassEffects = donor.bypassEffects; audio.bypassListenerEffects = donor.bypassListenerEffects;
            audio.bypassReverbZones = donor.bypassReverbZones; audio.panStereo = donor.panStereo;
            if (donor.rolloffMode == AudioRolloffMode.Custom)
                audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, donor.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            audio.pitch = pitch; audio.volume = gain;
            return audio;
        }
    }

    internal sealed class RitualAudioLease : MonoBehaviour, IDisposable
    {
        private CraftingStation station;
        private Player player;
        private ZNet net;
        private ZNetScene scene;
        private long world;
        private AudioSource first, second;
        private float start, end;
        private bool reserved, secondStarted;

        internal void Arm(CraftingStation bench, Player local, ZNet network, ZNetScene currentScene)
        {
            reserved = true;
            station = bench; player = local; net = network; scene = currentScene;
            world = network.GetWorldUID();
            start = Time.unscaledTime;
        }
        internal void SetOutputs(AudioSource a, AudioSource b, float duration)
        { first = a; second = b; end = start + duration; }

        private void Update()
        {
            if (!reserved) return;
            if (station == null || !station.isActiveAndEnabled || player == null || Player.m_localPlayer != player ||
                net == null || ZNet.instance != net || net.GetWorld() == null || net.GetWorldUID() != world ||
                scene == null || ZNetScene.instance != scene ||
                !MasteryPlugin.Settings.EnablePerkSFX.Value || Time.unscaledTime >= end || first == null || second == null)
            { Dispose(); return; }
            if (!secondStarted && Time.unscaledTime - start >= .8f)
            {
                secondStarted = true;
                try { second.Play(); } catch (Exception) { Dispose(); }
            }
        }
        public void Dispose()
        {
            if (!reserved) return;
            reserved = false;
            EnchantmentRitualAudio.Release();
            AudioSource a = first, b = second;
            first = second = null; station = null; player = null; net = null; scene = null;
            try { if (a != null) a.Stop(); } catch (Exception) { }
            try { if (b != null) b.Stop(); } catch (Exception) { }
            try { UnityEngine.Object.Destroy(gameObject); } catch (Exception) { }
        }
        private void OnDisable() { Dispose(); }
        private void OnDestroy() { Dispose(); }
    }
}
