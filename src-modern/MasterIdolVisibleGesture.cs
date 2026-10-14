using System;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Own native Movement: eventless idle for each family; finite own Greydwarf vibe only.
    internal sealed class MasterIdolVisibleGesture : MonoBehaviour
    {
        private const string Rpc = "VM_Idol_VisibleGesture_v2";
        private const int Movement = unchecked((int)3885065915u);
        private Character Actor; private BaseAI Ai; private ZNetView View, Registered; private ZDO Data;
        private ZNet Session; private ZDOMan Manager; private ulong Generation; private long World, Owner, ContextTicks, LastStamp; private ushort Revision;
        private Animator Animator; private RuntimeAnimatorController Original; private AnimatorOverrideController Override; private AnimationClip Clip;
        private bool Active, Lease; private float End; private string Home, BroadcastHome; private long LeaseEpoch;
        private static int GraphicsScenes; private static long GraphicsEpoch;
        private static ZNet GraphicsSession; private static ZDOMan GraphicsManager; private static ulong GraphicsGeneration;
        private static long GraphicsWorld;
        internal static int GraphicsCount { get { GraphicsContext(); return GraphicsScenes; } }
        private static void GraphicsContext()
        {
            long world = ZNet.instance?.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
            if (GraphicsSession == ZNet.instance && GraphicsManager == ZDOMan.instance && GraphicsGeneration == MasterIdolLoadIdentity.Generation && GraphicsWorld == world) return;
            GraphicsSession = ZNet.instance; GraphicsManager = ZDOMan.instance; GraphicsGeneration = MasterIdolLoadIdentity.Generation; GraphicsWorld = world; GraphicsScenes = 0; GraphicsEpoch++;
        }
        private void Awake() { Actor = GetComponent<Character>(); Ai = GetComponent<BaseAI>(); View = GetComponent<ZNetView>(); }
        private bool Context()
        {
            GraphicsContext();
            if (View?.IsValid() != true || ZNet.instance?.GetWorld() == null || !MasterIdolLoadIdentity.LoadReady) return false;
            if (Registered != View) { View.Register<ZPackage>(Rpc, Receive); Registered = View; }
            var data = View.GetZDO(); if (data == null || !ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data)) return false;
            if (!ReferenceEquals(Data, data) || Session != ZNet.instance || Manager != ZDOMan.instance || Generation != MasterIdolLoadIdentity.Generation ||
                Owner != data.GetOwner() || Revision != data.OwnerRevision || World != ZNet.instance.GetWorldUID())
            { Stop(); BroadcastHome = null; Data = data; Session = ZNet.instance; Manager = ZDOMan.instance; Generation = MasterIdolLoadIdentity.Generation; Owner = data.GetOwner(); Revision = data.OwnerRevision; World = Session.GetWorldUID(); ContextTicks = Session.GetTime().Ticks; LastStamp = 0; }
            return true;
        }
        private bool Peace() => Actor != null && Ai != null && !MasterIdolSocialSeats.Battle(Ai, Actor) && Ai.m_timeSinceHurt >= 8 && !Ai.IsSleeping() && !Actor.InDodge() && !Actor.InEmote() &&
            GetComponent<MasterIdolSocialSeats>()?.Active != true && GetComponent<MasterIdolWelcome>()?.IsPresenting != true && !MasterIdolResidentWork.Busy(Actor) &&
            GetComponent<MasterIdolResidentPresentation>()?.Playing != true && !MasterIdolResidentSceneAnimation.Playing(Actor)&&!MasterIdolResidentBirthPresentation.Playing(Actor);
        internal static bool Playing(Character actor) => actor?.GetComponent<MasterIdolVisibleGesture>()?.Active == true;
        internal static void Start(Character actor) => Start(actor, true, MasterIdolResidentIdleRules.SlotSeconds);
        internal static void Start(Character actor, bool vibe, float duration)
        {
            if (actor == null || MasterIdolResidentRoster.Limit(Utils.GetPrefabName(actor.gameObject)) == 0) return;
            var scene = actor.GetComponent<MasterIdolVisibleGesture>();
            if (scene == null || !scene.Context() || !scene.View.IsOwner() || !scene.Peace() || !MasterIdolSocialScenes.Active(actor.GetComponent<MasterIdolSocial>()) ||
                !MasterIdolResidentVisibility.Observed(actor) || !MasterIdolResidentPolicy.TryHome(actor, out var zone)) return;
            vibe = vibe && Utils.GetPrefabName(actor.gameObject) == "Greydwarf";
            duration = vibe ? MasterIdolResidentIdleRules.VibeSeconds : MasterIdolResidentIdleRules.SlotSeconds;
            scene.BroadcastHome = zone.Id; // Dedicated authority has no local graphics lease but must cancel peer cues.
            var packet = scene.Header(zone.Id, scene.Session.GetTime().Ticks, true); packet.Write(vibe); packet.Write(duration);
            scene.Receive(scene.Owner, new ZPackage(packet.GetArray())); scene.View.InvokeRPC(ZNetView.Everybody, Rpc, packet);
        }
        private ZPackage Header(string home, long stamp, bool begin)
        {
            var packet = new ZPackage(); packet.Write(World); MasterIdolResidentWork.WriteUid(packet, Data.m_uid); packet.Write((int)Revision);
            packet.Write(home); packet.Write(stamp); packet.Write(begin); return packet;
        }
        private void Receive(long sender, ZPackage packet)
        {
            try
            {
                if (packet == null || packet.Size() > 256 || !Context() || sender != Owner) return;
                long world = packet.ReadLong(); var uid = MasterIdolResidentWork.ReadUid(packet); int revision = packet.ReadInt();
                string home = packet.ReadString(); long stamp = packet.ReadLong(); bool begin = packet.ReadBool();
                bool vibe = false; float duration = 0; if (begin) { vibe = packet.ReadBool(); duration = packet.ReadSingle(); }
                if (packet.GetPos() != packet.Size() || world != World || uid != Data.m_uid || revision != Revision || home.Length > 128 ||
                    stamp < LastStamp || begin && stamp == LastStamp || !MasterIdolVisibilityRules.Stamp(stamp, Session.GetTime().Ticks, ContextTicks)) return;
                if (!begin) { if (Home == null || home == Home) { LastStamp = stamp; Stop(); } return; }
                if (!MasterIdolResidentPolicy.TryHome(Actor, out var zone) || zone.Id != home || !zone.Contains(transform.position) ||
                    !float.IsFinite(duration) || duration != (vibe ? MasterIdolResidentIdleRules.VibeSeconds : MasterIdolResidentIdleRules.SlotSeconds) ||
                    vibe && Utils.GetPrefabName(gameObject) != "Greydwarf") return;
                LastStamp = stamp; // A denied/offscreen cue is never replayed when the viewer returns.
                if (Active || !MasterIdolResidentVisibility.Local(Actor) || !Peace() ||
                    GraphicsCount + MasterIdolResidentSceneAnimation.GraphicsCount + MasterIdolResidentBirthPresentation.GraphicsCount >= MasterIdolResidentIdleRules.MaxGraphics) return;
                Animator = GetComponentInChildren<Animator>(); Original = Animator?.runtimeAnimatorController;
                if (Original == null || Original is AnimatorOverrideController || !Animator.HasState(0, Movement) || Animator.IsInTransition(0) ||
                    Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Movement) return;
                AnimationClip idle = null, donor = null;
                string ownIdle = Utils.GetPrefabName(gameObject) == "Greydwarf_Shaman" ? "Standing Idle" : "Idle";
                foreach (var source in Original.animationClips)
                {
                    if (source.name == ownIdle && source.events.Length == 0) idle = source;
                    if (source.name == "George Vibing" && Mathf.Abs(source.length - 3.75f) < .05f && source.events.Length == 0) donor = source;
                }
                if (idle == null || vibe && donor == null) return;
                float elapsed = Mathf.Max(0, (float)((Session.GetTime().Ticks - stamp) / (double)TimeSpan.TicksPerSecond));
                if (elapsed >= duration) return;
                Home = home; End = Time.unscaledTime + duration - elapsed;
                if (vibe)
                {
                    Clip = Instantiate(donor); Clip.name = "VM_EventlessVisibleGreydwarfGesture"; Clip.hideFlags = HideFlags.DontSave; Clip.events = Array.Empty<AnimationEvent>();
                    Override = new AnimatorOverrideController(Original); Override[idle] = Clip; Animator.runtimeAnimatorController = Override;
                }
                LeaseEpoch = GraphicsEpoch; Lease = Active = true; GraphicsScenes++;
                Hold(); Animator.Play(Movement, 0, elapsed / (vibe ? donor.length : idle.length));
                MasterIdolResidentMetrics.Count(vibe ? "idle.native-vibe-start" : "idle.native-idle-start");
            }
            catch (Exception) { Stop(); MasterIdolResidentMetrics.Count("idle.native-start-failed"); }
        }
        private void Update()
        {
            try
            {
                if (!Context()) { Stop(); return; } if (!Active) return;
                var owned = Override != null ? (RuntimeAnimatorController)Override : Original;
                if (Time.unscaledTime >= End || !MasterIdolResidentVisibility.Local(Actor) || !Peace() || Animator == null ||
                    !ReferenceEquals(Animator.runtimeAnimatorController, owned) || Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Movement ||
                    Animator.IsInTransition(0) || !MasterIdolResidentPolicy.TryHome(Actor, out var zone) || zone.Id != Home || !zone.Contains(transform.position)) Stop();
            }
            catch (Exception) { Stop(); }
        }
        private void Hold()
        {
            var owned = Override != null ? (RuntimeAnimatorController)Override : Original;
            if (Active && Animator != null && ReferenceEquals(Animator.runtimeAnimatorController, owned))
            { Animator.SetFloat("forward_speed", 0); Animator.SetFloat("sideway_speed", 0); Animator.SetFloat("turn_speed", 0); }
        }
        private void LateUpdate() { try { Hold(); } catch (Exception) { Stop(); } }
        private void Stop()
        {
            Active = false;
            try { if (Animator != null && Override != null && ReferenceEquals(Animator.runtimeAnimatorController, Override)) Animator.runtimeAnimatorController = Original; }
            finally
            {
                try { if (Override != null) Destroy(Override); }
                finally
                {
                    try { if (Clip != null) Destroy(Clip); }
                    finally { Override = null; Clip = null; Original = null; Home = null; if (Lease && LeaseEpoch == GraphicsEpoch) GraphicsScenes = Math.Max(0, GraphicsScenes - 1); Lease = false; }
                }
            }
        }
        internal static void Cancel(Character actor)
        {
            var scene = actor?.GetComponent<MasterIdolVisibleGesture>(); if (scene == null) return;
            try
            {
                if (scene.Context() && scene.View.IsOwner())
                {
                    string home = scene.BroadcastHome ?? scene.Home;
                    if (home != null) scene.View.InvokeRPC(ZNetView.Everybody, Rpc, scene.Header(home, scene.Session.GetTime().Ticks, false));
                }
            }
            finally { scene.BroadcastHome = null; scene.Stop(); }
        }
        private void OnDisable() => Stop(); private void OnDestroy() => Stop();
    }
    [HarmonyPatch(typeof(BaseAI), "Awake")]
    internal static class MasterIdolVisibleGestureAttachPatch
    { private static void Postfix(BaseAI __instance) { if (MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject)) > 0 && __instance.GetComponent<MasterIdolVisibleGesture>() == null) __instance.gameObject.AddComponent<MasterIdolVisibleGesture>(); } }
}
