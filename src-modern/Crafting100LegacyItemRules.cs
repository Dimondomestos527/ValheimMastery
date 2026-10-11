using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    // Item evidence only: caller must ALSO prove authenticated server pending, durable
    // character Applied receipt and unique inventory identity/token before importing.
    // This helper cannot authorize an effect, ledger cleanup or a profile save.
    internal static class Crafting100LegacyItemRules
    {
        internal const string LegacyGrantKey = "VM_Gold_Grant";
        internal static bool MatchesAppliedItem(IDictionary<string, string> data, string prefab,
            int quality, int variant, long crafter, long owner, GoldAction action)
        {
            if (data == null || action == null || !Guid.TryParseExact(action.Token, "N", out _) ||
                string.IsNullOrEmpty(action.Prefab) || prefab != action.Prefab || variant != action.Variant ||
                quality < 1 || quality > 10000 || !data.TryGetValue(LegacyGrantKey, out var grant) || grant != action.Token)
                return false;
            if (action.Kind == 1)
                return action.OldQuality == 0 && action.NewQuality == 5 && quality >= 5 && owner != 0 && crafter == owner;
            if (action.Kind != 2 || action.OldQuality < 1 || action.OldQuality > 9997 ||
                action.NewQuality != action.OldQuality + 3 || quality < action.NewQuality ||
                !Guid.TryParseExact(action.ItemIdentity, "N", out _) ||
                !data.TryGetValue(Crafting100ActionApplication.IdentityKey, out var identity) || identity != action.ItemIdentity)
                return false;
            return CanImportForgeMarker(data, action);
        }
        internal static bool CanImportForgeMarker(IDictionary<string, string> data, GoldAction action)
        {
            if (data == null || action?.Kind != 2 || !Guid.TryParseExact(action.Token, "N", out _)) return false;
            return !data.TryGetValue(Crafting100ForgeItemRules.UsedKey, out var marker) || marker == action.Token;
        }
    }
}
