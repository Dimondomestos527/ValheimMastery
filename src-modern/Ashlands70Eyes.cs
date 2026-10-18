using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class Ashlands70Eyes : MonoBehaviour
    {
        private sealed class Slot { internal Renderer Renderer; internal int Index; internal MaterialPropertyBlock Original; }
        private readonly List<Slot> Slots = new List<Slot>();
        private ParticleSystem Sign;
        private LineRenderer Rune;
        private float SignScale = 1f, HeadOffset = .45f, BodyOffset = .75f;
        private Transform Head;
        private float NextSign;
        internal Character Body;
        internal static Ashlands70Eyes Create(Character body)
        {
            var root = new GameObject("VM_Ashlands70_MadnessEyes"); root.SetActive(false); root.transform.SetParent(body.transform, false);
            var visual = root.AddComponent<Ashlands70Eyes>(); visual.Body = body;
            bool ready = false;
            try
            {
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i]; if (material == null || !material.HasProperty("_EmissionColor")) continue;
                    bool eye = renderer.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0 || material.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!eye) continue; // Never paint an entire integrated creature mesh red.
                    var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original, i);
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, i);
                    block.SetColor("_EmissionColor", new Color(3f, .01f, .01f, 1f));
                    renderer.SetPropertyBlock(block, i); visual.Slots.Add(new Slot { Renderer = renderer, Index = i, Original = original });
                }
            }
            if (visual.Slots.Count == 0)
            {
                visual.Sign = Ashlands70Presentation.RedZaps(root.transform, 16);
                if (visual.Sign == null) return null;
                Material material = visual.Sign.GetComponent<ParticleSystemRenderer>().trailMaterial;
                if (material == null) return null;
                visual.Rune = root.AddComponent<LineRenderer>();
                visual.Rune.sharedMaterial = material; visual.Rune.positionCount = 4; visual.Rune.useWorldSpace = true;
                visual.Rune.widthMultiplier = .035f; visual.Rune.startColor = visual.Rune.endColor = new Color(1f, .035f, .06f, 1f);
                visual.Rune.textureMode = LineTextureMode.Stretch; visual.Rune.alignment = LineAlignment.View;
                visual.Rune.numCornerVertices = 0; visual.Rune.numCapVertices = 0;
                visual.Rune.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; visual.Rune.receiveShadows = false;
                visual.FitFallback(); visual.DrawRune();
            }
            root.SetActive(true); if (visual.Sign != null) visual.Sign.Play(); ready = true; return visual;
            }
            finally
            {
                if (!ready) { visual.Restore(); UnityEngine.Object.Destroy(root); }
            }
        }
        private void Update()
        {
            if (!Ashlands70Presentation.Visible || !Ashlands70Madness.Active(Body)) { Restore(); if (Sign != null) Sign.Clear(); if (Rune != null) Rune.enabled = false; UnityEngine.Object.Destroy(gameObject); return; }
            DrawRune();
            if (Sign == null || Time.unscaledTime < NextSign) return; NextSign = Time.unscaledTime + .15f;
            Vector3 origin = AnchorPoint();
            Vector3 right = GameCamera.instance != null ? GameCamera.instance.transform.right : Vector3.right;
            for (int i = 0; i < 3; i++)
            {
                float angle = Mathf.PI * 2f * i / 3f + Mathf.PI * .5f;
                Ashlands70Presentation.Emit(Sign, origin + right * (Mathf.Cos(angle) * .18f * SignScale) + Vector3.up * (Mathf.Sin(angle) * .18f * SignScale), .12f * SignScale);
            }
        }
        private void FitFallback()
        {
            // The approved native Troll preview normalized its mesh height to
            // 2.8m. Preserve that marker/body ratio on the actual native rig;
            // cache once, then follow the head without per-frame bounds scans.
            Head = Body.m_head;
            GameObject model = Body.GetVisual(); bool found = false; Bounds bounds = default;
            foreach (Renderer renderer in model != null ? model.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>())
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
            }
            if (found)
            {
                SignScale = Mathf.Clamp(bounds.size.y / 2.8f, .65f, 3f);
                BodyOffset = Mathf.Max(0f, bounds.max.y - Body.transform.position.y) + .06f * SignScale;
                HeadOffset = Head != null ? Mathf.Max(0f, bounds.max.y - Head.position.y) + .06f * SignScale : BodyOffset;
            }
            Rune.widthMultiplier = .035f * SignScale;
        }
        private Vector3 AnchorPoint()
        {
            // Native GetHeadPoint unconditionally dereferences nullable m_head.
            // Cache visual bounds once; follow an existing head or the body.
            return Head != null ? Head.position + Vector3.up * HeadOffset : Body.transform.position + Vector3.up * BodyOffset;
        }
        private void DrawRune()
        {
            if (Rune == null || Body == null) return;
            Vector3 origin = AnchorPoint();
            Vector3 right = GameCamera.instance != null ? GameCamera.instance.transform.right : Vector3.right;
            float radius = .18f * SignScale * (1f + .12f * Mathf.Sin(Time.time * 7f));
            for (int i = 0; i < 4; i++)
            {
                float angle = (i % 3) * Mathf.PI * 2f / 3f + Mathf.PI * .5f;
                Rune.SetPosition(i, origin + (right * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * radius);
            }
        }
        private void Restore()
        { foreach (Slot slot in Slots) if (slot.Renderer != null) slot.Renderer.SetPropertyBlock(slot.Original, slot.Index); Slots.Clear(); }
        private void OnDestroy() => Restore();
    }
}
