using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    internal enum Fists70MaulPhase { Idle, Leap, Attach, Hit1, Hit2, Hit3, Hit4, Hit5, Finisher, Detach, Recovery }

    internal struct Fists70PairKey : IEquatable<Fists70PairKey>
    {
        internal long PlayerId;
        internal ZDOID TargetId;
        public bool Equals(Fists70PairKey other) => PlayerId == other.PlayerId && TargetId == other.TargetId;
        public override bool Equals(object obj) => obj is Fists70PairKey other && Equals(other);
        public override int GetHashCode() => (PlayerId.GetHashCode() * 397) ^ TargetId.GetHashCode();
    }

    internal sealed class Fists70GaugeState
    {
        internal float Gauge;
        internal float LastGaugeHitAt = -99f;
        internal int LastGaugeAttackSerial;
        internal float LastPerfectParryAt = -99f;
        internal int Hits;
    }

    internal sealed class Fists70PlayerState
    {
        internal double CooldownUntil;
        internal float LastCooldownHitAt = -99f;
        internal int LastCooldownAttackSerial;
    }

    internal sealed class Fists70MaulRun
    {
        internal Player Player;
        internal Character Target;
        internal CreatureClass TargetClass;
        internal Fists70MaulPhase Phase;
        internal float StartedAt;
        internal float AttachAt;
        internal float[] HitTimes;
        internal float[] Weights;
        internal int NextHit;
        internal float TotalDamage;
        internal float TotalHeal;
        internal float TotalStamina;
        internal float ReservedRemaining;
        internal Vector3 StartPosition;
        internal Vector3 TargetLocalAnchor;
        internal Vector3 TargetLocalApproachAnchor;
        internal Vector3 TargetLocalExitAnchor;
    }

    internal static class Fists70TargetClassifier
    {
        private static readonly string[] HeavyTokens =
        {
            "troll", "stonegolem", "abomination", "lox", "seekersoldier", "seeker_soldier", "seekerbrute",
            "morgen", "bear", "goblinbrute", "goblin_brute", "gjall", "fallenvalkyrie",
            "fallen_valkyrie", "serpent"
        };
        private static readonly HashSet<string> LoggedUncertain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static CreatureClass Classify(Character target)
        {
            if (target == null) return CreatureClass.SmallNormal;
            if (target.IsBoss()) return CreatureClass.Boss;
            string id = ((target.m_nview?.GetPrefabName() ?? "") + " " + target.gameObject.name).Replace("(Clone)", "").ToLowerInvariant();
            foreach (string token in HeavyTokens) if (id.Contains(token)) return CreatureClass.Heavy;
            if (target.GetMaxHealth() >= 1000f && LoggedUncertain.Add(id))
                MasteryPlugin.Log.LogWarning("[Fists70] Unclassified high-health creature (treated Normal): " + id + " hp=" + target.GetMaxHealth().ToString("0"));
            return CreatureClass.SmallNormal;
        }
    }

    internal static class Fists70MaulService
    {
        internal const string PerkId = "Fists70Maul";
        internal const float CooldownSeconds = 300f;
        internal const float ActivationRange = 15f;
        private static readonly Dictionary<Fists70PairKey, Fists70GaugeState> Gauges = new Dictionary<Fists70PairKey, Fists70GaugeState>();
        private static readonly Dictionary<long, Fists70PlayerState> Players = new Dictionary<long, Fists70PlayerState>();
        private static readonly Dictionary<long, Fists70MaulRun> Active = new Dictionary<long, Fists70MaulRun>();

        private static bool IsAuthority => ZNet.instance != null && ZNet.instance.IsServer();
        private static Fists70PairKey Key(Player player, Character target) => new Fists70PairKey { PlayerId = player.GetPlayerID(), TargetId = target.GetZDOID() };
        private static Fists70PlayerState PlayerState(Player player)
        {
            long id = player.GetPlayerID();
            if (!Players.TryGetValue(id, out Fists70PlayerState state)) Players[id] = state = new Fists70PlayerState();
            return state;
        }
        private static Fists70GaugeState GaugeState(Player player, Character target)
        {
            Fists70PairKey key = Key(player, target);
            if (!Gauges.TryGetValue(key, out Fists70GaugeState state)) Gauges[key] = state = new Fists70GaugeState();
            return state;
        }

        internal static bool IsFistWeapon(Player player)
        {
            ItemDrop.ItemData weapon = player?.GetCurrentWeapon();
            return weapon?.m_shared?.m_skillType == Skills.SkillType.Unarmed;
        }

        internal static void ObserveActualDamage(Character victim, HitData hit, float healthBefore)
        {
            if (victim == null || hit == null) return;
            float actual = Mathf.Max(0f, healthBefore - victim.GetHealth());
            if (actual <= 0.001f) return;
            if (!IsAuthority)
            {
                bool parried = victim is Player p && hit.GetAttacker() != null &&
                    Gauges.TryGetValue(Key(p, hit.GetAttacker()), out Fists70GaugeState localGauge) &&
                    Time.time - localGauge.LastPerfectParryAt <= .70f;
                Fists70HitRelay.Send(victim, hit, actual, parried);
                return;
            }
            ObserveConfirmedDamage(victim, hit, actual);
        }
        internal static void ObserveConfirmedDamage(Character victim, HitData hit, float actual)
        {
            if (!IsAuthority || victim == null || hit == null || actual <= .001f) return;

            if (victim is Player damagedPlayer)
            {
                if (IsDirectAttackHit(hit)) ObservePlayerDamaged(damagedPlayer, hit.GetAttacker(), actual);
                else if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Fists70] GAUGE_LOSS_IGNORED reason=dot_or_aura hitType=" + hit.m_hitType + " damage=" + actual.ToString("0.0"));
                return;
            }

            Player attacker = hit.GetAttacker() as Player;
            if (attacker == null || hit.m_skill != Skills.SkillType.Unarmed || !BaseAI.IsEnemy(attacker, victim) ||
                PerkRuntimeService.IsPerkGenerated(hit) || MasteryAttackTagService.Has(hit, MasteryAttackTag.Fists70Maul) ||
                !OwnerSkillAuthority.Has(attacker, Skills.SkillType.Unarmed, 70)) return;

            Fists70PlayerState playerState = PlayerState(attacker);
            int attackSerial = MasteryAttackTagService.GetAttackSerial(hit);
            bool newCooldownAttack = attackSerial > 0
                ? attackSerial != playerState.LastCooldownAttackSerial
                : Time.time - playerState.LastCooldownHitAt >= 0.12f;
            if (newCooldownAttack)
            {
                playerState.LastCooldownHitAt = Time.time;
                playerState.LastCooldownAttackSerial = attackSerial;
                if (playerState.CooldownUntil > ZNet.instance.GetTimeSeconds())
                    playerState.CooldownUntil = Math.Max(ZNet.instance.GetTimeSeconds(), playerState.CooldownUntil - 5d);
            }

            CreatureClass targetClass = Fists70TargetClassifier.Classify(victim);
            if (targetClass == CreatureClass.SmallNormal) return;
            Fists70GaugeState gauge = GaugeState(attacker, victim);
            bool duplicateGaugeAttack = attackSerial > 0
                ? attackSerial == gauge.LastGaugeAttackSerial
                : Time.time - gauge.LastGaugeHitAt < 0.12f;
            if (duplicateGaugeAttack) return;
            gauge.LastGaugeHitAt = Time.time;
            gauge.LastGaugeAttackSerial = attackSerial;
            gauge.Hits++;
            gauge.Gauge = Mathf.Clamp01(gauge.Gauge + (targetClass == CreatureClass.Boss ? 0.05f : 0.10f));
            Sync(attacker, victim, targetClass, gauge);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Fists70] GAUGE player=" + attacker.GetPlayerName() + " target=" + victim.gameObject.name + " class=" + targetClass + " gauge=" + gauge.Gauge.ToString("P0") + " hits=" + gauge.Hits);
        }

        internal static bool IsDirectAttackHit(HitData hit)
        {
            if (hit == null || hit.GetAttacker() == null) return false;
            string type = hit.m_hitType.ToString().ToLowerInvariant();
            string[] periodic = { "poison", "burn", "freez", "smoke", "water", "drown", "fall", "edge", "self" };
            foreach (string token in periodic) if (type.Contains(token)) return false;
            HitData.DamageTypes damage = hit.m_damage;
            float physical = damage.m_blunt + damage.m_slash + damage.m_pierce + damage.m_chop + damage.m_pickaxe;
            return physical > 0.001f || hit.m_ranged;
        }

        private static void ObservePlayerDamaged(Player player, Character source, float actualDamage)
        {
            if (player == null || source == null) return;
            Fists70PairKey key = Key(player, source);
            if (!Gauges.TryGetValue(key, out Fists70GaugeState gauge) || gauge.Gauge <= 0f) return;
            // Perfect parry is authoritative even if Valheim lets a small amount of chip damage
            // through. HP loss alone is not enough to classify this as an unblocked hit.
            if (Time.time - gauge.LastPerfectParryAt <= 0.70f)
            {
                gauge.LastPerfectParryAt = -99f;
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Fists70] GAUGE_LOSS_IGNORED reason=perfect_parry source=" + source.gameObject.name + " chip=" + actualDamage.ToString("0.0"));
                return;
            }
            float loss = actualDamage >= player.GetMaxHealth() * 0.15f ? 0.30f : 0.20f;
            gauge.Gauge = Mathf.Max(0f, gauge.Gauge - loss);
            CreatureClass targetClass = Fists70TargetClassifier.Classify(source);
            Sync(player, source, targetClass, gauge);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Fists70] GAUGE_LOSS source=" + source.gameObject.name + " damage=" + actualDamage.ToString("0.0") + " loss=" + loss.ToString("P0"));
        }

        internal static void MarkPerfectParry(Player player, Character source)
        {
            if (player == null || source == null) return;
            if (!IsAuthority) { GaugeState(player, source).LastPerfectParryAt = Time.time; return; }
            Fists70PairKey key = Key(player, source);
            if (Gauges.TryGetValue(key, out Fists70GaugeState gauge)) gauge.LastPerfectParryAt = Time.time;
        }

        internal static void RequestActivation(Player player, Character target)
        {
            if (player == null || target == null) return;
            ZDOID id = target.GetZDOID();
            string request = "fists70_maul:" + id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);
            NetworkSync.SendClientAbility(request);
        }

        internal static void HandleAbility(Player player, string eventId)
        {
            if (!IsAuthority || player == null || string.IsNullOrEmpty(eventId) || !eventId.StartsWith("fists70_", StringComparison.Ordinal)) return;
            string[] p = eventId.Split(':');
            if (p[0] == "fists70_maul" && p.Length == 3 && long.TryParse(p[1], out long user) && uint.TryParse(p[2], out uint id))
            {
                Character target = ZNetScene.instance?.FindInstance(new ZDOID(user, id))?.GetComponent<Character>();
                TryBegin(player, target);
                return;
            }
            HandleDebug(player, p);
        }

        private static void TryBegin(Player player, Character target)
        {
            if (player == null || target == null || target.IsDead() || player.IsDead() || Active.ContainsKey(player.GetPlayerID()) ||
                !OwnerSkillAuthority.Has(player, Skills.SkillType.Unarmed, 70) || !IsFistWeapon(player) || !BaseAI.IsEnemy(player, target)) return;
            CreatureClass targetClass = Fists70TargetClassifier.Classify(target);
            bool boss = targetClass == CreatureClass.Boss;
            if (targetClass == CreatureClass.SmallNormal || (target.IsFlying() && !boss) || !player.IsOnGround()) return;
            Fists70GaugeState gauge = GaugeState(player, target);
            Fists70PlayerState playerState = PlayerState(player);
            double now = ZNet.instance.GetTimeSeconds();
            if (gauge.Gauge < 0.999f || playerState.CooldownUntil > now) { Sync(player, target, targetClass, gauge); return; }
            float maxDistance = ActivationRange + Mathf.Max(0.5f, target.GetRadius());
            if ((target.GetCenterPoint() - player.GetCenterPoint()).sqrMagnitude > maxDistance * maxDistance || !HasLineOfSight(player, target)) return;

            // Bare hands can build and trigger Maul, but a crafted fist weapon must remain
            // the stronger delivery method.  Snapshot the source at activation so changing
            // equipment mid-sequence cannot alter already reserved damage.
            bool bareHands = PerkRuntimeService.IsBareHands(player.GetCurrentWeapon());
            float sourceDamageMultiplier = bareHands ? 0.65f : 1f;
            // Deliberate maul cadence: the old sub-second chain read as one noisy proc.
            // Damage is unchanged; only the time between readable impacts is tripled.
            float[] offsets = boss ? new[] { 0.60f, 1.14f, 1.68f, 2.22f, 2.82f, 3.54f } : new[] { 0.75f, 1.50f, 2.55f };
            float[] weights = boss ? new[] { 0.10f, 0.12f, 0.14f, 0.16f, 0.18f, 0.30f } : new[] { 0.25f, 0.30f, 0.45f };
            Vector3 anchor = ResolveLandingAnchor(player, target, targetClass);
            Vector3 approach = ResolveApproachAnchor(target, targetClass, anchor);
            Vector3 exit = ResolveExitAnchor(player, target, targetClass);
            if (!HasLeapPath(player, approach, anchor) && !boss)
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogWarning("[Fists70] MAUL_REJECT path_blocked target=" + target.gameObject.name);
                return;
            }
            if (boss && MasteryPlugin.Settings.VerboseLogging.Value && !HasLeapPath(player, approach, anchor))
                MasteryPlugin.Log.LogInfo("[Fists70] MAUL_BOSS_PATH_FALLBACK target=" + target.gameObject.name);
            float leapDuration = ComputeLeapDuration(Vector3.Distance(player.transform.position, approach) + Vector3.Distance(approach, anchor) * 0.45f);
            Fists70MaulRun run = new Fists70MaulRun
            {
                Player = player, Target = target, TargetClass = targetClass, Phase = Fists70MaulPhase.Leap,
                StartedAt = Time.time, AttachAt = Time.time + leapDuration, HitTimes = offsets, Weights = weights,
                TotalDamage = target.GetMaxHealth() * (boss ? 0.12f : 0.36f) * sourceDamageMultiplier,
                TotalHeal = player.GetMaxHealth() * (boss ? 0.35f : 0.25f),
                TotalStamina = player.GetMaxStamina() * (boss ? 0.50f : 0.40f),
                StartPosition = player.transform.position, TargetLocalAnchor = target.transform.InverseTransformPoint(anchor),
                TargetLocalApproachAnchor = target.transform.InverseTransformPoint(approach),
                TargetLocalExitAnchor = target.transform.InverseTransformPoint(exit)
            };
            run.ReservedRemaining = run.TotalDamage;
            gauge.Gauge = 0f; gauge.Hits = 0;
            playerState.CooldownUntil = now + CooldownSeconds;
            Active[player.GetPlayerID()] = run;
            Sync(player, target, targetClass, gauge, run);
            Broadcast(run, "start", -1);
            MasteryPlugin.Log.LogInfo("[Fists70] MAUL_START player=" + player.GetPlayerName() + " target=" + target.gameObject.name + " class=" + targetClass + " source=" + (bareHands ? "bare_hands" : "fist_weapon") + " multiplier=" + sourceDamageMultiplier.ToString("0.00") + " reserved=" + run.TotalDamage.ToString("0.0"));
        }

        private static bool HasLineOfSight(Player player, Character target)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "piece_nonsolid", "terrain");
            Vector3 eye = player.GetEyePoint();
            CapsuleCollider collider = target.GetCollider();
            if (collider != null)
            {
                Vector3 surface = collider.ClosestPoint(eye);
                if ((surface - eye).sqrMagnitude < 0.01f || !Physics.Linecast(eye, surface, out RaycastHit _, mask, QueryTriggerInteraction.Ignore)) return true;
                Bounds bounds = collider.bounds;
                Vector3 upper = bounds.center + Vector3.up * bounds.extents.y * 0.55f;
                if (!Physics.Linecast(eye, upper, out _, mask, QueryTriggerInteraction.Ignore)) return true;
            }
            return !Physics.Linecast(eye, target.GetCenterPoint(), out _, mask, QueryTriggerInteraction.Ignore);
        }

        internal static float ComputeLeapDuration(float distance) => Mathf.Lerp(0.38f, 0.68f, Mathf.Clamp01(distance / ActivationRange));
        internal static float ComputeLeapHeight(float distance) => Mathf.Lerp(1.0f, 3.0f, Mathf.Clamp01(distance / ActivationRange));

        private static Vector3 ResolveLandingAnchor(Player player, Character target, CreatureClass targetClass)
        {
            CapsuleCollider capsule = target.GetCollider();
            Bounds bounds = capsule != null ? capsule.bounds : new Bounds(target.GetCenterPoint(), Vector3.one * Mathf.Max(1f, target.GetRadius() * 2f));
            string id = ((target.m_nview?.GetPrefabName() ?? "") + " " + target.gameObject.name).Replace("(Clone)", "").ToLowerInvariant();
            Vector3 forward = target.transform.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward; else forward.Normalize();
            Vector3 right = target.transform.right; right.y = 0f;
            if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(Vector3.up, forward); else right.Normalize();
            float longitudinalExtent = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.z) * bounds.extents.z;
            float lateralExtent = Mathf.Abs(right.x) * bounds.extents.x + Mathf.Abs(right.z) * bounds.extents.z;
            float height = 0.62f, neck = 0.18f;
            if (id.Contains("lox")) { height = 0.70f; neck = 0.32f; }
            else if (id.Contains("dragon") || id.Contains("moder")) { height = 0.66f; neck = 0.22f; }
            else if (id.Contains("eikthyr") || id.Contains("bear")) { height = 0.68f; neck = 0.28f; }
            else if (id.Contains("troll") || id.Contains("goblinbrute")) { height = 0.58f; neck = 0.08f; }
            else if (id.Contains("stonegolem")) { height = 0.64f; neck = 0.04f; }
            else if (id.Contains("queen") || id.Contains("fader")) { height = 0.66f; neck = 0.20f; }
            else if (id.Contains("bonemass")) { height = 0.56f; neck = 0.06f; }
            else if (id.Contains("yagluth") || id.Contains("goblinking")) { height = 0.54f; neck = 0.10f; }
            else if (id.Contains("gd_king") || id.Contains("elder")) { height = 0.60f; neck = 0.10f; }
            float sideSign = ((player.GetPlayerID() ^ target.GetZDOID().GetHashCode()) & 1L) == 0L ? -1f : 1f;
            return bounds.center + forward * (longitudinalExtent * neck) + right * (lateralExtent * 0.10f * sideSign) + Vector3.up * (bounds.extents.y * height + 0.12f);
        }

        private static float LandingFlankAngle(string id, CreatureClass targetClass)
        {
            // Long-bodied and oversized enemies are approached from a flank so the player
            // lands beside the hurtbox instead of inside the head, belly or boss root.
            if (id.Contains("dragon") || id.Contains("moder") || id.Contains("lox")) return 68f;
            if (id.Contains("queen") || id.Contains("seeker_soldier") || id.Contains("seekersoldier") || id.Contains("seekerbrute")) return 56f;
            if (id.Contains("fader") || id.Contains("goblinking") || id.Contains("yagluth")) return 50f;
            if (id.Contains("bonemass") || id.Contains("gd_king") || id.Contains("elder")) return 34f;
            if (id.Contains("eikthyr") || id.Contains("troll") || id.Contains("stonegolem") || id.Contains("morgen") || id.Contains("bear")) return 40f;
            return targetClass == CreatureClass.Boss ? 45f : targetClass == CreatureClass.Heavy ? 35f : 18f;
        }

        private static Vector3 ResolveApproachAnchor(Character target, CreatureClass targetClass, Vector3 landing)
        {
            CapsuleCollider capsule = target.GetCollider();
            Bounds bounds = capsule != null ? capsule.bounds : new Bounds(target.GetCenterPoint(), Vector3.one * Mathf.Max(1f, target.GetRadius() * 2f));
            Vector3 side = target.transform.right; side.y = 0f;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right; else side.Normalize();
            return landing + side * Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.35f, 0.55f, 1.65f) + Vector3.up * Mathf.Clamp(bounds.extents.y * 0.30f, 0.70f, 2.0f);
        }

        private static Vector3 ResolveExitAnchor(Player player, Character target, CreatureClass targetClass)
        {
            CapsuleCollider capsule = target.GetCollider();
            Bounds bounds = capsule != null ? capsule.bounds : new Bounds(target.GetCenterPoint(), Vector3.one * Mathf.Max(1f, target.GetRadius() * 2f));
            Vector3 back = -target.transform.forward; back.y = 0f;
            if (back.sqrMagnitude < 0.01f) { back = player.transform.position - bounds.center; back.y = 0f; }
            if (back.sqrMagnitude < 0.01f) back = Vector3.back; else back.Normalize();
            float sideSign = ((player.GetPlayerID() ^ target.GetZDOID().GetHashCode()) & 1L) == 0L ? -1f : 1f;
            Vector3 side = target.transform.right * sideSign; side.y = 0f;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right; else side.Normalize();
            float separation = Mathf.Max(target.GetRadius(), Mathf.Max(bounds.extents.x, bounds.extents.z)) + (targetClass == CreatureClass.Boss ? 1.65f : 1.15f);
            Vector3[] directions = { (back + side * 0.55f).normalized, (back - side * 0.55f).normalized, side, -side, back };
            int groundMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            foreach (Vector3 direction in directions)
            {
                Vector3 candidate = bounds.center + direction * separation;
                Vector3 probe = candidate + Vector3.up * Mathf.Max(3f, bounds.size.y + 1f);
                if (Physics.Raycast(probe, Vector3.down, out RaycastHit ground, Mathf.Max(10f, bounds.size.y + 7f), groundMask, QueryTriggerInteraction.Ignore))
                    return ground.point + Vector3.up * 0.06f;
            }
            return player.transform.position;
        }

        private static bool HasLeapPath(Player player, Vector3 approach, Vector3 anchor)
        {
            Vector3 start = player.GetCenterPoint() + Vector3.up * 0.15f;
            Vector3 safeLanding = anchor;
            float distance = Vector3.Distance(start, approach) + Vector3.Distance(approach, safeLanding);
            float height = ComputeLeapHeight(distance);
            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            Vector3 previous = start;
            for (int i = 1; i <= 14; ++i)
            {
                float t = i / 14f;
                Vector3 next;
                if (t <= 0.68f)
                {
                    float u = t / 0.68f;
                    next = Vector3.Lerp(start, approach, Mathf.SmoothStep(0f, 1f, u)) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * height * 0.45f);
                }
                else next = Vector3.Lerp(approach, safeLanding, Mathf.SmoothStep(0f, 1f, (t - 0.68f) / 0.32f));
                Vector3 delta = next - previous;
                if (delta.sqrMagnitude > 0.001f && Physics.SphereCast(previous, 0.22f, delta.normalized, out RaycastHit _, delta.magnitude, mask, QueryTriggerInteraction.Ignore)) return false;
                previous = next;
            }
            return true;
        }

        internal static void Tick(Player player)
        {
            if (!IsAuthority || player == null || !Active.TryGetValue(player.GetPlayerID(), out Fists70MaulRun run)) return;
            if (!Valid(run)) { Abort(run, "invalid_or_dead"); return; }
            float elapsed = Time.time - run.AttachAt;
            if (Time.time < run.AttachAt) { run.Phase = Fists70MaulPhase.Leap; return; }
            if (run.Phase == Fists70MaulPhase.Leap) run.Phase = Fists70MaulPhase.Attach;
            while (run.NextHit < run.HitTimes.Length && elapsed >= run.HitTimes[run.NextHit])
            {
                ApplyMaulHit(run, run.NextHit);
                run.NextHit++;
                if (!Valid(run)) { Abort(run, "target_died"); return; }
            }
            if (run.NextHit >= run.HitTimes.Length && elapsed >= run.HitTimes[run.HitTimes.Length - 1] + 0.22f)
            {
                run.Phase = Fists70MaulPhase.Detach;
                Broadcast(run, "end", run.NextHit);
                Cleanup(run);
            }
        }

        private static bool Valid(Fists70MaulRun run)
        {
            if (run?.Player == null || run.Target == null || run.Player.IsDead() || run.Target.IsDead()) return false;
            if ((run.Target.GetCenterPoint() - run.Player.GetCenterPoint()).sqrMagnitude > 24f * 24f) return false;
            return run.Player.GetCurrentWeapon()?.m_shared?.m_skillType == Skills.SkillType.Unarmed;
        }

        private static void ApplyMaulHit(Fists70MaulRun run, int index)
        {
            bool final = index == run.HitTimes.Length - 1;
            run.Phase = final ? Fists70MaulPhase.Finisher : (Fists70MaulPhase)((int)Fists70MaulPhase.Hit1 + index);
            float amount = run.TotalDamage * run.Weights[index];
            HitData hit = new HitData();
            hit.m_skill = Skills.SkillType.None; hit.m_damage.m_damage = amount;
            hit.m_point = run.Target.GetCenterPoint(); hit.m_dir = (run.Target.GetCenterPoint() - run.Player.GetCenterPoint()).normalized;
            hit.m_pushForce = final ? 4f : 0f; hit.m_staggerMultiplier = 0f; hit.SetAttacker(run.Player);
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            context.IsPerkGenerated = true; context.PerkId = PerkId;
            context.AllowSelfProc = false; context.AllowOtherPerkProc = false;
            context.IgnoreReflect = true; context.IgnoreExecution = true; context.IgnoreOverdrawPayload = true;
            MasteryAttackTagService.Add(hit, MasteryAttackTag.Fists70Maul);
            run.Target.Damage(hit);
            run.ReservedRemaining = Mathf.Max(0f, run.ReservedRemaining - amount);
            run.Player.Heal(run.TotalHeal * run.Weights[index], true);
            PerkRuntimeService.RestoreStamina(run.Player, run.TotalStamina * run.Weights[index]);
            Broadcast(run, final ? "finisher" : "hit", index);
            Fists70GaugeState gauge = GaugeState(run.Player, run.Target);
            Sync(run.Player, run.Target, run.TargetClass, gauge, run);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Fists70] MAUL_HIT index=" + (index + 1) + " damage=" + amount.ToString("0.0") + " reserved=" + run.ReservedRemaining.ToString("0.0"));
        }

        private static void Abort(Fists70MaulRun run, string reason)
        {
            if (run == null) return;
            Broadcast(run, "abort", run.NextHit);
            MasteryPlugin.Log.LogWarning("[Fists70] MAUL_ABORT reason=" + reason + " hit=" + run.NextHit);
            Cleanup(run);
        }

        private static void Cleanup(Fists70MaulRun run)
        {
            if (run?.Player != null) Active.Remove(run.Player.GetPlayerID());
            if (run != null) { run.Phase = Fists70MaulPhase.Recovery; run.ReservedRemaining = 0f; }
        }

        internal static bool IsActive(Player player) => player != null && Active.ContainsKey(player.GetPlayerID());
        internal static float CooldownRemaining(Player player) => player == null || !Players.TryGetValue(player.GetPlayerID(), out Fists70PlayerState s) ? 0f : Mathf.Max(0f, (float)(s.CooldownUntil - (ZNet.instance?.GetTimeSeconds() ?? 0d)));

        internal static void RemoveCharacter(Character character)
        {
            if (character == null) return;
            ZDOID targetId = character.GetZDOID();
            long playerId = character is Player player ? player.GetPlayerID() : 0L;
            foreach (Fists70PairKey key in new List<Fists70PairKey>(Gauges.Keys))
                if (key.TargetId == targetId || (playerId != 0L && key.PlayerId == playerId)) Gauges.Remove(key);
            if (playerId != 0L) { Active.Remove(playerId); Players.Remove(playerId); }
            foreach (KeyValuePair<long, Fists70MaulRun> pair in new List<KeyValuePair<long, Fists70MaulRun>>(Active))
                if (pair.Value?.Target == character || pair.Value?.Player == character) Active.Remove(pair.Key);
        }

        internal static void Reset()
        {
            Gauges.Clear(); Players.Clear(); Active.Clear();
        }

        private static void Sync(Player player, Character target, CreatureClass targetClass, Fists70GaugeState gauge, Fists70MaulRun run = null)
        {
            if (player == null || target == null) return;
            ZDOID id = target.GetZDOID();
            string payload = string.Join(":", new[] { "fists70_state", id.UserID.ToString(CultureInfo.InvariantCulture), id.ID.ToString(CultureInfo.InvariantCulture),
                ((int)targetClass).ToString(CultureInfo.InvariantCulture), gauge.Gauge.ToString("0.####", CultureInfo.InvariantCulture), gauge.Hits.ToString(CultureInfo.InvariantCulture),
                CooldownRemaining(player).ToString("0.0", CultureInfo.InvariantCulture), ((int)(run?.Phase ?? Fists70MaulPhase.Idle)).ToString(CultureInfo.InvariantCulture),
                (run?.ReservedRemaining ?? 0f).ToString("0.0", CultureInfo.InvariantCulture), target.GetMaxHealth().ToString("0.0", CultureInfo.InvariantCulture) });
            if (player == Player.m_localPlayer) Fists70ClientService.TryHandle(payload, target.GetCenterPoint());
            else NetworkSync.SendProcFeedback(player, payload, target.GetCenterPoint());
        }

        private static void Broadcast(Fists70MaulRun run, string phase, int index)
        {
            if (run?.Player == null || run.Target == null) return;
            ZDOID id = run.Target.GetZDOID();
            string payload = string.Join(":", new[] { "fists70_visual", phase, run.Player.GetPlayerID().ToString(CultureInfo.InvariantCulture),
                id.UserID.ToString(CultureInfo.InvariantCulture), id.ID.ToString(CultureInfo.InvariantCulture), index.ToString(CultureInfo.InvariantCulture),
                ((int)run.TargetClass).ToString(CultureInfo.InvariantCulture), run.TargetLocalAnchor.x.ToString("0.###", CultureInfo.InvariantCulture),
                run.TargetLocalAnchor.y.ToString("0.###", CultureInfo.InvariantCulture), run.TargetLocalAnchor.z.ToString("0.###", CultureInfo.InvariantCulture),
                run.TargetLocalApproachAnchor.x.ToString("0.###", CultureInfo.InvariantCulture), run.TargetLocalApproachAnchor.y.ToString("0.###", CultureInfo.InvariantCulture),
                run.TargetLocalApproachAnchor.z.ToString("0.###", CultureInfo.InvariantCulture), run.TargetLocalExitAnchor.x.ToString("0.###", CultureInfo.InvariantCulture),
                run.TargetLocalExitAnchor.y.ToString("0.###", CultureInfo.InvariantCulture), run.TargetLocalExitAnchor.z.ToString("0.###", CultureInfo.InvariantCulture) });
            if (Player.m_localPlayer != null) Fists70ClientService.TryHandle(payload, run.Target.GetCenterPoint());
            NetworkSync.BroadcastProcFeedback(payload, run.Target.GetCenterPoint());
        }

        private static void HandleDebug(Player player, string[] p)
        {
            Character target = Fists70ClientService.FindTarget(player, 30f);
            if (target == null) return;
            Fists70GaugeState gauge = GaugeState(player, target);
            CreatureClass targetClass = Fists70TargetClassifier.Classify(target);
            if (p[0] == "fists70_debug_gauge" && p.Length >= 2 && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) { gauge.Gauge = Mathf.Clamp01(value); gauge.Hits = Mathf.RoundToInt(gauge.Gauge * (targetClass == CreatureClass.Boss ? 20f : 10f)); }
            else if (p[0] == "fists70_debug_ready") { gauge.Gauge = 1f; gauge.Hits = targetClass == CreatureClass.Boss ? 20 : 10; }
            else if (p[0] == "fists70_debug_cooldown" && p.Length >= 2 && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)) PlayerState(player).CooldownUntil = ZNet.instance.GetTimeSeconds() + Mathf.Max(0f, seconds);
            Sync(player, target, targetClass, gauge, Active.TryGetValue(player.GetPlayerID(), out Fists70MaulRun run) ? run : null);
        }
    }

    // Stamp the live Attack instance into serialized HitData so one swing cannot fill the
    // gauge or reduce cooldown multiple times through several target colliders.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class Fists70AttackIdentityPatch
    {
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack =
            AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");

        private static void Prefix(HitData hit)
        {
            Player attacker = hit?.GetAttacker() as Player;
            if (attacker == null || hit.m_skill != Skills.SkillType.Unarmed || PerkRuntimeService.IsPerkGenerated(hit)) return;
            Attack attack = CurrentAttack(attacker);
            if (attack == null) return;
            int serial = RuntimeHelpers.GetHashCode(attack) & ((1 << 17) - 1);
            MasteryAttackTagService.SetAttackSerial(hit, Math.Max(1, serial));
        }
    }

    // HitData is serialized between the attacker and the target owner. Restore the runtime-only
    // context from the serialized tag before any downstream perk hook sees the generated Maul hit.
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    [HarmonyPriority(Priority.First)]
    internal static class Fists70GeneratedContextRestorePatch
    {
        private static void Prefix(HitData hit)
        {
            if (!MasteryAttackTagService.Has(hit, MasteryAttackTag.Fists70Maul)) return;
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            context.IsPerkGenerated = true;
            context.PerkId = Fists70MaulService.PerkId;
            context.AllowSelfProc = false;
            context.AllowOtherPerkProc = false;
            context.IgnoreReflect = true;
            context.IgnoreExecution = true;
            context.IgnoreOverdrawPayload = true;
        }
    }
}

namespace ValheimMastery
{
    internal sealed class Fists70ClientTargetState
    {
        internal Character Target;
        internal CreatureClass TargetClass;
        internal float Gauge;
        internal int Hits;
        internal float CooldownUntil;
        internal Fists70MaulPhase Phase;
        internal float ReservedRemaining;
        internal float TargetMaxHealth;
    }

    internal static class Fists70ClientService
    {
        private static readonly Dictionary<ZDOID, Fists70ClientTargetState> States = new Dictionary<ZDOID, Fists70ClientTargetState>();
        private static readonly Dictionary<long, Fists70ClientMaulVisual> Visuals = new Dictionary<long, Fists70ClientMaulVisual>();
        internal static float FallProtectionUntil;

        internal static Character FindTarget(Player player, float range = Fists70MaulService.ActivationRange)
        {
            if (player == null) return null;
            Vector3 origin = player.GetEyePoint();
            Vector3 forward = player.GetAimDir(origin).normalized;
            RaycastHit[] hits = Physics.SphereCastAll(origin, 0.22f, forward, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                Character candidate = hit.collider?.GetComponentInParent<Character>();
                if (candidate == player) continue;
                if (candidate == null)
                {
                    // Solid terrain before the target breaks the lock.
                    if (hit.collider != null && !hit.collider.isTrigger) return null;
                    continue;
                }
                if (candidate.IsDead() || candidate.IsPlayer() || !BaseAI.IsEnemy(player, candidate)) continue;
                CreatureClass cls = Fists70TargetClassifier.Classify(candidate);
                if (cls == CreatureClass.SmallNormal) continue;
                return candidate;
            }
            return null;
        }

        internal static bool IsReady(Character target)
        {
            if (target == null || !States.TryGetValue(target.GetZDOID(), out Fists70ClientTargetState state)) return false;
            return state.Gauge >= 0.999f && Mathf.Max(0f, state.CooldownUntil - Time.time) <= 0f;
        }

        internal static bool TryGet(Character target, out Fists70ClientTargetState state)
        {
            state = null;
            if (target == null || !States.TryGetValue(target.GetZDOID(), out state)) return false;
            if (state.Target == null) state.Target = target;
            return true;
        }

        internal static bool TryHandle(string payload, Vector3 fallback)
        {
            if (string.IsNullOrEmpty(payload)) return false;
            string[] p = payload.Split(':');
            if (p[0] == "fists70_state" && p.Length == 10)
            {
                if (!long.TryParse(p[1], out long user) || !uint.TryParse(p[2], out uint id) || !int.TryParse(p[3], out int cls) ||
                    !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float gauge) || !int.TryParse(p[5], out int hits) ||
                    !float.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float cooldown) || !int.TryParse(p[7], out int phase) ||
                    !float.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float reserved) ||
                    !float.TryParse(p[9], NumberStyles.Float, CultureInfo.InvariantCulture, out float maxHealth)) return true;
                ZDOID zdoid = new ZDOID(user, id);
                if (!States.TryGetValue(zdoid, out Fists70ClientTargetState state)) States[zdoid] = state = new Fists70ClientTargetState();
                state.Target = ZNetScene.instance?.FindInstance(zdoid)?.GetComponent<Character>();
                state.TargetClass = (CreatureClass)cls; state.Gauge = Mathf.Clamp01(gauge); state.Hits = hits;
                state.CooldownUntil = Time.time + Mathf.Max(0f, cooldown); state.Phase = (Fists70MaulPhase)phase;
                state.ReservedRemaining = Mathf.Max(0f, reserved); state.TargetMaxHealth = Mathf.Max(1f, maxHealth);
                return true;
            }
            if (p[0] != "fists70_visual" || p.Length != 16) return false;
            if (!long.TryParse(p[2], out long playerId) || !long.TryParse(p[3], out long userId) || !uint.TryParse(p[4], out uint targetId) ||
                !int.TryParse(p[5], out int index) || !int.TryParse(p[6], out int targetClass) ||
                !float.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float ax) ||
                !float.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float ay) ||
                !float.TryParse(p[9], NumberStyles.Float, CultureInfo.InvariantCulture, out float az) ||
                !float.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float px) ||
                !float.TryParse(p[11], NumberStyles.Float, CultureInfo.InvariantCulture, out float py) ||
                !float.TryParse(p[12], NumberStyles.Float, CultureInfo.InvariantCulture, out float pz) ||
                !float.TryParse(p[13], NumberStyles.Float, CultureInfo.InvariantCulture, out float ex) ||
                !float.TryParse(p[14], NumberStyles.Float, CultureInfo.InvariantCulture, out float ey) ||
                !float.TryParse(p[15], NumberStyles.Float, CultureInfo.InvariantCulture, out float ez)) return true;
            Character target = ZNetScene.instance?.FindInstance(new ZDOID(userId, targetId))?.GetComponent<Character>();
            Player attacker = Player.GetPlayer(playerId);
            string phaseName = p[1];
            if (attacker == null) return true;
            if (target == null)
            {
                if (phaseName != "start") StopVisual(playerId, attacker, phaseName);
                return true;
            }
            if (phaseName == "start") StartVisual(attacker, target, (CreatureClass)targetClass, new Vector3(ax, ay, az), new Vector3(px, py, pz), new Vector3(ex, ey, ez));
            else if (phaseName == "hit" || phaseName == "finisher") HitVisual(attacker, target, index, phaseName == "finisher");
            else StopVisual(playerId, attacker, phaseName);
            return true;
        }

        private static void StartVisual(Player attacker, Character target, CreatureClass targetClass, Vector3 localAnchor, Vector3 localApproach, Vector3 localExit)
        {
            StopVisual(attacker.GetPlayerID(), attacker, "replace");
            GameObject root = new GameObject("ValheimMastery_Fists70MaulVisual");
            Fists70ClientMaulVisual visual = root.AddComponent<Fists70ClientMaulVisual>();
            visual.Initialize(attacker, target, targetClass, localAnchor, localApproach, localExit);
            Visuals[attacker.GetPlayerID()] = visual;
            attacker.m_zanim?.SetTrigger("jump");
            MasteryVfxMaterial.SpawnPrefab("fx_land", attacker.transform.position, 0.65f);
            PerkAudioService.Play("fists70_leap_start", "sfx_fenring_jump_start", attacker.transform.position, 0.25f);
        }

        private static void HitVisual(Player attacker, Character target, int index, bool finisher)
        {
            if (Visuals.TryGetValue(attacker.GetPlayerID(), out Fists70ClientMaulVisual visual)) visual.PulseAnimation(index, finisher);
            float side = index % 2 == 0 ? -1f : 1f;
            Vector3 point = target.GetCenterPoint() + target.transform.right * (side * Mathf.Clamp(target.GetRadius() * 0.18f, 0.12f, 0.75f)) +
                Vector3.up * Mathf.Clamp(0.08f + index * 0.07f, 0.08f, 0.45f);
            string effect = finisher ? "fx_goblinbrute_groundslam" : "vfx_BloodHit";
            string sound = finisher ? "sfx_frozenking_punchaoe_punch_third" : (index < 2 ? "sfx_unarmed_hit" : "sfx_fenring_claw_hit");
            MasteryVfxMaterial.SpawnPrefab(effect, point, finisher ? 0.48f : 0.58f + 0.06f * index);
            if (finisher) MasteryVfxMaterial.SpawnPrefab("vfx_HitSparks", point, 0.85f);
            PerkAudioService.Play("fists70_hit_" + index, sound, point, finisher ? 0.80f : 0.18f);
            if (finisher && MasteryPlugin.Settings.EnablePerkProcVFX.Value && GameCamera.instance != null)
                GameCamera.instance.AddShake(point, 6f, 0.25f, false);
        }

        private static void StopVisual(long playerId, Player attacker, string reason)
        {
            bool graceful = reason == "end" || reason == "abort" || reason == "target_destroyed";
            if (Visuals.TryGetValue(playerId, out Fists70ClientMaulVisual visual) && visual != null)
            {
                if (graceful) visual.BeginDismount();
                else UnityEngine.Object.Destroy(visual.gameObject);
            }
            else Visuals.Remove(playerId);
            if (attacker == Player.m_localPlayer)
            {
                if (attacker.m_animator != null) attacker.m_animator.speed = 1f;
            }
        }

        internal static bool LocalSequenceActive(Player player) => player != null && Visuals.ContainsKey(player.GetPlayerID());
        internal static IEnumerable<KeyValuePair<ZDOID, Fists70ClientTargetState>> AllStates => States;

        internal static void VisualDestroyed(long playerId, Fists70ClientMaulVisual visual)
        {
            if (Visuals.TryGetValue(playerId, out Fists70ClientMaulVisual current) && current == visual) Visuals.Remove(playerId);
        }

        internal static void RemoveCharacter(Character character)
        {
            if (character == null) return;
            States.Remove(character.GetZDOID());
            if (character is Player player) StopVisual(player.GetPlayerID(), player, "destroyed");
            foreach (KeyValuePair<long, Fists70ClientMaulVisual> pair in new List<KeyValuePair<long, Fists70ClientMaulVisual>>(Visuals))
                if (pair.Value == null || pair.Value.TargetMatches(character)) StopVisual(pair.Key, null, "target_destroyed");
        }

        internal static void Reset()
        {
            foreach (Fists70ClientMaulVisual visual in new List<Fists70ClientMaulVisual>(Visuals.Values))
                if (visual != null) UnityEngine.Object.Destroy(visual.gameObject);
            Visuals.Clear(); States.Clear(); FallProtectionUntil = 0f;
            Fists70TargetLockService.Clear();
        }
    }

    internal static class Fists70TargetLockService
    {
        private static Character Candidate;
        private static Fists70TargetLockMarker Marker;

        internal static Character Refresh(Player player)
        {
            Character next = null;
            bool eligible = player != null && player == Player.m_localPlayer && !player.IsDead() &&
                Fists70MaulService.IsFistWeapon(player) &&
                PerkRuntimeService.HasPerk(player, Skills.SkillType.Unarmed, 70) &&
                !Fists70ClientService.LocalSequenceActive(player);
            if (eligible)
            {
                Character aimed = Fists70ClientService.FindTarget(player);
                if (aimed != null && Fists70ClientService.IsReady(aimed)) next = aimed;
            }
            Set(next);
            return next;
        }

        internal static void Clear() => Set(null);

        private static void Set(Character target)
        {
            if (Candidate == target && (target == null || Marker != null)) return;
            if (Marker != null) UnityEngine.Object.Destroy(Marker.gameObject);
            Candidate = target;
            Marker = null;
            if (target == null) return;
            GameObject root = new GameObject("ValheimMastery_Fists70TargetLock");
            root.transform.SetParent(target.transform, false);
            Marker = root.AddComponent<Fists70TargetLockMarker>();
            Marker.Initialize(target);
        }

        internal static bool IsCandidate(Character target) => target != null && target == Candidate;
    }

    internal sealed class Fists70TargetLockMarker : MonoBehaviour
    {
        private Character Target;
        private Light Glow;
        private readonly List<LineRenderer> Brackets = new List<LineRenderer>();

        internal void Initialize(Character target)
        {
            Target = target;
            float radius = Mathf.Clamp(target.GetRadius(), 0.45f, 2.2f);
            Glow = gameObject.AddComponent<Light>();
            Glow.type = LightType.Point;
            Glow.color = new Color(1f, 0.34f, 0.06f);
            Glow.range = Mathf.Max(2.5f, radius * 2.8f);
            Glow.intensity = 2.2f;
            Glow.shadows = LightShadows.None;
            Brackets.Add(MakeBracket(-1f, radius));
            Brackets.Add(MakeBracket(1f, radius));
            SetVisible(MasteryPlugin.Settings.EnablePerkProcVFX.Value);
        }

        private LineRenderer MakeBracket(float side, float radius)
        {
            GameObject child = new GameObject(side < 0f ? "maul_lock_left" : "maul_lock_right");
            child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            MasteryVfxMaterial.AssignOwned(line, "vfx_HitSparks");
            line.useWorldSpace = false;
            line.positionCount = 3;
            float inner = radius * 0.52f;
            float outer = radius * 0.82f;
            line.SetPosition(0, new Vector3(side * outer, radius * 0.42f, 0f));
            line.SetPosition(1, new Vector3(side * inner, 0f, 0f));
            line.SetPosition(2, new Vector3(side * outer, -radius * 0.42f, 0f));
            line.startWidth = Mathf.Max(0.045f, radius * 0.065f);
            line.endWidth = Mathf.Max(0.018f, radius * 0.025f);
            return line;
        }

        private void SetVisible(bool visible)
        {
            if (Glow != null) Glow.enabled = visible;
            foreach (LineRenderer line in Brackets)
                if (line != null) line.enabled = visible;
        }

        private void LateUpdate()
        {
            if (Target == null || Target.IsDead() || !Fists70TargetLockService.IsCandidate(Target))
            {
                Destroy(gameObject);
                return;
            }
            bool visible = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            SetVisible(visible);
            if (!visible) return;
            float radius = Mathf.Clamp(Target.GetRadius(), 0.45f, 2.2f);
            Camera camera = Camera.main;
            Vector3 towardCamera = camera != null ? (camera.transform.position - Target.GetCenterPoint()).normalized : -Target.transform.forward;
            transform.position = Target.GetCenterPoint() + Vector3.up * (radius * 0.25f) + towardCamera * (radius + 0.12f);
            if (camera != null) transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up);
            float pulse = 0.70f + Mathf.PingPong(Time.time * 4.5f, 0.30f);
            Color color = new Color(1f, 0.30f, 0.04f, pulse);
            foreach (LineRenderer line in Brackets)
                if (line != null) line.startColor = line.endColor = color;
            if (Glow != null) Glow.intensity = 1.8f + Mathf.PingPong(Time.time * 5f, 2.2f);
        }
    }

    internal sealed class Fists70ClientMaulVisual : MonoBehaviour
    {
        private Player Attacker;
        private Character Target;
        private CreatureClass TargetClass;
        private Vector3 LocalAnchor;
        private Vector3 LocalApproach;
        private Vector3 LocalExit;
        private Vector3 Start;
        private float StartedAt;
        private float LastAnimAt;
        private float LeapDuration;
        private float LeapHeight;
        private bool Attached;
        private bool Dismounting;
        private float DismountStarted;
        private Vector3 DismountStart;
        private Vector3 DismountEnd;

        internal void Initialize(Player attacker, Character target, CreatureClass targetClass, Vector3 localAnchor, Vector3 localApproach, Vector3 localExit)
        {
            Attacker = attacker; Target = target; TargetClass = targetClass; LocalAnchor = localAnchor; LocalApproach = localApproach; LocalExit = localExit;
            Start = attacker.transform.position; StartedAt = Time.time;
            Vector3 anchor = target.transform.TransformPoint(localAnchor);
            Vector3 approach = target.transform.TransformPoint(localApproach);
            float distance = Vector3.Distance(Start, approach) + Vector3.Distance(approach, anchor) * 0.45f;
            LeapDuration = Fists70MaulService.ComputeLeapDuration(distance);
            LeapHeight = Fists70MaulService.ComputeLeapHeight(distance);
        }

        private void Update()
        {
            if (Attacker == null || Attacker.IsDead()) { Destroy(gameObject); return; }
            if (Attacker != Player.m_localPlayer) return;
            if (Dismounting) { UpdateDismount(); return; }
            if (Target == null) { Destroy(gameObject); return; }
            if (Target.IsDead()) { BeginDismount(); return; }
            Vector3 anchor = Target.transform.TransformPoint(LocalAnchor);
            Vector3 approach = Target.transform.TransformPoint(LocalApproach);
            float t = Mathf.Clamp01((Time.time - StartedAt) / Mathf.Max(0.01f, LeapDuration));
            Vector3 desired;
            if (t < 0.68f)
            {
                float u = t / 0.68f;
                desired = Vector3.Lerp(Start, approach, Mathf.SmoothStep(0f, 1f, u)) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * LeapHeight * 0.45f);
            }
            else if (t < 1f) desired = Vector3.Lerp(approach, anchor, Mathf.SmoothStep(0f, 1f, (t - 0.68f) / 0.32f));
            else desired = anchor;
            if (!Attached && t >= 1f)
            {
                Attached = true;
                MasteryVfxMaterial.SpawnPrefab("fx_land", anchor, 0.62f);
                PerkAudioService.Play("fists70_leap_land", "sfx_fenring_jump_trigger", anchor, 0.25f);
            }
            Vector3 facing = t >= 0.78f ? Target.transform.forward : Target.GetCenterPoint() - desired;
            facing.y = 0f; if (facing.sqrMagnitude < 0.01f) facing = Attacker.transform.forward;
            Quaternion rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            Attacker.transform.SetPositionAndRotation(desired, rotation);
            if (Attacker.m_body != null) { Attacker.m_body.position = desired; Attacker.m_body.rotation = rotation; Attacker.m_body.linearVelocity = Vector3.zero; }
        }

        internal void BeginDismount()
        {
            if (Dismounting || Attacker == null) return;
            Dismounting = true; DismountStarted = Time.time; DismountStart = Attacker.transform.position;
            DismountEnd = Target != null ? Target.transform.TransformPoint(LocalExit) : DismountStart - Attacker.transform.forward * 1.5f;
            int groundMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            Vector3 probe = DismountEnd + Vector3.up * 4f;
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit ground, 9f, groundMask, QueryTriggerInteraction.Ignore) && Vector3.Dot(ground.normal, Vector3.up) >= 0.48f)
                DismountEnd = ground.point + Vector3.up * 0.06f;
            else DismountEnd = Start;
            Attacker.m_zanim?.SetTrigger("jump");
            MasteryVfxMaterial.SpawnPrefab("fx_land", DismountStart, 0.42f);
        }

        private void UpdateDismount()
        {
            const float duration = 0.62f;
            float t = Mathf.Clamp01((Time.time - DismountStarted) / duration);
            Vector3 desired = Vector3.Lerp(DismountStart, DismountEnd, Mathf.SmoothStep(0f, 1f, t)) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.72f);
            Vector3 facing = DismountEnd - DismountStart; facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = Attacker.transform.forward;
            Quaternion rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            Attacker.transform.SetPositionAndRotation(desired, rotation);
            if (Attacker.m_body != null) { Attacker.m_body.position = desired; Attacker.m_body.rotation = rotation; Attacker.m_body.linearVelocity = Vector3.zero; }
            if (t < 1f) return;
            Fists70ClientService.FallProtectionUntil = Time.time + 1.5f;
            MasteryVfxMaterial.SpawnPrefab("fx_land", DismountEnd, 0.38f);
            Destroy(gameObject);
        }

        internal void PulseAnimation(int index, bool finisher)
        {
            if (Attacker == null || Time.time - LastAnimAt < 0.08f) return;
            LastAnimAt = Time.time;
            ItemDrop.ItemData weapon = Attacker.GetCurrentWeapon();
            Attack primary = weapon?.m_shared?.m_attack;
            Attack secondary = weapon?.m_shared?.m_secondaryAttack;
            // Every Maul impact is a kick. Alternating fist attacks looked like an ordinary
            // combo and made the sequence visually incoherent. The finisher uses the same
            // kick clip at a deliberately heavier cadence; its impact VFX/audio carry the mass.
            string animation = secondary?.m_attackAnimation;
            if (string.IsNullOrEmpty(animation)) animation = primary?.m_attackAnimation;
            if (string.IsNullOrEmpty(animation)) animation = "unarmed_attack";
            Attacker.m_zanim?.SetTrigger(animation);
            if (Attacker.m_animator != null) Attacker.m_animator.speed = finisher ? 1.08f : 1.62f;
        }

        internal bool TargetMatches(Character character) => Target == character;

        private void OnDestroy()
        {
            if (Attacker?.m_animator != null) Attacker.m_animator.speed = 1f;
            if (Attacker != null) Fists70ClientService.VisualDestroyed(Attacker.GetPlayerID(), this);
        }
    }

    [HarmonyPatch(typeof(Character), "OnDestroy")]
    internal static class Fists70CharacterCleanupPatch
    {
        private static void Prefix(Character __instance)
        {
            Fists70MaulService.RemoveCharacter(__instance);
            Fists70ClientService.RemoveCharacter(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class Fists70WorldCleanupPatch
    {
        private static void Prefix()
        {
            Fists70MaulService.Reset();
            Fists70ClientService.Reset();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class Fists70TickPatch
    {
        private static void Postfix(Player __instance)
        {
            Fists70MaulService.Tick(__instance);
            if (__instance == Player.m_localPlayer)
            {
                Fists70TargetLockService.Refresh(__instance);
                if (__instance.GetComponent<Fists70DebugOverlay>() == null)
                    __instance.gameObject.AddComponent<Fists70DebugOverlay>();
            }
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.First)]
    internal static class Fists70DamageObserverPatch
    {
        private static void Prefix(Character __instance, HitData hit, out float __state)
        {
            __state = __instance != null ? __instance.GetHealth() : 0f;
            if (!(__instance is Player player) || hit == null) return;
            if (hit.m_hitType == HitData.HitType.Fall && Fists70ClientService.FallProtectionUntil >= Time.time) { hit.ApplyModifier(0f); return; }
            if (Fists70MaulService.IsActive(player) || Fists70ClientService.LocalSequenceActive(player))
            {
                hit.ApplyModifier(0.25f); hit.m_pushForce = 0f; hit.m_staggerMultiplier = 0f;
            }
        }
        private static void Postfix(Character __instance, HitData hit, float __state) => Fists70MaulService.ObserveActualDamage(__instance, hit, __state);
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
#if MASTERY_SPEAR35_EXPERIMENT
    [HarmonyPriority(Priority.High)]
#else
    [HarmonyPriority(Priority.First)]
#endif
    internal static class Fists70MaulInputPatch
    {
        private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
        {
            Player player = __instance as Player;
            if (!secondaryAttack || player == null || player != Player.m_localPlayer || !Fists70MaulService.IsFistWeapon(player) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Unarmed, 70)) return true;
            if (Fists70ClientService.LocalSequenceActive(player)) { __result = false; return false; }

            // Preserve the vanilla kick unless the crosshair is explicitly locked
            // onto a ready Maul target. Merely owning the perk must never consume M3.
            Character target = Fists70TargetLockService.Refresh(player);
            if (target == null) return true;
            __result = false;
            Fists70MaulService.RequestActivation(player, target);
            Fists70TargetLockService.Clear();
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Stagger))]
    internal static class Fists70StaggerImmunityPatch
    {
        private static bool Prefix(Character __instance) => !(__instance is Player player) || (!Fists70MaulService.IsActive(player) && !Fists70ClientService.LocalSequenceActive(player));
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddStaggerDamage))]
    internal static class Fists70StaggerDamageImmunityPatch
    {
        private static bool Prefix(Character __instance, ref bool __result)
        {
            if (__instance is Player player && (Fists70MaulService.IsActive(player) || Fists70ClientService.LocalSequenceActive(player))) { __result = false; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CanMove))]
    internal static class Fists70MovementLockPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (Fists70MaulService.IsActive(__instance) || Fists70ClientService.LocalSequenceActive(__instance)) __result = false;
        }
    }

    internal sealed class Fists70HudMarker : MonoBehaviour
    {
        private TextMeshProUGUI Label;
        private Image Reserved;
        private RectTransform ReservedRect;

        internal void Refresh(Character target, EnemyHud.HudData hud, Fists70ClientTargetState state)
        {
            if (target == null || hud?.m_gui == null || state == null) return;
            if (Label == null && hud.m_name != null)
            {
                Label = UnityEngine.Object.Instantiate(hud.m_name, hud.m_gui.transform);
                Label.name = "ValheimMastery_Fists70Gauge"; Label.raycastTarget = false; Label.enableWordWrapping = false;
                Label.fontSize = Mathf.Max(7f, hud.m_name.fontSize * 0.42f); Label.alignment = TextAlignmentOptions.Center;
                Label.outlineColor = new Color32(18, 8, 25, 255); Label.outlineWidth = 0.18f;
                RectTransform rect = Label.rectTransform; rect.anchorMin = new Vector2(0.5f, 0.5f); rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = hud.m_name.rectTransform.anchoredPosition + new Vector2(0f, 21f);
                rect.sizeDelta = new Vector2(155f, 15f);
            }
            float cooldown = Mathf.Max(0f, state.CooldownUntil - Time.time);
            int filled = Mathf.Clamp(Mathf.RoundToInt(state.Gauge * 6f), 0, 6);
            string bar = new string('◆', filled) + new string('◇', 6 - filled);
            if (Label != null)
            {
                string cd = cooldown > 0f ? "  <color=#BEB8AE>CD " + Mathf.FloorToInt(cooldown / 60f) + ":" + Mathf.FloorToInt(cooldown % 60f).ToString("00") + "</color>" : "";
                Label.text = state.Gauge >= 0.999f ? "<color=#FFB14A>MAUL READY</color>" + cd : "<color=#D79CFF>MAUL  " + bar + "  " + Mathf.RoundToInt(state.Gauge * 100f) + "%</color>" + cd;
                Label.gameObject.SetActive(true);
            }
            RefreshReserved(target, hud, state, cooldown);
        }

        internal void Hide()
        {
            if (Label != null) Label.gameObject.SetActive(false);
            if (Reserved != null) Reserved.gameObject.SetActive(false);
        }

        private void RefreshReserved(Character target, EnemyHud.HudData hud, Fists70ClientTargetState state, float cooldown)
        {
            bool show = state.TargetClass == CreatureClass.Boss && ((state.Gauge >= 0.999f && cooldown <= 0f) || state.ReservedRemaining > 0f);
            if (!show) { if (Reserved != null) Reserved.gameObject.SetActive(false); return; }
            RectTransform full = hud.m_healthFast?.m_bar?.parent as RectTransform;
            if (full == null) return;
            if (Reserved == null)
            {
                GameObject go = new GameObject("ValheimMastery_Fists70Reserved", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(full, false); Reserved = go.GetComponent<Image>(); ReservedRect = go.GetComponent<RectTransform>();
                Reserved.color = new Color(0.95f, 0.22f, 0.08f, 0.78f); Reserved.raycastTarget = false;
                MasteryHudArtwork.Fill(Reserved);
            }
            float max = Mathf.Max(1f, state.TargetMaxHealth);
            float hp = Mathf.Clamp01(target.GetHealth() / max);
            float reserved = state.ReservedRemaining > 0f ? state.ReservedRemaining / max : 0.12f;
            ReservedRect.anchorMin = new Vector2(Mathf.Clamp01(hp - reserved), 0f); ReservedRect.anchorMax = new Vector2(hp, 1f);
            ReservedRect.offsetMin = Vector2.zero; ReservedRect.offsetMax = Vector2.zero;
            Reserved.gameObject.SetActive(true); Reserved.transform.SetAsLastSibling();
        }
    }

    [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
    [HarmonyPriority(Priority.Last)]
    internal static class Fists70HudPatch
    {
        private static void Postfix(EnemyHud __instance)
        {
            if (__instance?.m_huds == null) return;
            Player local = Player.m_localPlayer;
            bool showMaul = local != null && PerkRuntimeService.HasPerk(local, Skills.SkillType.Unarmed, 70) && Fists70MaulService.IsFistWeapon(local);
            foreach (KeyValuePair<Character, EnemyHud.HudData> pair in __instance.m_huds)
            {
                if (!Fists70ClientService.TryGet(pair.Key, out Fists70ClientTargetState state) || pair.Value?.m_gui == null) continue;
                Fists70HudMarker marker = pair.Value.m_gui.GetComponent<Fists70HudMarker>();
                if (!showMaul) { if (marker != null) marker.Hide(); continue; }
                if (marker == null) marker = pair.Value.m_gui.AddComponent<Fists70HudMarker>();
                marker.Refresh(pair.Key, pair.Value, state);
            }
        }
    }

    internal sealed class Fists70DebugOverlay : MonoBehaviour
    {
        private TMP_Text Text;
        private void LateUpdate()
        {
            if (!MasteryPlugin.Settings.UIDebugLogging.Value || Player.m_localPlayer == null || !PerkRuntimeService.HasPerk(Player.m_localPlayer, Skills.SkillType.Unarmed, 70)) { Hide(); return; }
            Character target = Fists70ClientService.FindTarget(Player.m_localPlayer, 30f);
            if (target == null || !Fists70ClientService.TryGet(target, out Fists70ClientTargetState state)) { Hide(); return; }
            EnsureText(); if (Text == null) return;
            int required = state.TargetClass == CreatureClass.Boss ? 20 : 10;
            Text.text = "FISTS70\nTargetClass: " + state.TargetClass + "\nGauge: " + state.Gauge.ToString("P0") +
                "\nRequiredHits: " + required + "\nHitsAccumulated: " + state.Hits + "\nCooldown: " + Mathf.Max(0f, state.CooldownUntil - Time.time).ToString("0.0") +
                "\nMaulState: " + state.Phase + "\nAnchorValid: " + (target != null && !target.IsDead()) +
                "\nTargetHP: " + target.GetHealth().ToString("0") + "/" + state.TargetMaxHealth.ToString("0") + "\nReservedDamage: " + state.ReservedRemaining.ToString("0.0");
            Text.gameObject.SetActive(true);
        }
        private void EnsureText()
        {
            if (Text != null || Hud.instance?.m_healthText == null) return;
            Text = UnityEngine.Object.Instantiate(Hud.instance.m_healthText, Hud.instance.transform);
            Text.name = "ValheimMastery_Fists70Debug"; Text.raycastTarget = false; Text.alignment = TextAlignmentOptions.TopLeft;
            Text.fontSize = 13f; Text.color = Color.white;
            RectTransform rect = Text.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-18f, -180f); rect.sizeDelta = new Vector2(280f, 220f);
        }
        private void Hide() { if (Text != null) Text.gameObject.SetActive(false); }
        private void OnDestroy() { if (Text != null) Destroy(Text.gameObject); }
    }
}
