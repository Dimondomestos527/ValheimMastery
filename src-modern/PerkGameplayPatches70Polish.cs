using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    [Flags]
    internal enum MasteryAttackTag
    {
        None = 0,
        Secondary = 1,
        LegacyAxe35Empowered = 2,
        ShadowStrike = 4,
        Knives70Shadow = 8,
        Fists70Maul = 16,
        Overdraw = 32
    }

    // HitData.m_radius is serialized by vanilla and is otherwise unused for Character melee hits.
    // A negative private range carries attack intent from the owning client to the target owner/server.
    internal static class MasteryAttackTagService
    {
        private const float TagBase = -9000f;
        private const int TagBits = 6;
        private const int TagMask = (1 << TagBits) - 1;
        // Keep the packed integer below the exact-integer boundary of a float even after TagBase.
        private const int MaxAttackSerial = (1 << 17) - 1;
        [ThreadStatic] private static int _secondaryScopeDepth;

        internal static bool SecondaryScopeActive => _secondaryScopeDepth > 0;
        internal static void EnterSecondaryScope() => _secondaryScopeDepth++;
        internal static void ExitSecondaryScope() => _secondaryScopeDepth = Math.Max(0, _secondaryScopeDepth - 1);

        internal static void Add(HitData hit, MasteryAttackTag tag)
        {
            if (hit == null || tag == MasteryAttackTag.None) return;
            int mask = (int)Get(hit) | (int)tag;
            int serial = GetAttackSerial(hit);
            hit.m_radius = TagBase - ((serial << TagBits) | mask);
        }

        internal static MasteryAttackTag Get(HitData hit)
        {
            if (!TryDecode(hit, out int packed)) return MasteryAttackTag.None;
            return (MasteryAttackTag)(packed & TagMask);
        }

        internal static bool Has(HitData hit, MasteryAttackTag tag) => (Get(hit) & tag) != 0;

        internal static void SetAttackSerial(HitData hit, int serial)
        {
            if (hit == null) return;
            int mask = (int)Get(hit) & TagMask;
            serial = Mathf.Clamp(serial, 1, MaxAttackSerial);
            hit.m_radius = TagBase - ((serial << TagBits) | mask);
        }

        internal static int GetAttackSerial(HitData hit)
        {
            return TryDecode(hit, out int packed) ? packed >> TagBits : 0;
        }

        private static bool TryDecode(HitData hit, out int packed)
        {
            packed = 0;
            if (hit == null || hit.m_radius > TagBase - 0.5f) return false;
            float encoded = TagBase - hit.m_radius;
            if (encoded < 0f || encoded > ((MaxAttackSerial << TagBits) | TagMask)) return false;
            packed = Mathf.RoundToInt(encoded);
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class MasteryMeleeAttackTagPatch
    {
        private static void Prefix(HitData hit)
        {
            Player player = hit?.GetAttacker() as Player;
            if (player == null || hit.m_ranged || PerkRuntimeService.IsPerkGenerated(hit)) return;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon?.m_shared == null || weapon.m_shared.m_skillType != hit.m_skill) return;

            if (MasteryAttackTagService.SecondaryScopeActive)
                MasteryAttackTagService.Add(hit, MasteryAttackTag.Secondary);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    internal static class MasterySecondaryMeleeScopePatch
    {
        private static void Prefix(Attack __instance, out bool __state)
        {
            Player player = __instance?.m_character as Player;
            __state = player != null && AttackIntentService.IsSecondary(__instance, player);
            if (__state) MasteryAttackTagService.EnterSecondaryScope();
        }

        private static void Postfix(ref bool __state)
        {
            if (__state) { MasteryAttackTagService.ExitSecondaryScope(); __state = false; }
        }
        private static Exception Finalizer(bool __state, Exception __exception) { if (__state) MasteryAttackTagService.ExitSecondaryScope(); return __exception; }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class MasterySecondaryAreaScopePatch
    {
        private static void Prefix(Attack __instance, out bool __state)
        {
            Player player = __instance?.m_character as Player;
            __state = player != null && AttackIntentService.IsSecondary(__instance, player);
            if (__state) MasteryAttackTagService.EnterSecondaryScope();
        }

        private static void Postfix(ref bool __state)
        {
            if (__state) { MasteryAttackTagService.ExitSecondaryScope(); __state = false; }
        }
        private static Exception Finalizer(bool __state, Exception __exception) { if (__state) MasteryAttackTagService.ExitSecondaryScope(); return __exception; }
    }
    internal enum PolearmSpinPhase { Idle, Starting, Spinning, Stopping }

    internal sealed class Polearms70SpinState
    {
        internal PolearmSpinPhase Phase;
        internal Attack Attack;
        internal bool Attack2Held;
        internal int CycleCount;
        internal int Stacks;
        internal float BaseSpeedFactor;
        internal float BaseRayWidth;
        internal float BaseRayWidthExtra;
        internal float BaseRadius;
        internal float NextPulseAt;
        internal GameObject Visual;
        internal Quaternion BaseVisualRotation;
        internal Quaternion SpinVisualRotation;
        internal bool VisualRotationCaptured;
        internal float VisualAngle;
        internal float ProgrammaticStartAt;
        internal bool ProgrammaticStarted;
        internal int SpinAnimationStateHash;
        internal bool SpinAnimationCaptured;
        internal float SpinAnimationPhase;
    }

    internal static class Polearms70ContinuousSpinService
    {
        private const float BasePeriod = 0.92f;
        private static readonly ConditionalWeakTable<Player, Polearms70SpinState> States = new ConditionalWeakTable<Player, Polearms70SpinState>();

        internal static bool IsActive(Player player) => player != null && States.TryGetValue(player, out Polearms70SpinState state) &&
            (state.Phase == PolearmSpinPhase.Starting || state.Phase == PolearmSpinPhase.Spinning);

        // Read-only authority boundary for consumers; Starting is not a combat window.
        internal static bool TryGetConfirmedSpin(Player player, out Attack attack, out float radius)
        {
            attack = null; radius = 0f;
            if (player == null || player != Player.m_localPlayer || player.m_nview?.IsOwner() != true ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Polearms, 70) ||
                player.IsDead() || player.IsTeleporting() || player.InCutscene() || player.IsStaggering() || player.InDodge() ||
                !States.TryGetValue(player, out Polearms70SpinState state) ||
                state.Phase != PolearmSpinPhase.Spinning || state.CycleCount <= 0 || state.Attack == null ||
                player.GetCurrentWeapon() != state.Attack.GetWeapon()) return false;
            bool actualNativeAttack = player.m_currentAttack == state.Attack && player.InAttack();
            bool actualContinuousSpin = state.ProgrammaticStarted && ZInput.GetButton("SecondaryAttack");
            if (!actualNativeAttack && !actualContinuousSpin) return false;
            float currentRadius = state.BaseRadius * (1f + .20f * state.Stacks);
            if (!float.IsFinite(currentRadius) || currentRadius <= 0f || currentRadius > 32f) return false;
            attack = state.Attack; radius = currentRadius;
            return true;
        }
        internal static void Begin(Player player, Attack attack)
        {
            if (player == null || attack == null) return;
            if (IsActive(player)) return;
            Polearms70SpinState state = States.GetOrCreateValue(player);
            state.Phase = PolearmSpinPhase.Starting; state.Attack = attack; state.Attack2Held = true;
            state.CycleCount = 0; state.Stacks = 0; state.BaseSpeedFactor = attack.m_speedFactor;
            state.BaseRayWidth = attack.m_attackRayWidth; state.BaseRayWidthExtra = attack.m_attackRayWidthCharExtra;
            state.BaseRadius = Mathf.Max(2.2f, attack.m_attackRange + attack.m_attackRayWidth + attack.m_attackRayWidthCharExtra);
            state.NextPulseAt = float.MaxValue; state.VisualAngle = 0f;
            state.ProgrammaticStartAt = float.MaxValue; state.ProgrammaticStarted = false;
            state.SpinAnimationStateHash = 0; state.SpinAnimationCaptured = false; state.SpinAnimationPhase = 0f;
            if (player.m_visual != null) { state.BaseVisualRotation = player.m_visual.transform.localRotation; state.VisualRotationCaptured = true; }
            attack.m_loopingAttack = false;
            state.Visual = PolearmSpinVisualService.Start(player, state.BaseRadius);
            MasteryPlugin.Log.LogInfo("[Polearms70] phase=START mode=programmatic_aoe weapon=" + (attack.m_attackAnimation ?? "unknown"));
        }

        internal static bool BeforeTrigger(Player player, Attack attack)
        {
            if (player == null || attack == null || !States.TryGetValue(player, out Polearms70SpinState state) || state.Attack != attack || !IsActive(player)) return true;
            if (state.CycleCount > 0) return false;
            player.AddStamina(Mathf.Max(0f, attack.GetAttackStamina() * 0.70f));
            state.CycleCount = 1; state.Phase = PolearmSpinPhase.Spinning;
            if (player.m_animator != null)
            {
                AnimatorStateInfo animationState = player.m_animator.GetCurrentAnimatorStateInfo(0);
                state.SpinAnimationStateHash = animationState.fullPathHash;
                state.SpinAnimationCaptured = state.SpinAnimationStateHash != 0;
            }
            // Let the first vanilla spin finish instead of freezing it at its hit frame.
            state.ProgrammaticStartAt = Time.time + BasePeriod * 0.65f;
            state.NextPulseAt = float.MaxValue;
            PolearmSpinVisualService.Pulse(player, 0);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Polearms70] phase=FIRST_TRIGGER radius=" + state.BaseRadius.ToString("0.00"));
            return true;
        }

        private static void DoAreaPulse(Player player, Polearms70SpinState state)
        {
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon?.m_shared == null || state.Attack == null) return;
            float radius = state.BaseRadius * (1f + 0.20f * state.Stacks);
            HitData.DamageTypes damage = weapon.GetDamage();
            float skillFactor = player.GetSkills().GetRandomSkillFactor(Skills.SkillType.Polearms);
            damage.Modify(Mathf.Max(0f, state.Attack.m_damageMultiplier * skillFactor * 0.35f));
            Vector3 center = player.GetCenterPoint();
            int hits = 0;
            foreach (Character target in Character.GetAllCharacters())
            {
                if (target == null || target == player || target.IsDead() || target.IsPlayer() || !BaseAI.IsEnemy(player, target)) continue;
                if ((target.GetCenterPoint() - center).sqrMagnitude > Mathf.Pow(radius + target.GetRadius(), 2f)) continue;
                HitData hit = new HitData(); hit.m_skill = Skills.SkillType.Polearms; hit.m_damage = damage;
                hit.m_point = target.GetCenterPoint(); hit.m_dir = (target.GetCenterPoint() - center).normalized;
                hit.m_staggerMultiplier = Mathf.Max(0f, state.Attack.m_staggerMultiplier * 0.35f);
                hit.m_pushForce = Mathf.Max(0f, state.Attack.m_forceMultiplier * 8f); hit.SetAttacker(player);
                MasteryAttackTagService.Add(hit, MasteryAttackTag.Secondary);
                PerkHitContext context = PerkRuntimeService.GetHitContext(hit); context.IsPerkGenerated = true;
                context.PerkId = "polearms_70_spin"; context.AllowSelfProc = false; context.AllowOtherPerkProc = false;
                target.Damage(hit); hits++;
            }
            PolearmSpinVisualService.Pulse(player, state.Stacks);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Polearms70] phase=AOE stack=" + state.Stacks + " radius=" + radius.ToString("0.00") + " hits=" + hits);
        }

        internal static void Tick(Player player)
        {
            if (player == null || player != Player.m_localPlayer || !States.TryGetValue(player, out Polearms70SpinState state) || !IsActive(player)) return;
            bool valid = player.GetCurrentWeapon()?.m_shared?.m_skillType == Skills.SkillType.Polearms &&
                PerkRuntimeService.HasPerk(player, Skills.SkillType.Polearms, 70) && !player.IsDead() && !player.IsStaggering();
            state.Attack2Held = valid && ZInput.GetButton("SecondaryAttack");
            if (!state.Attack2Held) { Stop(player, "released_or_invalid"); return; }
            if (state.Phase != PolearmSpinPhase.Spinning) return;

            float speedMultiplier = 1f + 0.40f * state.Stacks;
            if (!state.ProgrammaticStarted)
            {
                if (Time.time < state.ProgrammaticStartAt) return;
                state.ProgrammaticStarted = true;
                state.VisualAngle = 0f;
                state.SpinAnimationPhase = 0f;
                state.NextPulseAt = Time.time + BasePeriod / speedMultiplier;
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Polearms70] phase=CONTINUOUS_BEGIN stacks=" + state.Stacks);
            }
            if (player.m_animator != null && state.SpinAnimationCaptured)
            {
                // Scrub the actual vanilla secondary-attack clip. This keeps the body and
                // polearm moving together and preserves the clip's original spin direction.
                player.m_animator.speed = 0f;
                state.SpinAnimationPhase = Mathf.Repeat(state.SpinAnimationPhase + Time.deltaTime * speedMultiplier / BasePeriod, 1f);
                player.m_animator.Play(state.SpinAnimationStateHash, 0, state.SpinAnimationPhase);
            }
            if (Time.time < state.NextPulseAt) return;

            float cost = Mathf.Max(0f, state.Attack.GetAttackStamina() * 0.30f);
            if (player.GetStamina() + 0.001f < cost) { Stop(player, "stamina"); return; }
            player.UseStamina(cost);
            state.Stacks = Mathf.Clamp(state.Stacks + 1, 0, 5); state.CycleCount++;
            DoAreaPulse(player, state);
            state.NextPulseAt = Time.time + BasePeriod / (1f + 0.40f * state.Stacks);
        }

        internal static void Stop(Player player, string reason)
        {
            if (player == null || !States.TryGetValue(player, out Polearms70SpinState state) || state.Phase == PolearmSpinPhase.Idle) return;
            state.Phase = PolearmSpinPhase.Stopping;
            if (player.m_animator != null) player.m_animator.speed = 1f;
            if (player.m_visual != null && state.VisualRotationCaptured) player.m_visual.transform.localRotation = state.BaseVisualRotation;
            if (state.Attack != null)
            {
                state.Attack.m_speedFactor = state.BaseSpeedFactor; state.Attack.m_attackRayWidth = state.BaseRayWidth;
                state.Attack.m_attackRayWidthCharExtra = state.BaseRayWidthExtra; state.Attack.Abort();
            }
            if (state.Visual != null) UnityEngine.Object.Destroy(state.Visual, 0.35f);
            state.Visual = null; state.Attack2Held = false; state.Attack = null; state.Phase = PolearmSpinPhase.Idle;
            if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogInfo("[Polearms70] phase=STOP reason=" + reason + " cycles=" + state.CycleCount);
        }

        internal static string GetDebugState(Player player)
        {
            if (player == null || !States.TryGetValue(player, out Polearms70SpinState state)) return "State=Idle";
            return "State=" + state.Phase + " Held=" + state.Attack2Held + " Cycles=" + state.CycleCount + " Stacks=" + state.Stacks +
                " Next=" + Mathf.Max(0f, state.NextPulseAt - Time.time).ToString("0.00") + " Radius=" + (state.BaseRadius * (1f + 0.20f * state.Stacks)).ToString("0.00") +
                " Stamina=" + player.GetStamina().ToString("0.0") + " VFX=" + (state.Visual != null);
        }
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    [HarmonyPriority(Priority.First)]
    internal static class Polearms70StartPatch
    {
        private static void Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon)
        {
            Player player = character as Player;
            if (player == null || player != Player.m_localPlayer || weapon?.m_shared?.m_skillType != Skills.SkillType.Polearms ||
                !(AttackIntentService.IsSecondary(__instance, player) || ZInput.GetButton("SecondaryAttack")) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Polearms, 70)) return;
            if (!Polearms70ContinuousSpinService.IsActive(player))
                Polearms70ContinuousSpinService.Begin(player, __instance);
        }
    }

    // Holding secondary makes Valheim request a new Attack after the vanilla clip ends.
    // Keep the current continuous-spin state instead of resetting its stacks.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    [HarmonyPriority(Priority.First)]
    internal static class Polearms70RepeatedStartGuardPatch
    {
        private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
        {
            if (secondaryAttack && __instance is Player player && player == Player.m_localPlayer &&
                Polearms70ContinuousSpinService.IsActive(player))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    [HarmonyPriority(Priority.First)]
    internal static class Polearms70TriggerPatch
    {
        private static bool Prefix(Attack __instance)
        {
            Player player = __instance?.m_character as Player;
            if (player == null || __instance.m_weapon?.m_shared?.m_skillType != Skills.SkillType.Polearms) return true;
            return Polearms70ContinuousSpinService.BeforeTrigger(player, __instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class Polearms70UpdatePatch
    {
        private static void Postfix(Player __instance) => Polearms70ContinuousSpinService.Tick(__instance);
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackSpeedFactorMovement))]
    internal static class Polearms70WalkSpeedPatch
    {
        private static void Postfix(Humanoid __instance, ref float __result)
        {
            if (__instance is Player player && Polearms70ContinuousSpinService.IsActive(player)) __result = 1f;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackSpeedFactorRotation))]
    internal static class Polearms70RotationSpeedPatch
    {
        private static void Postfix(Humanoid __instance, ref float __result)
        {
            if (__instance is Player player && Polearms70ContinuousSpinService.IsActive(player)) __result = Mathf.Max(__result, 0.85f);
        }
    }
    internal static class Crafting70Service
    {
        internal const string StabilityKey = "valheim_mastery.crafting70_stability";
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.OnPlaced))]
    internal static class Crafting70PlacedPiecePatch
    {
        private static void Postfix(Piece __instance)
        {
            Player player = Player.m_localPlayer;
            if (__instance?.m_nview?.GetZDO() == null || player == null || __instance.GetCreator() != player.GetPlayerID() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70)) return;
            __instance.m_nview.GetZDO().Set(Crafting70Service.StabilityKey, 1);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.GetSupport))]
    internal static class Crafting70StructureSupportPatch
    {
        private static void Postfix(WearNTear __instance, ref float __result)
        {
            if (__instance == null) return;
            if (__instance.m_nview?.GetZDO()?.GetInt(Crafting70Service.StabilityKey, 0) != 1) return;
            float min = __instance.GetMinSupport();
            float max = __instance.GetMaxSupport();
            // No support at all is not a foundation. Preserve zero rather than
            // advertising a fictitious supported piece to neighbouring pieces.
            if (__result > 0f)
                __result = Mathf.Clamp(__result + Mathf.Max(0f, max - min) * 0.20f, 0f, max);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.HaveSupport))]
    internal static class Crafting70OwnStructureSupportPatch
    {
        private static void Postfix(WearNTear __instance, ref bool __result)
        {
            if (__instance?.m_nview?.GetZDO()?.GetInt(Crafting70Service.StabilityKey, 0) != 1) return;
            // Native HaveSupport reads raw m_support, while neighbours use
            // GetSupport. Both must agree on the same persistent perk bonus.
            __result = __instance.GetSupport() >= __instance.GetMinSupport();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class Polearms70SpinDamagePatch
    {
        private static void Prefix(HitData hit)
        {
            Player attacker = hit?.GetAttacker() as Player;
            if (attacker == null || hit.m_skill != Skills.SkillType.Polearms || PerkRuntimeService.IsPerkGenerated(hit) || !Polearms70ContinuousSpinService.IsActive(attacker)) return;
            hit.m_damage.Modify(0.35f);
            hit.m_staggerMultiplier *= 0.35f;
        }
    }}
