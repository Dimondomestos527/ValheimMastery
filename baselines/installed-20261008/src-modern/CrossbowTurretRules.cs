using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    // Pure policy shared by the native adapter and focused tests. No inventory writes.
    internal enum CrossbowTurretState { Unmarked, Supply, Frozen, Active, Return, Recovery }
    internal enum CrossbowTurretItemKind { Other, Crossbow, Bolt }
    internal readonly struct CrossbowTurretStockEntry
    {
        internal readonly CrossbowTurretItemKind Kind;
        internal readonly int Stack;
        internal readonly string AmmoType;
        internal readonly bool Ready;
        internal CrossbowTurretStockEntry(CrossbowTurretItemKind kind, int stack, string ammoType, bool ready = true)
        { Kind = kind; Stack = stack; AmmoType = ammoType; Ready = ready; }
    }
    internal static class CrossbowTurretRules
    {
        internal const int MaximumSnapshotBytes = 262144;
        internal const int MaximumEntries = 64;
        internal static float DamageFactor(float level, float roll)
        {
            level = Math.Max(0f, Math.Min(100f, level)); roll = Math.Max(0f, Math.Min(1f, roll));
            float center = .4f + .6f * level / 100f;
            float min = Math.Max(0f, center - .15f), max = Math.Min(1f, center + .15f);
            float zero = .25f + .3f * roll;
            return zero + .85f * (min + (max - min) * roll - zero);
        }

        internal static bool Validate(IReadOnlyList<CrossbowTurretStockEntry> entries, out int bolts, out string reason)
        {
            bolts = 0; reason = null;
            if (entries == null || entries.Count == 0 || entries.Count > MaximumEntries)
                return Fail("stock_size", out reason);
            string ammoType = null;
            int weapons = 0;
            foreach (var entry in entries)
            {
                if (entry.Stack <= 0) return Fail("invalid_stack", out reason);
                if (entry.Kind == CrossbowTurretItemKind.Other) return Fail("foreign_item", out reason);
                if (entry.Kind != CrossbowTurretItemKind.Crossbow) continue;
                if (++weapons != 1 || entry.Stack != 1) return Fail("one_crossbow_required", out reason);
                if (!entry.Ready || string.IsNullOrEmpty(entry.AmmoType)) return Fail("weapon_not_ready", out reason);
                ammoType = entry.AmmoType;
            }
            if (weapons != 1) return Fail("one_crossbow_required", out reason);
            foreach (var entry in entries)
            {
                if (entry.Kind != CrossbowTurretItemKind.Bolt) continue;
                if (!entry.Ready || !string.Equals(entry.AmmoType, ammoType, StringComparison.Ordinal))
                    return Fail("incompatible_bolt", out reason);
                // Native container slots/stacks bound the reserve; no arbitrary ten-bolt limit.
                if (entry.Stack > int.MaxValue - bolts) return Fail("bolt_count_overflow", out reason);
                bolts += entry.Stack;
            }
            return true; // Empty magazine is a valid, non-firing turret.
        }
        internal static float AimTolerance(float quaternionDot)
        {
            // Native Turret uses Quaternion.Dot, not degrees. Keep crossbow shots precise.
            if (float.IsNaN(quaternionDot) || float.IsInfinity(quaternionDot)) return .5f;
            double angle = 2d * Math.Acos(Math.Max(0f, Math.Min(1f, quaternionDot))) * 180d / Math.PI;
            return (float)Math.Max(.5d, Math.Min(5d, angle));
        }
        internal static float SearchYaw(float elapsed, float period, float halfArc)
            => (float)Math.Sin(elapsed * Math.PI * 2d / Math.Max(4f, period)) * Math.Max(0f, Math.Min(180f, halfArc));
        internal static bool TransitionAllowed(CrossbowTurretState from, CrossbowTurretState to)
            => (from == CrossbowTurretState.Supply || from == CrossbowTurretState.Return || from == CrossbowTurretState.Recovery)
                ? to == CrossbowTurretState.Frozen
                : from == CrossbowTurretState.Frozen
                    ? to == CrossbowTurretState.Active || to == CrossbowTurretState.Return || to == CrossbowTurretState.Recovery
                    : from == CrossbowTurretState.Active && (to == CrossbowTurretState.Return || to == CrossbowTurretState.Recovery);
        internal static bool LocksStock(CrossbowTurretState state)
            => state == CrossbowTurretState.Frozen || state == CrossbowTurretState.Active;
        private static bool Fail(string value, out string reason) { reason = value; return false; }
    }
}
