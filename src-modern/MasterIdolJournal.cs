using System;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class MasterIdolReceipt
    {
        internal string Token, Type;
        internal long World, Player;
        internal int Phase; // 1 requested, 2 uncertain, 3 applied, 4 cancelled, 5 settled
        internal ZDOID Output;
    }
    internal static class MasterIdolJournal
    {
        private static string Key => "VM_MasterIdol_Receipt_v1_" + (ZNet.instance?.GetWorldUID() ?? 0);
        internal static MasterIdolReceipt Read(Player player)
        {
            if (player == null || !player.m_customData.TryGetValue(Key, out string value)) return null;
            try
            {
                if (value.Length > 1024) throw new FormatException();
                var packet = new ZPackage(Convert.FromBase64String(value));
                if (packet.ReadInt() != 1) throw new FormatException();
                var receipt = new MasterIdolReceipt { World = packet.ReadLong(), Player = packet.ReadLong(),
                    Token = packet.ReadString(), Type = packet.ReadString(), Phase = packet.ReadInt(), Output = packet.ReadZDOID() };
                if (receipt.World != ZNet.instance.GetWorldUID() || receipt.Player != player.GetPlayerID() ||
                    !Guid.TryParseExact(receipt.Token, "N", out _) || MasterIdolProfiles.Find(receipt.Type) == null ||
                    receipt.Phase < 1 || receipt.Phase > 5 || (receipt.Phase == 3 && receipt.Output == ZDOID.None)) throw new FormatException();
                return receipt;
            }
            catch { return new MasterIdolReceipt { Phase = 2 }; } // malformed is blocked, never interpreted as no pending work
        }
        // Only called from the authenticated terminal kind2 ACK branch; never a payment proof.
        internal static bool SettleAcknowledged(Player player,MasterIdolReceipt receipt)
        {
            if(player==null || receipt==null || (receipt.Phase!=3 && receipt.Phase!=4) ||
                receipt.Player!=player.GetPlayerID() || receipt.World!=ZNet.instance?.GetWorldUID())return false;
            var current=Read(player);
            if(current==null || current.Token!=receipt.Token || current.Type!=receipt.Type || current.Phase!=receipt.Phase ||
                current.World!=receipt.World || current.Player!=receipt.Player || current.Output!=receipt.Output)return false;
            try
            {
                var packet=new ZPackage();packet.Write(1);packet.Write(receipt.World);packet.Write(receipt.Player);
                packet.Write(receipt.Token);packet.Write(receipt.Type);packet.Write(5);packet.Write(receipt.Output);
                string encoded=Convert.ToBase64String(packet.GetArray());
                player.m_customData[Key]=encoded;receipt.Phase=5;return true;
            }
            catch { return false; }
        }
        // Ordinary building semantics explicitly chosen by the user: native autosave persists
        // inventory and this receipt later. Phase3 proves in-memory native debit, not disk durability.
        internal static bool Record(Player player, MasterIdolReceipt receipt, int phase, ZDOID output)
        {
            if (player == null || receipt == null || receipt.Player != player.GetPlayerID() ||
                receipt.World != ZNet.instance?.GetWorldUID() || !Guid.TryParseExact(receipt.Token,"N",out _) ||
                MasterIdolProfiles.Find(receipt.Type)==null || phase<1 || phase>5 || phase==3&&output==ZDOID.None) return false;
            try
            {
            var packet = new ZPackage(); packet.Write(1); packet.Write(receipt.World); packet.Write(receipt.Player);
            packet.Write(receipt.Token); packet.Write(receipt.Type); packet.Write(phase); packet.Write(output);
            player.m_customData[Key] = Convert.ToBase64String(packet.GetArray());
            receipt.Phase = phase; receipt.Output = output;
            return true;
            }
            catch { return false; }
        }
    }
}
