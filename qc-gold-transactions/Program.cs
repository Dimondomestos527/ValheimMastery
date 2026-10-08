using System;
using System.IO;
using ValheimMastery;

class Program
{
    static void Check(bool yes, string label) { if (!yes) throw new Exception(label); }
    static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gold-receipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Check(GoldReceiptPolicy.Decide(1, 0, false) == GoldReceiptDecision.Ignore, "unsolicited grant ignored");
            Check(GoldReceiptPolicy.Decide(1, 1, false) == GoldReceiptDecision.Ignore, "changed contract ignored");
            Check(GoldReceiptPolicy.Decide(1, 1, true) == GoldReceiptDecision.ApplyOnce, "requested action applies");
            Check(GoldReceiptPolicy.Decide(1, 2, true) == GoldReceiptDecision.ResendReceipt, "already applied never reapplies");
            Check(GoldReceiptPolicy.Decide(0, 2, true) == GoldReceiptDecision.ResendReceipt, "late denial cannot delete applied evidence");
            Check(GoldReceiptPolicy.Decide(1, 3, true) == GoldReceiptDecision.ResendReceipt, "rejected action never reapplies");
            Check(GoldReceiptPolicy.Decide(2, 2, true) == GoldReceiptDecision.ClearSettled, "settled ACK clears journal");
            Check(GoldReceiptPolicy.Decide(1, 0, true) == GoldReceiptDecision.Ignore, "grant after cleared journal ignored");
            Check(GoldReceiptPolicy.Decide(4, 1, true) == GoldReceiptDecision.StorageUnavailable, "storage fault preserves requested evidence");
            Check(GoldReceiptPolicy.Decide(99, 1, true) == GoldReceiptDecision.Ignore, "unknown operation ignored");
            Recover(Path.Combine(dir, "masterwork.bin"), 1, 500, 5);
            Recover(Path.Combine(dir, "forge.bin"), 2, 250, 7);
            Recover(Path.Combine(dir, "heavy.bin"), 1, 750, 5);
            Reject(Path.Combine(dir, "rejected.bin"));
            Console.WriteLine("Gold receipt policy / ledger recovery QC PASS (managed state only, not Unity inventory or live network).");
        }
        finally { Directory.Delete(dir, true); }
    }
    static GoldCraftingLedger Load(string path)
    { var l = new GoldCraftingLedger(path); Check(l.Load(), "load"); return l; }
    static GoldCraftingLedger.PendingGrant Grant(int kind, float cost, int quality) =>
        new GoldCraftingLedger.PendingGrant { GuidToken = Guid.NewGuid().ToString("N"), Kind = kind, Prefab = "SwordIron",
            OldQuality = kind == 1 ? 0 : 4, NewQuality = quality, Variant = 1, Cost = cost, StationString = "test-contract", ExpiresAt = 100 };
    static void Recover(string path, int kind, float cost, int quality)
    {
        var l = Load(path); Check(l.Unlock(-57), "signed player unlock"); Check(l.Gain(-57, 1000), "favor");
        var grant = Grant(kind, cost, quality);
        Check(l.Reserve(-57, grant, 1), "reserve before item effect");
        l = Load(path); Check(l.Get(-57).Favor == 1000, "restart before apply does not charge");
        int durablePhase = 1, durableItems = 0;
        Check(GoldReceiptPolicy.Decide(1, durablePhase, true) == GoldReceiptDecision.ApplyOnce, "crash before owner save resumes requested");
        // Model the native atomic profile save: inventory and receipt become durable together.
        durableItems = 1; durablePhase = 2;
        Check(GoldReceiptPolicy.Decide(1, durablePhase, true) == GoldReceiptDecision.ResendReceipt, "crash after owner save does not create twice");
        Check(l.Commit(-57, grant.GuidToken, 9999), "late acknowledgment commits");
        l = Load(path); Check(l.Commit(-57, grant.GuidToken, 10000), "server crash after commit duplicate ACK");
        double expectedCooldown = cost <= 250f ? 0d : cost <= 500f ? 300d : 1200d;
        Check(l.Get(-57).Favor == 1000 - cost && l.Get(-57).ExhaustionRemaining == expectedCooldown, "one debit / exact tier cooldown");
        Check(durableItems == 1 && l.Get(-57).Pending == null, "one result / cleared server pending");
        Check(l.Get(-57).SettledTokens.Contains(grant.GuidToken), "durable replay receipt");
    }
    static void Reject(string path)
    {
        var l = Load(path); l.Unlock(91); l.Gain(91, 1000); var g = Grant(1, 500, 5);
        Check(l.Reserve(91, g, 1), "reject reserve");
        Check(GoldReceiptPolicy.Decide(0, 1, true) == GoldReceiptDecision.Reject, "validation failure rejects");
        Check(l.Reject(91, g.GuidToken), "reject clears only matching request");
        l = Load(path); Check(l.Get(91).Favor == 1000 && l.Get(91).ExhaustionRemaining == 0, "failure has no cost or cooldown");
    }
}
