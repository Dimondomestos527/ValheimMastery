#if MASTERY_CLUBS35_EXPERIMENT
using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // One native ground strike followed by a weaker, half-radius echo.
    internal static class Hammer35Epicenter
    {
        [ThreadStatic] internal static Attack Current;
        internal static Vector3 Center(Attack attack) => attack.GetAttackOrigin().position + Vector3.up * attack.m_attackHeight +
            attack.m_character.transform.forward * attack.m_attackRange + attack.m_character.transform.right * attack.m_attackOffset;
        internal static void Impact(Attack attack)
        {
            Player player = attack?.m_character as Player;
            if (player == null || !MasteryPlugin.Settings.Enabled.Value ||
                ClubWeaponClassService.Classify(attack.m_weapon) != ClubWeaponClass.SledgeHammer ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 35) || attack.GetAttackOrigin() == null) return;
            Vector3 center = Center(attack);
            float outer = OuterRadius(attack);
            Clubs70EchoService.BeginHammer(attack, center, outer);
            // Native DoAreaAttack already plays this hammer's own impact lists.
            // Do not overlay a universal Demolisher wave or duplicate its audio.
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Hammer35] area-dispatch weapon=" + PerkRuntimeService.ItemPrefabName(attack.m_weapon) +
                    " attack=" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(attack) +
                    " skill=" + PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Clubs).ToString("0.##") +
                    " nativeRadius=" + outer.ToString("0.###"));
        }
        internal static float OuterRadius(Attack attack) => attack == null ? 0f :
            Mathf.Max(0f, attack.m_attackRayWidth) + Mathf.Max(0f, attack.m_attackRayWidthCharExtra);
        internal static void Apply(Character victim, HitData hit)
        {
            Attack attack = Current; Player player = attack?.m_character as Player;
            if (player == null || victim == null || victim.IsPlayer() || victim.IsDead() ||
                hit == null || hit.GetAttacker() != player || hit.m_skill != Skills.SkillType.Clubs ||
                PerkRuntimeService.IsPerkGenerated(hit) || !BaseAI.IsEnemy(player, victim) ||
                ClubWeaponClassService.Classify(attack.m_weapon) != ClubWeaponClass.SledgeHammer ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 35)) return;
            Transform origin = attack.GetAttackOrigin();
            if (origin == null) return;
            // Preserve exactly the native attack geometry; no added radius or
            // inner fracture modifier. Echoes reuse this real hit payload.
            Vector3 center = Center(attack);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Hammer35] target-hit attack=" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(attack) +
                    " target=" + victim.GetZDOID() + " name=" + victim.name + " nativeRadius=" + OuterRadius(attack).ToString("0.###") +
                    " skill=" + hit.m_skill +
                    " variant=" + hit.m_variant);
            // Snapshot one genuine native enemy hit. Delayed echoes are scheduled
            // only after DoAreaAttack returns and replay its native hit payload.
            Clubs70EchoService.CaptureHammer(attack, victim, hit, center, OuterRadius(attack));
        }
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class Hammer35AttackScope
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Attack __instance, out Attack __state)
        { __state = Hammer35Epicenter.Current; Hammer35Epicenter.Current = __instance; Hammer35Epicenter.Impact(__instance); }
        private static Exception Finalizer(Attack __state, Exception __exception)
        { Hammer35Epicenter.Current = __state; return __exception; }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Hammer35InnerHitPatch
    { private static void Prefix(Character __instance, HitData hit) => Hammer35Epicenter.Apply(__instance, hit); }
}
#endif
