using System;
using System.IO;
using ValheimMastery;

internal static class Program
{
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "gold-ledger-qc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            PersistenceAndBounds(Path.Combine(root, "state.bin"));
            ReservationSafety(Path.Combine(root, "reserve.bin"));
            SignedIdsAndDebugFavor(Path.Combine(root, "signed.bin"));
            BackgroundBudget(Path.Combine(root, "background.bin"));
            BackgroundOutOfOrder(Path.Combine(root, "background-order.bin"));
            FavorModel(Path.Combine(root, "model.bin"));
            FavorModelSourceEviction();
            VersionOneMigration(Path.Combine(root, "v1.bin"));
            VersionTwoMigration(Path.Combine(root, "v2.bin"));
            CorruptionFailsClosed(Path.Combine(root, "corrupt.bin"));
            WriteFailureFailsClosed(Path.Combine(root, "blocked"));
            CommitWriteFailure(Path.Combine(root, "commit-blocked.bin"));
            CooldownTiers(root);
            PatronExhaustionPolicy();
            PatronChecks.Run(root);
            Console.WriteLine("GoldCraftingLedger QC passed.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
        finally { Directory.Delete(root, true); }
    }

    private static void PersistenceAndBounds(string path)
    {
        var ledger = NewLedger(path);
        Check(ledger.Unlock(123), "first unlock");
        Check(ledger.Gain(123, 1200f), "gain accepted and capped");
        Check(ledger.Get(123).Favor == 1000f, "favor cap");
        Check(!ledger.Gain(123, float.NaN), "non-finite gain rejected");
        Check(ledger.Flush(), "flush dirty favor");
        Check(ledger.Get(123).Favor == 1000f, "flush keeps state");
        Check(ledger.Reserve(123, Grant(1, 600f, 500d), 100d), "reserve heavy-cost action");
        Check(ledger.Commit(123, Token(1), 999999d), "late commit settles");
        Check(ledger.Get(123).Favor == 400f, "exact debit");
        Check(ledger.Get(123).ExhaustionRemaining == 1200d, "commit cooldown");
        Check(ledger.Commit(123, Token(1), 999999d), "duplicate commit idempotent");
        Check(ledger.Get(123).Favor == 400f, "duplicate does not charge");
        Check(ledger.Elapse(123, 300d), "online elapsed time");
        Check(ledger.Get(123).ExhaustionRemaining == 900d, "cooldown decremented");
        Check(ledger.Flush(), "flush cooldown");
        var loaded = NewLedger(path);
        Check(loaded.Get(123).Favor == 400f && loaded.Get(123).ExhaustionRemaining == 900d, "reload persisted state");
        Check(!loaded.Reserve(123, Grant(2, 1f, 10000d), 1000d), "cooldown gates reserve");
    }

    private static void ReservationSafety(string path)
    {
        var ledger = NewLedger(path);
        Check(ledger.Unlock(1), "unlock reservation player");
        Check(ledger.Gain(1, 50f), "favor before reserve");
        Check(ledger.Flush(), "save favor before reserve");
        Check(!ledger.Reserve(1, Grant(2, 1f, 10d), 10d), "expired before admission rejected");
        Check(ledger.Reserve(1, Grant(3, 5f, 20d), 10d), "valid reservation accepted");
        Check(!ledger.Reserve(1, Grant(4, 5f, 100d), 11d), "duplicate pending reservation rejected");
        var reloaded = NewLedger(path);
        Check(reloaded.Get(1).Pending != null, "pending persisted");
        Check(reloaded.Commit(1, Token(3), 1000000d), "admitted request commit not auto-expired");
        Check(reloaded.Get(1).Favor == 45f, "late commit charged exactly once");
        Check(reloaded.Reject(1, Token(99)) == false, "unknown reject ignored");
        Check(reloaded.Get(1).ExhaustionRemaining == 0d, "cheap commit has no cooldown");
        Check(reloaded.Reserve(1, Grant(5, 3f, 1000001d), 1000000d), "zero cooldown allows fresh next reservation");
        Check(reloaded.Commit(1, Token(3), 1000000d) && reloaded.Get(1).Pending.GuidToken == Token(5) &&
            reloaded.Get(1).Favor == 45f, "old commit replay cannot settle or charge new pending action");
        Check(reloaded.Reject(1, Token(5)), "new pending can be rejected independently");
    }

    private static void CorruptionFailsClosed(string path)
    {
        var ledger = NewLedger(path);
        Check(ledger.Unlock(8), "create persisted store");
        byte[] bytes = File.ReadAllBytes(path);
        bytes[bytes.Length - 1] ^= 0x5A;
        File.WriteAllBytes(path, bytes);
        var corrupt = new GoldCraftingLedger(path);
        Check(!corrupt.Load() && !corrupt.IsAvailable, "corrupt checksum fails closed");
        Check(!corrupt.Unlock(9), "corrupt store cannot mutate");
    }

    private static void BackgroundBudget(string path)
    {
        var ledger = NewLedger(path);
        Check(ledger.Unlock(77), "unlock background player");
        Check(ledger.GainBackground(77, 100f, 10000d, "station-A", 1) == 100f, "background receipt 1");
        Check(ledger.GainBackground(77, 100f, 10100d, "station-A", 2) == 100f, "background receipt 2");
        Check(ledger.GainBackground(77, 100f, 10200d, "station-A", 3) == 100f, "background receipt 3");
        Check(ledger.GainBackground(77, 50f, 10300d, "station-A", 4) == 0f, "300 per rolling hour cap");
        Check(ledger.GainBackground(77, 50f, 10301d, "station-A", 4) == 0f, "sequence replay rejected");
        Check(ledger.HasBackgroundReceipt(77, "station-A", 4), "zero-award cap receipt still settles");
        var loaded = NewLedger(path);
        Check(loaded.GainBackground(77, 50f, 10302d, "station-A", 5) == 0f, "hourly budget survives restart");
        float resetGain = loaded.GainBackground(77, 50f, 13700d, "station-A", 6);
        Check(resetGain == 50f, "budget resets after hour (got " + resetGain + ", state=" + loaded.IsAvailable + ")");
        Check(loaded.Get(77).Favor == 350f, "background Favor credited once");
    }

    private static void BackgroundOutOfOrder(string path)
    {
        var ledger = NewLedger(path);
        Check(ledger.Unlock(78), "unlock out-of-order player");
        Check(ledger.GainBackground(78, 1f, 20000d, "station-B", 2) == 1f, "out-of-order seq2 first");
        Check(ledger.GainBackground(78, 1f, 20001d, "station-B", 1) == 1f, "late seq1 accepted within bitmap");
        Check(ledger.HasBackgroundReceipt(78, "station-B", 1) && ledger.HasBackgroundReceipt(78, "station-B", 2), "both sequence bits set");
        Check(ledger.GainBackground(78, 1f, 20002d, "station-B", 1) == 0f, "out-of-order duplicate rejected");
        var loaded = NewLedger(path);
        Check(loaded.HasBackgroundReceipt(78, "station-B", 1) && loaded.HasBackgroundReceipt(78, "station-B", 2), "bitmap survives reload");
        Check(loaded.GainBackground(78, 1f, 20003d, "station-B", 66) == 1f, "large forward shift accepted");
        Check(!loaded.HasBackgroundReceipt(78, "station-B", 1) && loaded.IsBackgroundReceiptExpired(78, "station-B", 1), "older-than-window receipt expires");
        Check(loaded.GainBackground(78, 1f, 20004d, "station-B", 1) == 0f, "expired late sequence fails closed");
    }

    private static void FavorModel(string path)
    {
        var model = new GoldFavorModel();
        var first = model.Observe(1, "craft.Item.q1", 10f, 1000d);
        var second = model.Observe(1, "craft.Item.q1", 10f, 1001d);
        var third = model.Observe(1, "craft.Item.q1", 10f, 1002d);
        var fourth = model.Observe(1, "craft.Item.q1", 10f, 1003d);
        Check(first.FavorGain == 1.25f && second.FavorGain == 1.25f && third.FavorGain == 1.25f, "first three actions use x4 charge time");
        Check(fourth.Diminishing == .5f && fourth.FavorGain == .625f, "rolling repetition diminishing then charge multiplier");
        Check(!model.Observe(1, "craft.Item.q1", float.PositiveInfinity, 1004d).Accepted, "model rejects non-finite XP");
        var capped = new GoldFavorModel(10f).Observe(2, "build.Piece", 100f, 1000d);
        Check(capped.FavorGain == 25f, "per-event cap then x4 charge-time reduction");
        Check(new GoldFavorModel(10f, 1f).Observe(3, "build.Piece", 100f, 1000d).FavorGain == 100f,
            "baseline high-XP event cap remains 100");
        FavorChargeMultiplierIsProportional();
    }

    private static void FavorModelSourceEviction()
    {
        var model = new GoldFavorModel();
        for (int i = 0; i < 12; i++) Check(model.Observe(3, "hot.recipe", 10f, 3000d + i).Accepted, "hot source warmup");
        for (int i = 0; i < 70; i++)
        {
            Check(model.Observe(3, "unique." + i, 1f, 3020d + i).Accepted, "new source accepted beyond 64-key lifetime cap");
            Check(model.Observe(3, "hot.recipe", 10f, 3020d + i + .1d).Accepted, "active source retained while LRU evicts");
        }
        var repeated = model.Observe(3, "hot.recipe", 10f, 3100d);
        Check(repeated.Accepted && repeated.Diminishing == .2f && repeated.FavorGain == .25f,
            "active identical source keeps rolling diminishing after LRU eviction");
    }

    private static void FavorChargeMultiplierIsProportional()
    {
        var baseline = new GoldFavorModel(.5f, 1f);
        var slower = new GoldFavorModel(.5f, 4f);
        var baseCraft = baseline.Observe(4, "craft.Item.q1", 12f, 4000d);
        var slowCraft = slower.Observe(4, "craft.Item.q1", 12f, 4000d);
        var baseProcessing = baseline.Observe(4, "processing.CopperOre", 2.5f, 4001d);
        var slowProcessing = slower.Observe(4, "processing.CopperOre", 2.5f, 4001d);
        Check(slowCraft.FavorGain == baseCraft.FavorGain / 4f, "craft Favor exactly quartered");
        Check(slowProcessing.FavorGain == baseProcessing.FavorGain / 4f, "processing Favor exactly quartered");
        var described = new GoldFavorModel(.5f, 4f);
        described.Observe(4, "craft.Item.q1", 1f, 4000d);
        Check(described.Describe(4, 4002d).Contains("charge time x4"), "telemetry reports applied charge multiplier");
    }

    private static void VersionOneMigration(string path)
    {
        byte[] payload;
        using (var body = new MemoryStream())
        using (var w = new BinaryWriter(body))
        {
            w.Write(1); w.Write(-123456L); w.Write(true); w.Write(12.5f); w.Write(1200d);
            WriteString(w, string.Empty); w.Write(0); w.Write(false); w.Flush(); payload = body.ToArray();
        }
        using (var file = new MemoryStream())
        using (var w = new BinaryWriter(file))
        {
            w.Write(0x47434C31u); w.Write(1); w.Write(payload.Length); w.Write(payload); w.Write(Crc32(payload)); w.Flush();
            File.WriteAllBytes(path, file.ToArray());
        }
        var migrated = NewLedger(path);
        Check(migrated.Get(-123456L).Favor == 12.5f && migrated.Get(-123456L).BackgroundWindowStartUtc == 0d, "v1 payload migrated");
        Check(migrated.Get(-123456L).ExhaustionRemaining == 1200d, "legacy full20min timer retained");
        Check(migrated.SetFavor(-123456L, 13f), "migrated state saves as current format");
        var rewritten = NewLedger(path).Get(-123456L);
        Check(rewritten.Favor == 13f && rewritten.ExhaustionRemaining == 1200d, "migration rewrite retains legacy timer");
    }

    private static void VersionTwoMigration(string path)
    {
        byte[] payload;
        using (var body = new MemoryStream())
        using (var w = new BinaryWriter(body))
        {
            w.Write(1); w.Write(88L); w.Write(true); w.Write(5f); w.Write(0d);
            w.Write(10000d); w.Write(10f); w.Write(1); WriteString(w, "station-v2"); w.Write(5L);
            WriteString(w, string.Empty); w.Write(0); w.Write(false); w.Flush(); payload = body.ToArray();
        }
        using (var file = new MemoryStream())
        using (var w = new BinaryWriter(file))
        {
            w.Write(0x47434C31u); w.Write(2); w.Write(payload.Length); w.Write(payload); w.Write(Crc32(payload)); w.Flush();
            File.WriteAllBytes(path, file.ToArray());
        }
        var migrated = NewLedger(path);
        Check(migrated.HasBackgroundReceipt(88, "station-v2", 5) && migrated.HasBackgroundReceipt(88, "station-v2", 4), "v2 high-water migrated as all-settled bitmap");
        Check(migrated.GainBackground(88, 1f, 10001d, "station-v2", 6) == 1f, "v2 high-water accepts next sequence");
        Check(NewLedger(path).HasBackgroundReceipt(88, "station-v2", 6), "v2 migration saved as v3");
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static void SignedIdsAndDebugFavor(string path)
    {
        const long negativeId = -7654321L;
        var ledger = NewLedger(path);
        Check(!ledger.Unlock(0), "zero player ID rejected");
        Check(ledger.Unlock(negativeId), "negative player ID accepted");
        Check(ledger.Gain(negativeId, 40f), "negative ID favor gain");
        Check(!ledger.SetFavor(negativeId, float.NaN), "debug favor rejects non-finite");
        Check(!ledger.SetFavor(negativeId, 1000.1f), "debug favor rejects above cap");
        Check(!ledger.SetFavor(negativeId, -0.1f), "debug favor rejects below zero");
        Check(ledger.SetFavor(negativeId, 25f), "debug favor sets bounded balance");
        Check(ledger.Get(negativeId).Favor == 25f, "debug favor applied");
        Check(ledger.Reserve(negativeId, Grant(6, 10f, 200d), 100d), "negative ID reserve");
        Check(!ledger.SetFavor(negativeId, 0f), "debug favor cannot alter funds while pending");
        Check(ledger.Get(negativeId).Favor == 25f, "pending balance protected");
        var reloaded = NewLedger(path);
        Check(reloaded.Get(negativeId) != null && reloaded.Get(negativeId).Favor == 25f, "negative ID and favor persist");
        Check(reloaded.Commit(negativeId, Token(6), 100000d), "negative ID commit");
        Check(reloaded.Get(negativeId).Favor == 15f, "pending charge honored after reload");
        Check(reloaded.Get(0) == null && !reloaded.Gain(0, 1f), "zero ID rejected by read and mutation APIs");
    }

    private static void WriteFailureFailsClosed(string path)
    {
        Directory.CreateDirectory(path); // Save cannot atomically move a file over this directory.
        var ledger = NewLedger(path);
        Check(!ledger.Unlock(10), "durable write failure reported");
        Check(!ledger.IsAvailable, "write failure disables authority");
        Check(ledger.Get(10) == null, "unavailable state is not exposed");
    }

    private static void CooldownTiers(string root)
    {
        float[] costs = { 0f, 249.99f, 250f, 250.01f, 499.99f, 500f, 500.01f, 1000f };
        double[] expected = { 0d, 0d, 0d, 300d, 300d, 300d, 1200d, 1200d };
        for (int i = 0; i < costs.Length; i++)
        {
            string path = Path.Combine(root, "tier-" + i + ".bin");
            var ledger = NewLedger(path);
            Check(ledger.Unlock(90) && ledger.Gain(90, 1000), "tier setup");
            var grant = Grant(100 + i, costs[i], 100);
            Check(ledger.Reserve(90, grant, 1) && ledger.Commit(90, grant.GuidToken, 2), "tier commit");
            Check(ledger.Get(90).Favor == 1000f - costs[i] && ledger.Get(90).ExhaustionRemaining == expected[i],
                "exact cost/cooldown tier " + costs[i]);
            ledger = NewLedger(path);
            Check(ledger.Get(90).ExhaustionRemaining == expected[i], "tier persists on reload");
            Check(ledger.SetFavor(90, 1000), "replenish test balance to separate timer from funds");
            if (expected[i] > 0)
            {
                Check(!ledger.Reserve(90, Grant(200 + i, 1f, 100), 3), "same-patron cheap action blocked by active timer");
                Check(ledger.Elapse(90, 10) && ledger.Flush(), "tier elapsed saved");
                ledger = NewLedger(path);
                Check(ledger.Commit(90, grant.GuidToken, 4) && ledger.Get(90).ExhaustionRemaining == expected[i] - 10,
                    "replayed commit cannot restart timer");
            }
            else
            {
                Check(ledger.Reserve(90, Grant(200 + i, 1f, 100), 3), "fresh action permitted without cooldown");
            }
        }
        Console.WriteLine("PASS cost tiers 0/249.99/250/250.01/499.99/500/500.01/1000; reload/replay/same-patron blocking.");
    }

    private static void CommitWriteFailure(string path)
    {
        var ledger = NewLedger(path);
        Check(ledger.Unlock(95) && ledger.Gain(95, 1000), "commit failure setup");
        var grant = Grant(300, 500, 100);
        Check(ledger.Reserve(95, grant, 1), "durable pending before failed commit");
        Directory.CreateDirectory(path + ".tmp"); // Block only this disposable test's temporary write.
        Check(!ledger.Commit(95, grant.GuidToken, 2) && !ledger.IsAvailable && ledger.Get(95) == null,
            "failed commit exposes no authoritative debit or timer");
        var recovered = NewLedger(path);
        Check(recovered.Get(95).Favor == 1000 && recovered.Get(95).ExhaustionRemaining == 0 &&
            recovered.Get(95).Pending.GuidToken == grant.GuidToken, "durable precommit state survives failed save");
        Directory.Delete(path + ".tmp");
        Check(recovered.Commit(95, grant.GuidToken, 3) && recovered.Get(95).Favor == 500 &&
            recovered.Get(95).ExhaustionRemaining == 300, "retry after storage repair settles correct tier once");
    }

    private static void PatronExhaustionPolicy()
    {
        const string a = "Volundr", b = "Tyr";
        foreach (double remaining in new[] { 300d, 1200d })
        {
            Check(!GoldCooldownPolicy.CanStart(a, 0, a, remaining) && !GoldCooldownPolicy.CanStart(a, 250, a, remaining),
                "same-patron exhaustion blocks all costs");
            Check(GoldCooldownPolicy.CanStart(b, 0, a, remaining) && GoldCooldownPolicy.CanStart(b, 250, a, remaining),
                "other-patron free/250 action exempt");
            Check(!GoldCooldownPolicy.CanStart(b, 250.01f, a, remaining) && !GoldCooldownPolicy.CanStart(b, 500, a, remaining),
                "other-patron cost above250 still blocked");
            Check(GoldCooldownPolicy.RemainingAfterCommit(250, remaining) == remaining,
                "permitted other-patron cheap action preserves timer");
        }
        Check(GoldCooldownPolicy.CanStart(a, 500, null, 0) && GoldCooldownPolicy.CanStart(b, 1000, null, 0),
            "expired timer permits both patrons");
        Check(!GoldCooldownPolicy.CanStart(b, 250, null, 1200), "unknown exhaustion origin fails closed");
        Check(!GoldCooldownPolicy.CanStart(b, float.NaN, a, 0) && !GoldCooldownPolicy.CanStart(b, -1, a, 0) &&
            !GoldCooldownPolicy.CanStart(b, float.PositiveInfinity, a, 0) &&
            !GoldCooldownPolicy.CanStart(null, 250, a, 300) && !GoldCooldownPolicy.CanStart(b, 250, a, double.NaN),
            "invalid cost/patron/time cannot bypass exhaustion");
        bool threw = false;
        try { GoldCooldownPolicy.ForFavorCost(float.NaN); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "invalid duration cost rejected");
        Console.WriteLine("PASS patron policy: own blocks all; others <=250 exempt; timer preserved. Policy model only, no second patron ledger.");
    }

    private static GoldCraftingLedger NewLedger(string path)
    {
        var ledger = new GoldCraftingLedger(path);
        Check(ledger.Load(), "ledger load");
        return ledger;
    }

    private static GoldCraftingLedger.PendingGrant Grant(int seed, float cost, double expires)
    {
        return new GoldCraftingLedger.PendingGrant { GuidToken = Token(seed), Kind = 1, Prefab = "qc_item", OldQuality = 1, NewQuality = 1, Variant = 0, Cost = cost, StationString = null, ExpiresAt = expires };
    }

    private static string Token(int n) { return new Guid(n, 0, 0, new byte[8]).ToString("N"); }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("Failed: " + label); }
}
