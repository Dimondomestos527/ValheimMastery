using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class Stride70State
    {
        internal float StartedAt = -1f, EmergencyUntil, WaterUntil, NextSplash, LastRunningAt;
        internal int Stacks;
        internal bool WaterActive;
    }
    internal static class Stride70Service
    {
        internal static void Update(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            Stride70State state = MasteryStateStore.GetPlayerState<Stride70State>(player);
            if (!MovementPerkService.OwnerReady(player)) state.EmergencyUntil = 0f;
            bool sprint = MovementPerkService.Valid(player) && PerkRuntimeService.HasPerk(player, Skills.SkillType.Run, 35) && player.m_run && player.GetMoveDir().sqrMagnitude > .01f && player.GetStamina() > 0f && !player.IsCrouching() && !player.InAttack() && !player.InDodge();
            if (sprint && player.IsRunning()) state.LastRunningAt = Time.time;
            // Preserve the built rhythm across a short airborne shore entry,
            // but do not accumulate new stacks while jumping.
            if (sprint && !state.WaterActive && !player.IsRunning() && !player.InLiquidSwimDepth() &&
                !HasSolidGround(player) && Time.time - state.LastRunningAt < .8f) return;
            if (state.WaterActive && (!sprint || Time.time >= state.WaterUntil || HasSolidGround(player) || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Run, 70)))
                EndWater(player, state, !sprint ? "sprint stopped" : Time.time >= state.WaterUntil ? "duration expired" : HasSolidGround(player) ? "solid ground" : "perk lost");
            if (!sprint || (!player.IsRunning() && !state.WaterActive && !player.InLiquidSwimDepth()))
            {
                state.StartedAt = -1f; state.Stacks = 0;
                RoadRhythmPhaseVisual.Clear(player);
                if (state.EmergencyUntil > Time.time) RoadRhythmVisual.Set(player, 0);
                else RoadRhythmVisual.Clear(player);
                return;
            }
            // Deep swimming can preserve an entry rhythm for the physics hook, never build it.
            if (!state.WaterActive && player.InLiquidSwimDepth()) return;
            if (state.StartedAt < 0f)
            {
                state.StartedAt = Time.time;
                NativeSoftVisualAssets.GetPrefab("fx_land");
                NativeSoftVisualAssets.GetPrefab("sfx_dodge");
            }
            int stacks = RhythmTier(Time.time - state.StartedAt);
            if (stacks == state.Stacks) return;
            state.Stacks = stacks;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[RunRhythm] phase=" + stacks + " run70=" + PerkRuntimeService.HasPerk(player, Skills.SkillType.Run, 70));
            RoadRhythmVisual.Set(player, stacks);
            // Native phase-transition emission and sound are owned by one call; no
            // continuous wake and no parallel manual-emission implementation.
            if (stacks > 0)
                PerkFeedbackService.Play(player, "run_70_stack" + stacks, player.transform.position, false);
        }
        internal static int RhythmTier(float elapsed) => elapsed >= 7f ? 3 : elapsed >= 5f ? 2 : elapsed >= 3f ? 1 : 0;
        internal static Stride70State State(Player p) => MasteryStateStore.GetPlayerState<Stride70State>(p);
        internal static bool Trail(Player p) => MovementPerkService.Valid(p) && p.m_run && GetStacks(p) == 3 && PerkRuntimeService.HasPerk(p, Skills.SkillType.Run, 70);
        internal static float EmergencySpeed(Player p) => p != null && MasteryStateStore.TryGetPlayerState<Stride70State>(p, out var s) && s.EmergencyUntil > Time.time ? .15f : 0f;
        internal static bool HasSolidGround(Player p)
        {
            // Native IsOnGround includes 0.2s of coyote time. A stale shore contact
            // must not end water support on the very next frame after activation.
            if (!p.IsOnGround() || p.m_lastGroundCollider == null || p.m_lastGroundNormal.y <= .6f) return false;
            var bounds = p.GetCollider().bounds;
            var foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return p.m_lastGroundCollider.Raycast(new Ray(foot + Vector3.up * .15f, Vector3.down), out var contact, .35f) && contact.normal.y > .6f;
        }
        internal static void EndWater(Player p, Stride70State s, string reason = "motion eligibility lost")
        {
            if (!s.WaterActive) return;
            s.WaterActive = false;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Run70Water] ended: " + reason + ", groundTouch=" + p.m_lastGroundTouch.ToString("0.000") +
                    ", waterOffset=" + (p.transform.position.y - p.m_waterLevel).ToString("0.00"));
            PerkCooldownStateService.TryConsume(p, "run_70_water", 45d);
            PerkFeedbackService.Play(p, "run_70_water_end", p.transform.position, false);
        }
        internal static bool WaterMotion(Player p, float dt)
        {
            var s = State(p);
            float offset = p.transform.position.y - p.m_waterLevel;
            // UpdateSwimming may be called before actually reaching water. Do not
            // erase the running rhythm on dry land or above the entry window.
            if (!s.WaterActive && (HasSolidGround(p) || offset >= 1.5f || p.m_waterLevel <= p.m_tarLevel)) return false;
            bool eligible = Trail(p) && p.GetStamina() > 0f && p.GetMoveDir().sqrMagnitude > .01f && !p.InAttack() && !p.InDodge() && !p.IsBlocking() && !p.IsDrawingBow() && !p.IsEncumbered() &&
                p.m_waterLevel > p.m_tarLevel && !p.AboveOrInLava() && offset > -p.m_swimDepth - .35f && offset < 1.5f && p.m_body.linearVelocity.y > -5f;
            if (s.WaterActive && (!eligible || Time.time >= s.WaterUntil)) EndWater(p, s, eligible ? "duration expired" : "motion eligibility lost");
            if (!s.WaterActive)
            {
                if (HasSolidGround(p)) return false;
                if (!eligible || PerkCooldownStateService.GetRemainingSeconds(p, "run_70_water") > 0d) { s.Stacks = 0; s.StartedAt = -1f; return false; }
                s.WaterActive = true; s.WaterUntil = Time.time + 10f;
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Run70Water] started: offset=" + offset.ToString("0.00"));
                MovementPerkService.Proc(p, "run_70", "run_70_water_start", Skills.SkillType.Run, 70);
            }
            // Native walking retains steering, stamina, collision and animations. The temporary
            // support frame is restored before jump/fall/grounding logic can observe it.
            float touch = p.m_lastGroundTouch;
            Vector3 normal = p.m_lastGroundNormal;
            Rigidbody groundBody = p.m_lastGroundBody;
            try
            { p.m_lastGroundTouch = 0f; p.m_lastGroundNormal = Vector3.up; p.m_lastGroundBody = null; p.UpdateWalking(dt); }
            finally { p.m_lastGroundTouch = touch; p.m_lastGroundNormal = normal; p.m_lastGroundBody = groundBody; }
            // Bounded physical spring follows waves; no teleport and gravity stays enabled.
            float desiredY = Mathf.Clamp((p.m_waterLevel - .08f - p.transform.position.y) * 8f, -2.5f, 3f);
            float acceleration = Mathf.Clamp((desiredY - p.m_body.linearVelocity.y) * 12f - Physics.gravity.y, -35f, 45f);
            p.m_body.AddForce(Vector3.up * acceleration, ForceMode.Acceleration);
            return true;
        }
        internal static int GetStacks(Player player)
        {
            if (player != null && MasteryStateStore.TryGetPlayerState<Stride70State>(player, out Stride70State state)) return state.Stacks;
            return 0;
        }
    }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Stride70UpdatePatch
    {
        private static void Postfix(Player __instance)
        { Stride70Service.Update(__instance); MovementPerkService.Update(__instance); VeiledService.Update(__instance); }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.CheckRun))]
    internal static class Stride70StaminaPatch
    {
        private static void Prefix(Player __instance, out float __state)
        { __state = __instance.m_runStaminaDrain; if (MovementPerkService.Valid(__instance)) __instance.m_runStaminaDrain *= 1f - .4f * Stride70Service.GetStacks(__instance) / 3f; }
        private static void Finalizer(Player __instance, float __state) => __instance.m_runStaminaDrain = __state;
    }
    [HarmonyPatch(typeof(Character), "UpdateSwimming")]
    internal static class Run70WaterMotionPatch
    {
        private static bool Prefix(Character __instance, float dt) => !(__instance is Player p && MovementPerkService.Valid(p) && Stride70Service.WaterMotion(p, dt));
    }
    [HarmonyPatch(typeof(Character), "GetSlideAngle")]
    internal static class Run70SlopePatch
    {
        private static void Postfix(Character __instance, ref float __result)
        { if (__instance is Player p && Stride70Service.Trail(p)) __result = Mathf.Min(65f, __result + 15f); }
    }
    [HarmonyPatch(typeof(Character), "ApplyLiquidResistance")]
    internal static class Run70ShallowWaterPatch
    {
        private static void Prefix(float speed, out float __state) => __state = speed;
        private static void Postfix(Character __instance, ref float speed, float __state)
        { if (__instance is Player p && Stride70Service.Trail(p) && p.m_waterLevel > p.m_tarLevel && !p.AboveOrInLava()) speed = Mathf.Lerp(speed, __state, .8f); }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.IsSwimming))]
    internal static class Run70SurfaceSupportPatch
    {
        private static void Postfix(Character __instance, ref bool __result)
        {
            // Keep the water controller selected as the physical spring reaches the surface.
            // Real ground contact still wins; never advertise synthetic ground to fall/jump code.
            if (__instance is Player p && p == Player.m_localPlayer && !Stride70Service.HasSolidGround(p) && Stride70Service.State(p).WaterActive) __result = true;
        }
    }
    [HarmonyPatch(typeof(FootStep), "OnFoot", new[] { typeof(Transform) })]
    internal static class Run70FootSplashPatch
    {
        private static void Postfix(FootStep __instance, Transform foot)
        {
            if (!(__instance.m_character is Player p) || p != Player.m_localPlayer) return;
            var s = Stride70Service.State(p);
            if (!s.WaterActive || Time.time < s.NextSplash) return;
            s.NextSplash = Time.time + .12f;
            Vector3 point = foot != null ? foot.position : p.transform.position;
            point.y = p.m_waterLevel;
            PerkFeedbackService.Play(p, "run_70_water_step", point, false);
        }
    }
}
