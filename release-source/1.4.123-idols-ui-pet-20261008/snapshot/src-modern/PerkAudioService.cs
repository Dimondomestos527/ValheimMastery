using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal static class PerkAudioService
    {
        private const int ActiveSoundLimit = 32;
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        private static readonly HashSet<string> MissingPrefabs = new HashSet<string>();
        private sealed class AudioTemplate { internal GameObject Prefab; internal AudioSource[] Sources; }
        private static readonly Dictionary<int, AudioTemplate> Templates = new Dictionary<int, AudioTemplate>();
        internal static int ActiveSounds;

        internal static void Play(string perkId, string prefabName, Vector3 position, float throttleSeconds = 0.12f,
            float volumeScale = 1f, float pitchScale = 1f)
        {
            if (!MasteryPlugin.Settings.EnablePerkSFX.Value || Player.m_localPlayer == null || string.IsNullOrWhiteSpace(prefabName)) return;
            GameObject prefab = NativePerkAssetResolver.Resolve(prefabName);
            if (prefab == null)
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value && MissingPrefabs.Add(prefabName))
                    MasteryPlugin.Log.LogWarning("Missing perk sound prefab: " + prefabName);
                return;
            }
            PlayPrefab(perkId, prefab, position, throttleSeconds, volumeScale, pitchScale);
        }

        // Read the prefab's actual vanilla clips/settings, but never instantiate the prefab.
        // Some fx_ assets also contain network, camera, VFX or damage scripts. A sound call
        // must not create any of those, and SFX must stay independent of the VFX setting.
        internal static bool PlayPrefab(string perkId, GameObject prefab, Vector3 position,
            float throttleSeconds = 0.12f, float volumeScale = 1f, float pitchScale = 1f)
        {
            if (prefab == null || Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkSFX.Value) return false;
            string key = perkId + ":" + prefab.GetInstanceID();
            if (LastPlayed.TryGetValue(key, out float last) && Time.unscaledTime - last < throttleSeconds) return false;
            if (ActiveSounds >= ActiveSoundLimit) return false;
            bool played = false;
            foreach (AudioSource template in GetSources(prefab))
            {
                if (template == null || !template.enabled || !IsActiveChild(template.transform, prefab.transform)) continue;
                ZSFX settings = template.GetComponent<ZSFX>();
                AudioClip clip = template.clip;
                if (settings != null && settings.m_audioClips != null && settings.m_audioClips.Length > 0)
                    clip = settings.m_audioClips[Random.Range(0, settings.m_audioClips.Length)];
                if (clip == null || ActiveSounds >= ActiveSoundLimit) continue;

                GameObject instance = new GameObject("ValheimMastery_Audio_" + prefab.name);
                instance.transform.position = position;
                AudioSource audio = instance.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.loop = false;
                audio.clip = clip;
                audio.outputAudioMixerGroup = template.outputAudioMixerGroup;
                audio.priority = template.priority;
                audio.spatialBlend = template.spatialBlend;
                audio.rolloffMode = template.rolloffMode;
                audio.minDistance = template.minDistance;
                audio.maxDistance = template.maxDistance;
                audio.spread = template.spread;
                audio.dopplerLevel = template.dopplerLevel;
                audio.reverbZoneMix = template.reverbZoneMix;
                audio.bypassEffects = template.bypassEffects;
                audio.bypassListenerEffects = template.bypassListenerEffects;
                audio.bypassReverbZones = template.bypassReverbZones;
                if (template.rolloffMode == AudioRolloffMode.Custom)
                    audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                float pitch = settings != null ? Random.Range(settings.m_minPitch, settings.m_maxPitch) : template.pitch;
                float volume = settings != null ? Random.Range(settings.m_minVol, settings.m_maxVol) : template.volume;
                audio.pitch = Mathf.Clamp(pitch * pitchScale, 0.1f, 3f);
                audio.volume = Mathf.Clamp01(volume * volumeScale);
                audio.panStereo = settings != null && settings.m_randomPan ? Random.Range(settings.m_minPan, settings.m_maxPan) : template.panStereo;
                float delay = settings != null ? Mathf.Clamp(Random.Range(settings.m_minDelay, settings.m_maxDelay), 0f, 2f) : 0f;
                float duration = Mathf.Clamp(clip.length / audio.pitch, 0.05f, 8f);
                PerkAudioLifetime life = instance.AddComponent<PerkAudioLifetime>();
                life.Arm(audio, delay, duration, settings != null ? settings.m_fadeInDuration : 0f,
                    settings != null && settings.m_fadeOutOnAwake ? settings.m_fadeOutDelay : -1f,
                    settings != null ? settings.m_fadeOutDuration : 0f);
                audio.PlayDelayed(delay);
                played = true;
            }
            if (played) LastPlayed[key] = Time.unscaledTime;
            else if (MasteryPlugin.Settings.VerboseLogging.Value && MissingPrefabs.Add("audio:" + prefab.name))
                MasteryPlugin.Log.LogWarning("[PerkSFX] No playable AudioSource/ZSFX clip on actual prefab: " + prefab.name);
            return played;
        }

        private static AudioSource[] GetSources(GameObject prefab)
        {
            int id = prefab.GetInstanceID();
            if (Templates.TryGetValue(id, out AudioTemplate cached) && cached.Prefab == prefab) return cached.Sources;
            if (Templates.Count >= 128) Templates.Clear();
            AudioSource[] sources = prefab.GetComponentsInChildren<AudioSource>(true);
            Templates[id] = new AudioTemplate { Prefab = prefab, Sources = sources };
            return sources;
        }

        private static bool IsActiveChild(Transform child, Transform root)
        {
            // Asset roots need not be active in a loaded prefab library; only disabled
            // descendants are excluded, matching Instantiate's effective child state.
            for (Transform current = child; current != null && current != root; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }
    }

    internal sealed class PerkAudioLifetime : MonoBehaviour
    {
        private AudioSource _audio;
        private float _start, _end, _volume, _fadeIn, _fadeOutDelay, _fadeOut;
        private bool _counted;
        internal void Arm(AudioSource audio, float delay, float duration, float fadeIn, float fadeOutDelay, float fadeOut)
        {
            _audio = audio;
            _volume = audio.volume;
            _start = Time.unscaledTime + delay;
            _end = _start + duration + 0.15f;
            _fadeIn = Mathf.Max(0f, fadeIn);
            _fadeOutDelay = fadeOutDelay;
            _fadeOut = Mathf.Max(0f, fadeOut);
            _counted = true;
            PerkAudioService.ActiveSounds++;
            if (_fadeIn > 0f) audio.volume = 0f;
        }
        private void Update()
        {
            if (_audio == null || Time.unscaledTime >= _end) { Destroy(gameObject); return; }
            float elapsed = Mathf.Max(0f, Time.unscaledTime - _start);
            float factor = _fadeIn > 0f ? Mathf.Clamp01(elapsed / _fadeIn) : 1f;
            if (_fadeOutDelay >= 0f && elapsed >= _fadeOutDelay)
                factor *= _fadeOut > 0f ? 1f - Mathf.Clamp01((elapsed - _fadeOutDelay) / _fadeOut) : 0f;
            _audio.volume = _volume * factor;
        }
        private void OnDestroy()
        {
            if (_counted) PerkAudioService.ActiveSounds = Mathf.Max(0, PerkAudioService.ActiveSounds - 1);
        }
    }
}
