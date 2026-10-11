using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CookingInventoryCompatibility
    {
        internal sealed class Scope { internal ItemDrop.ItemData Previous; }
        [ThreadStatic] internal static ItemDrop.ItemData Incoming;
        internal static Scope Begin(ItemDrop.ItemData item)
        { var scope = new Scope { Previous = Incoming }; Incoming = item; return scope; }
        internal static void End(Scope scope) { if (scope != null) Incoming = scope.Previous; }
        internal static bool Match(ItemDrop.ItemData stored, ItemDrop.ItemData incoming) =>
            stored?.m_shared != null && incoming?.m_shared != null &&
            stored.m_shared.m_name == incoming.m_shared.m_name && stored.m_quality == incoming.m_quality &&
            stored.m_worldLevel == incoming.m_worldLevel && CookingProductIdentity.Compatible(stored, incoming);
        internal static int Space(Inventory inventory, ItemDrop.ItemData incoming)
        {
            if (inventory == null || incoming?.m_shared == null) return 0;
            long count = 0;
            foreach (var item in inventory.GetAllItems())
                if (Match(item, incoming)) count += Math.Max(0, item.m_shared.m_maxStackSize - item.m_stack);
            return (int)Math.Min(int.MaxValue, count);
        }
        internal static bool Fits(Inventory inventory, ItemDrop.ItemData item, int amount)
        {
            if (item?.m_shared == null || inventory == null) return false;
            if (amount <= 0) amount = item.m_stack;
            return amount <= (long)Space(inventory, item) + (long)inventory.GetEmptySlots() * Math.Max(1, item.m_shared.m_maxStackSize);
        }
    }

    [HarmonyPatch]
    internal static class CookingInventoryIncomingPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => new[] {
            AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData) }),
            AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) }) };
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Inventory __instance, ItemDrop.ItemData item, out CookingInventoryCompatibility.Scope __state)
        {
            __state = CookingInventoryCompatibility.Begin(item);
        }
        private static Exception Finalizer(CookingInventoryCompatibility.Scope __state, Exception __exception)
        { CookingInventoryCompatibility.End(__state); return __exception; }
    }
    [HarmonyPatch(typeof(Inventory), "FindFreeStackItem")]
    internal static class CookingInventoryStackFinderPatch
    {
        private static bool Prefix(Inventory __instance, ref ItemDrop.ItemData __result)
        {
            var item = CookingInventoryCompatibility.Incoming;
            if (!CookingProductIdentity.Relevant(item)) return true;
            __result = __instance.GetAllItems().FirstOrDefault(candidate =>
                CookingInventoryCompatibility.Match(candidate, item) && candidate.m_stack < candidate.m_shared.m_maxStackSize);
            return false;
        }
    }
    [HarmonyPatch(typeof(Inventory), "CanAddItem", new Type[] { typeof(ItemDrop.ItemData), typeof(int) })]
    internal static class CookingInventoryCapacityPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, int __1, ref bool __result)
        {
            if (!CookingProductIdentity.Relevant(item)) return true;
            __result = CookingInventoryCompatibility.Fits(__instance, item, __1); return false;
        }
    }
    [HarmonyPatch(typeof(Inventory), "CanAddItem", new Type[] { typeof(GameObject), typeof(int) })]
    internal static class CookingInventoryPrefabCapacityPatch
    {
        private static bool Prefix(Inventory __instance, GameObject __0, int __1, ref bool __result)
        {
            var item = CookingProductionScope.Current?.Template;
            var scope = CookingProductionScope.Current;
            if (item == null || scope.Inventory != __instance || __0 == null ||
                GatheringProgressionService.Normalize(__0.name) != scope.Prefab) return true;
            __result = CookingInventoryCompatibility.Fits(__instance, item, __1); return false;
        }
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.IsSameType))]
    internal static class CookingInventoryTypePatch
    {
        private static void Postfix(ItemDrop.ItemData __instance, ItemDrop.ItemData __0, ref bool __result)
        { if (__result && !CookingProductIdentity.Compatible(__instance, __0)) __result = false; }
    }
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.AutoStackItems))]
    internal static class CookingGroundStackPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); var quality = AccessTools.Field(typeof(ItemDrop.ItemData), "m_quality");
            var data = AccessTools.Field(typeof(ItemDrop), "m_itemData");
            var anchors = Enumerable.Range(1, Math.Max(0, code.Count-3)).Where(i => Equals(code[i-1].operand, quality) &&
                (code[i].opcode == OpCodes.Bne_Un || code[i].opcode == OpCodes.Bne_Un_S) &&
                code[i+1].opcode == OpCodes.Ldarg_0 && Equals(code[i+2].operand,data)).ToList();
            if (anchors.Count != 1) throw new InvalidOperationException("Cooking ground stack native contract changed.");
            int at = anchors[0] + 1; var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[at].labels); code[at].labels.Clear();
            first.blocks.AddRange(code[at].blocks); code[at].blocks.Clear();
            code.InsertRange(at,new[] { first, new CodeInstruction(OpCodes.Ldfld,data), new CodeInstruction(OpCodes.Ldloc_S,(byte)4),
                new CodeInstruction(OpCodes.Ldfld,data), new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CookingProductIdentity),nameof(CookingProductIdentity.Compatible))),
                new CodeInstruction(OpCodes.Brfalse,code[at-1].operand) });
            return code;
        }
    }
    // Whole requested output must fit before native creation's first chunk.
    [HarmonyPatch(typeof(Inventory), "AddItem", new Type[] { typeof(string),typeof(int),typeof(int),typeof(int),typeof(long),typeof(string),typeof(Vector2i),typeof(bool),typeof(bool),typeof(bool) })]
    internal static class CookingCreatedOutputAdmissionPatch
    {
        private static bool AddCreated(Inventory inventory, ItemDrop.ItemData item)
        { CookingProductionScope.StampCreated(inventory, item); return inventory.AddItem(item); }
        private static bool AddCreatedAt(Inventory inventory, ItemDrop.ItemData item, Vector2i position)
        { CookingProductionScope.StampCreated(inventory, item); return inventory.AddItem(item, position); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var automatic = AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData) });
            var positioned = AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) });
            int matches = 0;
            foreach (var instruction in code)
            {
                if (Equals(instruction.operand, automatic))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(CookingCreatedOutputAdmissionPatch), nameof(AddCreated)); matches++; }
                else if (Equals(instruction.operand, positioned))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(CookingCreatedOutputAdmissionPatch), nameof(AddCreatedAt)); matches++; }
            }
            if (matches != 2) throw new InvalidOperationException("Cooking private output chunk native contract changed.");
            return code;
        }
        private sealed class Creation { internal CookingProductionScope Scope; internal bool Previous; }
        private static bool Prefix(Inventory __instance, object[] __args, ref ItemDrop.ItemData __result, out Creation __state)
        {
            __state = null;
            var item = CookingProductionScope.Current?.Template;
            var scope = CookingProductionScope.Current;
            if (item == null || scope.Inventory != __instance ||
                !string.Equals(GatheringProgressionService.Normalize((string)__args[0]), scope.Prefab, StringComparison.Ordinal)) return true;
            var proposed = item.Clone(); proposed.m_quality = (int)__args[2];
            proposed.m_worldLevel = Game.m_worldLevel;
            CraftingOutcomeSnapshot.ExpectAll(__instance, TierDatabase.ItemKey(proposed), proposed.m_quality, (int)__args[1]);
            if (CookingInventoryCompatibility.Fits(__instance, proposed, (int)__args[1]))
            { __state = new Creation { Scope = scope, Previous = scope.Creates }; scope.Creates = true; return true; }
            __result = null; return false;
        }
        private static Exception Finalizer(Creation __state, Exception __exception)
        { if (__state != null) __state.Scope.Creates = __state.Previous; return __exception; }
    }
}
