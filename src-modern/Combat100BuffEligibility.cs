using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Concrete successful application provenance; neither a name whitelist nor
    // equipment presence grants eligibility. Resource payload timers are excluded.
    internal static class Combat100BuffEligibility
    {
        private sealed class Proof { internal int Hash; }
        private static readonly ConditionalWeakTable<StatusEffect, Proof> Proven = new ConditionalWeakTable<StatusEffect, Proof>();
        private static readonly FieldInfo[] Fields = typeof(SE_Stats).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        internal static bool Eligible(StatusEffect effect) => effect != null && Proven.TryGetValue(effect, out var proof) &&
            proof.Hash == effect.NameHash() && PositiveContinuous(effect);
        internal static void Record(Player player, StatusEffect definition)
        {
            if (player == null || player != Player.m_localPlayer || player.m_nview?.IsOwner() != true ||
                !PositiveContinuous(definition)) return;
            var actual = player.GetSEMan()?.GetStatusEffect(definition.NameHash());
            if (actual == null || actual.GetType() != definition.GetType() || !PositiveContinuous(actual)) return;
            Proven.Remove(actual); Proven.Add(actual, new Proof { Hash = definition.NameHash() });
            Combat100Muster.ObserveApplied(player.GetSEMan());
        }
        internal static void RecordTrinketCommit(Player player)
        {
            // Called only from the confirmed native or atomic-dual full-bar commit.
            // Cosmetic observation cannot cancel already committed gameplay.
            try
            {
                if (player == null || player != Player.m_localPlayer || player.m_nview?.IsOwner() != true) return;
                foreach (var item in player.GetInventory().GetAllItems())
                    if (item?.m_equipped == true && item.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Trinket)
                        Record(player, item.m_shared.m_fullAdrenalineSE);
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Combat100] Trinket buff provenance failed: " + error.Message); }
        }
        internal static bool PositiveContinuous(StatusEffect effect)
        {
            if (effect?.GetType() != typeof(SE_Stats) || effect.m_hidden || effect.m_cooldownIcon ||
                effect.m_cooldown != 0f || (int)effect.m_attributes != 0 || !float.IsFinite(effect.m_ttl) || effect.m_ttl <= 0f) return false;
            var stats = (SE_Stats)effect;
            bool positive = false;
            foreach (var field in Fields)
            {
                string name = field.Name;
                if (field.FieldType == typeof(float))
                {
                    float value = (float)field.GetValue(stats);
                    if (!float.IsFinite(value)) return false;
                    float baseline = name == "m_healthOverTimeInterval" ? 5f :
                        name == "m_healthRegenMultiplier" || name == "m_staminaRegenMultiplier" || name == "m_eitrRegenMultiplier" ||
                        name == "m_damageModifier" || name == "m_pheromoneLevelUpMultiplier" ? 1f : 0f;
                    bool increasing = name == "m_healthRegenMultiplier" || name == "m_staminaRegenMultiplier" || name == "m_eitrRegenMultiplier" ||
                        name == "m_addArmor" || name == "m_armorMultiplier" || name == "m_addMaxCarryWeight" ||
                        name == "m_speedModifier" || name == "m_swimSpeedModifier" || name == "m_timedBlockBonus";
                    bool decreasing = name == "m_runStaminaDrainModifier" || name == "m_jumpStaminaUseModifier" ||
                        name == "m_attackStaminaUseModifier" || name == "m_blockStaminaUseModifier" || name == "m_dodgeStaminaUseModifier" ||
                        name == "m_swimStaminaUseModifier" || name == "m_homeItemStaminaUseModifier" || name == "m_sneakStaminaUseModifier" ||
                        name == "m_staggerModifier" || name == "m_fallDamageModifier";
                    if (increasing) { if (value < baseline) return false; positive |= value > baseline; }
                    else if (decreasing) { if (value < -1f || value > 0f) return false; positive |= value < 0f; }
                    else if (value != baseline) return false; // Unknown/resource/XP/geometry/adrenaline stays native-only.
                }
                else if (field.FieldType == typeof(bool)) { if ((bool)field.GetValue(stats)) return false; }
                else if (field.FieldType.IsEnum || field.FieldType == typeof(int))
                { if (Convert.ToInt32(field.GetValue(stats)) != 0) return false; }
                else if (field.FieldType == typeof(Vector3))
                { var vector = (Vector3)field.GetValue(stats); if (vector.x != 0f || vector.y != 0f || vector.z != 0f) return false; }
                else if (name == "m_mods")
                {
                    if (stats.m_mods == null) return false;
                    foreach (var pair in stats.m_mods)
                    {
                        int mod = (int)pair.m_modifier;
                        // Single native damage channel only; ordinal ordering is NOT strength.
                        if (pair.m_type != HitData.DamageType.Blunt && pair.m_type != HitData.DamageType.Slash &&
                            pair.m_type != HitData.DamageType.Pierce && pair.m_type != HitData.DamageType.Chop &&
                            pair.m_type != HitData.DamageType.Pickaxe && pair.m_type != HitData.DamageType.Fire &&
                            pair.m_type != HitData.DamageType.Frost && pair.m_type != HitData.DamageType.Lightning &&
                            pair.m_type != HitData.DamageType.Poison && pair.m_type != HitData.DamageType.Spirit &&
                            pair.m_type != HitData.DamageType.NonPlayer) return false;
                        if (mod == 0) continue;
                        if (mod != 1 && mod != 3 && mod != 5 && mod != 7) return false;
                        positive = true;
                    }
                }
                else if (name == "m_percentigeDamageModifiers")
                { foreach (var channel in typeof(HitData.DamageTypes).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (channel.FieldType != typeof(float) || (float)channel.GetValue(stats.m_percentigeDamageModifiers) != 0f) return false; }
                else if (name == "m_tickEffect")
                { if (stats.m_tickEffect?.m_effectPrefabs?.Length > 0) return false; }
                else if (field.GetValue(stats) != null) return false;
            }
            return positive;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.AddAdrenaline))]
    internal static class Combat100MusterTrinketProofPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var matches = code.Select((c, i) => new { c, i }).Where(x => x.c.opcode == OpCodes.Ldfld &&
                x.c.operand is FieldInfo field && field.DeclaringType == typeof(Player) && field.Name == "m_adrenalinePopEffects").ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Native full-adrenaline cosmetic seam changed.");
            int start = matches[0].i - 1;
            if (start < 3 || code[start].opcode != OpCodes.Ldarg_0 ||
                (code[start - 1].opcode != OpCodes.Brfalse && code[start - 1].opcode != OpCodes.Brfalse_S) ||
                code[start - 3].opcode != OpCodes.Stfld || !(code[start - 3].operand is FieldInfo sink) || sink.Name != "m_adrenaline")
                throw new InvalidOperationException("Native committed full-adrenaline gate changed.");
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[start].labels); code[start].labels.Clear();
            code.InsertRange(start, new[] { first, CodeInstruction.Call(typeof(Combat100BuffEligibility), nameof(Combat100BuffEligibility.RecordTrinketCommit)) });
            return code;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class Combat100MusterMeadProofPatch
    {
        // Cooking35's ordinary postfix finalizes native TTL first. No template mutation.
        [HarmonyPriority(-100)]
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || item?.m_shared?.m_isDrink != true || item.m_shared.m_food > 0f) return;
            Combat100BuffEligibility.Record(__instance, item.m_shared.m_consumeStatusEffect);
        }
    }
}
