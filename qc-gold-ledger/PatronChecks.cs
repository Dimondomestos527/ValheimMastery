using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ValheimMastery;

internal static class PatronChecks
{
    private static int assertions;
    private static void Check(bool ok, string label) { assertions++; if (!ok) throw new Exception("Patron QC: " + label); }
    private static GoldCraftingLedger Load(string path) { var l = new GoldCraftingLedger(path); Check(l.Load(), "load " + Path.GetFileName(path)); return l; }
    private static string Token(int seed) => new Guid(seed, 0, 0, new byte[8]).ToString("N");
    private static GoldPatronGrant G(long generation, string patron, float cost, bool prepared = false) =>
        new GoldPatronGrant { Generation = generation, Patron = patron, Cost = cost, Action = "Action" + generation, Token = Token((int)generation), Prepared = prepared };
    private static GoldCraftingLedger.PendingGrant Legacy(int seed, float cost) => new GoldCraftingLedger.PendingGrant {
        GuidToken = Token(seed), Kind = 2, Prefab = "SwordIron", OldQuality = 4, NewQuality = 7, Variant = 0,
        Cost = cost, StationString = "unchanged-contract", ExpiresAt = 100 };
    private sealed class ModelEvidence : IGoldPreparedEvidence
    {
        private readonly bool verified;
        internal ModelEvidence(bool value) { verified = value; }
        public bool Verify(long id, GoldPatronGrant frozen, GoldPatronOutcome outcome) => verified;
    }
    private sealed class ReentrantEvidence : IGoldPreparedEvidence
    {
        internal GoldCraftingLedger Ledger;
        public bool Verify(long id, GoldPatronGrant frozen, GoldPatronOutcome outcome)
        {
            Check(!Ledger.SettleAction(id, frozen, outcome, new ModelEvidence(true)), "nested settlement denied");
            Check(!Ledger.Gain(id, frozen.Patron, 10) && !Ledger.Unlock(99) && !Ledger.Load(), "proof callback cannot mutate or detach authority");
            return true;
        }
    }

    internal static void Run(string root)
    {
        Pools(Path.Combine(root, "patron-pools.bin"), Path.Combine(root, "another-world.bin"));
        Holds(Path.Combine(root, "patron-hold.bin"));
        RevokeAndGeneration(Path.Combine(root, "patron-revoke.bin"));
        LegacyConcurrency(Path.Combine(root, "patron-concurrent.bin"));
        TokenNamespaceAndReentrancy(Path.Combine(root, "patron-namespace.bin"));
        Failure(Path.Combine(root, "patron-failure.bin"));
        AtomicAwards(Path.Combine(root, "patron-awards.bin"));
        MigrationAndInvalidGraphs(root);
        Console.WriteLine("PASS patron wallets/schema/coordinator: " + assertions + " assertions; prepared evidence is a model stub, no native/RPC proof.");
    }

    private static void Pools(string path, string otherWorld)
    {
        var l = Load(path);
        Check(l.Unlock(1) && l.Unlock(1, "Tyr") && l.Unlock(1, "Odin"), "three distinct unlocks");
        Check(!l.Unlock(1, "Tyr") && !l.Unlock(1, "../Tyr") && !l.Unlock(0, "Tyr"), "unlock boundaries");
        Check(l.Gain(1, "Tyr", 100) && l.Gain(1, "Tyr", 60), "two skills route to the same wallet");
        Check(l.GetWallet(1, "Tyr").Favor == 160 && l.Get(1).Favor == 0 && l.GetWallet(1, "Odin").Favor == 0, "single shared pool, no mirrored balances");
        var g = G(1, "Tyr", 40);
        Check(l.ReserveAction(1, g) && l.GetWallet(1, "Tyr").Available == 120, "reserve from patron pool");
        Check(l.ReserveAction(1, g.Copy()), "exact reserve retry returns existing grant");
        var changed = g.Copy(); changed.Action = "OtherAction";
        Check(!l.ReserveAction(1, changed) && !l.SettleAction(1, changed, GoldPatronOutcome.Committed), "changed identity rejected");
        Check(l.SettleAction(1, g, GoldPatronOutcome.Committed), "patron debit");
        Check(l.GetWallet(1, "Tyr").Favor == 120 && l.Get(1).Favor == 0, "correct wallet debited");
        Check(l.SettleAction(1, g, GoldPatronOutcome.Committed) && l.GetWallet(1, "Tyr").Favor == 120, "duplicate result exact once");
        Check(!l.ReserveAction(1, g), "terminal grant is not reissued");
        var snapshot = l.Get(1); snapshot.Wallets["Tyr"].Favor = 999; snapshot.Results["Tyr"].Grant.Cost = 900;
        Check(l.GetWallet(1, "Tyr").Favor == 120 && l.SettleAction(1, g, GoldPatronOutcome.Committed), "deep copies cannot mutate authority");
        Check(l.Unlock(2, "Tyr") && l.Gain(2, "Tyr", 90) && l.GetWallet(1, "Tyr").Favor == 120, "player isolation");
        Check(l.Flush(), "batched gains flushed before restart");
        Check(Load(otherWorld).GetWallet(1, "Tyr") == null, "world file isolation");
        l = Load(path); Check(l.GetWallet(1, "Tyr").Favor == 120 && l.GetWallet(2, "Tyr").Favor == 90, "wallets persist");
    }

    private static void Holds(string path)
    {
        var l = Load(path); l.Unlock(1); l.Unlock(1, "Tyr"); l.Unlock(1, "Odin"); l.Gain(1, 1000); l.Gain(1, "Tyr", 999); l.Gain(1, "Odin", 1000);
        Check(l.Gain(1, "Tyr", 1) && l.GetWallet(1, "Tyr").Favor == 1000, "999 to1000 per patron");
        var hold = G(1, "Tyr", 750, true);
        Check(l.ReserveAction(1, hold), "prepared accounting hold");
        Check(l.GetWallet(1, "Tyr").Favor == 1000 && l.GetWallet(1, "Tyr").Held == 750 && l.GetWallet(1, "Tyr").Available == 250, "hold inside cap");
        Check(l.Gain(1, "Tyr", 100) && l.GetWallet(1, "Tyr").Favor == 1000, "no hidden reserve bank");
        Check(!l.CanStart(1, "Tyr", 0) && !l.CanStart(1, "Tyr", 250), "same-patron cheap fenced before known consume");
        Check(l.CanStart(1, "Odin", 250) && !l.CanStart(1, "Odin", 250.01f), "foreign boundary under hold");
        Check(!l.Reserve(1, Legacy(99, 500), 1) && l.Reserve(1, Legacy(99, 250), 1), "hold fences legacy expensive but admits foreign cheap");
        var cheap = G(2, "Odin", 250);
        Check(l.ReserveAction(1, cheap), "foreign cheap generic claim coexists");
        l = Load(path);
        Check(l.GetWallet(1, "Tyr").Held == 750 && l.GetWallet(1, "Odin").Held == 250, "holds reload with legacy pending");
        Check(l.Commit(1, Token(99), 999) && l.SettleAction(1, cheap, GoldPatronOutcome.Committed), "foreign cheap settlements");
        Check(l.Get(1).ExhaustionRemaining == 0, "preparation not active cooldown");
        Check(!l.SettleAction(1, hold, GoldPatronOutcome.Unused) && !l.SettleAction(1, hold, GoldPatronOutcome.Committed, new ModelEvidence(false)), "no manufactured unused or consume proof");
        Check(l.SettleAction(1, hold, GoldPatronOutcome.Committed, new ModelEvidence(true)), "MODEL terminal consumed proof");
        Check(l.GetWallet(1, "Tyr").Favor == 250 && l.Get(1).ExhaustedPatron == "Tyr" && l.Get(1).ExhaustionRemaining == 1200, "one debit and origin cooldown");
        Check(l.Elapse(1, 10) && l.Flush(), "elapsed financial time"); l = Load(path);
        Check(l.SettleAction(1, hold, GoldPatronOutcome.Committed) && l.Get(1).ExhaustionRemaining == 1190, "terminal replay no restart");
        Check(!l.SettleAction(1, hold, GoldPatronOutcome.Unused, new ModelEvidence(true)), "contradictory terminal result rejected");
        Check(l.Reserve(1, Legacy(100, 250), 1) && l.Commit(1, Token(100), 999), "foreign legacy cheap under active Tyr cooldown");
        Check(l.Get(1).ExhaustedPatron == "Tyr" && l.Get(1).ExhaustionRemaining == 1190, "cheap preserves timer and origin");
        Check(!l.Reserve(1, Legacy(101, 500), 1) && !l.CanStart(1, "Tyr", 0), "active Tyr exact blocking");
    }

    private static void RevokeAndGeneration(string path)
    {
        var l = Load(path); l.Unlock(1, "Tyr"); l.Gain(1, "Tyr", 1000);
        var hold = G(1, "Tyr", 750, true); Check(l.ReserveAction(1, hold), "revoke setup");
        Check(l.SettleAction(1, hold, GoldPatronOutcome.Unused, new ModelEvidence(true)), "MODEL verified unused releases");
        Check(l.GetWallet(1, "Tyr").Available == 1000 && l.Get(1).ExhaustionRemaining == 0, "release unholds, no money creation");
        Check(l.SettleAction(1, hold, GoldPatronOutcome.Unused) && !l.SettleAction(1, hold, GoldPatronOutcome.Committed), "released replay distinct from committed");
        var next = G(2, "Tyr", 750); Check(l.ReserveAction(1, next) && l.SettleAction(1, next, GoldPatronOutcome.Committed), "next generation");
        Check(!l.ReserveAction(1, hold) && !l.SettleAction(1, hold, GoldPatronOutcome.Unused), "pruned old outcome fails closed");
        Check(l.Elapse(1, 1200) && l.Get(1).ExhaustedPatron == "", "timer expiry clears origin");
        var third = G(3, "Tyr", 250); Check(l.ReserveAction(1, third) && l.SettleAction(1, third, GoldPatronOutcome.Committed), "next cheap generation");
        l = Load(path); Check(l.GetWallet(1, "Tyr").Favor == 0 && l.Get(1).Generation == 3, "watermark persisted");
    }

    private static void LegacyConcurrency(string path)
    {
        var l = Load(path); l.Unlock(1); l.Unlock(1, "Tyr"); l.Gain(1, 1000); l.Gain(1, "Tyr", 1000);
        Check(l.Reserve(1, Legacy(44, 500), 1), "legacy pricey pending");
        Check(!l.ReserveAction(1, G(1, "Tyr", 750, true)), "legacy pricey fences preparation");
        Check(!l.ReserveAction(1, G(1, "Volundr", 250)), "same legacy wallet cannot reserve twice");
        Check(!l.SetFavor(1, 200), "debug cannot shrink reserved funds");
        Check(l.Reject(1, Token(44)), "legacy unused");
        var generic = G(1, "Volundr", 750); Check(l.ReserveAction(1, generic), "generic Volundr holds same canonical pool");
        Check(!l.Reserve(1, Legacy(45, 250), 1) && !l.SetFavor(1, 0), "legacy cannot overspend generic hold");
        Check(l.SettleAction(1, generic, GoldPatronOutcome.Unused), "manual unused release");
        Check(l.Get(1).Favor == 1000 && l.GetWallet(1, "Volundr").Favor == 1000, "one canonical alias");
    }

    private static void Failure(string path)
    {
        var l = Load(path); l.Unlock(1, "Odin"); l.Gain(1, "Odin", 1000);
        var g = G(1, "Odin", 750); Check(l.ReserveAction(1, g), "durable generic pending");
        Directory.CreateDirectory(path + ".tmp");
        Check(!l.SettleAction(1, g, GoldPatronOutcome.Committed) && !l.IsAvailable && l.Get(1) == null && !l.Gain(1, "Odin", 1), "write failure quarantines authority");
        var recovered = Load(path); Check(recovered.GetWallet(1, "Odin").Favor == 1000 && recovered.GetWallet(1, "Odin").Held == 750, "disk pending outcome retained");
        Directory.Delete(path + ".tmp");
        Check(recovered.SettleAction(1, g, GoldPatronOutcome.Committed) && recovered.GetWallet(1, "Odin").Favor == 250, "repaired storage settles once");
    }

    private static void TokenNamespaceAndReentrancy(string path)
    {
        var l = Load(path); l.Unlock(1); l.Unlock(1, "Tyr"); l.Gain(1, 1000); l.Gain(1, "Tyr", 1000);
        var g = G(1, "Tyr", 100); Check(l.ReserveAction(1, g), "namespace claim");
        Check(!l.Reserve(1, Legacy(1, 250), 1), "legacy cannot reuse generic pending GUID");
        Check(l.SettleAction(1, g, GoldPatronOutcome.Committed) && !l.Reserve(1, Legacy(1, 250), 1), "legacy cannot reuse generic result GUID");
        var hold = G(2, "Tyr", 300, true); Check(l.ReserveAction(1, hold), "reentrancy setup");
        Check(l.SettleAction(1, hold, GoldPatronOutcome.Committed, new ReentrantEvidence { Ledger = l }), "one outer settlement");
        Check(l.GetWallet(1, "Tyr").Favor == 600 && l.Get(1).ExhaustionRemaining == 300, "no double charge from callback");
        Check(Load(path).GetWallet(1, "Tyr").Favor == 600, "token namespace stays reloadable");
    }

    private static void MigrationAndInvalidGraphs(string root)
    {
        foreach (bool pending in new[] { false, true })
        {
            string path = Path.Combine(root, "v3-patron-" + pending + ".bin");
            Fixture(path, 3, pending, pending ? 0 : 1200, null);
            var l = Load(path);
            Check(l.Get(11).Favor == 900 && l.Get(11).Wallets.Count == 1 && l.GetWallet(11, "Tyr") == null, "v3 maps only Volundr");
            Check(l.Get(11).ExhaustedPatron == (pending ? "" : "Volundr"), "legacy timer provenance");
            Check(l.Unlock(11, "Tyr"), "rewrite v3 to v4"); l = Load(path);
            Check(l.Get(11).SettledTokens.Contains(Token(90)) && l.Get(11).ExhaustionRemaining == (pending ? 0 : 1200), "legacy tokens/timer persist");
            if (pending) Check(l.Get(11).Pending.StationString == "unchanged-contract" && l.Commit(11, Token(91), 999) && l.Get(11).Favor == 650, "legacy pending contract survives migration");
        }
        string max = Path.Combine(root, "v4-maxgen.bin");
        Fixture(max, 4, false, 0, w => { S(w, ""); w.Write(long.MaxValue); w.Write(0); w.Write(0); w.Write(0); w.Write(0); });
        Check(!Load(max).ReserveAction(11, G(long.MaxValue, "Volundr", 1)), "generation exhaustion fails closed");
        Action<BinaryWriter>[] malformed = {
            w => { S(w, ""); w.Write(0L); w.Write(1); S(w, "Volundr"); w.Write(true); w.Write(10f); w.Write(0); w.Write(0); },
            w => { S(w, ""); w.Write(0L); w.Write(1); S(w, "Tyr"); w.Write(true); w.Write(float.NaN); w.Write(0); w.Write(0); },
            w => { S(w, "Missing"); w.Write(0L); w.Write(0); w.Write(0); w.Write(0); },
            w => { S(w, ""); w.Write(1L); w.Write(0); w.Write(1); WG(w, G(1, "Tyr", 750, true)); w.Write(0); },
            w => { S(w, ""); w.Write(1L); w.Write(1); S(w, "Tyr"); w.Write(true); w.Write(100f); w.Write(1); WG(w, G(1, "Tyr", 750, true)); w.Write(0); },
            w => { S(w, ""); w.Write(1L); w.Write(0); w.Write(0); w.Write(1); WG(w, G(1, "Volundr", 1)); w.Write(99); },
            w => { S(w, ""); w.Write(0L); w.Write(0); w.Write(0); w.Write(0); w.Write(1); },
            w => { S(w, ""); w.Write(0L); w.Write(0); w.Write(0); w.Write(0); w.Write(1); S(w, "Combat100"); w.Write(2L); w.Write(2UL); },
            w => { S(w, ""); w.Write(0L); w.Write(0); w.Write(0); w.Write(0); w.Write(1); S(w, "Combat100"); w.Write(1L); w.Write(3UL); }
        };
        for (int i = 0; i < malformed.Length; i++)
        {
            string path = Path.Combine(root, "v4-invalid-" + i + ".bin"); Fixture(path, 4, false, 0, malformed[i]);
            var l = new GoldCraftingLedger(path); Check(!l.Load() && !l.IsAvailable && l.Get(11) == null, "CRC-valid malformed graph " + i);
        }
    }

    private static void AtomicAwards(string path)
    {
        var l = Load(path); l.Unlock(1, "Tyr"); l.Unlock(1, "Odin");
        var split = new Dictionary<string, float> { { "Tyr", 20 }, { "Odin", 40 } };
        Check(l.Award(1, "Combat100", 1, split) == GoldAwardResult.Accepted, "atomic split award");
        l = Load(path);
        Check(l.GetWallet(1, "Tyr").Favor == 20 && l.GetWallet(1, "Odin").Favor == 40, "split and receipt durable together");
        Check(l.Award(1, "Combat100", 1, split) == GoldAwardResult.Duplicate, "award retry after restart deduped");
        Check(l.Award(1, "Combat100", 1, new Dictionary<string, float> { { "Tyr", 100 } }) == GoldAwardResult.Duplicate && l.GetWallet(1, "Tyr").Favor == 20, "changed duplicate payload cannot mint another award");
        Check(l.Award(1, "Combat100", 2, new Dictionary<string, float> { { "Tyr", 10 }, { "Missing", 10 } }) == GoldAwardResult.Rejected && l.GetWallet(1, "Tyr").Favor == 20, "invalid split is all-or-none");
        Check(l.Award(1, "Combat100", 2, new Dictionary<string, float> { { "Tyr", float.NaN } }) == GoldAwardResult.Rejected, "nonfinite split rejected");
        Check(l.Gain(1, "Tyr", 980) && l.Award(1, "Combat100", 2, split) == GoldAwardResult.Accepted, "cap still consumes event");
        Check(l.GetWallet(1, "Tyr").Favor == 1000 && l.GetWallet(1, "Odin").Favor == 80, "independent per-patron cap");
        var spend = G(1, "Tyr", 100); Check(l.ReserveAction(1, spend) && l.SettleAction(1, spend, GoldPatronOutcome.Committed), "make space after capped award");
        Check(l.Award(1, "Combat100", 2, split) == GoldAwardResult.Duplicate && l.GetWallet(1, "Tyr").Favor == 900, "no overflow bank on retry after spend");
        Check(l.Award(1, "Combat100", 4, split) == GoldAwardResult.Accepted && l.Award(1, "Combat100", 3, split) == GoldAwardResult.Accepted, "out-of-order replay window");
        Check(l.Award(1, "Combat100", 3, split) == GoldAwardResult.Duplicate, "out-of-order duplicate");
        Check(l.Award(1, "Combat100", 70, split) == GoldAwardResult.Accepted && l.Award(1, "Combat100", 1, split) == GoldAwardResult.Expired, "old source sequence fails closed");
        Check(l.Unlock(2, "Tyr") && l.Award(2, "Combat100", 1, new Dictionary<string, float> { { "Tyr", 20 } }) == GoldAwardResult.Accepted, "personal award receipt isolation");
        float beforeTyr = l.GetWallet(1, "Tyr").Favor, beforeOdin = l.GetWallet(1, "Odin").Favor;
        Directory.CreateDirectory(path + ".tmp");
        Check(l.Award(1, "Combat100", 71, split) == GoldAwardResult.Unavailable && !l.IsAvailable && l.Get(1) == null, "award save failure no exposed partial split");
        var recovered = Load(path);
        Check(recovered.GetWallet(1, "Tyr").Favor == beforeTyr && recovered.GetWallet(1, "Odin").Favor == beforeOdin, "pre-award disk state recovered");
        Directory.Delete(path + ".tmp");
        Check(recovered.Award(1, "Combat100", 71, split) == GoldAwardResult.Accepted && Load(path).Award(1, "Combat100", 71, split) == GoldAwardResult.Duplicate, "award repaired then replay once");
    }

    private static void S(BinaryWriter w, string value) { byte[] b = Encoding.UTF8.GetBytes(value); w.Write(b.Length); w.Write(b); }
    private static void WG(BinaryWriter w, GoldPatronGrant g) { S(w, g.Patron); S(w, g.Action); S(w, g.Token); w.Write(g.Cost); w.Write(g.Generation); w.Write(g.Prepared); }
    private static void Fixture(string path, int version, bool pending, double timer, Action<BinaryWriter> extra)
    {
        byte[] payload;
        using (var m = new MemoryStream()) using (var w = new BinaryWriter(m))
        {
            w.Write(1); w.Write(11L); w.Write(true); w.Write(900f); w.Write(timer); w.Write(0d); w.Write(0f); w.Write(0);
            S(w, Token(90)); w.Write(1); S(w, Token(90)); w.Write(pending);
            if (pending) { var g = Legacy(91, 250); S(w, g.GuidToken); w.Write(g.Kind); S(w, g.Prefab); w.Write(g.OldQuality); w.Write(g.NewQuality); w.Write(g.Variant); w.Write(g.Cost); S(w, g.StationString); w.Write(g.ExpiresAt); }
            extra?.Invoke(w); w.Flush(); payload = m.ToArray();
        }
        uint crc = uint.MaxValue;
        foreach (byte b in payload) { crc ^= b; for (int j = 0; j < 8; j++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1; }
        using (var w = new BinaryWriter(File.Create(path))) { w.Write(0x47434C31u); w.Write(version); w.Write(payload.Length); w.Write(payload); w.Write(~crc); }
    }
}
