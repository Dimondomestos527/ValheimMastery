using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasterIdolGuardTierStats
    {
        private sealed class TierCache
        {
            internal ZNet Session;
            internal ZDOMan Manager;
            internal WorldGenerator Generator;
            internal ZDO Data;
            internal long World;
            internal ulong Generation;
            internal string Home;
            internal Vector3 Bench;
            internal int Tier;
        }
        private static readonly ConditionalWeakTable<Character, TierCache> Tiers =
            new ConditionalWeakTable<Character, TierCache>();

        internal static bool IsGuard(Character actor)
        {
            if (actor == null || actor is Player || actor.m_nview == null || !actor.m_nview.IsValid()) return false;
            var data = actor.m_nview.GetZDO();
            return data != null && ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data) &&
                MasterIdolForestGuardState.ModuleBorn(data);
        }

        private static bool Owned(Character actor) => IsGuard(actor) && actor.m_nview.IsOwner();
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static int BiomeTier(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Swamp: return 1;
                case Heightmap.Biome.Mountain: return 2;
                case Heightmap.Biome.Plains: return 3;
                case Heightmap.Biome.Mistlands: return 4;
                case Heightmap.Biome.AshLands: return 5;
                case Heightmap.Biome.DeepNorth: return 6;
                default: return 0;
            }
        }

        internal static int Tier(Character actor)
        {
            if (!Owned(actor) || !MasterIdolResidentPolicy.TryHome(actor, out var home) ||
                home == null || home.Type != "BlackForest") return 0;
            var net = ZNet.instance;
            var generator = WorldGenerator.instance;
            var point = home.Home;
            if (net?.GetWorld() == null || generator == null ||
                !Finite(point.x) || !Finite(point.y) || !Finite(point.z)) return 0;
            var data = actor.m_nview.GetZDO();
            long world = net.GetWorldUID();
            var cache = Tiers.GetValue(actor, _ => new TierCache());
            if (cache.Session != net || cache.Manager != ZDOMan.instance || cache.Generator != generator ||
                !ReferenceEquals(cache.Data, data) || cache.World != world ||
                cache.Generation != MasterIdolLoadIdentity.Generation || cache.Home != home.Id ||
                cache.Bench.x != point.x || cache.Bench.y != point.y || cache.Bench.z != point.z)
            {
                cache.Session = net; cache.Manager = ZDOMan.instance; cache.Generator = generator;
                cache.Data = data; cache.World = world; cache.Generation = MasterIdolLoadIdentity.Generation;
                cache.Home = home.Id; cache.Bench = point;
                cache.Tier = BiomeTier(generator.GetBiome(point));
            }
            return cache.Tier;
        }

        internal static void EnsureLevel(Character actor)
        {
            if (!Owned(actor) || actor.IsDead() || actor.GetLevel() == 3) return;
            float health = actor.GetHealth();
            if (!Finite(health) || health <= 0) return;
            // Awake removes the full-health key. Materialize the SAME HP before raising its fallback max.
            // SetHealth afterward could incorrectly no-op against native m_lastHealth.
            actor.m_nview.GetZDO().Set(ZDOVars.s_health, health);
            actor.SetLevel(3);
        }

        internal struct BirthState
        {
            internal ZDO Data;
            internal bool FreshFull;
        }
        internal static BirthState BeforeStamp(Character actor)
        {
            if (actor == null || actor is Player || actor.m_nview?.IsValid() != true ||
                !actor.m_nview.IsOwner() || actor.IsDead()) return default;
            var data = actor.m_nview.GetZDO();
            if (data == null || !data.Persistent || !ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data) ||
                data.GetBool(MasterIdolForestGuardState.Marker, false) || !MasterIdolResidents.Family(actor)) return default;
            float health = actor.GetHealth(), maximum = actor.GetMaxHealth();
            return new BirthState { Data = data, FreshFull = Finite(health) && health > 0 && health == maximum };
        }
        internal static void AfterStamp(Character actor, BirthState before)
        {
            EnsureLevel(actor);
            if (before.FreshFull && Owned(actor) && !actor.IsDead() &&
                ReferenceEquals(before.Data, actor.m_nview.GetZDO()) && actor.GetLevel() == 3)
                actor.SetHealth(actor.GetMaxHealth()); // New birth initialization ONLY, never load/rehome/owner transfer.
        }

        internal static int DropLevel(Character actor) => IsGuard(actor) ? 1 : actor.GetLevel();
        internal static bool ApplyTierArmor(Character actor, HitData hit)
        {
            if (hit == null) return false;
            int tier = Tier(actor);
            if (tier <= 0) return false;
            // The native NPC world armor is part of the SAME armor value, not a second nonlinear pass.
            int level = Game.m_worldLevel;
            float nativeArmor = level > 0 ? unchecked(level * Game.instance.m_worldLevelEnemyBaseAC) : 0;
            hit.ApplyArmor(nativeArmor + 10f * tier);
            return true;
        }
        internal static IEnumerable<CodeInstruction> ArmorAfterResistance(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var native = AccessTools.Method(typeof(HitData), nameof(HitData.ApplyResistance),
                new[] { typeof(HitData.DamageModifiers), typeof(HitData.DamageModifier).MakeByRefType() });
            var apply = AccessTools.Method(typeof(MasterIdolGuardTierStats), nameof(ApplyTierArmor));
            var isPlayer = AccessTools.Method(typeof(Character), nameof(Character.IsPlayer));
            var armor = AccessTools.Method(typeof(HitData), nameof(HitData.ApplyArmor));
            var durability = AccessTools.Method(typeof(Character), "DamageArmorDurability");
            var worldLevel = AccessTools.Field(typeof(Game), nameof(Game.m_worldLevel));
            var worldArmor = AccessTools.Field(typeof(Game), nameof(Game.m_worldLevelEnemyBaseAC));
            var game = AccessTools.PropertyGetter(typeof(Game), nameof(Game.instance));
            int seam = -1, matches = 0, playerEnd = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(native)) { seam = i; matches++; }
                if (code[i].Calls(durability)) { if (playerEnd != -1) throw new InvalidOperationException("Guard armor: ambiguous durability"); playerEnd = i + 1; }
            }
            if (matches != 1 || seam + 3 >= code.Count || code[seam + 1].opcode != OpCodes.Ldarg_0 ||
                !code[seam + 2].Calls(isPlayer) ||
                (code[seam + 3].opcode != OpCodes.Brfalse && code[seam + 3].opcode != OpCodes.Brfalse_S) ||
                !(code[seam + 3].operand is Label npcLabel) || playerEnd < 0 || playerEnd >= code.Count ||
                (code[playerEnd].opcode != OpCodes.Br && code[playerEnd].opcode != OpCodes.Br_S) ||
                !(code[playerEnd].operand is Label endLabel))
                throw new InvalidOperationException("Guard armor: unexpected native player armor boundary");
            int npc = code.FindIndex(i => i.labels.Contains(npcLabel));
            int end = code.FindIndex(i => i.labels.Contains(endLabel));
            // Exact native NPC branch, ending BEFORE poison/fire/status processing. Never skip that suffix.
            if (npc <= playerEnd || end != npc + 10 ||
                code[npc].opcode != OpCodes.Ldsfld || !Equals(code[npc].operand, worldLevel) ||
                code[npc + 1].opcode != OpCodes.Ldc_I4_0 ||
                (code[npc + 2].opcode != OpCodes.Ble && code[npc + 2].opcode != OpCodes.Ble_S) ||
                !(code[npc + 2].operand is Label npcEndLabel) ||
                code.FindIndex(i => i.labels.Contains(npcEndLabel)) != end || code[npc + 3].opcode != OpCodes.Ldarg_2 ||
                code[npc + 4].opcode != OpCodes.Ldsfld || !Equals(code[npc + 4].operand, worldLevel) ||
                !code[npc + 5].Calls(game) || code[npc + 6].opcode != OpCodes.Ldfld ||
                !Equals(code[npc + 6].operand, worldArmor) || code[npc + 7].opcode != OpCodes.Mul ||
                code[npc + 8].opcode != OpCodes.Conv_R4 || !code[npc + 9].Calls(armor))
                throw new InvalidOperationException("Guard armor: unexpected native NPC armor boundary");
            for (int i = 0; i < code.Count; i++)
            {
                yield return code[i]; // Preserve original labels/blocks/branches, including the entire suffix.
                if (i != seam) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, apply);
                yield return new CodeInstruction(OpCodes.Brtrue, endLabel);
            }
        }
        internal static IEnumerable<CodeInstruction> NormalDrops(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(Character), nameof(Character.GetLevel));
            var replacement = AccessTools.Method(typeof(MasterIdolGuardTierStats), nameof(DropLevel));
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; matches++; }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Guard normal drops: expected one native level call, found " + matches);
        }
    }

    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class MasterIdolGuardStatsAwakePatch
    { private static void Postfix(Character __instance) => MasterIdolGuardTierStats.EnsureLevel(__instance); }

    [HarmonyPatch(typeof(Character), "CustomFixedUpdate")]
    internal static class MasterIdolGuardStatsOwnerPatch
    { private static void Postfix(Character __instance) => MasterIdolGuardTierStats.EnsureLevel(__instance); }

    [HarmonyPatch(typeof(MasterIdolForestGuardState), "Stamp")]
    internal static class MasterIdolGuardStatsBirthPatch
    {
        private static void Prefix(Character actor, out MasterIdolGuardTierStats.BirthState __state) =>
            __state = MasterIdolGuardTierStats.BeforeStamp(actor);
        private static void Postfix(Character actor, MasterIdolGuardTierStats.BirthState __state) =>
            MasterIdolGuardTierStats.AfterStamp(actor, __state);
    }

    [HarmonyPatch(typeof(LevelEffects), "SetupLevelVisualization")]
    internal static class MasterIdolGuardNormalAppearancePatch
    { private static bool Prefix(LevelEffects __instance) => !MasterIdolGuardTierStats.IsGuard(__instance.m_character); }

    [HarmonyPatch(typeof(LevelEffects), "GetColorChanges")]
    internal static class MasterIdolGuardNormalColorPatch
    {
        private static bool Prefix(LevelEffects __instance, ref float __0, ref float __1, ref float __2)
        {
            if (!MasterIdolGuardTierStats.IsGuard(__instance.m_character)) return true;
            __0 = __1 = __2 = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterDrop), "GenerateDropList")]
    internal static class MasterIdolGuardNormalDropsPatch
    { private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => MasterIdolGuardTierStats.NormalDrops(instructions); }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class MasterIdolGuardTierArmorPatch
    { private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => MasterIdolGuardTierStats.ArmorAfterResistance(instructions); }

    [HarmonyPatch(typeof(Attack), "GetLevelDamageFactor")]
    internal static class MasterIdolGuardTierDamagePatch
    {
        private static void Postfix(Attack __instance, ref float __result)
        {
            int tier = MasterIdolGuardTierStats.Tier(__instance.m_character);
            if (tier > 0) __result *= 1f + .10f * tier;
        }
    }
}
