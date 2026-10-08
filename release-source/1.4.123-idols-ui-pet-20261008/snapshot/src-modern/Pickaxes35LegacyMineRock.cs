using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    /// <summary>
    /// Adds one ordinary drop when a legacy MineRock area is destroyed. This deliberately does not
    /// patch ItemDrop.OnCreateNew: Harmony-binding that global creation hook stalls the current server build.
    /// </summary>
    [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
    internal static class PickaxesPassiveLegacyMineRockPatch
    {
        internal sealed class State
        {
            internal MineRock Rock;
            internal HitData Hit;
            internal string HealthKey;
        }

        private static void Prefix(MineRock __instance, HitData __1, int __2, out State __state)
        {
            __state = null;
            Player player = __1?.GetAttacker() as Player;
            if (__instance == null || player == null || __instance.m_nview == null || !__instance.m_nview.IsOwner())
                return;

            string healthKey = "Health" + __2;
            if (__instance.m_nview.GetZDO().GetFloat(healthKey, __instance.GetHealth()) <= 0f)
                return;

            __state = new State { Rock = __instance, Hit = __1, HealthKey = healthKey };
        }

        private static void Postfix(State __state)
        {
            if (__state == null || __state.Rock == null || __state.Hit == null || __state.Rock.m_nview == null ||
                __state.Rock.m_nview.GetZDO().GetFloat(__state.HealthKey, __state.Rock.GetHealth()) > 0f ||
                !PerkRuntimeService.RollChance(Mathf.Clamp01(PerkRuntimeService.GetActualSkillLevel(
                    __state.Hit.GetAttacker() as Player, Skills.SkillType.Pickaxes) * 0.005f)))
                return;

            List<GameObject> drops = __state.Rock.m_dropItems?.GetDropList();
            if (drops == null || drops.Count == 0)
                return;

            drops.RemoveAll(prefab => prefab == null || GatheringProgressionService.IsPlainStone(prefab.name));
            if (drops.Count == 0) return;
            GameObject template = drops[UnityEngine.Random.Range(0, drops.Count)];
            if (template == null)
                return;

            GameObject extra = UnityEngine.Object.Instantiate(template,
                __state.Hit.m_point + UnityEngine.Random.insideUnitSphere * 0.25f, Quaternion.identity);
            ItemDrop.OnCreateNew(extra, false);

            Player player = __state.Hit.GetAttacker() as Player;
            if (player != null)
                PerkVisualService.PlayPickaxeResourceProc(player, __state.Hit.m_point);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("Pickaxes passive extra resource from legacy MineRock.");
        }
    }
}

