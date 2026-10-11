using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class GrapplingTraversalXp
    {
        // Anti-jitter eligibility only: the XP amount does not depend on metres.
        internal const float MinimumPullMovement = .25f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        internal sealed class Episode
        {
            internal Player Player;
            internal ItemDrop.ItemData Weapon;
            internal ZNet Net;
            internal ZNetScene Scene;
            internal long World;
            internal Vector3 Origin, Anchor;
            internal bool Started, DrovePreviousUpdate, HadPreviousDrive, MovementReady, InUpdate, Issued, Canceled;
            internal int TraceId, TraceEvents;
            internal bool Done => Issued || Canceled;
            internal void Cancel() { Canceled = true; InUpdate = MovementReady = DrovePreviousUpdate = false; }
        }

        private static readonly ConditionalWeakTable<GrapplingPoint, Episode> Episodes = new ConditionalWeakTable<GrapplingPoint, Episode>();
        private static WeakReference Active;
        private sealed class TraceTicket { internal WeakReference<ZNet> Net; internal long World; internal int Id; }
        private static readonly ConditionalWeakTable<GrapplingPoint, TraceTicket> TracedPoints = new ConditionalWeakTable<GrapplingPoint, TraceTicket>();
        private static WeakReference<ZNet> TraceNet;
        private static long TraceWorld;
        private static int TraceAttempts;
        private static readonly int[] TraceLines = new int[9];
        private static bool TraceSession(WeakReference<ZNet> reference, ZNet session) => reference != null && reference.TryGetTarget(out var old) && ReferenceEquals(old, session);
        private static int ClaimTrace(GrapplingPoint point, Player player)
        {
            if (point == null || player != Player.m_localPlayer || !Crossbows35ReloadGuard.IsTraversalHook(player?.GetCurrentWeapon())) return 0;
            long world = ZNet.instance?.GetWorldUID() ?? 0;
            if (!TraceSession(TraceNet, ZNet.instance) || TraceWorld != world)
            { TraceNet = new WeakReference<ZNet>(ZNet.instance); TraceWorld = world; TraceAttempts = 0; Array.Clear(TraceLines, 0, TraceLines.Length); }
            if (TracedPoints.TryGetValue(point, out var old) && TraceSession(old.Net, ZNet.instance) && old.World == world) return old.Id;
            if (TraceAttempts >= 8) return 0;
            TracedPoints.Remove(point);
            var ticket = new TraceTicket { Net = TraceNet, World = world, Id = ++TraceAttempts };
            TracedPoints.Add(point, ticket); return ticket.Id;
        }
        private static void Trace(int id, string text)
        { if (id > 0 && TraceLines[id] < 4) { TraceLines[id]++; try { MasteryPlugin.Log.LogInfo("[HookXP] episode=" + id + " " + text); } catch { } } }
        private static string SkillSnapshot(Player player)
        {
            try
            {
                if (player?.GetSkills()?.m_skillData.TryGetValue(Skills.SkillType.Crossbows, out var skill) == true)
                    return skill.m_level.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "/" + skill.m_accumulator.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            return "unavailable";
        }
        private static void TraceEpisode(Episode episode, string text)
        { if (episode != null && episode.TraceEvents < 3) { episode.TraceEvents++; Trace(episode.TraceId, text); } }

        private static bool Actor(Player player) => MasteryPlugin.Settings?.Enabled.Value == true &&
            player != null && player == Player.m_localPlayer && player.m_nview?.IsOwner() == true && !player.IsDead();
        private static bool Primary(GrapplingPoint point, Player player)
        {
            if (!Actor(player) || point == null || point.m_character != player || point.m_secondary ||
                (int)point.Method != 1 || point.m_nview?.IsValid() != true || !point.m_nview.IsOwner() ||
                GrapplingPoint.m_localGrappler != point || point.m_equipCheck?.m_itemData?.m_shared == null) return false;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (!Crossbows35ReloadGuard.IsTraversalHook(weapon) || !player.IsItemTypeEquiped(point.m_equipCheck.m_itemData)) return false;
            Projectile donor = weapon.m_shared?.m_attack?.m_attackProjectile?.GetComponent<Projectile>();
            Projectile secondaryDonor = weapon.m_shared?.m_secondaryAttack?.m_attackProjectile?.GetComponent<Projectile>();
            bool donorMatches = donor?.m_spawnOnHit != null && string.Equals(point.gameObject.name, donor.m_spawnOnHit.name + "(Clone)", StringComparison.Ordinal);
            bool secondaryMatches = secondaryDonor?.m_spawnOnHit != null && string.Equals(point.gameObject.name, secondaryDonor.m_spawnOnHit.name + "(Clone)", StringComparison.Ordinal);
            if (!donorMatches && !secondaryMatches) return false;
            Rigidbody body = point.GetComponentInParent<Rigidbody>();
            return point.GetComponentInParent<Character>() == null && (body == null || body.isKinematic);
        }
        internal static void Activated(GrapplingPoint point, bool newSecondaryPull = false)
        {
            if (point == null || !(point.m_character is Player player)) return;
            if (Episodes.TryGetValue(point, out var old) && (!old.Done || !newSecondaryPull)) return;
            int trace = ClaimTrace(point, player);
            bool primary = Primary(point, player);
            if (trace > 0 && (TraceLines[trace] == 0 || (newSecondaryPull && primary && TraceLines[trace] < 2)))
                Trace(trace, "fixed admission=" + primary + " point=" + point.gameObject.name + " secondary=" + point.m_secondary + " crossbows level/acc=" + SkillSnapshot(player));
            if (!primary || ZNet.instance == null || ZNetScene.instance == null) return;
            CancelActive();
            Episodes.Remove(point);
            var episode = new Episode { Player = player, Weapon = player.GetCurrentWeapon(), Net = ZNet.instance,
                Scene = ZNetScene.instance, World = ZNet.instance.GetWorldUID(), TraceId = trace, TraceEvents = 1 };
            Episodes.Add(point, episode); Active = new WeakReference(point);
        }
        private static bool Context(GrapplingPoint point, Episode episode, bool issued = false) =>
            !episode.Canceled && (issued || !episode.Issued) && Actor(episode.Player) && point != null &&
            point.m_character == episode.Player && !point.m_secondary && point.m_nview?.IsValid() == true &&
            point.m_nview.IsOwner() && GrapplingPoint.m_localGrappler == point &&
            ReferenceEquals(episode.Weapon, episode.Player.GetCurrentWeapon()) &&
            ReferenceEquals(episode.Net, ZNet.instance) && ReferenceEquals(episode.Scene, ZNetScene.instance) &&
            episode.Net != null && episode.Scene != null && episode.World == episode.Net.GetWorldUID();

        internal static Episode BeginUpdate(GrapplingPoint point)
        {
            if (point == null || !Episodes.TryGetValue(point, out var episode) || episode.Done) return null;
            if (!Context(point, episode)) { episode.Cancel(); return null; }
            episode.InUpdate = true;
            episode.HadPreviousDrive = episode.DrovePreviousUpdate;
            episode.DrovePreviousUpdate = false;
            episode.MovementReady = false;
            Vector3 position = episode.Player.transform.position, anchor = point.transform.position;
            if (!Finite(position) || !Finite(anchor) || (episode.Started && (anchor - episode.Anchor).sqrMagnitude > .0025f))
            { episode.Cancel(); return null; }
            // A preceding exact native pull is required. A no-drive gap never
            // banks walking. Render/physics elapsed time does not veto real motion.
            if (episode.Started && episode.HadPreviousDrive)
                episode.MovementReady = Vector3.Distance(episode.Origin, episode.Anchor) - Vector3.Distance(position, anchor) > MinimumPullMovement;
            return episode;
        }
        internal static void Driven(GrapplingPoint point)
        { try { DrivenCore(point); } catch { Cancel(point); } }
        private static void DrivenCore(GrapplingPoint point)
        {
            if (point == null || !Episodes.TryGetValue(point, out var episode) || episode.Done || !episode.InUpdate) return;
            if (!Context(point, episode) || !Primary(point, episode.Player)) { episode.Cancel(); return; }
            Vector3 velocity = episode.Player.GetVelocity(), position = episode.Player.transform.position;
            if (!Finite(velocity) || velocity.sqrMagnitude <= 0f || !Finite(position)) { episode.Cancel(); return; }
            if (!episode.Started || !episode.HadPreviousDrive)
            { episode.Origin = position; episode.Anchor = point.transform.position; episode.Started = true; }
            episode.DrovePreviousUpdate = true;
            if (episode.TraceEvents == 1) TraceEpisode(episode, "native-drive fixed floor=" + MinimumPullMovement);
            TryAward(point, episode);
        }
        internal static void NearAnchor(GrapplingPoint point) => NativeCompletion(point);
        internal static void MarkManualRelease(GrapplingPoint point) => NativeCompletion(point);
        private static void NativeCompletion(GrapplingPoint point)
        {
            try
            {
                if (point != null && Episodes.TryGetValue(point, out var episode) && episode.InUpdate && !episode.Done)
                    TryAward(point, episode);
            }
            catch { Cancel(point); } // XP failure cannot turn native close/release into failure.
        }
        internal static bool TryNativeReference(out float baseXp, out int itemTier)
        {
            baseXp = 0f; itemTier = 1;
            GameObject prefab = ObjectDB.instance?.GetItemPrefab("CrossbowArbalest");
            var shared = prefab?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
            if (shared?.m_skillType != Skills.SkillType.Crossbows || shared.m_attack == null ||
                !Finite(shared.m_attack.m_raiseSkillAmount) || shared.m_attack.m_raiseSkillAmount <= 0f) return false;
            baseXp = shared.m_attack.m_raiseSkillAmount;
            // The serialized prefab ItemData need not have its nonserialized
            // m_dropPrefab initialized. Supply a temporary identity for canonical lookup.
            itemTier = TierDatabase.GetItemTier(new ItemDrop.ItemData { m_dropPrefab = prefab });
            return itemTier > 0;
        }
        private static void TryAward(GrapplingPoint point, Episode episode)
        {
            if (!episode.MovementReady || !Context(point, episode)) return;
            if (!TryNativeReference(out float baseXp, out int tier))
            { TraceEpisode(episode, "no-award native CrossbowArbalest reference unavailable"); return; }
            if (!Context(point, episode)) return;
            episode.Issued = true; // Consume before context/event/native re-entry, never retry after throw.
            string before = episode.TraceId > 0 ? SkillSnapshot(episode.Player) : null;
            using (ExperienceContext.BeginFixedHookTraversal(episode.Player, tier))
            {
                if (!Context(point, episode, true)) return;
                try { episode.Player.RaiseSkill(Skills.SkillType.Crossbows, baseXp); }
                finally { TraceEpisode(episode, "award fixed nativeBase=" + baseXp.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    " tier=" + tier + " crossbows level/acc before=" + before + " after=" + SkillSnapshot(episode.Player)); }
            }
        }
        internal static void EndUpdate(GrapplingPoint point, Episode episode, Exception error)
        {
            if (episode == null) return;
            episode.InUpdate = false;
            if (error != null || (!episode.Issued && !Context(point, episode))) episode.Cancel();
        }
        internal static void Cancel(GrapplingPoint point)
        { if (!ReferenceEquals(point, null) && Episodes.TryGetValue(point, out var episode)) episode.Cancel(); }
        internal static void CancelPlayer(Player player)
        {
            var point = Active?.Target as GrapplingPoint;
            if (!ReferenceEquals(point, null) && Episodes.TryGetValue(point, out var episode) && ReferenceEquals(episode.Player, player)) episode.Cancel();
        }
        internal static void CancelActive() { Cancel(Active?.Target as GrapplingPoint); Active = null; }

        // Exact existing native Update callsites: force, close completion, input release.
        internal static IEnumerable<CodeInstruction> Instrument(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            const BindingFlags callbacks = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo drive = typeof(GrapplingTraversalXp).GetMethod(nameof(Driven), callbacks);
            MethodInfo near = typeof(GrapplingTraversalXp).GetMethod(nameof(NearAnchor), callbacks);
            MethodInfo release = typeof(GrapplingTraversalXp).GetMethod(nameof(MarkManualRelease), callbacks);
            MethodInfo nativeBreak = typeof(GrapplingPoint).GetMethod(nameof(GrapplingPoint.Break),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
            int driveCount = 0, nearCount = 0, releaseCount = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                if (i >= 2 && instruction.operand is MethodInfo set && set.DeclaringType == typeof(Character) && set.Name == nameof(Character.SetVelocity) &&
                    code[i - 2].operand is FieldInfo force && force.DeclaringType == typeof(GrapplingPoint) && force.Name == nameof(GrapplingPoint.m_pullForce))
                {
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, drive); driveCount++; continue;
                }
                if (i >= 3 && instruction.operand is MethodInfo stop && stop.DeclaringType == typeof(GrapplingPoint) && stop.Name == nameof(GrapplingPoint.Deactivate) &&
                    code[i - 3].operand is FieldInfo distance && distance.DeclaringType == typeof(GrapplingPoint) && distance.Name == nameof(GrapplingPoint.m_closeBreakDist))
                {
                    var duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    duplicate.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return duplicate;
                    yield return new CodeInstruction(OpCodes.Call, near); nearCount++;
                }
                if (instruction.Calls(nativeBreak) && i >= 8 && code[i - 1].opcode == OpCodes.Ldc_I4_1 &&
                    code.Skip(Math.Max(0, i - 12)).Take(12).Any(candidate => candidate.operand is string text && text == "Block") &&
                    code.Skip(Math.Max(0, i - 12)).Take(12).Any(candidate => candidate.operand is string text && text == "JoyBlock"))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, release); releaseCount++;
                }
                yield return instruction;
            }
            if (driveCount != 1 || nearCount != 1 || releaseCount != 1)
                throw new InvalidOperationException("Unsupported native GrapplingPoint traversal sites: " + driveCount + "/" + nearCount + "/" + releaseCount);
        }
    }
    [HarmonyPatch(typeof(GrapplingPoint), nameof(GrapplingPoint.Activate))]
    internal static class GrapplingTraversalXpActivatePatch
    { private static void Postfix(GrapplingPoint __instance) => GrapplingTraversalXp.Activated(__instance); }
    [HarmonyPatch(typeof(GrapplingPoint), "Pull")]
    internal static class GrapplingTraversalXpPullPatch
    {
        private static void Prefix(GrapplingPoint __instance, out bool __state) => __state = __instance != null && __instance.m_secondary;
        private static void Postfix(GrapplingPoint __instance, bool __state)
        { if (__state) GrapplingTraversalXp.Activated(__instance, true); }
    }
    [HarmonyPatch(typeof(GrapplingPoint), "Update")]
    internal static class GrapplingTraversalXpUpdatePatch
    {
        private static void Prefix(GrapplingPoint __instance, out GrapplingTraversalXp.Episode __state) => __state = GrapplingTraversalXp.BeginUpdate(__instance);
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => GrapplingTraversalXp.Instrument(instructions);
        private static Exception Finalizer(GrapplingPoint __instance, GrapplingTraversalXp.Episode __state, Exception __exception)
        { try { GrapplingTraversalXp.EndUpdate(__instance, __state, __exception); } catch { __state?.Cancel(); } return __exception; }
    }
    [HarmonyPatch(typeof(GrapplingPoint), nameof(GrapplingPoint.Break))]
    internal static class GrapplingTraversalXpBreakPatch
    { private static void Prefix(GrapplingPoint __instance) => GrapplingTraversalXp.Cancel(__instance); }
    [HarmonyPatch(typeof(GrapplingPoint), nameof(GrapplingPoint.Deactivate))]
    internal static class GrapplingTraversalXpDeactivatePatch
    { private static void Postfix(GrapplingPoint __instance) => GrapplingTraversalXp.Cancel(__instance); }
    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo), new[] { typeof(Vector3), typeof(Quaternion), typeof(bool) })]
    internal static class GrapplingTraversalXpTeleportPatch
    { private static void Prefix(Player __instance) => GrapplingTraversalXp.CancelPlayer(__instance); }
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class GrapplingTraversalXpDeathPatch
    { private static void Prefix(Player __instance) => GrapplingTraversalXp.CancelPlayer(__instance); }
    [HarmonyPatch(typeof(Player), "OnDestroy")]
    internal static class GrapplingTraversalXpPlayerCleanupPatch
    { private static void Prefix(Player __instance) => GrapplingTraversalXp.CancelPlayer(__instance); }
    [HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
    internal static class GrapplingTraversalXpSceneCleanupPatch
    { private static void Prefix() => GrapplingTraversalXp.CancelActive(); }
    [HarmonyPatch(typeof(ZNet), "Shutdown", new[] { typeof(bool) })]
    internal static class GrapplingTraversalXpSessionCleanupPatch
    { private static void Prefix() => GrapplingTraversalXp.CancelActive(); }
    [HarmonyPatch(typeof(MasteryPlugin), "OnDestroy")]
    internal static class GrapplingTraversalXpPluginCleanupPatch
    { private static void Prefix() => GrapplingTraversalXp.CancelActive(); }
}
