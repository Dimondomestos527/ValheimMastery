using System;
using System.Collections.Generic;
using System.IO;

namespace ValheimMastery
{
    internal sealed partial class GoldCraftingLedger
    {
        internal const int MaxPatrons = 16;
        internal static bool ValidPatronId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32) return false;
            foreach (char c in value) if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') &&
                !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
            return true;
        }

        private static Dictionary<string, GoldPatronWallet> NewWallets() =>
            new Dictionary<string, GoldPatronWallet>(StringComparer.Ordinal) { { GoldCooldownPolicy.LegacyPatronId, new GoldPatronWallet() } };

        internal GoldPatronWallet GetWallet(long playerId, string patron)
        {
            var state = Get(playerId);
            return state != null && ValidPatronId(patron) && state.Wallets.TryGetValue(patron, out var wallet) ? wallet : null;
        }

        // Coordinator-only: callers authenticate skill/unlock/award semantics first.
        // No RPC accepts a raw patron unlock or Favor amount.
        internal bool Unlock(long playerId, string patron)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !ValidPatronId(patron)) return false;
                var p = FindOrCreate(playerId);
                if (!p.Wallets.TryGetValue(patron, out var wallet))
                {
                    if (p.Wallets.Count >= MaxPatrons) return false;
                    p.Wallets.Add(patron, wallet = new GoldPatronWallet());
                }
                if (wallet.Unlocked) return false;
                wallet.Unlocked = true;
                return PersistOrRollback();
            }
        }

        internal bool Gain(long playerId, string patron, float amount)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !ValidPatronId(patron) || !Finite(amount) || amount <= 0 ||
                    !_players.TryGetValue(playerId, out var p) || !p.Wallets.TryGetValue(patron, out var wallet) || !wallet.Unlocked) return false;
                wallet.Favor = Math.Min(1000f, wallet.Favor + amount);
                _dirty = true;
                return true;
            }
        }

        internal bool CanStart(long playerId, string patron, float cost)
        {
            lock (_gate) return CanMutate() && _players.TryGetValue(playerId, out var p) && CanAdmit(p, patron, cost);
        }

        // Trusted server producer only. Freeze one accomplishment's personal split
        // before calling. Source/sequence is a durable publisher identity, NOT XP,
        // time or a sequence minted again on RPC retry. No client RPC is installed.
        // Receipt and ALL wallet increments share one durable Gold outcome.
        internal GoldAwardResult Award(long playerId, string source, long sequence, IReadOnlyDictionary<string, float> split)
        {
            // Snapshot external enumeration BEFORE capturing authoritative state.
            // Even a custom enumerable cannot detach a captured player via Load().
            if (playerId == 0 || !ValidPatronId(source) || sequence <= 0 || split == null) return GoldAwardResult.Rejected;
            double total = 0;
            var frozenSplit = new Dictionary<string, float>(StringComparer.Ordinal);
            try
            {
                if (split.Count == 0 || split.Count > MaxPatrons) return GoldAwardResult.Rejected;
                foreach (var pair in split)
                {
                    if (!ValidPatronId(pair.Key) || !Finite(pair.Value) || pair.Value <= 0 || pair.Value > 1000 ||
                        frozenSplit.Count >= MaxPatrons || frozenSplit.ContainsKey(pair.Key)) return GoldAwardResult.Rejected;
                    total += pair.Value; frozenSplit.Add(pair.Key, pair.Value);
                }
            }
            catch { return GoldAwardResult.Rejected; }
            if (frozenSplit.Count == 0 || total > 1000) return GoldAwardResult.Rejected;
            lock (_gate)
            {
                if (!CanMutate()) return GoldAwardResult.Unavailable;
                if (!_players.TryGetValue(playerId, out var p)) return GoldAwardResult.Rejected;
                foreach (var pair in frozenSplit)
                    if (!p.Wallets.TryGetValue(pair.Key, out var wallet) || !wallet.Unlocked) return GoldAwardResult.Rejected;
                bool hasSource = p.PatronAwardSources.TryGetValue(source, out var track);
                if (!hasSource && p.PatronAwardSources.Count >= MaxBackgroundStations) return GoldAwardResult.Rejected;
                long high = hasSource ? track.High : 0;
                ulong seen = hasSource ? track.Seen : 0;
                if (high == 0) { high = sequence; seen = 1; }
                else if (sequence > high)
                {
                    long shift = sequence - high;
                    seen = shift >= 64 ? 1UL : (seen << (int)shift) | 1UL; high = sequence;
                }
                else
                {
                    long offset = high - sequence;
                    if (offset >= 64) return GoldAwardResult.Expired;
                    ulong bit = 1UL << (int)offset;
                    if ((seen & bit) != 0) return GoldAwardResult.Duplicate;
                    seen |= bit;
                }
                foreach (var pair in frozenSplit)
                {
                    var wallet = p.Wallets[pair.Key]; wallet.Favor = Math.Min(1000f, wallet.Favor + pair.Value);
                }
                if (!hasSource) p.PatronAwardSources.Add(source, track = new BackgroundSequence());
                track.High = high; track.Seen = seen;
                // Capped/zero actual gain still consumes the award: no overflow bank.
                return PersistOrRollback() ? GoldAwardResult.Accepted : GoldAwardResult.Unavailable;
            }
        }

        // Availability/fences only, for arming without requiring current funds.
        internal bool CooldownAllows(long playerId, string patron, float cost)
        {
            lock (_gate) return CanMutate() && _players.TryGetValue(playerId, out var p) && GatesAllow(p, patron, cost);
        }

        private static float Held(PlayerState p, string patron)
        {
            float held = patron == GoldCooldownPolicy.LegacyPatronId && p.Pending != null ? p.Pending.Cost : 0f;
            if (p.Claims.TryGetValue(patron, out var claim)) held += claim.Cost;
            return held;
        }

        private static bool Conflict(string patron, float cost, string pendingPatron, float pendingCost) =>
            patron == pendingPatron || pendingCost > 250f && cost > 250f;

        private static bool GatesAllow(PlayerState p, string patron, float cost)
        {
            if (!ValidPatronId(patron) || !Finite(cost) || cost < 0 || cost > 1000 ||
                !GoldCooldownPolicy.CanStart(patron, cost, p.ExhaustedPatron, p.ExhaustionRemaining)) return false;
            if (p.CoordinatorPins.ContainsKey(patron)) return false;
            if (p.Pending != null && Conflict(patron, cost, GoldCooldownPolicy.LegacyPatronId, p.Pending.Cost)) return false;
            foreach (var claim in p.Claims.Values)
                if (Conflict(patron, cost, claim.Patron, claim.Cost)) return false;
            return true;
        }

        private static bool CanAdmit(PlayerState p, string patron, float cost) => GatesAllow(p, patron, cost) &&
            p.Wallets.TryGetValue(patron, out var wallet) && wallet.Unlocked && wallet.Favor - Held(p, patron) >= cost;

        private static bool ValidAction(GoldPatronGrant g) => g != null && ValidPatronId(g.Patron) &&
            ValidPatronId(g.Action) && ValidToken(g.Token) && g.Generation > 0 && Finite(g.Cost) &&
            g.Cost >= 0 && g.Cost <= 1000 && (!g.Prepared || g.Cost > 250);

        // Manual grants can await authority. Prepared grants are ONLY accounting
        // and conflict fences; this method does not authorize native lethal protection.
        internal bool ReserveAction(long playerId, GoldPatronGrant grant, bool requireCoordinatorAck = false)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !ValidAction(grant) || !_players.TryGetValue(playerId, out var p)) return false;
                if (requireCoordinatorAck && grant.Prepared) return false;
                if (FindIdolToken(grant.Token) != null) return false;
                if (p.Claims.TryGetValue(grant.Patron, out var pending))
                    return pending.Same(grant) && (!requireCoordinatorAck || PinAction(playerId, grant));
                if (p.Results.TryGetValue(grant.Patron, out var result) && result.Grant.Same(grant)) return false; // terminal, not a new grant
                if (p.Generation == long.MaxValue || grant.Generation != p.Generation + 1 || !CanAdmit(p, grant.Patron, grant.Cost)) return false;
                // GUID identity is independent of sequence; never reuse retained legacy/current evidence.
                if (WasSettled(p, grant.Token) || p.Pending?.GuidToken == grant.Token) return false;
                foreach (var other in p.Claims.Values) if (other.Token == grant.Token) return false;
                foreach (var other in p.Results.Values) if (other.Grant.Token == grant.Token) return false;
                p.Generation = grant.Generation;
                p.Claims.Add(grant.Patron, grant.Copy());
                if (requireCoordinatorAck) p.CoordinatorPins.Add(grant.Patron, grant.Copy());
                return PersistOrRollback();
            }
        }

        internal bool SettleAction(long playerId, GoldPatronGrant grant, GoldPatronOutcome outcome,
            IGoldPreparedEvidence evidence = null)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !ValidAction(grant) ||
                    (outcome != GoldPatronOutcome.Committed && outcome != GoldPatronOutcome.Unused) ||
                    !_players.TryGetValue(playerId, out var p)) return false;
                if (p.Results.TryGetValue(grant.Patron, out var result) && result.Grant.Same(grant)) return result.Outcome == outcome;
                if (p.CoordinatorPins.TryGetValue(grant.Patron, out var pin) && !pin.Same(grant)) return false;
                if (!p.Claims.TryGetValue(grant.Patron, out var pending) || !pending.Same(grant)) return false;
                if (pending.Prepared)
                {
                    // Fail closed until a separately reviewed durable owner-journal verifier exists.
                    if (evidence == null) return false;
                    _verifyingEvidence = true;
                    try { if (!evidence.Verify(playerId, pending.Copy(), outcome)) return false; }
                    catch { return false; }
                    finally { _verifyingEvidence = false; }
                }
                var wallet = p.Wallets[pending.Patron];
                if (wallet.Favor < pending.Cost) return false;
                if (outcome == GoldPatronOutcome.Committed)
                {
                    wallet.Favor -= pending.Cost;
                    p.ExhaustionRemaining = GoldCooldownPolicy.RemainingAfterCommit(pending.Cost, p.ExhaustionRemaining);
                    if (pending.Cost > 250) p.ExhaustedPatron = pending.Patron;
                }
                p.Claims.Remove(pending.Patron);
                p.Results[pending.Patron] = new GoldPatronResult { Grant = pending.Copy(), Outcome = outcome };
                return PersistOrRollback();
            }
        }

        // Recovery upgrade for an EXACT already admitted manual claim/result only.
        // Cannot recover an overwritten result or authorize a new grant.
        internal bool PinAction(long playerId, GoldPatronGrant grant)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !ValidAction(grant) || grant.Prepared ||
                    !_players.TryGetValue(playerId, out var p)) return false;
                if (p.CoordinatorPins.TryGetValue(grant.Patron, out var pin)) return pin.Same(grant);
                bool exactClaim = p.Claims.TryGetValue(grant.Patron, out var claim) && claim.Same(grant);
                bool exactResult = p.Results.TryGetValue(grant.Patron, out var result) && result.Grant.Same(grant);
                if (!exactClaim && (!exactResult || p.Claims.ContainsKey(grant.Patron))) return false;
                if (grant.Patron == GoldCooldownPolicy.LegacyPatronId && p.Pending != null) return false;
                p.CoordinatorPins.Add(grant.Patron, grant.Copy());
                return PersistOrRollback();
            }
        }

        // Trusted coordinator calls ONLY AFTER its own durable financial outcome.
        // This is NOT recipient/effect-applied ACK and does not itself prove that save.
        internal bool AcknowledgeAction(long playerId, GoldPatronGrant grant, GoldPatronOutcome outcome)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !ValidAction(grant) || grant.Prepared ||
                    (outcome != GoldPatronOutcome.Committed && outcome != GoldPatronOutcome.Unused) ||
                    !_players.TryGetValue(playerId, out var p) ||
                    !p.Results.TryGetValue(grant.Patron, out var result) || !result.Grant.Same(grant) || result.Outcome != outcome) return false;
                if (!p.CoordinatorPins.TryGetValue(grant.Patron, out var pin)) return true; // exact terminal duplicate, no mutation
                if (!pin.Same(grant) || p.Claims.ContainsKey(grant.Patron)) return false;
                p.CoordinatorPins.Remove(grant.Patron);
                return PersistOrRollback();
            }
        }

        private static void WriteCoordinatorPins(BinaryWriter w, PlayerState p)
        {
            w.Write(p.CoordinatorPins.Count);
            foreach (var pin in p.CoordinatorPins.Values) WriteGrant(w, pin);
        }

        private static bool ReadCoordinatorPins(BinaryReader r, PlayerState p)
        {
            int count = r.ReadInt32();
            if (count < 0 || count > MaxPatrons) return false;
            for (int i = 0; i < count; i++)
            {
                var pin = ReadGrant(r);
                if (!ValidAction(pin) || pin.Prepared || p.CoordinatorPins.ContainsKey(pin.Patron)) return false;
                bool exactClaim = p.Claims.TryGetValue(pin.Patron, out var claim) && claim.Same(pin);
                bool exactResult = p.Results.TryGetValue(pin.Patron, out var result) && result.Grant.Same(pin);
                if (!exactClaim && (!exactResult || p.Claims.ContainsKey(pin.Patron))) return false;
                if (pin.Patron == GoldCooldownPolicy.LegacyPatronId && p.Pending != null) return false;
                p.CoordinatorPins.Add(pin.Patron, pin);
            }
            return true;
        }

        private static void WriteGrant(BinaryWriter w, GoldPatronGrant g)
        {
            WriteString(w, g.Patron, 32); WriteString(w, g.Action, 32); WriteString(w, g.Token, 32);
            w.Write(g.Cost); w.Write(g.Generation); w.Write(g.Prepared);
        }

        private static GoldPatronGrant ReadGrant(BinaryReader r) => new GoldPatronGrant {
            Patron = ReadString(r, 32), Action = ReadString(r, 32), Token = ReadString(r, 32),
            Cost = r.ReadSingle(), Generation = r.ReadInt64(), Prepared = r.ReadBoolean() };

        private static void WritePatrons(BinaryWriter w, PlayerState p)
        {
            WriteString(w, p.ExhaustedPatron, 32); w.Write(p.Generation);
            w.Write(p.Wallets.Count - 1);
            foreach (var pair in p.Wallets)
            {
                if (pair.Key == GoldCooldownPolicy.LegacyPatronId) continue;
                WriteString(w, pair.Key, 32); w.Write(pair.Value.Unlocked); w.Write(pair.Value.Favor);
            }
            w.Write(p.Claims.Count); foreach (var claim in p.Claims.Values) WriteGrant(w, claim);
            w.Write(p.Results.Count);
            foreach (var result in p.Results.Values) { WriteGrant(w, result.Grant); w.Write((int)result.Outcome); }
            w.Write(p.PatronAwardSources.Count);
            foreach (var source in p.PatronAwardSources)
            { WriteString(w, source.Key, 32); w.Write(source.Value.High); w.Write(source.Value.Seen); }
        }

        private static bool ReadPatrons(BinaryReader r, PlayerState p)
        {
            p.ExhaustedPatron = ReadString(r, 32); p.Generation = r.ReadInt64();
            if (p.Generation < 0) return false;
            int wallets = r.ReadInt32(); if (wallets < 0 || wallets >= MaxPatrons) return false;
            for (int i = 0; i < wallets; i++)
            {
                string patron = ReadString(r, 32);
                var wallet = new GoldPatronWallet { Unlocked = r.ReadBoolean(), Favor = r.ReadSingle() };
                if (!ValidPatronId(patron) || p.Wallets.ContainsKey(patron) || !Finite(wallet.Favor) ||
                    wallet.Favor < 0 || wallet.Favor > 1000 || !wallet.Unlocked) return false;
                p.Wallets.Add(patron, wallet);
            }
            if (p.ExhaustionRemaining > 0 ? !ValidPatronId(p.ExhaustedPatron) ||
                !p.Wallets.TryGetValue(p.ExhaustedPatron, out var origin) || !origin.Unlocked : p.ExhaustedPatron != string.Empty) return false;
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            var generations = new HashSet<long>();
            if (p.Pending != null) tokens.Add(p.Pending.GuidToken);
            int claims = r.ReadInt32(); if (claims < 0 || claims > MaxPatrons) return false;
            for (int i = 0; i < claims; i++)
            {
                var claim = ReadGrant(r);
                if (!ValidAction(claim) || claim.Generation > p.Generation || !generations.Add(claim.Generation) ||
                    !tokens.Add(claim.Token) || WasSettled(p, claim.Token) || !p.Wallets.TryGetValue(claim.Patron, out var wallet) ||
                    !wallet.Unlocked || !CanAdmit(p, claim.Patron, claim.Cost)) return false;
                p.Claims.Add(claim.Patron, claim);
            }
            int results = r.ReadInt32(); if (results < 0 || results > MaxPatrons) return false;
            for (int i = 0; i < results; i++)
            {
                var grant = ReadGrant(r); var outcome = (GoldPatronOutcome)r.ReadInt32();
                if (!ValidAction(grant) || grant.Generation > p.Generation || !generations.Add(grant.Generation) ||
                    !tokens.Add(grant.Token) || WasSettled(p, grant.Token) || p.Results.ContainsKey(grant.Patron) ||
                    !p.Wallets.TryGetValue(grant.Patron, out var wallet) || !wallet.Unlocked ||
                    (outcome != GoldPatronOutcome.Committed && outcome != GoldPatronOutcome.Unused)) return false;
                p.Results.Add(grant.Patron, new GoldPatronResult { Grant = grant, Outcome = outcome });
            }
            // Committed expensive claims cannot coexist with conflicting outstanding claims.
            if (p.Pending != null && !GoldCooldownPolicy.CanStart(GoldCooldownPolicy.LegacyPatronId,
                p.Pending.Cost, p.ExhaustedPatron, p.ExhaustionRemaining)) return false;
            int sources = r.ReadInt32(); if (sources < 0 || sources > MaxBackgroundStations) return false;
            for (int i = 0; i < sources; i++)
            {
                string source = ReadString(r, 32); long high = r.ReadInt64(); ulong seen = r.ReadUInt64();
                if (!ValidPatronId(source) || p.PatronAwardSources.ContainsKey(source) || high <= 0 ||
                    (seen & 1UL) == 0 || high < 64 && (seen >> (int)high) != 0) return false;
                p.PatronAwardSources.Add(source, new BackgroundSequence { High = high, Seen = seen });
            }
            return true;
        }
    }
}
