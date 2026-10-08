using System;
using System.Linq;

namespace ValheimMastery
{
    // No prefab instantiation, global registry changes or MonoBehaviour simulation.
    // This adapter writes only the real chest owner's native items byte array.
    internal sealed class WorkshopRecordStore : IWorkshopDebitStore
    {
        internal readonly ZDO Record;
        internal readonly Inventory Inventory;
        private byte[] Expected;
        internal WorkshopRecordStore(ZDO record, Container definition)
        {
            if (record == null || definition == null) throw new ArgumentNullException();
            Record = record;
            Inventory = new Inventory(definition.m_name, definition.m_bkg, definition.m_width, definition.m_height);
            Expected = record.GetByteArray(ZDOVars.s_items, null);
            if (Expected == null || Expected.Length > WorkshopLeaseRules.MaximumSnapshotBytes)
                throw new InvalidOperationException("Missing or oversized native chest inventory.");
            Inventory.Load(new ZPackage(Expected));
        }
        public object Identity => Record;
        internal bool CanWrite()
        {
            ZDO current = ZDOMan.instance?.GetZDO(Record.m_uid);
            byte[] bytes = current?.GetByteArray(ZDOVars.s_items, null);
            return ZNet.instance?.IsServer() == true && ReferenceEquals(current, Record) &&
                current.GetOwner() == ZNet.GetUID() && bytes != null && Expected.SequenceEqual(bytes);
        }
        public int Count(string item) => Inventory.CountItems(item, -1, false);
        public object Capture()
        {
            if (!CanWrite()) throw new InvalidOperationException("Chest owner or stock changed before capture.");
            return new WorkshopInventorySnapshot(Inventory);
        }
        public void Remove(string item, int amount) => Inventory.RemoveItem(item, amount, -1, false);
        public void Restore(object snapshot)
        {
            if (!CanWrite()) throw new InvalidOperationException("Changed world stock cannot be silently overwritten by rollback.");
            ((WorkshopInventorySnapshot)snapshot).Restore();
        }
        public void Flush()
        {
            if (!CanWrite()) throw new InvalidOperationException("Chest owner or stock changed before persistence.");
            var packet = new ZPackage(); Inventory.Save(packet);
            byte[] output = packet.GetArray(); Record.Set(ZDOVars.s_items, output);
            Expected = output;
            // A loaded server Container must not later save its pre-debit inventory.
            Container loaded = ZNetScene.instance?.FindInstance(Record.m_uid)?.GetComponent<Container>();
            if (loaded != null && loaded.m_nview?.IsOwner() == true) loaded.Load();
            ZDOMan.instance.ForceSendZDO(Record.m_uid);
        }
    }
}
