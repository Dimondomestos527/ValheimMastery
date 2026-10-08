#if MASTERY_CLUBS35_EXPERIMENT
using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Mace35CorpseProjectile
    {
        internal const short GeneratedVariant = 1201;
        [ThreadStatic] internal static Attack Current;
        private sealed class AttackFeedback { internal bool Played; }
        private static readonly ConditionalWeakTable<Attack, AttackFeedback> Feedback =
            new ConditionalWeakTable<Attack, AttackFeedback>();
        internal static void Mark(Character target, HitData hit)
        {
            Attack attack = Current; Player player = attack?.m_character as Player;
            if (target == null || hit == null || player == null || hit.GetAttacker() != player || hit.m_skill != Skills.SkillType.Clubs ||
                !MasteryPlugin.Settings.Enabled.Value || PerkRuntimeService.IsPerkGenerated(hit) ||
                ClubWeaponClassService.Classify(attack.m_weapon) != ClubWeaponClass.Mace || !AttackIntentService.IsSecondary(attack, player) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 35) || !BaseAI.IsEnemy(player, target)) return;
            AttackFeedback feedback = Feedback.GetOrCreateValue(attack);
            if (!feedback.Played)
            {
                feedback.Played = true;
                PerkFeedbackService.Play(player, "clubs_35", hit.m_point, false);
            }
            float charge = 0f;
#if MASTERY_CLUBS70_EXPERIMENT
            charge = Clubs70Reservation.Factor(attack);
#endif
            // A native serialized variant keeps the real secondary intent and
            // charge fraction through the victim-owner damage RPC.
            hit.m_variant = (short)(1000 + Mathf.RoundToInt(100f * charge));
        }
        internal static void Launch(Character victim, Ragdoll ragdoll)
        {
            HitData hit = victim?.m_lastHit;
            Player player = hit?.GetAttacker() as Player;
            if (!MasteryPlugin.Settings.Enabled.Value || ragdoll?.m_nview?.IsOwner() != true || player == null ||
                victim.IsPlayer() || victim.IsTamed() || victim.IsBoss() ||
                MasteryClassificationService.GetCreatureClass(victim) != CreatureClass.SmallNormal ||
                hit.m_skill != Skills.SkillType.Clubs || hit.m_variant < 1000 || hit.m_variant > 1100 || !BaseAI.IsEnemy(player, victim)) return;
            float charge = (hit.m_variant - 1000) / 100f;
            Vector3 direction = hit.m_dir; direction.y = Mathf.Max(.12f, direction.y); direction.Normalize();
            Vector3 velocity = direction * Mathf.Min(40f, 16f * (1f + 1.5f * charge));
            ragdoll.m_nview.GetZDO().Set(ZDOVars.s_initVel, velocity);
            float world = Game.instance != null ? Game.instance.GetDifficultyDamageScaleEnemy(victim.transform.position) * Game.m_playerDamageRate : 1f;
            float damage = Mathf.Clamp(hit.GetTotalDamage() * world * .4f * (1f + 2f * charge), 0f, 1000f);
            var flight = ragdoll.gameObject.AddComponent<Mace35CorpseFlight>();
            // A corpse is the moving hitbox for this perk; make its confirmed
            // Charge survives the native damage RPC in the existing variant.
            // Full charge: 40m/s launch, 3x corpse damage and 120 impact force.
            flight.Begin(ragdoll, player, damage, hit.m_staggerMultiplier, 45f + 75f * charge);
            PerkNativeFeedback.PlayVfx("fx_land", victim.transform.position, .5f, .7f);
            // Native ragdoll lifetime/loot are untouched. No cloned enemy and no
            // additional drops; only the existing corpse acquires bounded hits.
        }
    }
    internal sealed class Mace35CorpseFlight : MonoBehaviour
    {
        private Ragdoll Body;
        private Player Source;
        private Vector3 Previous;
        private float Until, Damage, Stagger, Force;
        private bool FeedbackPlayed;
        private readonly RaycastHit[] Sweep = new RaycastHit[32];
        private readonly System.Collections.Generic.HashSet<ZDOID> Hit = new System.Collections.Generic.HashSet<ZDOID>();
        internal void Begin(Ragdoll body, Player source, float damage, float stagger, float force)
        {
            Body = body; Source = source; Damage = damage; Stagger = Mathf.Clamp(stagger, 0f, 4f); Force = Mathf.Clamp(force, 0f, 120f);
            Previous = body.GetAverageBodyPosition(); Until = Time.time + 1.5f; FeedbackPlayed = false;
        }
        private void FixedUpdate()
        {
            if (Time.time >= Until || Body?.m_nview?.IsOwner() != true || Source == null || Source.IsDead() ||
                !MasteryPlugin.Settings.Enabled.Value || Hit.Count >= 16) { Destroy(this); return; }
            Vector3 current = Body.GetAverageBodyPosition(), delta = current - Previous;
            float length = delta.magnitude;
            if (length <= .01f) return;
            if (length > 5f) { Destroy(this); return; } // teleport/ownership discontinuity
            int count = Physics.SphereCastNonAlloc(Previous, .5f, delta / length, Sweep, length,
                LayerMask.GetMask("character", "character_net", "terrain", "static_solid", "piece", "Default"), QueryTriggerInteraction.Ignore);
            if (count == Sweep.Length) { Destroy(this); return; }
            float wall = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Sweep[i].collider;
                if (collider == null || collider.GetComponentInParent<Ragdoll>() == Body || collider.GetComponentInParent<Character>() != null) continue;
                wall = Mathf.Min(wall, Sweep[i].distance);
            }
            for (int i = 0; i < count; i++)
            {
                RaycastHit impact = Sweep[i]; Sweep[i] = default;
                Character target = impact.collider?.GetComponentInParent<Character>();
                if (impact.distance > wall || target == null || target == Source || target.IsPlayer() || target.IsTamed() || target.IsDead() ||
                    !BaseAI.IsEnemy(Source, target) || !Hit.Add(target.GetZDOID())) continue;
                var hit = new HitData { m_skill = Skills.SkillType.Clubs, m_variant = Mace35CorpseProjectile.GeneratedVariant,
                    m_skillRaiseAmount = 0f, m_point = impact.point, m_dir = delta / length, m_pushForce = Force, m_staggerMultiplier = Stagger };
                hit.m_damage.m_blunt = Damage; hit.SetAttacker(Source);
                var context = PerkRuntimeService.GetHitContext(hit); context.IsPerkGenerated = true;
                context.AllowSelfProc = context.AllowOtherPerkProc = false; context.XpMultiplier = 0f; context.PerkId = "clubs_35_corpse";
                target.Damage(hit);
                // The corpse flight can be simulated on a dedicated server, where
                // local-only PlayVfx/audio calls are intentionally suppressed.
                // Relay one confirmed physical impact cue to the owning client.
                if (!FeedbackPlayed)
                { FeedbackPlayed = true; PerkFeedbackService.Play(Source, "clubs_35", impact.point, false); }
            }
            Previous = current;
            if (!float.IsPositiveInfinity(wall)) Destroy(this);
        }
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    internal static class Mace35AttackScope
    {
        private static void Prefix(Attack __instance, out Attack __state) { __state = Mace35CorpseProjectile.Current; Mace35CorpseProjectile.Current = __instance; }
        private static Exception Finalizer(Attack __state, Exception __exception) { Mace35CorpseProjectile.Current = __state; return __exception; }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Mace35IntentMark
    { private static void Prefix(Character __instance, HitData hit) => Mace35CorpseProjectile.Mark(__instance, hit); }
    [HarmonyPatch(typeof(Character), nameof(Character.OnRagdollCreated))]
    internal static class Mace35DeathLaunch
    { private static void Postfix(Character __instance, Ragdoll ragdoll) => Mace35CorpseProjectile.Launch(__instance, ragdoll); }
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class ClubsDraftVisualVariant
    { private static void Prefix(ref int variant) { if (variant == 935 || variant == 1201 || (variant >= 1000 && variant <= 1100)) variant = 0; } }
}
#endif
