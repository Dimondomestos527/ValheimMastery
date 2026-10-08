using System;

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

        internal static CraftingOutcomeSnapshot Capture(Player player, Recipe recipe, int quality)
        {
            if (player == null || recipe?.m_item?.m_itemData == null) return null;
            var snapshot = new CraftingOutcomeSnapshot
            {
                _inventory = player.GetInventory(),
                _outputKey = GatheringProgressionService.Normalize(recipe.m_item.gameObject.name),
                _quality = quality
            };
            if (snapshot._inventory == null) return null;
            snapshot._before = snapshot.CountOutput();
            return snapshot;
        }

        internal bool HasSuccessfulOutput() => CountOutput() > _before;

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
