using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class Ashlands70State
    {
        internal Ashlands70Grant Grant;
        internal int Remaining = Ashlands70Rules.Shards;
        internal bool Ended;
        internal readonly Dictionary<ZDOID, long> NextHit = new Dictionary<ZDOID, long>();
    }
    internal static class Ashlands70World
    {
        internal const string OrbitCountKey = "vm.ash70.orbitcount", OrbitUntilKey = "vm.ash70.orbituntil", OrbitStoppedKey = "vm.ash70.orbitstopped";
        private static readonly Dictionary<string, Ashlands70State> States = new Dictionary<string, Ashlands70State>();
        private static readonly List<string> Remove = new List<string>();
        private static float NextTick;
        internal static IEnumerable<Ashlands70State> ActiveStates => States.Values;
        private static string Key(ZDOID caster, Ashlands70Kind kind) => caster + ":" + kind;
        internal static Character Resolve(ZDOID id) => ZNetScene.instance?.FindInstance(id)?.GetComponent<Character>();
        internal static void Accept(Ashlands70Grant grant)
        {
            if (grant.World != Ashlands70Cast.CurrentWorld || !Ashlands70Rules.Active(Ashlands70Cast.Now, grant.Until) ||
                grant.Kind < Ashlands70Kind.Madness || grant.Kind > Ashlands70Kind.Orbit) return;
            string key = Key(grant.Caster, grant.Kind);
            if (States.TryGetValue(key, out Ashlands70State previous) &&
                (previous.Grant.Token == grant.Token || previous.Grant.Started >= grant.Started)) return;
            var state = new Ashlands70State { Grant = grant }; States[key] = state;
            if (grant.Kind == Ashlands70Kind.Madness) Ashlands70Madness.Apply(grant);
            if (grant.Kind == Ashlands70Kind.Orbit && Resolve(grant.Caster) is Player caster && caster.m_nview.IsOwner())
            {
                ZDO data = caster.m_nview.GetZDO();
                state.Ended = grant.Started <= data.GetLong(OrbitStoppedKey, 0);
                if (state.Ended) state.Remaining = 0;
                else if (data.GetLong(OrbitUntilKey, 0) == grant.Until) state.Remaining = Mathf.Clamp(data.GetInt(OrbitCountKey, 0), 0, Ashlands70Rules.Shards);
                else { data.Set(OrbitCountKey, Ashlands70Rules.Shards); data.Set(OrbitUntilKey, grant.Until); }
            }
        }
        internal static bool CanRefresh(ZDOID caster, long author, Ashlands70Kind kind, out long previousStarted)
        {
            previousStarted = 0;
            if ((kind != Ashlands70Kind.Grove && kind != Ashlands70Kind.Madness) || author == 0 ||
                !States.TryGetValue(Key(caster, kind), out Ashlands70State state)) return false;
            Ashlands70Grant grant = state.Grant;
            if (state.Ended || grant.World != Ashlands70Cast.CurrentWorld || grant.Author != author || grant.Caster != caster ||
                !Ashlands70Rules.Active(Ashlands70Cast.Now, grant.Until)) return false;
            previousStarted = grant.Started; return true;
        }
        internal static Ashlands70State Orbit(Player player)
        {
            if (player == null || player.IsDead() || !MasteryPlugin.Settings.Enabled.Value || player.m_nview?.IsValid() != true) return null;
            return States.TryGetValue(Key(player.GetZDOID(), Ashlands70Kind.Orbit), out Ashlands70State state) &&
                !state.Ended && Ashlands70Rules.Active(Ashlands70Cast.Now, state.Grant.Until) ? state : null;
        }
        internal static void Tick()
        {
            Ashlands70Presentation.Tick();
            if (Time.unscaledTime < NextTick) return; NextTick = Time.unscaledTime + .1f;
            long now = Ashlands70Cast.Now;
            Remove.Clear(); foreach (var row in States) if (!Ashlands70Rules.Active(now, row.Value.Grant.Until)) Remove.Add(row.Key);
            foreach (string key in Remove) States.Remove(key);
            Ashlands70Madness.Tick();
            if (!MasteryPlugin.Settings.Enabled.Value) return;
            foreach (Ashlands70State state in States.Values)
            {
                Ashlands70Grant grant = state.Grant;
                if (grant.Kind == Ashlands70Kind.Orbit && !state.Ended && Resolve(grant.Caster) is Player dead && dead.IsDead() && dead.m_nview?.IsOwner() == true)
                {
                    state.Ended = true; state.Remaining = 0; ZDO data = dead.m_nview.GetZDO();
                    data.Set(OrbitCountKey, 0); data.Set(OrbitUntilKey, 0L); data.Set(OrbitStoppedKey, now);
                }
                if (grant.Kind != Ashlands70Kind.Chain) continue;
                Character caster = Resolve(grant.Caster);
                foreach (Character target in Character.GetAllCharacters())
                {
                    if (!VictimOwner(target) || target.GetZDOID() == grant.Caster ||
                        (target.GetCenterPoint() - grant.Origin).sqrMagnitude > 18f * 18f) continue;
                    ZDOID id = target.GetZDOID();
                    if (state.NextHit.TryGetValue(id, out long next) && now < next) continue;
                    Vector3 point = Nearest(grant.Origin, grant.End, target.GetCenterPoint());
                    Collider collider = target.GetComponent<Collider>();
                    if (collider == null || (collider.ClosestPoint(point) - point).sqrMagnitude > .12f * .12f) continue;
                    if (!ChainEnemy(caster, target)) continue;
                    state.NextHit[id] = Ashlands70Rules.Deadline(now, Ashlands70Rules.ChainInterval);
                    HitData hit = grant.Damage.Clone(); hit.m_point = point; hit.m_dir = (target.GetCenterPoint() - point).normalized;
                    // Native RPC_Damage rejects unresolved attacker IDs. A fixed zone outlives
                    // its caster's streamed body; NPC-only fallback carries no invented attacker.
                    if (caster == null) hit.m_attacker = ZDOID.None;
                    Ashlands70Cast.Generated(hit); target.Damage(hit);
                }
            }
        }
        internal static bool VictimOwner(Character body) => body != null && !body.IsDead() && body.m_nview?.IsValid() == true && body.m_nview.IsOwner();
        internal static bool Enemy(Character caster, Character target)
        {
            if (caster == null || target == null || caster == target || target.IsDead()) return false;
            if (caster is Player first && target is Player second) return first.IsPVPEnabled() && second.IsPVPEnabled();
            return BaseAI.IsEnemy(caster, target);
        }
        private static bool ChainEnemy(Character caster, Character target)
        {
            if (caster != null) return Enemy(caster, target);
            if (target == null || target.IsDead() || target.IsPlayer()) return false;
            if (Ashlands70Madness.Active(target)) return true;
            // Native player-faction branch: tame/player-faction allies and unaggravated
            // Dvergr are friendly. Used only while the actual caster is unloaded.
            return !target.IsTamed() && target.GetFaction() != Character.Faction.Players &&
                (target.GetFaction() != Character.Faction.Dverger || target.GetBaseAI()?.IsAggravated() == true);
        }
        private static Vector3 Nearest(Vector3 a, Vector3 b, Vector3 point)
        { Vector3 delta = b - a; float square = delta.sqrMagnitude; return square < .0001f ? a : a + delta * Mathf.Clamp01(Vector3.Dot(point - a, delta) / square); }
        internal static bool InGrove(Character body)
        {
            if (body == null || body.IsDead() || !MasteryPlugin.Settings.Enabled.Value) return false;
            long now = Ashlands70Cast.Now;
            foreach (Ashlands70State state in States.Values)
                if (state.Grant.Kind == Ashlands70Kind.Grove && Ashlands70Rules.Active(now, state.Grant.Until) &&
                    (body.transform.position - state.Grant.Origin).sqrMagnitude <= Ashlands70Rules.GroveRadius * Ashlands70Rules.GroveRadius) return true;
            return false;
        }
        internal static float DamageMultiplier(Character victim)
        {
            if (!VictimOwner(victim) || !InGrove(victim)) return 1f;
            if (victim.IsPlayer()) return .85f;
            ZDO data = victim.m_nview.GetZDO();
            bool summon = data.GetLong(SummonRosterCommands.AuthorKey, 0) != 0 ||
                data.GetLong(Ashlands35Summons.AuthorKey, 0) != 0 || Ashlands70Madness.ManagedSummon(victim) || Ashlands70Madness.NativeSummon(victim);
            return summon && !Ashlands70Madness.Active(victim) && victim.GetFaction() == Character.Faction.Players ? .70f : 1f;
        }
        internal static void Retaliate(Player player, Character attacker)
        {
            Ashlands70State orbit = Orbit(player);
            if (orbit == null || orbit.Remaining <= 0 || !VictimOwner(player) || attacker == null || attacker == player || attacker.IsDead() || attacker.m_nview?.IsValid() != true) { Magic70Diagnostics.Event("orbit-guard", player, attacker, "no-active-orbit/ammo/owner/valid-attacker"); return; }
            int slot = --orbit.Remaining; // Reserve before any synchronous native callbacks.
            if (!Ashlands70Projectiles.Fire(player, attacker, orbit.Grant, slot)) { orbit.Remaining++; Magic70Diagnostics.Event("orbit-fire", player, attacker, "not-spawned;slot-restored"); return; }
            player.m_nview.GetZDO().Set(OrbitCountKey, orbit.Remaining);
            Magic70Diagnostics.Event("orbit-fire", player, attacker, "spawned;remaining=" + orbit.Remaining);
        }
        internal static void Reset()
        { Magic70Diagnostics.Reset(); States.Clear(); NextTick = 0f; Ashlands70Madness.Reset(); Ashlands70Presentation.Reset(); Ashlands70Descriptions.Reset(); }
    }
}
