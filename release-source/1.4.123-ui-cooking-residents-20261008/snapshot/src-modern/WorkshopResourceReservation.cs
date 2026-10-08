using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // A short-lived server-side intent lock. It does not grant craft permission,
    // mutate inventories or deliver output. The eventual action RPC must invoke
    // a separate, server-authorized commit while holding this same lock.
    internal static class WorkshopResourceReservation
    {
        private sealed class Entry
        {
            internal Guid Id;
            internal long PlayerId;
            internal uint NetworkRevision;
            internal float ExpiresAt;
            internal List<WorkshopResourceDebit> Plan;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<int, Entry> ByComponent = new Dictionary<int, Entry>();

        internal static bool TryReserve(Player player, Vector3 actionPoint,
            Piece.Requirement[] requirements, int quality, out Guid reservationId)
        {
            reservationId = Guid.Empty;
            if (ZNet.instance == null || !ZNet.instance.IsServer() || player == null ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70)) return false;
            // Vanilla persists the character inventory through Player.Save, not
            // Container/ZDO sync. Do not mistake a remote server Player copy for
            // an authoritative inventory. Dedicated-server requests fail closed
            // until the owner/server action bridge is implemented.
            if (player != Player.m_localPlayer) return false;
            lock (Gate)
            {
                int component = WorkshopNetwork.CoveredComponent(actionPoint);
                if (component < 0) return false;
                uint revision = WorkshopNetwork.Revision;
                if (ByComponent.TryGetValue(component, out Entry existing))
                {
                    if (existing.ExpiresAt > Time.time && existing.NetworkRevision == revision)
                        return false; // A second player may not reserve the same component.
                    ByComponent.Remove(component);
                }
                List<Container> chests = WorkshopNetworkStorage.FindServerOwnedEligible(player, actionPoint);
                if (!WorkshopResourcePlan.TryBuild(player.GetInventory(), chests,
                    requirements, quality, out List<WorkshopResourceDebit> plan)) return false;
                bool usesNetworkStorage = false;
                foreach (WorkshopResourceDebit debit in plan)
                    if (debit.Container != null) { usesNetworkStorage = true; break; }
                if (!usesNetworkStorage) return false; // Vanilla handles inventory-only actions.
                Guid id = Guid.NewGuid();
                ByComponent[component] = new Entry
                {
                    Id = id, PlayerId = player.GetPlayerID(), NetworkRevision = revision,
                    ExpiresAt = Time.time + 15f, Plan = plan
                };
                reservationId = id;
                return true;
            }
        }

        internal static void Cancel(long playerId, Guid reservationId)
        {
            if (reservationId == Guid.Empty) return;
            lock (Gate)
            {
                int found = -1;
                foreach (KeyValuePair<int, Entry> pair in ByComponent)
                    if (pair.Value.Id == reservationId && pair.Value.PlayerId == playerId)
                    { found = pair.Key; break; }
                if (found >= 0) ByComponent.Remove(found);
            }
        }

        internal static bool IsCurrent(long playerId, Guid reservationId, Vector3 actionPoint)
        {
            if (reservationId == Guid.Empty || ZNet.instance == null || !ZNet.instance.IsServer()) return false;
            lock (Gate)
            {
                int component = WorkshopNetwork.CoveredComponent(actionPoint);
                if (component < 0 || !ByComponent.TryGetValue(component, out Entry entry) ||
                    entry.Id != reservationId || entry.PlayerId != playerId ||
                    entry.NetworkRevision != WorkshopNetwork.Revision || entry.ExpiresAt <= Time.time)
                    return false;
                Player player = Player.GetPlayer(playerId);
                if (player == null || player != Player.m_localPlayer ||
                    !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70)) return false;
                List<Container> eligible = WorkshopNetworkStorage.FindServerOwnedEligible(player, actionPoint);
                foreach (WorkshopResourceDebit debit in entry.Plan)
                {
                    if (debit == null || debit.Inventory == null || debit.Amount <= 0 ||
                        debit.Inventory.CountItems(debit.ItemName, -1, false) < debit.Amount)
                        return false;
                    if (debit.Container == null)
                    {
                        if (!ReferenceEquals(debit.Inventory, player.GetInventory())) return false;
                    }
                    else if (!eligible.Contains(debit.Container) ||
                        !ReferenceEquals(debit.Inventory, debit.Container.GetInventory())) return false;
                }
                return true;
            }
        }
    }
}
