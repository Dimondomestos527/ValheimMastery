using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ValheimMastery
{
    internal enum GoldIdolStatus { Rejected, Accepted, Duplicate, Unavailable }
    internal enum GoldIdolPhase { Paid = 1, Bound = 2, Dismantling = 3, Cancelled = 4, Refunded = 5 }
    internal enum GoldIdolEvidenceKind { BindOutput, CancelUnstarted, BeginDismantle, Destroyed }
    internal sealed class GoldIdolPurchase
    {
        internal long World, Payer;
        internal string Token, Type, Output = "";
        internal GoldIdolPhase Phase;
        internal float Credited;
        internal const float PaidCost = 250f;
        internal const float DismantleRefund = 187.5f;
        internal GoldIdolPurchase Copy() => (GoldIdolPurchase)MemberwiseClone();
    }
    // Trusted coordinator's pure server evidence only, not a client-supplied receipt.
    internal interface IGoldIdolEvidence
    {
        bool Verify(GoldIdolPurchase frozen, GoldIdolEvidenceKind kind);
    }
    internal sealed partial class GoldCraftingLedger
    {
        internal const int MaxIdolPurchases = 4096; // WORLD total, permanent tombstones.
        private static bool IdolToken(string token) => Guid.TryParseExact(token, "N", out var guid) && token == guid.ToString("N");
        private static bool SameIdolToken(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static bool IdolType(string type) => type == "Meadows" || type == "BlackForest" ||
            type == "Swamp" || type == "Mountain" || type == "Plains" || type == "Mistlands";
        private bool IdolWorld(long world) => world != 0 && Path.GetFileName(_path) ==
            world.ToString(CultureInfo.InvariantCulture) + ".bin";
        private static bool IdolOutput(string value)
        {
            var parts = value?.Split(':');
            return parts?.Length == 2 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) &&
                uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint id) && (user != 0 || id != 0) &&
                value == user.ToString(CultureInfo.InvariantCulture) + ":" + id.ToString(CultureInfo.InvariantCulture);
        }
        private GoldIdolPurchase FindIdolToken(string token)
        {
            if (!Guid.TryParseExact(token, "N", out var guid)) return null;
            token = guid.ToString("N");
            foreach (var p in _players.Values)
                if (p.IdolPurchases.TryGetValue(token, out var receipt)) return receipt;
            return null;
        }
        private static bool IdolIdentity(GoldIdolPurchase r, long world, long payer, string token, string type) =>
            r != null && r.World == world && r.Payer == payer && r.Token == token && r.Type == type;
        internal GoldIdolPurchase FindIdol(long world, long payer, string token, string type)
        {
            lock (_gate)
            {
                if (!_loaded || _faulted || !IdolWorld(world) || !IdolToken(token)) return null;
                var r = _players.TryGetValue(payer, out var p) && p.IdolPurchases.TryGetValue(token, out var found) ? found : null;
                return IdolIdentity(r, world, payer, token, type) ? r.Copy() : null;
            }
        }
        internal GoldIdolStatus BuyIdol(long world, long payer, string token, string type)
        {
            lock (_gate)
            {
                if (!CanMutate()) return GoldIdolStatus.Unavailable;
                if (!IdolWorld(world) || payer == 0 || !IdolToken(token) || !IdolType(type)) return GoldIdolStatus.Rejected;
                var old = FindIdolToken(token);
                if (old != null) return IdolIdentity(old, world, payer, token, type) ? GoldIdolStatus.Duplicate : GoldIdolStatus.Rejected;
                int total = 0;
                foreach (var other in _players.Values)
                {
                    total += other.IdolPurchases.Count;
                    if (SameIdolToken(other.Pending?.GuidToken, token) || SameIdolToken(other.LastCommitted, token)) return GoldIdolStatus.Rejected;
                    foreach (var settled in other.SettledTokens) if (SameIdolToken(settled, token)) return GoldIdolStatus.Rejected;
                    foreach (var claim in other.Claims.Values) if (SameIdolToken(claim.Token, token)) return GoldIdolStatus.Rejected;
                    foreach (var result in other.Results.Values) if (SameIdolToken(result.Grant.Token, token)) return GoldIdolStatus.Rejected;
                }
                if (total >= MaxIdolPurchases || !_players.TryGetValue(payer, out var p) ||
                    !CanAdmit(p, GoldCooldownPolicy.LegacyPatronId, GoldIdolPurchase.PaidCost)) return GoldIdolStatus.Rejected;
                foreach (var r in p.IdolPurchases.Values)
                    if (r.Phase == GoldIdolPhase.Paid) return GoldIdolStatus.Rejected;
                p.Favor -= GoldIdolPurchase.PaidCost;
                p.IdolPurchases.Add(token, new GoldIdolPurchase { World = world, Payer = payer, Token = token, Type = type, Phase = GoldIdolPhase.Paid });
                // No exhaustion mutation, cost-tier call, generation advance or profile save.
                return PersistOrRollback() ? GoldIdolStatus.Accepted : GoldIdolStatus.Unavailable;
            }
        }
        internal GoldIdolStatus CancelIdol(long world, long payer, string token, string type, IGoldIdolEvidence proof) =>
            ChangeIdol(world, payer, token, type, "", GoldIdolPhase.Paid, GoldIdolPhase.Cancelled, GoldIdolEvidenceKind.CancelUnstarted, proof);
        internal GoldIdolStatus BindIdol(long world, long payer, string token, string type, string output, IGoldIdolEvidence proof) =>
            ChangeIdol(world, payer, token, type, output, GoldIdolPhase.Paid, GoldIdolPhase.Bound, GoldIdolEvidenceKind.BindOutput, proof);
        internal GoldIdolStatus BeginIdolDismantle(long world, long payer, string token, string type, string output, IGoldIdolEvidence proof) =>
            ChangeIdol(world, payer, token, type, output, GoldIdolPhase.Bound, GoldIdolPhase.Dismantling, GoldIdolEvidenceKind.BeginDismantle, proof);
        internal GoldIdolStatus RefundIdol(long world, long payer, string token, string type, string output, IGoldIdolEvidence proof) =>
            ChangeIdol(world, payer, token, type, output, GoldIdolPhase.Dismantling, GoldIdolPhase.Refunded, GoldIdolEvidenceKind.Destroyed, proof);

        private GoldIdolStatus ChangeIdol(long world, long payer, string token, string type, string output,
            GoldIdolPhase from, GoldIdolPhase to, GoldIdolEvidenceKind kind, IGoldIdolEvidence proof)
        {
            lock (_gate)
            {
                if (!CanMutate()) return GoldIdolStatus.Unavailable;
                if (!IdolWorld(world) || payer == 0 || !IdolToken(token) || !IdolType(type) ||
                    (to != GoldIdolPhase.Cancelled && !IdolOutput(output))) return GoldIdolStatus.Rejected;
                var r = _players.TryGetValue(payer, out var owner) && owner.IdolPurchases.TryGetValue(token, out var found) ? found : null;
                if (!IdolIdentity(r, world, payer, token, type)) return GoldIdolStatus.Rejected;
                if (r.Phase == to) return r.Output == output ? GoldIdolStatus.Duplicate : GoldIdolStatus.Rejected;
                if (r.Phase != from || (from != GoldIdolPhase.Paid && r.Output != output) || proof == null) return GoldIdolStatus.Rejected;
                var evidence = r.Copy();
                if (to == GoldIdolPhase.Bound) evidence.Output = output;
                bool verified;
                _verifyingEvidence = true;
                try { verified = proof.Verify(evidence, kind); }
                catch { verified = false; }
                finally { _verifyingEvidence = false; }
                if (!verified) return GoldIdolStatus.Rejected;
                // Callback cannot Load or mutate financial state. Verify() receives a copy.
                r.Output = output; r.Phase = to;
                if (to == GoldIdolPhase.Cancelled || to == GoldIdolPhase.Refunded)
                {
                    var p = _players[payer];
                    float entitlement = to == GoldIdolPhase.Cancelled ? GoldIdolPurchase.PaidCost : GoldIdolPurchase.DismantleRefund;
                    r.Credited = Math.Min(entitlement, 1000f - p.Favor);
                    p.Favor += r.Credited; // Cap consumes entitlement even if actual credit is zero.
                }
                return PersistOrRollback() ? GoldIdolStatus.Accepted : GoldIdolStatus.Unavailable;
            }
        }
        private static void WriteIdolPurchases(BinaryWriter w, PlayerState p)
        {
            w.Write(p.IdolPurchases.Count);
            foreach (var r in p.IdolPurchases.Values)
            {
                w.Write(r.World); w.Write(r.Payer); WriteString(w, r.Token, 32); WriteString(w, r.Type, 32);
                WriteString(w, r.Output, 80); w.Write((int)r.Phase); w.Write(r.Credited);
            }
        }
        private static bool ReadIdolPurchases(BinaryReader reader, PlayerState p)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxIdolPurchases || (count != 0 && !p.Unlocked)) return false;
            for (int i = 0; i < count; i++)
            {
                var r = new GoldIdolPurchase { World = reader.ReadInt64(), Payer = reader.ReadInt64(),
                    Token = ReadString(reader, 32), Type = ReadString(reader, 32), Output = ReadString(reader, 80),
                    Phase = (GoldIdolPhase)reader.ReadInt32(), Credited = reader.ReadSingle() };
                bool terminal = r.Phase == GoldIdolPhase.Cancelled || r.Phase == GoldIdolPhase.Refunded;
                float cap = r.Phase == GoldIdolPhase.Cancelled ? 250f : 187.5f;
                if (r.World == 0 || r.Payer != p.PlayerId || !IdolToken(r.Token) || !IdolType(r.Type) ||
                    r.Phase < GoldIdolPhase.Paid || r.Phase > GoldIdolPhase.Refunded || !Finite(r.Credited) ||
                    r.Credited < 0 || r.Credited > cap || (!terminal && r.Credited != 0) ||
                    ((r.Phase == GoldIdolPhase.Paid || r.Phase == GoldIdolPhase.Cancelled) ? r.Output != "" : !IdolOutput(r.Output)) ||
                    p.IdolPurchases.ContainsKey(r.Token)) return false;
                p.IdolPurchases.Add(r.Token, r);
            }
            return true;
        }
        private bool ValidateIdolPurchases()
        {
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            var otherTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _players.Values)
            {
                if (p.Pending != null) otherTokens.Add(p.Pending.GuidToken);
                if (!string.IsNullOrEmpty(p.LastCommitted)) otherTokens.Add(p.LastCommitted);
                foreach (var token in p.SettledTokens) otherTokens.Add(token);
                foreach (var g in p.Claims.Values) otherTokens.Add(g.Token);
                foreach (var s in p.Results.Values) otherTokens.Add(s.Grant.Token);
            }
            foreach (var p in _players.Values)
            {
                int unbound = 0;
                foreach (var r in p.IdolPurchases.Values)
                {
                    if (!IdolWorld(r.World) || otherTokens.Contains(r.Token) || !tokens.Add(r.Token) || tokens.Count > MaxIdolPurchases ||
                        (r.Phase == GoldIdolPhase.Paid && ++unbound > 1)) return false;
                }
            }
            return true;
        }
    }
}
