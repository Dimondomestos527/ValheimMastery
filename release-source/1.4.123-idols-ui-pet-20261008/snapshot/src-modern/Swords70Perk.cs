using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class SwordOffensiveParryState
    {
        internal Attack CurrentSwordAttack;
        internal float StartTime;
        internal float ValidUntil;
        internal bool OffensiveParryTriggered;
        internal bool SwordAttackCancelled;
        internal string CurrentAttack = "none";
        internal string IncomingAttack = "none";
        internal float TimingDelta;
        internal bool Eligible;
        internal float StaggerMultiplier = 1f;
    }

    internal static class SwordQuickSecondaryBalance
    {
        // The animation event is the actual hit trigger. Speeding this concrete Attack's
        // CharacterAnimEvent keeps visible motion and resolved-hit timing together.
        internal const float AnimationSpeed = 1.20f;
        internal const float DamageMultiplier = 1.35f;
        internal const float StaminaMultiplier = 1f;
        internal const float StaggerMultiplier = 1.75f;
    }

    internal static class SwordOffensiveParryService
    {
        internal const float TimingWindow = 0.35f;
        private static readonly ConditionalWeakTable<Player, SwordOffensiveParryState> States =
            new ConditionalWeakTable<Player, SwordOffensiveParryState>();

        internal static bool DebugEnabled { get; private set; }

        internal static SwordOffensiveParryState For(Player player) => States.GetOrCreateValue(player);

        internal static void Arm(Player player, Attack attack)
        {
            if (player == null || attack == null) return;
            SwordOffensiveParryState state = For(player);
            state.CurrentSwordAttack = attack;
            state.StartTime = Time.time;
            state.ValidUntil = state.StartTime + TimingWindow;
            state.OffensiveParryTriggered = false;
            state.SwordAttackCancelled = false;
            state.CurrentAttack = attack.m_attackAnimation ?? "sword";
            state.IncomingAttack = "none";
            state.TimingDelta = 0f;
            state.Eligible = false;
            state.StaggerMultiplier = 1f;
            Log(player, state, "ARMED");
        }

        internal static bool TryParry(Player player, HitData hit)
        {
            if (player == null || hit == null || !States.TryGetValue(player, out SwordOffensiveParryState state)) return false;

            Character attacker = hit.GetAttacker();
            state.TimingDelta = Time.time - state.StartTime;
            state.IncomingAttack = DescribeIncoming(hit, attacker);
            state.Eligible = IsEligible(player, hit, attacker, state);
            if (!state.Eligible)
            {
                Log(player, state, "REJECTED");
                return false;
            }

            state.ValidUntil = 0f;
            state.OffensiveParryTriggered = true;
            state.StaggerMultiplier = 1.5f;

            // Offensive Parry belongs to the already-running Sword attack. Calling
            // Humanoid.BlockAttack here can select an equipped shield and transition the player
            // into normal blocking, so neutralize only this concrete incoming melee HitData.
            hit.m_damage.Modify(0f);
            hit.m_statusEffectHash = 0;
            hit.m_pushForce = 0f;
            hit.m_staggerMultiplier = 0f;

            // Match vanilla perfect-block target behavior. Enemies that vanilla marks as immune
            // to block stagger remain immune.
            if (attacker != null && attacker.m_staggerWhenBlocked)
                attacker.Stagger(-hit.m_dir);

            SwordPerkFeedback.PlayOffensiveParry(player, attacker, hit.m_point);
            Log(player, state, "TRIGGERED");
            return true;
        }

        internal static bool TryApplyContinuingStrike(Player player, HitData hit)
        {
            if (player == null || hit == null || !States.TryGetValue(player, out SwordOffensiveParryState state) ||
                !state.OffensiveParryTriggered || state.CurrentSwordAttack == null ||
                player.m_currentAttack != state.CurrentSwordAttack ||
                !PerkRuntimeService.TryMarkApplied(hit, "swords_70_offensive_parry_stagger")) return false;

            hit.m_staggerMultiplier *= 1.5f;
            state.StaggerMultiplier = 1.5f;
            state.OffensiveParryTriggered = false;
            Log(player, state, "CONTINUING_STRIKE");
            return true;
        }

        internal static void Observe(Player player)
        {
            if (player == null || !States.TryGetValue(player, out SwordOffensiveParryState state) || state.CurrentSwordAttack == null) return;
            if (player.m_currentAttack == state.CurrentSwordAttack) return;

            if (Time.time <= state.ValidUntil)
            {
                state.SwordAttackCancelled = true;
                Log(player, state, "ATTACK_CANCELLED");
            }
            state.CurrentSwordAttack = null;
            state.ValidUntil = 0f;
            state.OffensiveParryTriggered = false;
        }

        internal static void SetDebug(bool enabled)
        {
            DebugEnabled = enabled;
            if (enabled && Player.m_localPlayer != null) Log(Player.m_localPlayer, For(Player.m_localPlayer), "DEBUG_ENABLED");
        }

        internal static string DebugSummary(Player player)
        {
            if (player == null) return "Sword70: player unavailable.";
            SwordOffensiveParryState state = For(player);
            bool active = state.CurrentSwordAttack != null && player.m_currentAttack == state.CurrentSwordAttack && Time.time <= state.ValidUntil;
            return "SwordAttackActive=" + active +
                " CurrentAttack=" + state.CurrentAttack +
                " AttackStartTime=" + state.StartTime.ToString("0.000") +
                " IncomingAttack=" + state.IncomingAttack +
                " TimingDelta=" + state.TimingDelta.ToString("0.000") +
                " Eligible=" + state.Eligible +
                " OffensiveParryTriggered=" + state.OffensiveParryTriggered +
                " SwordAttackCancelled=" + state.SwordAttackCancelled +
                " StaggerMultiplier=" + state.StaggerMultiplier.ToString("0.00");
        }

        private static bool IsEligible(Player player, HitData hit, Character attacker, SwordOffensiveParryState state)
        {
            if (!PerkRuntimeService.HasPerk(player, Skills.SkillType.Swords, 70) || player.IsDead() || player.IsStaggering()) return false;
            // This prefix runs before vanilla RPC_Damage's early returns. An ignored
            // or non-owned hit must not spend the parry window or stagger its sender.
            if (player.m_nview == null || !player.m_nview.IsOwner() || player.GetHealth() <= 0f ||
                player.IsDebugFlying() || player.IsTeleporting() || player.InCutscene() ||
                (hit.m_dodgeable && player.IsDodgeInvincible())) return false;
            if (state.CurrentSwordAttack == null || player.m_currentAttack != state.CurrentSwordAttack || Time.time > state.ValidUntil) return false;
            if (player.GetCurrentWeapon()?.m_shared?.m_skillType != Skills.SkillType.Swords) return false;
            if (attacker == null || attacker == player || attacker.IsDead()) return false;
            if (attacker.IsPlayer() && !player.IsPVPEnabled() && !hit.m_ignorePVP) return false;
            if (!hit.m_blockable || hit.m_ranged || hit.m_radius > 0.01f) return false;
            if (hit.m_hitType != HitData.HitType.EnemyHit && hit.m_hitType != HitData.HitType.PlayerHit && hit.m_hitType != HitData.HitType.Undefined) return false;
            HitData.DamageTypes damage = hit.m_damage;
            float physical = damage.m_blunt + damage.m_slash + damage.m_pierce + damage.m_chop + damage.m_pickaxe;
            if (physical <= 0.001f) return false;
            float maximumMeleeReach = attacker.GetRadius() + player.GetRadius() + 6f;
            return (attacker.GetCenterPoint() - player.GetCenterPoint()).sqrMagnitude <= maximumMeleeReach * maximumMeleeReach;
        }

        private static string DescribeIncoming(HitData hit, Character attacker)
        {
            string source = attacker != null ? attacker.gameObject.name.Replace("(Clone)", "") : "none";
            return source + ":" + hit.m_hitType + (hit.m_ranged ? ":ranged" : ":melee");
        }

        private static void Log(Player player, SwordOffensiveParryState state, string phase)
        {
            if (!DebugEnabled && !MasteryPlugin.Settings.VerboseLogging.Value) return;
            MasteryPlugin.Log.LogInfo("[Sword70] Phase=" + phase + " Player=" + (player?.GetPlayerName() ?? "none") + " " + DebugSummary(player));
        }
    }

    internal static class SwordPerkFeedback
    {
        internal static void PlaySecondaryStart(Player player)
        {
            if (player != null) PerkAudioService.Play("swords_35_swoosh", "sfx_sword_swing", player.GetCenterPoint(), 0.28f);
        }

        internal static void PlaySecondaryImpact(Character target, bool staggered)
        {
            if (target == null) return;
            Vector3 point = target.GetCenterPoint();
            MasteryVfxMaterial.SpawnPrefab("vfx_HitSparks", point, staggered ? 0.85f : 0.55f);
            PerkAudioService.Play(staggered ? "swords_35_staggered_snap" : "swords_35_impact", "sfx_sword_hit", point, staggered ? 0.52f : 0.30f);
        }

        internal static void PlayOffensiveParry(Player player, Character attacker, Vector3 hitPoint)
        {
            Vector3 point = hitPoint;
            if (point == Vector3.zero)
                point = attacker != null ? Vector3.Lerp(player.GetCenterPoint(), attacker.GetCenterPoint(), 0.45f) : player.GetCenterPoint();
            MasteryVfxMaterial.SpawnPrefab("vfx_perfectblock", point, 0.60f);
            MasteryVfxMaterial.SpawnPrefab("vfx_HitSparks", point, 0.78f);
            PerkAudioService.Play("swords_70_offensive_parry", "sfx_perfectblock", point, 0.62f);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    [HarmonyPriority(Priority.Last)]
    internal static class SwordsMilestoneAttackStartPatch
    {
        private static void Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon, out bool __state)
        {
            // Attack.Start assigns m_character and m_weapon inside the original method, after all
            // prefixes have already run. Use Harmony-bound arguments here; reading the fields in a
            // prefix was the reason the previous Sword speed patch silently never activated.
            Player player = character as Player;
            __state = player != null && weapon?.m_shared?.m_skillType == Skills.SkillType.Swords &&
                AttackIntentService.IsSecondary(__instance, player) &&
                PerkRuntimeService.HasPerk(player, Skills.SkillType.Swords, 35);
            if (__state) __instance.m_attackStamina *= SwordQuickSecondaryBalance.StaminaMultiplier;
        }

        private static void Postfix(Attack __instance, bool __result, bool __state)
        {
            Player player = __instance?.m_character as Player;
            if (__result && __state)
            {
                // Capture this attack only. Native Speed events set each phase's tempo;
                // the event patch scales those values without flattening the clip.
                Swords35SecondaryAnimationSpeedPatch.Mark(__instance.m_animEvent, __instance);
                SwordPerkFeedback.PlaySecondaryStart(player);
            }
            if (!__result || player == null || __instance.m_weapon?.m_shared?.m_skillType != Skills.SkillType.Swords ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Swords, 70)) return;
            SwordOffensiveParryService.Arm(player, __instance);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.Last)]
    internal static class SwordResolvedHitPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Player player = hit?.GetAttacker() as Player;
            if (__instance == null || hit == null || player == null || hit.m_skill != Skills.SkillType.Swords ||
                player.GetCurrentWeapon()?.m_shared?.m_skillType != Skills.SkillType.Swords || PerkRuntimeService.IsPerkGenerated(hit)) return;

            bool secondary = MasteryAttackTagService.Has(hit, MasteryAttackTag.Secondary) ||
                AttackIntentService.IsSecondary(player.m_currentAttack, player);
            bool staggered = __instance.IsStaggering();

            if (staggered && PerkRuntimeService.TryMarkApplied(hit, "sword_passive_staggered_damage"))
            {
                hit.ApplyModifier(1.25f);
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[SwordPassive] Target=" + __instance.gameObject.name + " Staggered=true Multiplier=1.25");
            }

            bool quickSecondaryApplied = secondary && PerkRuntimeService.HasPerk(player, Skills.SkillType.Swords, 35) &&
                PerkRuntimeService.TryMarkApplied(hit, "swords_35_quick_secondary");
            if (quickSecondaryApplied)
            {
                hit.ApplyModifier(SwordQuickSecondaryBalance.DamageMultiplier);
                hit.m_staggerMultiplier *= SwordQuickSecondaryBalance.StaggerMultiplier;
            }

            SwordOffensiveParryService.TryApplyContinuingStrike(player, hit);
            if (quickSecondaryApplied)
                SwordPerkFeedback.PlaySecondaryImpact(__instance, staggered);
        }
    }

    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
    [HarmonyPriority(Priority.Last)]
    internal static class Swords35SecondaryAnimationSpeedPatch
    {
        private sealed class ActiveSwordSpeed { internal Attack Attack; internal float Started; }
        private static readonly ConditionalWeakTable<CharacterAnimEvent, ActiveSwordSpeed> Active =
            new ConditionalWeakTable<CharacterAnimEvent, ActiveSwordSpeed>();

        internal static void Mark(CharacterAnimEvent animEvent, Attack attack)
        {
            if (animEvent == null || attack == null) return;
            var state = Active.GetOrCreateValue(animEvent);
            state.Attack = attack; state.Started = Time.time;
        }

        internal static float ScaleNativeSpeed(float speed) => speed * SwordQuickSecondaryBalance.AnimationSpeed;

        internal static void ScaleEvent(CharacterAnimEvent animEvent, ref float speed)
        {
            if (animEvent == null || !Active.TryGetValue(animEvent, out ActiveSwordSpeed state)) return;
            Player player = animEvent.m_character as Player;
            if (player == null || player.m_currentAttack != state.Attack ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Swords, 35)) return;
            // Speed is an absolute value provided by the animation clip, NOT a
            // multiplier. Preserve slow wind-up/recovery events (e.g. .5 -> .6).
            // Never overwrite the whole clip with 1.20 every fixed update.
            speed = ScaleNativeSpeed(speed);
        }

        private static void Postfix(CharacterAnimEvent __instance)
        {
            if (__instance == null || !Active.TryGetValue(__instance, out ActiveSwordSpeed state)) return;
            Player player = __instance?.m_character as Player;
            Attack attack = state.Attack;
            if (player == null || (!player.InAttack() && Time.time - state.Started > 0.35f) || player.m_currentAttack != attack ||
                player.GetCurrentWeapon()?.m_shared?.m_skillType != Skills.SkillType.Swords ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Swords, 35))
            {
                // Another attack may already have taken control of this animator.
                // Do not reset that attack's speed from an old sword state.
                bool canReset = player == null || player.m_currentAttack == null || player.m_currentAttack == attack;
                Active.Remove(__instance);
                if (canReset)
                {
                    if (__instance.m_pauseTimer > 0f) __instance.m_pauseSpeed = 1f;
                    else __instance.Speed(1f);
                }
                return;
            }

            // Vanilla animation events and FreezeFrame own the current speed.
        }
    }

    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
    internal static class Swords35NativeSpeedEventPatch
    {
        private static void Prefix(CharacterAnimEvent __instance, ref float speedScale) =>
            Swords35SecondaryAnimationSpeedPatch.ScaleEvent(__instance, ref speedScale);
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    [HarmonyPriority(Priority.First)]
    internal static class SwordOffensiveParryIncomingPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance is Player player) SwordOffensiveParryService.TryParry(player, hit);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class SwordOffensiveParryStateCleanupPatch
    {
        private static void Postfix(Player __instance)
        {
            SwordOffensiveParryService.Observe(__instance);
        }
    }
}
