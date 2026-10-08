using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimMastery
{
    // Decodes the SAME native container payload into an isolated working inventory.
    // No ItemData clones, player refunds or second persistent weapon/ammo store.
    internal sealed class CrossbowTurretStock
    {
        internal readonly Inventory Inventory;
        internal readonly ItemDrop.ItemData Weapon;
        internal readonly int Bolts;
        private CrossbowTurretStock(Inventory inventory, ItemDrop.ItemData weapon, int bolts)
        { Inventory = inventory; Weapon = weapon; Bolts = bolts; }

        internal static bool TryRead(byte[] bytes, Container definition, out CrossbowTurretStock stock, out string reason)
        {
            stock = null; reason = "invalid_snapshot";
            if (definition == null || bytes == null || bytes.Length == 0 || bytes.Length > CrossbowTurretRules.MaximumSnapshotBytes)
                return false;
            try
            {
                var inventory = new Inventory(definition.m_name, definition.m_bkg, definition.m_width, definition.m_height);
                inventory.Load(new ZPackage(bytes));
                // Native Load can skip unknown prefabs. Refuse non-lossless packages instead of silently deleting cargo.
                var roundTrip = new ZPackage(); inventory.Save(roundTrip);
                if (!bytes.SequenceEqual(roundTrip.GetArray())) { reason = "non_lossless_snapshot"; return false; }
                var entries = new List<CrossbowTurretStockEntry>();
                ItemDrop.ItemData weapon = null;
                foreach (var item in inventory.GetAllItems())
                {
                    if (item?.m_shared == null || item.m_dropPrefab == null) { reason = "missing_item_definition"; return false; }
                    bool crossbow = item.IsWeapon() && item.m_shared.m_skillType == Skills.SkillType.Crossbows;
                    bool ammo = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo;
                    bool ready = !float.IsNaN(item.m_durability) && !float.IsInfinity(item.m_durability) &&
                        (!item.m_shared.m_useDurability || item.m_durability > 0f);
                    if (crossbow) weapon = item;
                    entries.Add(new CrossbowTurretStockEntry(crossbow ? CrossbowTurretItemKind.Crossbow :
                        ammo ? CrossbowTurretItemKind.Bolt : CrossbowTurretItemKind.Other,
                        item.m_stack, item.m_shared.m_ammoType, ready));
                }
                if (!CrossbowTurretRules.Validate(entries, out int bolts, out reason)) return false;
                stock = new CrossbowTurretStock(inventory, weapon, bolts); return true;
            }
            catch (Exception) { reason = "malformed_snapshot"; return false; }
        }
        internal bool TrySpendBolt(out ItemDrop.ItemData bolt, out byte[] output)
        {
            bolt = null; output = null;
            if (Bolts == 0) return false;
            foreach (var item in Inventory.GetAllItems())
                if (!ReferenceEquals(item, Weapon) && item.m_stack > 0 &&
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo &&
                    item.m_shared.m_ammoType == Weapon.m_shared.m_ammoType)
                { bolt = item; break; }
            if (bolt == null || !Inventory.RemoveItem(bolt, 1)) return false;
            var packet = new ZPackage(); Inventory.Save(packet); output = packet.GetArray();
            return true;
        }
        internal static bool Lossless(byte[] bytes, Container definition)
        {
            TryRead(bytes, definition, out _, out string reason);
            return reason != "non_lossless_snapshot" && reason != "malformed_snapshot" &&
                reason != "invalid_snapshot" && reason != "missing_item_definition";
        }
    }
}
