using System;

namespace ValheimMastery
{
    // Pure policy; Gold owns durable permits, clock deltas, recovery and snapshots.
    // Channel timers are per player, never per station or per item tier.
    internal enum Crafting100CooldownChannel { PerfectCraft = 1, PotentialForge = 2 }

    internal static class Crafting100CooldownRules
    {
        internal const double ForgeSeconds = 15 * 60;
        internal const double MaximumSeconds = 90 * 60;
        internal static double PerfectCraftSeconds(int tier)
        {
            switch (tier)
            {
                case 1: return 15 * 60;
                case 2: return 20 * 60;
                case 3: return 30 * 60;
                case 4: return 40 * 60;
                case 5: return 50 * 60;
                case 6: return 60 * 60;
                case 7: return 75 * 60;
                case 8: return 90 * 60;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }
        // New armor actions only; historical receipts retain the original table.
        internal static double PerfectCraftSeconds(int tier, bool armor) =>
            PerfectCraftSeconds(tier) / (armor ? 2 : 1);
        internal static double Duration(Crafting100CooldownChannel channel, int itemTier)
        {
            if (channel == Crafting100CooldownChannel.PerfectCraft) return PerfectCraftSeconds(itemTier);
            if (channel == Crafting100CooldownChannel.PotentialForge) return ForgeSeconds;
            throw new ArgumentOutOfRangeException(nameof(channel));
        }
        internal static bool ValidRemaining(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= MaximumSeconds;
        // elapsed is validated active-player gameplay time from the server, not UTC/offline time.
        internal static double RemainingAfterElapsed(double remaining, double elapsed)
        {
            if (!ValidRemaining(remaining)) throw new ArgumentOutOfRangeException(nameof(remaining));
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            return Math.Max(0, remaining - elapsed);
        }
        internal static bool CanStart(Crafting100CooldownChannel channel, double craftRemaining,
            double forgeRemaining, bool unresolvedAction)
        {
            if (unresolvedAction || !ValidRemaining(craftRemaining) || !ValidRemaining(forgeRemaining)) return false;
            if (channel == Crafting100CooldownChannel.PerfectCraft) return craftRemaining == 0;
            if (channel == Crafting100CooldownChannel.PotentialForge) return forgeRemaining == 0;
            return false;
        }
    }
}
