using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class Polearm35SpinState
    {
        internal float StartAt;
        internal float ExpiresAt;
        internal bool Pending;
        internal ItemDrop.ItemData TriggerWeapon;
        internal Character Attacker;
        internal bool WasTargetStaggeredByParry;
        internal float MobilityUntil;
    }

    // Immutable decision data captured during the BlockAttack call itself. The delayed
    // follow-up must never recalculate this from a later target animation state.
    internal sealed class PolearmParryContext
    {
        internal Character Target;
        internal bool WasTargetStaggeredByParry;
        internal float TriggerTime;
    }

    internal static class Polearm35ParryResolutionService
    {
        private sealed class ResolutionState
        {
            internal Player Player;
            internal Character Target;
            internal bool TargetWasAlreadyStaggering;
            internal bool StaggerTriggered;
        }

        [System.ThreadStatic] private static ResolutionState Active;

        internal static void CancelResolution() => Active = null;

        internal static void Begin(Player player, Character target, bool isPerfectParry)
        {
            ItemDrop.ItemData weapon = player?.GetCurrentWeapon();
            Active = isPerfectParry && weapon?.m_shared?.m_skillType == Skills.SkillType.Polearms &&
                     PerkRuntimeService.HasPerk(player, Skills.SkillType.Polearms, 35)
                ? new ResolutionState
                {
                    Player = player,
                    Target = target,
                    TargetWasAlreadyStaggering = target != null && target.IsStaggering()
                }
                : null;
        }

        // Both Character.Stagger and AddStaggerDamage are observed because Valheim attacks
        // use either route. They occur inside the same vanilla BlockAttack resolution.
        internal static void ObserveStagger(Character target)
        {
            if (Active != null && Active.Target == target && !Active.TargetWasAlreadyStaggering)
                Active.StaggerTriggered = true;
        }

        internal static PolearmParryContext End(bool blockSucceeded)
        {
            ResolutionState state = Active;
            Active = null;
            if (!blockSucceeded || state == null || state.Player == null) return null;

            // This is still the BlockAttack postfix frame, not the later queued attack frame.
            bool staggeredNow = state.Target != null && state.Target.IsStaggering();
            return new PolearmParryContext
            {
                Target = state.Target,
                WasTargetStaggeredByParry = !state.TargetWasAlreadyStaggering && (state.StaggerTriggered || staggeredNow),
                TriggerTime = Time.time
            };
        }
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class Polearm35ResolutionFailureCleanupPatch
    {
        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception != null) Polearm35ParryResolutionService.CancelResolution();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Stagger))]
    [HarmonyPriority(Priority.Last)]
    internal static class Polearm35StaggerResolutionPatch
    {
        private static void Prefix(Character __instance) => Polearm35ParryResolutionService.ObserveStagger(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddStaggerDamage))]
    [HarmonyPriority(Priority.Last)]
    internal static class Polearm35StaggerOverflowResolutionPatch
    {
        private static void Postfix(Character __instance, bool __result)
        {
            if (__result) Polearm35ParryResolutionService.ObserveStagger(__instance);
        }
    }

    internal static class Polearm35AutoSpinService
    {
        internal static void Queue(Player player, PolearmParryContext context)
        {
            ItemDrop.ItemData weapon = player?.GetCurrentWeapon();
            if (player == null || player != Player.m_localPlayer || weapon?.m_shared?.m_skillType != Skills.SkillType.Polearms || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Polearms, 35)) return;
            bool staggeredAtParry = context != null && context.WasTargetStaggeredByParry;
            if (!staggeredAtParry)
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                {
                    string target = context?.Target != null ? context.Target.gameObject.name : "none";
                    float stamina = player.GetStamina();
                    MasteryPlugin.Log.LogInfo("[Polearms35] target=" + target + " staggeredAtParry=false branch=NONE");
                    MasteryPlugin.Log.LogInfo("[Polearms35] attackStarted=false attackType=NONE staminaBefore=" +
                        stamina.ToString("0.0") + " staminaAfter=" + stamina.ToString("0.0"));
                }
                return;
            }
            Polearm35SpinState state = MasteryStateStore.GetPlayerState<Polearm35SpinState>(player);
            state.StartAt = Time.time + 0.08f;
            state.ExpiresAt = Time.time + 1.50f;
            state.TriggerWeapon = weapon;
            state.Attacker = context?.Target;
            state.WasTargetStaggeredByParry = true;
            state.Pending = true;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
            {
                string target = state.Attacker != null ? state.Attacker.gameObject.name : "none";
                MasteryPlugin.Log.LogInfo("[Polearms35] target=" + target + " staggeredAtParry=" + state.WasTargetStaggeredByParry +
                    " branch=SPIN");
            }
        }

        internal static void Update(Player player)
        {
            if (player == null || player != Player.m_localPlayer || !MasteryStateStore.TryGetPlayerState<Polearm35SpinState>(player, out Polearm35SpinState state) || !state.Pending) return;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (Time.time >= state.ExpiresAt || player.IsDead() || player.GetHealth() <= 0f ||
                player.IsTeleporting() || player.InCutscene() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Polearms, 35) ||
                weapon?.m_shared?.m_skillType != Skills.SkillType.Polearms || weapon != state.TriggerWeapon)
            {
                ClearPending(state);
                return;
            }
            if (Time.time < state.StartAt) return;

            // The stored parry snapshot is the sole branch selector. Do not query Target
            // state here: it may have changed while waiting for vanilla's attack slot.
            Attack secondary = weapon.m_shared.m_secondaryAttack;
            ClearPending(state);
            if (secondary == null) return;
            HitData.DamageTypes damage = weapon.GetDamage();
            float skillFactor = player.GetSkills().GetRandomSkillFactor(Skills.SkillType.Polearms);
            damage.Modify(Mathf.Max(0f, secondary.m_damageMultiplier * skillFactor));
            float radius = Mathf.Max(2.2f, secondary.m_attackRange + secondary.m_attackRayWidth + secondary.m_attackRayWidthCharExtra);
            Vector3 center = player.GetCenterPoint();
            int hits = 0;
            foreach (Character target in Character.GetAllCharacters())
            {
                if (target == null || target == player || target.IsDead() || target.IsPlayer() || !BaseAI.IsEnemy(player, target)) continue;
                float reach = radius + target.GetRadius();
                if ((target.GetCenterPoint() - center).sqrMagnitude > reach * reach) continue;
                HitData hit = new HitData(); hit.m_skill = Skills.SkillType.Polearms; hit.m_damage = damage;
                hit.m_point = target.GetCenterPoint(); hit.m_dir = (target.GetCenterPoint() - center).normalized;
                hit.m_staggerMultiplier = secondary.m_staggerMultiplier;
                hit.m_pushForce = Mathf.Max(0f, secondary.m_forceMultiplier * 8f); hit.SetAttacker(player);
                MasteryAttackTagService.Add(hit, MasteryAttackTag.Secondary);
                PerkHitContext generated = PerkRuntimeService.GetHitContext(hit);
                generated.IsPerkGenerated = true; generated.PerkId = "polearms_35_phantom";
                generated.AllowSelfProc = false; generated.AllowOtherPerkProc = false;
                target.Damage(hit); hits++;
            }
            PolearmBurstVisualService.Play(player, radius);
            PerkAudioService.Play("polearms_35_wind_cut", "sfx_atgeir_attack_secondary", center, 0.60f);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Polearms35] phantom=true radius=" + radius.ToString("0.00") + " hits=" + hits);
        }

        private static void ClearPending(Polearm35SpinState state)
        {
            state.Pending = false;
            state.TriggerWeapon = null;
            state.Attacker = null;
            state.WasTargetStaggeredByParry = false;
        }

        internal static bool HasMobility(Player player) => player != null && MasteryStateStore.TryGetPlayerState<Polearm35SpinState>(player, out Polearm35SpinState state) && state.MobilityUntil > Time.time;
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Polearm35AutoSpinUpdatePatch { private static void Postfix(Player __instance) => Polearm35AutoSpinService.Update(__instance); }

    [HarmonyPatch(typeof(Character), nameof(Character.GetAttackSpeedFactorMovement))]
    internal static class PolearmAttackMobilityPatch
    {
        private static void Postfix(Character __instance, ref float __result)
        {
            Player player = __instance as Player;
            ItemDrop.ItemData weapon = player?.GetCurrentWeapon();
            bool polearmSecondary = player != null && weapon?.m_shared?.m_skillType == Skills.SkillType.Polearms &&
                (AttackIntentService.IsSecondary(player.m_currentAttack, player) || Polearm35AutoSpinService.HasMobility(player) || Polearms70ContinuousSpinService.IsActive(player));
            if (polearmSecondary) __result = Mathf.Max(1f, __result);
        }
    }
}
