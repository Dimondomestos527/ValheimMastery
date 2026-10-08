using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // The ordinary gold milestone stays. Divine recognition is native center
    // text and owned combat cosmetics, never another Canvas or gameplay action.
    internal static class PatronAscensionPresentation
    {
        private static GameObject _pendingOrActive;
        internal const float Duration = 4.2f;
        internal static bool IsPendingOrActive => _pendingOrActive != null;
        internal static bool Supports(Skills.SkillType skill) => skill == Skills.SkillType.Unarmed || skill == Skills.SkillType.Polearms;
        internal static void Schedule(Player player, Skills.SkillType skill)
        {
            if (!Supports(skill) || player == null || player != Player.m_localPlayer || player.IsDead() ||
                Application.isBatchMode || _pendingOrActive != null || ZNet.instance?.GetWorld() == null ||
                MasteryPlugin.Settings?.Enabled.Value != true) return;
            if (!MasteryPlugin.Settings.EnableMilestoneMessages.Value && !MasteryPlugin.Settings.EnableMilestoneVFX.Value &&
                !MasteryPlugin.Settings.EnablePerkSFX.Value) return;
            var root = new GameObject("ValheimMastery_PatronAscension");
            _pendingOrActive = root;
            try { root.AddComponent<PatronAscensionSequence>().Initialize(player, skill); }
            catch { _pendingOrActive = null; UnityEngine.Object.Destroy(root); throw; }
        }
        internal static void Release(GameObject owner) { if (_pendingOrActive == owner) _pendingOrActive = null; }
    }
    internal sealed class PatronAscensionSequence : MonoBehaviour
    {
        private sealed class Burst
        {
            internal float At, Scale, Lifetime; internal int Budget;
            internal string Asset; internal Vector3 Offset; internal bool Done;
            internal GameObject Root; internal bool ParticleDonor;
        }
        private sealed class Sound
        { internal float At, Volume, Pitch, MaxSeconds; internal string Asset; internal bool Done; }
        private Player _player;
        private Skills.SkillType _skill;
        private ZNet _session;
        private ZNetScene _scene;
        private long _world;
        private float _received, _started, _retryAt;
        private bool _begun, _messagePlayed, _windAttemptDone;
        private readonly List<Burst> _bursts = new List<Burst>();
        private readonly List<Sound> _sounds = new List<Sound>();
        private readonly List<GameObject> _audio = new List<GameObject>();
        private PatronRecognitionWind _wind;
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        internal void Initialize(Player player, Skills.SkillType skill)
        {
            _player = player; _skill = skill; _session = ZNet.instance; _scene = ZNetScene.instance;
            _world = _session.GetWorldUID(); _received = Time.unscaledTime;
            bool tyr = skill == Skills.SkillType.Unarmed;
            if (tyr)
            {
                AddBurst(.10f, "fx_bossstone_attach", Vector3.zero, .65f, 1.4f, 96);
                AddBurst(.45f, "TrinketBloodGoldHealth", new Vector3(-.42f,.10f,0f), .85f, 3.15f, 64, true);
                AddBurst(.45f, "TrinketBloodGoldStamina", new Vector3(.42f,.10f,0f), .85f, 3.15f, 64, true);
                AddBurst(.90f, "fx_Adrenaline1", Vector3.zero, 1f, 3.1f, 144);
                _sounds.Add(new Sound { At=2.60f, Asset="sfx_bossstone_attach_done", Volume=.80f, Pitch=.95f, MaxSeconds=1.55f });
            }
            else
            {
                AddBurst(.10f, "vfx_odin_despawn", new Vector3(0f,.60f,-.45f), .80f, 2.9f, 144);
                AddBurst(.75f, "vfx_raven_feathers", new Vector3(0f,.40f,.05f), 1f, 2.8f, 64);
                _sounds.Add(new Sound { At=1.20f, Asset="sfx_atgeir_attack_secondary", Volume=.22f, Pitch=.95f, MaxSeconds=.55f });
                _sounds.Add(new Sound { At=2.80f, Asset="sfx_bossstone_attach_done", Volume=.75f, Pitch=.95f, MaxSeconds=1.35f });
            }
            // Warm exact soft references during the five-second gold frame, not at cue time.
            foreach (var burst in _bursts) NativePerkAssetResolver.Resolve(burst.Asset);
            foreach (var sound in _sounds) NativePerkAssetResolver.Resolve(sound.Asset);
        }
        private void AddBurst(float at, string asset, Vector3 offset, float scale, float lifetime, int budget, bool particleDonor=false) =>
            _bursts.Add(new Burst { At=at, Asset=asset, Offset=offset, Scale=scale, Lifetime=lifetime, Budget=budget, ParticleDonor=particleDonor });
        private bool Current => _player != null && _player == Player.m_localPlayer && !_player.IsDead() &&
            !Application.isBatchMode && MasteryPlugin.Settings?.Enabled.Value == true && _session != null &&
            ReferenceEquals(_session, ZNet.instance) && _session.GetWorld() != null && _session.GetWorldUID() == _world &&
            _scene != null && ReferenceEquals(_scene, ZNetScene.instance);
        private float Age => Time.unscaledTime - _started;
        private void Update()
        {
            if (!Current) { Destroy(gameObject); return; }
            if (!_begun)
            {
                if (Time.unscaledTime - _received < MasteryMilestonePresentation.Duration + .10f || MasteryMilestonePresentation.IsActive) return;
                _begun = true; _started = Time.unscaledTime;
            }
            float age = Age;
            if (age >= PatronAscensionPresentation.Duration) { Destroy(gameObject); return; }
            transform.position = _player.transform.position;
            if (!_messagePlayed && age >= .85f)
            {
                _messagePlayed = true;
                if (MasteryPlugin.Settings.EnableMilestoneMessages.Value)
                    _player.Message(MessageHud.MessageType.Center, "<color=#FFD700>" +
                        (_skill == Skills.SkillType.Unarmed ? GoldUiLocalization.Text("TYR RECOGNIZES YOUR UNYIELDING WILL", "ТЮР ВИЗНАВ ТВОЮ НЕПОХИТНУ ВОЛЮ") :
                        GoldUiLocalization.Text("ODIN DEEMS YOU WORTHY OF HIS WILL", "ОДІН ВИЗНАВ ТЕБЕ ГІДНИМ СВОЄЇ ВОЛІ")) + "</color>", 0, null);
            }
            bool visuals = MasteryPlugin.Settings.EnableMilestoneVFX.Value;
            foreach (var burst in _bursts)
            {
                if (burst.Root != null && (!visuals || age >= burst.At + burst.Lifetime))
                { CancelVisual(burst.Root); burst.Root = null; }
                if (burst.Done || age < burst.At) continue;
                if (!visuals || age > burst.At + .40f) { burst.Done = true; continue; }
                burst.Done = TryBurst(burst);
            }
            foreach (var sound in _sounds)
            {
                if (sound.Done || age < sound.At) continue;
                if (!MasteryPlugin.Settings.EnablePerkSFX.Value || age > sound.At + .35f) { sound.Done = true; continue; }
                sound.Done = TrySound(sound);
            }
            if (!MasteryPlugin.Settings.EnablePerkSFX.Value)
                foreach (var root in _audio) if (root != null) { root.SetActive(false); Destroy(root); }
            if (_skill == Skills.SkillType.Polearms && !_windAttemptDone && age >= .35f && age >= _retryAt)
            {
                _retryAt = age + .10f;
                if (!visuals || age > .80f) { _windAttemptDone = true; }
                else { _wind = PatronRecognitionWind.Create(transform,
                    () => Current && MasteryPlugin.Settings.EnableMilestoneVFX.Value && Age < PatronAscensionPresentation.Duration,
                    () => Age <= .80f); _windAttemptDone = _wind != null; }
                if (_windAttemptDone && _wind == null && visuals) Warn("wind", "native environmental wind donor unavailable; no substitute");
            }
            if (_wind != null)
            {
                if (!visuals) { Destroy(_wind.gameObject); _wind = null; }
                else _wind.SetPhase(age);
            }
        }
        private bool TryBurst(Burst burst)
        {
            var prefab = NativePerkAssetResolver.Resolve(burst.Asset);
            if (prefab == null) return false; // Exact resource only; bounded retry until cue deadline.
            var root = burst.ParticleDonor ? Combat100RecognitionParticles.Clone(prefab.GetComponentInChildren<ParticleSystem>(true), transform, burst.Budget, burst.Lifetime) : PerkNativeFeedback.CreateVisualOnly(prefab);
            if (root == null) return false;
            burst.Root = root; root.transform.SetParent(transform, true);
            root.transform.position = _player.GetCenterPoint() + _player.transform.TransformDirection(burst.Offset);
            root.transform.localScale *= burst.Scale;
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0 || systems.Length > 12)
            { CancelVisual(root); burst.Root = null; Warn(burst.Asset,"unsupported particle layout"); return true; }
            foreach (var particle in systems)
            {
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particle.main; if (!burst.ParticleDonor) main.loop=false; main.playOnAwake=false; main.useUnscaledTime=true;
                main.stopAction=ParticleSystemStopAction.None; main.maxParticles=Mathf.Min(main.maxParticles,Mathf.Max(1,burst.Budget/systems.Length));
                main.startLifetimeMultiplier=Mathf.Min(main.startLifetimeMultiplier,burst.Lifetime);
                if (burst.Asset == "vfx_raven_feathers")
                {
                    var emission=particle.emission;
                    if (emission.burstCount != 1)
                    { CancelVisual(root); burst.Root=null; Warn(burst.Asset,"native feather burst layout changed; no substitute"); return true; }
                    // Native donor emits only1–3: use a readable, bounded cluster on this clone.
                    var featherBurst=emission.GetBurst(0);
                    featherBurst.count=new ParticleSystem.MinMaxCurve(40f,60f);
                    emission.SetBurst(0,featherBurst);
                }
                var sub=particle.subEmitters; sub.enabled=false;
                var collision=particle.collision; collision.enabled=false;
                var trigger=particle.trigger; trigger.enabled=false;
                var lights=particle.lights; lights.enabled=false;
                var shape=particle.shape;
                if ((shape.meshRenderer!=null && !shape.meshRenderer.transform.IsChildOf(root.transform)) ||
                    (shape.skinnedMeshRenderer!=null && !shape.skinnedMeshRenderer.transform.IsChildOf(root.transform)) ||
                    (shape.spriteRenderer!=null && !shape.spriteRenderer.transform.IsChildOf(root.transform)) ||
                    (main.simulationSpace==ParticleSystemSimulationSpace.Custom &&
                        (main.customSimulationSpace==null || !main.customSimulationSpace.IsChildOf(root.transform))))
                { CancelVisual(root); burst.Root=null; Warn(burst.Asset,"external particle binding unsupported; no substitute"); return true; }
            }
            foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled=false;
            NativeVfxSafeFrame.AfterReady(root, () =>
            {
                if (!Current || root == null || !MasteryPlugin.Settings.EnableMilestoneVFX.Value || Age > burst.At+.40f || Age >= PatronAscensionPresentation.Duration)
                { CancelVisual(root); return; }
                foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true)) particle.Play(false);
            });
            root.SetActive(true);
            return true;
        }
        private bool TrySound(Sound cue)
        {
            var prefab = NativePerkAssetResolver.Resolve(cue.Asset);
            if (prefab == null) return false;
            bool played = false;
            foreach (var template in prefab.GetComponentsInChildren<AudioSource>(true))
            {
                if (template == null || !template.enabled || PerkAudioService.ActiveSounds >= 32) continue;
                bool active=true;
                for (var child=template.transform; child!=null && child!=prefab.transform; child=child.parent)
                    if (!child.gameObject.activeSelf) { active=false; break; }
                if (!active) continue;
                var settings=template.GetComponent<ZSFX>();
                var clip = settings?.m_audioClips?.Length > 0 ? settings.m_audioClips[UnityEngine.Random.Range(0,settings.m_audioClips.Length)] : template.clip;
                if (clip==null || !float.IsFinite(clip.length) || clip.length<=0f) continue;
                float nativePitch=settings!=null ? UnityEngine.Random.Range(settings.m_minPitch,settings.m_maxPitch) : template.pitch;
                float pitch=Mathf.Clamp(nativePitch*cue.Pitch,.50f,2f);
                float nativeVolume=settings!=null ? UnityEngine.Random.Range(settings.m_minVol,settings.m_maxVol) : template.volume;
                if (!float.IsFinite(pitch) || !float.IsFinite(nativeVolume)) continue;
                float seconds=Mathf.Min(clip.length/pitch,cue.MaxSeconds,PatronAscensionPresentation.Duration-Age-.02f);
                if(seconds < .05f) continue;
                var root=new GameObject("Mastery_PatronAudio_"+cue.Asset); _audio.Add(root);
                root.transform.SetParent(transform,true);root.transform.position=_player.GetCenterPoint();
                var sound=root.AddComponent<AudioSource>();sound.playOnAwake=false;sound.loop=false;sound.clip=clip;
                sound.outputAudioMixerGroup=template.outputAudioMixerGroup;sound.priority=template.priority;
                sound.spatialBlend=template.spatialBlend;sound.rolloffMode=template.rolloffMode;
                sound.minDistance=template.minDistance;sound.maxDistance=template.maxDistance;sound.spread=template.spread;sound.dopplerLevel=0f;
                sound.reverbZoneMix=template.reverbZoneMix;sound.bypassEffects=template.bypassEffects;
                sound.bypassListenerEffects=template.bypassListenerEffects;sound.bypassReverbZones=template.bypassReverbZones;
                if(template.rolloffMode==AudioRolloffMode.Custom)sound.SetCustomCurve(AudioSourceCurveType.CustomRolloff,template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                sound.pitch=pitch;sound.volume=Mathf.Clamp01(nativeVolume*cue.Volume);sound.panStereo=template.panStereo;
                // Time cue explicitly, rather than inheriting randomized native ZSFX delay.
                root.AddComponent<PerkAudioLifetime>().Arm(sound,0f,seconds,.025f,Mathf.Max(0f,seconds-.18f),.18f);
                sound.Play(); played=true;
            }
            if (!played) Warn(cue.Asset,"no available native audio clip/budget; no substitute");
            return true;
        }
        private void Warn(string asset,string reason)
        { if(_warned.Add(asset+reason))MasteryPlugin.Log.LogWarning("[Combat100 Recognition] "+asset+": "+reason); }
        private static void CancelVisual(GameObject root)
        {
            if(root==null)return;
            var pending=root.GetComponent<DeferredNativeVfx>();
            if(pending!=null)pending.Abort();else {root.SetActive(false);UnityEngine.Object.Destroy(root);}
        }
        private void OnDestroy()
        {
            foreach(var burst in _bursts)CancelVisual(burst.Root);
            foreach(var root in _audio)if(root!=null){root.SetActive(false);Destroy(root);}
            if(_wind!=null)Destroy(_wind.gameObject);
            PatronAscensionPresentation.Release(gameObject);
        }
    }
    // Clone the actual environment emitter, preserving its native streak geometry.
    // Reproduce GlobalWind's module writes on this instance; never drive the donor.
    internal sealed class PatronRecognitionWind : MonoBehaviour
    {
        private GameObject _root;
        private ParticleSystem _system;
        private bool _align, _velocity, _force, _emission;
        private float _multiplier, _minimum, _maximum, _age;
        private Func<bool> _current;
        internal static PatronRecognitionWind Create(Transform parent, Func<bool> current, Func<bool> ready)
        {
            ParticleSystem donor = NativePerkAssetResolver.WindParticles();
            if (donor == null || current == null || ready == null || !current() || !ready()) return null;
            var root = Combat100RecognitionParticles.Clone(donor, parent, 96, 3.8f);
            if (root == null) return null;
            var wind = root.AddComponent<PatronRecognitionWind>();
            wind._root = root; wind._current = current;
            wind._system = root.GetComponentInChildren<ParticleSystem>(true);
            foreach (GlobalWind source in UnityEngine.Object.FindObjectsOfType<GlobalWind>())
                if (source != null && source.m_ps == donor)
                {
                    wind._align = source.m_alignToWindDirection;
                    wind._velocity = source.m_particleVelocity;
                    wind._force = source.m_particleForce;
                    wind._emission = source.m_particleEmission;
                    wind._multiplier = source.m_multiplier;
                    wind._minimum = source.m_particleEmissionMin;
                    wind._maximum = source.m_particleEmissionMax;
                    break;
                }
            if (wind._system == null)
            { root.GetComponent<DeferredNativeVfx>()?.Abort(); return null; }
            var shape = wind._system.shape;
            // Only recognition volume changes; native type/motion/renderer are retained.
            shape.position = Vector3.zero;
            if (shape.shapeType == ParticleSystemShapeType.Box ||
                shape.shapeType == ParticleSystemShapeType.BoxShell ||
                shape.shapeType == ParticleSystemShapeType.BoxEdge)
            {
                Vector3 size = shape.scale;
                float largest = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                if (largest > 5f) shape.scale = size * (5f / largest);
            }
            else shape.radius = Mathf.Min(shape.radius, 2.5f);
            wind.SampleNativeWind();
            var main = wind._system.main; main.loop = true;
            root.transform.localPosition = Vector3.up * .8f;
            NativeVfxSafeFrame.AfterReady(root, () =>
            {
                if (root == null || !current() || !ready())
                { if (root != null) root.GetComponent<DeferredNativeVfx>()?.Abort(); return; }
                wind.SampleNativeWind();
                wind._system.Play(false);
                MasteryPlugin.Log.LogInfo("[Combat100 Recognition] native wind donor=" + donor.name +
                    " renderer=" + wind._system.GetComponent<ParticleSystemRenderer>()?.renderMode +
                    " shape=" + wind._system.shape.shapeType + " maxParticles=" + wind._system.main.maxParticles);
            });
            root.SetActive(true);
            return wind;
        }
        internal void SetPhase(float age) { _age = age; }
        private void SampleNativeWind()
        {
            var env = EnvMan.instance;
            if (env == null || _system == null) return;
            if (_align && env.GetWindDir().sqrMagnitude > .0001f)
                _system.transform.rotation = Quaternion.LookRotation(env.GetWindDir(), Vector3.up);
            if (!_system.emission.enabled) return;
            Vector3 force = env.GetWindForce();
            if (_velocity)
            {
                var velocity = _system.velocityOverLifetime;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = force.x * _multiplier; velocity.z = force.z * _multiplier;
            }
            if (_force)
            {
                var acceleration = _system.forceOverLifetime;
                acceleration.space = ParticleSystemSimulationSpace.World;
                acceleration.x = force.x * _multiplier; acceleration.z = force.z * _multiplier;
            }
            var emission = _system.emission;
            if (_emission) emission.rateOverTimeMultiplier = Mathf.Clamp(
                Mathf.Lerp(_minimum, _maximum, env.GetWindIntensity()), 0f, 32f);
        }
        private void LateUpdate()
        {
            if (_current == null || !_current()) { _root?.GetComponent<DeferredNativeVfx>()?.Abort(); return; }
            if (_age >= 3.3f && _system != null)
                _system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
