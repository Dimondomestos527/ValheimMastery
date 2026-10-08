using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Effect executor only. ReceiveCommitted is called exclusively by the reviewed
    // authenticated action coordinator AFTER durable Gold committed entitlement.
    // No input/price admission, grant parsing or optimistic activation lives here.
    internal static class Combat100Muster
    {
        internal const double Duration = 300d;
        private sealed class Window
        {
            internal string Token;
            internal ZNet Session;
            internal long World, UtcEnd;
            internal double End;
        }
        private sealed class Extension
        {
            internal double GrantEnd;
            internal float NativeTtl, PublishedTtl;
            internal int RefreshDepth;
            internal bool Pending;
        }
        private static readonly ConditionalWeakTable<Player, Window> Windows = new ConditionalWeakTable<Player, Window>();
        private static readonly ConditionalWeakTable<StatusEffect, Extension> Extensions = new ConditionalWeakTable<StatusEffect, Extension>();
        private static double Now => Time.timeAsDouble;
        internal static bool Active(Player p)
        {
            if (p == null || p != Player.m_localPlayer || p.m_nview?.IsOwner() != true || p.IsDead() ||
                !Windows.TryGetValue(p, out var w)) return false;
            return ReferenceEquals(w.Session, ZNet.instance) && w.World != 0 && w.World == w.Session.GetWorldUID() && Now < w.End && DateTime.UtcNow.Ticks < w.UtcEnd;
        }
        internal static double Remaining(Player player) => Active(player) && Windows.TryGetValue(player, out var window) ?
            Math.Max(0d, Math.Min(window.End - Now, (window.UtcEnd - DateTime.UtcNow.Ticks) / (double)TimeSpan.TicksPerSecond)) : 0d;
        internal static string ActiveToken(Player player) => Active(player) && Windows.TryGetValue(player, out var window) ? window.Token : null;
        internal static void BeforeLoad(Player player)
        {
            // A successful profile reload can restore the SAME original token;
            // the prior live object tombstone must not override durable hydration.
            if (player != null) Windows.Remove(player);
        }
        internal static bool ReceiveCommitted(Player player, string originalToken, double remainingSeconds)
        {
            if (player == null || player != Player.m_localPlayer || player.m_nview?.IsOwner() != true || player.IsDead() ||
                ZNet.instance == null || string.IsNullOrEmpty(originalToken) || originalToken.Length > 32 ||
                double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) || remainingSeconds <= 0 || remainingSeconds > Duration) return false;
            if (Active(player)) return true; // No second layer, no refresh, no new extension pass.
            if (Windows.TryGetValue(player, out var old) && old.Token == originalToken) return false;
            Windows.Remove(player);
            Windows.Add(player, new Window { Token = originalToken, Session = ZNet.instance,
                World = ZNet.instance.GetWorldUID(), End = Now + remainingSeconds,
                UtcEnd = checked(DateTime.UtcNow.Ticks + (long)(remainingSeconds * TimeSpan.TicksPerSecond)) });
            Reconcile(player.GetSEMan());
            RefreshMaxima(player);
            return true;
        }
        internal static void EndLife(Player player)
        {
            // A revived/reloaded same Player object cannot inherit the prior life window.
            // Durable paid-action delivery must independently deny old-life replay.
            if (player != null && Windows.TryGetValue(player, out var window)) window.End = 0;
        }
        internal static void RefreshMaxima(Player player)
        {
            if (player == null || player.m_nview?.IsOwner() != true || player.IsDead()) return;
            player.GetTotalFoodValue(out float hp, out float stamina, out _);
            // Pure cached aggregate: NO food update/tick, regeneration or bar refill.
            // Existing Clubs70 max-drop reservation reconciliation remains native.
            player.SetMaxHealth(hp, false); player.SetMaxStamina(stamina, false);
        }
        internal static void BeforeSeUpdate(SEMan manager)
        {
            if (manager?.m_character is not Player player || player.m_nview?.IsOwner() != true) return;
            bool had = Windows.TryGetValue(player, out var window);
            if (had && !Active(player))
            {
                // Leave the last token as an expired tombstone until the next genuine
                // committed cast. Coordinator provides durable all-token replay defense.
                bool needsRefresh = window.End > 0;
                window.End = 0;
                if (needsRefresh) RefreshMaxima(player);
            }
            Reconcile(manager); // BEFORE native can delete short-lived published SEs.
        }
        internal static void ObserveApplied(SEMan manager)
        {
            if (manager?.m_character is not Player player || !Active(player)) return;
            foreach (var effect in manager.m_statusEffects)
                if (Eligible(effect, player) && !Extensions.TryGetValue(effect, out _))
                    Extensions.Add(effect, new Extension { Pending = true });
            // Defer initial extension until the pre-update catchup. Cooking35 writes
            // its final native TTL AFTER ConsumeItem's native Add call.
        }
        private static void Reconcile(SEMan manager)
        {
            if (manager?.m_character is not Player player || player.m_nview?.IsOwner() != true) return;
            bool active = Active(player);
            foreach (var effect in manager.m_statusEffects)
            {
                if (effect == null) continue;
                if (!Extensions.TryGetValue(effect, out var tag))
                {
                    if (!active || !Eligible(effect, player)) continue;
                    Extensions.Add(effect, tag = new Extension { Pending = true });
                }
                if (tag.RefreshDepth != 0) continue;
                if (tag.Pending)
                {
                    if (!Eligible(effect, player)) continue;
                    float remaining = effect.m_ttl - effect.m_time;
                    tag.NativeTtl = effect.m_ttl;
                    tag.GrantEnd = Now + remaining + Duration;
                    tag.Pending = false;
                }
                else if (effect.m_ttl != tag.PublishedTtl)
                {
                    // An audited/native writer replaced TTL (e.g. Cooking35).
                    // It changes the native baseline; it NEVER remints the fixed grant.
                    tag.NativeTtl = effect.m_ttl;
                }
                Publish(effect, tag);
            }
        }
        internal static bool BeginRefresh(StatusEffect effect)
        {
            if (effect == null || !Extensions.TryGetValue(effect, out var tag) || tag.Pending) return false;
            if (tag.RefreshDepth++ == 0) effect.m_ttl = tag.NativeTtl;
            return true;
        }
        internal static void EndRefresh(StatusEffect effect, bool began)
        {
            if (!began || effect == null || !Extensions.TryGetValue(effect, out var tag)) return;
            if (--tag.RefreshDepth != 0) return;
            tag.NativeTtl = effect.m_ttl;
            Publish(effect, tag);
        }
        private static void Publish(StatusEffect effect, Extension tag)
        {
            double nativeRemaining = Math.Max(0d, tag.NativeTtl - effect.m_time);
            double onceRemaining = Math.Max(0d, tag.GrantEnd - Now);
            double ttl = effect.m_time + Math.Max(nativeRemaining, onceRemaining);
            if (double.IsNaN(ttl) || double.IsInfinity(ttl) || ttl > float.MaxValue) return;
            tag.PublishedTtl = (float)ttl; effect.m_ttl = tag.PublishedTtl;
        }
        private static bool Eligible(StatusEffect effect, Player player)
        {
            if (effect == null || !float.IsFinite(effect.m_ttl) || !float.IsFinite(effect.m_time) ||
                effect.m_ttl <= 0 || effect.m_time < 0 || effect.m_ttl <= effect.m_time) return false;
            if (effect.GetType() == typeof(SE_Rested) && effect.NameHash() == "Rested".GetStableHashCode()) return true;
            // Known native guardian routes; require a resolved actual ObjectDB template,
            // and exact plain/stat type. Unavailable/custom/unknown subclasses excluded.
            string name = effect.name;
            bool guardian = name == "GP_Eikthyr" || name == "GP_TheElder" || name == "GP_Bonemass" ||
                name == "GP_Moder" || name == "GP_Yagluth" || name == "GP_Queen" || name == "GP_Ashlands";
            var template = guardian ? ObjectDB.instance?.GetStatusEffect(effect.NameHash()) : null;
            if (guardian && template != null && template.GetType() == effect.GetType() &&
                (effect.GetType() == typeof(StatusEffect) || effect.GetType() == typeof(SE_Stats))) return true;
            return Combat100BuffEligibility.Eligible(effect);
        }
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class Combat100MusterDeathPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance) => Combat100Muster.EndLife(__instance);
        // Save only AFTER native death has set dead state/applied its own penalties.
        // No early alive-character checkpoint and no rollback on ambiguous failure.
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance) => Combat100MusterOwner.EndLife(__instance);
    }
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.Update))]
    internal static class Combat100MusterSeUpdatePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(SEMan __instance) => Combat100Muster.BeforeSeUpdate(__instance);
    }
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new Type[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short) })]
    internal static class Combat100MusterSeAddPatch
    {
        private static void Postfix(SEMan __instance) => Combat100Muster.ObserveApplied(__instance);
    }
    [HarmonyPatch]
    internal static class Combat100MusterRefreshPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in new[] { typeof(StatusEffect), typeof(SE_Stats), typeof(SE_Rested) })
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (method.Name == "ResetTime" || type == typeof(SE_Rested) && (method.Name == "UpdateTTL" || method.Name == "SetComfortLevel")) yield return method;
        }
        private static void Prefix(StatusEffect __instance, out bool __state) => __state = Combat100Muster.BeginRefresh(__instance);
        private static Exception Finalizer(StatusEffect __instance, bool __state, Exception __exception)
        { Combat100Muster.EndRefresh(__instance, __state); return __exception; }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.GetTotalFoodValue))]
    internal static class Combat100MusterMaximaPatch
    {
        private static void Postfix(Player __instance, ref float __0, ref float __1)
        { if (Combat100Muster.Active(__instance)) { __0 += 25f; __1 += 25f; } }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
    internal static class Combat100MusterArmorPatch
    {
        // Repository Crossbows35 is Priority.Last(0), so -100 executes after its
        // multiplicative armor formula. Exactly +10 once in this repository.
        [HarmonyPriority(-100)]
        private static void Postfix(Player __instance, ref float __result)
        { if (Combat100Muster.Active(__instance)) __result += 10f; }
    }
}




