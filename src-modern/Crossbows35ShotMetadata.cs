using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // One mechanic's transient authored-shot sidecar. No native damage hold/replay,
    // no persistence/ACK and no reuse of the gathering XP codec or mutable scopes.
    internal static class Crossbows35ShotMetadata
    {
        internal const string Rpc = "VM_CrossbowCombatShotV2";
        internal const int MaximumEntries = 128, PacketBytes = 76;
        internal const float Lifetime = 8f;
        internal static readonly int RpcHash = Rpc.GetStableHashCode();
        internal static readonly int DamageHash = "RPC_Damage".GetStableHashCode();
        internal struct Envelope : IEquatable<Envelope>
        {
            internal long World, Sender, Message;
            internal ZDOID Target;
            internal int Method;
            public bool Equals(Envelope b) => World == b.World && Sender == b.Sender && Message == b.Message && Target == b.Target && Method == b.Method;
            public override bool Equals(object obj) => obj is Envelope b && Equals(b);
            public override int GetHashCode() => World.GetHashCode() ^ Sender.GetHashCode() ^ Message.GetHashCode() ^ Target.GetHashCode() ^ Method;
        }
        internal sealed class Proof
        {
            internal Envelope Key;
            internal long Owner, Author;
            internal ZDOID Character;
            internal float Level;
        }
        private sealed class Shot
        {
            internal Player Actor;
            internal ZNet Net;
            internal ZNetScene Scene;
            internal long World;
            internal float Level;
        }
        internal sealed class Emission
        {
            internal Proof Author;
            internal Character Target;
            internal HitData Hit;
            internal bool Bound;
        }
        internal sealed class Frame
        {
            internal Envelope Key;
            internal Proof Proof;
            internal Character Target;
            internal HitData Hit;
            internal ZNet Net;
            internal ZNetScene Scene;
            internal bool Used;
        }
        internal struct DamageState { internal bool Entered; internal Emission Previous; }
        internal struct HandleState { internal bool Entered; internal Frame Previous; }
        internal struct IncomingState { internal bool Entered; internal ZRpc Previous; }
        private sealed class Entry { internal Proof Proof; internal float Expires; }
        private static ConditionalWeakTable<Projectile, Shot> Shots = new ConditionalWeakTable<Projectile, Shot>();
        private static readonly Dictionary<Envelope, Entry> Entries = new Dictionary<Envelope, Entry>();
        // Native m_msgID = sender m_id + increasing m_rpcMsgID. Retain only one
        // watermark per authored character in this session, not every old hit.
        private static readonly Dictionary<string, long> Seen = new Dictionary<string, long>();
        private static readonly List<Envelope> Expired = new List<Envelope>();
        private static ZNet Session;
        private static ZNetScene Scene;
        private static long SessionWorld;
        private static ZRoutedRpc Registered;
        private static float CleanupAt;
        [ThreadStatic] internal static Emission Impact, Emitting;
        [ThreadStatic] internal static Frame Current;
        [ThreadStatic] internal static ZRpc Incoming;
        [ThreadStatic] private static int Sending;
        internal static long World => ZNet.instance?.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
        internal static bool Enabled => MasteryPlugin.Settings?.Enabled.Value == true;

        internal static void Ensure()
        {
            long world = World;
            if (!ReferenceEquals(Session, ZNet.instance) || !ReferenceEquals(Scene, ZNetScene.instance) || SessionWorld != world)
            {
                Session = ZNet.instance; Scene = ZNetScene.instance; SessionWorld = world;
                Entries.Clear(); Seen.Clear(); Shots = new ConditionalWeakTable<Projectile, Shot>(); CleanupAt = 0f;
            }
            if (Time.time < CleanupAt) return;
            CleanupAt = Time.time + 1f;
            Expired.Clear();
            foreach (var pair in Entries) if (pair.Value.Expires < Time.time) Expired.Add(pair.Key);
            foreach (Envelope key in Expired) Entries.Remove(key);
            Expired.Clear();
        }
        internal static void Register()
        {
            var routed = ZRoutedRpc.instance;
            if (routed == null || ReferenceEquals(Registered, routed)) return;
            routed.Register<ZPackage>(Rpc, Receive);
            Registered = routed;
        }
        internal static void Capture(Projectile projectile, Character owner, HitData hit, ItemDrop.ItemData item)
        {
            if (projectile == null) return;
            Shots.Remove(projectile);
            Player player = owner as Player;
            if (!Enabled || player == null || player != Player.m_localPlayer || player.IsDead() ||
                player.m_nview?.IsOwner() != true || projectile.m_nview?.IsOwner() != true ||
                !Crossbows35ParryStagger.IsActualCrossbow(item) ||
                hit == null || hit.m_skill != Skills.SkillType.Crossbows || !hit.m_ranged ||
                PerkRuntimeService.IsPerkGenerated(hit) || projectile.m_hitVariant == CrossbowTurretProjectile.Variant ||
                projectile.m_aoe != 0f) return;
            float raw = Crossbows35ParryStagger.LearnedLevel(player);
            if (raw <= 0f) return; // Zero learned level has no bonus, so no sidecar traffic.
            Ensure(); Register();
            if (World == 0 || Session == null || Scene == null || player.GetPlayerID() == 0 || player.GetZDOID() == ZDOID.None) return;
            Shots.Add(projectile, new Shot { Actor = player, Net = Session, Scene = Scene, World = World, Level = raw });
        }
        internal static bool NativeHit(HitData hit) => hit != null && hit.m_ranged && hit.m_skill == Skills.SkillType.Crossbows &&
            hit.GetAttacker() is Player && !PerkRuntimeService.IsPerkGenerated(hit);
        internal static void Deliver(IDestructible receiver, HitData hit, Projectile projectile)
        {
            Emission previous = Impact; Impact = null;
            try
            {
                try
                {
                    Ensure();
                    Character target = receiver as Character;
                    if (Enabled && target != null && !target.IsPlayer() && target.m_nview?.IsValid() == true &&
                        projectile != null && projectile.m_nview?.IsOwner() == true && projectile.m_aoe == 0f &&
                        projectile.m_hitVariant != CrossbowTurretProjectile.Variant && NativeHit(hit) &&
                        Shots.TryGetValue(projectile, out Shot shot) && shot.Actor == hit.GetAttacker() && shot.Actor == Player.m_localPlayer &&
                        shot.Actor.m_nview?.IsOwner() == true && ReferenceEquals(shot.Net, Session) && ReferenceEquals(shot.Scene, Scene) && shot.World == World)
                    {
                        Impact = new Emission { Hit = hit, Target = target, Author = new Proof { Author = shot.Actor.GetPlayerID(), Character = shot.Actor.GetZDOID(), Level = shot.Level } };
                    }
                }
                catch { Impact = null; } // Metadata cannot prevent native impact.
                receiver.Damage(hit); // Exactly one unchanged native call, including native exceptions.
            }
            finally { Impact = previous; }
        }
        internal static void BeginDamage(Character target, HitData hit, out DamageState state)
        {
            state = new DamageState { Entered = true, Previous = Emitting };
            Emitting = Impact != null && ReferenceEquals(Impact.Hit, hit) && Impact.Target == target && NativeHit(hit) ? Impact : null;
        }
        internal static void EndDamage(DamageState state) { if (state.Entered) Emitting = state.Previous; }
        private static Character Target(ZDOID id) => ZNetScene.instance?.FindInstance(id)?.GetComponent<Character>();
        internal static Envelope Key(ZRoutedRpc.RoutedRPCData data) => new Envelope { World = World, Sender = data.m_senderPeerID, Message = data.m_msgID, Target = data.m_targetZDO, Method = data.m_methodHash };
        private static bool ValidKey(Envelope key) => key.World != 0 && key.World == World && key.Sender != 0 && key.Message != 0 && key.Target != ZDOID.None && key.Method == DamageHash;
        private static string SourceKey(long sender, ZDOID character) => sender.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + character;
        private static Proof Bind(ZRoutedRpc.RoutedRPCData data, Character target)
        {
            var emission = Emitting;
            if (emission == null || emission.Bound || target != emission.Target || !ReferenceEquals(Impact, emission) ||
                !Enabled || !NativeHit(emission.Hit) || data.m_senderPeerID != ZNet.GetUID() || !ValidKey(Key(data))) return null;
            emission.Bound = true;
            return new Proof { Key = Key(data), Owner = target.m_nview.GetZDO().GetOwner(), Author = emission.Author.Author, Character = emission.Author.Character, Level = emission.Author.Level };
        }
        internal static bool Allow(ZRoutedRpc.RoutedRPCData data)
        {
            if (data == null || data.m_methodHash != RpcHash) return true;
            if (!Enabled || ZNet.instance == null) return false;
            if (Sending > 0 && data.m_senderPeerID == ZNet.GetUID()) return true;
            if (Incoming == null) return false;
            if (ZNet.instance.IsServer())
            {
                ZNetPeer peer = ZNet.instance.GetPeer(Incoming);
                return peer?.IsReady() == true && peer.m_uid == data.m_senderPeerID;
            }
            return ReferenceEquals(ZNet.instance.GetServerPeer()?.m_rpc, Incoming) && NetworkSync.HasServerSettings;
        }
        internal static bool Route(ZRoutedRpc.RoutedRPCData data)
        {
            if (!Allow(data)) return false; // ONLY our custom hash can be rejected.
            if (!Enabled || data == null || data.m_methodHash != DamageHash || Emitting == null) return true;
            try
            {
                Ensure(); Register();
                Character target = Target(data.m_targetZDO);
                if (target == null || target.IsPlayer() || target.m_nview?.IsValid() != true ||
                    data.m_targetPeerID == ZNet.GetUID() || data.m_targetPeerID != target.m_nview.GetZDO().GetOwner()) return true;
                Proof proof = Bind(data, target);
                if (proof != null)
                {
                    try { Sending++; ZRoutedRpc.instance.InvokeRoutedRPC(proof.Owner, Rpc, Encode(proof)); }
                    finally { Sending--; }
                }
            }
            catch { } // Sidecar failure never blocks/duplicates native damage.
            return true;
        }
        internal static ZPackage Encode(Proof proof)
        {
            var p = new ZPackage(); p.Write(2); p.Write(proof.Key.World); p.Write(proof.Key.Sender); p.Write(proof.Key.Message);
            p.Write(proof.Key.Target); p.Write(proof.Key.Method); p.Write(proof.Owner); p.Write(proof.Author); p.Write(proof.Character); p.Write(proof.Level); return p;
        }
        internal static void Receive(long sender, ZPackage p)
        {
            try
            {
                Ensure();
                if (!Enabled || p == null || p.Size() != PacketBytes || p.ReadInt() != 2 ||
                    !(Sending > 0 && sender == ZNet.GetUID()) && !TrustedSender(sender)) return;
                var proof = new Proof { Key = new Envelope { World = p.ReadLong(), Sender = p.ReadLong(), Message = p.ReadLong(), Target = p.ReadZDOID(), Method = p.ReadInt() }, Owner = p.ReadLong(), Author = p.ReadLong(), Character = p.ReadZDOID(), Level = p.ReadSingle() };
                Character target = Target(proof.Key.Target);
                if (float.IsNaN(proof.Level) || float.IsInfinity(proof.Level) || proof.Level < 0f || proof.Level > 100f ||
                    sender != proof.Key.Sender || !ValidKey(proof.Key) || proof.Author == 0 || proof.Character == ZDOID.None ||
                    proof.Owner != ZNet.GetUID() || target == null || target.IsPlayer() || target.m_nview?.IsOwner() != true ||
                    target.m_nview.GetZDO().GetOwner() != proof.Owner || Entries.ContainsKey(proof.Key) || Entries.Count >= MaximumEntries) return;
                if (Seen.TryGetValue(SourceKey(sender, proof.Character), out long previous) && proof.Key.Message <= previous) return;
                Entries.Add(proof.Key, new Entry { Proof = proof, Expires = Time.time + Lifetime });
            }
            catch { } // Invalid/missing sidecar => no extra, never native retry.
        }
        private static bool TrustedSender(long sender)
        {
            if (Incoming == null || ZNet.instance == null) return false;
            if (ZNet.instance.IsServer()) { ZNetPeer peer = ZNet.instance.GetPeer(Incoming); return peer?.IsReady() == true && peer.m_uid == sender; }
            return ReferenceEquals(ZNet.instance.GetServerPeer()?.m_rpc, Incoming) && NetworkSync.HasServerSettings;
        }
        internal static void BeginHandle(ZRoutedRpc.RoutedRPCData data, out HandleState state)
        {
            state = new HandleState { Entered = true, Previous = Current }; Current = null;
            if (!Enabled || data == null || data.m_methodHash != DamageHash) return;
            try
            {
                Ensure();
                var key = Key(data); Character target = Target(data.m_targetZDO);
                if (!ValidKey(key) || target == null || target.IsPlayer() || target.m_nview?.IsOwner() != true ||
                    data.m_targetPeerID != ZNet.GetUID()) return;
                Proof proof = null;
                if (Entries.TryGetValue(key, out Entry old))
                {
                    if (old.Expires >= Time.time) proof = old.Proof;
                    old.Proof = null; // Tombstone consumes even an unused/non-stagger hit.
                }
                else
                {
                    if (data.m_senderPeerID == ZNet.GetUID()) proof = Bind(data, target); // Native local Handle precedes Route.
                    if (Entries.Count >= MaximumEntries) proof = null;
                    else Entries.Add(key, new Entry { Expires = Time.time + Lifetime }); // Late metadata cannot authorize a replay.
                }
                if (proof != null && (proof.Owner != ZNet.GetUID() || target.m_nview.GetZDO().GetOwner() != proof.Owner)) proof = null;
                Current = new Frame { Key = key, Proof = proof, Target = target, Net = Session, Scene = Scene };
            }
            catch { Current = null; }
        }
        internal static void EndHandle(HandleState state) { if (state.Entered) Current = state.Previous; }
        internal static void BeginRpc(Character target, long sender, HitData hit, out HandleState state)
        {
            state = new HandleState { Entered = true, Previous = Current };
            Frame frame = Current;
            if (frame == null || frame.Target != target || sender != frame.Key.Sender ||
                (frame.Hit != null && !ReferenceEquals(frame.Hit, hit))) { Current = null; return; }
            frame.Hit = hit;
            Player author = hit?.GetAttacker() as Player;
            ZDOID character = frame.Proof != null ? frame.Proof.Character :
                NativeHit(hit) && author?.m_nview?.IsValid() == true && author.m_nview.GetZDO().GetOwner() == sender ? author.GetZDOID() : ZDOID.None;
            if (character != ZDOID.None)
            {
                string source = SourceKey(sender, character);
                if (Seen.TryGetValue(source, out long previous))
                {
                    if (frame.Key.Message <= previous) frame.Proof = null;
                    else Seen[source] = frame.Key.Message;
                }
                else if (Seen.Count < MaximumEntries) Seen.Add(source, frame.Key.Message);
                else frame.Proof = null; // Bounded state, native hit is never blocked.
            }
        }
        internal static bool TryConsume(Character target, HitData hit, out float level)
        {
            level = 0f;
            Frame frame = Current;
            if (!Enabled || frame == null || frame.Proof == null || frame.Used) return false;
            frame.Used = true;
            Player author = hit?.GetAttacker() as Player;
            bool valid = NativeHit(hit) && ReferenceEquals(frame.Hit, hit) && ReferenceEquals(frame.Net, ZNet.instance) &&
                ReferenceEquals(frame.Scene, ZNetScene.instance) && frame.Target == target && target.m_nview?.IsOwner() == true &&
                target.GetZDOID() == frame.Proof.Key.Target && frame.Proof.Key.World == World &&
                target.m_nview.GetZDO().GetOwner() == frame.Proof.Owner &&
                author != null && author.m_nview?.IsValid() == true && author.m_nview.GetZDO().GetOwner() == frame.Proof.Key.Sender &&
                author.GetPlayerID() == frame.Proof.Author && author.GetZDOID() == frame.Proof.Character;
            if (valid) level = frame.Proof.Level;
            return valid;
        }
    }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class Crossbows35ShotSetupPatch
    {
        private static void Postfix(Projectile __instance, Character __0, HitData __3, ItemDrop.ItemData __4)
        { try { Crossbows35ShotMetadata.Capture(__instance, __0, __3, __4); } catch { } }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Crossbows35EmissionPatch
    {
        private static void Prefix(Character __instance, HitData hit, out Crossbows35ShotMetadata.DamageState __state)
            => Crossbows35ShotMetadata.BeginDamage(__instance, hit, out __state);
        private static Exception Finalizer(Crossbows35ShotMetadata.DamageState __state, Exception __exception)
        { Crossbows35ShotMetadata.EndDamage(__state); return __exception; }
    }
    [HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
    internal static class Crossbows35IncomingPatch
    {
        private static void Prefix(ZRpc __0, out Crossbows35ShotMetadata.IncomingState __state)
        { __state = new Crossbows35ShotMetadata.IncomingState { Entered = true, Previous = Crossbows35ShotMetadata.Incoming }; Crossbows35ShotMetadata.Incoming = __0; }
        private static Exception Finalizer(Crossbows35ShotMetadata.IncomingState __state, Exception __exception)
        { if (__state.Entered) Crossbows35ShotMetadata.Incoming = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Crossbows35RpcHitScopePatch
    {
        private static void Prefix(Character __instance, long __0, HitData __1, out Crossbows35ShotMetadata.HandleState __state)
            => Crossbows35ShotMetadata.BeginRpc(__instance, __0, __1, out __state);
        private static Exception Finalizer(Crossbows35ShotMetadata.HandleState __state, Exception __exception)
        { Crossbows35ShotMetadata.EndHandle(__state); return __exception; }
    }
    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    internal static class Crossbows35HandlePatch
    {
        [HarmonyPriority(Priority.Low)] // AFTER XP snapshot; its default-state finalizer has no Entered guard.
        internal static bool Prefix(ZRoutedRpc.RoutedRPCData __0, out Crossbows35ShotMetadata.HandleState __state)
        { Crossbows35ShotMetadata.BeginHandle(__0, out __state); return Crossbows35ShotMetadata.Allow(__0); }
        internal static Exception Finalizer(Crossbows35ShotMetadata.HandleState __state, Exception __exception)
        { Crossbows35ShotMetadata.EndHandle(__state); return __exception; }
    }
    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    internal static class Crossbows35RoutePatch
    {
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(ZRoutedRpc.RoutedRPCData __0) => Crossbows35ShotMetadata.Route(__0);
    }
    [HarmonyPatch(typeof(NetworkSync), nameof(NetworkSync.Register))]
    internal static class Crossbows35MetadataRegistrationPatch
    { private static void Postfix() => Crossbows35ShotMetadata.Register(); }
}
