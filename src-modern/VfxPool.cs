using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Instance-only pool for small, short-lived client VFX. It never mutates a shared vanilla prefab.
    internal static class VfxPool
    {
        private const int PerPrefabLimit = 12;
        private const int ActiveLimit = 48;
        internal static int ActiveLeases;
        private static readonly Dictionary<string, Queue<GameObject>> Available = new Dictionary<string, Queue<GameObject>>(StringComparer.Ordinal);

        internal static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
        {
            if (prefab == null || Player.m_localPlayer == null || ActiveLeases >= ActiveLimit) return null;
            string key = prefab.name;
            GameObject instance = null;
            if (Available.TryGetValue(key, out Queue<GameObject> queue))
                while (queue.Count > 0 && instance == null) instance = queue.Dequeue();
            if (instance == null)
            {
                instance = PerkNativeFeedback.CreateVisualOnly(prefab);
                if (instance == null) return null;
                instance.AddComponent<VfxPoolBaseline>().Capture();
            }
            else instance.GetComponent<VfxPoolBaseline>()?.Restore();
            instance.name = key;
            instance.transform.SetParent(null, false);
            instance.transform.SetPositionAndRotation(position, rotation);
            // Caller tunes the inactive instance first, then activates it. No early
            // native audio/camera/network callback may escape the visual-only boundary.
            instance.GetComponent<VfxPoolBaseline>()?.StopParticles();
            VfxPoolLease lease = instance.GetComponent<VfxPoolLease>() ?? instance.AddComponent<VfxPoolLease>();
            lease.Arm(key, Mathf.Clamp(lifetime, 0.15f, 5f));
            return instance;
        }

        internal static void Return(string key, GameObject instance)
        {
            if (instance == null) return;
            instance.transform.SetParent(null, false);
            instance.SetActive(false);
            if (!Available.TryGetValue(key, out Queue<GameObject> queue)) Available[key] = queue = new Queue<GameObject>();
            if (queue.Count >= PerPrefabLimit) { UnityEngine.Object.Destroy(instance); return; }
            queue.Enqueue(instance);
        }
    }

    /// <summary>
    /// VfxRecipeService.Tune multiplies root, particle and light values. A leased
    /// instance must return to its untuned template values before the next recipe,
    /// otherwise scale/emission compound on every proc.
    /// </summary>
    internal sealed class VfxPoolBaseline : MonoBehaviour
    {
        private Vector3 _scale;
        private Transform _nativeRoot;
        private Vector3 _nativeScale;
        private Vector3 _nativePosition;
        private Quaternion _nativeRotation;
        private ParticleSystem[] _particles;
        private float[] _size, _speed, _life, _emission;
        private int[] _maxParticles;
        private ParticleSystemScalingMode[] _scalingModes;
        private ParticleSystem.MinMaxGradient[] _startColors;
        private ParticleSystemRenderer[] _particleRenderers;
        private ParticleSystemRenderSpace[] _renderSpaces;
        private Renderer[] _renderers;
        private bool[] _rendererEnabled;
        private bool[] _emissionEnabled;
        private bool _restartPending;
        private ParticleSystem.Burst[][] _bursts, _tunedBursts;
        private Light[] _lights;
        private float[] _intensity;
        private AudioSource[] _audio;
        private bool[] _audioEnabled;
        private Behaviour[] _camShakers;
        private bool[] _camShakerEnabled;

        internal ParticleSystem[] Particles => _particles;
        internal Light[] Lights => _lights;
        internal AudioSource[] Audio => _audio;
        internal Behaviour[] CamShakers => _camShakers;
        internal Transform NativeRoot => _nativeRoot;

        internal void Capture()
        {
            _scale = transform.localScale;
            // CreateVisualOnly's first child is the original native prefab root.
            // Legacy Local particle scaling must act on that transform, not on
            // the newer cosmetic wrapper parent. Restore it for all pool users.
            _nativeRoot = transform.childCount > 0 ? transform.GetChild(0) : null;
            if (_nativeRoot != null)
            {
                _nativeScale = _nativeRoot.localScale;
                _nativePosition = _nativeRoot.localPosition; _nativeRotation = _nativeRoot.localRotation;
            }
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            _size = new float[_particles.Length]; _speed = new float[_particles.Length];
            _life = new float[_particles.Length]; _emission = new float[_particles.Length];
            _maxParticles = new int[_particles.Length]; _emissionEnabled = new bool[_particles.Length];
            _scalingModes = new ParticleSystemScalingMode[_particles.Length];
            _startColors = new ParticleSystem.MinMaxGradient[_particles.Length];
            _particleRenderers = new ParticleSystemRenderer[_particles.Length];
            _renderSpaces = new ParticleSystemRenderSpace[_particles.Length];
            _bursts = new ParticleSystem.Burst[_particles.Length][];
            _tunedBursts = new ParticleSystem.Burst[_particles.Length][];
            for (int i = 0; i < _particles.Length; ++i)
            {
                ParticleSystem.MainModule main = _particles[i].main;
                ParticleSystem.EmissionModule emission = _particles[i].emission;
                _size[i] = main.startSizeMultiplier;
                _speed[i] = main.startSpeedMultiplier;
                _life[i] = main.startLifetimeMultiplier;
                _emission[i] = emission.rateOverTimeMultiplier;
                _maxParticles[i] = main.maxParticles; _emissionEnabled[i] = emission.enabled;
                _scalingModes[i] = main.scalingMode;
                _startColors[i] = main.startColor;
                _particleRenderers[i] = _particles[i].GetComponent<ParticleSystemRenderer>();
                if (_particleRenderers[i] != null) _renderSpaces[i] = _particleRenderers[i].alignment;
                _bursts[i] = new ParticleSystem.Burst[emission.burstCount];
                _tunedBursts[i] = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(_bursts[i]);
            }
            _renderers = GetComponentsInChildren<Renderer>(true);
            _rendererEnabled = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) _rendererEnabled[i] = _renderers[i].enabled;
            _lights = GetComponentsInChildren<Light>(true);
            _intensity = new float[_lights.Length];
            for (int i = 0; i < _lights.Length; ++i) _intensity[i] = _lights[i].intensity;
            _audio = GetComponentsInChildren<AudioSource>(true);
            _audioEnabled = new bool[_audio.Length];
            for (int i = 0; i < _audio.Length; ++i) _audioEnabled[i] = _audio[i].enabled;
            List<Behaviour> shakers = new List<Behaviour>();
            foreach (Component component in GetComponentsInChildren<Component>(true))
                if (component is Behaviour behaviour &&
                    component.GetType().Name.IndexOf("CamShaker", StringComparison.OrdinalIgnoreCase) >= 0)
                    shakers.Add(behaviour);
            _camShakers = shakers.ToArray();
            _camShakerEnabled = new bool[_camShakers.Length];
            for (int i = 0; i < _camShakers.Length; ++i) _camShakerEnabled[i] = _camShakers[i].enabled;
        }

        internal void RestartParticles()
        {
            if (GetComponent<DeferredNativeVfx>()?.IsPrepared == false) { _restartPending = true; return; }
            _restartPending = false;
            if (_particles == null) return;
            foreach (ParticleSystem system in _particles)
                if (system != null) { system.Clear(false); system.Play(false); }
        }
        internal void FinishPendingRestart() { if (_restartPending) RestartParticles(); }

        internal void StopParticles()
        {
            _restartPending = false;
            if (_particles == null) return;
            foreach (ParticleSystem system in _particles)
                if (system != null)
                {
                    // Pool callers explicitly request playback. Do not let native
                    // playOnAwake race a Stop or replay a burst during preparation.
                    var main = system.main; main.playOnAwake = false;
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
        }

        internal void TuneBurstEmission(float multiplier)
        {
            if (_particles == null) return;
            for (int i = 0; i < _particles.Length; ++i)
            {
                if (_particles[i] == null || _bursts[i].Length == 0) continue;
                for (int j = 0; j < _bursts[i].Length; ++j)
                {
                    ParticleSystem.Burst burst = _bursts[i][j];
                    ParticleSystem.MinMaxCurve count = burst.count;
                    if (count.mode == ParticleSystemCurveMode.Constant) count.constant *= multiplier;
                    else if (count.mode == ParticleSystemCurveMode.TwoConstants)
                    { count.constantMin *= multiplier; count.constantMax *= multiplier; }
                    else count.curveMultiplier *= multiplier;
                    burst.count = count;
                    _tunedBursts[i][j] = burst;
                }
                ParticleSystem.EmissionModule emission = _particles[i].emission;
                emission.SetBursts(_tunedBursts[i]);
            }
        }

        internal void Restore()
        {
            transform.localScale = _scale;
            if (_nativeRoot != null)
            {
                _nativeRoot.localScale = _nativeScale;
                _nativeRoot.localPosition = _nativePosition; _nativeRoot.localRotation = _nativeRotation;
            }
            if (_particles != null) for (int i = 0; i < _particles.Length; ++i)
            {
                if (_particles[i] == null) continue;
                ParticleSystem.MainModule main = _particles[i].main;
                ParticleSystem.EmissionModule emission = _particles[i].emission;
                main.startSizeMultiplier = _size[i];
                main.startSpeedMultiplier = _speed[i];
                main.startLifetimeMultiplier = _life[i];
                emission.rateOverTimeMultiplier = _emission[i];
                main.maxParticles = _maxParticles[i]; emission.enabled = _emissionEnabled[i];
                main.scalingMode = _scalingModes[i];
                main.startColor = _startColors[i];
                if (_particleRenderers[i] != null) _particleRenderers[i].alignment = _renderSpaces[i];
                emission.SetBursts(_bursts[i]);
            }
            if (_renderers != null) for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = _rendererEnabled[i];
            if (_lights != null) for (int i = 0; i < _lights.Length; ++i)
                if (_lights[i] != null) _lights[i].intensity = _intensity[i];
            if (_audio != null) for (int i = 0; i < _audio.Length; ++i)
                if (_audio[i] != null) _audio[i].enabled = _audioEnabled[i];
            if (_camShakers != null) for (int i = 0; i < _camShakers.Length; ++i)
                if (_camShakers[i] != null) _camShakers[i].enabled = _camShakerEnabled[i];
        }
    }

    internal sealed class VfxPoolLease : MonoBehaviour
    {
        private string _key;
        private float _returnAt;
        private float _lifetime, _armedAt;
        private bool _ready;
        private bool _leased;
        internal void Arm(string key, float lifetime)
        {
            _key = key; _lifetime = lifetime; _armedAt = Time.time; _ready = false;
            NotifyReady();
            if (!_leased) { _leased = true; VfxPool.ActiveLeases++; }
        }
        internal void NotifyReady()
        {
            if (_ready || !gameObject.activeInHierarchy || GetComponent<DeferredNativeVfx>()?.IsPrepared == false) return;
            _ready = true; _returnAt = Time.time + _lifetime;
        }
        private void OnEnable() => NotifyReady();
        private void OnDisable() => ReleaseCount();
        private void OnDestroy() => ReleaseCount();
        internal void Cancel() => ReleaseCount();
        private void ReleaseCount()
        {
            if (!_leased) return;
            _leased = false;
            VfxPool.ActiveLeases = Mathf.Max(0, VfxPool.ActiveLeases - 1);
        }
        private void Update()
        {
            if (!_ready) { if (Time.time - _armedAt >= 2f) GetComponent<DeferredNativeVfx>()?.Abort(); return; }
            if (Time.time >= _returnAt) VfxPool.Return(_key, gameObject);
        }
    }
}
