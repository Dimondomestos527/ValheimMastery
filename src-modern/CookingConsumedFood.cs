using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ValheimMastery
{
    // Native food time remains authoritative, including save/load and offline time.
    // A record qualifies one active consumption, never every future meal of a name.
    internal static class CookingConsumedFood
    {
        private const string Prefix = "vm.cook.consumed35.v1.";
        private sealed class Entry { internal string Raw; internal CookingProductRecord Product; internal ItemDrop.ItemData Applied; internal bool Qualified; }
        private sealed class PlayerRecords { internal bool Loaded; internal readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(); }
        private static readonly ConditionalWeakTable<Player, PlayerRecords> Cache = new ConditionalWeakTable<Player, PlayerRecords>();
        private static Dictionary<string, Entry> Entries(Player player)
        {
            var state = Cache.GetOrCreateValue(player);
            if (!state.Loaded)
            {
                foreach (var pair in player.m_customData)
                    if (pair.Key.StartsWith(Prefix, StringComparison.Ordinal))
                        state.Entries[pair.Key.Substring(Prefix.Length)] = new Entry { Raw = pair.Value };
                state.Loaded = true;
            }
            return state.Entries;
        }

        internal static void Record(Player player, ItemDrop.ItemData item)
        {
            if (player != Player.m_localPlayer || !Cooking35Service.IsPreparedMeal(item)) return;
            string prefab = TierDatabase.ItemKey(item);
            if (string.IsNullOrEmpty(prefab)) return;
            // Cooking70 owns its serving clone, persisted theme and full-strength rule.
            // Supersede only our35 record if the same food was replaced by a70 serving.
            if (Cooking70Service.IsFeast(item) && Cooking35Service.HasCookMastery(player, item, 70))
            { player.m_customData.Remove(Prefix + prefab); Entries(player).Remove(prefab); return; }
            foreach (var food in player.m_foods)
            {
                if (food == null || food.m_time <= 0f || !string.Equals(food.m_name, prefab, StringComparison.Ordinal)) continue;
                var snapshot = item.Clone();
                var product = CookingProductRecord.Capture(snapshot);
                // Encoding validates the bounded metadata before altering this slot.
                string raw = Guid.NewGuid().ToString("N") + ";" + product.Encode();
                food.m_item = snapshot;
                player.m_customData[Prefix + prefab] = raw;
                Entries(player)[prefab] = new Entry { Raw = raw, Product = product, Applied = snapshot,
                    Qualified = Cooking35Service.HasCookMastery(player, snapshot, 35) };
                return;
            }
        }

        internal static bool Qualified(Player player, Player.Food food)
        {
            if (player == null || food == null || food.m_time <= 0f || string.IsNullOrEmpty(food.m_name)) return false;
            if (!player.m_customData.TryGetValue(Prefix + food.m_name, out string raw))
                return Cooking35Service.HasCookMastery(player, food.m_item, 35) || CookingFoodPersistence.Has(player, food.m_name);
            try
            {
                var entries = Entries(player);
                if (!entries.TryGetValue(food.m_name, out var entry) || entry.Raw != raw || entry.Product == null)
                {
                    if (raw == null || raw.Length > 11000 || raw.Length < 34 || raw[32] != ';' ||
                        !Guid.TryParseExact(raw.Substring(0, 32), "N", out Guid id) || id == Guid.Empty) return false;
                    var product = CookingProductRecord.Decode(raw.Substring(33));
                    if (!string.Equals(product.Prefab, food.m_name, StringComparison.Ordinal)) return false;
                    entry = new Entry { Raw = raw, Product = product };
                    entries[food.m_name] = entry;
                }
                if (!ReferenceEquals(entry.Applied, food.m_item))
                {
                    // Load restores native item templates. Reattach this consumption's
                    // metadata only on a private clone; never touch the prefab.
                    var snapshot = food.m_item?.Clone();
                    if (snapshot == null) return false;
                    entry.Product.Apply(snapshot);
                    food.m_item = snapshot;
                    entry.Applied = snapshot;
                    entry.Qualified = Cooking35Service.HasCookMastery(player, snapshot, 35);
                }
                return entry.Qualified;
            }
            catch (Exception) { return false; } // Malformed records never grant mastery.
        }

        internal static void DenyFailedRecord(Player player, ItemDrop.ItemData item)
        {
            if (player != Player.m_localPlayer || item == null || player.m_foods == null) return;
            string prefab = TierDatabase.ItemKey(item);
            foreach (var food in player.m_foods)
                if (food != null && food.m_time > 0f && food.m_name == prefab)
                {
                    // Supersede any older consumption/legacy fallback even when the
                    // new product's metadata cannot be serialized. Native meal stands.
                    player.m_customData[Prefix + prefab] = "!";
                    Entries(player)[prefab] = new Entry { Raw = "!" };
                    return;
                }
        }

        internal static void Prune(Player player)
        {
            if (player != Player.m_localPlayer || player.m_foods == null) return;
            List<string> obsolete = null;
            foreach (var pair in Entries(player))
            {
                string prefab = pair.Key;
                bool active = false;
                foreach (var food in player.m_foods)
                    if (food != null && food.m_time > 0f && food.m_name == prefab) { active = true; break; }
                if (!active) { if (obsolete == null) obsolete = new List<string>(); obsolete.Add(prefab); }
            }
            if (obsolete == null) return;
            foreach (string key in obsolete)
            {
                player.m_customData.Remove(Prefix + key);
                Entries(player).Remove(key);
            }
            // Historical vm.cook.eaten35.* and Cooking70 records are untouched.
        }
    }
}
