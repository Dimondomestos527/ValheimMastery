using System;
using UnityEngine;

namespace ValheimMastery
{
    internal enum Crafting100ApplicationResult { Applied, Rejected, Uncertain }

    // Called only for an authenticated admitted permit. Gold owns request/phase journaling and retries.
    // Persistence callbacks save their phase and the mutated inventory together, never memory-only ACK.
    internal static class Crafting100ActionApplication
    {
        internal const string GrantKey = "VM_Crafting100_Grant_v1";
        internal const string IdentityKey = "VM_Gold_ItemIdentity";

        internal static Crafting100ApplicationResult Apply(Player player, Recipe recipe,
            ItemDrop.ItemData target, int kind, int variant, int expectedQuality, string token,
            Func<bool> currentPermit, Func<bool> persistApplied, Func<bool> persistRejected)
        {
            if (persistApplied == null || persistRejected == null) return Crafting100ApplicationResult.Uncertain;
            Inventory inventory = player?.GetInventory();
            ItemDrop.ItemData created = null, idol = null;
            int oldStack = 0, oldIndex = -1;
            float oldDurability = target?.m_durability ?? 0;
            bool mutated = false, hadGrant = false;
            string oldGrant = null;
            try
            {
                if (!Valid(player, recipe, target, kind, variant, expectedQuality, token, currentPermit))
                    return Reject(persistRejected);
                if (kind == 1)
                {
                    if (!inventory.CanAddItem(recipe.m_item.gameObject, 1)) return Reject(persistRejected);
                    created = recipe.m_item.m_itemData.Clone();
                    created.m_dropPrefab = recipe.m_item.gameObject;
                    created.m_quality = 5; created.m_variant = variant; created.m_stack = 1;
                    created.m_worldLevel = Game.m_worldLevel;
                    created.m_crafterID = player.GetPlayerID(); created.m_crafterName = player.GetPlayerName();
                    created.m_customData.Remove("VM_Masterwork"); created.m_customData.Remove("VM_MasterworkPatron");
                    created.m_customData.Remove(Crafting100ForgeItemRules.UsedKey);
                    created.m_customData[GrantKey] = token;
                    created.m_customData[IdentityKey] = Guid.NewGuid().ToString("N");
                    created.m_durability = created.GetMaxDurability();
                    // Changed callbacks may throw after insertion.
                    mutated = true;
                    if (!inventory.AddItem(created)) throw new InvalidOperationException("Crafting100 capacity changed.");
                }
                else
                {
                    idol = player.GetFirstRequiredItem(inventory, recipe, expectedQuality + 1, out _, out _, 1);
                    if (!RequiredIdol(recipe, idol, expectedQuality + 1) || ReferenceEquals(idol, target))
                        return Reject(persistRejected);
                    oldStack = idol.m_stack; oldIndex = inventory.m_inventory.IndexOf(idol);
                    hadGrant = target.m_customData.TryGetValue(GrantKey, out oldGrant);
                    // Revalidate immediately before native debit and item mutation.
                    if (!Valid(player, recipe, target, kind, variant, expectedQuality, token, currentPermit))
                        return Reject(persistRejected);
                    mutated = true;
                    if (!inventory.RemoveItem(idol, 1)) throw new InvalidOperationException("Crafting100 idol debit changed.");
                    target.m_quality = Crafting100ForgeItemRules.UpgradedQuality(expectedQuality, target.m_customData);
                    target.m_durability = target.GetMaxDurability();
                    Crafting100ForgeItemRules.MarkApplied(target.m_customData, token);
                    target.m_customData[GrantKey] = token;
                    inventory.Changed();
                }
                // Gold persistence revalidates session/player/pending journal after inventory callbacks.
                if (persistApplied()) return Crafting100ApplicationResult.Applied;
            }
            catch (Exception) { /* Roll back below, including callbacks throwing after native mutation. */ }

            if (mutated)
            {
                try
                {
                    if (kind == 1) inventory.m_inventory.Remove(created);
                    else
                    {
                        target.m_quality = expectedQuality; target.m_durability = oldDurability;
                        target.m_customData.Remove(Crafting100ForgeItemRules.UsedKey);
                        if (hadGrant) target.m_customData[GrantKey] = oldGrant; else target.m_customData.Remove(GrantKey);
                        idol.m_stack = oldStack;
                        if (!inventory.ContainsItem(idol))
                            inventory.m_inventory.Insert(Math.Min(Math.Max(0, oldIndex), inventory.m_inventory.Count), idol);
                    }
                }
                catch (Exception) { return Crafting100ApplicationResult.Uncertain; }
                // Authoritative fields restored first; display callback failure must not defeat rollback/save.
                try { inventory.Changed(); } catch (Exception) { }
            }
            return Reject(persistRejected);
        }

        private static Crafting100ApplicationResult Reject(Func<bool> persistRejected)
        {
            try { return persistRejected() ? Crafting100ApplicationResult.Rejected : Crafting100ApplicationResult.Uncertain; }
            catch (Exception) { return Crafting100ApplicationResult.Uncertain; }
        }
        private static bool Valid(Player player, Recipe recipe, ItemDrop.ItemData target, int kind,
            int variant, int expectedQuality, string token, Func<bool> currentPermit)
        {
            if (player == null || player != Player.m_localPlayer || player.IsDead() || player.IsTeleporting() ||
                currentPermit?.Invoke() != true || !Guid.TryParseExact(token, "N", out _) ||
                recipe?.m_enabled != true || recipe.m_item?.m_itemData?.m_shared == null ||
                !recipe.m_item.m_itemData.IsEquipable() || recipe.m_item.m_itemData.m_shared.m_maxStackSize != 1 ||
                recipe.m_item.m_itemData.m_shared.m_maxQuality <= 1 || variant < 0 ||
                recipe.m_item.m_itemData.m_shared.m_icons == null || variant >= recipe.m_item.m_itemData.m_shared.m_icons.Length)
                return false;
            if (kind == 1) return target == null && expectedQuality == 0;
            return kind == 2 && target != null && player.GetInventory().ContainsItem(target) &&
                target.m_stack == 1 && target.m_quality == expectedQuality && target.m_variant == variant &&
                TierDatabase.ItemKey(target) == recipe.m_item.gameObject.name &&
                Crafting100ForgeItemRules.CanUpgrade(expectedQuality, target.m_customData);
        }
        private static bool RequiredIdol(Recipe recipe, ItemDrop.ItemData idol, int nextQuality)
        {
            if (idol?.m_shared == null || idol.m_stack < 1 || recipe.m_resources == null) return false;
            foreach (var requirement in recipe.m_resources)
                if (requirement?.m_upgraderResource == true && requirement.m_resItem != null &&
                    requirement.GetAmount(nextQuality) > 0 &&
                    TierDatabase.ItemKey(idol) == requirement.m_resItem.gameObject.name) return true;
            return false;
        }
    }
}

