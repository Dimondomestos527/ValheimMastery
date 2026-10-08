using System;

namespace ValheimMastery
{
    // Server-owned completed encounter -> journal -> Gold bridge. No client award RPC.
    // Native target/participant authority MUST be established by the caller; this
    // component deliberately does not subscribe to raw XP or kill/hit events.
    internal sealed class Combat100FavorService : IDisposable
    {
        internal enum Delivery { NotReady, NoPending, Retry, Recorded, Expired }
        private readonly ZNet _session;
        private readonly long _world;
        private readonly GoldCraftingLedger _ledger;
        private readonly Combat100StateJournal _journal;
        private bool _busy, _disposed;
        private readonly int _thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        internal Combat100FavorService(ZNet session, GoldCraftingLedger ledger, Combat100StateJournal journal)
        {
            if (session == null || ledger == null || journal == null) throw new ArgumentNullException();
            _session = session; _world = session.GetWorldUID(); _ledger = ledger; _journal = journal;
            if (_world == 0 || journal.WorldUid != _world ||
                !GoldCraftingService.TryGetServerLedger(session, _world, out var currentLedger) || !ReferenceEquals(currentLedger, ledger)) throw new ArgumentException("Combat journal world mismatch.");
        }
        private bool Current() => System.Threading.Thread.CurrentThread.ManagedThreadId == _thread && !_disposed && MasteryPlugin.Settings?.Enabled.Value == true &&
            ReferenceEquals(ZNet.instance, _session) && _session.IsServer() && _session.GetWorldUID() == _world &&
            GoldCraftingService.TryGetServerLedger(_session, _world, out var currentLedger) &&
            ReferenceEquals(currentLedger, _ledger) && _ledger.IsAvailable && _journal.IsAvailable;
        // Durable freeze precedes ANY wallet call, even for zero qualifying recipients.
        // The same closed model returns the same immutable rewards on storage retry.
        internal bool StoreCompleted(Combat100FavorModel authoritativeEncounter, double now)
        {
            if (_busy || authoritativeEncounter == null || !Current()) return false;
            _busy = true;
            try
            {
                if (!authoritativeEncounter.TryComplete(now, out var rewards) || !Current()) return false;
                return _journal.Prepare(authoritativeEncounter.EncounterId, rewards, out _);
            }
            finally { _busy = false; }
        }
        // At most ONE synchronous award is in flight. Rejected/Unavailable do not
        // advance the personal lane; even >64 queued events cannot evict its receipt.
        internal Delivery Deliver(long playerId)
        {
            if (_busy || !Current()) return Delivery.NotReady;
            _busy = true;
            try
            {
                var award = _journal.Peek(playerId);
                if (award == null) return Delivery.NoPending;
                if (!CaptureBinding(playerId, out var character, out var owner)) return Delivery.NotReady;
                var split = award.Split();
                if (!Current() || !CaptureBinding(playerId, out var currentCharacter, out var currentOwner) ||
                    character != currentCharacter || owner != currentOwner) return Delivery.NotReady;
                GoldAwardResult result = _ledger.Award(playerId, Combat100StateJournal.AwardSource, award.Sequence, split);
                // A changed/faulted authority is not an acknowledgment. The original
                // durable record survives and reconciles by the SAME Gold sequence.
                if (!Current()) return Delivery.Retry;
                Combat100StateJournal.Outcome outcome;
                switch (result)
                {
                    case GoldAwardResult.Accepted: outcome = Combat100StateJournal.Outcome.Accepted; break;
                    case GoldAwardResult.Duplicate: outcome = Combat100StateJournal.Outcome.Duplicate; break;
                    case GoldAwardResult.Expired: outcome = Combat100StateJournal.Outcome.Expired; break;
                    default: return Delivery.Retry;
                }
                if (!_journal.Acknowledge(playerId, award.EncounterId, award.Sequence, outcome)) return Delivery.Retry;
                if (result == GoldAwardResult.Expired) return Delivery.Expired;
                // Financial/journal outcome is durable before best-effort UI delivery.
                try { GoldCraftingService.NotifyFavor(playerId); }
                catch (Exception e) { MasteryPlugin.Log.LogWarning("[Combat100] Favor snapshot delivery failed: " + e.Message); }
                return Delivery.Recorded;
            }
            finally { _busy = false; }
        }
        private bool CaptureBinding(long playerId, out ZDOID character, out long owner)
        {
            character = ZDOID.None; owner = 0;
            if (playerId == 0 || !Current()) return false;
            var local = Player.m_localPlayer;
            if (local?.m_nview?.IsOwner() == true && local.GetPlayerID() == playerId)
            {
                var zdo = local.m_nview.GetZDO();
                if (zdo == null || zdo.GetLong(ZDOVars.s_playerID, 0) != playerId) return false;
                character = zdo.m_uid; owner = zdo.GetOwner(); return character != ZDOID.None && owner != 0;
            }
            foreach (var peer in _session.GetPeers())
            {
                if (peer == null || !peer.IsReady()) continue;
                var zdo = OwnerSkillAuthority.ResolveCharacterData(peer);
                if (zdo == null || zdo.GetLong(ZDOVars.s_playerID, 0) != playerId) continue;
                character = zdo.m_uid; owner = zdo.GetOwner();
                return character != ZDOID.None && owner == peer.m_uid && owner != 0;
            }
            return false;
        }
        public void Dispose()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != _thread)
                throw new InvalidOperationException("Combat coordinator must remain on its owning Unity thread.");
            // No callbacks run in the journal; callers shut down admission first.
            // A synchronous delivery cannot be disposed by a reentrant UI callback.
            if (_busy) throw new InvalidOperationException("Combat delivery is in progress.");
            if (_disposed) return;
            _disposed = true; _journal.Dispose();
        }
    }
}

