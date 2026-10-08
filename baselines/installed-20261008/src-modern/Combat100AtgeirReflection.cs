#if MASTERY_SHIELD35_EXPERIMENT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Projectile owner holds one real shot. Defender owner only confirms its actual
    // window; it never spawns a second return. Advertised ZDO state is a hint only.
    internal static class Combat100AtgeirReflection
    {
        private const string RequestRpc = "VM_C100_ReflectRequest", AckRpc = "VM_C100_ReflectAck";
        private static readonly int ActiveKey = "VM_C100_AtgeirActive".GetStableHashCode();
        private static readonly int EpochKey = "VM_C100_AtgeirEpoch".GetStableHashCode();
        private static readonly int RadiusKey = "VM_C100_AtgeirRadius".GetStableHashCode();
        private const double HoldSeconds = .150d;
        private const int MaxPending = 64;
        private sealed class Registration { internal ZNetView View; }
        private sealed class Window { internal Attack Attack; internal long Epoch; internal float Radius; }
        private sealed class Attempt { internal readonly HashSet<string> Used = new HashSet<string>(); internal bool Expired; }
        private sealed class Hold
        {
            internal Projectile Shot;
            internal Player Defender;
            internal ZDOID ShotId, DefenderId, SourceId;
            internal long Nonce, Epoch, ProjectileOwner, DefenderOwner;
            internal Vector3 Contact, Velocity;
            internal float RayRadius;
            internal double Deadline;
            internal float HeldGameSeconds;
            internal bool Accepted, Rejected, Finished, Consumed, TimedTtl;
        }
        private static readonly ConditionalWeakTable<Player, Registration> Registered = new ConditionalWeakTable<Player, Registration>();
        private static ConditionalWeakTable<Player, Window> Windows = new ConditionalWeakTable<Player, Window>();
        private static ConditionalWeakTable<Projectile, Attempt> Attempted = new ConditionalWeakTable<Projectile, Attempt>();
        private static readonly Dictionary<ZDOID, Hold> Pending = new Dictionary<ZDOID, Hold>();
        private static ZNet Session;
        private static long NextNonce;
        private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        private static bool Owned(Projectile p) => p != null && p.m_nview?.IsValid() == true && p.m_nview.IsOwner();
        private static bool Eligible(Player p) => p != null && p == Player.m_localPlayer && p.m_nview?.IsOwner() == true &&
            MasteryPlugin.Settings?.Enabled.Value == true && !p.IsDead() && !p.IsTeleporting() && !p.InCutscene() && !Game.IsPaused() &&
            PerkRuntimeService.HasPerk(p, Skills.SkillType.Polearms, 100);

        internal static void Register(Player player)
        {
            if (player?.m_nview?.IsValid() != true) return;
            Registration state = Registered.GetOrCreateValue(player);
            if (state.View == player.m_nview) return;
            player.m_nview.Register<ZPackage>(RequestRpc, (sender, packet) => ReceiveRequest(player, sender, packet));
            player.m_nview.Register<ZPackage>(AckRpc, (sender, packet) => ReceiveAck(player, sender, packet));
            state.View = player.m_nview;
        }
        internal static void Publish(Player p)
        {
            CheckSession();
            Register(p);
            if (p?.m_nview?.IsOwner() != true) return;
            ZDO data = p.m_nview.GetZDO();
            Window state = Windows.GetOrCreateValue(p);
            bool active = Eligible(p) && Polearms70ContinuousSpinService.TryGetConfirmedSpin(p, out var attack, out var radius);
            if (!active)
            {
                state.Attack = null;
                data.Set(ActiveKey, false);
                return;
            }
            // Re-read after short-circuit eligibility for definite assignment.
            if (!Polearms70ContinuousSpinService.TryGetConfirmedSpin(p, out attack, out radius)) return;
            if (state.Attack != attack)
            {
                long previous = data.GetLong(EpochKey, 0L);
                if (previous == long.MaxValue) { data.Set(ActiveKey, false); return; }
                state.Epoch = Math.Max(0L, previous) + 1;
                state.Attack = attack;
                data.Set(EpochKey, state.Epoch);
            }
            state.Radius = radius;
            data.Set(RadiusKey, radius);
            data.Set(ActiveKey, true);
        }
        private static bool CurrentWindow(Player p, long epoch, out float radius)
        {
            radius = 0f;
            return Eligible(p) && Windows.TryGetValue(p, out Window window) && window.Epoch == epoch &&
                Polearms70ContinuousSpinService.TryGetConfirmedSpin(p, out Attack attack, out radius) && attack == window.Attack;
        }
        private static void CheckSession()
        {
            if (ReferenceEquals(Session, ZNet.instance)) return;
            foreach (Hold old in new List<Hold>(Pending.Values))
            { old.Accepted = false; old.Rejected = true; ServiceHold(old); }
            Pending.Clear();
            Windows = new ConditionalWeakTable<Player, Window>();
            Attempted = new ConditionalWeakTable<Projectile, Attempt>();
            Session = ZNet.instance;
        }
        internal static bool BeforeFlight(Projectile shot)
        {
            CheckSession();
            if (!Owned(shot)) return true;
            ZDO data = shot.m_nview.GetZDO();
            if (Pending.TryGetValue(data.m_uid, out Hold held) && !ServiceHold(held)) return false;
            Attempt history = Attempted.GetOrCreateValue(shot);
            if (Session == null || MasteryPlugin.Settings?.Enabled.Value != true || history.Expired || history.Used.Count >= 4 ||
                Pending.Count >= MaxPending || shot.m_didHit || shot.m_hitMidFlight || shot.m_originalHitData == null ||
                shot.m_hitVariant == Blocking35ProjectileService.ReflectedVariant ||
                PerkRuntimeService.IsPerkGenerated(shot.m_originalHitData) || !Finite(shot.m_vel) ||
                shot.m_vel.sqrMagnitude < .01f || shot.m_vel.sqrMagnitude > 90000f ||
                !float.IsFinite(shot.m_rayRadius) || shot.m_rayRadius < 0f || shot.m_rayRadius > 4f ||
                shot.m_owner == null || shot.m_owner.IsPlayer() || shot.m_owner.IsTamed()) return true;
            float dt = Time.fixedDeltaTime;
            Vector3 position = shot.transform.position;
            Vector3 velocity = shot.m_vel + Vector3.down * shot.m_gravity * dt;
            velocity -= velocity * velocity.magnitude * shot.m_drag * dt;
            Vector3 end = position + velocity * dt;
            Vector3 start = shot.m_haveStartPoint ? shot.m_startPoint : position;
            if (!Finite(start) || !Finite(end) || !Finite(velocity) ||
                (shot.m_canHitWater && end.y < Floating.GetLiquidLevel(end, 1f, LiquidType.All))) return true;
            if (!shot.m_haveStartPoint)
            { Vector3 step = end - position; start = position - .5f * step; end = position + .5f * step; }
            Player chosen = null;
            Vector3 contact = default;
            float fraction = float.MaxValue;
            long epoch = 0;
            foreach (Character character in Character.GetAllCharacters())
            {
                Player defender = character as Player;
                ZDO target = defender?.m_nview?.GetZDO();
                if (defender == null || defender.IsDead() || target == null || !target.GetBool(ActiveKey, false) ||
                    !BaseAI.IsEnemy(defender, shot.m_owner) ||
                    history.Used.Contains(target.m_uid.ToString() + ":" + target.GetLong(EpochKey, 0L))) continue;
                float radius = target.GetFloat(RadiusKey, 0f);
                if (!float.IsFinite(radius) || radius <= 0f || radius > 32f) continue;
                if (!IncomingRing(start, end, defender.GetCenterPoint(), radius + Mathf.Max(0f, shot.m_rayRadius),
                    out float candidateFraction, out Vector3 candidateContact, 1.25f + shot.m_rayRadius) ||
                    Mathf.Abs(candidateContact.y - defender.GetCenterPoint().y) > 1.25f + shot.m_rayRadius) continue;
                if (candidateFraction < fraction || (candidateFraction == fraction &&
                    (chosen == null || defender.GetPlayerID() < chosen.GetPlayerID())))
                { chosen = defender; fraction = candidateFraction; contact = candidateContact; epoch = target.GetLong(EpochKey, 0L); }
            }
            if (chosen == null || epoch <= 0 || Obstructed(shot, start, contact)) return true;
            ZDO defenderData = chosen.m_nview.GetZDO();
            ZDO sourceData = shot.m_owner.m_nview?.GetZDO();
            if (sourceData == null || sourceData.GetOwner() != data.GetOwner() || NextNonce == long.MaxValue) return true;
            var hold = new Hold { Shot = shot, Defender = chosen, ShotId = data.m_uid,
                DefenderId = defenderData.m_uid, SourceId = sourceData.m_uid, Nonce = ++NextNonce,
                Epoch = epoch, ProjectileOwner = data.GetOwner(), DefenderOwner = defenderData.GetOwner(),
                Contact = contact, Velocity = velocity, RayRadius = shot.m_rayRadius,
                Deadline = Now + HoldSeconds, TimedTtl = shot.m_ttl > 0f };
            history.Used.Add(hold.DefenderId.ToString() + ":" + hold.Epoch);
            Pending.Add(data.m_uid, hold);
            shot.transform.position = contact;
            shot.m_haveStartPoint = false;
            shot.m_startPoint = contact;
            shot.m_vel = velocity;
            var request = new ZPackage();
            request.Write(hold.ShotId); request.Write(hold.SourceId); request.Write(hold.Nonce); request.Write(hold.Epoch);
            request.Write(hold.Contact); request.Write(hold.Velocity); request.Write(hold.RayRadius);
            request.Write((int)shot.m_hitVariant); request.Write(shot.m_hitMidFlight);
            request.Write(PerkRuntimeService.IsPerkGenerated(shot.m_originalHitData));
            if (chosen.m_nview.IsOwner()) { request.SetPos(0); ReceiveRequest(chosen, hold.ProjectileOwner, request); }
            else chosen.m_nview.InvokeRPC(RequestRpc, request);
            bool continueNative = ServiceHold(hold);
            return continueNative && !history.Expired && history.Used.Count < 4 ? BeforeFlight(shot) : continueNative;
        }
        internal static bool IncomingRing(Vector3 start, Vector3 end, Vector3 center, float radius,
            out float fraction, out Vector3 contact, float halfHeight = 1.25f)
        {
            fraction = 0f; contact = default;
            Vector3 from = start - center, delta = end - start;
            double a = (double)delta.x * delta.x + (double)delta.z * delta.z;
            double b = 2d * ((double)from.x * delta.x + (double)from.z * delta.z);
            double c = (double)from.x * from.x + (double)from.z * from.z - (double)radius * radius;
            if (a <= 1e-12d || c < -1e-5d || b >= 0d) return false; // no inside/outgoing capture
            double discriminant = b * b - 4d * a * c;
            if (discriminant <= 1e-12d) return false; // tangent is not entering
            double t = (-b - Math.Sqrt(discriminant)) / (2d * a);
            if (t < -1e-6d || t > 1d) return false;
            t = Math.Max(0d, t);
            fraction = (float)t; contact = start + delta * fraction;
            return Math.Abs(contact.y - center.y) <= halfHeight;
        }
        private static bool Obstructed(Projectile shot, Vector3 start, Vector3 contact)
        {
            Vector3 delta = contact - start; float distance = delta.magnitude;
            if (distance < .001f) return false;
            RaycastHit[] hits = shot.m_rayRadius > 0f
                ? Physics.SphereCastAll(start, shot.m_rayRadius, delta / distance, distance, Projectile.s_rayMaskSolids)
                : Physics.RaycastAll(start, delta / distance, distance, Projectile.s_rayMaskSolids);
            foreach (RaycastHit hit in hits)
                if (hit.collider != null && hit.collider.GetComponentInParent<Projectile>() != shot && hit.distance < distance - .001f)
                    return true;
            return false;
        }        private static void ReceiveRequest(Player defender, long sender, ZPackage packet)
        {
            if (!Eligible(defender) || packet == null || packet.Size() > 128) return;
            try
            {
                ZDOID shotId = packet.ReadZDOID(), sourceId = packet.ReadZDOID();
                long nonce = packet.ReadLong(), epoch = packet.ReadLong();
                Vector3 contact = packet.ReadVector3(), velocity = packet.ReadVector3();
                float rayRadius = packet.ReadSingle();
                int variant = packet.ReadInt(); bool midFlight = packet.ReadBool(), generated = packet.ReadBool();
                ZDO shot = ZDOMan.instance?.GetZDO(shotId), source = ZDOMan.instance?.GetZDO(sourceId);
                Character attacker = ZNetScene.instance?.FindInstance(sourceId)?.GetComponent<Character>();
                bool accepted = nonce > 0 && epoch > 0 && !midFlight && !generated &&
                    variant != Blocking35ProjectileService.ReflectedVariant && shot != null && source != null &&
                    shot.GetOwner() == sender && source.GetOwner() == sender &&
                    attacker != null && !attacker.IsPlayer() && !attacker.IsTamed() && BaseAI.IsEnemy(defender, attacker) &&
                    ZNetScene.instance.GetPrefab(shot.GetPrefab())?.GetComponent<Projectile>() != null &&
                    Finite(contact) && Finite(velocity) && float.IsFinite(rayRadius) && rayRadius >= 0f && rayRadius <= 4f &&
                    velocity.sqrMagnitude > .01f && velocity.sqrMagnitude <= 90000f &&
                    CurrentWindow(defender, epoch, out float radius) &&
                    Mathf.Abs(contact.y - defender.GetCenterPoint().y) <= 1.25f + rayRadius + .25f &&
                    Mathf.Abs(new Vector2(contact.x - defender.GetCenterPoint().x, contact.z - defender.GetCenterPoint().z).magnitude - (radius + rayRadius)) <= .75f;
                var ack = new ZPackage(); ack.Write(shotId); ack.Write(nonce); ack.Write(epoch); ack.Write(accepted);
                if (sender == ZNet.GetUID()) { ack.SetPos(0); ReceiveAck(defender, ZNet.GetUID(), ack); }
                else defender.m_nview.InvokeRPC(sender, AckRpc, ack);
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Atgeir100] Invalid reflection request: " + error.GetType().Name); }
        }
        private static void ReceiveAck(Player defender, long sender, ZPackage packet)
        {
            if (packet == null || packet.Size() > 64) return;
            try
            {
                ZDOID shotId = packet.ReadZDOID(); long nonce = packet.ReadLong(), epoch = packet.ReadLong();
                bool accepted = packet.ReadBool();
                if (!Pending.TryGetValue(shotId, out Hold hold) || hold.Accepted || hold.Rejected ||
                    hold.Nonce != nonce || hold.Epoch != epoch || hold.Defender != defender ||
                    hold.DefenderId != defender.m_nview?.GetZDO()?.m_uid || sender != hold.DefenderOwner ||
                    defender.m_nview.GetZDO().GetOwner() != sender || Now >= hold.Deadline || !Owned(hold.Shot) || hold.Shot.m_didHit ||
                    hold.Shot.m_nview.GetZDO().GetOwner() != hold.ProjectileOwner ||
                    hold.Shot.m_nview.GetZDO().m_uid != hold.ShotId ||
                    hold.Shot.m_owner?.GetZDOID() != hold.SourceId ||
                    hold.Shot.m_hitVariant == Blocking35ProjectileService.ReflectedVariant ||
                    hold.Shot.m_originalHitData == null || PerkRuntimeService.IsPerkGenerated(hold.Shot.m_originalHitData)) return;
                hold.Accepted = accepted; hold.Rejected = !accepted;
                ServiceHold(hold); // honor a timely ACK immediately; do not await a later physics deadline
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Atgeir100] Invalid reflection ACK: " + error.GetType().Name); }
        }
        private static bool ServiceHold(Hold hold)
        {
            if (hold.Finished) return !hold.Consumed;
            Projectile shot = hold.Shot;
            if (!Owned(shot) || shot.m_nview.GetZDO().m_uid != hold.ShotId ||
                shot.m_nview.GetZDO().GetOwner() != hold.ProjectileOwner)
            { Pending.Remove(hold.ShotId); hold.Finished = true; return true; }
            if (shot.m_didHit)
            { Pending.Remove(hold.ShotId); hold.Finished = true; return true; }
            bool finish = hold.Accepted || hold.Rejected || Now >= hold.Deadline ||
                MasteryPlugin.Settings?.Enabled.Value != true || hold.Defender == null || hold.Defender.IsDead() ||
                hold.Defender.m_nview?.GetZDO()?.GetOwner() != hold.DefenderOwner;
            if (!finish) { hold.HeldGameSeconds += Time.fixedDeltaTime; return false; }
            Pending.Remove(hold.ShotId);
            hold.Finished = true; // consume/continuation decision exactly once, including inline ACK
            bool expired = false;
            if (hold.TimedTtl)
            {
                float remaining = shot.m_ttl - hold.HeldGameSeconds;
                expired = remaining <= 0f;
                // Native only enters its expiry block when TTL is positive. A tiny
                // positive budget lets native perform its one expiry/spawn/delete;
                // assigning zero/negative would accidentally turn it into unlimited flight.
                shot.m_ttl = expired ? float.Epsilon : remaining;
            }
            if (expired)
            { shot.m_didHit = true; Attempted.GetOrCreateValue(shot).Expired = true; }
            if (expired || !hold.Accepted) return true;
            hold.Consumed = Return(hold);
            return !hold.Consumed;
        }
        private static bool Return(Hold hold)
        {
            Projectile original = hold.Shot;
            if (!Owned(original) || original.m_didHit || hold.Defender == null || hold.Defender.IsDead() ||
                hold.Defender.IsTeleporting() || hold.Defender.InCutscene() ||
                hold.Defender.m_nview?.GetZDO()?.GetOwner() != hold.DefenderOwner ||
                original.m_owner?.GetZDOID() != hold.SourceId || original.m_originalHitData == null ||
                PerkRuntimeService.IsPerkGenerated(original.m_originalHitData)) return false;
            Character source = original.m_owner;
            Vector3 direction = source != null && !source.IsDead()
                ? (source.GetCenterPoint() - hold.Contact).normalized : -hold.Velocity.normalized;
            if (!Finite(direction) || direction.sqrMagnitude < .01f) return false;
            GameObject prefab = ZNetScene.instance?.GetPrefab(original.m_nview.GetZDO().GetPrefab());
            if (prefab?.GetComponent<Projectile>() == null) return false;
            Projectile reflected = null;
            try
            {
                reflected = UnityEngine.Object.Instantiate(prefab, hold.Contact + direction * .05f,
                    Quaternion.LookRotation(direction)).GetComponent<Projectile>();
                HitData payload = original.m_originalHitData.Clone();
                payload.SetAttacker(hold.Defender); payload.m_variant = Blocking35ProjectileService.ReflectedVariant;
                payload.m_skill = Skills.SkillType.Polearms; payload.m_skillRaiseAmount = 0f;
                reflected.m_respawnItemOnHit = false; reflected.m_spawnItem = null;
                reflected.m_aoe = 0f; reflected.m_hitMidFlight = false;
                reflected.m_spawnOnHit = null; reflected.m_randomSpawnOnHit = new List<GameObject>();
                reflected.m_onHit = null;
                reflected.m_onlySpawnedProjectilesDealDamage = false; reflected.m_spawnOnTtl = false;
                reflected.Setup(hold.Defender, direction * Mathf.Max(1f, hold.Velocity.magnitude) * 1.5f,
                    -1f, payload, null, null);
                reflected.m_hitVariant = Blocking35ProjectileService.ReflectedVariant;
                reflected.m_raiseSkillAmount = 0f; reflected.m_adrenaline = 0f;
                reflected.m_healthReturn = 0f; reflected.m_eitrAdd = 0f;
                reflected.m_ttl = hold.TimedTtl ?
                    (reflected.m_ttl > 0f ? Mathf.Min(reflected.m_ttl, original.m_ttl) : original.m_ttl) : 0f;
                // The old shot cannot produce another direct/TTL payload even if deletion fails.
                original.m_didHit = true; original.m_spawnOnTtl = false;
                original.m_respawnItemOnHit = false;
                try { ZNetScene.instance.Destroy(original.gameObject); }
                catch (Exception error) { MasteryPlugin.Log.LogWarning("[Atgeir100] Spent projectile cleanup: " + error.Message); }
                return true;
            }
            catch (Exception error)
            {
                if (reflected != null)
                {
                    reflected.m_didHit = true; reflected.m_spawnOnTtl = false; reflected.m_respawnItemOnHit = false;
                    try { ZNetScene.instance?.Destroy(reflected.gameObject); }
                    catch (Exception cleanup) { MasteryPlugin.Log.LogWarning("[Atgeir100] Failed staging cleanup: " + cleanup.Message); }
                }
                MasteryPlugin.Log.LogError("[Atgeir100] Return staging failed: " + error.Message);
                return false;
            }
        }
        internal static void CancelAfterException(Projectile shot)
        {
            ZDO data = shot?.m_nview?.GetZDO();
            if (data != null && Pending.TryGetValue(data.m_uid, out Hold hold))
            { hold.Accepted = false; hold.Rejected = true; ServiceHold(hold); }
        }
        internal static void Hydrate(HitData hit)
        {
            if (hit?.m_variant != Blocking35ProjectileService.ReflectedVariant || hit.m_skill != Skills.SkillType.Polearms) return;
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            context.IsPerkGenerated = true; context.PerkId = "polearms_100_reflection";
            context.SourceSkill = Skills.SkillType.Polearms;
            context.SourcePlayerId = (hit.GetAttacker() as Player)?.GetPlayerID() ?? 0;
            context.GenerationDepth = Math.Max(1, context.GenerationDepth);
            context.AllowSelfProc = context.AllowOtherPerkProc = context.AllowKnife70InitialProc = false;
            context.AllowShadowRecursion = false;
            context.IgnoreReflect = context.IgnoreExecution = context.IgnoreOverdrawPayload = true;
            context.XpMultiplier = 0f;
        }
        internal static void Sweep()
        {
            CheckSession();
            if (Pending.Count == 0) return;
            var holds = new List<Hold>(Pending.Values);
            foreach (Hold hold in holds)
                if (!Owned(hold.Shot) || Now >= hold.Deadline || hold.Rejected || hold.Accepted)
                {
                    ServiceHold(hold); // deadline and timely ACK are also serviced on render frames
                }
        }
    }
    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class Combat100ReflectionRegistrationPatch
    { private static void Postfix(Player __instance) => Combat100AtgeirReflection.Register(__instance); }
    [HarmonyPatch(typeof(Character), "CustomFixedUpdate")]
    internal static class Combat100ReflectionWindowPatch
    { private static void Postfix(Character __instance) { if (__instance is Player p) Combat100AtgeirReflection.Publish(p); } }
    [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
    [HarmonyPriority(Priority.First)]
    internal static class Combat100ReflectionFlightPatch
    {
        private static bool Prefix(Projectile __instance)
        {
            try { return Combat100AtgeirReflection.BeforeFlight(__instance); }
            catch (Exception error)
            { Combat100AtgeirReflection.CancelAfterException(__instance);
                MasteryPlugin.Log.LogError("[Atgeir100] Flight adapter failed: " + error.Message); return true; }
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class Combat100ReflectionDamageContextPatch
    { private static void Prefix(HitData hit) => Combat100AtgeirReflection.Hydrate(hit); }
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    [HarmonyPriority(Priority.First)]
    internal static class Combat100ReflectionRpcContextPatch
    { private static void Prefix(HitData hit) => Combat100AtgeirReflection.Hydrate(hit); }
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.First)]
    internal static class Combat100ReflectionResolvedContextPatch
    { private static void Prefix(HitData hit) => Combat100AtgeirReflection.Hydrate(hit); }
    [HarmonyPatch(typeof(MasteryPlugin), "Update")]
    internal static class Combat100ReflectionSweepPatch
    { private static void Postfix() => Combat100AtgeirReflection.Sweep(); }
}
#endif