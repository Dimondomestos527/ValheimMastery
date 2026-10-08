using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMastery
{
    internal sealed class MasterIdolVisual : MonoBehaviour
    {
        public MeshRenderer NativeModel;
        public GameObject NativeGlow;
        public EffectList ActivateSound;
        private Light _light;
        private int _switch;
        private Material[] _original;
        private readonly List<Material> _owned = new List<Material>();
        private Color _color;
        private bool _initialized, _on;
        private float _started, _from, _brightness;
        private ZNetView _view;
        private Color _lastEmission;private float _lastIntensity;private bool _emissionWritten;private int _pilotEpoch;
        private float _welcomeAt=-10f,_lastWelcomePulse=-10f;
        private MasterIdolWelcome _welcomeOwner;
        internal void WelcomePulse(MasterIdolWelcome owner,float remaining){if(Time.unscaledTime-_lastWelcomePulse<.75f)return;_lastWelcomePulse=Time.unscaledTime;_welcomeAt=Time.unscaledTime-(MasterIdolWelcomeRules.Duration-Mathf.Clamp(remaining,0,MasterIdolWelcomeRules.Duration));_welcomeOwner=owner;}
        private static Color Palette(string id)
        {
            switch (id)
            {
                case "Meadows": return new Color(1f, .82f, .18f);
                case "BlackForest": return new Color(.18f, .8f, .38f);
                case "Swamp": return new Color(.64f, 1f, .12f);
                case "Mountain": return new Color(.35f, .8f, 1f);
                case "Plains": return new Color(1f, .25f, .025f);
                default: return new Color(.72f, .35f, 1f);
            }
        }
        private void Update()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (_view == null) _view = GetComponent<ZNetView>();
            var zdo = _view?.GetZDO();
            if (zdo == null || MasterIdolWorldRegistry.Profile(zdo.GetPrefab()) == null) return; // templates/placement ghosts
            int state = zdo.GetInt(MasterIdolWorldRegistry.SwitchKey, 0);
            bool on = GoldCraftingService.Enabled && zdo.GetBool(MasterIdolWorldRegistry.ActiveKey, false) &&
                zdo.GetBool(MasterIdolWorldRegistry.BoundKey, false) && MasterIdolToggleRules.On(zdo.GetInt(MasterIdolWorldRegistry.SwitchKey, 0));
            if (!_initialized)
            {
                _initialized = true; _switch = state; _on = on; _brightness = _from = on ? 1f : 0f; _started = Time.time - 2f;
                _color = Palette(MasterIdolWorldRegistry.Profile(zdo.GetPrefab()).Id);
                if (NativeGlow != null && NativeGlow != gameObject && NativeGlow.transform.IsChildOf(transform))
                {
                    foreach (var light in NativeGlow.GetComponentsInChildren<Light>(true)) { light.enabled = false; }
                    foreach (var particles in NativeGlow.GetComponentsInChildren<ParticleSystem>(true))
                    { var main = particles.main; main.startColor = _color; main.maxParticles = Mathf.Min(main.maxParticles, 12); var emission = particles.emission; emission.rateOverTimeMultiplier *= .25f; }
                }
                else NativeGlow = null;
                var halo = new GameObject("VM_IdolBiomeLight"); halo.transform.SetParent(transform, false); halo.transform.localPosition = Vector3.up * .8f;
                _light = halo.AddComponent<Light>(); _light.type = LightType.Point; _light.color = _color; _light.range = 2f; _light.shadows = LightShadows.None;
                if (NativeModel == null) return;
                _original = NativeModel.sharedMaterials; var copies = (Material[])_original.Clone();
                for (int i = 0; i < copies.Length; i++)
                {
                    var source = copies[i];
                    // Preserve the native line mask; never tint the whole stone as a fallback.
                    if (source == null || !source.HasProperty("_EmissionColor") || !source.HasProperty("_EmissionMap") || source.GetTexture("_EmissionMap") == null) continue;
                    var copy = new Material(source); copy.EnableKeyword("_EMISSION"); copies[i] = copy; _owned.Add(copy);
                }
                NativeModel.sharedMaterials = copies;
                if (_owned.Count == 0) MasteryPlugin.Log.LogWarning("[MasterIdols] Native emission mask/color unavailable; no visual substitute applied.");
            }
            if (_switch != state)
            {
                bool activate = MasterIdolToggleRules.On(state) && !MasterIdolToggleRules.On(_switch);
                _switch = state;
                if (activate && on && ActivateSound?.m_effectPrefabs != null)
                    foreach (var effect in ActivateSound.m_effectPrefabs)
                        if (effect.m_enabled && PerkAudioService.PlayPrefab("idol:" + zdo.m_uid, effect.m_prefab, transform.position)) break;
            }
            if(!on){_welcomeOwner=null;_welcomeAt=-10f;}
            bool visible = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (NativeGlow != null && NativeGlow.activeSelf != (on && visible)) NativeGlow.SetActive(on && visible);
            if (_on != on) { _from = _brightness; _on = on; _started = Time.time; }
            _brightness = MasterIdolToggleRules.Brightness(_from, _on, Time.time - _started);
            float pulse=on&&visible&&_welcomeOwner!=null&&_welcomeOwner.IsPresenting?Mathf.Sin(Mathf.Clamp01((Time.unscaledTime-_welcomeAt)/MasterIdolWelcomeRules.Duration)*Mathf.PI)*.45f:0f;
            if(!MasterIdolPilotA.Enabled)
            {
                foreach (var material in _owned) material.SetColor("_EmissionColor", _color * (visible ? (_brightness+pulse) * 1.5f : 0f));
                if (_light != null) _light.intensity = visible ? (_brightness+pulse) * .35f : 0f;
                _emissionWritten=false;
            }
            else
            {
                int epoch=MasterIdolPilotA.Generation;if(epoch!=_pilotEpoch){_pilotEpoch=epoch;_emissionWritten=false;}
                Color emission=_color*(visible?(_brightness+pulse)*1.5f:0f);
                float intensity=visible?(_brightness+pulse)*.35f:0f;
                if(!_emissionWritten||!_lastEmission.Equals(emission)){foreach(var material in _owned)material.SetColor("_EmissionColor",emission);_lastEmission=emission;}
                if(_light!=null&&(!_emissionWritten||_lastIntensity!=intensity)){_light.intensity=intensity;_lastIntensity=intensity;}
                _emissionWritten=true;
            }
        }
        private void OnDestroy()
        {
            if (NativeModel != null && _original != null) NativeModel.sharedMaterials = _original;
            foreach (var material in _owned) if (material != null) Destroy(material);
            _owned.Clear();
        }
    }
}




