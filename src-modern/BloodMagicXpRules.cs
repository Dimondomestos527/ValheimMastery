using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimMastery
{
    internal static class BloodMagicXpRules
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal struct Credit { internal float Raw, TargetTier; }
        internal sealed class Absorption
        {
            internal readonly float Capacity;
            internal double Fraction, WeightedTier;
            internal Absorption(float capacity) { Capacity = capacity; }
            internal Credit Add(float incoming, float remaining, int tier)
            {
                if (!Finite(Capacity) || Capacity <= 0f || !Finite(incoming) || incoming <= 0f ||
                    !Finite(remaining) || remaining <= 0f || tier < 1 || tier > 8) return default;
                double part = Math.Min(incoming, remaining) / (double)Capacity;
                double total = Fraction + part;
                int chunks = (int)Math.Floor((total + 1e-7) / .1);
                if (chunks <= 0) { Fraction = total; WeightedTier += part * tier; return default; }
                double issued = chunks * .1;
                double rest = Math.Max(0d, total - issued);
                double weight = WeightedTier + part * tier - rest * tier;
                Fraction = rest; WeightedTier = rest * tier;
                return new Credit { Raw = (float)issued, TargetTier = (float)Math.Max(1d, Math.Min(8d, weight / issued)) };
            }
        }

        // An out-of-order packet within the window is valid once. The monotonic
        // source sequence is stored on its ZDO, including across ownership changes.
        internal sealed class Window
        {
            internal long High;
            internal ulong Bits;
            internal bool Accept(long serial)
            {
                if (serial <= 0) return false;
                if (serial > High)
                {
                    long shift = serial - High;
                    Bits = shift >= 64 ? 1UL : (Bits << (int)shift) | 1UL;
                    High = serial; return true;
                }
                long age = High - serial;
                if (age >= 64) return false;
                ulong bit = 1UL << (int)age;
                if ((Bits & bit) != 0) return false;
                Bits |= bit; return true;
            }
        }
        internal sealed class Receipts
        {
            internal const int Limit = 256;
            private readonly Dictionary<string, Window> rows = new Dictionary<string, Window>();
            private readonly Queue<string> order = new Queue<string>();
            internal bool Accept(string key, long serial)
            {
                if (!ValidKey(key) || serial <= 0) return false;
                if (!rows.TryGetValue(key, out Window row))
                {
                    if (rows.Count >= Limit) rows.Remove(order.Dequeue());
                    rows[key] = row = new Window(); order.Enqueue(key);
                }
                return row.Accept(serial);
            }
            internal string Export()
            {
                var text = new StringBuilder();
                foreach (string key in order)
                {
                    Window row = rows[key];
                    if (text.Length > 0) text.Append(';');
                    text.Append(key).Append('/').Append(row.High.ToString(CultureInfo.InvariantCulture))
                        .Append('/').Append(row.Bits.ToString("X", CultureInfo.InvariantCulture));
                }
                return text.ToString();
            }
            internal static Receipts Import(string text)
            {
                var result = new Receipts();
                if (string.IsNullOrEmpty(text) || text.Length > 32768) return result;
                string[] lines = text.Split(';');
                if (lines.Length > Limit) return result;
                foreach (string line in lines)
                {
                    string[] parts = line.Split('/');
                    if (parts.Length != 3 || !ValidKey(parts[0]) || result.rows.ContainsKey(parts[0]) ||
                        !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long high) || high <= 0 ||
                        !ulong.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong bits) || (bits & 1UL) == 0)
                        return new Receipts();
                    result.rows.Add(parts[0], new Window { High = high, Bits = bits }); result.order.Enqueue(parts[0]);
                }
                return result;
            }
            private static bool ValidKey(string key)
            {
                if (string.IsNullOrEmpty(key) || key.Length > 96) return false;
                foreach (char c in key) if (!(char.IsDigit(c) || c == ':' || c == '_' || c == '-')) return false;
                return true;
            }
        }
    }
}
