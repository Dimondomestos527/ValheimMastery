using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class WoodInventoryCapacity
    {
        private sealed class OwnedShared { internal ItemDrop.ItemData.SharedData Original, Expanded; }
        private sealed class Clock { internal float Next; }
        private static readonly ConditionalWeakTable<ItemDrop.ItemData, OwnedShared> Copies = new ConditionalWeakTable<ItemDrop.ItemData, OwnedShared>();
        private static readonly ConditionalWeakTable<Player, Clock> Clocks = new ConditionalWeakTable<Player, Clock>();
        private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        internal static bool PersistenceReady;
        internal static bool ExpandedOwner(Inventory inventory)
        {
            var owner = Player.m_localPlayer;
            return PersistenceReady && owner != null && owner.GetInventory() == inventory &&
                PerkRuntimeService.HasPerk(owner, Skills.SkillType.WoodCutting, 70);
        }
        // Loading must preserve a previously saved quantity, including a grave
        // or a character who has lost the perk. This does NOT expand their merge
        // capacity: only ExpandedOwner grants new stacks above vanilla50.
        internal static int ClampPersistedStack(int stack, int vanillaMax, int prefabHash)
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(prefabHash);
            return ClampWoodStack(stack, vanillaMax, prefab != null && PerkProfessionService.IsWoodMaterial(prefab.name));
        }
        internal static int ClampWoodStack(int stack, int vanillaMax, bool wood) =>
            Math.Min(stack, vanillaMax == 50 && wood ? 100 : vanillaMax);
        internal static void Tick(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            var clock = Clocks.GetOrCreateValue(player);
            if (Time.time < clock.Next) return;
            clock.Next = Time.time + .5f;
            Normalize(player.GetInventory());
        }
        internal static void Normalize(Inventory inventory)
        {
            if (inventory == null) return;
            bool expand = ExpandedOwner(inventory);
            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_shared == null || !PerkProfessionService.IsWoodMaterial(item)) continue;
                var vanilla = item.m_dropPrefab?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
                if (vanilla == null || vanilla.m_maxStackSize != 50) continue;
                if (expand)
                {
                    if (!Copies.TryGetValue(item, out var copy))
                    {
                        copy = new OwnedShared { Original = vanilla, Expanded = (ItemDrop.ItemData.SharedData)Clone.Invoke(item.m_shared, null) };
                        copy.Expanded.m_maxStackSize = 100; Copies.Add(item, copy);
                    }
                    item.m_shared = copy.Expanded;
                }
                else if (item.m_shared.m_maxStackSize == 100)
                {
                    item.m_shared = Copies.TryGetValue(item, out var copy) ? copy.Original : vanilla;
                    // Never truncate a saved stack on perk loss or chest transfer.
                    // It remains spendable/splittable; further merges obey vanilla50.
                }
            }
        }
    }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class WoodInventoryOwnerPatch
    { private static void Postfix(Player __instance) => WoodInventoryCapacity.Tick(__instance); }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
    internal static class WoodInventoryTransferPatch
    { private static void Prefix(Inventory __instance) => WoodInventoryCapacity.Normalize(__instance); }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] {
        typeof(int), typeof(int), typeof(float), typeof(Vector2i), typeof(bool), typeof(int), typeof(int),
        typeof(long), typeof(string), typeof(Dictionary<string, string>), typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class WoodInventoryLoadPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var min = AccessTools.Method(typeof(Mathf), nameof(Mathf.Min), new[] { typeof(int), typeof(int) });
            int index = -1, matches = 0;
            for (int i = 1; i < code.Count; i++)
                if (code[i].Calls(min) && code[i - 1].operand is FieldInfo field && field.Name == "m_maxStackSize")
                { index = i; matches++; }
            if (matches != 1)
            {
                WoodInventoryCapacity.PersistenceReady = false;
                MasteryPlugin.Log.LogError("[Wood70] Stack100 disabled: native inventory load clamp changed.");
                return code;
            }
            var replacement = new CodeInstruction(OpCodes.Ldarg_1);
            replacement.labels.AddRange(code[index].labels);
            replacement.blocks.AddRange(code[index].blocks);
            code[index] = replacement;
            code.Insert(index + 1, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(WoodInventoryCapacity), nameof(WoodInventoryCapacity.ClampPersistedStack))));
            WoodInventoryCapacity.PersistenceReady = true;
            return code;
        }
    }

    // Shift-click: transfer a separate <=50 batch, never remove an entire100
    // source stack just because the destination accepted the first half.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new Type[] { typeof(Inventory), typeof(ItemDrop.ItemData) })]
    internal static class WoodInventoryQuickMovePatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, ItemDrop.ItemData item)
        {
            if (item == null || item.m_stack <= 50 || !PerkProfessionService.IsWoodMaterial(item) ||
                __instance == fromInventory || WoodInventoryCapacity.ExpandedOwner(__instance)) return true;
            WoodInventoryCapacity.Normalize(__instance);
            var batch = item.Clone();
            batch.m_shared = item.m_dropPrefab?.GetComponent<ItemDrop>()?.m_itemData?.m_shared ?? batch.m_shared;
            batch.m_stack = 50;
            bool all = __instance.AddItem(batch);
            int moved = all ? 50 : 50 - batch.m_stack;
            if (moved > 0) fromInventory.RemoveItem(item, moved);
            __instance.Changed(); fromInventory.Changed();
            return false;
        }
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new Type[] {
        typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
    internal static class WoodInventoryDragMovePatch
    {
        private static void Prefix(Inventory __instance, Inventory fromInventory, ItemDrop.ItemData item, ref int amount)
        {
            if (__instance != fromInventory && !WoodInventoryCapacity.ExpandedOwner(__instance) &&
                item != null && PerkProfessionService.IsWoodMaterial(item)) amount = Mathf.Min(amount, 50);
            WoodInventoryCapacity.Normalize(__instance);
        }
    }
}
