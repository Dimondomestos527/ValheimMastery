using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class AssassinBlinkState
    {
        internal float BlinkAttackUntil;
        internal float ServerBlinkUntil;
    }

    internal static class AssassinBlink70Service
    {
        private const float Range = 20f;
        private const float MinimumBehindDistance = 0.65f;
        private const float Cooldown = 12f;
        [System.ThreadStatic] private static bool RecursionGuard;
        internal static bool IsStartingBlinkAttack => RecursionGuard;

        internal static bool TryActivateFromStart(Player player, Character requestedTarget, bool secondaryAttack, out bool attackStarted)
        {
            attackStarted = false;
            if (RecursionGuard || player == null || player != Player.m_localPlayer || !secondaryAttack || !player.IsCrouching() || player.InAttack() ||
                player.GetCurrentWeapon()?.m_shared?.m_skillType != Skills.SkillType.Knives ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Knives, 35) ||
                PerkCooldownStateService.GetRemainingSeconds(player, "knives_35") > 0d) return false;

            ItemDrop.ItemData blinkWeapon = player.GetCurrentWeapon();
            Character target = IsValidTarget(player, requestedTarget) ? requestedTarget : FindCrosshairEnemy(player);
            if (target == null)
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogInfo("[Knife35Blink] rejected: no valid crosshair target");
                return false;
            }
            if (!TryFindDestination(player, target, out Vector3 destination))
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogInfo("[Knife35Blink] rejected: no safe destination behind " + target.gameObject.name);
                return false;
            }

            Vector3 origin = player.transform.position;
            Quaternion oldRotation = player.transform.rotation;
            AssassinBlinkVisualService.PlayDeparture(player, origin);
            MovePlayer(player, destination, target);
            Character impactTarget = FindNearestEnemy(player, Mathf.Max(4f, target.GetRadius() + 2f)) ?? target;
            FaceTarget(player, impactTarget);
            RecursionGuard = true;
            try { attackStarted = player.StartAttack(impactTarget, true); }
            finally { RecursionGuard = false; }
            if (attackStarted && player.m_currentAttack != null)
            {
                player.m_currentAttack.m_attackRange = Mathf.Max(player.m_currentAttack.m_attackRange, impactTarget.GetRadius() + 1.8f);
                player.m_currentAttack.m_attackRayWidth = Mathf.Max(player.m_currentAttack.m_attackRayWidth, impactTarget.GetRadius() + 0.75f);
                player.m_currentAttack.m_attackRayWidthCharExtra = Mathf.Max(player.m_currentAttack.m_attackRayWidthCharExtra, 0.65f);
            }
            target = impactTarget;
            if (!attackStarted)
            {
                MovePlayerRaw(player, origin, oldRotation);
                return false;
            }
            if (!PerkCooldownStateService.TryConsume(player, "knives_35", Cooldown)) return true;
            AssassinBlinkState state = MasteryStateStore.GetPlayerState<AssassinBlinkState>(player);
            state.BlinkAttackUntil = Time.time + 1.50f;
            NetworkSync.SendClientAbility("knife35_blink");
            ShadowStrikeGuarantee.Schedule(player, target, blinkWeapon, player.m_currentAttack);
            AssassinBlinkVisualService.PlayArrival(player, target, origin, destination, Cooldown);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Knife35Blink] phase=ACTIVATED target=" + target.gameObject.name + " zdo=" + target.GetZDOID() + " destination=" + destination);
            return true;
        }

        internal static bool IsBlinkImpact(Player player)
        {
            return player != null && MasteryStateStore.TryGetPlayerState<AssassinBlinkState>(player, out AssassinBlinkState state) &&
                (state.BlinkAttackUntil >= Time.time || state.ServerBlinkUntil >= Time.time);
        }

        internal static bool SuppressPhysicalBlinkHit(Player player, HitData hit)
        {
            if (player == null || hit == null || !IsBlinkImpact(player) ||
                MasteryAttackTagService.Has(hit, MasteryAttackTag.ShadowStrike)) return false;
            hit.m_damage.Modify(0f);
            hit.m_staggerMultiplier = 0f;
            hit.m_pushForce = 0f;
            return true;
        }
        internal static void ArmServerBlink(Player player)
        {
            if (player == null) return;
            MasteryStateStore.GetPlayerState<AssassinBlinkState>(player).ServerBlinkUntil = Time.time + 1.75f;
        }

        internal static void OnServerKnifeKill(Player player)
        {
            if (player == null || !MasteryStateStore.TryGetPlayerState<AssassinBlinkState>(player, out AssassinBlinkState state) || state.ServerBlinkUntil < Time.time) return;
            state.ServerBlinkUntil = 0f;
            NetworkSync.SendProcFeedback(player, "knife35_blink_kill", player.GetCenterPoint());
        }

        internal static void OnConfirmedKillLocal(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            AssassinBlinkState state = MasteryStateStore.GetPlayerState<AssassinBlinkState>(player);
            if (state.BlinkAttackUntil < Time.time) return;
            state.BlinkAttackUntil = 0f;
            PerkCooldownStateService.ReduceRemaining(player, "knives_35", 0.30f);
            AssassinBlinkVisualService.RefreshCooldown(player);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Knife35Blink] phase=KILL_CONFIRMED cooldownRemaining=" + PerkCooldownStateService.GetRemainingSeconds(player, "knives_35").ToString("0.00"));
        }

        internal static Character FindCrosshairEnemy(Player player)
        {
            if (player == null) return null;
            Vector3 eye = player.GetEyePoint();
            Vector3 aim = player.GetAimDir(eye).normalized;
            RaycastHit[] hits = Physics.SphereCastAll(eye, 0.16f, aim, Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                Character candidate = hit.collider?.GetComponentInParent<Character>();
                if (IsValidTarget(player, candidate)) return candidate;
                if (candidate == null && hit.collider != null && !hit.collider.isTrigger) return null;
            }
            return null;
        }

        private static Character FindNearestEnemy(Player player, float range)
        {
            Character best = null; float bestDistance = range * range;
            foreach (Character candidate in Character.GetAllCharacters())
            {
                if (!IsValidTarget(player, candidate)) continue;
                float distance = (candidate.GetCenterPoint() - player.GetCenterPoint()).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            return best;
        }

        private static void FaceTarget(Player player, Character target)
        {
            Vector3 facing = target.GetCenterPoint() - player.transform.position; facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) return;
            player.SetLookDir(facing.normalized, 0f); player.FaceLookDirection();
        }
        private static bool IsValidTarget(Player player, Character target) => target != null && target != player && !target.IsDead() && !(target is Player) && BaseAI.IsEnemy(player, target);

        internal static string GetTargetDebug(Player player)
        {
            Character candidate = FindCrosshairEnemy(player);
            if (candidate == null) return "candidate=none reject=crosshair_or_los";
            bool destination = TryFindDestination(player, candidate, out Vector3 point);
            return "candidate=" + candidate.gameObject.name + " zdo=" + candidate.GetZDOID() + " distance=" +
                Vector3.Distance(player.GetEyePoint(), candidate.GetCenterPoint()).ToString("0.00") + " destination=" + destination +
                (destination ? " point=" + point : "");
        }

        private static bool TryFindDestination(Player player, Character target, out Vector3 destination)
        {
            destination = Vector3.zero;
            Vector3 backward = -target.transform.forward; backward.y = 0f;
            if (backward.sqrMagnitude < 0.01f) backward = (target.transform.position - player.transform.position).normalized;
            backward.Normalize();
            float baseDistance = Mathf.Clamp(target.GetRadius() + 0.55f, MinimumBehindDistance, 5.0f);
            float[] distanceFactors = { 0.92f, 1.00f, 1.10f, 1.22f, 1.35f };
            float[] angles = { 0f, -12f, 12f, -25f, 25f, -40f, 40f, -55f, 55f };
            foreach (float distanceFactor in distanceFactors)
            foreach (float angle in angles)
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * backward;
                Vector3 probe = target.transform.position + direction * baseDistance * distanceFactor;
                if (!Physics.Raycast(probe + Vector3.up * 8f, Vector3.down, out RaycastHit ground, 18f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                Vector3 candidate = ground.point + Vector3.up * 0.05f;
                Vector3 targetToCandidate = candidate - target.transform.position; targetToCandidate.y = 0f;
                if (targetToCandidate.sqrMagnitude < 0.05f || Vector3.Dot(targetToCandidate.normalized, backward) < 0.55f) continue;
                if (Mathf.Abs(candidate.y - target.transform.position.y) > 8f || !DestinationBodyClear(player, target, candidate)) continue;
                destination = candidate;
                return true;
            }
            return false;
        }

        private static bool DestinationBodyClear(Player player, Character target, Vector3 destination)
        {
            foreach (Collider blocker in Physics.OverlapCapsule(destination + Vector3.up * 0.42f, destination + Vector3.up * 1.55f, 0.24f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Character owner = blocker.GetComponentInParent<Character>();
                if (owner == target || owner == player) continue;
                Vector3 closest = blocker.ClosestPoint(destination + Vector3.up * 0.65f);
                if (closest.y <= destination.y + 0.42f) continue;
                return false;
            }
            return true;
        }
        private static void MovePlayer(Player player, Vector3 destination, Character target)
        {
            Vector3 facing = target.GetCenterPoint() - destination; facing.y = 0f;
            Quaternion rotation = facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : player.transform.rotation;
            MovePlayerRaw(player, destination, rotation);
            if (facing.sqrMagnitude > 0.01f)
            {
                player.SetLookDir(facing.normalized, 0f);
                player.FaceLookDirection();
            }
        }

        private static void MovePlayerRaw(Player player, Vector3 position, Quaternion rotation)
        {
            player.transform.SetPositionAndRotation(position, rotation);
            if (player.m_body != null) { player.m_body.position = position; player.m_body.rotation = rotation; player.m_body.linearVelocity = Vector3.zero; }
            if (player.m_nview?.GetZDO() != null && player.m_nview.IsOwner()) player.m_nview.GetZDO().SetPosition(position);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    [HarmonyPriority(Priority.First)]
    internal static class AssassinBlink70StartAttackPatch
    {
        private static bool Prefix(Humanoid __instance, Character target, bool secondaryAttack, ref bool __result)
        {
            if (__instance is Player player && AssassinBlink70Service.TryActivateFromStart(player, target, secondaryAttack, out bool started))
            {
                __result = started;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    [HarmonyPriority(Priority.Last)]
    internal static class AssassinBlink35AttackSpeedPatch
    {
        private static void Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon)
        {
            if (AssassinBlink70Service.IsStartingBlinkAttack && character is Player && weapon?.m_shared?.m_skillType == Skills.SkillType.Knives)
                __instance.m_speedFactor *= 1.75f;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class AssassinBlink70TargetIndicatorPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            bool eligible = __instance.IsCrouching() && __instance.GetCurrentWeapon()?.m_shared?.m_skillType == Skills.SkillType.Knives &&
                PerkRuntimeService.HasPerk(__instance, Skills.SkillType.Knives, 35) && PerkCooldownStateService.GetRemainingSeconds(__instance, "knives_35") <= 0d;
            AssassinBlinkVisualService.SetCandidate(eligible ? AssassinBlink70Service.FindCrosshairEnemy(__instance) : null);
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class AssassinBlink70KillPatch
    {
        private static void Prefix(Character __instance, HitData hit, out bool __state)
        {
            __state = __instance != null && !__instance.IsDead() && hit?.GetAttacker() is Player && hit.m_skill == Skills.SkillType.Knives;
            if (__state) AssassinBlink70Service.SuppressPhysicalBlinkHit(hit.GetAttacker() as Player, hit);
        }
        private static void Postfix(Character __instance, bool __state, HitData hit)
        {
            if (!__state || __instance == null || !__instance.IsDead()) return;
            Player attacker = hit.GetAttacker() as Player;
            if (ZNet.instance != null && ZNet.instance.IsServer()) AssassinBlink70Service.OnServerKnifeKill(attacker);
            else if (attacker == Player.m_localPlayer) AssassinBlink70Service.OnConfirmedKillLocal(attacker);
        }
    }
}
