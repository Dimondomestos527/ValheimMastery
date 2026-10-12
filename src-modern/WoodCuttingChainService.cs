using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class PendingWoodPiece
    {
        internal Component Target;
        internal Player Attacker;
        internal float ReadyAt;
        internal int InstanceId;
        internal short ToolTier;
        internal float ExpiresAt;
        internal UtilityWoodNode Provenance;
    }

    internal static class WoodCuttingChainService
    {
        private static readonly List<PendingWoodPiece> Pieces = new List<PendingWoodPiece>();
        private static readonly HashSet<int> Queued = new HashSet<int>();

        internal static void RegisterTree(TreeBase source, Player attacker, short toolTier) =>
            UtilityWoodLineage.RegisterRoot(source, attacker, toolTier);

        internal static void ObserveSpawn(Component component)
        {
            if (component == null) return;
            var node = UtilityWoodLineage.ObserveSpawn(component);
            if (node != null)
            {
                int id = component.GetInstanceID();
                if (!Queued.Add(id)) return;
                Pieces.Add(new PendingWoodPiece
                {
                    Target = component,
                    InstanceId = id,
                    ToolTier = node.Graph.ToolTier,
                    Attacker = node.Graph.Actor,
                    Provenance = node,
                    ReadyAt = Time.time + 0.35f + Pieces.Count * 0.12f,
                    ExpiresAt = Time.time + 8f
                });
                return;
            }
        }

        internal static void Tick()
        {
            UtilityWoodLineage.Tick();

            for (int i = Pieces.Count - 1; i >= 0; --i)
            {
                PendingWoodPiece pending = Pieces[i];
                if (pending == null || pending.Target == null || pending.Attacker == null)
                { if (pending != null) { Queued.Remove(pending.InstanceId); UtilityWoodLineage.Fail(pending.Provenance?.Graph); } Pieces.RemoveAt(i); continue; }
                if (Time.time < pending.ReadyAt) continue;
                if (Time.time > pending.ExpiresAt)
                { Pieces.RemoveAt(i); Queued.Remove(pending.InstanceId); UtilityWoodLineage.Fail(pending.Provenance?.Graph); continue; }
                ZNetView view = pending.Target.GetComponent<ZNetView>();
                if (view == null || !view.IsOwner())
                { pending.ReadyAt = Time.time + 0.20f; continue; }
                Pieces.RemoveAt(i);
                Queued.Remove(pending.InstanceId);
                UtilityWoodLineage.Bind(pending.Provenance);
                HitData hit = new HitData();
                hit.m_toolTier = pending.ToolTier;
                hit.m_point = pending.Target.transform.position;
                hit.SetAttacker(pending.Attacker);
                if (pending.Target is TreeLog log)
                {
                    float health = view.GetZDO().GetFloat(ZDOVars.s_health, log.m_health);
                    WoodCutting35Service.MakeLethal(hit, health, log.m_damages);
                    PerkVisualService.PlayProc(pending.Attacker, "woodcutting_35", log.transform.position, true, false);
                    using (UtilityWoodLineage.EnterGenerated(pending.Provenance, log, pending.Attacker, hit))
                        log.Damage(hit);
                }
                else if (pending.Target is Destructible destructible &&
                         (destructible.m_destructibleType & DestructibleType.Tree) != 0)
                {
                    float health = view.GetZDO().GetFloat(ZDOVars.s_health, destructible.m_health);
                    WoodCutting35Service.MakeLethal(hit, health, destructible.m_damages);
                    PerkVisualService.PlayProc(pending.Attacker, "woodcutting_35", destructible.transform.position, true, false);
                    using (UtilityWoodLineage.EnterGenerated(pending.Provenance, destructible, pending.Attacker, hit))
                        destructible.Damage(hit);
                }
            }
        }
    }

    [HarmonyPatch(typeof(TreeLog), "Awake")]
    internal static class WoodCutting35SpawnedLogPatch
    {
        private static void Postfix(TreeLog __instance) => WoodCuttingChainService.ObserveSpawn(__instance);
    }

    [HarmonyPatch(typeof(Destructible), "Awake")]
    internal static class WoodCutting35SpawnedTreePiecePatch
    {
        private static void Postfix(Destructible __instance)
        {
            if (__instance != null && (__instance.m_destructibleType & DestructibleType.Tree) != 0)
                WoodCuttingChainService.ObserveSpawn(__instance);
        }
    }
}
