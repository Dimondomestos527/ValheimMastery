using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // A single renderless rig per local player samples the real native controller.
    // Only upper-body BONE rotations reach the live model. No cloned scripts,
    // renderer, cloth, collider, root motion, attack events or gameplay Animator hold.
    internal sealed class Fire35PoseDriver : MonoBehaviour
    {
        private struct Bone { internal Transform Live, Pose; }
        private readonly List<Bone> Bones = new List<Bone>(64);
        private readonly List<AnimatorClipInfo> Clips = new List<AnimatorClipInfo>(4);
        private Player Owner;
        private Animator Source, Sampler;
        private GameObject Rig;
        private string Trigger;
        private int State, Layer, Nodes;
        private float Length = 1f, Stop = .35f, Fraction;
        private bool Active, Warned;
        private void Awake() => Owner = GetComponent<Player>();
        internal void Begin(Player player, string trigger)
        {
            End(); Owner = player; Trigger = trigger;
            try
            {
                if (Source != player.m_animator || Sampler == null)
                {
                    ClearRig(); Source = player.m_animator;
                    if (Source == null || Source.runtimeAnimatorController == null) return;
                    Rig = new GameObject("VM_Fire35_RenderlessPoseRig");
                    Nodes = 0;
                    var boneSet = new HashSet<Transform>();
                    foreach (var skin in Source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        foreach (Transform bone in skin.bones)
                            if (bone != null) boneSet.Add(bone);
                    // Skeleton ancestry only. Equipment and attached VFX can
                    // exceed the budget without contributing any animated bone.
                    var needed = new HashSet<Transform> { Source.transform };
                    foreach (Transform bone in boneSet)
                        for (Transform parent = bone; parent != null; parent = parent.parent)
                        { needed.Add(parent); if (parent == Source.transform) break; }
                    var pairs = new Dictionary<Transform, Transform>();
                    Clone(Source.transform, Rig.transform, pairs, needed);
                    foreach (Transform bone in boneSet)
                        if (Upper(bone.name) && pairs.TryGetValue(bone, out Transform pose))
                            Bones.Add(new Bone { Live = bone, Pose = pose });
                    Sampler = Rig.AddComponent<Animator>();
                    Sampler.avatar = Source.avatar; Sampler.runtimeAnimatorController = Source.runtimeAnimatorController;
                    Sampler.fireEvents = false; Sampler.applyRootMotion = false;
                    Sampler.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Sampler.Rebind(); Sampler.Update(0f); Sampler.speed = 0f;
                }
                Rig.SetActive(true);
                foreach (var parameter in Source.parameters)
                    switch (parameter.type)
                    {
                        case AnimatorControllerParameterType.Bool: Sampler.SetBool(parameter.nameHash, Source.GetBool(parameter.nameHash)); break;
                        case AnimatorControllerParameterType.Int: Sampler.SetInteger(parameter.nameHash, Source.GetInteger(parameter.nameHash)); break;
                        case AnimatorControllerParameterType.Float: Sampler.SetFloat(parameter.nameHash, Source.GetFloat(parameter.nameHash)); break;
                    }
                State = 0; Fraction = 0f; Length = 1f; Stop = .35f; Active = Bones.Count > 0;
                int direct = Animator.StringToHash(trigger);
                for (int layer = 0; layer < Sampler.layerCount; layer++)
                {
                    Sampler.SetLayerWeight(layer, Source.GetLayerWeight(layer));
                    if (State == 0 && Sampler.HasState(layer, direct))
                    { Layer = layer; Sampler.Play(direct, layer, 0f); State = direct; }
                }
                if (State == 0) { Sampler.SetTrigger(trigger); Sampler.speed = 1f; Sampler.Update(.01f); Sampler.speed = 0f; }
                else Sampler.Update(0f);
                Capture();
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Fire35Pose] isolated=" + Active + " bones=" + Bones.Count + " state=" + State + " trigger=" + trigger);
            }
            catch (Exception error)
            {
                ClearRig();
                if (!Warned) { Warned = true; MasteryPlugin.Log.LogWarning("[Fire35Pose] cosmetic unavailable; native attacks preserved: " + error.Message); }
            }
        }
        private void Clone(Transform source, Transform target, Dictionary<Transform, Transform> pairs, HashSet<Transform> needed)
        {
            if (++Nodes > 384) throw new InvalidOperationException("Rig exceeds transform budget");
            pairs[source] = target;
            target.localPosition = source.localPosition; target.localRotation = source.localRotation; target.localScale = source.localScale;
            for (int i = 0; i < source.childCount; i++)
            {
                Transform child = source.GetChild(i);
                if (!needed.Contains(child)) continue;
                var copy = new GameObject(child.name).transform; copy.SetParent(target, false);
                Clone(child, copy, pairs, needed);
            }
        }
        private static bool Upper(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("armature")) return false;
            return lower.Contains("spine") || lower.Contains("chest") || lower.Contains("clavicle") ||
                lower.Contains("shoulder") || lower.Contains("arm") || lower.Contains("hand") ||
                lower.Contains("finger") || lower.Contains("thumb") || lower.Contains("neck") || lower.Contains("head");
        }
        private void Capture()
        {
            if (Sampler == null) return;
            if (State == 0)
                for (int layer = 0; layer < Sampler.layerCount; layer++)
                {
                    var next = Sampler.GetNextAnimatorStateInfo(layer);
                    var current = Sampler.GetCurrentAnimatorStateInfo(layer);
                    if (Sampler.IsInTransition(layer) && next.tagHash == Humanoid.s_animatorTagAttack)
                    { State = next.fullPathHash; Layer = layer; break; }
                    if (current.tagHash == Humanoid.s_animatorTagAttack)
                    { State = current.fullPathHash; Layer = layer; break; }
                }
            if (State == 0) return;
            Clips.Clear();
            if (Sampler.IsInTransition(Layer)) Sampler.GetNextAnimatorClipInfo(Layer, Clips);
            else Sampler.GetCurrentAnimatorClipInfo(Layer, Clips);
            if (Clips.Count == 0 || Clips[0].clip == null) return;
            AnimationClip clip = Clips[0].clip;
            Length = Mathf.Max(.05f, clip.length); Stop = Length * .35f;
            foreach (var evt in clip.events)
                if (evt.functionName == "OnAttackTrigger" && evt.time > .02f) Stop = Mathf.Min(Stop, evt.time * .85f);
        }
        internal void Draw(float held)
        {
            if (!Active || Sampler == null || Owner == null || Owner.IsDead()) return;
            if (State == 0) { Sampler.speed = 1f; Sampler.Update(.01f); Sampler.speed = 0f; Capture(); }
            if (State == 0) return;
            Fraction = Stop / Length * Mathf.SmoothStep(0f, 1f, held / .55f);
            Sampler.Play(State, Layer, Fraction); Sampler.Update(0f);
            float weight = Mathf.SmoothStep(0f, 1f, held / .25f);
            foreach (Bone bone in Bones)
                if (bone.Live != null && bone.Pose != null)
                    bone.Live.localRotation = Quaternion.Slerp(bone.Live.localRotation, bone.Pose.localRotation, weight);
        }
        internal void Release(Player player)
        {
            // ONE resume, after a real native Attack has been admitted. The live
            // Animator advances normally afterward, crossing its release event.
            if (Active && State != 0 && player.m_currentAttack != null && player.m_animator.HasState(Layer, State))
            {
                // StartAttack already sent the native trigger. Consume its local
                // pending copy before resuming, so it cannot restart this pose.
                player.m_animator.ResetTrigger(Trigger);
                player.m_animator.Play(State, Layer, Fraction); player.m_animator.Update(0f);
            }
        }
        internal void End() { Active = false; if (Rig != null) Rig.SetActive(false); }
        private void LateUpdate() => FireStaff35Charge.SamplePose(Owner);
        private void OnDisable() => End();
        private void ClearRig() { Active = false; if (Rig != null) Destroy(Rig); Rig = null; Sampler = null; Source = null; Bones.Clear(); }
        private void OnDestroy() => ClearRig();
    }
}
