using System;
using System.Collections.Generic;
using System.Globalization;
namespace ValheimMastery
{
    // Immutable result of the roll made when an input is actually accepted.
    internal sealed class ProcessingBatchReceipt
    {
        internal long Author;
        internal float Level;
        internal bool Bonus;
        internal string Encode() => Author.ToString(CultureInfo.InvariantCulture) + "," + Level.ToString("R", CultureInfo.InvariantCulture) + "," + (Bonus ? "1" : "0");
        internal static ProcessingBatchReceipt Decode(string raw)
        {
            var fields = (raw ?? "").Split(',');
            if (fields.Length != 3 || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long author) ||
                !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float level) ||
                float.IsNaN(level) || float.IsInfinity(level) || level < 0 || level > 100 || (fields[2] != "0" && fields[2] != "1")) return new ProcessingBatchReceipt();
            return new ProcessingBatchReceipt { Author = author, Level = level, Bonus = author != 0 && fields[2] == "1" };
        }
    }
    internal sealed class ProcessingBatchLedger
    {
        internal readonly List<ProcessingBatchReceipt> Items = new List<ProcessingBatchReceipt>();
        internal static ProcessingBatchLedger Read(string raw, int vanillaCount)
        {
            var ledger = new ProcessingBatchLedger();
            if (!string.IsNullOrEmpty(raw)) foreach (var item in raw.Split(';')) ledger.Items.Add(ProcessingBatchReceipt.Decode(item));
            vanillaCount = Math.Max(0, Math.Min(4096, vanillaCount));
            if (ledger.Items.Count > vanillaCount) ledger.Items.RemoveRange(vanillaCount, ledger.Items.Count - vanillaCount);
            while (ledger.Items.Count < vanillaCount) ledger.Items.Add(new ProcessingBatchReceipt());
            return ledger;
        }
        internal string Encode() => string.Join(";", Items.ConvertAll(item => item.Encode()));
    }
}
