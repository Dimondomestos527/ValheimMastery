using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class BloodMagic35Service
    {
        internal const float StaffBlockMultiplier = 4f;
        internal const float BareMinimumStaffBlock = 60f;
        internal const float NovaRadius = 4f;
        internal const float NovaDamage = 30f;
        internal const float NovaStaggerMultiplier = 2f;

        internal static Player ResolveSummonOwner(Character summon)
        {
            if (summon == null)
                return null;
            MonsterAI ai = summon.GetComponent<MonsterAI>();
            GameObject follow = ai?.GetFollowTarget();
            return follow != null ? follow.GetComponent<Player>() : null;
        }

        internal static void Nova(Player owner, Vector3 position, string source)
        {
            if (owner == null || !PerkRuntimeService.HasPerk(owner, Skills.SkillType.BloodMagic, 35))
                return;

            Collider[] hits = Physics.OverlapSphere(position, NovaRadius, LayerMask.GetMask("character", "character_net"));
            System.Collections.Generic.HashSet<Character> seen = new System.Collections.Generic.HashSet<Character>();
            foreach (Collider collider in hits)
            {
                Character target = collider?.GetComponentInParent<Character>();
                if (target == null || target == owner || !seen.Add(target) || target.IsDead() || target.IsPlayer())
                    continue;
                if (!BaseAI.IsEnemy(owner, target))
                    continue;

                HitData hit = new HitData();
                hit.m_skill = Skills.SkillType.BloodMagic;
                hit.m_point = target.GetCenterPoint();
                hit.m_dir = (target.transform.position - position).normalized;
                hit.m_damage.m_damage = NovaDamage;
                hit.m_staggerMultiplier = NovaStaggerMultiplier;
                hit.SetAttacker(owner);
                PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
                context.IsPerkGenerated = true;
                context.SourceSkill = Skills.SkillType.BloodMagic;
                context.PerkId = source;
                context.AllowSelfProc = false;
                target.Damage(hit);
            }

            PerkVisualService.PlayProc(owner, "bloodmagic_35", position, true, false);
        }
    }

    /// <summary>
    /// Blood staves become practical defensive weapons. This patch affects only the local player's
    /// currently held Blood Magic item and therefore does not mutate shared item prefabs.
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), new[] { typeof(float) })]
    internal static class BloodMagic35StaffBlockPatch
    {
        // The approved staff redesign replaces the legacy all-staves block bonus.
        private static bool Prepare() => false;
        private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
        {
            Player player = Player.m_localPlayer;
            if (player == null || __instance == null || player.GetCurrentWeapon() != __instance ||
                __instance.m_shared?.m_skillType != Skills.SkillType.BloodMagic ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.BloodMagic, 35))
                return;

            __result = Mathf.Max(BloodMagic35Service.BareMinimumStaffBlock, __result * BloodMagic35Service.StaffBlockMultiplier);
        }
    }

    /// <summary>
    /// Own-player protection barrier nova. SE_Shield contains the casting skill level but no reliable
    /// caster player id, so the safe first implementation attributes a barrier nova only when the
    /// shielded character is itself the Blood Magic 35+ player. Codex should extend this with caster
    /// attribution when validating the live staff/status-effect pipeline.
    /// </summary>
    [HarmonyPatch(typeof(SE_Shield), "OnDamaged")]
    internal static class BloodMagic35BarrierNovaPatch
    {
        // Replaced by MagicShieldPressurePatch: actual damage-capacity exhaustion,
        // master caster attribution and pushback without damage/stagger.
        private static bool Prepare() => false;
        private sealed class ShieldState
        {
            internal float Before;
            internal Player Owner;
        }

        private static void Prefix(SE_Shield __instance, out ShieldState __state)
        {
            Player owner = __instance?.m_character as Player;
            __state = new ShieldState { Before = __instance?.m_absorbDamage ?? 0f, Owner = owner };
        }

        private static void Postfix(SE_Shield __instance, ShieldState __state)
        {
            if (__instance == null || __state?.Owner == null || __state.Before <= 0f || __instance.m_absorbDamage > 0f ||
                !PerkRuntimeService.HasPerk(__state.Owner, Skills.SkillType.BloodMagic, 35))
                return;
            BloodMagic35Service.Nova(__state.Owner, __state.Owner.transform.position, "bloodmagic_35_barrier");
        }
    }

    /// <summary>
    /// Blood summon death nova. Vanilla summoned skeletons follow their commanding player through
    /// MonsterAI.GetFollowTarget(), which gives us reliable owner attribution without guessing by proximity.
    /// </summary>
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class BloodMagic35SummonDeathNovaPatch
    {
        // Legacy damaging death nova is not part of the new skeleton/shield spells.
        private static bool Prepare() => false;
        private sealed class DeathState
        {
            internal bool WasAlive;
            internal Player Owner;
        }

        private static void Prefix(Character __instance, out DeathState __state)
        {
            __state = new DeathState
            {
                WasAlive = __instance != null && !__instance.IsDead(),
                Owner = BloodMagic35Service.ResolveSummonOwner(__instance)
            };
        }

        private static void Postfix(Character __instance, DeathState __state)
        {
            if (!(__state?.WasAlive ?? false) || __instance == null || !__instance.IsDead() || __state.Owner == null ||
                !PerkRuntimeService.HasPerk(__state.Owner, Skills.SkillType.BloodMagic, 35))
                return;
            BloodMagic35Service.Nova(__state.Owner, __instance.transform.position, "bloodmagic_35_summon");
        }
    }
}
