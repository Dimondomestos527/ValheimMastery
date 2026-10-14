using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // Observes the existing replicated deadline. Never owns or extends the buff.
    internal sealed class Ashlands35RootBuffVisual : MonoBehaviour
    {
        private sealed class Glow
        {
            internal Renderer Renderer;
            internal int Slot;
            internal float Brightness;
            internal readonly MaterialPropertyBlock Original = new MaterialPropertyBlock();
            internal readonly MaterialPropertyBlock Working = new MaterialPropertyBlock();
        }
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private Character Body;
        private Glow[] Glows;
        private bool Applied;
        private float NextCheck;

        private void Awake() { Body = GetComponent<Character>(); }
        private void LateUpdate()
        {
            if (Time.unscaledTime < NextCheck) return;
            NextCheck = Time.unscaledTime + .1f;
            ZDO data = Ashlands35Summons.Managed(Body);
            bool valid = Body?.m_nview?.IsValid() == true;
            bool active = Ashlands35VisualRules.RootBoostActive(
                MasteryPlugin.Settings.Enabled.Value && MasteryPlugin.Settings.EnablePerkProcVFX.Value,
                Application.isBatchMode, Player.m_localPlayer != null, valid, Body == null || Body.IsDead(),
                data != null, data?.GetInt(Ashlands35Summons.GenerationKey, 0) ?? 0,
                Ashlands35Summons.Now, data?.GetLong(Ashlands35Summons.UntilKey, 0) ?? 0);
            if (!active) { Restore(); return; }
            if (Glows == null && !Capture()) return;
            Color gold = new Color(1f, .62f, .055f, 1f);
            float pulse = Ashlands35VisualRules.RootPulse(Time.unscaledTime);
            foreach (Glow glow in Glows)
            {
                if (glow.Renderer == null) continue;
                // Keep native textures, shader and other live property overrides.
                glow.Renderer.GetPropertyBlock(glow.Working, glow.Slot);
                glow.Working.SetColor(Emission, gold * (glow.Brightness * pulse));
                glow.Renderer.SetPropertyBlock(glow.Working, glow.Slot);
            }
            Applied = true;
        }
        private bool Capture()
        {
            var found = new List<Glow>(4);
            foreach (SkinnedMeshRenderer renderer in Body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length && found.Count < 4; slot++)
                {
                    Material native = materials[slot];
                    if (native == null || !native.name.StartsWith("Thornroot_mat", StringComparison.Ordinal) ||
                        !native.HasProperty("_EmissionColor") || !native.HasProperty("_EmissionMap") ||
                        native.GetTexture("_EmissionMap") == null) continue;
                    Color color = native.GetColor(Emission);
                    var glow = new Glow { Renderer = renderer, Slot = slot,
                        Brightness = Mathf.Max(color.r, Mathf.Max(color.g, color.b)) };
                    renderer.GetPropertyBlock(glow.Original, slot);
                    found.Add(glow);
                }
            }
            // An asynchronously loaded body may become renderable on the next tick.
            if (found.Count == 0) return false;
            Glows = found.ToArray(); return true;
        }
        private void Restore()
        {
            if (Applied && Glows != null)
                foreach (Glow glow in Glows)
                    if (glow.Renderer != null) glow.Renderer.SetPropertyBlock(glow.Original, glow.Slot);
            Applied = false;
            // Fresh snapshot if the VFX setting is toggled during the same buff.
            Glows = null;
        }
        private void OnDisable() { Restore(); }
        private void OnDestroy() { Restore(); }
    }
}
