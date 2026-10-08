using System;
using ValheimMastery;
static class Program
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static int Main()
    {
        var model = new CraftingBuildReuseModel();
        Check(!model.ConsumeReplacement("wall", 1), "ordinary new build rewarded");
        Check(model.Removed("wall", 2), "demolition adds debt");
        Check(!model.ConsumeReplacement("roof", 3), "other recipe unaffected");
        Check(model.ConsumeReplacement("wall", 4), "replacement suppressed");
        Check(!model.ConsumeReplacement("wall", 5), "next genuinely new build rewarded");
        for (int i = 0; i < 3; i++) model.Removed("wall", 10 + i);
        var loaded = CraftingBuildReuseModel.Decode(model.Encode(15), 16);
        for (int i = 0; i < 3; i++) Check(loaded.ConsumeReplacement("wall", 17 + i), "three saved debts recovered");
        Check(!loaded.ConsumeReplacement("wall", 25), "exact debt count, not blanket five-minute ban");
        model.Removed("floor", 100);
        Check(!model.ConsumeReplacement("floor", 400), "debt expires at five minutes");
        Check(!CraftingBuildReuseModel.Decode(model.Encode(120), 500).ConsumeReplacement("wall", 501), "expired on relog");
        Check(!CraftingBuildReuseModel.Decode("bad|payload", 1).ConsumeReplacement("wall", 2), "malformed profile safe");
        Check(!model.Removed("wall", double.NaN), "nonfinite time rejected");
        Console.WriteLine("Build reuse QC PASS: matching debts, diversity, expiry, persistence and malformed state (managed model only).");
        return 0;
    }
}
