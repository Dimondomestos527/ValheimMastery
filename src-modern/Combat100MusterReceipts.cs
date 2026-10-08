using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace ValheimMastery
{
    // Pure strict codec for trusted-server PAID Muster acceptance records.
    // Financial authority does not come from caller-supplied text. The runtime
    // validates direct server RPC/current native recipient before creating one.
    internal static class Combat100MusterReceipts
    {
        internal const string DataKey = "VM_Combat100_MusterPaid_v1", LifeKey = "VM_Combat100_Life_v1";
        internal const int MaxRecords = 4096, MaxBytes = 1024 * 1024;
        internal sealed class Receipt
        {
            internal long World, Player, ServerDeadline, LocalDeadline;
            internal string Token, Epoch, Life, Character;
            internal bool Finished;
            internal Receipt Copy() => (Receipt)MemberwiseClone();
            internal bool Same(Receipt r) => r != null && World == r.World && Player == r.Player &&
                ServerDeadline == r.ServerDeadline && Token == r.Token && Epoch == r.Epoch && Life == r.Life && Character == r.Character;
        }
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static bool Read(string encoded, out Dictionary<string, Receipt> records)
        {
            records = null;
            if (string.IsNullOrEmpty(encoded)) { records = new Dictionary<string, Receipt>(StringComparer.Ordinal); return true; }
            try
            {
                if (encoded.Length > ((MaxBytes + 2) / 3) * 4) return false;
                var raw = Convert.FromBase64String(encoded);
                if (raw.Length < 44 || raw.Length > MaxBytes) return false;
                using var input = new MemoryStream(raw, false); using var reader = new BinaryReader(input, Utf8, true);
                if (reader.ReadUInt32() != 0x4D315243 || reader.ReadInt32() != 1) return false;
                byte[] digest = reader.ReadBytes(32), body = reader.ReadBytes(raw.Length - 40);
                using (var sha = SHA256.Create())
                { byte[] actual = sha.ComputeHash(body); for (int i = 0; i < 32; ++i) if (digest[i] != actual[i]) return false; }
                using var stream = new MemoryStream(body, false); using var r = new BinaryReader(stream, Utf8, true);
                int count = r.ReadInt32(); if (count < 0 || count > MaxRecords) return false;
                var parsed = new Dictionary<string, Receipt>(StringComparer.Ordinal);
                for (int i = 0; i < count; ++i)
                {
                    var item = new Receipt { World = r.ReadInt64(), Player = r.ReadInt64(), ServerDeadline = r.ReadInt64(), LocalDeadline = r.ReadInt64(),
                        Token = Text(r, 32), Epoch = Text(r, 32), Life = Text(r, 32), Character = Text(r, 128), Finished = r.ReadBoolean() };
                    if (!Valid(item) || parsed.ContainsKey(item.Token)) return false;
                    parsed.Add(item.Token, item);
                }
                if (stream.Position != stream.Length) return false;
                records = parsed; return true;
            }
            catch { return false; }
        }
        internal static bool Encode(IReadOnlyDictionary<string, Receipt> records, out string encoded)
        {
            encoded = null;
            try
            {
                if (records == null || records.Count > MaxRecords) return false;
                using var body = new MemoryStream(); using (var w = new BinaryWriter(body, Utf8, true))
                {
                    w.Write(records.Count);
                    foreach (var pair in records)
                    {
                        var item = pair.Value;
                        if (!Valid(item) || pair.Key != item.Token) return false;
                        w.Write(item.World); w.Write(item.Player); w.Write(item.ServerDeadline); w.Write(item.LocalDeadline);
                        Text(w, item.Token); Text(w, item.Epoch); Text(w, item.Life); Text(w, item.Character); w.Write(item.Finished);
                    }
                }
                if (body.Length > MaxBytes - 40) return false;
                using var output = new MemoryStream(); using (var w = new BinaryWriter(output, Utf8, true))
                {
                    w.Write(0x4D315243u); w.Write(1);
                    using var sha = SHA256.Create(); byte[] bytes = body.ToArray(); w.Write(sha.ComputeHash(bytes)); w.Write(bytes);
                }
                encoded = Convert.ToBase64String(output.ToArray()); return true;
            }
            catch { return false; }
        }
        // Permanent token tombstones; expired records are never silently pruned.
        // Caller freezes both deadlines at FIRST trusted acceptance; retry cannot refresh.
        internal static bool Accept(Dictionary<string, Receipt> records, Receipt proposed, out Receipt accepted)
        {
            accepted = null; if (records == null || !Valid(proposed) || proposed.Finished) return false;
            if (records.TryGetValue(proposed.Token, out var old))
            { if (!old.Same(proposed)) return false; accepted = old.Copy(); return true; }
            if (records.Count >= MaxRecords) return false;
            accepted = proposed.Copy(); records.Add(proposed.Token, accepted.Copy()); return true;
        }
        internal static double Remaining(Receipt receipt, long world, long player, string life, long now)
        {
            if (!Valid(receipt) || receipt.Finished || receipt.World != world || receipt.Player != player || receipt.Life != life ||
                now <= 0 || now >= receipt.LocalDeadline) return 0d;
            double seconds = (receipt.LocalDeadline - now) / (double)TimeSpan.TicksPerSecond;
            // Clock rollback must not produce a new oversized entitlement.
            return seconds <= 300d ? seconds : 0d;
        }
        internal static bool EndLife(Dictionary<string, Receipt> records, string life)
        {
            if (records == null || !Guid.TryParseExact(life, "N", out _)) return false;
            foreach (var item in records.Values) if (item.Life == life) item.Finished = true;
            return true;
        }
        private static bool Valid(Receipt r) => r != null && r.World != 0 && r.Player != 0 &&
            r.ServerDeadline > 0 && r.ServerDeadline <= DateTime.MaxValue.Ticks && r.LocalDeadline > 0 && r.LocalDeadline <= DateTime.MaxValue.Ticks &&
            Guid.TryParseExact(r.Token, "N", out _) && Guid.TryParseExact(r.Epoch, "N", out _) && Guid.TryParseExact(r.Life, "N", out _) && Ascii(r.Character, 128);
        private static bool Ascii(string s, int max)
        { if (string.IsNullOrEmpty(s) || s.Length > max) return false; foreach (char c in s) if (c < 33 || c > 126) return false; return true; }
        private static string Text(BinaryReader r, int max)
        {
            int length = r.ReadInt32(); if (length < 1 || length > max) throw new InvalidDataException();
            var bytes = r.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            var text = Utf8.GetString(bytes); if (!Ascii(text, max)) throw new InvalidDataException(); return text;
        }
        private static void Text(BinaryWriter w, string text) { var bytes = Utf8.GetBytes(text); w.Write(bytes.Length); w.Write(bytes); }
    }
}

namespace ValheimMastery
{
    // Owner application endpoint; the caller MUST authenticate a direct server
    // grant and frozen recipient native identity. This class installs no RPC.
    internal static class Combat100MusterOwner
    {
        internal enum Outcome { NotReady, Denied, Quarantined, Accepted }
        private sealed class Gate
        {
            internal bool Busy, Quarantined, Loading, TerminalOverflow; internal bool FoodReady = true;
            internal readonly HashSet<string> TerminatedLives = new HashSet<string>(StringComparer.Ordinal);
            internal ZNet Session;
            internal long World;
            internal readonly Dictionary<string, Combat100MusterReceipts.Receipt> Authorized = new Dictionary<string, Combat100MusterReceipts.Receipt>(StringComparer.Ordinal);
        }
        private static readonly ConditionalWeakTable<Player, Gate> Gates = new ConditionalWeakTable<Player, Gate>();
        private static readonly int ThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        private static bool OwnerScope(Player p) => System.Threading.Thread.CurrentThread.ManagedThreadId == ThreadId &&
            p != null && p == Player.m_localPlayer && p.m_nview?.IsOwner() == true && p.m_nview.GetZDO() != null;
        private static bool Current(Player p) => OwnerScope(p) && !p.IsDead() &&
            ZNet.instance?.GetWorld() != null && MasteryPlugin.Settings?.Enabled.Value == true;
        internal static Outcome Accept(Player player, long world, long id, string character, string token, string epoch,
            long originalServerDeadline, double remaining)
        {
            if (!Current(player) || !GoldCraftingService.CoreReady) return Outcome.NotReady;
            var entrySession = ZNet.instance;
            if (world != entrySession.GetWorldUID() || player.GetPlayerID() != id ||
                player.m_nview.GetZDO()?.m_uid.ToString() != character || !double.IsFinite(remaining) || remaining <= 0 || remaining > 300) return Outcome.Denied;
            var gate = Gates.GetOrCreateValue(player);
            if (gate.Quarantined || gate.TerminalOverflow) return Outcome.Quarantined; if (gate.Busy || !gate.FoodReady) return Outcome.NotReady;
            gate.Busy = true;
            try
            {
                player.m_customData.TryGetValue(Combat100MusterReceipts.DataKey, out var raw);
                if (!Combat100MusterReceipts.Read(raw, out var records)) { gate.Quarantined = true; return Outcome.Quarantined; }
                if (!player.m_customData.TryGetValue(Combat100MusterReceipts.LifeKey, out var life))
                {
                    if (records.Count != 0) return Outcome.Denied;
                    player.m_customData[Combat100MusterReceipts.LifeKey] = life = Guid.NewGuid().ToString("N");
                }
                if (!Guid.TryParseExact(life, "N", out _) || gate.TerminatedLives.Contains(life)) return Outcome.Denied;
                long now = DateTime.UtcNow.Ticks;
                double active = Combat100Muster.Remaining(player);
                if (active > 0)
                {
                    remaining = Math.Min(remaining, active);
                    string activeToken = Combat100Muster.ActiveToken(player);
                    if (activeToken == null || !records.TryGetValue(activeToken, out var original)) return Outcome.Denied;
                    double durableLeft = Combat100MusterReceipts.Remaining(original, world, id, life, now);
                    if (durableLeft <= 0 || !ReferenceEquals(gate.Session, entrySession) || gate.World != world ||
                        original.Character != character || !gate.Authorized.TryGetValue(activeToken, out var activeProof) ||
                        !activeProof.Same(original)) return Outcome.Denied;
                    durableLeft = Math.Min(durableLeft, Combat100MusterReceipts.Remaining(activeProof, world, id, life, now));
                    if (durableLeft <= 0) return Outcome.Denied;
                    remaining = Math.Min(remaining, durableLeft); // UTC deadline cannot grow during game-clock stalls.
                }
                var proposal = new Combat100MusterReceipts.Receipt { World = world, Player = id, Life = life,
                    Token = token, Epoch = epoch, Character = character, ServerDeadline = originalServerDeadline,
                    LocalDeadline = checked(now + (long)(remaining * TimeSpan.TicksPerSecond)) };
                bool known = records.ContainsKey(token ?? "");
                if (!Combat100MusterReceipts.Accept(records, proposal, out var receipt)) return Outcome.Denied;
                if (!known)
                {
                    if (!Combat100MusterReceipts.Encode(records, out var encoded)) return Outcome.Denied;
                    player.m_customData[Combat100MusterReceipts.DataKey] = encoded;
                    // False can mean the disk already contains Accepted. Do not roll
                    // metadata back, apply optimistically, or retry on this instance.
                    if (!GoldCharacterSave.Persist(player)) { gate.Quarantined = true; return Outcome.Quarantined; }
                }
                if (gate.Quarantined || gate.Loading) return Outcome.Quarantined;
                if (!Current(player) || !GoldCraftingService.CoreReady || !ReferenceEquals(ZNet.instance, entrySession) ||
                    world != entrySession.GetWorldUID() || player.GetPlayerID() != id ||
                    player.m_nview.GetZDO()?.m_uid.ToString() != character ||
                    player.m_customData[Combat100MusterReceipts.LifeKey] != life) return Outcome.NotReady;
                double left = Combat100MusterReceipts.Remaining(receipt, world, id, life, DateTime.UtcNow.Ticks);
                if (receipt.Finished || left <= 0) return Outcome.Accepted; // Original terminal, never reactivate.
                left = Math.Min(left, remaining); // A retained profile cannot overrun current trusted server remaining.
                if (!ReferenceEquals(gate.Session, ZNet.instance) || gate.World != world)
                { gate.Authorized.Clear(); gate.Session = entrySession; gate.World = world; }
                var authorized = receipt.Copy();
                authorized.LocalDeadline = Math.Min(receipt.LocalDeadline, checked(DateTime.UtcNow.Ticks + (long)(left * TimeSpan.TicksPerSecond)));
                if (gate.Authorized.TryGetValue(receipt.Token, out var priorAuthorization))
                {
                    if (!priorAuthorization.Same(receipt)) return Outcome.Denied;
                    authorized.LocalDeadline = Math.Min(authorized.LocalDeadline, priorAuthorization.LocalDeadline);
                }
                gate.Authorized[receipt.Token] = authorized;
                left = Combat100MusterReceipts.Remaining(authorized, world, id, life, DateTime.UtcNow.Ticks);
                return left > 0 && Combat100Muster.ReceiveCommitted(player, receipt.Token, left) ? Outcome.Accepted : Outcome.Denied;
            }
            catch (Exception error)
            { gate.Quarantined = true; MasteryPlugin.Log.LogWarning("[Combat100] Muster owner acceptance failed: " + error.Message); return Outcome.Quarantined; }
            finally { gate.Busy = false; }
        }
        internal static bool Restore(Player player)
        {
            if (!Current(player) || !GoldCraftingService.CoreReady) return false; // Accepted is NOT proof of applied effect.
            var gate = Gates.GetOrCreateValue(player);
            if (gate.Busy || gate.Quarantined || gate.TerminalOverflow || !gate.FoodReady || Combat100Muster.Active(player) ||
                !ReferenceEquals(gate.Session, ZNet.instance) || gate.World != ZNet.instance.GetWorldUID()) return false;
            if (!player.m_customData.TryGetValue(Combat100MusterReceipts.LifeKey, out var life) ||
                !player.m_customData.TryGetValue(Combat100MusterReceipts.DataKey, out var raw)) return false;
            if (!Combat100MusterReceipts.Read(raw, out var records)) { gate.Quarantined = true; return false; }
            long world = ZNet.instance.GetWorldUID(), id = player.GetPlayerID(), now = DateTime.UtcNow.Ticks;
            Combat100MusterReceipts.Receipt selected = null; double remaining = 0;
            foreach (var receipt in records.Values)
            {
                if (receipt.Character != player.m_nview.GetZDO().m_uid.ToString() ||
                    !gate.Authorized.TryGetValue(receipt.Token, out var proof) || !proof.Same(receipt)) continue;
                double left = Math.Min(Combat100MusterReceipts.Remaining(receipt, world, id, life, now),
                    Combat100MusterReceipts.Remaining(proof, world, id, life, now));
                if (left > remaining) { selected = receipt; remaining = left; }
            }
            return selected != null && Combat100Muster.ReceiveCommitted(player, selected.Token, remaining);
        }
        // Call only from native Load prefix/postfix, not an arbitrary recovery retry.
        // No hook is installed here until its native lifecycle review is complete.
        internal static bool BeforeLoad(Player player)
        {
            if (!OwnerScope(player)) return false;
            var gate = Gates.GetOrCreateValue(player);
            if (gate.Busy) { gate.Quarantined = true; return false; }
            gate.FoodReady = false; gate.Loading = gate.Quarantined = true;
            player.m_customData.Remove(Combat100MusterReceipts.DataKey);
            player.m_customData.Remove(Combat100MusterReceipts.LifeKey);
            Combat100Muster.BeforeLoad(player);
            return true;
        }
        internal static void Loaded(Player player)
        {
            if (!OwnerScope(player) || !Gates.TryGetValue(player, out var gate) || !gate.Loading) return;
            // Successful native hydration is the ONLY quarantine reset. Original
            // Load exceptions leave it set; keys absent on disk cannot survive merge.
            gate.Loading = false; gate.Quarantined = gate.TerminalOverflow;
            // Loaded food caches are ZERO. Native full-cache-pass must precede restore.
        }
        internal static void AfterNativeFoodCaches(Player player)
        {
            if (!OwnerScope(player)) return;
            var gate = Gates.GetOrCreateValue(player);
            if (gate.Loading || gate.Busy || gate.Quarantined || gate.TerminalOverflow) return;
            gate.FoodReady = true;
            try { Restore(player); }
            catch (Exception error)
            { gate.Quarantined = true; MasteryPlugin.Log.LogWarning("[Combat100] Muster cache-ready restore failed: " + error.Message); }
        }
        internal static void EndLife(Player player)
        {
            if (!OwnerScope(player)) return; // Terminalization ignores gameplay/Core gates.
            var gate = Gates.GetOrCreateValue(player);
            gate.Authorized.Clear(); // Terminal regardless of whether its disk writer succeeds.
            if (!player.m_customData.TryGetValue(Combat100MusterReceipts.LifeKey, out var old)) return;
            if (gate.TerminatedLives.Count >= Combat100MusterReceipts.MaxRecords && !gate.TerminatedLives.Contains(old))
            { gate.TerminalOverflow = gate.Quarantined = true; return; }
            if (Guid.TryParseExact(old, "N", out _)) gate.TerminatedLives.Add(old);
            if (gate.Busy || gate.Loading) { gate.Quarantined = true; return; }
            gate.Busy = true;
            try
            {
                player.m_customData[Combat100MusterReceipts.LifeKey] = Guid.NewGuid().ToString("N");
                if (player.m_customData.TryGetValue(Combat100MusterReceipts.DataKey, out var raw))
                {
                    if (!Combat100MusterReceipts.Read(raw, out var records) ||
                        !Combat100MusterReceipts.EndLife(records, old) || !Combat100MusterReceipts.Encode(records, out var encoded))
                    { gate.Quarantined = true; return; }
                    player.m_customData[Combat100MusterReceipts.DataKey] = encoded;
                }
                // Native respawn serialization alone is NOT a durable disk writer.
                // False remains ambiguous; server death terminal/recovery fencing is
                // still required before claiming crash-proof old-life rejection.
                if (!GoldCharacterSave.Persist(player)) gate.Quarantined = true;
            }
            catch (Exception error)
            { gate.Quarantined = true; MasteryPlugin.Log.LogWarning("[Combat100] Muster death save failed: " + error.Message); }
            finally { gate.Busy = false; }
        }
    }
}






