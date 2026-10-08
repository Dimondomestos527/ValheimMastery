using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    // No Unity dependency: the state machine is exercised with real failing stores
    // in QC. Runtime adapters must execute synchronously on the world-owner thread.
    internal interface IWorkshopDebitStore
    {
        object Identity { get; }
        int Count(string item);
        object Capture();
        void Remove(string item, int amount);
        void Restore(object snapshot);
        void Flush();
    }

    internal sealed class WorkshopDebitLine
    {
        internal IWorkshopDebitStore Store;
        internal string Item;
        internal int Amount;
    }

    internal sealed class WorkshopAtomicDebit : IDisposable
    {
        private static readonly HashSet<object> Locked = new HashSet<object>();
        private readonly List<IWorkshopDebitStore> _stores = new List<IWorkshopDebitStore>();
        private readonly Dictionary<object, object> _snapshots = new Dictionary<object, object>();
        private bool _closed;
        private bool _mutated;
        internal static bool IsLocked(object identity) => identity != null && Locked.Contains(identity);
        // Only the recovery adapter may call this, after validating every escrow
        // snapshot and restoring/saving the entire participant set.
        internal static void ReleaseRecovered(object identity) { if (identity != null) Locked.Remove(identity); }

        internal static WorkshopAtomicDebit Begin(IList<WorkshopDebitLine> lines, Func<bool> validate)
        {
            if (lines == null || lines.Count == 0 || validate == null) return null;
            var transaction = new WorkshopAtomicDebit();
            // Aggregate duplicate lines by stable storage identity before validation.
            var totals = new Dictionary<object, Dictionary<string, int>>();
            foreach (WorkshopDebitLine line in lines)
            {
                if (line?.Store?.Identity == null || string.IsNullOrEmpty(line.Item) || line.Amount <= 0) return null;
                object key = line.Store.Identity;
                if (!totals.TryGetValue(key, out var items))
                {
                    totals.Add(key, items = new Dictionary<string, int>(StringComparer.Ordinal));
                    transaction._stores.Add(line.Store);
                }
                items.TryGetValue(line.Item, out int amount);
                if (amount > int.MaxValue - line.Amount) return null;
                items[line.Item] = amount + line.Amount;
            }
            foreach (var store in transaction._stores)
                if (Locked.Contains(store.Identity)) return null;
            foreach (var store in transaction._stores) Locked.Add(store.Identity);
            try
            {
                if (!validate()) { transaction.Release(); return null; }
                foreach (var store in transaction._stores)
                    foreach (var item in totals[store.Identity])
                        if (store.Count(item.Key) < item.Value) { transaction.Release(); return null; }
                // Capture ALL stores before the first mutation, including when a
                // later Capture fails. A failing preflight must remove nothing.
                foreach (var store in transaction._stores)
                    transaction._snapshots.Add(store.Identity, store.Capture());
                transaction._mutated = true;
                foreach (var store in transaction._stores)
                    foreach (var item in totals[store.Identity])
                    {
                        int before = store.Count(item.Key);
                        store.Remove(item.Key, item.Value);
                        if (store.Count(item.Key) != before - item.Value)
                            throw new InvalidOperationException("Workshop debit did not remove the exact required amount.");
                    }
                foreach (var store in transaction._stores) store.Flush();
                return transaction;
            }
            catch
            {
                transaction.Dispose();
                throw;
            }
        }

        // Call only after the actual action output has been confirmed. Calling
        // Commit twice is harmless; Dispose after Commit can never refund.
        internal void Commit() { if (!_closed) Release(); }

        public void Dispose()
        {
            if (_closed) return;
            var failures = new List<Exception>();
            if (_mutated)
            {
                // Restore every store even when one fails. A failed restore keeps
                // its identity locked: silently unlocking uncertain stock is unsafe.
                for (int i = _stores.Count - 1; i >= 0; --i)
                {
                    var store = _stores[i];
                    try
                    {
                        store.Restore(_snapshots[store.Identity]);
                        store.Flush();
                        Locked.Remove(store.Identity);
                    }
                    catch (Exception error) { failures.Add(error); }
                }
                _closed = true;
                if (failures.Count != 0) throw new AggregateException("Workshop rollback failed; affected storage remains locked.", failures);
            }
            else Release();
        }

        private void Release()
        {
            foreach (var store in _stores) Locked.Remove(store.Identity);
            _closed = true;
        }
    }
}
