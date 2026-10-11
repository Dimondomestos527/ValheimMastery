using System;
using UnityEngine;

namespace ValheimMastery
{
    // Gold alone supplies authenticated, confirmed durable legacy phase and a linked
    // receipt/inventory save callback. False means unresolved; never clean old holds.
    internal static class Crafting100LegacyItemApplication
    {
        internal static bool Prepare(Player player, GoldAction action, int phase, Func<bool> persistLinkedProof)
        {
            if (player == null || player != Player.m_localPlayer || action == null ||
                !Guid.TryParseExact(action.Token, "N", out _) || persistLinkedProof == null || phase < 1 || phase > 3)
                return false;
            var inventory = player.GetInventory();
            if (inventory == null) return false;
            ItemDrop.ItemData matched = null, identityTarget = null;
            int grants = 0, identities = 0, newGrants = 0;
            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_customData == null) return false;
                if (item.m_customData.TryGetValue(Crafting100LegacyItemRules.LegacyGrantKey, out var token) && token == action.Token)
                { grants++; matched = item; }
                if (item.m_customData.TryGetValue(Crafting100ActionApplication.GrantKey, out var newToken) && newToken == action.Token) newGrants++;
                if (action.Kind == 2 && item.m_customData.TryGetValue(Crafting100ActionApplication.IdentityKey, out var id) && id == action.ItemIdentity)
                { identities++; identityTarget = item; }
            }
            if (action.Kind != 1 && action.Kind != 2) return false;
            if (phase != 2)
            {
                if (grants != 0 || newGrants != 0) return false;
                if (action.Kind == 2 && (identities != 1 || identityTarget == null ||
                    TierDatabase.ItemKey(identityTarget) != action.Prefab || identityTarget.m_variant != action.Variant ||
                    identityTarget.m_quality != action.OldQuality ||
                    (phase == 1 && Crafting100ForgeItemRules.Used(identityTarget.m_customData)))) return false;
                return Save(persistLinkedProof);
            }
            if (grants != 1 || matched == null || (action.Kind == 2 && (identities != 1 || !ReferenceEquals(matched, identityTarget))) ||
                !Crafting100LegacyItemRules.MatchesAppliedItem(matched.m_customData, TierDatabase.ItemKey(matched),
                    matched.m_quality, matched.m_variant, matched.m_crafterID, player.GetPlayerID(), action)) return false;
            if (action.Kind == 1) return Save(persistLinkedProof);
            bool existed = matched.m_customData.TryGetValue(Crafting100ForgeItemRules.UsedKey, out var oldMarker);
            try
            {
                if (!existed) Crafting100ForgeItemRules.MarkApplied(matched.m_customData, action.Token);
                if (Save(persistLinkedProof)) return true;
            }
            catch (Exception) { }
            // Exact memory rollback; failed/uncertain native save must remain Gold-quarantined.
            // Do not send Rejected or issue another save/quality change here.
            if (existed) matched.m_customData[Crafting100ForgeItemRules.UsedKey] = oldMarker;
            else matched.m_customData.Remove(Crafting100ForgeItemRules.UsedKey);
            return false;
        }
        private static bool Save(Func<bool> persist)
        { try { return persist(); } catch (Exception) { return false; } }
    }
}
