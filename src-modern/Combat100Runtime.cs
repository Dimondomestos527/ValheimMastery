using System;
namespace ValheimMastery
{
    // Paid-action engine. Native ingress must authenticate current actor/weapon,
    // skill, original request and roster BEFORE Start. No RPC/input/effect call
    // exists here. Entitlement is published only after BOTH durable commits.
    internal sealed class Combat100MusterCoordinator
    {
        internal enum Result { NotReady, Denied, Retry, Committed, Cancelled, Finished, Unknown }
        private readonly ZNet _session;
        private readonly long _world;
        private readonly GoldCraftingLedger _ledger;
        private readonly Combat100ActionJournal _journal;
        private readonly Func<long> _utc;
        private readonly int _thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        private bool _busy;
        internal Combat100MusterCoordinator(ZNet session, GoldCraftingLedger ledger, Combat100ActionJournal journal, Func<long> utc = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _world = session.GetWorldUID(); _utc = utc ?? (() => DateTime.UtcNow.Ticks);
            if (_world == 0 || journal.WorldUid != _world || !Current(false)) throw new ArgumentException("Current loaded Gold/action world required.");
        }
        private bool Current(bool fresh) => System.Threading.Thread.CurrentThread.ManagedThreadId == _thread &&
            _journal.IsAvailable && (fresh ? GoldCraftingService.TryGetServerLedger(_session, _world, out var ledger) :
            GoldCraftingService.TryGetRecoveryLedger(_session, _world, out ledger)) && ReferenceEquals(ledger, _ledger);
        internal Result Start(long player, long owner, string character, string epoch, string originalRequest,
            Combat100ActionJournal.Recipient[] authenticatedSnapshot)
        {
            if (_busy || !Current(true)) return Result.NotReady;
            if (!Guid.TryParseExact(originalRequest, "N", out _) || !Guid.TryParseExact(epoch, "N", out _)) return Result.Denied;
            _busy = true;
            try
            {
                var existing = _journal.Get(originalRequest);
                if (existing != null)
                    return existing.PlayerId == player && existing.Owner == owner && existing.Character == character && existing.Epoch == epoch ?
                        Advance(existing, false) : Result.Denied;
                var pinnedState = _ledger.Get(player);
                if (pinnedState != null && pinnedState.CoordinatorPins.TryGetValue("Odin", out var pinnedGrant))
                {
                    var pinnedRecord = _journal.Get(pinnedGrant.Token);
                    if (pinnedRecord != null && pinnedRecord.PlayerId == player && pinnedRecord.Action == Combat100ActionJournal.Kind.Muster && pinnedRecord.Grant.Same(pinnedGrant))
                    {
                        var recovered = Advance(pinnedRecord, false);
                        if (recovered == Result.Retry || recovered == Result.Unknown || recovered == Result.NotReady) return Result.Retry;
                    }
                }
                foreach (var pending in _journal.Pending())
                    if (pending.PlayerId == player && pending.Action == Combat100ActionJournal.Kind.Muster)
                    {
                        var recovered = Advance(pending, false);
                        if (recovered != Result.Finished && recovered != Result.Cancelled) return Result.Retry;
                    }
                var state = _ledger.Get(player);
                if (state == null || state.Generation == long.MaxValue || !_ledger.CanStart(player, "Odin", 750)) return Result.Denied;
                long now = _utc();
                var plan = new Combat100ActionJournal.Record {
                    Action = Combat100ActionJournal.Kind.Muster, State = Combat100ActionJournal.Phase.Planned,
                    PlayerId = player, Owner = owner, Character = character, Epoch = epoch, CreatedUtcTicks = now,
                    DeadlineUtcTicks = checked(now + TimeSpan.FromSeconds(300).Ticks), Recipients = authenticatedSnapshot,
                    Grant = new GoldPatronGrant { Patron = "Odin", Action = "OdinMuster", Token = originalRequest,
                        Cost = 750, Prepared = false, Generation = state.Generation + 1 }
                };
                if (!_journal.Prepare(plan)) return Result.Retry;
                // Reload immutable journal copy; never use caller-owned roster after freeze.
                return Advance(_journal.Get(originalRequest), true);
            }
            finally { _busy = false; }
        }
        internal Result Recover(string token)
        {
            if (_busy || !Current(false)) return Result.NotReady;
            _busy = true;
            try { return Advance(_journal.Get(token), false); }
            finally { _busy = false; }
        }
        private Result Advance(Combat100ActionJournal.Record record, bool initial)
        {
            if (record == null || record.Action != Combat100ActionJournal.Kind.Muster) return Result.Denied;
            if (record.State == Combat100ActionJournal.Phase.Abandoned) return Result.Cancelled;
            if (record.State == Combat100ActionJournal.Phase.Cancelled)
                return CleanPin(record, GoldPatronOutcome.Unused) ? Result.Cancelled : Result.Retry;
            if (record.State == Combat100ActionJournal.Phase.Finished)
                return CleanPin(record, GoldPatronOutcome.Committed) ? Result.Finished : Result.Retry;
            if (!Current(false) || _utc() < record.CreatedUtcTicks) return Result.NotReady;
            if (record.State == Combat100ActionJournal.Phase.Committed)
            {
                if (!CleanPin(record, GoldPatronOutcome.Committed)) return Result.Retry;
                if (_utc() >= record.DeadlineUtcTicks) return _journal.Finish(record.Grant.Token) ? Result.Finished : Result.Retry;
                return Result.Committed;
            }
            var state = _ledger.Get(record.PlayerId);
            if (state == null || !_ledger.IsAvailable) return Result.Retry;
            bool hasResult = state.Results.TryGetValue("Odin", out var settled) && settled.Grant.Same(record.Grant);
            bool hasClaim = state.Claims.TryGetValue("Odin", out var claim) && claim.Same(record.Grant);
            if (hasResult)
            {
                if (!_ledger.PinAction(record.PlayerId, record.Grant) || !Current(false)) return Result.Retry;
                if (settled.Outcome == GoldPatronOutcome.Unused)
                    return CancelPaid(record);
                if (settled.Outcome != GoldPatronOutcome.Committed) return Result.Unknown;
                return PublishCommit(record);
            }
            if (!hasClaim)
            {
                if (record.State != Combat100ActionJournal.Phase.Planned) return Result.Unknown;
                if (!initial || _utc() >= record.DeadlineUtcTicks || !Current(true))
                    return state.Generation < record.Grant.Generation && Current(false) ?
                        (_journal.CancelMuster(record.Grant.Token, true) ? Result.Cancelled : Result.Retry) : Result.Unknown;
                if (!Current(true)) return Result.NotReady;
                if (!_ledger.ReserveAction(record.PlayerId, record.Grant, requireCoordinatorAck: true))
                {
                    // Failed storage may have persisted. Never infer no claim from false.
                    if (!Current(false) || !_ledger.IsAvailable) return Result.Retry;
                    var after = _ledger.Get(record.PlayerId);
                    return after != null && after.Generation < record.Grant.Generation ?
                        (_journal.CancelMuster(record.Grant.Token, true) ? Result.Cancelled : Result.Retry) : Result.Unknown;
                }
                if (!Current(false)) return Result.Retry;
                hasClaim = true;
            }
            if (!_ledger.PinAction(record.PlayerId, record.Grant) || !Current(false)) return Result.Retry;
            if (record.State == Combat100ActionJournal.Phase.Planned)
            {
                if (!_journal.MarkReserved(record.Grant.Token)) return Result.Retry;
                record = _journal.Get(record.Grant.Token);
            }
            if (record.State != Combat100ActionJournal.Phase.Reserved) return Result.Unknown;
            if (_utc() >= record.DeadlineUtcTicks)
            {
                // No effect has ever been disclosed in Planned/Reserved.
                if (!Current(false) || !_ledger.SettleAction(record.PlayerId, record.Grant, GoldPatronOutcome.Unused)) return Result.Retry;
                return Current(false) ? CancelPaid(record) : Result.Retry;
            }
            if (!Current(true)) return Result.NotReady; // Core OFF: hold admitted request, no new charge/effect.
            if (!_ledger.SettleAction(record.PlayerId, record.Grant, GoldPatronOutcome.Committed)) return Result.Retry;
            if (!Current(false)) return Result.Retry;
            return PublishCommit(record);
        }
        private Result PublishCommit(Combat100ActionJournal.Record record)
        {
            if (!Current(false)) return Result.Retry;
            if (record.State == Combat100ActionJournal.Phase.Planned && !_journal.MarkReserved(record.Grant.Token)) return Result.Retry;
            if (!_journal.MarkCommitted(record.Grant.Token)) return Result.Retry;
            if (!CleanPin(record, GoldPatronOutcome.Committed)) return Result.Retry;
            return _utc() >= record.DeadlineUtcTicks ? (_journal.Finish(record.Grant.Token) ? Result.Finished : Result.Retry) : Result.Committed;
        }
        private Result CancelPaid(Combat100ActionJournal.Record record)
        {
            if (!_journal.CancelMuster(record.Grant.Token)) return Result.Retry;
            return CleanPin(record, GoldPatronOutcome.Unused) ? Result.Cancelled : Result.Retry;
        }
        private bool CleanPin(Combat100ActionJournal.Record record, GoldPatronOutcome outcome)
        {
            if (!Current(false)) return false;
            var state = _ledger.Get(record.PlayerId);
            if (state == null) return false;
            if (!state.CoordinatorPins.TryGetValue(record.Grant.Patron, out var pin)) return true;
            // An already ACKed old durable journal cannot clear a later action pin.
            if (!pin.Same(record.Grant)) return pin.Token != record.Grant.Token;
            return _ledger.AcknowledgeAction(record.PlayerId, record.Grant, outcome) && Current(false);
        }
        internal Combat100ActionJournal.Record Delivery(string token)
        {
            if (_busy || !Current(true)) return null;
            var record = _journal.Get(token);
            return record?.Action == Combat100ActionJournal.Kind.Muster && record.State == Combat100ActionJournal.Phase.Committed &&
                _utc() >= record.CreatedUtcTicks && _utc() < record.DeadlineUtcTicks ? record : null;
        }
    }
}


