using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMastery
{
    // Deferred, cosmetic-only emitters. Geometry/rune readiness never depends on ambient assets.
    internal sealed class MasterIdolEnvironment : IDisposable
    {
        private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();
        private readonly List<Material> _materials = new List<Material>();
        private Transform _parent;
        private GameObject _root;
        private string _id;
        private bool _active, _ready, _reportedPending;
        private float _retryAt;
        private int _attempts;
        internal bool IsReady => _ready;
        internal static MasterIdolEnvironment Create(Transform parent, string id, Color color) =>
            new MasterIdolEnvironment { _parent = parent, _id = id };

        private void CloneNative(GameObject source, string name, Vector3 position, float scale, bool forest)
        {
            var clone = UnityEngine.Object.Instantiate(source, _root.transform, false);
            clone.SetActive(false); clone.name = "VM_" + name;
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
                if (!(component is Transform) && !(component is ParticleSystem) && !(component is ParticleSystemRenderer))
                    UnityEngine.Object.DestroyImmediate(component);
            var systems = clone.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0) throw new InvalidOperationException("Native particle prefab has no emitters: " + name);
            int capacity = Math.Max(1, (forest ? 12 : 24) / systems.Length);
            foreach (var ps in systems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.playOnAwake = false; main.loop = true; main.stopAction = ParticleSystemStopAction.None;
                main.maxParticles = Mathf.Clamp(main.maxParticles, 1, capacity); main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var collision = ps.collision; collision.enabled = false; var trigger = ps.trigger; trigger.enabled = false;
                var sub = ps.subEmitters; sub.enabled = false; var lights = ps.lights; lights.enabled = false;
                if (forest)
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f); main.startSpeed = 0f;
                    var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .75f;
                    var emission = ps.emission; emission.rateOverTime = 2f / systems.Length; emission.rateOverDistance = 0;
                    emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                    var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
                    velocity.x = new ParticleSystem.MinMaxCurve(-.02f,.02f); velocity.y = new ParticleSystem.MinMaxCurve(-.015f,.015f); velocity.z = new ParticleSystem.MinMaxCurve(-.02f,.02f);
                    var noise = ps.noise; if (noise.enabled) noise.strengthMultiplier = Mathf.Min(noise.strengthMultiplier,.035f);
                }
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.sharedMaterial == null) throw new InvalidOperationException("Native particle renderer unavailable: " + name);
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                _particles.Add(ps);
            }
            clone.transform.localPosition = position; clone.transform.localRotation = Quaternion.identity; clone.transform.localScale = Vector3.one * scale;
            clone.SetActive(true);
        }
        private static ParticleSystem SnowSource(GameObject source)
        {
            if (source == null) return null;
            foreach (var ps in source.GetComponentsInChildren<ParticleSystem>(true))
            {
                var mat = ps.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
                if (mat != null && mat.name.StartsWith("snow_flake", StringComparison.OrdinalIgnoreCase)) return ps;
            }
            return null;
        }
        private bool TryBuild()
        {
            if (_parent == null) return false;
            GameObject nativePrefab = null;
            Material particleMaterial = null;
            ParticleSystem snowSource = null;
            if (_id == "Plains")
                nativePrefab = NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/Fire/fx_Torch_Basic.prefab");
            else if (_id == "Mountain")
                snowSource = SnowSource(NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/weather/SnowStorm.prefab"));
            else
            {
                nativePrefab = NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/FireFlies.prefab");
                particleMaterial = nativePrefab?.GetComponentInChildren<ParticleSystemRenderer>(true)?.sharedMaterial;
            }
            if (_id == "Mountain" ? snowSource == null : nativePrefab == null || (_id != "Plains" && particleMaterial == null)) return false;
            try
            {
                _root = new GameObject("VM_IdolAmbient_" + _id); _root.SetActive(false); _root.transform.SetParent(_parent,false);
                switch (_id)
                {
                    case "Meadows":
                        Add(particleMaterial,"Pollen",new Vector3(0,.30f,0),new Color(1,.86f,.5f,.55f),.80f,2,4,.04f,.03f,12);
                        break;
                    case "BlackForest":
                        CloneNative(nativePrefab,"NativeBlackForestFireFlies",new Vector3(0,.45f,0),1,true);
                        break;
                    case "Swamp":
                        Add(particleMaterial,"DampMist",new Vector3(0,.16f,0),new Color(.28f,.4f,.12f,.12f),.80f,2,3,.20f,.015f,12);
                        Add(particleMaterial,"Moisture",new Vector3(0,.65f,0),new Color(.55f,.65f,.5f,.28f),.65f,2,2,.022f,-.18f,12);
                        break;
                    case "Mountain":
                        var snow = NativeSnowVisualAssets.CreateSnow(_root.transform,24);
                        if (snow == null) throw new InvalidOperationException("Native snow emitter pending.");
                        snow.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); snow.transform.localPosition = new Vector3(0,1.65f,0);
                        var main = snow.main; main.playOnAwake = false; main.loop = true; main.startLifetime = 4; main.startSize = .035f; main.startSpeed = 0;
                        var shape = snow.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .80f; shape.scale = new Vector3(1,.12f,1);
                        var emission = snow.emission; emission.enabled = true; emission.rateOverTime = 6; emission.rateOverDistance = 0;
                        var velocity = snow.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local; velocity.y = -.35f;
                        _particles.Add(snow);
                        break;
                    case "Plains":
                        CloneNative(nativePrefab,"NativeTorchFlame",new Vector3(0,.10f,.52f),.18f,false);
                        break;
                    case "Mistlands":
                        Add(particleMaterial,"LowMist",new Vector3(0,.20f,0),new Color(.62f,.51f,.86f,.14f),.80f,3,3,.23f,.012f,12);
                        Add(particleMaterial,"EitrMotes",new Vector3(0,.90f,0),new Color(.58f,1,.7f,.5f),.50f,2,3,.035f,.04f,12);
                        break;
                    default: throw new InvalidOperationException("Unknown idol ambience profile.");
                }
                foreach (var ps in _particles) ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                _root.SetActive(true); _ready = true;
                if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log?.LogInfo("[MasterIdols] Ambient ready: " + _id + "; emitters=" + _particles.Count);
                return true;
            }
            catch (Exception e)
            {
                ClearEmitters();
                if (!_reportedPending) { _reportedPending = true; MasteryPlugin.Log?.LogWarning("[MasterIdols] Ambient build pending: " + _id + ": " + e.Message); }
                return false;
            }
        }
        private ParticleSystem Add(Material source, string name, Vector3 position, Color color, float radius, float rate, float life, float size, float vertical, int capacity)
        {
            var obj = new GameObject("VM_Idol" + name); obj.SetActive(false); obj.transform.SetParent(_root.transform,false); obj.transform.localPosition = position;
            var ps = obj.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = life; main.startSpeed = 0; main.startSize = size; main.startColor = color; main.maxParticles = capacity; main.stopAction = ParticleSystemStopAction.None;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = radius;
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = rate;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-.025f,.025f); velocity.y = vertical; velocity.z = new ParticleSystem.MinMaxCurve(-.025f,.025f);
            var lifetime = ps.colorOverLifetime; lifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(1,.7f),new GradientAlphaKey(0,1)});
            lifetime.color = gradient;
            var collision = ps.collision; collision.enabled = false; var trigger = ps.trigger; trigger.enabled = false; var sub = ps.subEmitters; sub.enabled = false; var lights = ps.lights; lights.enabled = false;
            var renderer = obj.GetComponent<ParticleSystemRenderer>(); var material = new Material(source) { name = "VM_Idol" + name };
            _materials.Add(material); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _particles.Add(ps); obj.SetActive(true); return ps;
        }
        internal void Tick(bool active)
        {
            bool changed = _active != active; _active = active;
            if (!active)
            {
                if (changed) foreach (var ps in _particles) if (ps != null) ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }
            bool created = false;
            if (!_ready && Time.unscaledTime >= _retryAt)
            {
                if (_attempts < 61) _attempts++;
                _retryAt = Time.unscaledTime + (_attempts < 60 ? .5f : 10f);
                created = TryBuild();
                if (!created && _attempts == 60 && !_reportedPending)
                {
                    _reportedPending = true;
                    MasteryPlugin.Log?.LogWarning("[MasterIdols] Ambient native assets pending; model/runes unaffected; retrying every10s: " + _id);
                }
            }
            if (_ready && (changed || created))
                foreach (var ps in _particles) if (ps != null) ps.Play(false);
        }
        private static void Cleanup(Action action)
        {
            try { action(); } catch (Exception e) { MasteryPlugin.Log?.LogWarning("[MasterIdols] Ambient cleanup: " + e.Message); }
        }
        private void ClearEmitters()
        {
            foreach (var ps in _particles) if (ps != null) Cleanup(() => ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear));
            if (_root != null) { Cleanup(() => _root.SetActive(false)); Cleanup(() => UnityEngine.Object.Destroy(_root)); _root = null; }
            foreach (var material in _materials) if (material != null) Cleanup(() => UnityEngine.Object.Destroy(material));
            _particles.Clear(); _materials.Clear(); _ready = false;
        }
        public void Dispose()
        {
            ClearEmitters(); _parent = null; _active = false;
        }
    }
}
