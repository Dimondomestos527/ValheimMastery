using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ValheimMastery
{
    internal sealed class MeadXpGrant
    {
        internal long Serial, Player;
        internal int Stage;
        internal float Coefficient;
        internal string Input;
        internal bool Valid => Serial > 0 && Player != 0 && Input != null && Input.Length <= 64 && MeadXpReceiptPolicy.BaseXp(Stage) > 0f &&
            MeadXpReceiptPolicy.ValidCoefficient(Coefficient) && CookingRecipeProgression.IsMeadBase(Input);
        internal bool Same(MeadXpGrant other) => other != null && Serial == other.Serial && Player == other.Player &&
            Stage == other.Stage && Coefficient == other.Coefficient && string.Equals(Input, other.Input, StringComparison.Ordinal);
    }

    internal static class MeadXpOutbox
    {
        internal const int MaxRows = 128, MaxBytes = 32768, ReservedRowBytes = 160;
        internal static readonly int Key = "valheim_mastery.mead_xp.outbox_v1".GetStableHashCode();
        internal static readonly int DeferredLoadKey = "valheim_mastery.mead_xp.deferred_load_v2".GetStableHashCode();
        internal static readonly int DeferredCompletionKey = "valheim_mastery.mead_xp.deferred_completion_v2".GetStableHashCode();
        internal static int DeferredKey(int stage) => stage == 2 ? DeferredLoadKey : DeferredCompletionKey;
        private static int Bytes(string value)
        {
            if (value.Length > MaxBytes) throw new FormatException("Mead XP outbox exceeds byte capacity.");
            int size = Encoding.UTF8.GetByteCount(value);
            if (size > MaxBytes) throw new FormatException("Mead XP outbox exceeds byte capacity.");
            return size;
        }
        internal static List<MeadXpGrant> Read(string value)
        {
            var result = new List<MeadXpGrant>();
            if (string.IsNullOrEmpty(value)) return result;
            Bytes(value);
            string[] lines = value.Split(new[] { '\n' }, MaxRows + 2);
            if (lines.Length > MaxRows + 1) throw new FormatException("Mead XP outbox exceeds row capacity.");
            if (lines[0] != "1") throw new FormatException("Unknown mead XP outbox schema.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] fields = lines[i].Split(';');
                if (fields.Length != 5) throw new FormatException("Malformed mead XP outbox row.");
                var row = new MeadXpGrant { Serial = long.Parse(fields[0], CultureInfo.InvariantCulture),
                    Stage = int.Parse(fields[1], CultureInfo.InvariantCulture), Player = long.Parse(fields[2], CultureInfo.InvariantCulture),
                    Coefficient = float.Parse(fields[3], CultureInfo.InvariantCulture), Input = fields[4] };
                if (!row.Valid || !identities.Add(row.Serial.ToString(CultureInfo.InvariantCulture) + "." + row.Stage))
                    throw new FormatException("Invalid/duplicate mead XP outbox row.");
                result.Add(row);
            }
            return result;
        }
        internal static string Write(IEnumerable<MeadXpGrant> rows)
        {
            var values = rows.Take(MaxRows + 1).ToList();
            if (values.Count > MaxRows || values.Any(row => row?.Valid != true)) throw new FormatException("Invalid mead XP outbox write.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            if (values.Any(row => !identities.Add(row.Serial.ToString(CultureInfo.InvariantCulture) + "." + row.Stage)))
                throw new FormatException("Duplicate mead XP outbox write.");
            string encoded = "1\n" + string.Join("\n", values.Select(row => row.Serial.ToString(CultureInfo.InvariantCulture) +
                ";" + row.Stage + ";" + row.Player.ToString(CultureInfo.InvariantCulture) + ";" + row.Coefficient.ToString("R", CultureInfo.InvariantCulture) + ";" + row.Input));
            Bytes(encoded); return encoded;
        }
        internal static bool HasCapacity(string raw, int required, int reserved = 0)
        {
            if (required < 0 || reserved < 0 || required > MaxRows || reserved > MaxRows || required + reserved > MaxRows) return false;
            try { var rows = Read(raw); return rows.Count + required + reserved <= MaxRows &&
                (string.IsNullOrEmpty(raw) ? 2 : Bytes(raw)) + (required + reserved) * ReservedRowBytes <= MaxBytes; }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }
        internal static MeadXpGrant ReadDeferred(string raw, int stage)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var rows = Read(raw);
            if (rows.Count != 1 || rows[0].Stage != stage) throw new FormatException("Invalid deferred mead XP grant.");
            return rows[0];
        }
        internal static bool Append(List<MeadXpGrant> rows, MeadXpGrant grant)
        {
            if (grant?.Valid != true) return false;
            MeadXpGrant existing = rows.FirstOrDefault(row => row.Serial == grant.Serial && row.Stage == grant.Stage);
            if (existing != null)
            {
                if (!existing.Same(grant)) throw new InvalidOperationException("Conflicting mead XP receipt identity.");
                return false;
            }
            if (rows.Count >= MaxRows) throw new InvalidOperationException("Mead XP outbox capacity exhausted.");
            rows.Add(grant); return true;
        }
        internal static bool CanDeliver(List<MeadXpGrant> rows, MeadXpGrant grant) => rows.Any(row => row.Same(grant)) &&
            !rows.Any(row => row.Player == grant.Player && row.Stage == grant.Stage && row.Serial < grant.Serial);
        internal static bool Remove(List<MeadXpGrant> rows, MeadXpGrant grant) => rows.RemoveAll(row => row.Same(grant)) > 0;
    }
}
