using ValheimMastery;

internal static class WorkshopAtomicChecks
{
    private sealed class Store : IWorkshopDebitStore
    {
        public object Identity { get; } = new object();
        internal Dictionary<string, int> Stock = new();
        internal bool FailRemove, FailCapture, FailRestore, FailFlush, ShortRemove;
        internal Action Removing;
        public int Count(string item) => Stock.GetValueOrDefault(item);
        public object Capture() => FailCapture ? throw new Exception("capture") : new Dictionary<string, int>(Stock);
        public void Remove(string item, int amount)
        {
            Removing?.Invoke();
            if (FailRemove) throw new Exception("remove");
            Stock[item] -= ShortRemove ? amount - 1 : amount;
        }
        public void Restore(object snapshot)
        {
            if (FailRestore) throw new Exception("restore");
            Stock = new Dictionary<string, int>((Dictionary<string, int>)snapshot);
        }
        public void Flush()
        {
            if (FailFlush) { FailFlush = false; throw new Exception("flush"); }
        }
    }
    private static Store New(int count = 20) => new() { Stock = new() { ["iron"] = count } };
    private static WorkshopDebitLine Line(Store s, int amount) => new() { Store = s, Item = "iron", Amount = amount };
    private static void Check(bool okay, string name) { if (!okay) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected failure"); }
    internal static int Run()
    {
        string receiptId = Guid.NewGuid().ToString("N");
        string history = WorkshopSettlementHistory.Append("", 1, receiptId, true);
        Check(WorkshopSettlementHistory.TryFind(history, 1, receiptId, out bool historicalCommit) && historicalCommit,
            "older settlement found independently of latest chest marker");
        Check(WorkshopSettlementHistory.Append(history, 1, receiptId, true) == history, "settlement history idempotent");
        Throws(() => WorkshopSettlementHistory.Append(history, 1, receiptId, false));
        Check(!WorkshopSettlementHistory.TryFind(history, 2, receiptId, out _), "history cannot acknowledge another character");
        for (int i = 0; i < 128; i++) history = WorkshopSettlementHistory.Append(history, 1, Guid.NewGuid().ToString("N"), false);
        Check(history.Split('\n').Length == 128 && !WorkshopSettlementHistory.TryFind(history, 1, receiptId, out _),
            "history is bounded; evicted identity remains unresolved rather than fabricated");
        string journalDir = Path.Combine(Path.GetTempPath(), "vm-workshop-qc-" + receiptId);
        Directory.CreateDirectory(journalDir);
        string journalPath = Path.Combine(journalDir, "requests.log");
        try
        {
            var journal = new WorkshopDurableRequests(journalPath);
            Check(journal.Admit(1, receiptId), "durable request admitted before debit");
            Check(!journal.Admit(1, receiptId), "same-process durable duplicate rejected");
            Check(!new WorkshopDurableRequests(journalPath).Admit(1, receiptId), "restart cannot replay consumed request");
            Check(!new WorkshopDurableRequests(journalPath).Admit(2, receiptId), "request cannot be replayed by another character");
            Check(journal.Admit(1, Guid.NewGuid().ToString("N")), "new request allowed after earlier settlement");
            Check(!journal.Admit(0, Guid.NewGuid().ToString("N")) && !journal.Admit(1, "bad"), "invalid durable identity rejected");
            string broken = Path.Combine(journalDir, "broken.log");
            File.WriteAllText(broken, "1|" + receiptId); // Deliberately interrupted append.
            var corrupted = new WorkshopDurableRequests(broken);
            Throws(() => corrupted.Admit(1, Guid.NewGuid().ToString("N")));
            File.WriteAllText(broken, "");
            Throws(() => corrupted.Admit(1, Guid.NewGuid().ToString("N")));
            Check(true, "truncated journal fails closed and cannot silently reopen in-process");
        }
        finally
        {
            // Only the two explicitly named, GUID-isolated test files are removed.
            File.Delete(journalPath); File.Delete(Path.Combine(journalDir, "broken.log")); Directory.Delete(journalDir);
        }
        byte[] inventory = { 1, 2, 3 };
        Check(WorkshopLeaseRules.Evaluate(10, 20, 10, false, true, inventory, inventory) == WorkshopLeaseReadiness.Ready,
            "handoff requires replicated owner and exact acknowledged stock");
        Check(WorkshopLeaseRules.Evaluate(20, 20, 10, false, true, inventory, inventory) == WorkshopLeaseReadiness.Waiting,
            "matching bytes alone cannot authorize old-owner inventory");
        Check(WorkshopLeaseRules.Evaluate(10, 20, 10, false, true, inventory, null) == WorkshopLeaseReadiness.Waiting,
            "ownership alone cannot authorize missing owner acknowledgment");
        Check(WorkshopLeaseRules.Evaluate(10, 20, 10, false, true, new byte[] { 1, 2, 4 }, inventory) == WorkshopLeaseReadiness.Waiting,
            "out-of-order snapshot waits instead of overwriting world bytes");
        Check(WorkshopLeaseRules.Evaluate(30, 20, 10, false, true, inventory, inventory) == WorkshopLeaseReadiness.Abort,
            "third-party ownership transfer aborts handoff");
        Check(WorkshopLeaseRules.Evaluate(10, 20, 10, true, true, inventory, inventory) == WorkshopLeaseReadiness.Abort,
            "open inventory aborts handoff");
        Check(WorkshopLeaseRules.Evaluate(10, 20, 10, false, false, inventory, inventory) == WorkshopLeaseReadiness.Abort,
            "disconnected owner cannot authorize debit");
        Check(WorkshopLeaseRules.Evaluate(10, 20, 10, false, true, inventory, Array.Empty<byte>()) == WorkshopLeaseReadiness.Abort,
            "empty acknowledged snapshot is invalid");
        Check(WorkshopLeaseRules.Evaluate(10, 20, 10, false, true, inventory, new byte[262145]) == WorkshopLeaseReadiness.Abort,
            "oversized snapshot rejected");
        Check(WorkshopLeaseRules.Evaluate(10, 20, 0, false, true, inventory, inventory) == WorkshopLeaseReadiness.Abort,
            "missing server identity rejected");
        var ledger = new WorkshopRequestLedger(2, 3);
        Check(ledger.Remember(1, receiptId) == WorkshopRequestAdmission.Accepted, "first character request admitted");
        Check(ledger.Remember(1, receiptId) == WorkshopRequestAdmission.Duplicate, "same character reconnect cannot replay consumed ID");
        Check(ledger.Remember(2, receiptId) == WorkshopRequestAdmission.Accepted, "different characters have independent request identity");
        Check(ledger.Remember(1, Guid.NewGuid().ToString("N")) == WorkshopRequestAdmission.Accepted, "second unique request admitted");
        Check(ledger.Remember(1, Guid.NewGuid().ToString("N")) == WorkshopRequestAdmission.Capacity, "per-character bounded without eviction");
        Check(ledger.Remember(3, Guid.NewGuid().ToString("N")) == WorkshopRequestAdmission.Capacity, "global session bounded without eviction");
        Check(ledger.Remember(1, receiptId) == WorkshopRequestAdmission.Duplicate, "old replay remains rejected at capacity");
        Check(ledger.Remember(0, receiptId) == WorkshopRequestAdmission.Invalid && ledger.Remember(1, "bad") == WorkshopRequestAdmission.Invalid,
            "invalid request identity rejected");
        ledger.Clear();
        Check(ledger.Remember(1, receiptId) == WorkshopRequestAdmission.Accepted, "explicit new world session clears in-memory ledger");
        foreach (WorkshopReceiptPhase phase in Enum.GetValues<WorkshopReceiptPhase>())
        {
            Check(WorkshopReceipt.TryRead(WorkshopReceipt.Encode(receiptId, phase), out string id, out var read) &&
                id == receiptId && read == phase, "receipt roundtrip " + phase);
            bool decidable = WorkshopReceipt.TryDecision(phase, out bool committed);
            Check(decidable == (phase == WorkshopReceiptPhase.Committed || phase == WorkshopReceiptPhase.Refunded) && committed == (phase == WorkshopReceiptPhase.Committed),
                "recovery decision " + phase);
        }
        foreach (string invalid in new[] { "", receiptId + "|4", receiptId + "|-1", "x" + receiptId + "|0", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz|0" })
            Check(!WorkshopReceipt.TryRead(invalid, out _, out _), "malformed receipt rejected");
        Check(!WorkshopReceipt.TryDecision((WorkshopReceiptPhase)42, out _), "unknown recovery phase cannot refund");
        var a = New(); var b = New();
        var tx = WorkshopAtomicDebit.Begin(new[] { Line(a, 4), Line(b, 7) }, () => true);
        Check(tx != null && a.Count("iron") == 16 && b.Count("iron") == 13, "exact multi-chest debit");
        Check(WorkshopAtomicDebit.Begin(new[] { Line(a, 1) }, () => true) == null, "second action cannot spend locked chest");
        tx.Commit(); tx.Commit(); tx.Dispose();
        Check(a.Count("iron") == 16 && !WorkshopAtomicDebit.IsLocked(a.Identity), "commit is idempotent and never refunds");
        tx = WorkshopAtomicDebit.Begin(new[] { Line(a, 3), Line(b, 2) }, () => true);
        tx.Dispose(); tx.Dispose();
        Check(a.Count("iron") == 16 && b.Count("iron") == 13, "cancel restores all stores exactly once");
        Check(WorkshopAtomicDebit.Begin(new[] { Line(a, 10), Line(a, 10) }, () => true) == null && a.Count("iron") == 16,
            "duplicate requirements cannot reuse stock");
        tx = WorkshopAtomicDebit.Begin(new[] { Line(a, 3), Line(a, 4) }, () => true);
        Check(a.Count("iron") == 9, "duplicate requirements aggregate exact debit"); tx.Dispose();
        Check(WorkshopAtomicDebit.Begin(new[] { Line(a, 3) }, () => false) == null && !WorkshopAtomicDebit.IsLocked(a.Identity),
            "authorization/topology rejection releases without removal");
        b.FailCapture = true;
        Throws(() => WorkshopAtomicDebit.Begin(new[] { Line(a, 4), Line(b, 4) }, () => true)); b.FailCapture = false;
        Check(a.Count("iron") == 16 && b.Count("iron") == 13 && !WorkshopAtomicDebit.IsLocked(a.Identity), "capture failure before any write");
        b.FailRemove = true;
        Throws(() => WorkshopAtomicDebit.Begin(new[] { Line(a, 4), Line(b, 4) }, () => true)); b.FailRemove = false;
        Check(a.Count("iron") == 16 && b.Count("iron") == 13, "second chest failure rolls back first chest");
        b.ShortRemove = true;
        Throws(() => WorkshopAtomicDebit.Begin(new[] { Line(a, 4), Line(b, 4) }, () => true)); b.ShortRemove = false;
        Check(a.Count("iron") == 16 && b.Count("iron") == 13, "inexact removal detected and reversed");
        b.FailFlush = true;
        Throws(() => WorkshopAtomicDebit.Begin(new[] { Line(a, 4), Line(b, 4) }, () => true));
        Check(a.Count("iron") == 16 && b.Count("iron") == 13, "persistence failure reverses both debits");
        a.Removing = () => Check(WorkshopAtomicDebit.Begin(new[] { Line(b, 1) }, () => true) == null,
            "all stores locked before first callback can reenter");
        tx = WorkshopAtomicDebit.Begin(new[] { Line(a, 1), Line(b, 1) }, () => true); a.Removing = null; tx.Dispose();
        Check(WorkshopAtomicDebit.Begin(new[] { Line(a, int.MaxValue), Line(a, 1) }, () => true) == null,
            "overflow rejected");
        Check(WorkshopAtomicDebit.Begin(new[] { Line(a, 0) }, () => true) == null &&
            WorkshopAtomicDebit.Begin(new[] { Line(a, -1) }, () => true) == null, "invalid debit rejected");
        tx = WorkshopAtomicDebit.Begin(new[] { Line(a, 2), Line(b, 2) }, () => true);
        b.FailRestore = true; Throws(tx.Dispose);
        Check(a.Count("iron") == 16 && !WorkshopAtomicDebit.IsLocked(a.Identity) && WorkshopAtomicDebit.IsLocked(b.Identity),
            "failed rollback quarantines only uncertain chest; other chests restored");
        Check(WorkshopAtomicDebit.Begin(new[] { Line(b, 1) }, () => true) == null, "quarantined stock cannot be spent");
        b.FailRestore = false; b.Stock["iron"] = 13; // Simulated externally verified recovery.
        WorkshopAtomicDebit.ReleaseRecovered(b.Identity);
        tx = WorkshopAtomicDebit.Begin(new[] { Line(b, 1) }, () => true);
        Check(tx != null && b.Count("iron") == 12, "verified recovery can release quarantined identity"); tx.Dispose();
        Console.WriteLine("Workshop atomic core: all managed tests passed. Unity adapters and remote multiplayer remain LIVE_TEST_REQUIRED.");
        return 0;
    }
}
