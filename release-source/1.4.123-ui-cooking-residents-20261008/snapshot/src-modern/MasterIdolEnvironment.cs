using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMastery
{
    // Decorative children only: no terrain, collectible resources, colliders, lights or gameplay scripts.
    internal sealed class MasterIdolEnvironment : IDisposable
    {
        private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();
        private readonly List<Material> _materials = new List<Material>();
        private bool _active;
        internal static bool Ready(string id)
        {
            bool ready = NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/FireFlies.prefab") != null;
            if (id == "Plains") ready &= NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/Fire/fx_Torch_Basic.prefab") != null;
            if (id == "Mountain") ready &= NativeSnowVisualAssets.Snow() != null;
            return ready;
        }
        private void CloneNative(Transform parent, GameObject source, string name, Vector3 position, float scale, bool forest)
        {
            var clone = UnityEngine.Object.Instantiate(source, parent, false); clone.SetActive(false); clone.name = "VM_" + name;
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
                if (!(component is Transform) && !(component is ParticleSystem) && !(component is ParticleSystemRenderer)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var ps in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.playOnAwake = false; main.loop = true; main.stopAction = ParticleSystemStopAction.None;
                main.maxParticles = Mathf.Min(main.maxParticles, forest ? 12 : 24); main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var collision = ps.collision; collision.enabled = false; var trigger = ps.trigger; trigger.enabled = false;
                var sub = ps.subEmitters; sub.enabled = false; var lights = ps.lights; lights.enabled = false;
                if (forest)
                {
                    var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .85f;
                    var emission = ps.emission; emission.rateOverTime = 2f; emission.rateOverDistance = 0; emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                }
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                _particles.Add(ps);
            }
            clone.transform.localPosition = position; clone.transform.localRotation = Quaternion.identity; clone.transform.localScale = Vector3.one * scale;
            clone.SetActive(true);
        }
        internal static MasterIdolEnvironment Create(Transform parent, string id, Color color)
        {
            var env = new MasterIdolEnvironment();
            try
            {
                var fireflies = NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/FireFlies.prefab");
                var native = fireflies?.GetComponentInChildren<ParticleSystemRenderer>(true)?.sharedMaterial;
                if (id == "BlackForest")
                {
                    if (fireflies != null) env.CloneNative(parent,fireflies,"NativeBlackForestFireFlies",new Vector3(0,.45f,0),1,true);
                }
                if (native == null)
                {
                    if (env._particles.Count == 0) MasteryPlugin.Log?.LogWarning("[MasterIdols] Native ambient particle material unavailable: " + id);
                    return env;
                }
                switch (id)
                {
                    case "Meadows": env.Add(parent,native,"Pollen",new Vector3(0,.25f,0),new Color(1,.86f,.5f,.42f),.8f,2,4,.025f,.03f); break;
                    case "Swamp":
                        env.Add(parent,native,"DampMist",new Vector3(0,.15f,0),new Color(.28f,.4f,.12f,.1f),.8f,2,3,.2f,.015f);
                        env.Add(parent,native,"Moisture",new Vector3(0,.65f,0),new Color(.55f,.65f,.5f,.28f),.65f,2,2,.022f,-.18f); break;
                    case "Mountain":
                        var snow = NativeSnowVisualAssets.CreateSnow(parent,24);
                        if (snow != null)
                        {
                            snow.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); snow.transform.localPosition = new Vector3(0,1.1f,0);
                            var shape = snow.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .8f;
                            var main = snow.main; main.startLifetime = 4; main.startSize = .025f;
                            var emission = snow.emission; emission.enabled = true; emission.rateOverTime = 6;
                            var velocity = snow.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local; velocity.y = -.22f;
                            env._particles.Add(snow);
                        }
                        break;
                    case "Plains":
                        var torch = NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/Fire/fx_Torch_Basic.prefab");
                        if (torch != null) env.CloneNative(parent,torch,"NativeTorchFlame",new Vector3(0,1.82f,0),.28f,false);
                        break;
                    case "Mistlands":
                        env.Add(parent,native,"LowMist",new Vector3(0,.2f,0),new Color(.62f,.51f,.86f,.11f),.85f,3,3,.23f,.012f);
                        env.Add(parent,native,"EitrMotes",new Vector3(0,.9f,0),new Color(.58f,1,.7f,.5f),.5f,2,3,.035f,.04f); break;
                }
            }
            catch (Exception e) { env.Dispose(); MasteryPlugin.Log?.LogWarning("[MasterIdols] Ambient presentation unavailable: " + id + ": " + e.Message); }
            return env;
        }
        private ParticleSystem Add(Transform parent, Material source, string name, Vector3 position, Color color, float radius, float rate, float life, float size, float vertical)
        {
            var obj = new GameObject("VM_Idol" + name); obj.SetActive(false); obj.transform.SetParent(parent, false); obj.transform.localPosition = position;
            var ps = obj.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = life; main.startSpeed = 0; main.startSize = size; main.startColor = color; main.maxParticles = 24;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = radius;
            var emission = ps.emission; emission.rateOverTime = rate;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local; velocity.x = new ParticleSystem.MinMaxCurve(-.025f,.025f); velocity.y = vertical; velocity.z = new ParticleSystem.MinMaxCurve(-.025f,.025f);
            var lifetime = ps.colorOverLifetime; lifetime.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(1,.7f),new GradientAlphaKey(0,1)}); lifetime.color = gradient;
            var renderer = obj.GetComponent<ParticleSystemRenderer>(); var material = new Material(source) { name = "VM_Idol" + name }; _materials.Add(material);
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _particles.Add(ps); obj.SetActive(true); return ps;
        }
        internal void Tick(bool active)
        {
            if (_active == active) return; _active = active;
            foreach (var ps in _particles) if (ps != null)
            { if (active) ps.Play(false); else ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear); }
        }
        public void Dispose()
        {
            foreach (var ps in _particles) if (ps != null) ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
            _particles.Clear(); _materials.Clear(); _active = false;
        }
    }
}
