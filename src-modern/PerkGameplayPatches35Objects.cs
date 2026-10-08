using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Axe35Service
    {
        internal const string ChopChopId = "chop_chop";
        internal const float Duration = 180f;
        internal const int MaxStacks = 3;
        internal const float VulnerabilityPerStack = 0.11f;

        internal static bool IsAxeHit(Player player, HitData hit)
        {
            if (player == null || hit == null) return false;
            if (hit.m_skill == Skills.SkillType.Axes) return true;
            // Never classify an explicitly typed projectile/spell/tool hit from the
            // weapon currently held when the delayed hit reaches the server. Without
            // this guard an arrow could be mistaken for an axe hit after a weapon swap
            // and roll the Axe 70 execution-ready state on a gouged target.
            if (hit.m_skill != Skills.SkillType.None && hit.m_skill != Skills.SkillType.WoodCutting) return false;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon?.m_shared == null) return false;
            Skills.SkillType skill = weapon.m_shared.m_skillType;
            return (skill == Skills.SkillType.Axes || skill == Skills.SkillType.WoodCutting) && weapon.GetDamage().m_chop > 0f;
        }

        internal static void ApplyHit(Player player, Character target, HitData hit)
        {
            if (player == null || target == null || target.IsDead() || hit == null || !IsAxeHit(player, hit) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Axes, 35) || PerkRuntimeService.IsPerkGenerated(hit)) return;
            int stacks = 1;
            if (TargetEffectService.TryGet(target, ChopChopId, out TargetEffect existing)) stacks = Mathf.Min(MaxStacks, existing.Stacks + 1);
            TargetEffectService.Apply(target, ChopChopId, player, stacks, stacks * VulnerabilityPerStack, Duration);
            AxeTargetVisualService.SendStacks(player, target, stacks, Duration);
        }

        internal static bool TryGet(Character target, out TargetEffect effect) => TargetEffectService.TryGet(target, ChopChopId, out effect);
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.Last)]
    internal static class Axe35ChopChopApplyPatch
    {
        private static void Postfix(Character __instance, HitData hit)
        {
            Axe35Service.ApplyHit(hit?.GetAttacker() as Player, __instance, hit);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class Axe35ChopChopVulnerabilityPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null || !Axe35Service.TryGet(__instance, out TargetEffect effect) || !IsTeamCombatHit(hit)) return;
            // Gouges expose flesh: they amplify slash only, not blunt, pierce,
            // elemental or tool damage. Three stacks therefore grant +33% slash.
            hit.m_damage.m_slash *= 1f + effect.Strength;
        }

        private static bool IsTeamCombatHit(HitData hit)
        {
            Character attacker = hit.GetAttacker();
            if (attacker is Player) return true;
            MonsterAI ai = attacker != null ? attacker.GetComponent<MonsterAI>() : null;
            return ai?.GetFollowTarget()?.GetComponent<Player>() != null;
        }
    }

    internal static class AxeGeometryRules
    {
        internal static bool IsHeavyAxe(ItemDrop.ItemData weapon)
        {
            string id = ((weapon?.m_dropPrefab != null ? weapon.m_dropPrefab.name : "") + " " + (weapon?.m_shared?.m_name ?? "")).ToLowerInvariant();
            return id.Contains("battleaxe") || id.Contains("crystalbattleaxe");
        }

        internal static float HorizontalFactor(float level, bool heavySecondary) =>
            1f + Mathf.Clamp(level, 0f, 100f) * (heavySecondary ? 0.0025f : 0.0030f);

        internal static float ForwardFactor(float level, bool heavySecondary) =>
            1f + Mathf.Clamp(level, 0f, 100f) * (heavySecondary ? 0.0005f : 0.0008f);
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    internal static class AxeSkillGeometryPatch
    {
        private sealed class State { internal float Width; internal float ExtraWidth; internal float Range; }
        private static void Prefix(Attack __instance, out State __state)
        {
            __state = null;
            Player player = __instance?.m_character as Player;
            if (player == null || __instance.m_weapon?.m_shared?.m_skillType != Skills.SkillType.Axes) return;
            float level = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Axes);
            bool heavySecondary = AxeGeometryRules.IsHeavyAxe(__instance.m_weapon) && AttackIntentService.IsSecondary(__instance, player);
            __state = new State { Width = __instance.m_attackRayWidth, ExtraWidth = __instance.m_attackRayWidthCharExtra, Range = __instance.m_attackRange };
            float horizontal = AxeGeometryRules.HorizontalFactor(level, heavySecondary);
            __instance.m_attackRayWidth *= horizontal;
            __instance.m_attackRayWidthCharExtra *= horizontal;
            __instance.m_attackRange *= AxeGeometryRules.ForwardFactor(level, heavySecondary);
            AxeGeometryAuditService.Begin(__instance, false, __state.Range, __state.Width, __state.ExtraWidth);
        }
        private static void Postfix(Attack __instance, State __state) { AxeGeometryAuditService.End(__instance); Restore(__instance, __state); }
        private static Exception Finalizer(Attack __instance, State __state, Exception __exception) { AxeGeometryAuditService.End(__instance); Restore(__instance, __state); return __exception; }
        private static void Restore(Attack attack, State state)
        {
            if (attack == null || state == null) return;
            attack.m_attackRayWidth = state.Width; attack.m_attackRayWidthCharExtra = state.ExtraWidth; attack.m_attackRange = state.Range;
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class AxeSkillAreaGeometryPatch
    {
        private sealed class State { internal float Width; internal float ExtraWidth; internal float Range; }
        private static void Prefix(Attack __instance, out State __state)
        {
            __state = null;
            Player player = __instance?.m_character as Player;
            if (player == null || __instance.m_weapon?.m_shared?.m_skillType != Skills.SkillType.Axes) return;
            float level = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Axes);
            bool heavySecondary = AxeGeometryRules.IsHeavyAxe(__instance.m_weapon) && AttackIntentService.IsSecondary(__instance, player);
            __state = new State { Width = __instance.m_attackRayWidth, ExtraWidth = __instance.m_attackRayWidthCharExtra, Range = __instance.m_attackRange };
            float horizontal = AxeGeometryRules.HorizontalFactor(level, heavySecondary);
            // DoAreaAttack uses attackRange to PLACE the sphere. Scaling it caused the live
            // battleaxe hit volume to jump away from the player. Only the radius is scaled.
            __instance.m_attackRayWidth *= horizontal;
            __instance.m_attackRayWidthCharExtra *= horizontal;
            AxeGeometryAuditService.Begin(__instance, true, __state.Range, __state.Width, __state.ExtraWidth);
        }
        private static void Postfix(Attack __instance, State __state) { AxeGeometryAuditService.End(__instance); Restore(__instance, __state); }
        private static Exception Finalizer(Attack __instance, State __state, Exception __exception) { AxeGeometryAuditService.End(__instance); Restore(__instance, __state); return __exception; }
        private static void Restore(Attack attack, State state)
        {
            if (attack == null || state == null) return;
            attack.m_attackRayWidth = state.Width; attack.m_attackRayWidthCharExtra = state.ExtraWidth; attack.m_attackRange = state.Range;
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    [HarmonyPriority(Priority.Last)]
    internal static class Axe35SpecialStartSpeedPatch
    {
        private static void Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon)
        {
            Player player = character as Player;
            if (player != null && weapon?.m_shared?.m_skillType == Skills.SkillType.Axes &&
                (AttackIntentService.IsSecondary(__instance, player) || player.m_currentAttackIsSecondary) &&
                PerkRuntimeService.HasPerk(player, Skills.SkillType.Axes, 35))
                __instance.m_speedFactor *= 1f + 0.005f * Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Axes), 0f, 100f);
        }
    }    internal sealed class Riding35State { internal bool HasSprinted; internal long Rider; }

    [HarmonyPatch(typeof(Sadle), "UpdateStamina")]
    internal static class Riding35StaminaRegenPatch
    {
        private static readonly ConditionalWeakTable<Sadle, Riding35State> States = new ConditionalWeakTable<Sadle, Riding35State>();

        private static void Prefix(Sadle __instance, out float __state)
        {
            __state = __instance?.GetStamina() ?? 0f;
            if (__instance != null)
            {
                Riding35State state = States.GetOrCreateValue(__instance);
                long rider = __instance.GetUser();
                if (state.Rider != rider) { state.Rider = rider; state.HasSprinted = false; }
            }
            if (__instance != null && __instance.m_speed == Sadle.Speed.Run)
                States.GetOrCreateValue(__instance).HasSprinted = true;
        }

        private static void Postfix(Sadle __instance, float __state)
        {
            if (__instance == null || __instance.m_speed == Sadle.Speed.Run)
                return;
            Riding35State state = States.GetOrCreateValue(__instance);
            if (!state.HasSprinted)
                return;
            Player rider = Player.GetPlayer(__instance.GetUser());
            if (!RidingPerkAuthority.Has(rider, 35))
                return;
            float regenerated = __instance.GetStamina() - __state;
            if (regenerated > 0f)
                __instance.SetStamina(Mathf.Min(__instance.GetMaxStamina(), __instance.GetStamina() + regenerated * 1.5f));
        }
    }

    internal sealed class Dodge35State
    {
        internal float GraceUntil;
        internal int LastPerfectDodgeFrame = -1;
    }

    internal static class Dodge35Service
    {
        private static readonly ConditionalWeakTable<Player, Dodge35State> States = new ConditionalWeakTable<Player, Dodge35State>();
        internal static Dodge35State Get(Player player) => States.GetOrCreateValue(player);

        // A roll with no incoming hit remains a normal roll: no perk proc, VFX or bonus.
        internal static void TryTrigger(Player player, HitData hit)
        {
            if (player == null || player.m_nview?.IsOwner() != true || player.IsDead() || player.InCutscene() ||
                player.IsTeleporting() || player.IsDebugFlying() || hit == null || !hit.m_dodgeable ||
                hit.GetAttacker() == null || !BaseAI.IsEnemy(player, hit.GetAttacker()) || !player.IsDodgeInvincible() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Dodge, 35)) return;
            Dodge35State state = Get(player);
            if (state.LastPerfectDodgeFrame == Time.frameCount) return;
            state.LastPerfectDodgeFrame = Time.frameCount;
            state.GraceUntil = Time.time + 0.10f;
            Vector3 velocity = player.m_body.linearVelocity;
            velocity.x *= 1.25f;
            velocity.z *= 1.25f;
            player.m_body.linearVelocity = velocity;
            PerkVisualService.PlayProc(player, "dodge_35", false, false);
            Dodge70Service.Apply(player, hit.GetAttacker());
        }
    }

    internal static class Dodge70Service
    {
        internal static void Apply(Player player, Character attacker)
        {
            if (player == null || attacker == null || attacker.IsDead() ||
                !Dodge70Refund.TryRefund(player)) return;
            PerkVisualService.PlayProc(player, "dodge_70", player.transform.position, false, false);
        }
    }
    // Damage merely sends an RPC from the attacker's peer. The actual dodge
    // decision and skill ownership live in RPC_Damage on the player's peer.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    [HarmonyPriority(Priority.First)]
    internal static class Dodge35PerfectDodgePatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Player player = __instance as Player;
            if (player != null && player.m_nview?.IsOwner() == true && hit?.m_dodgeable == true && player.IsDodgeInvincible())
                ExperienceContext.ObserveDodge(player, hit?.GetAttacker());
            Dodge35Service.TryTrigger(player, hit);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.IsDodgeInvincible))]
    internal static class Dodge35InvulnerabilityPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (!__result && PerkRuntimeService.HasPerk(__instance, Skills.SkillType.Dodge, 35) &&
                Dodge35Service.Get(__instance).GraceUntil >= Time.time)
                __result = true;
        }
    }

    internal static class WoodCutting35Service
    {
        private static readonly int RolledKey = "valheim_mastery.woodcutting35.rolled".GetStableHashCode();

        internal static bool ShouldProc(ZNetView view, float currentHealth, float maximumHealth, HitData hit, bool standingTree)
        {
            if (view == null || !view.IsOwner() || hit == null || currentHealth <= 0f ||
                hit.m_damage.m_chop <= 0f || !Axe35Service.IsAxeHit(hit.GetAttacker() as Player, hit) ||
                (!standingTree && currentHealth < maximumHealth - 0.01f))
                return false;
            ZDO zdo = view.GetZDO();
            if (zdo == null || (!standingTree && zdo.GetBool(RolledKey, false)))
                return false;
            if (!standingTree) zdo.Set(RolledKey, true);
            Player attacker = hit.GetAttacker() as Player;
            float chance = standingTree ? 0.20f : 0.25f;
            bool proc = PerkRuntimeService.HasPerk(attacker, Skills.SkillType.WoodCutting, 35) && PerkRuntimeService.RollChance(chance);
            if (proc)
                PerkVisualService.PlayProc(attacker, "woodcutting_35", view.transform.position + Vector3.up * 1.1f, true, false);
            return proc;
        }

        internal static void MakeLethal(HitData hit, float health, HitData.DamageModifiers resistance)
        {
            if (hit == null || health <= 0f) return;
            HitData probe = hit.Clone();
            probe.m_damage = new HitData.DamageTypes { m_chop = 1f };
            probe.ApplyResistance(resistance, out HitData.DamageModifier ignored);
            float effectivePerPoint = probe.GetTotalDamage();
            if (effectivePerPoint <= 0f) return;
            hit.m_damage = new HitData.DamageTypes { m_chop = (health + 0.01f) / effectivePerPoint };
        }
    }

    [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
    internal static class WoodCutting35TreePatch
    {
        private static void Prefix(TreeBase __instance, HitData hit)
        {
            float health = __instance.m_nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health);
            if (WoodCutting35Service.ShouldProc(__instance.m_nview, health, __instance.m_health, hit, true))
            {
                WoodCuttingChainService.RegisterTree(__instance.transform.position, hit.GetAttacker() as Player, hit.m_toolTier);
                WoodCutting35Service.MakeLethal(hit, health, __instance.m_damageModifiers);
            }
        }
    }

    [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
    internal static class WoodCutting35LogPatch
    {
        private static void Prefix(TreeLog __instance, HitData hit)
        {
            float health = __instance.m_nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health);
            if (WoodCutting35Service.ShouldProc(__instance.m_nview, health, __instance.m_health, hit, false))
                WoodCutting35Service.MakeLethal(hit, health, __instance.m_damages);
        }
    }

    [HarmonyPatch(typeof(Destructible), "RPC_Damage")]
    internal static class WoodCutting35StumpPatch
    {
        private static void Prefix(Destructible __instance, HitData hit)
        {
            if ((__instance.m_destructibleType & DestructibleType.Tree) == 0)
                return;
            float health = __instance.m_nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health);
            if (WoodCutting35Service.ShouldProc(__instance.m_nview, health, __instance.m_health, hit, false))
                WoodCutting35Service.MakeLethal(hit, health, __instance.m_damages);
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.DamageArea))]
    internal static class PickaxesPassiveBonusResourcePatch
    {
        private static void Postfix(MineRock5 __instance, HitData hit, bool __result)
        {
            Player player = hit?.GetAttacker() as Player;
            float chance = player != null ? Mathf.Clamp01(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Pickaxes) * 0.005f) : 0f;
            if (!__result || __instance == null || player == null || !PerkRuntimeService.RollChance(chance))
                return;
            List<ItemDrop.ItemData> candidates = __instance.m_dropItems.GetDropListItems();
            if (candidates == null || candidates.Count == 0)
                return;
            candidates.RemoveAll(item => item?.m_dropPrefab == null || GatheringProgressionService.IsPlainStone(item.m_dropPrefab.name));
            if (candidates.Count == 0) return;
            ItemDrop.ItemData selected = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            GameObject extra = UnityEngine.Object.Instantiate(selected.m_dropPrefab, hit.m_point + UnityEngine.Random.insideUnitSphere * 0.2f, Quaternion.identity);
            ItemDrop item = extra.GetComponent<ItemDrop>();
            if (item != null)
                item.m_itemData.m_stack = 1;
            ItemDrop.OnCreateNew(extra, false);
            PerkVisualService.PlayPickaxeResourceProc(player, hit.m_point);
        }
    }

}





