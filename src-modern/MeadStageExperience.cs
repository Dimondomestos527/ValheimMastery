using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MeadStageExperience
    {

        private static readonly int Serial = "valheim_mastery.mead_xp.serial".GetStableHashCode();
        private static readonly int Eligible = "valheim_mastery.mead_xp.eligible".GetStableHashCode();
        private static readonly int Content = "valheim_mastery.mead_xp.content".GetStableHashCode();
        private static readonly int Tier = "valheim_mastery.mead_xp.tier".GetStableHashCode();
        private static readonly int LoadPlayer = "valheim_mastery.mead_xp.load_player_v2".GetStableHashCode();
        private static readonly int PendingSerial = "valheim_mastery.mead_xp.tap_serial".GetStableHashCode();
        private static readonly int PendingContent = "valheim_mastery.mead_xp.tap_content".GetStableHashCode();
        private static readonly int PendingPeer = "valheim_mastery.mead_xp.tap_peer".GetStableHashCode();
        private static readonly int PendingPlayer = "valheim_mastery.mead_xp.tap_player".GetStableHashCode();
        private static readonly int PendingTier = "valheim_mastery.mead_xp.tap_tier".GetStableHashCode();
        private static readonly int Completed = "valheim_mastery.mead_xp.completed_serial".GetStableHashCode();

        // P2 supplies an authenticated persistent actor around its ONE native
        // accepted call. Product producer remains separate from this XP actor.
        private sealed class AcceptedActor { internal Fermenter Station; internal string Key; internal long Serial, Player; internal int Stage; }
        [ThreadStatic] private static AcceptedActor ActiveActor;
        // Retained P2 commits have pre-debit admission and authenticated context.
        // A native RPC arrives after vanilla's client debit, so its XP observer
        // must not add a new rejection of that already-paid native operation.
        internal static bool RequiresLoadAdmission => ActiveActor != null;
        private sealed class ActorScope : IDisposable
        { internal AcceptedActor Previous; private bool Disposed; public void Dispose() { if (!Disposed) { ActiveActor = Previous; Disposed = true; } } }
        internal static IDisposable BeginAcceptedActor(Fermenter f, string station, long serial, int stage, long player)
        {
            if (!Owner(f) || player == 0 || serial <= 0 || (stage != 2 && stage != 3) ||
                !MeadXpTransport.TryGetStationKey(f, out string actual) || actual != station)
                throw new ArgumentException("Invalid authenticated mead event context.");
            var scope = new ActorScope { Previous = ActiveActor };
            ActiveActor = new AcceptedActor { Station = f, Key = station, Serial = serial, Stage = stage, Player = player }; return scope;
        }
        private static long Actor(Fermenter f, long sender, long serial, int stage)
        {
            AcceptedActor actor = ActiveActor;
            if (actor != null) return actor.Station == f && actor.Serial == serial && actor.Stage == stage &&
                MeadXpTransport.TryGetStationKey(f, out string station) && station == actor.Key ? actor.Player : 0;
            return ProcessingStationProgressionService.ResolveSender(sender);
        }
        internal static long BatchSerial(Fermenter f) => Owner(f) ? f.m_nview.GetZDO().GetLong(Serial, 0) : 0;
        internal static long NextLoadSerial(Fermenter f)
        { long serial = BatchSerial(f); return Owner(f) && serial >= 0 && serial < long.MaxValue ? serial + 1 : 0; }
        internal static bool HasPendingOutput(Fermenter f) => Owner(f) &&
            f.m_nview.GetZDO().GetLong(PendingSerial, 0) > f.m_nview.GetZDO().GetLong(Completed, 0);
        internal static string EventIdentity(Fermenter f, long serial, int stage) => serial > 0 && (stage == 2 || stage == 3) &&
            MeadXpTransport.TryGetStationKey(f, out string station) ? station + ".batch." + serial.ToString(CultureInfo.InvariantCulture) + ".stage." + stage : null;
        // Recovery entry points require P2's durable original accepted receipt,
        // never a fresh client assertion or an attempt to replay the native RPC.
        internal static bool ConfirmAcceptedInput(Fermenter f, string identity, long serial, int input, long player)
        {
            if (!Owner(f) || !MasteryPlugin.Settings.Enabled.Value || player == 0 || input == 0 || f.GetContent() != input ||
                identity == null || identity != EventIdentity(f, serial, 2) || !CookingRecipeProgression.IsMeadBase(BaseKey(f, input))) return false;
            ZDO zdo = f.m_nview.GetZDO(); long current = zdo.GetLong(Serial, 0);
            if (current == serial - 1)
            {
                if (HasPendingOutput(f)) return false;
                zdo.Set(Content, input); zdo.Set(Eligible, true); zdo.Set(Tier, CookingRecipeProgression.MeadCoefficient(BaseKey(f, input)));
                zdo.Set(LoadPlayer, player); zdo.Set(Serial, serial);
            }
            else if (current != serial || zdo.GetInt(Content, 0) != input || !zdo.GetBool(Eligible, false) ||
                (zdo.GetLong(LoadPlayer, 0) != 0 && zdo.GetLong(LoadPlayer, 0) != player)) return false;
            zdo.Set(LoadPlayer, player);
            if (!Send(f, 0, player, serial, 2, BaseKey(f, input), zdo.GetFloat(Tier, 1f))) return false;
            MeadXpTransport.Drain(f, true); return true;
        }
        internal static bool ConfirmAcceptedTap(Fermenter f, string identity, long serial, int input, long player, long sender = 0)
        {
            if (!Owner(f) || !MasteryPlugin.Settings.Enabled.Value || player == 0 || input == 0 || f.GetContent() != 0 ||
                identity == null || identity != EventIdentity(f, serial, 3)) return false;
            ZDO zdo = f.m_nview.GetZDO();
            if (zdo.GetLong(Serial, 0) != serial || zdo.GetInt(Content, 0) != input || !zdo.GetBool(Eligible, false)) return false;
            if (zdo.GetLong(Completed, 0) >= serial) return true;
            long pending = zdo.GetLong(PendingSerial, 0);
            if (pending > zdo.GetLong(Completed, 0)) return pending == serial && zdo.GetInt(PendingContent, 0) == input &&
                zdo.GetLong(PendingPlayer, 0) == player && zdo.GetFloat(PendingTier, 1f) == zdo.GetFloat(Tier, 1f);
            zdo.Set(PendingContent, input); zdo.Set(PendingPeer, sender); zdo.Set(PendingPlayer, player);
            zdo.Set(PendingTier, zdo.GetFloat(Tier, 1f)); zdo.Set(PendingSerial, serial); return true;
        }
        // Only the P2 durable output receipt may call this recovery entry point,
        // AFTER proving the original native output exists. Never respawn/reroll.
        internal static bool ConfirmAcceptedOutput(Fermenter f, string identity)
        {
            if (!Owner(f) || !MasteryPlugin.Settings.Enabled.Value) return false;
            ZDO zdo = f.m_nview.GetZDO(); long serial = zdo.GetLong(PendingSerial, 0);
            if (identity == null || identity != EventIdentity(f, serial, 3)) return false;
            if (zdo.GetLong(Completed, 0) >= serial) return true;
            int input = zdo.GetInt(PendingContent, 0);
            if (input == 0 || !CookingRecipeProgression.IsMeadBase(BaseKey(f, input))) return false;
            if (!Send(f, zdo.GetLong(PendingPeer, 0), zdo.GetLong(PendingPlayer, 0), serial, 3,
                BaseKey(f, input), zdo.GetFloat(PendingTier, 1f))) return false;
            CommitQueuedCompletion(f, serial); MeadXpTransport.Drain(f, true); return true;
        }

        internal sealed class LoadState { internal int Before, Input; internal bool Processed, Blocked; }
        internal sealed class TapState { internal int Input; internal long Serial; internal float Coefficient; internal bool Eligible, Processed, Blocked; }
        internal sealed class OutputState
        { internal long Serial, Peer, Player; internal float Coefficient; internal int Before; internal string Base, Output; }

        private static bool Owner(Fermenter f) => f?.m_nview?.IsValid() == true && f.m_nview.IsOwner();
        private static string BaseKey(Fermenter f, int input) =>
            GatheringProgressionService.Normalize(f?.GetItemConversion(input)?.m_from?.gameObject.name);

        internal static LoadState BeforeLoad(Fermenter f, int input)
        {
            if (!Owner(f)) return null;
            var state = new LoadState { Before = f.GetContent(), Input = input };
            state.Blocked = MeadXpTransport.HasDeferred(f.m_nview.GetZDO()) ||
                (MasteryPlugin.Settings.Enabled.Value && state.Before == 0 && CookingRecipeProgression.IsMeadBase(BaseKey(f, input)) &&
                    !MeadXpTransport.CanAcceptLoad(f));
            return state;
        }

        internal static void AfterLoad(Fermenter f, long sender, LoadState state)
        {
            if (state == null || state.Blocked || state.Processed || !Owner(f) || state.Before != 0 || f.GetContent() != state.Input || state.Input == 0) return;
            state.Processed = true;
            ZDO zdo = f.m_nview.GetZDO();
            long last = zdo.GetLong(Serial, 0);
            if (last == long.MaxValue) return; // Never wrap a receipt identity.
            long serial = last + 1;
            string input = BaseKey(f, state.Input);
            bool eligible = MasteryPlugin.Settings.Enabled.Value && CookingRecipeProgression.IsMeadBase(input);
            float coefficient = CookingRecipeProgression.MeadCoefficient(input);
            long player = Actor(f, sender, serial, 2);
            zdo.Set(Content, state.Input); zdo.Set(Eligible, eligible); zdo.Set(Tier, coefficient); zdo.Set(LoadPlayer, player); zdo.Set(Serial, serial);
            if (eligible) ConfirmAcceptedInput(f, EventIdentity(f, serial, 2), serial, state.Input, player);
        }

        internal static TapState BeforeTap(Fermenter f)
        {
            if (!Owner(f) || (int)f.GetStatus() != 3) return null;
            if (MeadXpTransport.HasDeferred(f.m_nview.GetZDO())) return new TapState { Blocked = true };
            if (!MasteryPlugin.Settings.Enabled.Value) return null;
            int input = f.GetContent(); string key = BaseKey(f, input);
            if (input == 0 || !CookingRecipeProgression.IsMeadBase(key)) return null;
            if (!MeadXpTransport.CanAcceptTap(f)) return new TapState { Blocked = true };
            ZDO zdo = f.m_nview.GetZDO(); long serial = zdo.GetLong(Serial, 0);
            bool tracked = serial > 0 && zdo.GetInt(Content, 0) == input;
            // A ready batch created before this patch can earn completion, never a
            // retroactive load reward. Native Ready/content are the evidence.
            if (!tracked)
            {
                if (serial == long.MaxValue) return null;
                serial++;
                zdo.Set(Serial, serial); zdo.Set(Content, input); zdo.Set(Eligible, true);
                zdo.Set(Tier, CookingRecipeProgression.MeadCoefficient(key));
            }
            return new TapState { Input = input, Serial = serial,
                Eligible = zdo.GetBool(Eligible, false), Coefficient = zdo.GetFloat(Tier, 1f) };
        }

        internal static void AfterTap(Fermenter f, long sender, TapState state)
        {
            if (state == null || state.Blocked || state.Processed || !state.Eligible || !Owner(f) || f.GetContent() != 0 || f.m_delayedTapItem != state.Input) return;
            long recipient = Actor(f, sender, state.Serial, 3);
            if (recipient == 0) return;
            state.Processed = ConfirmAcceptedTap(f, EventIdentity(f, state.Serial, 3), state.Serial, state.Input, recipient, sender);
        }

        internal static OutputState BeforeOutput(Fermenter f)
        {
            if (!Owner(f) || !MasteryPlugin.Settings.Enabled.Value || f.m_outputPoint == null) return null;
            ZDO zdo = f.m_nview.GetZDO(); long serial = zdo.GetLong(PendingSerial, 0);
            if (serial <= 0 || serial <= zdo.GetLong(Completed, 0) ||
                zdo.GetInt(PendingContent, 0) != f.m_delayedTapItem) return null;
            Fermenter.ItemConversion conversion = f.GetItemConversion(f.m_delayedTapItem);
            if (conversion?.m_to?.m_itemData == null || conversion.m_producedItems <= 0) return null;
            return new OutputState { Serial = serial, Peer = zdo.GetLong(PendingPeer, 0),
                Player = zdo.GetLong(PendingPlayer, 0), Coefficient = zdo.GetFloat(PendingTier, 1f),
                Before = ItemDrop.s_instances.Count, Base = BaseKey(f, f.m_delayedTapItem), Output = GatheringProgressionService.Normalize(conversion.m_to.gameObject.name) };
        }

        internal static void AfterOutput(Fermenter f, OutputState state, bool completedNormally)
        {
            if (state == null || !completedNormally || !Owner(f)) return;
            ZDO zdo = f.m_nview.GetZDO();
            if (zdo.GetLong(PendingSerial, 0) != state.Serial || zdo.GetLong(Completed, 0) >= state.Serial) return;
            bool output = false;
            for (int i = state.Before; i < ItemDrop.s_instances.Count; i++)
            {
                ItemDrop drop = ItemDrop.s_instances[i];
                if (drop?.m_itemData != null && drop.m_itemData.m_stack > 0 &&
                    string.Equals(GatheringProgressionService.Normalize(drop.gameObject.name), state.Output, StringComparison.OrdinalIgnoreCase) &&
                    (drop.transform.position - f.m_outputPoint.position).sqrMagnitude < 4f) { output = true; break; }
            }
            if (!output) return;
            // One completed production batch, independent of six/three bottles or
            // an extra bonus bottle. Repeated DelayedTap cannot earn XP again.
            // Do not consume a terminal marker until the earned grant is durable.
            // Failed append preserves one immutable deferred completion for retry.
            ConfirmAcceptedOutput(f, EventIdentity(f, state.Serial, 3));
        }

        internal static void CommitQueuedCompletion(Fermenter f, long serial)
        {
            if (!Owner(f) || serial <= 0) return;
            ZDO zdo = f.m_nview.GetZDO(); zdo.Set(Completed, Math.Max(serial, zdo.GetLong(Completed, 0)));
        }
        private static bool Send(Fermenter f, long peer, long player, long serial, int stage, string input, float coefficient)
        {
            if (player == 0 || !Owner(f) || !MeadXpReceiptPolicy.ValidCoefficient(coefficient) || ZNet.instance == null) return false;
            var grant = new MeadXpGrant { Player = player, Serial = serial, Stage = stage, Input = input, Coefficient = coefficient };
            if (MeadXpTransport.TryQueue(f, grant)) return true;
            MeadXpTransport.Defer(f, grant); return false;
        }

        internal static bool Apply(Player player, string station, long serial, int stage, string input, float coefficient)
        {
            if (player == null || player != Player.m_localPlayer || !MasteryPlugin.Settings.Enabled.Value ||
                !CookingRecipeProgression.IsMeadBase(input) || !MeadXpReceiptPolicy.ValidCoefficient(coefficient)) return false;
            if (MeadXpReceiptPolicy.Seen(player.m_customData, station, serial, stage)) return true;
            if (!MeadXpReceiptPolicy.TryRecord(player.m_customData, station, serial, stage, out string previous)) return false;
            Skills skills = player.GetSkills(); skills.m_skillData.TryGetValue(Skills.SkillType.Cooking, out Skills.Skill before);
            float oldLevel = before?.m_level ?? 0f, oldAccumulator = before?.m_accumulator ?? 0f;
            try
            {
                using (ExperienceContext.BeginCookingRecipe(player, coefficient, true, "mead.stage" + stage + "." + input))
                    player.RaiseSkill(Skills.SkillType.Cooking, MeadXpReceiptPolicy.BaseXp(stage));
                skills.m_skillData.TryGetValue(Skills.SkillType.Cooking, out Skills.Skill after);
                MasteryPlugin.Log.LogInfo("[MeadXP] stage=" + stage + " base=" + input + " raw=" + MeadXpReceiptPolicy.BaseXp(stage) +
                    " recipeCoef=" + coefficient + " level=" + oldLevel + "->" + (after?.m_level ?? 0f) +
                    " accumulator=" + oldAccumulator + "->" + (after?.m_accumulator ?? 0f));
                return true;
            }
            catch
            {
                // Retry is safe only if native skill state did not change. Preserve
                // the receipt after a partial grant to avoid awarding the delta twice.
                skills.m_skillData.TryGetValue(Skills.SkillType.Cooking, out Skills.Skill after);
                if ((after?.m_level ?? 0f) == oldLevel && (after?.m_accumulator ?? 0f) == oldAccumulator)
                    MeadXpReceiptPolicy.Rollback(player.m_customData, station, serial, stage, previous);
                throw;
            }
        }
    }

    [HarmonyPatch(typeof(Fermenter), "SlowUpdate")]
    internal static class MeadXpRetryPatch
    { private static void Postfix(Fermenter __instance) => MeadXpTransport.Drain(__instance); }

    [HarmonyPatch(typeof(Fermenter), "RPC_AddItem")]
    internal static class MeadXpLoadPatch
    {
        private static bool Prefix(Fermenter __instance, int __1, out MeadStageExperience.LoadState __state)
        {
            bool reserved = MeadStageExperience.RequiresLoadAdmission;
            __state = null;
            try
            {
                __state = MeadStageExperience.BeforeLoad(__instance, __1);
                return !reserved || __state?.Blocked != true;
            }
            catch (Exception error)
            {
                Fault(error);
                return !reserved;
            }
        }
        private static void Postfix(Fermenter __instance, long __0, MeadStageExperience.LoadState __state)
        {
            try
            {
                MeadStageExperience.AfterLoad(__instance, __0, __state);
                if (__state?.Blocked == true && !__state.Processed && !MeadStageExperience.RequiresLoadAdmission &&
                    __state.Before == 0 && __state.Input != 0 && __instance.m_nview?.IsOwner() == true && __instance.GetContent() == __state.Input)
                {
                    __state.Processed = true;
                    MasteryPlugin.Log?.LogWarning("[MeadXP] native input accepted without a new XP receipt; existing XP history retained.");
                }
            }
            catch (Exception error) { Fault(error); }
        }
        private static void Fault(Exception error)
        {
            try { MasteryPlugin.Log?.LogWarning("[MeadXP] load observer: " + error.Message); }
            catch { /* XP logging must not add a native input failure. */ }
        }
    }

    [HarmonyPatch(typeof(Fermenter), "RPC_Tap")]
    internal static class MeadXpTapPatch
    {
        private static bool Prefix(Fermenter __instance, out MeadStageExperience.TapState __state)
        { __state = MeadStageExperience.BeforeTap(__instance); return __state?.Blocked != true; }
        private static void Postfix(Fermenter __instance, long __0, MeadStageExperience.TapState __state) => MeadStageExperience.AfterTap(__instance, __0, __state);
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.DelayedTap))]
    internal static class MeadXpOutputPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Fermenter __instance, out MeadStageExperience.OutputState __state) => __state = MeadStageExperience.BeforeOutput(__instance);
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Fermenter __instance, MeadStageExperience.OutputState __state, Exception __exception)
        { MeadStageExperience.AfterOutput(__instance, __state, __exception == null); return __exception; }
    }
}
