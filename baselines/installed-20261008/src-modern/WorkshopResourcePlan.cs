using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    internal sealed class WorkshopResourceDebit
    {
        internal Inventory Inventory;
        internal Container Container; // null only for the player's own inventory
        internal string ItemName;
        internal int Amount;
    }

    // Produces a deterministic proposal, never removes materials or changes
    // HaveRequirements. A future server commit must revalidate every debit.
    internal static class WorkshopResourcePlan
    {
        internal static bool TryBuild(Inventory personal, IList<Container> eligibleChests,
            Piece.Requirement[] requirements, int quality, out List<WorkshopResourceDebit> debits,
            int multiplier = 1)
        {
            debits = new List<WorkshopResourceDebit>();
            if (personal == null || eligibleChests == null || requirements == null || quality < 0 || multiplier < 1) return false;

            // Recipes can contain the same item more than once. Aggregate before
            // counting so one stack cannot satisfy two separate requirements.
            Dictionary<string, int> amounts = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            foreach (Piece.Requirement requirement in requirements)
            {
                if (requirement?.m_upgraderResource == true) continue;
                if (requirement?.m_resItem?.m_itemData?.m_shared == null) return false;
                // Resource quality selection / alternative ingredients need their own
                // adapter. Never combine incompatible qualities to make a craft pass.
                if (requirement.m_resItem.m_itemData.m_shared.m_maxQuality != 1) return false;
                int amount = requirement.GetAmount(quality);
                if (amount <= 0) continue;
                if (amount > int.MaxValue / multiplier) return false;
                amount *= multiplier;
                string name = requirement.m_resItem.m_itemData.m_shared.m_name;
                if (string.IsNullOrEmpty(name)) return false;
                if (!amounts.ContainsKey(name)) { amounts[name] = 0; order.Add(name); }
                if (amounts[name] > int.MaxValue - amount) return false;
                amounts[name] += amount;
            }

            foreach (string name in order)
            {
                int left = amounts[name];
                int own = Math.Min(left, personal.CountItems(name, -1, false));
                if (own > 0)
                {
                    debits.Add(new WorkshopResourceDebit { Inventory = personal, ItemName = name, Amount = own });
                    left -= own;
                }
                var visited = new HashSet<Inventory> { personal };
                foreach (Container chest in eligibleChests)
                {
                    if (left == 0) break;
                    Inventory inventory = chest?.GetInventory();
                    if (inventory == null || !visited.Add(inventory)) continue;
                    int take = Math.Min(left, inventory.CountItems(name, -1, false));
                    if (take <= 0) continue;
                    debits.Add(new WorkshopResourceDebit
                    {
                        Inventory = inventory, Container = chest, ItemName = name, Amount = take
                    });
                    left -= take;
                }
                if (left != 0) { debits.Clear(); return false; }
            }
            return true;
        }
    }
}
