namespace ValheimMastery
{
    internal enum GoldReceiptDecision { Ignore, ClearSettled, Reject, ApplyOnce, ResendReceipt, StorageUnavailable }
    // Pure policy also used by the runtime handler. No result is created from an unrequested grant.
    internal static class GoldReceiptPolicy
    {
        internal static GoldReceiptDecision Decide(int reply, int savedPhase, bool exactContract)
        {
            if (!exactContract || savedPhase < 1 || savedPhase > 3) return GoldReceiptDecision.Ignore;
            if (reply == 4) return GoldReceiptDecision.StorageUnavailable;
            if (reply == 2) return GoldReceiptDecision.ClearSettled;
            if (reply != 0 && reply != 1) return GoldReceiptDecision.Ignore;
            if (savedPhase != 1) return GoldReceiptDecision.ResendReceipt;
            return reply == 0 ? GoldReceiptDecision.Reject : GoldReceiptDecision.ApplyOnce;
        }
    }
}
