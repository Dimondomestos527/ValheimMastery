using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class AxeGeometryAuditService
    {
        private static bool Armed;
        private static bool Show;
        private static Attack ActiveAttack;
        private static Vector3 ActiveCenter;
        private static float ActiveRadius;
        private static Vector3 ActiveForward;
        private static readonly List<string> Hits = new List<string>();

        internal static void Arm(bool show)
        {
            Armed = true;
            Show = show;
        }

        internal static string ArmMessage(bool show)
        {
            Arm(show);
            return "Axe geometry armed for next attack. show=" + show;
        }

        internal static void Begin(Attack attack, bool area, float vanillaRange, float vanillaWidth, float vanillaExtra)
        {
            if (!Armed || attack == null) return;
            Armed = false;
            ActiveAttack = attack;
            Hits.Clear();
            Player player = attack.m_character as Player;
            Transform origin = attack.GetAttackOrigin();
            Vector3 forward = player != null ? player.transform.forward : origin.forward;
            Vector3 right = player != null ? player.transform.right : origin.right;
            ActiveCenter = origin.position + origin.up * attack.m_attackHeight + forward * attack.m_attackRange + right * attack.m_attackOffset;
            ActiveRadius = Mathf.Max(0.05f, attack.m_attackRayWidth + attack.m_attackRayWidthCharExtra);
            ActiveForward = forward;
            string weapon = attack.m_weapon?.m_dropPrefab != null ? attack.m_weapon.m_dropPrefab.name : attack.m_weapon?.m_shared?.m_name ?? "unknown";
            MasteryPlugin.Log.LogInfo("[AxeGeometry] weapon=" + weapon + " secondary=" + AttackIntentService.IsSecondary(attack, player) +
                " mode=" + (area ? "AREA" : "MELEE") + " attackType=" + attack.m_attackType +
                " vanillaRange=" + vanillaRange.ToString("0.###") + " vanillaWidth=" + vanillaWidth.ToString("0.###") +
                " vanillaExtra=" + vanillaExtra.ToString("0.###") + " finalRange=" + attack.m_attackRange.ToString("0.###") +
                " finalWidth=" + attack.m_attackRayWidth.ToString("0.###") + " finalExtra=" + attack.m_attackRayWidthCharExtra.ToString("0.###") +
                " height=" + attack.m_attackHeight.ToString("0.###") + " offset=" + attack.m_attackOffset.ToString("0.###") +
                " center=" + ActiveCenter);
        }

        internal static void ObserveHit(Character target)
        {
            if (ActiveAttack == null || target == null) return;
            string id = target.gameObject.name;
            if (!Hits.Contains(id)) Hits.Add(id);
        }

        internal static void End(Attack attack)
        {
            if (ActiveAttack != attack) return;
            MasteryPlugin.Log.LogInfo("[AxeGeometry] actualHits=" + (Hits.Count == 0 ? "none" : string.Join(",", Hits)));
            if (Show) AxeGeometryDebugVisual.Create(ActiveCenter, ActiveForward, ActiveRadius);
            ActiveAttack = null;
            Show = false;
            Hits.Clear();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.Last)]
    internal static class AxeGeometryHitObserverPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (hit?.m_skill == Skills.SkillType.Axes) AxeGeometryAuditService.ObserveHit(__instance);
        }
    }

    internal sealed class AxeGeometryDebugVisual : MonoBehaviour
    {
        private readonly List<LineRenderer> Lines = new List<LineRenderer>();

        internal static void Create(Vector3 center, Vector3 forward, float radius)
        {
            GameObject root = new GameObject("ValheimMastery_AxeGeometryDebug");
            AxeGeometryDebugVisual visual = root.AddComponent<AxeGeometryDebugVisual>();
            visual.Build(center, forward, radius);
            Object.Destroy(root, 1.05f);
        }

        private void Build(Vector3 center, Vector3 forward, float radius)
        {
            CreateCircle(center, radius, new Color(1f, 0.25f, 0.05f, 0.95f));
            CreateLine(center, center + forward.normalized * Mathf.Max(0.5f, radius), Color.yellow);
            CreateLine(center - Vector3.up * 0.35f, center + Vector3.up * 0.35f, Color.white);
        }

        private void CreateCircle(Vector3 center, float radius, Color color)
        {
            const int segments = 48;
            LineRenderer line = NewLine(color, segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius));
            }
        }

        private void CreateLine(Vector3 from, Vector3 to, Color color)
        {
            LineRenderer line = NewLine(color, 2);
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }

        private LineRenderer NewLine(Color color, int count)
        {
            GameObject child = new GameObject("line");
            child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            MasteryVfxMaterial.AssignOwned(line, "vfx_HitSparks");
            line.startColor = color;
            line.endColor = color;
            line.startWidth = 0.035f;
            line.endWidth = 0.035f;
            line.positionCount = count;
            line.useWorldSpace = true;
            Lines.Add(line);
            return line;
        }
    }
}
