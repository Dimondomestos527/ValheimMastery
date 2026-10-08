using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class FeastJumpRules
    {
        internal static float AddHeight(float verticalSpeed, float gravity)
        {
            return verticalSpeed > 0f && gravity > 0f ? Mathf.Sqrt(verticalSpeed * verticalSpeed + 4f * gravity) : verticalSpeed;
        }
        internal static float FallBase(float height) => Mathf.Clamp01((height - 6f) / 16f) * 100f;
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    [HarmonyPriority(Priority.First)]
    internal static class FeastMistlandsJumpScopePatch
    {
        [ThreadStatic] internal static Player Current;
        private static void Prefix(Character __instance, out Player __state)
        {
            __state = Current;
            Current = __instance is Player player && MasterFeastThemeService.GetTheme(player) == FeastTheme.Mistlands ? player : null;
        }
        private static void Finalizer(Player __state) => Current = __state;
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ForceJump))]
    internal static class FeastMistlandsJumpPatch
    {
        private static void Prefix(Character __instance, ref Vector3 vel)
        {
            if (ReferenceEquals(__instance, FeastMistlandsJumpScopePatch.Current))
                vel.y = FeastJumpRules.AddHeight(vel.y, Mathf.Abs(Physics.gravity.y));
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyFallDamage))]
    [HarmonyPriority(Priority.Last)]
    internal static class FeastMistlandsSafeFallPatch
    {
        private static void Postfix(SEMan __instance, float baseDamage, ref float damage)
        {
            if (baseDamage > 0f && __instance.m_character is Player player &&
                MasterFeastThemeService.GetTheme(player) == FeastTheme.Mistlands)
            {
                float height = Mathf.Max(0f, player.m_maxAirAltitude - player.transform.position.y);
                damage *= FeastJumpRules.FallBase(height) / baseDamage;
            }
        }
    }

    internal static class FeastSecondWindRules
    {
        internal static bool Cross(float current, float maximum, long now, long readyAt, ref bool armed)
        {
            if (maximum <= 0f) { armed = false; return false; }
            if (current / maximum >= .15f) { armed = true; return false; }
            if (!armed) return false;
            armed = false;
            return current > 0f && now >= readyAt;
        }
    }

    [HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect))]
    internal static class FeastMeadowsCozyPatch
    {
        private static void Prefix(SE_Cozy __instance, out float? __state)
        {
            __state = null;
            if (__instance.m_character is Player player && MasterFeastThemeService.GetTheme(player) == FeastTheme.Meadows)
            { __state = __instance.m_delay; __instance.m_delay = 7f; }
        }
        private static void Finalizer(SE_Cozy __instance, float? __state)
        { if (__state.HasValue) __instance.m_delay = __state.Value; }
    }

    [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.UpdateTTL))]
    internal static class FeastMeadowsRestedPatch
    {
        private static void Prefix(SE_Rested __instance, out float? __state)
        {
            __state = null;
            if (__instance.m_character is Player player && MasterFeastThemeService.GetTheme(player) == FeastTheme.Meadows)
            { __state = __instance.m_baseTTL; __instance.m_baseTTL = __state.Value + 300f; }
        }
        private static void Finalizer(SE_Rested __instance, float? __state)
        { if (__state.HasValue) __instance.m_baseTTL = __state.Value; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetDamageModifiers))]
    internal static class FeastSwampPoisonPatch
    {
        private static void Postfix(Character __instance, ref HitData.DamageModifiers __result)
        {
            if (__instance is Player player && MasterFeastThemeService.GetTheme(player) == FeastTheme.Swamp)
                __result.ApplyIfBetter(ref __result.m_poison, HitData.DamageModifier.Resistant);
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifySkillLevel))]
    internal static class FeastOceanSkillPatch
    {
        private static void Postfix(SEMan __instance, Skills.SkillType skill, ref float level)
        {
            if (skill == Skills.SkillType.Swim && __instance.m_character is Player player &&
                MasterFeastThemeService.GetTheme(player) == FeastTheme.Oceans)
                level = Mathf.Min(100f, level + 20f);
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyRaiseSkill))]
    internal static class FeastOceanExperiencePatch
    {
        private static void Postfix(SEMan __instance, Skills.SkillType skill, ref float multiplier)
        {
            if (skill == Skills.SkillType.Swim && Game.m_skillGainRate > 0f &&
                __instance.m_character is Player player && MasterFeastThemeService.GetTheme(player) == FeastTheme.Oceans)
                multiplier += 1f;
        }
    }

    internal sealed class FeastSecondWindState
    {
        internal readonly bool[] Armed = new bool[3];
        internal readonly long[] ReadyAt = new long[3];
        internal bool Loaded;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class FeastNorthSecondWindPatch
    {
        private const string CooldownKey = "valheim_mastery.feast.second_wind.";
        private static void Postfix(Player __instance)
        {
            Player player = __instance;
            if (player == null || player != Player.m_localPlayer || player.m_nview == null ||
                !player.m_nview.IsValid() || !player.m_nview.IsOwner() || player.IsDead() || player.GetHealth() <= 0f) return;
            var state = MasteryStateStore.GetPlayerState<FeastSecondWindState>(player);
            if (!state.Loaded)
            {
                for (int i = 0; i < 3; i++)
                    if (player.m_customData.TryGetValue(CooldownKey + i, out string raw))
                        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out state.ReadyAt[i]);
                state.Loaded = true;
            }
            if (MasterFeastThemeService.GetTheme(player) != FeastTheme.DeepNorth)
            { Array.Clear(state.Armed, 0, state.Armed.Length); return; }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Check(player, state, 0, player.GetHealth(), player.GetMaxHealth(), now);
            Check(player, state, 1, player.GetStamina(), player.GetMaxStamina(), now);
            Check(player, state, 2, player.GetEitr(), player.GetMaxEitr(), now);
        }
        private static void Check(Player player, FeastSecondWindState state, int resource, float current, float maximum, long now)
        {
            // Zero stamina/eitr are valid depleted resources; zero HP is never resurrected.
            float crossingValue = resource == 0 ? current : Mathf.Max(float.Epsilon, current);
            if (!FeastSecondWindRules.Cross(crossingValue, maximum, now, state.ReadyAt[resource], ref state.Armed[resource])) return;
            state.ReadyAt[resource] = now + 60000L;
            player.m_customData[CooldownKey + resource] = state.ReadyAt[resource].ToString(CultureInfo.InvariantCulture);
            float amount = maximum * .25f;
            if (resource == 0) player.Heal(amount, true);
            else if (resource == 1) player.AddStamina(amount);
            else player.AddEitr(amount);
        }
    }
}
