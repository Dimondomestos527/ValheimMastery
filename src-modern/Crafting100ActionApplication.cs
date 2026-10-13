using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal enum Crafting100ApplicationResult { Applied, Rejected, Uncertain }

    // Called only for an authenticated admitted permit. Gold owns request/phase journaling and retries.
    // Persistence callbacks save their phase and the mutated inventory together, never memory-only ACK.
    internal static class Crafting100ActionApplication
    {
        internal const string GrantKey = "VM_Crafting100_Grant_v1";
        internal const string IdentityKey = "VM_Gold_ItemIdentity";
        internal static bool Equipment(ItemDrop.ItemData item)=>item?.m_shared!=null&&item.IsEquipable()&&item.m_shared.m_maxStackSize==1;
        internal static bool Quantity(Recipe recipe,bool shift,out int quantity)
        {
            quantity=0;var item=recipe?.m_item?.m_itemData;
            if(item?.m_shared==null||recipe.m_amount<1||item.m_shared.m_maxStackSize<1)return false;
            if(Equipment(item)){quantity=1;return true;}
            if(shift&&item.m_shared.m_maxStackSize<=1)return false;
            long count=(long)recipe.m_amount*(shift?5:1);if(count<1||count>4096)return false;
            quantity=(int)count;return true;
        }
        internal static bool CanFit(Inventory inventory,Recipe recipe,int quality,int quantity)
        {
            if(inventory==null||inventory.m_inventory.Count>4096||recipe?.m_item?.m_itemData==null||quantity<1||quantity>4096)return false;
            var preview=recipe.m_item.m_itemData.Clone();preview.m_quality=quality;preview.m_worldLevel=Game.m_worldLevel;
            long room=(long)inventory.GetEmptySlots()*preview.m_shared.m_maxStackSize;
            foreach(var item in inventory.m_inventory)
                if(item.m_shared.m_name==preview.m_shared.m_name&&item.m_quality==quality&&item.m_worldLevel==Game.m_worldLevel)
                    room+=Math.Max(0,item.m_shared.m_maxStackSize-item.m_stack);
            return room>=quantity&&inventory.CanAddItem(preview,quantity);
        }
        internal static bool TryOutcome(Recipe recipe,ItemDrop.ItemData target,bool forge,bool shift,
            out int operation,out int quality,out int quantity,out string reason)
        {
            operation=quality=quantity=0;reason="";var item=recipe?.m_item?.m_itemData;
            if(item?.m_shared==null)return false;
            operation=forge?3:target==null?1:2;
            if(forge)
            {
                if(target==null||!GoldDivineTransactions.Eligible(item)||!Crafting100ForgeItemRules.CanUpgrade(target.m_quality,target.m_customData))return false;
                quality=target.m_quality+3;return true;
            }
            long next=target==null?(shift&&Equipment(item)?3:1):(long)target.m_quality+(shift?2:1);
            if(next<1||next>item.m_shared.m_maxQuality||target!=null&&!Equipment(item))
            {reason=GoldUiLocalization.Text("This item cannot receive the full selected improvement.","Цей виріб не може отримати повне вибране покращення.");return false;}
            quality=(int)next;
            if(target==null&&!Quantity(recipe,shift,out quantity))
            {reason=GoldUiLocalization.Text("This recipe cannot create the selected number of items.","Цей рецепт не дозволяє створити вибрану кількість виробів.");return false;}
            return true;
        }
        private sealed class Mutation
        {
            internal ItemDrop.ItemData Item;internal bool Created,Removed;
            internal int Index,Stack,Quality,Variant,World,ExpectedStack,ExpectedQuality;
            internal float Durability,ExpectedDurability;
            internal GameObject Prefab;internal ItemDrop.ItemData.SharedData Shared;internal long Crafter;internal string CrafterName;
            internal Dictionary<string,string> Metadata,ExpectedMetadata;
            internal Mutation(ItemDrop.ItemData item,int index,bool created=false)
            {Item=item;Index=index;Created=created;Stack=ExpectedStack=item.m_stack;Quality=ExpectedQuality=item.m_quality;
                Variant=item.m_variant;World=item.m_worldLevel;Durability=ExpectedDurability=item.m_durability;
                Prefab=item.m_dropPrefab;Shared=item.m_shared;Crafter=item.m_crafterID;CrafterName=item.m_crafterName;
                Metadata=new Dictionary<string,string>(item.m_customData);ExpectedMetadata=new Dictionary<string,string>(Metadata);}
            internal bool Matches(Inventory inventory)
            {
                if(inventory.ContainsItem(Item)==Removed||Item.m_stack!=ExpectedStack||Item.m_quality!=ExpectedQuality||
                    Item.m_variant!=Variant||Item.m_worldLevel!=World||Item.m_durability!=ExpectedDurability||
                    Item.m_customData.Count!=ExpectedMetadata.Count||Item.m_dropPrefab!=Prefab||!ReferenceEquals(Item.m_shared,Shared)||
                    Item.m_crafterID!=Crafter||Item.m_crafterName!=CrafterName)return false;
                foreach(var pair in ExpectedMetadata)if(!Item.m_customData.TryGetValue(pair.Key,out var value)||value!=pair.Value)return false;
                return true;
            }
            internal void Restore(Inventory inventory)
            {
                if(Created){inventory.m_inventory.Remove(Item);return;}
                Item.m_stack=Stack;Item.m_quality=Quality;Item.m_durability=Durability;
                Item.m_customData.Clear();foreach(var pair in Metadata)Item.m_customData.Add(pair.Key,pair.Value);
                if(Removed)inventory.m_inventory.Insert(Math.Min(Index,inventory.m_inventory.Count),Item);
            }
        }
        // V2 uses one character outcome checkpoint, not output metadata/count as replay proof.
        internal static Crafting100ApplicationResult ApplyV2(Player player,Recipe recipe,ItemDrop.ItemData target,
            GoldAction action,Func<bool> currentPermit,Func<bool> persistApplied,Func<bool> persistRejected)
        {
            if(persistApplied==null||persistRejected==null)return Crafting100ApplicationResult.Uncertain;
            Inventory inventory=player?.GetInventory();var session=ZNet.instance;long world=session?.GetWorldUID()??0;
            var character=player?.m_nview?.GetZDO()?.m_uid??ZDOID.None;var changes=new List<Mutation>();
            bool mutated=false;
            bool Context()=>player!=null&&player==Player.m_localPlayer&&ZNet.instance==session&&session?.GetWorldUID()==world&&
                character!=ZDOID.None&&player.m_nview?.GetZDO()?.m_uid==character&&ReferenceEquals(player.GetInventory(),inventory);
            try
            {
                if(!Context()||player.IsDead()||player.IsTeleporting()||action?.WireVersion!=2||
                    !Guid.TryParseExact(action.Token,"N",out _)||currentPermit?.Invoke()!=true||inventory.m_inventory.Count>4096||
                    !TryOutcome(recipe,target,action.Operation==3,action.Shift,out int op,out int quality,out int quantity,out _)||
                    op!=action.Operation||quality!=action.NewQuality||quantity!=action.OutputAmount)return Reject(persistRejected);
                if(op==1)
                {
                    if(!CanFit(inventory,recipe,quality,quantity))return Reject(persistRejected);
                    int remaining=quantity;
                    while(remaining>0)
                    {
                        var shared=recipe.m_item.m_itemData.m_shared;
                        var stack=shared.m_maxStackSize>1?inventory.FindFreeStackItem(shared.m_name,quality,Game.m_worldLevel):null;
                        if(stack!=null)
                        {
                            int add=Math.Min(remaining,stack.m_shared.m_maxStackSize-stack.m_stack);
                            if(add<=0)throw new InvalidOperationException("V2 stack capacity changed");
                            var change=new Mutation(stack,inventory.m_inventory.IndexOf(stack));changes.Add(change);
                            change.ExpectedStack=checked(stack.m_stack+add);mutated=true;stack.m_stack=change.ExpectedStack;remaining-=add;
                        }
                        else
                        {
                            var created=recipe.m_item.m_itemData.Clone();created.m_dropPrefab=recipe.m_item.gameObject;
                            created.m_quality=quality;created.m_variant=action.Variant;created.m_stack=Math.Min(remaining,shared.m_maxStackSize);
                            created.m_worldLevel=Game.m_worldLevel;created.m_crafterID=player.GetPlayerID();created.m_crafterName=player.GetPlayerName();
                            created.m_customData.Remove("VM_Masterwork");created.m_customData.Remove("VM_MasterworkPatron");created.m_customData.Remove(Crafting100ForgeItemRules.UsedKey);
                            if(Equipment(created)){created.m_customData[GrantKey]=action.Token;created.m_customData[IdentityKey]=Guid.NewGuid().ToString("N");}
                            created.m_durability=created.GetMaxDurability();created.m_gridPos=inventory.FindEmptySlot(inventory.TopFirst(created));
                            if(created.m_gridPos.x<0)throw new InvalidOperationException("V2 inventory capacity changed");
                            var change=new Mutation(created,inventory.m_inventory.Count,true);changes.Add(change);
                            mutated=true;inventory.m_inventory.Add(created);remaining-=created.m_stack;
                        }
                    }
                }
                else
                {
                    if(target==null||!inventory.ContainsItem(target)||target.m_stack!=1||target.m_quality!=action.OldQuality||
                        !target.m_customData.TryGetValue(IdentityKey,out var identity)||identity!=action.ItemIdentity)return Reject(persistRejected);
                    var targetChange=new Mutation(target,inventory.m_inventory.IndexOf(target));
                    if(op==3)
                    {
                        var idol=SelectRequiredIdol(inventory,recipe,action.OldQuality+1,target);
                        if(idol==null||TierDatabase.ItemKey(idol)!=action.Idol||idol.m_quality!=action.IdolQuality)return Reject(persistRejected);
                        var debit=new Mutation(idol,inventory.m_inventory.IndexOf(idol));debit.ExpectedStack=idol.m_stack-1;debit.Removed=debit.ExpectedStack==0;
                        changes.Add(debit);mutated=true;idol.m_stack=debit.ExpectedStack;if(debit.Removed)inventory.m_inventory.Remove(idol);
                    }
                    changes.Add(targetChange);mutated=true;target.m_quality=quality;target.m_durability=target.GetMaxDurability();
                    if(op==3)Crafting100ForgeItemRules.MarkApplied(target.m_customData,action.Token);
                    target.m_customData[GrantKey]=action.Token;targetChange.ExpectedQuality=quality;targetChange.ExpectedDurability=target.m_durability;
                    targetChange.ExpectedMetadata=new Dictionary<string,string>(target.m_customData);
                }
                inventory.Changed();
                if(!Context())return Crafting100ApplicationResult.Uncertain;
                foreach(var change in changes)if(!change.Matches(inventory))return Crafting100ApplicationResult.Uncertain;
                // Once save has been attempted, failure may already exist on disk.
                // Gold's V2 RAM fence remains Uncertain4 on failure + quarantines; never reject/roll back this output.
                try{return persistApplied()?Crafting100ApplicationResult.Applied:Crafting100ApplicationResult.Uncertain;}
                catch{return Crafting100ApplicationResult.Uncertain;}
            }
            catch(Exception)
            {
                if(mutated)
                {
                    if(!Context())return Crafting100ApplicationResult.Uncertain;
                    foreach(var change in changes)if(!change.Matches(inventory))return Crafting100ApplicationResult.Uncertain;
                    try{for(int i=changes.Count-1;i>=0;i--)changes[i].Restore(inventory);inventory.Changed();}
                    catch{return Crafting100ApplicationResult.Uncertain;}
                }
                return Reject(persistRejected);
            }
        }

        // Special forge needs ONE recipe-defined upgrader resource, independently of
        // the normal recipe quantity. Preserve requirement/quality/inventory ordering,
        // native world-level eligibility, and enforce exact prefab instead of shared name.
        internal static ItemDrop.ItemData SelectRequiredIdol(Inventory inventory, Recipe recipe,
            int nextQuality, ItemDrop.ItemData target)
        {
            if (inventory == null || recipe?.m_resources == null || nextQuality < 1) return null;
            foreach (var requirement in recipe.m_resources)
            {
                if (requirement?.m_upgraderResource != true || requirement.m_resItem?.m_itemData?.m_shared == null ||
                    requirement.GetAmount(nextQuality) <= 0) continue;
                string prefab = requirement.m_resItem.gameObject.name;
                int maxQuality = requirement.m_resItem.m_itemData.m_shared.m_maxQuality;
                ItemDrop.ItemData first = null;
                foreach (var item in inventory.m_inventory)
                {
                    if (item?.m_shared == null || ReferenceEquals(item, target) || item.m_stack < 1 ||
                        item.m_quality < 0 || item.m_quality > maxQuality || item.m_worldLevel < Game.m_worldLevel ||
                        TierDatabase.ItemKey(item) != prefab) continue;
                    if (first == null || item.m_quality < first.m_quality) first = item;
                }
                if (first != null) return first;
            }
            return null;
        }

        internal static Crafting100ApplicationResult Apply(Player player, Recipe recipe,
            ItemDrop.ItemData target, int kind, int variant, int expectedQuality, string token,
            Func<bool> currentPermit, Func<bool> persistApplied, Func<bool> persistRejected)
        {
            if (persistApplied == null || persistRejected == null) return Crafting100ApplicationResult.Uncertain;
            Inventory inventory = player?.GetInventory();
            ItemDrop.ItemData created = null, idol = null;
            int oldStack = 0, oldIndex = -1;
            float oldDurability = target?.m_durability ?? 0;
            bool mutated = false, hadGrant = false;
            string oldGrant = null;
            try
            {
                if (!Valid(player, recipe, target, kind, variant, expectedQuality, token, currentPermit))
                    return Reject(persistRejected);
                if (kind == 1)
                {
                    if (!inventory.CanAddItem(recipe.m_item.gameObject, 1)) return Reject(persistRejected);
                    created = recipe.m_item.m_itemData.Clone();
                    created.m_dropPrefab = recipe.m_item.gameObject;
                    created.m_quality = 5; created.m_variant = variant; created.m_stack = 1;
                    created.m_worldLevel = Game.m_worldLevel;
                    created.m_crafterID = player.GetPlayerID(); created.m_crafterName = player.GetPlayerName();
                    created.m_customData.Remove("VM_Masterwork"); created.m_customData.Remove("VM_MasterworkPatron");
                    created.m_customData.Remove(Crafting100ForgeItemRules.UsedKey);
                    created.m_customData[GrantKey] = token;
                    created.m_customData[IdentityKey] = Guid.NewGuid().ToString("N");
                    created.m_durability = created.GetMaxDurability();
                    // Changed callbacks may throw after insertion.
                    mutated = true;
                    if (!inventory.AddItem(created)) throw new InvalidOperationException("Crafting100 capacity changed.");
                }
                else
                {
                    idol = SelectRequiredIdol(inventory, recipe, expectedQuality + 1, target);
                    if (!RequiredIdol(recipe, idol, expectedQuality + 1) || ReferenceEquals(idol, target))
                        return Reject(persistRejected);
                    oldStack = idol.m_stack; oldIndex = inventory.m_inventory.IndexOf(idol);
                    hadGrant = target.m_customData.TryGetValue(GrantKey, out oldGrant);
                    // Revalidate immediately before native debit and item mutation.
                    if (!Valid(player, recipe, target, kind, variant, expectedQuality, token, currentPermit) ||
                        !ReferenceEquals(SelectRequiredIdol(inventory, recipe, expectedQuality + 1, target), idol))
                        return Reject(persistRejected);
                    mutated = true;
                    if (!inventory.RemoveItem(idol, 1)) throw new InvalidOperationException("Crafting100 idol debit changed.");
                    target.m_quality = Crafting100ForgeItemRules.UpgradedQuality(expectedQuality, target.m_customData);
                    target.m_durability = target.GetMaxDurability();
                    Crafting100ForgeItemRules.MarkApplied(target.m_customData, token);
                    target.m_customData[GrantKey] = token;
                    inventory.Changed();
                }
                // Gold persistence revalidates session/player/pending journal after inventory callbacks.
                if (persistApplied()) return Crafting100ApplicationResult.Applied;
            }
            catch (Exception) { /* Roll back below, including callbacks throwing after native mutation. */ }

            if (mutated)
            {
                try
                {
                    if (kind == 1) inventory.m_inventory.Remove(created);
                    else
                    {
                        target.m_quality = expectedQuality; target.m_durability = oldDurability;
                        target.m_customData.Remove(Crafting100ForgeItemRules.UsedKey);
                        if (hadGrant) target.m_customData[GrantKey] = oldGrant; else target.m_customData.Remove(GrantKey);
                        idol.m_stack = oldStack;
                        if (!inventory.ContainsItem(idol))
                            inventory.m_inventory.Insert(Math.Min(Math.Max(0, oldIndex), inventory.m_inventory.Count), idol);
                    }
                }
                catch (Exception) { return Crafting100ApplicationResult.Uncertain; }
                // Authoritative fields restored first; display callback failure must not defeat rollback/save.
                try { inventory.Changed(); } catch (Exception) { }
            }
            return Reject(persistRejected);
        }

        private static Crafting100ApplicationResult Reject(Func<bool> persistRejected)
        {
            try { return persistRejected() ? Crafting100ApplicationResult.Rejected : Crafting100ApplicationResult.Uncertain; }
            catch (Exception) { return Crafting100ApplicationResult.Uncertain; }
        }
        private static bool Valid(Player player, Recipe recipe, ItemDrop.ItemData target, int kind,
            int variant, int expectedQuality, string token, Func<bool> currentPermit)
        {
            if (player == null || player != Player.m_localPlayer || player.IsDead() || player.IsTeleporting() ||
                currentPermit?.Invoke() != true || !Guid.TryParseExact(token, "N", out _) ||
                recipe?.m_enabled != true || recipe.m_item?.m_itemData?.m_shared == null ||
                !recipe.m_item.m_itemData.IsEquipable() || recipe.m_item.m_itemData.m_shared.m_maxStackSize != 1 ||
                recipe.m_item.m_itemData.m_shared.m_maxQuality <= 1 || variant < 0 ||
                recipe.m_item.m_itemData.m_shared.m_icons == null || variant >= recipe.m_item.m_itemData.m_shared.m_icons.Length)
                return false;
            if (kind == 1) return target == null && expectedQuality == 0;
            return kind == 2 && target != null && player.GetInventory().ContainsItem(target) &&
                target.m_stack == 1 && target.m_quality == expectedQuality && target.m_variant == variant &&
                TierDatabase.ItemKey(target) == recipe.m_item.gameObject.name &&
                Crafting100ForgeItemRules.CanUpgrade(expectedQuality, target.m_customData);
        }
        private static bool RequiredIdol(Recipe recipe, ItemDrop.ItemData idol, int nextQuality)
        {
            if (idol?.m_shared == null || idol.m_stack < 1 || recipe.m_resources == null) return false;
            foreach (var requirement in recipe.m_resources)
                if (requirement?.m_upgraderResource == true && requirement.m_resItem != null &&
                    requirement.GetAmount(nextQuality) > 0 &&
                    TierDatabase.ItemKey(idol) == requirement.m_resItem.gameObject.name) return true;
            return false;
        }
    }
}

