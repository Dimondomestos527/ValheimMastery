using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class WoodBonusResourceContext
    {
        [ThreadStatic] internal static Player Player;
        [ThreadStatic] internal static Vector3 Position;
        [ThreadStatic] internal static bool Active;
        internal static void Begin(HitData hit, long sender, Vector3 position)
        {
            Active = true;
            Player = hit?.GetAttacker() as Player;
            if (Player == null)
            {
                ZNetPeer peer = ZNet.instance?.GetPeer(sender);
                if (peer != null) Player = OwnerSkillAuthority.ResolvePlayer(peer);
            }
            Position = position;
        }
        internal static void End() { Active = false; Player = null; Position = Vector3.zero; }
    }

    [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
    [HarmonyPriority(Priority.First)]
    internal static class WoodBonusTreeContextPatch
    {
        private static void Prefix(TreeBase __instance, long sender, HitData hit) => WoodBonusResourceContext.Begin(hit, sender, __instance.transform.position);
        private static void Postfix() => WoodBonusResourceContext.End();
        private static Exception Finalizer(Exception __exception) { WoodBonusResourceContext.End(); return __exception; }
    }

    [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
    [HarmonyPriority(Priority.First)]
    internal static class WoodBonusLogContextPatch
    {
        private static void Prefix(TreeLog __instance, long sender, HitData hit) => WoodBonusResourceContext.Begin(hit, sender, __instance.transform.position);
        private static void Postfix() => WoodBonusResourceContext.End();
        private static Exception Finalizer(Exception __exception) { WoodBonusResourceContext.End(); return __exception; }
    }

    [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new[] { typeof(int) })]
    internal static class WoodPassiveBonusDropPatch
    {
        private static void Postfix(ref List<GameObject> __result)
        {
            Player player = WoodBonusResourceContext.Player;
            if (WoodBonusResourceContext.Active && player == null && MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogWarning("[ResourceBonus] Wood drop has no resolved attacker; bonus skipped.");
            if (player == null || __result == null || __result.Count == 0) return;
            float chance = Mathf.Clamp01(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.WoodCutting) * 0.005f);
            List<GameObject> eligible = __result.FindAll(prefab => prefab != null &&
                IsValuableWood(prefab.name));
            if (eligible.Count == 0) return;
            if (!PerkRuntimeService.RollChance(chance)) return;
            GameObject resource = eligible[UnityEngine.Random.Range(0, eligible.Count)];
            int bonusCount = UnityEngine.Random.Range(2, 6); // int upper bound is exclusive: 2..5.
            for (int i = 0; i < bonusCount; i++) __result.Add(resource);
            PerkVisualService.PlayWoodResourceProc(player, WoodBonusResourceContext.Position, bonusCount);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[ResourceBonus] WoodCutting +" + bonusCount + " " + resource.name +
                    " from tree/log drop, chance=" + chance.ToString("0.###"));
        }

        private static bool IsValuableWood(string raw)
        {
            string name = (raw ?? string.Empty).Replace("(Clone)", string.Empty).Trim();
            return !GatheringProgressionService.IsPlainWood(name) &&
                (PerkProfessionService.IsWoodMaterial(name) || name.EndsWith("Wood", StringComparison.OrdinalIgnoreCase));
        }
    }
}
