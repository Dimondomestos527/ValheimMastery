using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Inventory owns both item instances. Native primary is retained for visuals.
    // Compatible activation means an audited, side-effect-free staging adapter.
    internal static class Combat100DualTrinkets
    {
        internal const string SlotKey = "VM_Combat100_SecondTrinket";
        [ThreadStatic] private static bool Committing;
        [ThreadStatic] private static int EquipmentDepth;

        internal static bool Eligible(Player p) => p != null && MasteryPlugin.Settings?.Enabled.Value == true &&
            p.m_nview?.IsOwner() == true && PerkRuntimeService.HasPerk(p, Skills.SkillType.Unarmed, 100);
        private static bool Trinket(ItemDrop.ItemData item) =>
            item?.m_shared != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket;
        internal static ItemDrop.ItemData RawSecondary(Humanoid h)
        {
            var p = h as Player;
            if (p == null) return null;
            foreach (var item in h.GetInventory().GetAllItems())
                if (item != h.m_trinketItem && Trinket(item) && item.m_equipped &&
                    item.m_customData.ContainsKey(SlotKey)) return item;
            return null;
        }
        internal static ItemDrop.ItemData Secondary(Humanoid h) => Eligible(h as Player) ? RawSecondary(h) : null;
        internal static bool SafeEffect(StatusEffect effect)
        {
            if (effect == null || (effect.GetType() != typeof(StatusEffect) && effect.GetType() != typeof(SE_Stats))) return false;
            if (effect is SE_Stats stats)
                return stats.m_healthUpFront == 0f && stats.m_staminaUpFront == 0f &&
                    stats.m_eitrUpFront == 0f && stats.m_adrenalineUpFront == 0f;
            return true;
        }
        private static bool Compatible(ItemDrop.ItemData a, ItemDrop.ItemData b) =>
            a != b && Trinket(a) && Trinket(b) && SafeEffect(a.m_shared.m_fullAdrenalineSE) &&
            SafeEffect(b.m_shared.m_fullAdrenalineSE);

        internal static bool Equip(Humanoid h, ItemDrop.ItemData item, bool triggerEquipEffects, ref bool result)
        {
            var p = h as Player;
            if (!Eligible(p) || !Trinket(item))
            {
                // Native replacement must not inherit a promoted, untracked third item.
                if (p != null && p.m_nview?.IsOwner() == true && Trinket(item) && !Eligible(p))
                {
                    var stale = RawSecondary(p);
                    if (stale != null) p.UnequipItem(stale, false);
                }
                return true;
            }
            if (h.m_trinketItem == null || h.m_trinketItem == item)
            { item.m_customData.Remove(SlotKey); return true; }
            if (h.IsItemEquiped(item) || !h.GetInventory().ContainsItem(item) ||
                h.InAttack() || h.InDodge() ||
                (h.IsPlayer() && !h.IsDead() && h.IsSwimming() && !h.IsOnGround()) ||
                (item.m_shared.m_useDurability && item.m_durability <= 0f))
            { result = false; return false; }
            if (!string.IsNullOrEmpty(item.m_shared.m_dlc) &&
                (DLCMan.instance == null || !DLCMan.instance.IsDLCInstalled(item.m_shared.m_dlc)))
            { result = false; p.Message(MessageHud.MessageType.Center, "$msg_dlcrequired"); return false; }
            if (Game.m_worldLevel > 0 && item.m_worldLevel < Game.m_worldLevel)
            { result = false; p.Message(MessageHud.MessageType.Center, "$msg_ng_item_too_low"); return false; }
            if (!PairPresent(p, h.m_trinketItem, item) || !Compatible(h.m_trinketItem, item))
            {
                // Explicit rejection; never silently fall back to an unsafe combined proc.
                result = false;
                p.Message(MessageHud.MessageType.Center, "Ці трінкети не підтримують спільну активацію.");
                return false;
            }
            if (!h.GetInventory().ContainsItem(item)) { result = false; return false; }
            var previous = Secondary(h);
            if (previous != null && previous != item) h.UnequipItem(previous, false);
            item.m_customData[SlotKey] = "1";
            item.m_equipped = true;
            Refresh(p);
            if (h.m_visEquipment != null && h.m_visEquipment.m_isPlayer && FejdStartup.instance == null)
                item.m_shared.m_equipEffect.Create(h.transform.position + Vector3.up,
                    h.transform.rotation, null, 1f, -1, h.GetZDOID());
            if (triggerEquipEffects) h.TriggerEquipEffect(item);
            result = true;
            return false;
        }
        private static bool PairPresent(Player p, ItemDrop.ItemData primary, ItemDrop.ItemData secondary) =>
            primary != null && secondary != null && primary.m_equipped &&
            p.GetInventory().ContainsItem(primary) && p.GetInventory().ContainsItem(secondary);
        internal static void Refresh(Player p)
        {
            p.UpdateModifiers();
            p.SetupEquipment();
            if (EquipmentDepth == 0)
                p.m_adrenaline = Mathf.Clamp(p.m_adrenaline, 0f, p.GetMaxAdrenaline());
        }
        internal static bool BeginEquipment(Humanoid h, ItemDrop.ItemData item)
        {
            if (!(h is Player p) || p.m_nview?.IsOwner() != true ||
                (RawSecondary(p) == null && !(Eligible(p) && Trinket(item)))) return false;
            EquipmentDepth++;
            return true;
        }
        internal static void EndEquipment(Humanoid h)
        {
            if (EquipmentDepth <= 0) throw new InvalidOperationException("Trinket equipment transaction underflow.");
            EquipmentDepth--;
            if (EquipmentDepth == 0) Refresh((Player)h);
        }
        internal static bool MayPromote(Player p) => EquipmentDepth == 0 && Eligible(p);
        internal static void AppendEffects(Humanoid h, HashSet<StatusEffect> desired)
        {
            var item = Secondary(h);
            if (item == null) return;
            if (item.m_shared.m_equipStatusEffect != null) desired.Add(item.m_shared.m_equipStatusEffect);
            if (item.m_shared.m_setStatusEffect != null && h.HaveSetEffect(item)) desired.Add(item.m_shared.m_setStatusEffect);
        }
        internal static void AddModifiers(Player p)
        {
            var secondary = Secondary(p);
            if (secondary == null) return;
            var fields = Player.s_equipmentModifierSourceFields;
            var values = p.m_equipmentModifierValues;
            if (fields == null || values == null) return;
            for (int i = 0; i < fields.Length && i < values.Length; i++)
                if (fields[i]?.FieldType == typeof(float))
                    values[i] += (float)fields[i].GetValue(secondary.m_shared);
        }
        internal static void Capacity(Player p, ref float result)
        {
            var secondary = Secondary(p);
            var primary = p?.m_trinketItem;
            if (secondary == null || !PairPresent(p, primary, secondary) || !Compatible(primary, secondary)) return;
            float a = primary.m_shared.m_maxAdrenaline, b = secondary.m_shared.m_maxAdrenaline;
            float baseline = result - a - b;
            float isolatedA = baseline + a, isolatedB = baseline + b;
            if (float.IsFinite(isolatedA) && float.IsFinite(isolatedB) && isolatedA > 0f && isolatedB > 0f)
            {
                float capacity = .75f * isolatedA + .75f * isolatedB;
                if (float.IsFinite(capacity) && capacity > 0f) result = capacity;
            }
        }

        // Returns true when the dual adapter owns this full-bar branch, including rejection.
        // Native gain/rates/curve and subsequent threshold status logic remain native.
        internal static bool Activate(Player p)
        {
            var secondary = Secondary(p);
            if (secondary == null) return false;
            if (Committing) return true;
            if (!PairPresent(p, p.m_trinketItem, secondary) || !Compatible(p.m_trinketItem, secondary)) return false;
            float cost = p.GetMaxAdrenaline();
            if (!float.IsFinite(cost) || cost <= 0f || p.m_adrenaline < cost) return true;
            var manager = p.GetSEMan();
            if (manager == null) return true;
            var definitions = new Dictionary<int, StatusEffect>();
            foreach (var item in p.GetInventory().GetAllItems())
            {
                if (!item.m_equipped || item.m_shared.m_fullAdrenalineSE == null) continue;
                var effect = item.m_shared.m_fullAdrenalineSE;
                if (!SafeEffect(effect) || !effect.CanAdd(p)) return true;
                definitions[effect.NameHash()] = effect;
            }
            if (!definitions.ContainsKey(p.m_trinketItem.m_shared.m_fullAdrenalineSE.NameHash()) ||
                !definitions.ContainsKey(secondary.m_shared.m_fullAdrenalineSE.NameHash())) return true;
            var staged = new List<StatusEffect>();
            var existing = new List<StatusEffect>();
            Committing = true;
            try
            {
                // Staging suppresses native messages/start effects on clones only.
                // Exact classes + zero upfront resources exclude irreversible Setup callbacks.
                foreach (var entry in definitions)
                {
                    var current = manager.GetStatusEffect(entry.Key);
                    if (current != null)
                    {
                        if (!SafeEffect(current)) return true;
                        existing.Add(current);
                        continue;
                    }
                    var clone = entry.Value.Clone();
                    staged.Add(clone);
                    clone.m_startMessage = "";
                    clone.m_startEffects = new EffectList();
                    clone.m_hitVariant = -1;
                    clone.Setup(p);
                    clone.SetLevel(0, 0f);
                }
                foreach (var current in existing) current.ResetTime();
                foreach (var clone in staged)
                {
                    manager.m_statusEffects.Add(clone);
                    manager.m_statusEffectsHashSet.Add(clone.NameHash());
                }
                p.m_adrenaline = 0f; // Native full-bar excess policy: one proc, reset.
                // Exact postcommit admission for direct-published timed buffs.
                // Observer failure never rolls back the committed equipment proc.
                // Passive-test build deliberately excludes the unwired paid Muster observer.
                // Commit is now complete. Cosmetic callbacks cannot undo gameplay.
                foreach (var clone in staged)
                {
                    var template = definitions[clone.NameHash()];
                    clone.m_startMessage = template.m_startMessage;
                    clone.m_startEffects = template.m_startEffects;
                    try { clone.TriggerStartEffects(); }
                    catch (Exception error) { MasteryPlugin.Log.LogWarning("[Fists100] Trinket cue failed: " + error.Message); }
                }
                staged.Clear(); // Committed clones are owned by SEMan.
                return true;
            }
            catch (Exception error)
            {
                MasteryPlugin.Log.LogError("[Fists100] Atomic trinket staging failed: " + error.Message);
                return true;
            }
            finally
            {
                foreach (var clone in staged)
                    if (!manager.m_statusEffects.Contains(clone)) UnityEngine.Object.Destroy(clone);
                Committing = false;
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class Combat100TrinketEquipPatch
    {
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, bool __1,
            ref bool __result, out bool __state)
        {
            __state = Combat100DualTrinkets.BeginEquipment(__instance, item);
            return Combat100DualTrinkets.Equip(__instance, item, __1, ref __result);
        }
        private static Exception Finalizer(Humanoid __instance, bool __state, Exception __exception)
        {
            if (__state) Combat100DualTrinkets.EndEquipment(__instance);
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    internal static class Combat100TrinketUnequipPatch
    {
        private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, out bool __state)
        {
            __state = __instance is Player player && player.m_nview?.IsOwner() == true &&
                (Combat100DualTrinkets.RawSecondary(player) != null ||
                 item?.m_customData.ContainsKey(Combat100DualTrinkets.SlotKey) == true);
        }
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __state)
        {
            if (!__state) return;
            // Native handles flags, setup and equip/unequip cues first. Promote a surviving
            // equipped secondary after primary removal without re-equipping its effect.
            item?.m_customData.Remove(Combat100DualTrinkets.SlotKey);
            if (!(__instance is Player player)) return;
            var second = Combat100DualTrinkets.RawSecondary(player);
            if (player.m_trinketItem == null && second != null)
            {
                if (Combat100DualTrinkets.MayPromote(player))
                {
                    player.m_trinketItem = second;
                    second.m_customData.Remove(Combat100DualTrinkets.SlotKey);
                }
                else player.UnequipItem(second, false);
            }
            if (item?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Trinket)
                Combat100DualTrinkets.Refresh(player);
        }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipAllItems))]
    internal static class Combat100TrinketUnequipAllPatch
    {
        private static void Prefix(Humanoid __instance)
        {
            var second = Combat100DualTrinkets.RawSecondary(__instance);
            if (second != null) __instance.UnequipItem(second, false);
        }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsItemEquiped))]
    internal static class Combat100TrinketEquippedPatch
    {
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        { if (item != null && item == Combat100DualTrinkets.RawSecondary(__instance)) __result = true; }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetSetCount))]
    internal static class Combat100TrinketSetPatch
    {
        private static void Postfix(Humanoid __instance, string __0, ref int __result)
        { var item = Combat100DualTrinkets.Secondary(__instance); if (item?.m_shared.m_setName == __0) __result++; }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetEquipmentWeight))]
    internal static class Combat100TrinketWeightPatch
    {
        private static void Postfix(Humanoid __instance, ref float __result)
        { var item = Combat100DualTrinkets.Secondary(__instance); if (item != null) __result += item.GetWeight(); }
    }
    [HarmonyPatch(typeof(Humanoid), "UpdateEquipment")]
    internal static class Combat100TrinketDurabilityPatch
    {
        private static void Postfix(Humanoid __instance, float dt)
        { var item = Combat100DualTrinkets.Secondary(__instance); if (item?.m_shared.m_useDurability == true) __instance.DrainEquipedItemDurability(item, dt); }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.UpdateModifiers))]
    internal static class Combat100TrinketModifiersPatch
    { private static void Postfix(Player __instance) => Combat100DualTrinkets.AddModifiers(__instance); }
    [HarmonyPatch(typeof(Player), nameof(Player.GetMaxAdrenaline))]
    internal static class Combat100TrinketCapacityPatch
    { private static void Postfix(Player __instance, ref float __result) => Combat100DualTrinkets.Capacity(__instance, ref __result); }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipmentStatusEffects))]
    internal static class Combat100TrinketEquipmentEffectsPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            int index = code.FindIndex(c => c.opcode == OpCodes.Ldfld &&
                c.operand is FieldInfo field && field.Name == "m_equipmentStatusEffects");
            if (index < 1 || code[index - 1].opcode != OpCodes.Ldarg_0)
                throw new InvalidOperationException("Native equipment effect diff shape changed.");
            int start = index - 1;
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[start].labels); code[start].labels.Clear();
            code.InsertRange(start, new[] { first, new CodeInstruction(OpCodes.Ldloc_0),
                CodeInstruction.Call(typeof(Combat100DualTrinkets), nameof(Combat100DualTrinkets.AppendEffects)) });
            return code;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.AddAdrenaline))]
    internal static class Combat100TrinketActivationPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            int all = code.FindIndex(c => c.operand is MethodInfo m && m.DeclaringType == typeof(Inventory) && m.Name == "GetAllItems");
            int start = all - 2;
            if (start < 0 || code[start].opcode != OpCodes.Ldarg_0)
                throw new InvalidOperationException("Native full-adrenaline branch changed.");
            CodeInstruction exit = null;
            for (int i = start - 1; i >= Math.Max(0, start - 12); i--)
                if ((code[i].opcode == OpCodes.Blt_Un || code[i].opcode == OpCodes.Blt_Un_S) && code[i].operand is Label)
                { exit = code[i]; break; }
            if (exit == null) throw new InvalidOperationException("Native adrenaline continuation missing.");
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[start].labels); code[start].labels.Clear();
            code.InsertRange(start, new[] { first,
                CodeInstruction.Call(typeof(Combat100DualTrinkets), nameof(Combat100DualTrinkets.Activate)),
                new CodeInstruction(OpCodes.Brtrue, exit.operand) });
            return code;
        }
    }
    [HarmonyPatch(typeof(Player), "EquipInventoryItems")]
    internal static class Combat100TrinketRestorePatch
    {
        private static void Prefix(Player __instance, out List<ItemDrop.ItemData> __state)
        {
            __state = new List<ItemDrop.ItemData>();
            foreach (var item in __instance.GetInventory().GetAllItems())
                if (item.m_equipped && item.m_customData.ContainsKey(Combat100DualTrinkets.SlotKey))
                { __state.Add(item); item.m_equipped = false; }
        }
        private static void Postfix(Player __instance, List<ItemDrop.ItemData> __state)
        {
            foreach (var item in __state)
            {
                if (Combat100DualTrinkets.Eligible(__instance))
                {
                    if (!__instance.EquipItem(item, false))
                    { item.m_equipped = false; item.m_customData.Remove(Combat100DualTrinkets.SlotKey); }
                }
                else { item.m_equipped = false; item.m_customData.Remove(Combat100DualTrinkets.SlotKey); }
            }
        }
    }
    [HarmonyPatch(typeof(Character), "CustomFixedUpdate")]
    internal static class Combat100TrinketEligibilityPatch
    {
        private static void Postfix(Character __instance)
        {
            var player = __instance as Player;
            if (player?.m_nview?.IsOwner() != true) return;
            if (Combat100DualTrinkets.Eligible(player))
            {
                var second = Combat100DualTrinkets.RawSecondary(player);
                if (second != null && (player.m_trinketItem == null ||
                    !player.GetInventory().ContainsItem(player.m_trinketItem) || !player.m_trinketItem.m_equipped))
                {
                    player.m_trinketItem = second;
                    second.m_customData.Remove(Combat100DualTrinkets.SlotKey);
                    Combat100DualTrinkets.Refresh(player);
                }
                return;
            }
            var item = Combat100DualTrinkets.RawSecondary(player);
            if (item == null) return;
            player.UnequipItem(item, false);
            Combat100DualTrinkets.Refresh(player);
        }
    }
}
