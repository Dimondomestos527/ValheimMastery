using System.Linq;

namespace ValheimMastery
{
    // Server CAS/state primitives. Intentionally not wired to deployment until
    // the close/freeze/ownership-ACK and native writer guards are integrated.
    internal static class CrossbowTurretCustody
    {
        internal const string VersionKey = "vm.crossbow70.version";
        internal const string OwnerKey = "vm.crossbow70.owner";
        internal const string StateKey = "vm.crossbow70.state";
        internal const string RevisionKey = "vm.crossbow70.revision";
        internal const string QuarantineKey = "vm.crossbow70.quarantine";
        internal static bool Marked(ZDO record) => record?.GetInt(VersionKey, 0) == 1;
        internal static CrossbowTurretState State(ZDO record)
            => Marked(record) ? (CrossbowTurretState)record.GetInt(StateKey, 0) : CrossbowTurretState.Unmarked;
        internal static bool CanWrite(ZDO record, int revision, byte[] expected)
            => ZNet.instance?.IsServer() == true && record != null &&
                ReferenceEquals(ZDOMan.instance?.GetZDO(record.m_uid), record) && Marked(record) &&
                !record.GetBool(QuarantineKey, false) &&
                record.GetLong(OwnerKey, 0) != 0 && record.GetOwner() == ZNet.GetUID() &&
                record.GetInt(RevisionKey, -1) == revision && expected != null &&
                expected.Length > 0 && expected.Length <= CrossbowTurretRules.MaximumSnapshotBytes &&
                expected.SequenceEqual(record.GetByteArray(ZDOVars.s_items, null) ?? new byte[0]);
        internal static bool AnotherActive(long ownerId, ZDOID except)
        {
            // Activation-only scan includes UNLOADED native world records; never a per-frame target scan.
            if (ZDOMan.instance == null || ownerId == 0) return true;
            foreach (var pair in ZDOMan.instance.m_objectsByID)
                if (pair.Key != except && Marked(pair.Value) && pair.Value.GetLong(OwnerKey, 0) == ownerId &&
                    CrossbowTurretRules.LocksStock(State(pair.Value))) return true;
            return false;
        }
        internal static bool TryTransition(ZDO record, int revision, byte[] expected, CrossbowTurretState next,
            Container definition, out string reason)
        {
            reason = "custody_changed";
            if (!CanWrite(record, revision, expected) || revision == int.MaxValue ||
                !CrossbowTurretRules.TransitionAllowed(State(record), next)) return false;
            if (next == CrossbowTurretState.Active)
            {
                if (AnotherActive(record.GetLong(OwnerKey, 0), record.m_uid)) { reason = "another_turret"; return false; }
                if (!CrossbowTurretStock.TryRead(expected, definition, out _, out reason)) return false;
            }
            // State-only lifecycle changes preserve byte-for-byte weapon and remaining ammunition.
            record.Set(StateKey, (int)next); record.Set(RevisionKey, revision + 1);
            ZDOMan.instance.ForceSendZDO(record.m_uid); reason = null; return true;
        }
        internal static bool TryCommitShot(ZDO record, int revision, byte[] expected, Container definition,
            out ItemDrop.ItemData weapon, out ItemDrop.ItemData bolt, out string reason)
        {
            weapon = bolt = null; reason = "custody_changed";
            if (!CanWrite(record, revision, expected) || revision == int.MaxValue || State(record) != CrossbowTurretState.Active)
                return false;
            Container loaded = ZNetScene.instance?.FindInstance(record.m_uid)?.GetComponent<Container>();
            if (loaded != null && (loaded.IsInUse() || loaded.m_nview?.IsOwner() != true)) return false;
            if (!CrossbowTurretStock.TryRead(expected, definition, out var stock, out reason)) return false;
            if (!stock.TrySpendBolt(out var spent, out var output)) { reason = "empty"; return false; }
            // Recheck after isolated decoding/debit. No world write on a failed compare.
            if (!CanWrite(record, revision, expected)) { reason = "custody_changed"; return false; }
            record.Set(ZDOVars.s_items, output); record.Set(RevisionKey, revision + 1);
            if (loaded != null) loaded.Load();
            ZDOMan.instance.ForceSendZDO(record.m_uid);
            weapon = stock.Weapon; bolt = spent; reason = null; return true;
        }
    }
}
