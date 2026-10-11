using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    // Internal persistent item metadata, not a visible masterpiece label or a receipt replacement.
    internal static class Crafting100ForgeItemRules
    {
        internal const string UsedKey = "VM_Crafting100_ForgeOnce_v1";
        internal static bool Used(IDictionary<string, string> metadata) => metadata == null || metadata.ContainsKey(UsedKey);
        internal static bool CanUpgrade(int quality, IDictionary<string, string> metadata) =>
            quality >= 1 && quality <= 9997 && !Used(metadata);
        internal static int UpgradedQuality(int quality, IDictionary<string, string> metadata)
        {
            if (!CanUpgrade(quality, metadata)) throw new InvalidOperationException("Special forge upgrade unavailable.");
            return checked(quality + 3);
        }
        // Must be called inside the same item mutation/save boundary as quality +3 and idol debit.
        // Caller restores the exact prior dictionary entry together with item/idol on rollback.
        internal static void MarkApplied(IDictionary<string, string> metadata, string actionToken)
        {
            if (Used(metadata) || !Guid.TryParseExact(actionToken, "N", out _))
                throw new InvalidOperationException("Special forge upgrade metadata unavailable.");
            metadata.Add(UsedKey, actionToken);
        }
    }
}
