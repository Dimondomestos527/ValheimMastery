using System.Globalization;
using ValheimMastery;

internal static class ProcessingBatchChecks
{
    internal static int Run()
    {
        var ledger = new ProcessingBatchLedger();
        for (int i = 0; i < 50; i++) ledger.Items.Add(new ProcessingBatchReceipt
        { Author = i < 30 ? 123 : 456, Level = i < 30 ? 70 : 100, Bonus = i % 3 == 0 });
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var locale in new[] { "en-US", "uk-UA", "de-DE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                string saved = ledger.Encode();
                for (int reload = 0; reload < 5; reload++)
                {
                    var restored = ProcessingBatchLedger.Read(saved, 50);
                    Check(restored.Items.Count == 50 && restored.Items.Count(x => x.Author == 123) == 30 && restored.Items.Count(x => x.Author == 456) == 20, "mixed authors survive reload");
                    Check(restored.Items.Count(x => x.Bonus) == 17, "bonus result unchanged across reloads");
                    Check(restored.Items[0].Level == 70 && restored.Items[49].Level == 100, "author levels persist without live player");
                    Check(restored.Encode() == saved, "stable serialized receipts");
                    saved = restored.Encode();
                }
            }
            Check(ProcessingBatchLedger.Read("", 50).Items.All(x => x.Author == 0 && !x.Bonus), "legacy output not retroactively awarded");
            Check(ProcessingBatchReceipt.Decode("123,NaN,1").Author == 0, "invalid level fails closed");
            Check(!ProcessingBatchReceipt.Decode("0,100,1").Bonus, "unattributed receipt cannot grant bonus");
            Check(ProcessingBatchLedger.Read(ledger.Encode(), 20).Items.Count == 20, "discard stale extra receipts");
            Check(ProcessingBatchLedger.Read("123,70,1", 2).Items[1].Author == 0, "unknown gap does not inherit previous author");
            Check(ProcessingBatchLedger.Read("", 0).Items.Count == 0, "consumed ledger cannot replay bonus");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Console.WriteLine("PASS: persisted 50-item mixed-author output, offline receipts, 5 reloads in 3 locales, legacy and malformed input. LIVE_TEST_REQUIRED for station/RPC/FX.");
        return 0;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
