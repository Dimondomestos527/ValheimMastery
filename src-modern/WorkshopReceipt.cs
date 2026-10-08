using System;

namespace ValheimMastery
{
    internal enum WorkshopReceiptPhase { Requested, Uncertain, Committed, Refunded }
    internal static class WorkshopReceipt
    {
        internal static string Encode(string id, WorkshopReceiptPhase phase)
        {
            if (!Guid.TryParseExact(id, "N", out _) || (int)phase < 0 || (int)phase > 3)
                throw new ArgumentException("Invalid workshop receipt");
            return id + "|" + (int)phase;
        }
        internal static bool TryRead(string text, out string id, out WorkshopReceiptPhase phase)
        {
            id = null; phase = WorkshopReceiptPhase.Uncertain;
            if (text == null || text.Length != 34 || text[32] != '|' || text[33] < '0' || text[33] > '3') return false;
            string candidate = text.Substring(0, 32);
            if (!Guid.TryParseExact(candidate, "N", out _)) return false;
            id = candidate; phase = (WorkshopReceiptPhase)(text[33] - '0'); return true;
        }
        internal static bool TryDecision(WorkshopReceiptPhase phase, out bool commit)
        {
            commit = phase == WorkshopReceiptPhase.Committed;
            // A persisted Requested receipt can be an old character save made
            // BEFORE execution. It is not evidence that output was never made.
            return phase == WorkshopReceiptPhase.Committed || phase == WorkshopReceiptPhase.Refunded;
        }
    }
}
