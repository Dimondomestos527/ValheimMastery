using System.Diagnostics;
using ValheimMastery;

// QC-only comparison, NOT compiled into the game plugin. Measure before choosing a runtime replacement.
internal static class WorkshopPlannerExperiment
{
    private sealed class Stock : IWorkshopDebitStore
    {
        internal string Id;
        internal bool Owned;
        internal Dictionary<string, int> Items = new(StringComparer.Ordinal);
        public object Identity => Id;
        public int Count(string item) => Items.GetValueOrDefault(item);
        public object Capture() => new Dictionary<string, int>(Items);
        public void Restore(object snapshot) => Items = new((Dictionary<string, int>)snapshot);
        public void Remove(string item, int amount) => Items[item] -= amount;
        public void Flush() { }
    }
    // Counterpart of the current per-resource distance-ordered debit, using in-memory counts only.
    private static List<WorkshopDebitLine> Current(List<Stock> stocks, Dictionary<string, int> costs)
    {
        var result = new List<WorkshopDebitLine>();
        foreach (var need in costs)
        {
            int left = need.Value;
            foreach (var stock in stocks)
            {
                int take = Math.Min(left, stock.Count(need.Key));
                if (take > 0) result.Add(new() { Store = stock, Item = need.Key, Amount = take });
                left -= take;
                if (left == 0) break;
            }
            if (left != 0) return null;
        }
        return result;
    }
    private static List<WorkshopDebitLine> Greedy(List<Stock> stocks, Dictionary<string, int> costs)
    {
        string[] names = costs.Keys.Order(StringComparer.Ordinal).ToArray();
        int[] left = names.Select(n => costs[n]).ToArray();
        var unique = stocks.DistinctBy(s => s.Identity).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        int[][] amounts = unique.Select(s => names.Select(n => Math.Max(0, s.Count(n))).ToArray()).ToArray();
        bool[] used = new bool[unique.Count];
        var result = new List<WorkshopDebitLine>();
        while (left.Any(n => n > 0))
        {
            int best = -1; double bestScore = 0; long bestUnits = 0;
            for (int c = 0; c < unique.Count; c++)
            {
                if (used[c]) continue;
                double score = 0; long units = 0;
                for (int m = 0; m < names.Length; m++)
                {
                    if (left[m] == 0) continue;
                    int usable = Math.Min(left[m], amounts[c][m]);
                    score += (double)usable / left[m]; units += usable;
                }
                if (score > bestScore || (score == bestScore && score > 0 &&
                    (best < 0 || (unique[c].Owned && !unique[best].Owned) ||
                     (unique[c].Owned == unique[best].Owned && units > bestUnits))))
                { best = c; bestScore = score; bestUnits = units; }
            }
            if (best < 0) return null;
            used[best] = true;
            for (int m = 0; m < names.Length; m++)
            {
                int take = Math.Min(left[m], amounts[best][m]);
                if (take <= 0) continue;
                result.Add(new() { Store = unique[best], Item = names[m], Amount = take }); left[m] -= take;
            }
        }
        return result;
    }
    private static int Touches(List<WorkshopDebitLine> plan) => plan.Select(l => l.Store.Identity).Distinct().Count();
    private static void Check(bool good, string message)
    { if (!good) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static double Microseconds(Func<List<WorkshopDebitLine>> action)
    {
        for (int i = 0; i < 1000; i++) action();
        var samples = new double[11];
        for (int batch = 0; batch < samples.Length; batch++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 1000; i++) action();
            samples[batch] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
        Array.Sort(samples); return samples[samples.Length / 2]; // us/call = ms/1000calls *1000
    }
    internal static int Run()
    {
        var example = new List<Stock> { new() { Id = "a", Items = new() { ["wood"] = 2 } },
            new() { Id = "b", Items = new() { ["wood"] = 3 } }, new() { Id = "c", Items = new() { ["wood"] = 6 } },
            new() { Id = "d", Items = new() { ["wood"] = 47 } } };
        var wood = new Dictionary<string, int> { ["wood"] = 20 };
        Check(Touches(Current(example, wood)) == 4 && Touches(Greedy(example, wood)) == 1, "2/3/6/47 stock: four touches to one");
        var mixed = new List<Stock> { new() { Id = "a", Items = new() { ["wood"] = 10 } },
            new() { Id = "b", Items = new() { ["stone"] = 20 } }, new() { Id = "c", Items = new() { ["iron"] = 2 } },
            new() { Id = "d", Items = new() { ["wood"] = 10, ["stone"] = 20, ["iron"] = 2 } } };
        var costs = new Dictionary<string, int> { ["wood"] = 10, ["stone"] = 20, ["iron"] = 2 };
        Check(Touches(Current(mixed, costs)) == 3 && Touches(Greedy(mixed, costs)) == 1, "whole-recipe coverage beats fragmented resources");
        string Signature(List<WorkshopDebitLine> p) => string.Join("|", p.Select(l => l.Store.Identity + ":" + l.Item + ":" + l.Amount));
        Check(Signature(Greedy(mixed, costs)) == Signature(Greedy(mixed.AsEnumerable().Reverse().ToList(), costs)), "stable ties independent of source enumeration");
        Check(Greedy(new() { example[0], example[0] }, wood) == null, "duplicate inventory identity never doubles stock");
        var plan = Greedy(mixed, costs);
        Check(mixed[3].Count("wood") == 10, "proposal does not mutate inventory");
        mixed[3].Items["iron"] = 1;
        using (var failed = WorkshopAtomicDebit.Begin(plan, () => true))
            Check(failed == null && mixed[3].Count("wood") == 10, "stale candidate rejected atomically before any removal");
        mixed[3].Items["iron"] = 2;
        using (var debit = WorkshopAtomicDebit.Begin(Greedy(mixed, costs), () => true))
        { Check(debit != null && mixed[3].Count("wood") == 0, "candidate debits exact requirements"); }
        Check(mixed[3].Count("wood") == 10 && mixed[3].Count("iron") == 2, "failed output rollback restores every material");
        Console.WriteLine("QC-only in-memory planner; NO Inventory.Load, Unity, ownership, disk, RTT or action timing. Median11x1000 after1000warmup; Release .NET8.");
        Console.WriteLine("chests,materials,current_us,candidate_us,current_touches,candidate_touches");
        foreach (int count in new[] { 1, 5, 20, 32 })
        foreach (int materials in new[] { 1, 3 })
        {
            var stocks = Enumerable.Range(0, count).Select(i => new Stock { Id = i.ToString("D4"), Items =
                Enumerable.Range(0, materials).ToDictionary(m => "resource" + m, m => i == count - 1 ? 100 : 2) }).ToList();
            var need = Enumerable.Range(0, materials).ToDictionary(m => "resource" + m, m => 20);
            Console.WriteLine(FormattableString.Invariant($"{count},{materials},{Microseconds(() => Current(stocks, need)):F3},{Microseconds(() => Greedy(stocks, need)):F3},{Touches(Current(stocks, need))},{Touches(Greedy(stocks, need))}"));
        }
        return 0;
    }
}
