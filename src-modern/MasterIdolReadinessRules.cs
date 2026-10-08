namespace ValheimMastery
{
    // Transient dependency observations only; no payment or durable desired-state mutation.
    internal sealed class MasterIdolReadinessRules
    {
        private object Ledger;
        private bool Observed, JournalReady, SwitchReady, Deferred, Legacy;
        private float Next;
        internal bool Changed(object ledger, bool journalReady, bool switchReady, bool deferred, bool legacy)
        {
            bool changed = !Observed || !object.ReferenceEquals(Ledger, ledger) || JournalReady != journalReady ||
                SwitchReady != switchReady || Deferred != deferred || Legacy != legacy;
            Observed = true; Ledger = ledger; JournalReady = journalReady; SwitchReady = switchReady; Deferred = deferred; Legacy = legacy;
            return changed;
        }
        internal bool ProbeDue(float now)
        { if (now < Next) return false; Next = now + 2f; return true; }
    }
}
