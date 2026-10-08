#if MASTERY_CLUBS35_EXPERIMENT || MASTERY_CLUBS70_EXPERIMENT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Small main-thread queue: animation callbacks only snapshot data; native
    // cosmetic prefab work happens from Player.Update after the callback returns.
    internal static class WeaponImpactVisualQueue
    {
        private const int Capacity = 16;
        private const int PerUpdateLimit = 4;
        private sealed class Request
        {
            internal Vector3 Center;
            internal float Radius;
            internal float InnerRadius;
            internal bool Mace70, Hammer70, Secondary;
            internal float Expires;
        }
        private static readonly Queue<Request> Pending = new Queue<Request>(Capacity);
        private static ZNetScene Scene;

        internal static void QueueHammerImpact(Vector3 center, float outer, float inner)
        {
#if MASTERY_CLUBS35_EXPERIMENT
            if (Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value || Pending.Count >= Capacity) return;
            // One attack-origin request owns both authored zones. A later victim
            // contact must not add a second demolition burst at the victim body.
            Pending.Enqueue(new Request { Center = center, Radius = Mathf.Clamp(outer, .1f, 20f),
                InnerRadius = Mathf.Clamp(inner, 0f, outer), Expires = Time.time + .5f });
#endif
        }

        internal static void QueueMace70Impact(Vector3 center, bool secondary)
        {
#if MASTERY_CLUBS70_EXPERIMENT
            if (Player.m_localPlayer == null ||
                (!MasteryPlugin.Settings.EnablePerkProcVFX.Value && !MasteryPlugin.Settings.EnablePerkSFX.Value) ||
                Pending.Count >= Capacity) return;
            Pending.Enqueue(new Request { Center = center, Mace70 = true, Secondary = secondary, Expires = Time.time + .5f });
#endif
        }

        internal static void QueueHammer70Impact(Vector3 center, bool secondary)
        {
#if MASTERY_CLUBS70_EXPERIMENT
            if (Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value || Pending.Count >= Capacity) return;
            Pending.Enqueue(new Request { Center = center, Hammer70 = true, Secondary = secondary, Expires = Time.time + .5f });
#endif
        }

        internal static void Flush()
        {
            if (Player.m_localPlayer == null) { Pending.Clear(); return; }
            if (Scene != ZNetScene.instance)
            {
                Scene = ZNetScene.instance; Pending.Clear();
                // Start async resolution before the first impact, not inside the
                // animation callback. A cold bundle must not hide the first cue.
                NativeSoftVisualAssets.GetPrefab("vfx_HitSparks");
                NativeSoftVisualAssets.GetPrefab("fx_sledge_demolisher_hit");
            }
            bool hammerGroundQueued = false;
            foreach (Request queued in Pending)
                if (!queued.Mace70 && !queued.Hammer70 && Time.time <= queued.Expires) hammerGroundQueued = true;
            int count = 0;
            while (Pending.Count > 0 && count++ < PerUpdateLimit)
            {
                Request request = Pending.Dequeue();
                if (Time.time > request.Expires) continue;
                if (request.Mace70)
                {
                    NativeContactSparks.Play(request.Center, request.Secondary);
                    if (request.Secondary) NativeGroundPressure.Play(request.Center, 1.8f, 0f);
                    PerkAudioService.Play("clubs70_mace_impact", "fx_sledge_demolisher_hit", request.Center, .18f, .75f, .95f);
                }
                else if (request.Hammer70)
                {
                    // Hammer35 already uses the charged native area geometry.
                    // Do not repeat a full effect at every damaged victim.
                    if (!hammerGroundQueued) NativeGroundPressure.Play(request.Center, request.Secondary ? 2.4f : 1.2f, 0f);
                }
#if MASTERY_CLUBS35_EXPERIMENT
                else Hammer35DustRing.PlaySafe(request.Center, request.Radius, request.InnerRadius);
#endif
            }
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class WeaponImpactVisualQueuePatch
    { private static void Postfix(Player __instance) { if (__instance == Player.m_localPlayer) WeaponImpactVisualQueue.Flush(); } }

#if MASTERY_CLUBS35_EXPERIMENT
    // Presentation only; the outer radius and fracture core come from the
    // unchanged native attack geometry in Hammer35Epicenter.
    internal static class Hammer35DustRing
    {
        internal static void PlaySafe(Vector3 center, float radius, float inner) => NativeGroundPressure.Play(center, radius, inner);
    }
#endif

    // Native Demolisher's block_wave emitter only. Its huge dust, rocks, bubble,
    // lights and distortion wave stay muted. No replacement geometry/material.
    internal sealed class NativeGroundPressure : MonoBehaviour
    {
        private ParticleSystem Wave;
        private int Count;
        private float Delay, Radius;
        private readonly ParticleSystem.Particle[] Probe = new ParticleSystem.Particle[1];
        private static int Traced;
        private static ZNetScene Scene;

        internal static void Play(Vector3 center, float outer, float inner)
        {
            if (Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            int mask = LayerMask.GetMask("terrain", "static_solid", "piece");
            if (!Physics.Raycast(center + Vector3.up * .6f, Vector3.down, out RaycastHit contact, 3.5f, mask,
                QueryTriggerInteraction.Ignore) || contact.normal.y < .35f) return;
            GameObject source = NativePerkAssetResolver.Resolve("fx_sledge_demolisher_hit");
            if (source == null)
            {
                NativeVfxSafeFrame.Request("fx_sledge_demolisher_hit", loaded => SpawnZones(loaded, contact.point, contact.normal, outer, inner));
                return;
            }
            SpawnZones(source, contact.point, contact.normal, outer, inner);
        }
        private static bool SpawnZones(GameObject source, Vector3 ground, Vector3 normal, float outer, float inner)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value || Player.m_localPlayer == null) return true;
            bool played = Spawn(source, ground, normal, Mathf.Clamp(outer, .1f, 20f), false);
            if (played && inner > 0f) Spawn(source, ground, normal, Mathf.Clamp(inner, .1f, outer), true);
            return played;
        }
        private static bool Spawn(GameObject source, Vector3 ground, Vector3 normal, float radius, bool inner)
        {
            ParticleSystem[] native = source.GetComponentsInChildren<ParticleSystem>(true);
            int selected = -1;
            for (int i = 0; i < native.Length; i++)
                if (NativeBurstPlayback.HasMaterial(native[i], "block_wave") &&
                    native[i].GetComponent<ParticleSystemRenderer>()?.renderMode == ParticleSystemRenderMode.HorizontalBillboard)
                { selected = i; break; }
            if (selected < 0) { NativeVfxSafeFrame.Warn(source.name, "native block_wave emitter absent"); return true; }
            var nativeRenderer = native[selected].GetComponent<ParticleSystemRenderer>();
            float diameter = native[selected].main.startSize.Evaluate(0f, .5f);
            if (diameter <= .01f) { NativeVfxSafeFrame.Warn(source.name, "native wave diameter unavailable"); return true; }
            GameObject root = VfxPool.Spawn(source, ground, Quaternion.identity, .95f);
            if (root == null) return false;
            NativeBurstPlayback.Mute(root);
            ParticleSystem wave = root.GetComponentsInChildren<ParticleSystem>(true)[selected];
            NativeGroundPressure pressure = root.GetComponent<NativeGroundPressure>() ?? root.AddComponent<NativeGroundPressure>();
            pressure.StopAllCoroutines(); pressure.Wave = wave; pressure.Radius = radius;
            // This selected wave is one authored pressure ring. Its native
            // emitter may use a delayed/rate burst, which NativeCount(t=0)
            // deliberately omits; an explicit replay must still emit one ring.
            pressure.Count = 1;
            pressure.Delay = inner ? .09f : 0f;
            var main = wave.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            // Native wave lifetime is .8s; shortening it also accelerates its
            // authored color fade. Keep it within the existing .95s lease.
            main.maxParticles = 1; main.startLifetimeMultiplier *= inner ? .85f : 1f;
            var renderer = wave.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = pressure.Count > 0;
            // The native size-over-life reaches1. Its real mesh bounds and start
            // size calibrate final diameter, rather than scaling the whole blast.
            root.transform.localScale *= radius * 2f / diameter;
            Vector3 offset = wave.transform.position - root.transform.position;
            // Actual native waves(1) sits .5m above the impact. Its block_wave
            // material soft-fades within .42m of surfaces; .08m buried the cue
            // in that fade band despite particles/bounds reporting success.
            root.transform.position = ground + normal * .5f - offset;
            NativeVfxSafeFrame.AfterReady(root, pressure.Begin);
            root.SetActive(true);
            return true;
        }
        private void Begin()
        {
            if (Wave == null) return;
            // Content must remain inactive until the shared sanitizer finishes.
            // Activating its ancestors during Spawn makes Sanitize reject it.
            for (Transform node = Wave.transform; node != null && node != transform; node = node.parent)
                node.gameObject.SetActive(true);
            StartCoroutine(EmitReady());
        }
        private IEnumerator EmitReady()
        {
            if (Delay > 0f) yield return new WaitForSeconds(Delay);
            if (Wave == null || !gameObject.activeInHierarchy || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) yield break;
            NativeBurstPlayback.EmitOnce(Wave, Count);
            if (Scene != ZNetScene.instance) { Scene = ZNetScene.instance; Traced = 0; }
            if (!MasteryPlugin.Settings.VerboseLogging.Value || Traced++ >= 6) yield break;
            yield return new WaitForSeconds(.16f);
            if (Wave == null || !gameObject.activeInHierarchy) yield break;
            var renderer = Wave.GetComponent<ParticleSystemRenderer>();
            int sampled = Wave.GetParticles(Probe);
            string particle = sampled > 0
                ? " position=" + Probe[0].position + " color=" + Probe[0].GetCurrentColor(Wave) + " size=" + Probe[0].GetCurrentSize(Wave).ToString("F3")
                : " sample=none";
            MasteryPlugin.Log.LogInfo("[GroundPressureTrace] radius=" + Radius.ToString("F2") + " native=block_wave particles=" + Wave.particleCount +
                " cameraVisible=" + (renderer != null && renderer.isVisible) + " bounds=" + (renderer != null ? renderer.bounds.size.ToString() : "none") +
                " emitter=" + Wave.transform.position + " simulation=" + Wave.main.simulationSpace + particle);
        }
        private void OnDisable() => StopAllCoroutines();
    }

    internal static class NativeContactSparks
    {
        private const int ParticleBudget = 32;
        internal static void Play(Vector3 center, bool secondary)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            GameObject source = NativePerkAssetResolver.Resolve("vfx_HitSparks");
            if (source == null)
            { NativeVfxSafeFrame.Request("vfx_HitSparks", loaded => Spawn(loaded, center, secondary)); return; }
            Spawn(source, center, secondary);
        }
        private static bool Spawn(GameObject source, Vector3 center, bool secondary)
        {
            if (Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return true;
            GameObject root = VfxPool.Spawn(source, center, Quaternion.identity, .65f);
            if (root == null) return false;
            NativeBurstPlayback.Mute(root);
            root.transform.localScale *= secondary ? .9f : .65f;
            ParticleSystem[] native = source.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            int[] counts = new int[systems.Length]; int remaining = ParticleBudget;
            for (int i = 0; i < Mathf.Min(native.Length, systems.Length); i++)
            {
                bool spark = NativeBurstPlayback.HasMaterial(native[i], "gnista");
                int count = spark ? Mathf.Min(secondary ? 24 : 16, remaining) : 0;
                counts[i] = count; remaining -= count;
                var main = systems[i].main;
                main.maxParticles = Mathf.Max(1, count); main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startSizeMultiplier *= 1.5f; main.startSpeedMultiplier *= .65f;
                var renderer = systems[i].GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.enabled = count > 0;
            }
            NativeVfxSafeFrame.AfterReady(root, () =>
            {
                if (root == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i] == null || counts[i] <= 0) continue;
                    for (Transform node = systems[i].transform; node != null && node != root.transform; node = node.parent)
                        node.gameObject.SetActive(true);
                    NativeBurstPlayback.EmitOnce(systems[i], counts[i]);
                }
            });
            root.SetActive(true);
            return true;
        }
    }

#if MASTERY_CLUBS70_EXPERIMENT
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    internal static class Mace70ImpactVisualPatch
    {
        private sealed class Played { }
        private sealed class Scope { internal Attack Previous; }
        private static readonly ConditionalWeakTable<Attack, Played> Seen = new ConditionalWeakTable<Attack, Played>();
        [ThreadStatic] internal static Attack CurrentAttack;

        private static void Prefix(Attack __instance, out Scope __state)
        {
            __state = new Scope { Previous = CurrentAttack };
            Player player = __instance?.m_character as Player;
            if (player == null || player != Player.m_localPlayer || player.m_nview?.IsOwner() != true ||
                !MasteryPlugin.Settings.Enabled.Value || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70) ||
                ClubWeaponClassService.Classify(__instance.m_weapon) != ClubWeaponClass.Mace ||
                Clubs70Reservation.Factor(__instance) <= 0f) return;
            CurrentAttack = __instance;
        }

        private static Exception Finalizer(Scope __state, Exception __exception)
        { CurrentAttack = __state?.Previous; return __exception; }

        internal static bool TryMarkContact(Attack attack)
        {
            if (attack == null || Seen.TryGetValue(attack, out _)) return false;
            Seen.Add(attack, new Played());
            return true;
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class Club70AreaImpactVisualPatch
    {
        private sealed class Scope { internal Attack Previous; }
        [ThreadStatic] internal static Attack CurrentAttack;

        private static void Prefix(Attack __instance, out Scope __state)
        {
            __state = new Scope { Previous = CurrentAttack };
            Player player = __instance?.m_character as Player;
            ClubWeaponClass kind = ClubWeaponClassService.Classify(__instance?.m_weapon);
            if (player == null || player != Player.m_localPlayer || player.m_nview?.IsOwner() != true ||
                !MasteryPlugin.Settings.Enabled.Value || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70) ||
                (kind != ClubWeaponClass.Mace && kind != ClubWeaponClass.SledgeHammer) ||
                Clubs70Reservation.Factor(__instance) <= 0f) return;
            CurrentAttack = __instance;
        }

        private static Exception Finalizer(Scope __state, Exception __exception)
        { CurrentAttack = __state?.Previous; return __exception; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Mace70ConfirmedContactPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Attack attack = Mace70ImpactVisualPatch.CurrentAttack ?? Club70AreaImpactVisualPatch.CurrentAttack;
            Player player = attack?.m_character as Player;
            if (attack == null || player == null || __instance == null || __instance == player ||
                hit == null || hit.GetAttacker() != player || !Mace70ImpactVisualPatch.TryMarkContact(attack)) return;
            Vector3 center = hit.m_point.sqrMagnitude > .001f ? hit.m_point : __instance.GetCenterPoint();
            bool secondary = AttackIntentService.IsSecondary(attack, player);
            ClubWeaponClass kind = ClubWeaponClassService.Classify(attack.m_weapon);
            if (kind == ClubWeaponClass.Mace) WeaponImpactVisualQueue.QueueMace70Impact(center, secondary);
            else if (kind == ClubWeaponClass.SledgeHammer) WeaponImpactVisualQueue.QueueHammer70Impact(center, secondary);
        }
    }
#endif
}
#endif
