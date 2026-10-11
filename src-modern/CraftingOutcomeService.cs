using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ValheimMastery
{
    // DoCrafting returns void and has several ordinary early returns. A postfix alone
    // is not proof of a craft: confirm that the requested output quality was added.
    // Vanilla upgrades remove the old item and add a replacement, so checking only
    // the original ItemData.m_quality would miss successful upgrades.
    internal sealed class CraftingOutcomeSnapshot
    {
        private Inventory _inventory;
        private string _outputKey;
        private int _quality;
        private int _before;
        private bool _wholeProduct, _observed;
        private int _expected;
        private static readonly ConditionalWeakTable<Inventory,List<WeakReference<CraftingOutcomeSnapshot>>> Pending =
            new ConditionalWeakTable<Inventory,List<WeakReference<CraftingOutcomeSnapshot>>>();

        internal static CraftingOutcomeSnapshot Capture(Player player, Recipe recipe, int quality)
        {
            if (player == null || recipe?.m_item?.m_itemData == null) return null;
            var snapshot = new CraftingOutcomeSnapshot
            {
                _inventory = player.GetInventory(),
                _outputKey = GatheringProgressionService.Normalize(recipe.m_item.gameObject.name),
                _quality = quality,
                _wholeProduct = CookingProductIdentity.Relevant(recipe.m_item.m_itemData)
            };
            if (snapshot._inventory == null) return null;
            snapshot._before = snapshot.CountOutput();
            if (snapshot._wholeProduct)
            {
                var rows = Pending.GetOrCreateValue(snapshot._inventory);
                rows.RemoveAll(row => !row.TryGetTarget(out var old) || old._observed);
                rows.Add(new WeakReference<CraftingOutcomeSnapshot>(snapshot));
            }
            return snapshot;
        }

        // Native creation's exact requested stack includes batch and resolved bonuses.
        internal static void ExpectAll(Inventory inventory, string key, int quality, int amount)
        {
            if (inventory == null || amount <= 0 || !Pending.TryGetValue(inventory, out var rows)) return;
            rows.RemoveAll(row => !row.TryGetTarget(out var old) || old._observed);
            foreach (var row in rows)
                if (row.TryGetTarget(out var snapshot) && snapshot._quality == quality && snapshot._outputKey == key)
                    snapshot._expected = amount;
        }
        internal bool HasSuccessfulOutput()
        {
            _observed = true;
            int increase = CountOutput() - _before;
            return _wholeProduct ? _expected > 0 && increase >= _expected : increase > 0;
        }

        private int CountOutput()
        {
            int count = 0;
            foreach (ItemDrop.ItemData item in _inventory.GetAllItems())
                if (item != null && item.m_quality == _quality &&
                    string.Equals(TierDatabase.ItemKey(item), _outputKey, StringComparison.Ordinal))
                    count += item.m_stack;
            return count;
        }
    }
}
