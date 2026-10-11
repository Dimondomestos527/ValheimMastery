using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace ValheimMastery
{
    internal sealed class CookingProductRecord
    {
        internal string Prefab, AuthorName;
        internal long Author;
        internal Dictionary<string,string> Data;
        internal static CookingProductRecord Capture(ItemDrop.ItemData item) => item?.m_shared == null ? null :
            new CookingProductRecord { Prefab=TierDatabase.ItemKey(item), Author=item.m_crafterID,
                AuthorName=item.m_crafterName ?? "", Data=new Dictionary<string,string>(item.m_customData) };
        internal void Apply(ItemDrop.ItemData item)
        {
            item.m_crafterID=Author; item.m_crafterName=AuthorName;
            item.m_customData=new Dictionary<string,string>(Data);
        }
        internal string Encode()
        {
            if(string.IsNullOrEmpty(Prefab) || Prefab.Length>128 || (AuthorName??"").Length>256 || Data==null || Data.Count>256 ||
                Data.Any(row=>row.Key==null || row.Key.Length>256 || row.Value==null || row.Value.Length>4096))
                throw new InvalidOperationException("Invalid product receipt fields.");
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
            {
                writer.Write(1); writer.Write(Prefab); writer.Write(Author); writer.Write(AuthorName ?? ""); writer.Write(Data.Count);
                foreach(var row in Data.OrderBy(row=>row.Key,StringComparer.Ordinal)) { writer.Write(row.Key); writer.Write(row.Value); }
                writer.Flush(); if(stream.Length>8192) throw new InvalidOperationException("Product metadata exceeds receipt bound.");
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        internal static CookingProductRecord Decode(string raw)
        {
            if(string.IsNullOrEmpty(raw) || raw.Length>10924) throw new FormatException("Invalid product receipt size.");
            using(var stream=new MemoryStream(Convert.FromBase64String(raw))) using(var reader=new BinaryReader(stream))
            {
                if(reader.ReadInt32()!=1) throw new FormatException("Unknown product receipt schema.");
                var result=new CookingProductRecord { Prefab=reader.ReadString(),Author=reader.ReadInt64(),AuthorName=reader.ReadString(),Data=new Dictionary<string,string>() };
                int count=reader.ReadInt32(); if(count<0 || count>256 || result.Prefab.Length>128 || result.AuthorName.Length>256) throw new FormatException("Invalid product receipt fields.");
                for(int i=0;i<count;i++) { string key=reader.ReadString(),value=reader.ReadString(); if(key.Length>256 || value.Length>4096 || result.Data.ContainsKey(key)) throw new FormatException("Invalid product metadata."); result.Data.Add(key,value); }
                if(stream.Position!=stream.Length) throw new FormatException("Trailing product receipt bytes.");
                return result;
            }
        }
    }
    internal sealed class CookingDebitProvenance : IDisposable
    {
        [ThreadStatic] internal static CookingDebitProvenance Current;
        private readonly CookingDebitProvenance Previous;
        private readonly List<CookingProductRecord> Items=new List<CookingProductRecord>();
        private bool Closed;
        private CookingDebitProvenance() { Previous=Current; Current=this; }
        internal static CookingDebitProvenance Begin()=>new CookingDebitProvenance();
        internal static void Preflight(Inventory inventory,string name)
        {
            if(inventory==null)throw new InvalidOperationException("Missing product inventory.");
            foreach(var item in inventory.GetAllItems())
                if(item?.m_shared?.m_name==name && CookingProductIdentity.Relevant(item))CookingProductRecord.Capture(item).Encode();
        }
        internal CookingProductRecord Single(string prefab)
        {
            var matches=Items.Where(item=>item.Prefab==prefab).ToList();
            if(matches.Count!=1) throw new InvalidOperationException("Feast payment must consume one exact physical product.");
            return matches[0];
        }
        internal void Add(ItemDrop.ItemData item,int amount)
        {
            if(amount<=0 || !CookingProductIdentity.Relevant(item)) return;
            if(amount>32) throw new InvalidOperationException("Unsupported product debit count.");
            for(int i=0;i<amount;i++) Items.Add(CookingProductRecord.Capture(item));
        }
        public void Dispose() { if(Closed)return; Closed=true; Current=Previous; }
        internal sealed class Removal
        {
            internal CookingDebitProvenance Trace;
            internal Inventory Inventory;
            internal readonly List<Tuple<ItemDrop.ItemData,int,ItemDrop.ItemData>> Before=new List<Tuple<ItemDrop.ItemData,int,ItemDrop.ItemData>>();
        }
        internal static Removal Before(Inventory inv,string name)
        {
            if(Current==null)return null;
            var state=new Removal { Trace=Current, Inventory=inv };
            foreach(var item in inv.GetAllItems()) if(item?.m_shared?.m_name==name)
                state.Before.Add(Tuple.Create(item,item.m_stack,item.Clone()));
            return state;
        }
        internal static void After(Removal state)
        {
            if(state==null)return;
            var items=state.Inventory.GetAllItems();
            foreach(var row in state.Before)
            {
                int after=items.Contains(row.Item1)?row.Item1.m_stack:0;
                state.Trace.Add(row.Item3,row.Item2-after);
            }
        }
    }
    [HarmonyPatch(typeof(Inventory), "RemoveItem", new Type[] {typeof(string),typeof(int),typeof(int),typeof(bool)})]
    internal static class CookingExactDebitTracePatch
    {
        private static void Prefix(Inventory __instance,string __0,out CookingDebitProvenance.Removal __state)=>__state=CookingDebitProvenance.Before(__instance,__0);
        private static void Postfix(CookingDebitProvenance.Removal __state)=>CookingDebitProvenance.After(__state);
    }
}
