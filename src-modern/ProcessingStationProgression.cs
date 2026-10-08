using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal static class ProcessingStationProgressionService
    {
        internal static long ResolveSender(long sender)
        {
            ZNet net = ZNet.instance;
            if (net == null) return 0L;
            if (sender == ZNet.GetUID()) return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            // A client owner may receive a routed RPC from another client without a
            // direct ZNetPeer for that sender. Never substitute the owner's player.
            var peer = net.GetPeer(sender);
            if (peer != null)
            {
                long id = OwnerSkillAuthority.ResolvePlayerId(peer);
                if (id != 0) return id;
                Player bound = OwnerSkillAuthority.ResolvePlayer(peer);
                if (bound != null) return bound.GetPlayerID();
            }
            foreach (var player in Player.GetAllPlayers())
                if (player?.m_nview?.GetZDO()?.GetOwner() == sender) return player.GetPlayerID();
            return 0L;
        }

        internal static bool CanDeliver(Player player)
        {
            if (player == null) return false;
            if (player == Player.m_localPlayer) return true;
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer()) return false;
            foreach (ZNetPeer peer in net.GetPeers())
                if (peer?.m_rpc != null && OwnerSkillAuthority.ResolvePlayer(peer) == player) return true;
            return false;
        }
    }

    internal sealed class ProcessingInputState
    {
        internal long PlayerId;
        internal int QueueBefore;
    }

    internal sealed class ProcessingOutputState
    {
        internal string Ore;
        internal string OutputKey;
        internal int Original;
        internal int Bonus;
        internal int InstanceCountBefore;
        internal Vector3 Position;
        internal ProcessingBatchLedger Ledger;
    }

    [HarmonyPatch(typeof(Smelter), "RPC_AddOre")]
    internal static class ProcessingStationOwnerPatch
    {
        private static void Prefix(Smelter __instance, long sender, string name, out ProcessingInputState __state)
        {
            __state = null;
            if (__instance?.m_nview == null || !__instance.m_nview.IsOwner() || !__instance.IsItemAllowed(name)) return;
            __state = new ProcessingInputState
            {
                PlayerId = ProcessingStationProgressionService.ResolveSender(sender),
                QueueBefore = __instance.GetQueueSize()
            };
        }

        private static void Postfix(Smelter __instance, ProcessingInputState __state)
        {
            if (__state != null && __instance != null && __instance.GetQueueSize() > __state.QueueBefore)
                // Only accepted input receives a persisted author and one bonus roll.
                ProcessingPersistentBatches.Record(__instance, __state.PlayerId, __state.QueueBefore);
        }
    }

    [HarmonyPatch(typeof(Smelter), nameof(Smelter.Spawn))]
    internal static class ProcessingStationOutputPatch
    {
        [ThreadStatic] private static bool BonusOutput;
        private static void Prefix(Smelter __instance, string ore, ref int stack, out ProcessingOutputState __state)
        {
            __state = null;
            if (BonusOutput || __instance?.m_nview == null || !__instance.m_nview.IsOwner() || __instance.m_outputPoint == null || stack <= 0) return;
            Smelter.ItemConversion conversion = __instance.GetItemConversion(ore);
            if (conversion?.m_to?.m_itemData == null) return;
            int original = stack;
            var ledger = ProcessingBatchLedger.Read(__instance.m_nview.GetZDO().GetString(ProcessingPersistentBatches.OutputKey, ""), original);
            int bonus = 0;
            foreach (var receipt in ledger.Items) if (receipt.Bonus) bonus++;

            __state = new ProcessingOutputState
            {
                Ledger = ledger,
                Ore = ore,
                OutputKey = GatheringProgressionService.Normalize(conversion.m_to.gameObject.name),
                Original = original,
                Bonus = bonus,
                InstanceCountBefore = ItemDrop.s_instances.Count,
                Position = __instance.m_outputPoint.position
            };
        }

        private static void Postfix(Smelter __instance, ProcessingOutputState __state)
        {
            if (__state == null) return;
            // The current game's ItemDrop.Awake appends to s_instances synchronously.
            // Inspect only objects added during this Spawn call, without a scene scan
            // or a global ItemDrop creation hook. A void return is not a success signal.
            ItemDrop output = null;
            for (int i = __state.InstanceCountBefore; i < ItemDrop.s_instances.Count; ++i)
            {
                ItemDrop item = ItemDrop.s_instances[i];
                if (item?.m_itemData != null && item.m_itemData.m_stack == __state.Original &&
                    TierDatabase.ItemKey(item.m_itemData) == __state.OutputKey &&
                    (item.transform.position - __state.Position).sqrMagnitude < 0.01f)
                { output = item; break; }
            }
            if (output == null) return;
            // Consume the persisted receipts only after real output exists.
            __instance.m_nview.GetZDO().Set(ProcessingPersistentBatches.OutputKey, "");
            int delivered = 0;
            if (__state.Bonus > 0)
            {
                // Never create an oversized vanilla stack. Extra output has its own
                // stacks; recursion is scoped and cannot earn bonus or XP again.
                BonusOutput = true;
                try
                {
                    int remaining = __state.Bonus;
                    while (remaining > 0)
                    {
                        int amount = Math.Min(remaining, Math.Max(1, output.m_itemData.m_shared.m_maxStackSize));
                        int before = ItemDrop.s_instances.Count;
                        __instance.Spawn(__state.Ore, amount);
                        bool found = false;
                        for (int i = before; i < ItemDrop.s_instances.Count; i++)
                        {
                            var item = ItemDrop.s_instances[i];
                            if (item?.m_itemData != null && item.m_itemData.m_stack == amount && TierDatabase.ItemKey(item.m_itemData) == __state.OutputKey &&
                                (item.transform.position - __state.Position).sqrMagnitude < .01f) { found = true; break; }
                        }
                        if (!found) break;
                        delivered += amount; remaining -= amount;
                    }
                }
                finally { BonusOutput = false; }
                if (delivered > 0) __instance.m_nview.InvokeRPC(ZNetView.Everybody, "VM_ProcessingBonusFx", delivered);
            }
            int tier = Math.Max(GatheringProgressionService.GetResourceTier(__state.Ore, Skills.SkillType.Pickaxes),
                GatheringProgressionService.GetResourceTier(__state.Ore, Skills.SkillType.Farming));
            if (__state.OutputKey == "Eitr") tier = 6;
            var authors = new Dictionary<long, int>();
            foreach (var receipt in __state.Ledger.Items)
                if (receipt.Author != 0) { authors.TryGetValue(receipt.Author, out int count); authors[receipt.Author] = count + 1; }
            foreach (var author in authors)
            {
                var player = Player.GetPlayer(author.Key);
                if (ProcessingStationProgressionService.CanDeliver(player))
                    NetworkSync.SendStationXp(player, .25f * tier * author.Value, "processing." + __state.Ore);
            }
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[ProcessingXP] ore=" + __state.Ore + " output=" + __state.Original + " bonus=" + delivered + " authors=" + authors.Count + " craftingXP=" +
                    (0.25f * Mathf.Max(1, tier) * __state.Original).ToString("0.###"));
        }
    }
}
