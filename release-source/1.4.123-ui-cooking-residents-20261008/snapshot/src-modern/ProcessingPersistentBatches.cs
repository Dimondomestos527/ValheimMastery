using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class ProcessingPersistentBatches
    {
        internal const string OutputKey = "vm.processing.output.v2";
        internal static string InputKey(int index) => "vm.processing.input.v2." + index;
        private sealed class StationState
        {
            internal ProcessingBatchReceipt Consumed;
            internal readonly Dictionary<long, float> Levels = new Dictionary<long, float>();
        }
        private static readonly ConditionalWeakTable<Smelter, StationState> States = new ConditionalWeakTable<Smelter, StationState>();
        internal static void Claim(Smelter station, long sender, float level)
        {
            if (station?.m_nview?.IsOwner() != true || !OwnerSkillAuthority.Valid(level)) return;
            long id = ProcessingStationProgressionService.ResolveSender(sender);
            if (id != 0) States.GetOrCreateValue(station).Levels[id] = level;
        }
        internal static void Record(Smelter station, long author, int index)
        {
            var player = author != 0 ? Player.GetPlayer(author) : null;
            float level = 0;
            if (player != null && player == Player.m_localPlayer) level = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Crafting);
            else if (!States.GetOrCreateValue(station).Levels.TryGetValue(author, out level) && player != null)
                level = OwnerSkillAuthority.Level(player, Skills.SkillType.Crafting);
            if (!MasteryPlugin.Settings.Enabled.Value || !OwnerSkillAuthority.Valid(level)) level = 0;
            var receipt = new ProcessingBatchReceipt { Author = author, Level = level, Bonus = author != 0 && PerkRuntimeService.RollChance(level * .005f) };
            station.m_nview.GetZDO().Set(InputKey(index), receipt.Encode());
        }
        internal static void Advance(Smelter station)
        {
            if (station?.m_nview?.IsOwner() != true || station.GetQueueSize() <= 0) return;
            var zdo = station.m_nview.GetZDO(); int count = station.GetQueueSize();
            States.GetOrCreateValue(station).Consumed = ProcessingBatchReceipt.Decode(zdo.GetString(InputKey(0), ""));
            for (int i = 0; i < count - 1; i++) zdo.Set(InputKey(i), zdo.GetString(InputKey(i + 1), ""));
            zdo.Set(InputKey(count - 1), "");
        }
        internal static void Complete(Smelter station, string ore)
        {
            if (station?.m_nview?.IsOwner() != true) return;
            var state = States.GetOrCreateValue(station); var receipt = state.Consumed ?? new ProcessingBatchReceipt(); state.Consumed = null;
            var zdo = station.m_nview.GetZDO(); int previous = station.m_spawnStack ? zdo.GetInt(ZDOVars.s_spawnAmount, 0) : 0;
            // Vanilla flushes the old conversion when its resource changes. Do it
            // before attaching the new conversion's receipt, not afterwards.
            if (station.m_spawnStack && previous > 0 && zdo.GetString(ZDOVars.s_spawnOre, "") != ore)
            { station.SpawnProcessed(); previous = 0; }
            var ledger = ProcessingBatchLedger.Read(zdo.GetString(OutputKey, ""), previous);
            ledger.Items.Add(receipt); zdo.Set(OutputKey, ledger.Encode());
        }
        internal static void Fx(Smelter station, long sender, int amount)
        {
            if (station?.m_nview?.GetZDO()?.GetOwner() != sender || amount < 1 || amount > 4096) return;
            var player = Player.m_localPlayer;
            if (player != null && (player.transform.position - station.transform.position).sqrMagnitude <= 900f)
                PerkVisualService.PlayRemoteProc(player, "resource_bonus:" + amount, station.transform.position);
        }
    }
    [HarmonyPatch(typeof(Smelter), "Awake")]
    internal static class ProcessingBatchRpcPatch
    {
        private static void Postfix(Smelter __instance)
        {
            if (__instance?.m_nview?.IsValid() != true) return;
            __instance.m_nview.Register<float>("VM_ProcessingAuthor", (sender, level) => ProcessingPersistentBatches.Claim(__instance, sender, level));
            __instance.m_nview.Register<int>("VM_ProcessingBonusFx", (sender, amount) => ProcessingPersistentBatches.Fx(__instance, sender, amount));
        }
    }
    [HarmonyPatch(typeof(Smelter), "OnAddOre")]
    internal static class ProcessingAuthorLevelPatch
    {
        private static void Prefix(Smelter __instance, Humanoid user)
        {
            // Sent on the same ordered routed channel BEFORE vanilla RPC_AddOre.
            // Approved vanilla trust: owner supplies level, station validates sender.
            if (user is Player player && player == Player.m_localPlayer && __instance?.m_nview?.IsValid() == true)
                __instance.m_nview.InvokeRPC("VM_ProcessingAuthor", PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Crafting));
        }
    }
    [HarmonyPatch(typeof(Smelter), "RemoveOneOre")]
    internal static class ProcessingQueueAdvancePatch
    { private static void Prefix(Smelter __instance) => ProcessingPersistentBatches.Advance(__instance); }
    [HarmonyPatch(typeof(Smelter), "QueueProcessed")]
    internal static class ProcessingQueueCompletedPatch
    { private static void Prefix(Smelter __instance, string ore) => ProcessingPersistentBatches.Complete(__instance, ore); }
}
