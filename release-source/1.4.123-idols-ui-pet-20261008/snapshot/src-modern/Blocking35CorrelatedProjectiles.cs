#if MASTERY_SHIELD35_EXPERIMENT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // One native victim-owner damage/block resolution, never a manual block probe.
    internal static class Blocking35ProjectileService
    {
        internal const short ReflectedVariant = 1235;
        [ThreadStatic] internal static Projectile Current;
        [ThreadStatic] internal static Flight CurrentExplosion;
        [ThreadStatic] private static Impact Resolving;
        internal sealed class Flight { internal ZDOID Id; internal int Prefab; internal Vector3 Velocity; internal long Authority; }
        private static readonly ConditionalWeakTable<Aoe, Flight> Explosions = new ConditionalWeakTable<Aoe, Flight>();
        private sealed class Impact
        {
            internal Player Player;
            internal int Prefab;
            internal Vector3 Velocity;
            internal HitData Original;
            internal ZDOID Identity;
            internal bool Perfect, Tower, Guard;
        }
        private sealed class Receipts
        {
            // Direct contact and a short spawned explosion are separate native
            // payloads. Deduplicate each, not the whole shot, unless parried.
            internal readonly Dictionary<ZDOID, int> Seen = new Dictionary<ZDOID, int>();
            internal readonly Queue<ZDOID> Order = new Queue<ZDOID>();
        }
        private static readonly ConditionalWeakTable<Player, Receipts> History = new ConditionalWeakTable<Player, Receipts>();
        internal static bool Handling(Player player) => Resolving?.Player == player;
        private static bool Direct(Projectile shot) => shot != null && shot.m_hitVariant != ReflectedVariant;
        private static Flight Capture(Projectile shot)
        {
            if (!Direct(shot) || shot.m_nview?.IsValid() != true || !shot.m_nview.IsOwner()) return null;
            ZDO data = shot.m_nview.GetZDO();
            return new Flight { Id = data.m_uid, Prefab = data.GetPrefab(), Velocity = shot.m_vel, Authority = data.GetOwner() };
        }
        internal static void CaptureExplosion(Aoe area)
        {
            Flight flight = Capture(Current);
            // Only a short, immediate impact spawned by a real projectile.
            // Persistent clouds and unrelated environmental/melee AoE stay native.
            if (flight != null && area.m_ttl > 0f && area.m_ttl <= 3f)
            { Explosions.Remove(area); Explosions.Add(area, flight); }
        }
        internal static Flight Explosion(Aoe area) => Explosions.TryGetValue(area, out var flight) ? flight : null;
        internal static void Register(Player player)
        {
            if (player.m_nview?.IsValid() == true)
                player.m_nview.Register<ZPackage>("VM_GuardProjectile35", (sender, packet) => Receive(player, sender, packet));
        }
        internal static bool Route(Character victim, HitData hit)
        {
            Flight flight = CurrentExplosion ?? Capture(Current);
            Player player = victim as Player;
            if (player == null || hit == null || !MasteryPlugin.Settings.Enabled.Value || flight == null ||
                player.m_nview?.IsValid() != true || PerkRuntimeService.IsPerkGenerated(hit) ||
                (hit.GetTotalDamage() <= 0f && hit.m_statusEffectHash == 0) ||
                hit.GetAttacker()?.m_nview?.GetZDO()?.GetOwner() != flight.Authority) return true;
            ZPackage packet = new ZPackage();
            packet.Write(flight.Id); packet.Write(flight.Prefab); packet.Write(flight.Velocity);
            packet.Write(CurrentExplosion != null);
            hit.Serialize(ref packet);
            if (player.m_nview.IsOwner()) { packet.SetPos(0); Receive(player, flight.Authority, packet); }
            else player.m_nview.InvokeRPC("VM_GuardProjectile35", packet);
            return false;
        }
        private static void Receive(Player player, long sender, ZPackage packet)
        {
            if (player?.m_nview?.IsOwner() != true || packet == null || packet.Size() > 2048) return;
            try
            {
                ZDOID identity = packet.ReadZDOID(); int prefab = packet.ReadInt(); Vector3 velocity = packet.ReadVector3();
                int phase = packet.ReadBool() ? 2 : 1;
                HitData hit = new HitData(); hit.Deserialize(ref packet);
                Character source = hit.GetAttacker(); GameObject native = ZNetScene.instance?.GetPrefab(prefab);
                // Native OnHit removes the original entity. Authenticate sender
                // against its native source, using the approved vanilla trust model.
                if (identity == ZDOID.None || source?.m_nview?.IsValid() != true || source.m_nview.GetZDO().GetOwner() != sender ||
                    !Direct(native?.GetComponent<Projectile>()) || !Finite(velocity) || velocity.sqrMagnitude > 90000f ||
                    !Finite(hit.m_point) || (hit.m_point - player.GetCenterPoint()).sqrMagnitude > 25f ||
                    !float.IsFinite(hit.GetTotalDamage()) || hit.GetTotalDamage() < 0f || hit.GetTotalDamage() > 100000f) return;
                Receipts receipts = History.GetOrCreateValue(player);
                bool known = receipts.Seen.TryGetValue(identity, out int phases);
                if ((phases & (phase | 4)) != 0) return;
                receipts.Seen[identity] = phases | phase;
                if (!known) receipts.Order.Enqueue(identity);
                while (receipts.Order.Count > 256) receipts.Seen.Remove(receipts.Order.Dequeue());
                Impact previous = Resolving;
                Resolving = new Impact { Player = player, Prefab = prefab, Velocity = velocity, Original = hit.Clone(), Identity = identity };
                try { player.RPC_Damage(sender, hit); }
                finally { Resolving = previous; }
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Shield35] Invalid correlated projectile: " + error.GetType().Name); }
        }
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        internal static void BeforeBlock(Humanoid character)
        {
            Impact impact = Resolving;
            if (impact?.Player != character || !PerkRuntimeService.HasPerk(impact.Player, Skills.SkillType.Blocking, 35)) return;
            MasteryShieldClass kind = ShieldWeaponClassService.Classify(impact.Player.GetCurrentBlocker());
            impact.Tower = kind == MasteryShieldClass.Heavy;
            impact.Perfect = kind == MasteryShieldClass.ParryCapable && impact.Player.m_blockTimer >= 0f &&
                impact.Player.m_blockTimer < Humanoid.m_perfectBlockInterval;
            impact.Guard = impact.Tower || impact.Perfect;
        }
        internal static void Stagger(Character character, ref float damage)
        { if (Resolving?.Player == character && Resolving.Tower && Resolving.Guard) damage *= .5f; }
        internal static void AfterBlock(Humanoid character, HitData hit, bool blocked)
        {
            Impact impact = Resolving;
            if (impact?.Player != character || !impact.Guard || !blocked || character.IsStaggering() ||
                !character.HaveStamina(0f) || character.m_staggerDamage >= character.GetStaggerTreshold()) return;
            hit.m_statusEffectHash = 0;
            if (!impact.Perfect) return;
            hit.m_damage = default; hit.m_pushForce = 0f;
            Receipts receipts = History.GetOrCreateValue(impact.Player);
            receipts.Seen[impact.Identity] = receipts.Seen[impact.Identity] | 4;
            Return(impact);
        }
        private static void Return(Impact impact)
        {
            Player defender = impact.Player; Character source = impact.Original.GetAttacker();
            Vector3 direction = source != null && !source.IsDead() ? (source.GetCenterPoint() - defender.GetCenterPoint()).normalized : -impact.Velocity.normalized;
            if (direction.sqrMagnitude < .01f) direction = defender.transform.forward;
            GameObject prefab = ZNetScene.instance.GetPrefab(impact.Prefab);
            Projectile reflected = UnityEngine.Object.Instantiate(prefab, defender.GetCenterPoint() + direction * (defender.GetRadius() + .5f), Quaternion.LookRotation(direction)).GetComponent<Projectile>();
            HitData payload = impact.Original.Clone();
            payload.m_variant = ReflectedVariant; payload.m_skill = Skills.SkillType.Blocking; payload.m_skillRaiseAmount = 0f;
            payload.SetAttacker(defender); reflected.m_respawnItemOnHit = false;
            // A safe elemental counterpart: original visual/type/status, but a
            // single direct payload instead of a second spawned environmental AoE.
            reflected.m_aoe = 0f; reflected.m_spawnOnHit = null; reflected.m_randomSpawnOnHit = null;
            reflected.m_onlySpawnedProjectilesDealDamage = false; reflected.m_spawnOnTtl = false;
            reflected.Setup(defender, direction * Mathf.Max(1f, impact.Velocity.magnitude) * 1.5f, -1f, payload, null, null);
            reflected.m_hitVariant = ReflectedVariant;
            PerkNativeFeedback.PlayVfx("fx_StaffShield_Hit", defender.GetCenterPoint(), .35f, .6f);
            PerkAudioService.Play("shield35_return", "sfx_perfectblock", defender.GetCenterPoint(), .15f, .65f);
        }
    }
    [HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
    internal static class Blocking35ExplosionCapture
    { private static void Postfix(Aoe __instance) => Blocking35ProjectileService.CaptureExplosion(__instance); }
    [HarmonyPatch(typeof(Aoe), "OnHit")]
    internal static class Blocking35ExplosionScope
    {
        private static void Prefix(Aoe __instance, out Blocking35ProjectileService.Flight __state)
        { __state = Blocking35ProjectileService.CurrentExplosion; Blocking35ProjectileService.CurrentExplosion = Blocking35ProjectileService.Explosion(__instance); }
        private static Exception Finalizer(Blocking35ProjectileService.Flight __state, Exception __exception)
        { Blocking35ProjectileService.CurrentExplosion = __state; return __exception; }
    }
    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class Blocking35Registration { private static void Postfix(Player __instance) => Blocking35ProjectileService.Register(__instance); }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class Blocking35ProjectileScope
    {
        private static void Prefix(Projectile __instance, out Projectile __state) { __state = Blocking35ProjectileService.Current; Blocking35ProjectileService.Current = __instance; }
        private static Exception Finalizer(Projectile __state, Exception __exception) { Blocking35ProjectileService.Current = __state; return __exception; }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Blocking35CorrelatedDamage
    { private static bool Prefix(Character __instance, HitData hit) => Blocking35ProjectileService.Route(__instance, hit); }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.BlockAttack))]
    internal static class Blocking35NativeBlock
    {
        private static void Prefix(Humanoid __instance) => Blocking35ProjectileService.BeforeBlock(__instance);
        private static void Postfix(Humanoid __instance, HitData hit, bool __result) => Blocking35ProjectileService.AfterBlock(__instance, hit, __result);
    }
    [HarmonyPatch(typeof(Character), nameof(Character.AddStaggerDamage))]
    internal static class Blocking35TowerStagger
    { private static void Prefix(Character __instance, ref float damage) => Blocking35ProjectileService.Stagger(__instance, ref damage); }
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class Blocking35VariantVisual
    { private static void Prefix(ref int variant) { if (variant == Blocking35ProjectileService.ReflectedVariant) variant = 0; } }
}
#endif
