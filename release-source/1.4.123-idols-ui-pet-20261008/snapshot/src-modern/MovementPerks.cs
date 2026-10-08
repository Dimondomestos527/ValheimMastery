using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class MovementPerkState
    {
        internal bool AirJumpSpent, AirJumpArmed;
        internal float GroundSince = -1f, StillSince = -1f, NextSkillSync;
    }
    internal static class MovementPerkService
    {
        internal static readonly int QuietSkillKey = "vm_movement_quiet35".GetStableHashCode();
        internal static bool OwnerReady(Player p) => p != null && p == Player.m_localPlayer && p.m_nview != null && p.m_nview.IsOwner() &&
            !p.IsDead() && !p.IsTeleporting() && !p.InIntro() && !p.IsAttached() && !p.IsDebugFlying();
        internal static bool Valid(Player p) => OwnerReady(p) && !p.IsStaggering() && !Fists70MaulService.IsActive(p) && !Fists70ClientService.LocalSequenceActive(p) && !AssassinBlink70Service.IsStartingBlinkAttack;
        internal static bool DirectAttack(Player p, HitData hit)
        {
            if (hit == null || hit.GetAttacker() == null || hit.GetAttacker() == p || PerkRuntimeService.IsPerkGenerated(hit)) return false;
            // Periodic SE damage has no dodge/block flag; authored attack packets do.
            bool attackType = hit.m_hitType == HitData.HitType.EnemyHit || hit.m_hitType == HitData.HitType.PlayerHit || hit.m_hitType == HitData.HitType.Undefined;
            return attackType && hit.GetTotalDamage() > 0f && (hit.m_dodgeable || hit.m_blockable);
        }
        internal static void Proc(Player p, string id, string recipe, Skills.SkillType skill, int milestone)
        { PerkFeedbackService.Play(p, recipe, p.transform.position, false); PerkProcHudService.Show(p, id, skill, milestone); }
        internal static void Update(Player p)
        {
            if (p == null || p != Player.m_localPlayer) return;
            NativeMovementPulse.Preload();
            var s = MasteryStateStore.GetPlayerState<MovementPerkState>(p);
            if (OwnerReady(p) && Time.time >= s.NextSkillSync)
            {
                s.NextSkillSync = Time.time + .5f;
                p.m_nview.GetZDO()?.Set(QuietSkillKey, PerkRuntimeService.HasPerk(p, Skills.SkillType.Sneak, 35));
                p.m_nview.GetZDO()?.Set(PerfectDodgeStagger.LearnedKey, PerkRuntimeService.HasPerk(p, Skills.SkillType.Dodge, 70));
            }
            if (!Valid(p)) { s.AirJumpSpent = true; s.AirJumpArmed = false; s.GroundSince = -1f; s.StillSince = -1f; return; }
            // Require a real, stable upward-facing contact, not coyote time or a wall brush.
            if (p.IsOnGround() && p.m_lastGroundCollider != null && p.m_lastGroundNormal.y > .6f && p.m_body.linearVelocity.y < 1f && !Stride70Service.State(p).WaterActive)
            {
                if (s.GroundSince < 0f) s.GroundSince = Time.time;
                if (Time.time - s.GroundSince >= .15f) { s.AirJumpSpent = false; s.AirJumpArmed = true; }
            }
            else s.GroundSince = -1f;
            if (!p.IsSwimming() || p.IsOnGround() || Stride70Service.State(p).WaterActive || p.GetMoveDir().sqrMagnitude > .0025f || new Vector2(p.m_body.linearVelocity.x, p.m_body.linearVelocity.z).sqrMagnitude > .16f)
                s.StillSince = -1f;
            else if (s.StillSince < 0f) s.StillSince = Time.time;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    internal static class Jump70AirJumpPatch
    {
        private const float AirJumpForceScale = .8f;

        private static bool Prefix(Character __instance, bool force)
        {
            if (!(__instance is Player p) || !MovementPerkService.Valid(p) || force || p.IsOnGround() || p.InLiquidSwimDepth() ||
                p.InAttack() || p.InDodge() || p.IsKnockedBack() || p.IsEncumbered() || GrapplingPoint.m_localGrappler != null ||
                !PerkRuntimeService.HasPerk(p, Skills.SkillType.Jump, 70)) return true;
            var s = MasteryStateStore.GetPlayerState<MovementPerkState>(p);
            if (!s.AirJumpArmed || s.AirJumpSpent || p.m_jumpTimer < .15f || !p.HaveStamina(p.m_jumpStaminaUsage)) return true;
            s.AirJumpSpent = true;
            Vector3 velocity = p.m_body.linearVelocity;
            Vector3 jump = Vector3.up * p.m_jumpForce * (1f + p.GetSkills().GetSkillFactor(Skills.SkillType.Jump) * .4f);
            p.m_seman.ApplyStatusEffectJumpMods(ref jump);
            velocity.y = jump.y * AirJumpForceScale;
            // Native ForceJump handles animation, noise, stamina and networked jump trigger.
            p.ForceJump(velocity, true);
            // The air-jump path does not emit the perk's jump cue through its VFX recipe.
            PerkAudioService.Play("jump_70_air", "sfx_dodge", p.transform.position, 0.35f, volumeScale: .65f);
            // One native textured ring and short streak burst, no repeated HUD toast.
            NativeMovementPulse.PlayAirJump(p);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSwimming))]
    internal static class Swim35DrainPatch
    {
        private static void Prefix(Player __instance, out Vector2 __state)
        {
            __state = new Vector2(__instance.m_swimStaminaDrainMinSkill, __instance.m_swimStaminaDrainMaxSkill);
            if (!MovementPerkService.Valid(__instance) || !PerkRuntimeService.HasPerk(__instance, Skills.SkillType.Swim, 35)) return;
            __instance.m_swimStaminaDrainMinSkill *= .75f; __instance.m_swimStaminaDrainMaxSkill *= .75f;
        }
        private static void Finalizer(Player __instance, Vector2 __state)
        { __instance.m_swimStaminaDrainMinSkill = __state.x; __instance.m_swimStaminaDrainMaxSkill = __state.y; }
    }
    [HarmonyPatch(typeof(Character), "UpdateSwimming")]
    internal static class SwimSpeedPatch
    {
        private static void Prefix(Character __instance, out float __state)
        {
            __state = __instance.m_swimSpeed;
            if (!(__instance is Player p) || !MovementPerkService.Valid(p)) return;
            if (PerkRuntimeService.HasPerk(p, Skills.SkillType.Swim, 70)) p.m_swimSpeed *= 1.4f;
            else if (PerkRuntimeService.HasPerk(p, Skills.SkillType.Swim, 35)) p.m_swimSpeed *= 1.2f;
        }
        private static void Finalizer(Character __instance, float __state) => __instance.m_swimSpeed = __state;
    }
    [HarmonyPatch(typeof(Player), "UpdateStats", new[] { typeof(float) })]
    internal static class Swim70RestPatch
    {
        private static void Postfix(Player __instance, float dt)
        {
            var p = __instance;
            if (!MovementPerkService.Valid(p) || !p.IsSwimming() || p.IsOnGround() || p.IsEncumbered() || p.InAttack() || p.InDodge() ||
                Stride70Service.State(p).WaterActive || !PerkRuntimeService.HasPerk(p, Skills.SkillType.Swim, 70) || p.m_staminaRegenTimer > 0f) return;
            var s = MasteryStateStore.GetPlayerState<MovementPerkState>(p);
            if (s.StillSince < 0f || Time.time - s.StillSince < 1f || p.GetMoveDir().sqrMagnitude > .0025f) return;
            // Vanilla deep-water regen is zero. Reconstruct its land formula, including SEs,
            // without enabling the land branch (which would stack a full regen tick).
            float regen = p.m_staminaRegen * (1f + (1f - p.GetStaminaPercentage()) * p.m_staminaRegenTimeMultiplier);
            float modifier = 1f; p.m_seman.ModifyStaminaRegen(ref modifier);
            if (p.IsBlocking()) regen *= .8f;
            p.AddStamina(regen * modifier * Game.m_staminaRegenRate * dt * .4f);
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.OnSneaking))]
    internal static class Sneak35DrainPatch
    {
        private static void Prefix(Player __instance, out float __state)
        { __state = __instance.m_sneakStaminaDrain; if (MovementPerkService.Valid(__instance) && PerkRuntimeService.HasPerk(__instance, Skills.SkillType.Sneak, 35)) __instance.m_sneakStaminaDrain *= .7f; }
        private static void Finalizer(Player __instance, float __state) => __instance.m_sneakStaminaDrain = __state;
    }
    [HarmonyPatch(typeof(Character), "UpdateWalking")]
    internal static class MovementWalkingFieldsPatch
    {
        private static void Prefix(Character __instance, out Vector2 __state)
        {
            __state = new Vector2(__instance.m_crouchSpeed, __instance.m_deepSnowSlowMax);
            if (!(__instance is Player p) || !MovementPerkService.Valid(p)) return;
            if (PerkRuntimeService.HasPerk(p, Skills.SkillType.Sneak, 35)) p.m_crouchSpeed *= 1.25f;
            if (Stride70Service.Trail(p)) p.m_deepSnowSlowMax *= .2f;
        }
        private static void Finalizer(Character __instance, Vector2 __state)
        { __instance.m_crouchSpeed = __state.x; __instance.m_deepSnowSlowMax = __state.y; }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.AddNoise))]
    internal static class Sneak35NoisePatch
    {
        private static void Prefix(Character __instance, ref float range)
        { if (__instance is Player p && MovementPerkService.Valid(p) && p.IsCrouching() && !p.InAttack() && PerkRuntimeService.HasPerk(p, Skills.SkillType.Sneak, 35)) range *= .8f; }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.GetStealthFactor))]
    internal static class Sneak35VisibilityPatch
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            bool perk = __instance == Player.m_localPlayer ? PerkRuntimeService.HasPerk(__instance, Skills.SkillType.Sneak, 35) : (__instance.m_nview?.GetZDO()?.GetBool(MovementPerkService.QuietSkillKey, false) ?? false);
            if (perk && __instance.IsCrouching()) __result *= .85f;
        }
    }
}
