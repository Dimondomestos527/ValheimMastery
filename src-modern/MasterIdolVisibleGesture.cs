using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Own native Movement only: idle, existing Greydwarf vibe, or an explicit own-family reaction.
    internal sealed class MasterIdolVisibleGesture : MonoBehaviour
    {
        private const string Rpc = "VM_Idol_VisibleGesture_v2";
        private const int Movement = unchecked((int)3885065915u);
        private Character Actor; private BaseAI Ai; private ZNetView View, Registered; private ZDO Data;
        private ZNet Session; private ZDOMan Manager; private ulong Generation; private long World, Owner, ContextTicks, LastStamp; private ushort Revision;
        private Animator Animator; private RuntimeAnimatorController Original; private AnimatorOverrideController Override; private AnimationClip Clip;
        private MasterIdolResidentReactionPose GroundPose;
        private MasterIdolResidentReactionWaveUpperBody Wave;private float CueStart;
        private bool Active, Lease; private float End; private string Home, BroadcastHome; private long LeaseEpoch;
        private byte Style;
        private static readonly HashSet<string> TraceKeys=new HashSet<string>(StringComparer.Ordinal);
        private static ZNet TraceNet;private static ZDOMan TraceManager;private static ulong TraceGeneration;private static long TraceWorld;
        private static int GraphicsScenes,SeatGraphics; private static long GraphicsEpoch;
        private static ZNet GraphicsSession; private static ZDOMan GraphicsManager; private static ulong GraphicsGeneration;
        private static long GraphicsWorld;
        internal static int GraphicsCount { get { GraphicsContext(); return GraphicsScenes+SeatGraphics; } }
        internal static bool ReserveSeat(out long epoch)
        {
            GraphicsContext();epoch=GraphicsEpoch;
            if(GraphicsCount+MasterIdolResidentSceneAnimation.GraphicsCount+MasterIdolResidentBirthPresentation.GraphicsCount>=MasterIdolResidentIdleRules.MaxGraphics)return false;
            SeatGraphics++;return true;
        }
        internal static void ReleaseSeat(long epoch){GraphicsContext();if(epoch==GraphicsEpoch)SeatGraphics=Math.Max(0,SeatGraphics-1);}
        private static void GraphicsContext()
        {
            long world = ZNet.instance?.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
            if (GraphicsSession == ZNet.instance && GraphicsManager == ZDOMan.instance && GraphicsGeneration == MasterIdolLoadIdentity.Generation && GraphicsWorld == world) return;
            GraphicsSession = ZNet.instance; GraphicsManager = ZDOMan.instance; GraphicsGeneration = MasterIdolLoadIdentity.Generation; GraphicsWorld = world; GraphicsScenes = SeatGraphics = 0; GraphicsEpoch++;
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
        private bool Peace() => Actor != null && Ai != null && MasterIdolResidentGrounding.Slow(Actor) && !MasterIdolSocialSeats.Battle(Ai, Actor) && Ai.m_timeSinceHurt >= 8 && !Ai.IsSleeping() && !Actor.InDodge() && !Actor.InEmote() &&
            GetComponent<MasterIdolSocialSeats>()?.Active != true && GetComponent<MasterIdolWelcome>()?.IsPresenting != true && !MasterIdolResidentWork.Busy(Actor) &&
            GetComponent<MasterIdolResidentPresentation>()?.Playing != true && !MasterIdolResidentSceneAnimation.Playing(Actor)&&!MasterIdolResidentBirthPresentation.Playing(Actor);
        internal static bool Playing(Character actor) => actor?.GetComponent<MasterIdolVisibleGesture>()?.Active == true;
        internal static void Start(Character actor) => Start(actor, true, MasterIdolResidentIdleRules.SlotSeconds);
        internal static void Start(Character actor, bool vibe, float duration)
            => TryStart(actor,vibe,duration);
        internal static bool TryStart(Character actor, bool vibe, float duration, bool reaction=false)
        {
            if (actor == null || MasterIdolResidentRoster.Limit(Utils.GetPrefabName(actor.gameObject)) == 0) return false;
            var scene = actor.GetComponent<MasterIdolVisibleGesture>();
            if (scene == null || !scene.Context() || !scene.View.IsOwner() || !scene.Peace() || !MasterIdolSocialScenes.Active(actor.GetComponent<MasterIdolSocial>()) ||
                !reaction&&!MasterIdolResidentVisibility.Observed(actor) || !MasterIdolResidentPolicy.TryHome(actor, out var zone))
            { Trace(actor,"denied","request-admission",reaction?(byte)2:(byte)0);return false; }
            vibe = vibe && Utils.GetPrefabName(actor.gameObject) == "Greydwarf";
            byte style=reaction?(byte)2:vibe?(byte)1:(byte)0;
            duration = style==2?ReactionSeconds(actor):vibe?MasterIdolResidentIdleRules.VibeSeconds:MasterIdolResidentIdleRules.SlotSeconds;
            if(style==2)
            {
                var animator=actor.GetComponentInChildren<Animator>();
                if(!ReactionClips(actor,animator,animator?.runtimeAnimatorController,out _,out _,out var reason))
                { Trace(actor,"denied",reason,style);return false; }
            }
            scene.BroadcastHome = zone.Id; // Dedicated authority has no local graphics lease but must cancel peer cues.
            var packet = scene.Header(zone.Id, Math.Max(scene.Session.GetTime().Ticks,scene.LastStamp+1), true); packet.Write(style); packet.Write(duration);
            Trace(actor,"request","published-style",style);
            scene.Receive(scene.Owner, new ZPackage(packet.GetArray()));
            if(reaction&&HasGraphics()&&!scene.Active)return false;
            scene.View.InvokeRPC(ZNetView.Everybody, Rpc, packet);
            return true;
        }
        internal static float ReactionSeconds(Character actor)
        {
            if(actor==null)return 0;
            switch(Utils.GetPrefabName(actor.gameObject))
            {case "Greyling":return MasterIdolResidentReactionWaveUpperBody.Seconds;case "Greydwarf":return MasterIdolResidentIdleRules.VibeSeconds;case "Greydwarf_Elite":return MasterIdolResidentReactionWaveUpperBody.Seconds;case "Greydwarf_Shaman":return 2.266667f;default:return 0;}
        }
        private static bool UpperWave(Character actor)
            =>actor!=null&&(Utils.GetPrefabName(actor.gameObject)=="Greyling"||Utils.GetPrefabName(actor.gameObject)=="Greydwarf_Elite");
        private static bool ReactionClips(Character actor,Animator animator,RuntimeAnimatorController original,out AnimationClip idle,out AnimationClip donor,out string reason)
        {
            idle=donor=null;reason="reaction-actor-controller";
            if(actor==null||animator==null||original==null||ReactionSeconds(actor)<=0)return false;
            string family=Utils.GetPrefabName(actor.gameObject);var native=ZNetScene.instance?.GetPrefab(family)?.GetComponentInChildren<Animator>(true);
            // Current four stock controllers are type91 assets. Foreign or transient overrides are never nested.
            if(native==null||original!=native.runtimeAnimatorController||animator.avatar!=native.avatar||animator.applyRootMotion||animator.layerCount!=1)
            {reason="reaction-native-binding-mismatch";return false;}
            if(original is AnimatorOverrideController){reason="reaction-native-override-unsupported";return false;}
            string ownIdle=family=="Greydwarf_Shaman"?"Standing Idle":"Idle";
            string name=UpperWave(actor)?ownIdle:family=="Greydwarf"?"George Vibing":"Standing 1H Cast Spell 01";
            int idleCount=0,donorCount=0;
            foreach(var clip in original.animationClips)
            {if(clip==null)continue;if(clip.name==ownIdle){idle=clip;idleCount++;}if(clip.name==name){donor=clip;donorCount++;}}
            if(idleCount!=1||donorCount!=1||idle.legacy||donor.legacy||idle.events.Length!=0){reason="reaction-clip-object-not-unique";return false;}
            float expected=UpperWave(actor)?4f:family=="Greydwarf"?3.75f:ReactionSeconds(actor);
            if(!float.IsFinite(donor.length)||Mathf.Abs(donor.length-expected)>.05f){reason="reaction-native-length-mismatch";return false;}
            reason="ready";return true;
        }
        private static bool HasGraphics()=>!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
        private bool ReactionBound()
        {
            if(Animator==null||Clip==null||Animator.IsInTransition(0)||Animator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement)return false;
            foreach(var current in Animator.GetCurrentAnimatorClipInfo(0))if(current.clip==Clip&&current.weight>0)return true;
            return false;
        }
        private static void Trace(Character actor,string stage,string reason,byte style)
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(TraceNet!=net||TraceManager!=ZDOMan.instance||TraceGeneration!=MasterIdolLoadIdentity.Generation||TraceWorld!=world)
            {TraceKeys.Clear();TraceNet=net;TraceManager=ZDOMan.instance;TraceGeneration=MasterIdolLoadIdentity.Generation;TraceWorld=world;}
            string family=actor==null?"none":Utils.GetPrefabName(actor.gameObject);if(MasterIdolResidentRoster.Limit(family)==0)family="other";
            byte keyStyle=style<=2?style:(byte)255;
            string key=family+":"+keyStyle+":"+stage+":"+reason;if(TraceKeys.Count>=64||!TraceKeys.Add(key))return;
            try
            {
                var animator=actor?.GetComponentInChildren<Animator>();var controller=animator?.runtimeAnimatorController;var text=new System.Text.StringBuilder();
                text.Append("[GuardAnimation] world=").Append(world).Append(" family=").Append(family).Append(" style=").Append(style).Append(" stage=").Append(stage).Append(" reason=").Append(reason);
                try
                {
                  if(animator!=null)
                  {
                    var state=animator.GetCurrentAnimatorStateInfo(0);
                    text.Append(" state=").Append(unchecked((uint)state.fullPathHash)).Append(" normalized=").Append(state.normalizedTime.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture));
                    text.Append(" speed=").Append(animator.speed.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)).Append(" initialized=").Append(animator.isInitialized).Append(" culling=").Append(animator.cullingMode);
                    text.Append(" controller=").Append(controller==null?"none":controller.name+"#"+controller.GetInstanceID()+"/"+controller.GetType().Name).Append(" clips=");
                    int count=0;foreach(var current in animator.GetCurrentAnimatorClipInfo(0)){if(count++>=4)break;text.Append(current.clip?.name??"none").Append('@').Append(current.weight.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)).Append(';');}
                  }
                }
                catch(Exception){text.Append(" pose=unavailable");}
                MasteryPlugin.Log?.LogInfo(text.ToString());
            }
            catch(Exception){}
        }
        internal static bool SendEmote(Character actor,long target,string home,long stamp,Action<ZPackage> write)
        {
            var scene=actor?.GetComponent<MasterIdolVisibleGesture>();
            if(scene==null||!scene.Context()||!MasterIdolForestGuardState.ModuleBorn(scene.Data)){Trace(actor,"denied","emote-transport-context",2);return false;}
            var packet=scene.Header(home,stamp,false);write(packet);
            Trace(actor,"transport","emote-envelope-send",2);
            if(target==ZNet.GetUID())scene.Receive(target,new ZPackage(packet.GetArray()));
            else scene.View.InvokeRPC(target,Rpc,packet);
            return true;
        }
        internal static long ContextStart(Character actor)
        {var scene=actor?.GetComponent<MasterIdolVisibleGesture>();return scene?.Context()==true?scene.ContextTicks:long.MaxValue;}
        private ZPackage Header(string home, long stamp, bool begin)
        {
            var packet = new ZPackage(); packet.Write(World); MasterIdolResidentWork.WriteUid(packet, Data.m_uid); packet.Write((int)Revision);
            packet.Write(home); packet.Write(stamp); packet.Write(begin); return packet;
        }
        private void Receive(long sender, ZPackage packet)
        {
            bool committing=false;
            try
            {
                if (packet == null || packet.Size() > 384 || !Context()) return;
                long world = packet.ReadLong(); var uid = MasterIdolResidentWork.ReadUid(packet); int revision = packet.ReadInt();
                string home = packet.ReadString(); long stamp = packet.ReadLong(); bool begin = packet.ReadBool();
                if(!begin&&packet.GetPos()<packet.Size())
                {
                    Trace(Actor,"transport","emote-envelope-receive",2);
                    MasterIdolResidentEmote.Receive(Actor,View,sender,world,uid,revision,home,stamp,packet);return;
                }
                byte style=0;float duration=0;if(begin){style=packet.ReadByte();duration=packet.ReadSingle();if(style>2){Trace(Actor,"denied","unknown-style",style);return;}}
                if (sender!=Owner||packet.Size()>256||packet.GetPos() != packet.Size() || world != World || uid != Data.m_uid || revision != Revision || home.Length > 128 ||
                    stamp < LastStamp || begin && stamp == LastStamp || !MasterIdolVisibilityRules.Stamp(stamp, Session.GetTime().Ticks, ContextTicks))
                {if(begin)Trace(Actor,"denied","header-owner-context-stamp",style);return;}
                if (!begin) { if (Home == null || home == Home) { LastStamp = stamp; Stop(); } return; }
                if (!MasterIdolResidentPolicy.TryHome(Actor, out var zone) || zone.Id != home || !zone.Contains(transform.position) ||
                    !float.IsFinite(duration) || duration<=0 || duration != (style==2?ReactionSeconds(Actor):style==1?MasterIdolResidentIdleRules.VibeSeconds:MasterIdolResidentIdleRules.SlotSeconds) ||
                    style==1 && Utils.GetPrefabName(gameObject) != "Greydwarf")
                {Trace(Actor,"denied","home-style-duration",style);return;}
                LastStamp = stamp; // A denied/offscreen cue is never replayed when the viewer returns.
                if (Active || !MasterIdolResidentVisibility.Local(Actor) || !Peace() ||
                    GraphicsCount + MasterIdolResidentSceneAnimation.GraphicsCount + MasterIdolResidentBirthPresentation.GraphicsCount >= MasterIdolResidentIdleRules.MaxGraphics)
                {Trace(Actor,"denied","visibility-peace-or-graphics",style);return;}
                Animator = GetComponentInChildren<Animator>(); Original = Animator?.runtimeAnimatorController;
                if (Original == null || Original is AnimatorOverrideController || !Animator.HasState(0, Movement) || Animator.IsInTransition(0) ||
                    Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Movement)
                {Trace(Actor,"denied","animator-current-controller-state",style);return;}
                AnimationClip idle = null, donor = null;
                string ownIdle = Utils.GetPrefabName(gameObject) == "Greydwarf_Shaman" ? "Standing Idle" : "Idle";
                foreach (var source in Original.animationClips)
                {
                    if (source.name == ownIdle && source.events.Length == 0) idle = source;
                    if (source.name == "George Vibing" && Mathf.Abs(source.length - 3.75f) < .05f && source.events.Length == 0) donor = source;
                }
                if(style==2&&!ReactionClips(Actor,Animator,Original,out idle,out donor,out var clipReason)){Trace(Actor,"denied",clipReason,style);return;}
                if (idle == null || style==1 && donor == null){Trace(Actor,"denied","native-idle-or-vibe-clip",style);return;}
                float elapsed = Mathf.Max(0, (float)((Session.GetTime().Ticks - stamp) / (double)TimeSpan.TicksPerSecond));
                if (elapsed >= duration){Trace(Actor,"denied","cue-expired",style);return;}
                MasterIdolResidentReactionPose groundPose=null;
                if(style==2&&MasterIdolResidentReactionPose.Required(Actor)&&!MasterIdolResidentReactionPose.Capture(Actor,Animator,out groundPose))
                {Trace(Actor,"denied","reaction-planted-pose-unavailable",style);return;}
                committing=true;
                if(style==2&&UpperWave(Actor)&&!MasterIdolResidentReactionWaveUpperBody.Capture(Actor,Animator,out Wave))
                {Trace(Actor,"denied","reaction-upper-wave-unavailable",style);Stop("reaction-upper-wave-unavailable");return;}
                Home = home; End = Time.unscaledTime + duration - elapsed;CueStart=Time.unscaledTime-elapsed;Style=style;
                GroundPose=groundPose;
                if (style!=0)
                {
                    Clip = Instantiate(donor); Clip.name = style==2?"VM_EventlessResidentReaction":"VM_EventlessVisibleGreydwarfGesture"; Clip.hideFlags = HideFlags.DontSave; Clip.events = Array.Empty<AnimationEvent>();
                    Override = new AnimatorOverrideController(Original); Override[idle] = Clip; Animator.runtimeAnimatorController = Override;
                }
                LeaseEpoch = GraphicsEpoch; Lease = Active = true; GraphicsScenes++;
                Hold(); Animator.Play(Movement, 0, elapsed / (style!=0 ? donor.length : idle.length));
                if(style==2)
                {
                    Animator.Update(0);
                    if(!float.IsFinite(Animator.speed)||Animator.speed<=0||!ReactionBound()){Trace(Actor,"denied","reaction-motion-not-effective",style);Stop("reaction-binding-failed");return;}
                    if(GroundPose!=null&&!GroundPose.Stable()){Trace(Actor,"denied","reaction-planted-pose-drop",style);Stop("reaction-planted-pose-drop");return;}
                }
                Trace(Actor,"start","native-motion-effective",style);
                MasterIdolResidentMetrics.Count(style==2?"emote.native-reaction-shown":style==1?"idle.native-vibe-start":"idle.native-idle-start");
            }
            catch (Exception) { if(committing)Stop("start-exception"); MasterIdolResidentMetrics.Count("idle.native-start-failed"); }
        }
        private void Update()
        {
            try
            {
                if (!Context()) { Stop(); return; } if (!Active) return;
                var owned = Override != null ? (RuntimeAnimatorController)Override : Original;
                if(GroundPose!=null&&!GroundPose.Stable()){Stop("reaction-planted-pose-drop");return;}
                if (Time.unscaledTime >= End || !MasterIdolResidentVisibility.Local(Actor) || !Peace() || Animator == null ||
                    !ReferenceEquals(Animator.runtimeAnimatorController, owned) || Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Movement ||
                    Animator.IsInTransition(0) || !MasterIdolResidentPolicy.TryHome(Actor, out var zone) || zone.Id != Home || !zone.Contains(transform.position)) Stop(Time.unscaledTime>=End?"completed":"safety-context");
            }
            catch (Exception) { Stop(); }
        }
        private void Hold()
        {
            var owned = Override != null ? (RuntimeAnimatorController)Override : Original;
            if (Active && Animator != null && ReferenceEquals(Animator.runtimeAnimatorController, owned))
            { Animator.SetFloat("forward_speed", 0); if(Style!=2)Animator.SetFloat("sideway_speed", 0); Animator.SetFloat("turn_speed", 0); }
        }
        private void LateUpdate()
        {
            try
            {
                Hold();
                if(!Active||Wave==null)return;
                var owned=Override!=null?(RuntimeAnimatorController)Override:Original;
                // Native Animator has evaluated its idle first; never resample it every frame.
                if(!Context()||!Active||!Peace()||Animator.runtimeAnimatorController!=owned||!ReactionBound()||
                    !MasterIdolResidentPolicy.TryHome(Actor,out var zone)||zone.Id!=Home||!zone.Contains(transform.position)||
                    !Wave.Tick(Time.unscaledTime-CueStart,owned))Stop("reaction-upper-wave-ended");
            }
            catch(Exception){Stop("late-update-exception");}
        }
        private void Stop(string reason="cleanup")
        {
            if(Active)Trace(Actor,"end",reason,Style);
            Active = false;
            try
            {
                try{Wave?.Dispose();}catch(Exception){/* One cue cleanup cannot retain our controller or lease. */}finally{Wave=null;}
                if (Animator != null && Override != null && ReferenceEquals(Animator.runtimeAnimatorController, Override))
                {
                    bool idlePose=false;
                    try{idlePose=GroundPose!=null&&Actor!=null&&!Actor.IsDead()&&!Actor.InAttack()&&!Animator.IsInTransition(0)&&Animator.GetCurrentAnimatorStateInfo(0).fullPathHash==Movement;}
                    catch(Exception){/* A failed pose query cannot prevent restoring our own controller. */}
                    Animator.runtimeAnimatorController = Original;
                    if(idlePose)Animator.Update(0); // Restore native sampled pose this frame; never move the actor or bones directly.
                }
            }
            finally
            {
                try { if (Override != null) Destroy(Override); }
                finally
                {
                    try { if (Clip != null) Destroy(Clip); }
                    finally { Override = null; Clip = null; Original = null; Home = null; GroundPose=null; if (Lease && LeaseEpoch == GraphicsEpoch) GraphicsScenes = Math.Max(0, GraphicsScenes - 1); Lease = false; }
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
                    if (home != null) scene.View.InvokeRPC(ZNetView.Everybody, Rpc, scene.Header(home, Math.Max(scene.Session.GetTime().Ticks,scene.LastStamp+1), false));
                }
            }
            finally { scene.BroadcastHome = null; scene.Stop(); }
        }
        private void OnDisable() => Stop("disabled"); private void OnDestroy() => Stop("destroyed");
    }
    [HarmonyPatch(typeof(BaseAI), "Awake")]
    internal static class MasterIdolVisibleGestureAttachPatch
    { private static void Postfix(BaseAI __instance) { if (MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject)) > 0 && __instance.GetComponent<MasterIdolVisibleGesture>() == null) __instance.gameObject.AddComponent<MasterIdolVisibleGesture>(); } }
}
