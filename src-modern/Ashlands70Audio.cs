using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // Confirmed grant cues only. Independent of VFX, and never creates a donor
    // prefab (which can contain damage/network components). Shared audio copies clips.
    internal static class Ashlands70Audio
    {
        private static readonly Dictionary<string,long> Played = new Dictionary<string,long>();
        private static readonly List<string> Expired = new List<string>();
        private sealed class OrbitTail
        {
            internal Ashlands70Grant Grant;
            internal float Ready, Deadline, NextTry;
        }
        private static readonly Dictionary<string, OrbitTail> OrbitTails = new Dictionary<string, OrbitTail>();
        private static float NextAttempt;
        internal static string NativeSound(Ashlands70Kind kind)
        {
            switch (kind)
            {
                case Ashlands70Kind.Madness: return "sfx_staff_trollstav_cast";
                case Ashlands70Kind.Chain: return "sfx_staff_lightning_fire";
                case Ashlands70Kind.Grove: return "sfx_staff_elder_cast";
                case Ashlands70Kind.Orbit: return "sfx_staff_grenade_cast";
                default: return null;
            }
        }
        internal static void Tick()
        {
            if (Application.isBatchMode || Player.m_localPlayer == null || MasteryPlugin.Settings?.Enabled?.Value != true ||
                MasteryPlugin.Settings.EnablePerkSFX.Value != true) { OrbitTails.Clear(); return; }
            long now = Ashlands70Cast.Now;
            if (now <= 0) { OrbitTails.Clear(); return; }
            TickOrbitTails(now);
            if (Time.unscaledTime < NextAttempt) return;
            NextAttempt = Time.unscaledTime + .15f;
            Expired.Clear(); foreach (var pair in Played) if (pair.Value <= now) Expired.Add(pair.Key);
            foreach (string key in Expired) Played.Remove(key);
            foreach (Ashlands70State state in Ashlands70World.ActiveStates)
            {
                Ashlands70Grant grant = state.Grant;
                // Periodic state snapshots and late-loaded sectors must not replay old casts.
                if (grant == null || string.IsNullOrEmpty(grant.Token) || Played.ContainsKey(grant.Token) || Played.Count >= 128 ||
                    grant.World != Ashlands70Cast.CurrentWorld || grant.Started <= 0 || now < grant.Started ||
                    now - grant.Started > 3 * TimeSpan.TicksPerSecond || !Ashlands70Rules.Active(now, grant.Until) ||
                    (grant.Origin - Player.m_localPlayer.transform.position).sqrMagnitude > 4096f) continue;
                string name = NativeSound(grant.Kind); if (name == null) continue;
                GameObject prefab = NativePerkAssetResolver.Resolve(name);
                // Async asset unavailable / shared channel busy: bounded retry within the cast window.
                if (prefab != null && PerkAudioService.PlayPrefab("ashlands70_cast_" + grant.Kind, prefab, grant.Origin, .08f, .5f))
                {
                    Played[grant.Token] = grant.Until;
                    // Human-selected native small-impact layer. Starts only after
                    // the primary cast actually plays; no loop or snapshot replay.
                    if (grant.Kind == Ashlands70Kind.Orbit && OrbitTails.Count < 128)
                    {
                        float ready = Time.unscaledTime + .16f;
                        OrbitTails[grant.Token] = new OrbitTail { Grant = grant, Ready = ready, NextTry = ready, Deadline = ready + .6f };
                    }
                }
            }
        }
        private static void TickOrbitTails(long now)
        {
            if (OrbitTails.Count == 0) return;
            Expired.Clear();
            foreach (var pair in OrbitTails)
            {
                OrbitTail tail = pair.Value; Ashlands70Grant grant = tail.Grant;
                if (Time.unscaledTime > tail.Deadline || grant.World != Ashlands70Cast.CurrentWorld ||
                    now < grant.Started || now - grant.Started > 3 * TimeSpan.TicksPerSecond ||
                    !Ashlands70Rules.Active(now, grant.Until) ||
                    (grant.Origin - Player.m_localPlayer.transform.position).sqrMagnitude > 4096f)
                { Expired.Add(pair.Key); continue; }
                if (Time.unscaledTime < tail.Ready || Time.unscaledTime < tail.NextTry) continue;
                tail.NextTry = Time.unscaledTime + .1f;
                GameObject prefab = NativePerkAssetResolver.Resolve("sfx_staff_grenade_impact_small");
                // Native source volume is 2.7: this scale yields nominal 0.25.
                // Preserve the donor's own clip and pitch variation.
                if (prefab != null && PerkAudioService.PlayPrefab("ashlands70_orbit_tail", prefab, grant.Origin, .08f, .09259259f))
                    Expired.Add(pair.Key);
            }
            foreach (string token in Expired) OrbitTails.Remove(token);
        }
        internal static void Reset() { Played.Clear(); OrbitTails.Clear(); Expired.Clear(); NextAttempt = 0f; }
    }
}
