#if MASTERY_CLUBS35_EXPERIMENT
using System.Collections.Generic;
using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Delayed club echoes use a real native hit as their template. The extra
    // strikes are character-only and owner-originated; they never hit pieces,
    // terrain, players, or tamed allies.
    internal static class Clubs70EchoService
    {
        private sealed class Capture
        {
            internal Player Source;
            internal HitData Hit;
            internal Vector3 Center;
            internal float Radius;
            internal HammerNativeEchoPresentation.Cue[] Cues;
            internal bool RealHit;
            internal bool NativeHitCue;
            internal Vector3 NativeHitCenter;
            internal float Due;
            internal int Remaining;
            internal int AttackId, EchoIndex;
        }
        private static readonly ConditionalWeakTable<Attack, Capture> Captures = new ConditionalWeakTable<Attack, Capture>();
        private static readonly List<Capture> Pending = new List<Capture>(24);
        private static ZNetScene Scene;
        private const int MaxPending = 24;
        private const float EchoDelay = 1.25f;

#if MASTERY_CLUBS35_EXPERIMENT
        internal static void BeginHammer(Attack attack, Vector3 center, float radius)
        {
            Player source = attack?.m_character as Player;
            if (source == null || source.m_nview?.IsOwner() != true ||
                ClubWeaponClassService.Classify(attack.m_weapon) != ClubWeaponClass.SledgeHammer ||
                !PerkRuntimeService.HasPerk(source, Skills.SkillType.Clubs, 35)) return;
            Capture capture = Captures.GetOrCreateValue(attack);
            if (capture.Source != null) return;
            capture.Source = source; capture.Center = center; capture.Radius = Mathf.Clamp(radius * .5f, .25f, 12f);
            capture.Cues = HammerNativeEchoPresentation.Capture(attack); capture.Hit = NativeHammerTemplate(attack, center);
#if MASTERY_CLUBS70_EXPERIMENT
            capture.Remaining = Clubs70Reservation.ChargeFraction(attack) >= .999f ? 2 : 1;
#else
            capture.Remaining = 1;
#endif
        }

        internal static void CaptureHammer(Attack attack, Character victim, HitData hit, Vector3 center, float radius)
        {
            Player source = attack?.m_character as Player;
            if (source == null || source.m_nview?.IsOwner() != true || victim == null || hit == null ||
                ClubWeaponClassService.Classify(attack.m_weapon) != ClubWeaponClass.SledgeHammer ||
                !PerkRuntimeService.HasPerk(source, Skills.SkillType.Clubs, 35) || !BaseAI.IsEnemy(source, victim)) return;
            Capture capture = Captures.GetOrCreateValue(attack);
            if (capture.RealHit) return;
            capture.Source = source; capture.Hit = hit.Clone(); capture.Center = center;
            capture.Radius = Mathf.Clamp(radius * .5f, .25f, 12f);
            if (capture.Cues == null) capture.Cues = HammerNativeEchoPresentation.Capture(attack);
            capture.RealHit = true;
#if MASTERY_CLUBS70_EXPERIMENT
            float charge = Clubs70Reservation.ChargeFraction(attack);
            capture.Remaining = charge >= .999f ? 2 : 1;
#else
            capture.Remaining = 1;
#endif
        }

        internal static void CompleteHammer(Attack attack)
        {
            if (attack == null || !Captures.TryGetValue(attack, out Capture capture) || capture.Hit == null || Pending.Count >= MaxPending) return;
            Queue(attack, capture, EchoDelay);
        }

        internal static void RecordNativeHitCue(Attack attack, Vector3 center)
        {
            if (attack != null && Captures.TryGetValue(attack, out Capture capture))
            { capture.NativeHitCue = true; capture.NativeHitCenter = center; }
        }

        private static HitData NativeHammerTemplate(Attack attack, Vector3 center)
        {
            ItemDrop.ItemData weapon = attack.m_weapon;
            Player source = attack.m_character as Player;
            if (weapon?.m_shared == null || source == null) return null;
            float skillFactor = source.GetRandomSkillFactor(weapon.m_shared.m_skillType);
            HitData hit = new HitData
            {
                m_toolTier = (short)weapon.m_shared.m_toolTier,
                m_skillLevel = source.GetSkillLevel(weapon.m_shared.m_skillType),
                m_itemLevel = (short)weapon.m_quality,
                m_itemWorldLevel = (byte)weapon.m_worldLevel,
                m_pushForce = weapon.m_shared.m_attackForce * skillFactor * attack.m_forceMultiplier,
                m_backstabBonus = weapon.m_shared.m_backstabBonus,
                m_staggerMultiplier = attack.m_staggerMultiplier,
                m_dodgeable = weapon.m_shared.m_dodgeable,
                m_blockable = weapon.m_shared.m_blockable,
                m_skill = weapon.m_shared.m_skillType,
                m_skillRaiseAmount = attack.m_raiseSkillAmount,
                m_damage = weapon.GetDamage(),
                m_point = center,
                m_dir = source.transform.forward,
                m_hitType = HitData.HitType.PlayerHit,
                m_healthReturn = attack.m_attackHealthReturnHit,
                m_eitrAdd = attack.m_attackEitrAdd,
                m_variant = weapon.m_shared.m_hitVariant
            };
            attack.ModifyDamage(hit, skillFactor);
            source.GetSEMan().ModifyAttack(weapon.m_shared.m_skillType, ref hit);
            return hit;
        }
#endif

        private static void Queue(Attack attack, Capture capture, float delay)
        {
            // Initialize before enqueue, so the first owner Update in a new
            // scene cannot silently discard the first scheduled echo.
            if (Scene != ZNetScene.instance) { Scene = ZNetScene.instance; Pending.Clear(); }
            if (Pending.Contains(capture)) return;
            capture.AttackId = RuntimeHelpers.GetHashCode(attack);
            capture.Due = Time.time + delay;
            Pending.Add(capture);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[ClubsEcho] queued actor=" + capture.Source.GetZDOID() + " attack=" + capture.AttackId +
                    " hammer=True realHit=" + capture.RealHit + " count=" + capture.Remaining +
                    " delay=" + delay.ToString("F2") + " radius=" + capture.Radius.ToString("F2"));
        }

        internal static void Tick(Player player)
        {
            if (player != Player.m_localPlayer || player?.m_nview?.IsOwner() != true) return;
            if (Scene != ZNetScene.instance) { Scene = ZNetScene.instance; Pending.Clear(); }
            for (int i = Pending.Count - 1; i >= 0; --i)
            {
                Capture capture = Pending[i];
                if (capture.Source == null || capture.Source.IsDead() || capture.Source.m_nview?.IsOwner() != true ||
                    !MasteryPlugin.Settings.Enabled.Value)
                { Pending.RemoveAt(i); continue; }
                if (Time.time < capture.Due) continue;
                Pending.RemoveAt(i);
                capture.EchoIndex++;
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[ClubsEcho] replay actor=" + capture.Source.GetZDOID() + " attack=" + capture.AttackId +
                        " index=" + capture.EchoIndex + " time=" + Time.time.ToString("F3") + " center=" + capture.Center);
                EchoHammer(capture);
            }
        }

        private static void EchoHammer(Capture capture)
        {
            Player source = capture.Source;
            if (source == null || source.IsDead()) return;
            if (Player.m_localPlayer == source)
            {
                HammerNativeEchoPresentation.Play(capture.Cues, capture.Center, capture.NativeHitCue, capture.NativeHitCenter);
            }
            if (capture.Hit == null) return;
            foreach (Character target in Character.GetAllCharacters())
            {
                if (target == null || target == source || target.IsPlayer() || target.IsTamed() || target.IsDead() ||
                    !BaseAI.IsEnemy(source, target) || !WithinEcho(capture, target)) continue;
                HitData echo = Prepare(capture.Hit, source, target, capture.Center);
                // Stagger is derived from damage * multiplier in native code;
                // damage at 50% with unchanged multiplier yields 50% final stagger.
                echo.m_damage.Modify(.5f); echo.m_pushForce *= .5f;
                TraceTarget(capture, target, echo);
                target.Damage(echo);
            }
            if (--capture.Remaining > 0) { capture.Due = Time.time + EchoDelay; Pending.Add(capture); }
        }

        private static void TraceTarget(Capture capture, Character target, HitData hit)
        {
            if (!MasteryPlugin.Settings.VerboseLogging.Value) return;
            MasteryPlugin.Log.LogInfo("[ClubsEcho] dispatch attack=" + capture.AttackId + " index=" + capture.EchoIndex +
                " target=" + target.GetZDOID() + " name=" + target.name + " damage=" + hit.GetTotalDamage().ToString("F2") +
                " hpBefore=" + target.GetHealth().ToString("F2") + " targetOwner=" + (target.m_nview?.IsOwner() == true) +
                " generated=" + PerkRuntimeService.IsPerkGenerated(hit));
        }

        private static bool WithinEcho(Capture capture, Character target)
        {
            Collider collider = target.GetCollider();
            Vector3 closest = collider != null ? collider.ClosestPoint(capture.Center) : target.GetCenterPoint();
            if ((closest - capture.Center).sqrMagnitude > capture.Radius * capture.Radius) return false;
            // A strike cannot travel through solid world geometry. Raising the
            // ray start slightly avoids treating the impact ground as a wall.
            Vector3 from = capture.Center + Vector3.up * .2f;
            int solidMask = LayerMask.GetMask("terrain", "static_solid", "piece", "Default");
            return !Physics.Linecast(from, closest, solidMask, QueryTriggerInteraction.Ignore);
        }

        private static HitData Prepare(HitData sourceHit, Player source, Character target, Vector3 center)
        {
            HitData hit = sourceHit.Clone();
            hit.m_variant = 1270; // Already treated as generated by the runtime.
            hit.m_skillRaiseAmount = 0f;
            hit.m_point = target.GetCenterPoint();
            Vector3 direction = target.GetCenterPoint() - center;
            hit.m_dir = direction.sqrMagnitude > .001f ? direction.normalized : sourceHit.m_dir;
            hit.SetAttacker(source);
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            context.IsPerkGenerated = true; context.PerkId = "clubs_70_echo";
            context.AllowSelfProc = false; context.AllowOtherPerkProc = false; context.XpMultiplier = 0f;
            context.IgnoreExecution = true;
            return hit;
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Clubs70EchoTick
    { private static void Postfix(Player __instance) => Clubs70EchoService.Tick(__instance); }

#if MASTERY_CLUBS35_EXPERIMENT
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class Hammer35EchoCompletion
    { private static void Postfix(Attack __instance) => Clubs70EchoService.CompleteHammer(__instance); }
#endif

}
#endif
