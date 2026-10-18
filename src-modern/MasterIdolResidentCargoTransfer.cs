using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace ValheimMastery
{
    internal static class ResidentCargoTransfer
    {
        private sealed class References
        {
            private readonly Inventory Inventory;private readonly ItemDrop.ItemData[] Items,States;
            internal References(Inventory inventory){Inventory=inventory;Items=inventory.GetAllItems().ToArray();States=Items.Select(item=>item.Clone()).ToArray();}
            internal void Restore()
            {
                Inventory.m_inventory.Clear();Inventory.m_inventory.AddRange(Items);
                var fields=typeof(ItemDrop.ItemData).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                for(int i=0;i<Items.Length;i++)foreach(var field in fields)field.SetValue(Items[i],field.GetValue(States[i]));Inventory.Changed(true,false);
            }
        }
        internal static bool CanFit(Inventory inventory,ItemDrop.ItemData item)
        {
            if(item?.m_shared==null)return false;
            for(int y=0;y<inventory.GetHeight();y++)for(int x=0;x<inventory.GetWidth();x++)
            {var cell=inventory.GetItemAt(x,y);if(cell==null||cell.m_stack<cell.m_shared.m_maxStackSize&&SameMetadata(cell,item))return true;}
            return false;
        }
        internal static byte[] Snapshot(Inventory inventory){var p=new ZPackage();inventory.Save(p);return p.GetArray();}
        internal static bool SameMetadata(ItemDrop.ItemData left,ItemDrop.ItemData right)
        {
            if(left?.m_shared==null||right?.m_shared==null)return false;
            var a=left.Clone();var b=right.Clone();a.m_stack=b.m_stack=1;a.m_gridPos=b.m_gridPos=Vector2i.zero;
            var pa=new ZPackage();var pb=new ZPackage();a.Save(pa);b.Save(pb);var aa=pa.GetArray();var bb=pb.GetArray();
            if(aa.Length!=bb.Length)return false;for(int i=0;i<aa.Length;i++)if(aa[i]!=bb[i])return false;return true;
        }
        // Positional native AddItem may return false after a partial mutation.
        // The input clone's stack delta, not the bool, is the commit quantity.
        internal static int Add(Inventory destination,ItemDrop.ItemData source)
        {
            if(source?.m_shared==null||source.m_stack<=0)return 0;
            int before=source.m_stack;
            for(int y=0;y<destination.GetHeight()&&source.m_stack>0;y++)for(int x=0;x<destination.GetWidth()&&source.m_stack>0;x++)
            {
                var existing=destination.GetItemAt(x,y);if(existing!=null&&(existing.m_stack>=existing.m_shared.m_maxStackSize||!SameMetadata(source,existing)))continue;
                int capacity=existing==null?source.m_shared.m_maxStackSize:existing.m_shared.m_maxStackSize-existing.m_stack;
                if(capacity>0)destination.AddItem(source,Math.Min(capacity,source.m_stack),x,y,false);
            }
            return before-source.m_stack;
        }
        internal static int Deposit(Container from,Container to)
        {
            if(from==null||to==null||ReferenceEquals(from,to)||!from.IsOwner()||!to.IsOwner()||from.IsInUse()||to.IsInUse())return 0;
            var a=from.GetInventory();var b=to.GetInventory();var sa=Snapshot(a);var sb=Snapshot(b);int moved=0;
            // Suppress intermediate Container.Save callbacks, then save the pair
            // once on the single owner main thread. No yield/RPC inside commit.
            from.m_loading=to.m_loading=true;
            try
            {
                var items=a.GetAllItems().ToArray();
                foreach(var original in items)
                {
                    var copy=original.Clone();int count=Add(b,copy);
                    if(count>0){if(!a.RemoveItem(original,count))throw new InvalidOperationException("Cargo source changed");moved+=count;}
                }
                from.m_loading=to.m_loading=false;from.Save();to.Save();return moved;
            }
            catch
            {
                a.Load(new ZPackage(sa));b.Load(new ZPackage(sb));from.m_loading=to.m_loading=false;from.Save();to.Save();throw;
            }
            finally{from.m_loading=to.m_loading=false;}
        }
        internal static int Manual(Container box,Inventory player,bool takeAll)
        {
            var cargo=box.GetInventory();var from=takeAll?cargo:player;var to=takeAll?player:cargo;
            byte[] beforeCargo=Snapshot(cargo);var playerRefs=player.GetAllItems().ToArray();var playerStates=playerRefs.Select(item=>item.Clone()).ToArray();box.m_loading=true;int moved=0;
            try
            {
                foreach(var original in from.GetAllItems().ToArray())
                {
                    var copy=original.Clone();int n=0;
                    if(takeAll)n=Add(to,copy);
                    else for(int y=0;y<to.GetHeight()&&copy.m_stack>0;y++)for(int x=0;x<to.GetWidth()&&copy.m_stack>0;x++)
                    {var cell=to.GetItemAt(x,y);if(cell==null||!SameMetadata(cell,copy))continue;int before=copy.m_stack;to.AddItem(copy,copy.m_stack,x,y,false);n+=before-copy.m_stack;}
                    if(n>0){if(!from.RemoveItem(original,n))throw new InvalidOperationException("Manual cargo source changed");moved+=n;}
                }
                box.m_loading=false;box.Save();return moved;
            }
            catch
            {
                // Preserve native equipped/hidden/ammo references to existing
                // player ItemData. Inventory.Load would orphan those references.
                player.m_inventory.Clear();player.m_inventory.AddRange(playerRefs);
                var fields=typeof(ItemDrop.ItemData).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                for(int i=0;i<playerRefs.Length;i++)foreach(var field in fields)field.SetValue(playerRefs[i],field.GetValue(playerStates[i]));
                player.Changed(true,false);cargo.Load(new ZPackage(beforeCargo));box.m_loading=false;box.Save();throw;
            }
            finally{box.m_loading=false;}
        }
        // Actual GUI quick-move and drag/drop enter native Inventory seams,
        // not Container.TakeAll. Scope strictly to the two tagged inventories.
        internal static int Move(Inventory from,Inventory to,ItemDrop.ItemData item,int amount,int x=-1,int y=-1)
        {
            if(from==null||to==null||item==null||amount<=0||!from.GetAllItems().Contains(item))return 0;
            int wanted=Math.Min(amount,item.m_stack);if(ReferenceEquals(from,to)&&(x<0||item.m_gridPos.x==x&&item.m_gridPos.y==y))return wanted;
            var a=new References(from);var b=ReferenceEquals(from,to)?a:new References(to);var fromBox=ResidentCargoGui.Container(from);var toBox=ResidentCargoGui.Container(to);
            if(fromBox!=null)fromBox.m_loading=true;if(toBox!=null)toBox.m_loading=true;
            try
            {
                var copy=item.Clone();copy.m_stack=wanted;int moved;
                if(x>=0&&y>=0)
                {var cell=to.GetItemAt(x,y);if(cell!=null&&!SameMetadata(cell,copy))return 0;int before=copy.m_stack;to.AddItem(copy,wanted,x,y,false);moved=before-copy.m_stack;}
                else moved=Add(to,copy);
                if(moved>0&&!from.RemoveItem(item,moved))throw new InvalidOperationException("GUI cargo source changed");
                if(fromBox!=null){fromBox.m_loading=false;fromBox.Save();}if(toBox!=null&&toBox!=fromBox){toBox.m_loading=false;toBox.Save();}return moved;
            }
            catch
            {
                a.Restore();if(!ReferenceEquals(a,b))b.Restore();
                if(fromBox!=null){fromBox.m_loading=false;fromBox.Save();}if(toBox!=null&&toBox!=fromBox){toBox.m_loading=false;toBox.Save();}throw;
            }
            finally{if(fromBox!=null)fromBox.m_loading=false;if(toBox!=null)toBox.m_loading=false;}
        }
        internal static int Pickup(Container box,ItemDrop drop)
        {
            if(box==null||drop==null||!box.IsOwner()||box.IsInUse()||drop.m_nview?.IsValid()!=true||!drop.m_nview.IsOwner()||!drop.CanPickup(true))return 0;
            drop.Load();var original=drop.m_itemData.Clone();var copy=original.Clone();var inventory=box.GetInventory();var snapshot=Snapshot(inventory);box.m_loading=true;
            try
            {
                int moved=Add(inventory,copy);if(moved==0)return 0;
                drop.SetStack(original.m_stack-moved);box.m_loading=false;box.Save();
                if(drop.m_itemData.m_stack==0)ZNetScene.instance.Destroy(drop.gameObject);
                return moved;
            }
            catch{inventory.Load(new ZPackage(snapshot));drop.m_itemData=original;drop.Save();box.m_loading=false;box.Save();throw;}
            finally{box.m_loading=false;}
        }
    }
}
