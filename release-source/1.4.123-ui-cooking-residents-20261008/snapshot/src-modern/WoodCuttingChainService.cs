using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class FelledTreeChain
    {
        internal Vector3 Origin;
        internal Player Attacker;
        internal float ExpiresAt;
        internal short ToolTier;
    }

    internal sealed class PendingWoodPiece
    {
        internal Component Target;
        internal Player Attacker;
        internal float ReadyAt;
        internal int InstanceId;
        internal short ToolTier;
        internal float ExpiresAt;
    }

    internal static class WoodCuttingChainService
    {
        private static readonly List<FelledTreeChain> Trees = new List<FelledTreeChain>();
        private static readonly List<PendingWoodPiece> Pieces = new List<PendingWoodPiece>();
        private static readonly HashSet<int> Queued = new HashSet<int>();

        internal static void RegisterTree(Vector3 origin, Player attacker, short toolTier)
        {
            if (attacker == null) return;
            Trees.Add(new FelledTreeChain { Origin = origin, Attacker = attacker, ToolTier = toolTier, ExpiresAt = Time.time + 4f });
        }

        internal static void ObserveSpawn(Component component)
        {
            if (component == null) return;
            for (int i = Trees.Count - 1; i >= 0; --i)
            {
                FelledTreeChain tree = Trees[i];
                if (tree.ExpiresAt < Time.time) { Trees.RemoveAt(i); continue; }
                if (Vector3.Distance(component.transform.position, tree.Origin) > 10f) continue;
                int id = component.GetInstanceID();
                if (!Queued.Add(id)) return;
                Pieces.Add(new PendingWoodPiece
                {
                    Target = component,
                    InstanceId = id,
                    ToolTier = tree.ToolTier,
                    Attacker = tree.Attacker,
                    ReadyAt = Time.time + 0.35f + Pieces.Count * 0.12f,
                    ExpiresAt = Time.time + 8f
                });
                return;
            }
        }

        internal static void Tick()
        {
            for (int i = Trees.Count - 1; i >= 0; --i)
                if (Trees[i].ExpiresAt < Time.time) Trees.RemoveAt(i);

            for (int i = Pieces.Count - 1; i >= 0; --i)
            {
                PendingWoodPiece pending = Pieces[i];
                if (pending == null || pending.Target == null || pending.Attacker == null)
                { if (pending != null) Queued.Remove(pending.InstanceId); Pieces.RemoveAt(i); continue; }
                if (Time.time < pending.ReadyAt) continue;
                if (Time.time > pending.ExpiresAt)
                { Pieces.RemoveAt(i); Queued.Remove(pending.InstanceId); continue; }
                ZNetView view = pending.Target.GetComponent<ZNetView>();
                if (view == null || !view.IsOwner())
                { pending.ReadyAt = Time.time + 0.20f; continue; }
                Pieces.RemoveAt(i);
                Queued.Remove(pending.InstanceId);
                HitData hit = new HitData();
                hit.m_toolTier = pending.ToolTier;
                hit.m_point = pending.Target.transform.position;
                hit.SetAttacker(pending.Attacker);
                if (pending.Target is TreeLog log)
                {
                    float health = view.GetZDO().GetFloat(ZDOVars.s_health, log.m_health);
                    WoodCutting35Service.MakeLethal(hit, health, log.m_damages);
                    RegisterTree(log.transform.position, pending.Attacker, pending.ToolTier);
                    PerkVisualService.PlayProc(pending.Attacker, "woodcutting_35", log.transform.position, true, false);
                    log.Damage(hit);
                }
                else if (pending.Target is Destructible destructible &&
                         (destructible.m_destructibleType & DestructibleType.Tree) != 0)
                {
                    float health = view.GetZDO().GetFloat(ZDOVars.s_health, destructible.m_health);
                    WoodCutting35Service.MakeLethal(hit, health, destructible.m_damages);
                    PerkVisualService.PlayProc(pending.Attacker, "woodcutting_35", destructible.transform.position, true, false);
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
