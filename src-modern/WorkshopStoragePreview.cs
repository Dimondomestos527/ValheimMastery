using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // UI-only, short-lived counts. No locks, owner transfer, or writes. The real
    // request ALWAYS replans against current authoritative stock and permissions.
    internal static class WorkshopStoragePreview
    {
        private static ZNet _session;
        private static Player _player;
        private static uint _revision;
        private static int _component = -1;
        private static float _expires;
        private static float _refreshUntil, _refreshAt;
        private static readonly Dictionary<string, long> Counts = new Dictionary<string, long>(StringComparer.Ordinal);
        internal static readonly List<Container> Contributors = new List<Container>();
        internal static void Invalidate() => _expires = 0f;
        internal static void RefreshUiSoon()
        { Invalidate(); _refreshUntil = Time.time + 1.25f; _refreshAt = 0f; }
        internal static void Tick()
        {
            if (Time.time > _refreshUntil || Time.time < _refreshAt || WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding) return;
            var gui = InventoryGui.instance;
            if (gui == null || !InventoryGui.IsVisible() || gui.m_craftTimer >= 0f) return;
            _refreshAt = Time.time + .25f; Invalidate(); gui.UpdateCraftingPanel(false);
        }
        internal static long StoredCount(string item) => Counts.TryGetValue(item, out long count) ? count : 0L;
        internal static bool CanPay(Player player, Piece.Requirement[] requirements, int quality, int amount)
        {
            if (!WorkshopRemoteCraft.TryResourceCosts(requirements, quality, amount, out var costs) || !Refresh(player)) return false;
            foreach (var cost in costs)
            {
                Counts.TryGetValue(cost.Key, out long stored);
                if ((long)player.GetInventory().CountItems(cost.Key, -1, false) + stored < cost.Value) return false;
            }
            return true;
        }
        // Per-row preview must also work for an incomplete recipe. It never grants output.
        internal static bool Refresh(Player player)
        {
            if (!WorkshopRemoteCraft.Ready || player == null || player != Player.m_localPlayer ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70) ||
                player.IsDead() || player.IsTeleporting()) { Counts.Clear(); Contributors.Clear(); Invalidate(); return false; }
            int component = WorkshopNetwork.CoveredComponent(player.transform.position);
            if (component < 0) { Counts.Clear(); Contributors.Clear(); Invalidate(); return false; }
            if (_session != ZNet.instance || _player != player || _component != component ||
                _revision != WorkshopNetwork.Revision || Time.time >= _expires)
            {
                Counts.Clear();
                Contributors.Clear();
                foreach (var chest in WorkshopNetworkStorage.FindEligible(player, player.transform.position, false, true))
                {
                    // Native Load reads only the current ZDO inventory and suppresses Save
                    // through m_loading. No ownership claim or resource mutation is made.
                    chest.Load();
                    Contributors.Add(chest);
                    foreach (var item in chest.GetInventory().GetAllItems())
                    {
                        if (item?.m_shared == null || item.m_stack <= 0) continue;
                        string name = item.m_shared.m_name;
                        Counts.TryGetValue(name, out long count); Counts[name] = count + item.m_stack;
                    }
                }
                _session = ZNet.instance; _player = player; _component = component;
                _revision = WorkshopNetwork.Revision; _expires = Time.time + 0.5f;
            }
            return true;
        }
    }
}
