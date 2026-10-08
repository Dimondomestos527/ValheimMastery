#if false // Replaced by simplified penetration/shockwave design in OverdrawPenetration.cs.
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    /// <summary>One-shot payloads for a manually released Overdraw arrow. Generated hits are explicitly marked non-recursive.</summary>
    internal static class OverdrawPayloadService
    {
        private static readonly HashSet<string> ConsumedCharacterHits = new HashSet<string>();

        internal static void TryApplyFromHit(Character primary, HitData hit)
        {
            if (primary == null || hit == null || PerkRuntimeService.IsPerkGenerated(hit) || !MasteryAttackTagService.Has(hit, MasteryAttackTag.Overdraw)) return;
            Player player = hit.GetAttacker() as Player;
            if (player == null || primary.IsDead() || !BaseAI.IsEnemy(player, primary)) return;
            int token = MasteryAttackTagService.GetAttackSerial(hit);
            int encodedArrow = token & 0xF;
            ArrowClass arrow = encodedArrow >= 1 && encodedArrow <= (int)ArrowClass.Other + 1 ? (ArrowClass)(encodedArrow - 1) : ArrowClass.Other;
            string key = player.GetPlayerID() + ":" + primary.GetZDOID() + ":" + token;
            if (!ConsumedCharacterHits.Add(key)) return;
            if (ConsumedCharacterHits.Count > 2048) ConsumedCharacterHits.Clear();
            ApplyCharacterPayload(player, primary, hit.m_point, Mathf.Max(1f, hit.m_damage.GetTotalDamage()), arrow);
        }

        internal static void TryApply(Projectile projectile, Collider collider, Vector3 point)
        {
            if (projectile == null) return;
            OverdrawProjectileTag tag = projectile.GetComponent<OverdrawProjectileTag>();
            bool hasMarker = MasteryStateStore.TryGetObjectState<ProjectileOverdrawMarker>(projectile, out ProjectileOverdrawMarker marker) && marker.IsOverdraw && !marker.PayloadConsumed;
            ZDO zdo = projectile.m_nview?.GetZDO();
            bool networkMarked = zdo?.GetBool(OverdrawProjectileTag.ActiveZdoKey, false) == true && !zdo.GetBool(OverdrawProjectileTag.ConsumedZdoKey, false);
            if ((tag == null || !tag.IsOverdraw || tag.PayloadConsumed) && !hasMarker && !networkMarked) return;
            long ownerId = zdo?.GetLong(OverdrawProjectileTag.OwnerZdoKey, 0L) ?? 0L;
            Player player = tag?.Owner ?? projectile.m_owner as Player ?? FindPlayer(ownerId);
            if (player == null) return;
            ArrowClass arrow = tag != null && tag.IsOverdraw ? tag.Arrow : hasMarker ? marker.Arrow : (ArrowClass)zdo.GetInt(OverdrawProjectileTag.ArrowZdoKey, (int)ArrowClass.Other);
            int token = tag != null && tag.ShotToken != 0 ? tag.ShotToken : hasMarker && marker.ShotToken != 0 ? marker.ShotToken : zdo?.GetInt(OverdrawProjectileTag.TokenZdoKey, 0) ?? 0;
            string payloadProfile = GetPayloadProfile(arrow);
            Character character = ResolveHitCharacter(player, collider, point);
            if (character != null)
            {
                string key = player.GetPlayerID() + ":" + character.GetZDOID() + ":" + token;
                if (ConsumedCharacterHits.Add(key))
                {
                    if (ConsumedCharacterHits.Count > 2048) ConsumedCharacterHits.Clear();
                    ApplyCharacterPayload(player, character, point, Mathf.Max(1f, projectile.m_damage.GetTotalDamage()), arrow);
                }
                MarkConsumed(marker, tag, zdo);
                return;
            }
            if (arrow == ArrowClass.Iron && TryIronToolPayload(player, collider, point))
            {
                MarkConsumed(marker, tag, zdo);
                PerkFeedbackService.Play(player, payloadProfile, point, true);
                return;
            }
            MarkConsumed(marker, tag, zdo);
            return;
        }

        private static void MarkConsumed(ProjectileOverdrawMarker marker, OverdrawProjectileTag tag, ZDO zdo)
        {
            if (marker != null) marker.PayloadConsumed = true;
            if (tag != null) tag.PayloadConsumed = true;
            if (zdo != null) zdo.Set(OverdrawProjectileTag.ConsumedZdoKey, true);
        }

        private static Character ResolveHitCharacter(Player player, Collider collider, Vector3 point)
        {
            Character direct = collider?.GetComponentInParent<Character>();
            if (direct != null && direct != player && !direct.IsDead() && BaseAI.IsEnemy(player, direct)) return direct;
            Character best = null; float bestDistance = 2.25f * 2.25f;
            foreach (Character candidate in Character.GetAllCharacters())
            {
                if (candidate == null || candidate == player || candidate.IsDead() || !BaseAI.IsEnemy(player, candidate)) continue;
                float distance = (candidate.GetCenterPoint() - point).sqrMagnitude;
                foreach (Collider candidateCollider in candidate.GetComponentsInChildren<Collider>(true))
                {
                    Vector3 closest = candidateCollider.ClosestPoint(point);
                    distance = Mathf.Min(distance, (closest - point).sqrMagnitude);
                }
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            return best;
        }

        private static void ApplyCharacterPayload(Player player, Character primary, Vector3 point, float baseDamage, ArrowClass arrow)
        {
            string payloadProfile = GetPayloadProfile(arrow);
            switch (arrow)
            {
                case ArrowClass.Wood: Splash(player, primary, point, 4.5f, 4, baseDamage * 0.15f, Skills.SkillType.Bows, "bows_70_payload"); break;
                case ArrowClass.Obsidian: Splash(player, primary, point, 5f, 4, baseDamage * 0.25f, Skills.SkillType.Bows, "bows_70_payload"); break;
                case ArrowClass.Bronze: Ricochet(player, primary, baseDamage * 0.50f); break;
                case ArrowClass.Fire: ElementalSplash(player, primary, point, 3f, baseDamage * 0.45f, DamageKind.Fire); break;
                case ArrowClass.Poison: ElementalSplash(player, primary, point, 3.5f, baseDamage * 0.40f, DamageKind.Poison); break;
                case ArrowClass.Frost: ElementalSplash(player, primary, point, 3f, baseDamage * 0.45f, DamageKind.Frost); break;
                case ArrowClass.Silver: Deal(player, primary, baseDamage * (IsUndead(primary) ? 0.75f : 0.20f), DamageKind.Spirit, "bows_70_payload"); break;
                case ArrowClass.Iron: primary.AddStaggerDamage(baseDamage * 2f, (primary.GetCenterPoint() - player.GetCenterPoint()).normalized, GeneratedHit(player, primary, baseDamage * 0.01f, DamageKind.Blunt, "bows_70_payload")); break;
                case ArrowClass.Carapace: TargetEffectService.Apply(primary, "shell_break", player, 1, 0.25f, 10f); break;
                case ArrowClass.Charred: Splash(player, primary, point, 3.5f, 4, baseDamage * 0.35f, Skills.SkillType.Bows, "bows_70_payload"); break;
                case ArrowClass.Flint: Deal(player, primary, baseDamage * 0.35f, DamageKind.Slash, "bows_70_payload"); break;
                case ArrowClass.Needle: Deal(player, primary, baseDamage * 0.50f, DamageKind.Pierce, "bows_70_payload"); primary.AddStaggerDamage(baseDamage * 0.45f, (primary.GetCenterPoint() - player.GetCenterPoint()).normalized, GeneratedHit(player, primary, 0.01f, DamageKind.Pierce, "bows_70_payload")); break;
                default: Deal(player, primary, baseDamage * 0.20f, DamageKind.Pierce, "bows_70_payload"); break;
            }
            PerkFeedbackService.Play(player, payloadProfile, point, true);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Bows70] PAYLOAD_APPLIED arrow=" + arrow + " target=" + primary.gameObject.name + " base=" + baseDamage.ToString("0.0"));
        }

        private static string GetPayloadProfile(ArrowClass arrow)
        {
            switch (arrow)
            {
                case ArrowClass.Poison: return "bows_70_payload_poison";
                case ArrowClass.Obsidian: return "bows_70_payload_obsidian";
                case ArrowClass.Iron: return "bows_70_payload_iron";
                default: return "bows_70_payload";
            }
        }

        private static Player FindPlayer(long playerId)
        {
            if (playerId == 0L) return null;
            foreach (Player candidate in Player.GetAllPlayers())
                if (candidate != null && candidate.GetPlayerID() == playerId) return candidate;
            return null;
        }

        private static bool TryIronToolPayload(Player player, Collider collider, Vector3 point)
        {
            if (player == null || collider == null || ObjectDB.instance == null) return false;
            ItemDrop ironDrop = ObjectDB.instance.GetItemPrefab("PickaxeIron")?.GetComponent<ItemDrop>();
            ItemDrop.ItemData iron = ironDrop?.m_itemData;
            ItemDrop axeDrop = ObjectDB.instance.GetItemPrefab("AxeIron")?.GetComponent<ItemDrop>();
            ItemDrop.ItemData axe = axeDrop?.m_itemData;
            if (iron?.m_shared == null && axe?.m_shared == null) return false;
            const float IronArrowToolMultiplier = 1.75f;
            float pickaxeDamage = (iron?.m_shared?.m_damages.m_pickaxe ?? 0f) * IronArrowToolMultiplier;
            float chopDamage = (axe?.m_shared?.m_damages.m_chop ?? 0f) * IronArrowToolMultiplier;
            short toolTier = (short)Mathf.Max(iron?.m_shared?.m_toolTier ?? 0, axe?.m_shared?.m_toolTier ?? 0);
            const float BlastRadius = 2f;
            HashSet<IDestructible> targets = new HashSet<IDestructible>();
            Collider[] overlaps = Physics.OverlapSphere(point, BlastRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            foreach (Collider overlap in overlaps)
            {
                IDestructible destructible = FindNaturalDestructible(overlap);
                if (destructible != null) targets.Add(destructible);
            }
            IDestructible direct = FindNaturalDestructible(collider);
            if (direct != null) targets.Add(direct);
            if (targets.Count == 0) return false;
            foreach (IDestructible target in targets)
            {
                Component component = target as Component;
                Vector3 targetPoint = component != null ? component.transform.position : point;
                HitData hit = BuildIronToolHit(player, point, targetPoint, toolTier, pickaxeDamage, chopDamage);
                target.Damage(hit);
            }
            GameObject blast = new GameObject("ValheimMastery_IronArrowForceBlast");
            blast.transform.position = point;
            blast.AddComponent<IronArrowForceBlast>().Initialize(BlastRadius);
            MasteryVfxMaterial.SpawnPrefab("vfx_RockDestroyed", point, 1.35f);
            MasteryVfxMaterial.SpawnPrefab("vfx_RockHit", point, 1.10f);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Bows70] IRON_TOOL_BLAST center=" + collider.gameObject.name + " radius=" + BlastRadius.ToString("0.0") + " targets=" + targets.Count + " pickaxe=" + pickaxeDamage.ToString("0.0") + " chop=" + chopDamage.ToString("0.0") + " tier=" + toolTier);
            return true;
        }

        private static IDestructible FindNaturalDestructible(Collider collider)
        {
            if (collider == null) return null;
            foreach (Component component in collider.GetComponentsInParent<Component>(true))
            {
                if (!(component is IDestructible destructible) || destructible is Character) continue;
                if (component.GetComponentInParent<Piece>() != null) continue;
                return destructible;
            }
            return null;
        }

        private static HitData BuildIronToolHit(Player player, Vector3 origin, Vector3 target, short toolTier, float pickaxeDamage, float chopDamage)
        {
            HitData hit = new HitData();
            hit.m_skill = Skills.SkillType.Pickaxes; hit.m_point = target;
            Vector3 direction = target - origin; hit.m_dir = direction.sqrMagnitude > 0.001f ? direction.normalized : player.transform.forward;
            hit.m_toolTier = toolTier; hit.m_damage.m_pickaxe = pickaxeDamage; hit.m_damage.m_chop = chopDamage;
            hit.SetAttacker(player);
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            context.IsPerkGenerated = true; context.PerkId = "bows_70_iron_pickaxe"; context.AllowSelfProc = false; context.AllowOtherPerkProc = false;
            return hit;
        }
        private enum DamageKind { Pierce, Slash, Blunt, Fire, Poison, Frost, Spirit }
        private static void Ricochet(Player player, Character primary, float damage)
        {
            Character next = null; float best = 25f;
            foreach (Character candidate in Character.GetAllCharacters())
            {
                if (candidate == null || candidate == primary || candidate == player || candidate.IsDead() || !BaseAI.IsEnemy(player, candidate)) continue;
                float dist = (candidate.GetCenterPoint() - primary.GetCenterPoint()).sqrMagnitude;
                if (dist < best) { best = dist; next = candidate; }
            }
            if (next != null) Deal(player, next, damage, DamageKind.Pierce, "bows_70_payload");
        }
        private static void Splash(Player player, Character primary, Vector3 point, float radius, int max, float damage, Skills.SkillType skill, string id)
        {
            List<Character> targets = Nearby(player, null, point, radius);
            for (int i = 0; i < Mathf.Min(max, targets.Count); ++i) Deal(player, targets[i], damage, DamageKind.Pierce, id);
        }
        private static void ElementalSplash(Player player, Character primary, Vector3 point, float radius, float damage, DamageKind kind)
        {
            foreach (Character target in Nearby(player, null, point, radius)) Deal(player, target, damage, kind, "bows_70_payload");
        }
        private static List<Character> Nearby(Player player, Character skip, Vector3 point, float radius)
        {
            List<Character> result = new List<Character>();
            foreach (Character candidate in Character.GetAllCharacters())
                if (candidate != null && candidate != skip && candidate != player && !candidate.IsDead() && !candidate.IsPlayer() && BaseAI.IsEnemy(player, candidate) && (candidate.GetCenterPoint() - point).sqrMagnitude <= radius * radius) result.Add(candidate);
            result.Sort((a,b) => (a.GetCenterPoint() - point).sqrMagnitude.CompareTo((b.GetCenterPoint() - point).sqrMagnitude));
            return result;
        }
        private static bool IsUndead(Character target)
        {
            string name = target?.gameObject?.name ?? "";
            return name.IndexOf("skeleton", System.StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("draugr", System.StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("wraith", System.StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("ghost", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static void Deal(Player player, Character target, float damage, DamageKind kind, string id)
        {
            if (target == null || target.IsDead() || damage <= 0f) return;
            target.Damage(GeneratedHit(player, target, damage, kind, id));
        }
        private static HitData GeneratedHit(Player player, Character target, float damage, DamageKind kind, string id)
        {
            HitData hit = new HitData();
            hit.m_skill = Skills.SkillType.Bows;
            hit.m_point = target.GetCenterPoint();
            hit.m_dir = (target.GetCenterPoint() - player.GetCenterPoint()).normalized;
            hit.SetAttacker(player);
            switch (kind)
            {
                case DamageKind.Slash: hit.m_damage.m_slash = damage; break;
                case DamageKind.Blunt: hit.m_damage.m_blunt = damage; break;
                case DamageKind.Fire: hit.m_damage.m_fire = damage; break;
                case DamageKind.Poison: hit.m_damage.m_poison = damage; break;
                case DamageKind.Frost: hit.m_damage.m_frost = damage; break;
                case DamageKind.Spirit: hit.m_damage.m_spirit = damage; break;
                default: hit.m_damage.m_pierce = damage; break;
            }
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            context.IsPerkGenerated = true; context.PerkId = id; context.AllowSelfProc = false; context.AllowOtherPerkProc = false;
            return hit;
        }
    }

    internal sealed class IronArrowForceBlast : MonoBehaviour
    {
        private const float Lifetime = 0.45f;
        private float Born;
        private float Radius;
        private LineRenderer Ring;
        private Material Material;

        internal void Initialize(float radius)
        {
            Radius = radius; Born = Time.time;
            Ring = gameObject.AddComponent<LineRenderer>();
            Material = MasteryVfxMaterial.CloneFromPrefab("vfx_RockHit"); Ring.material = Material;
            Ring.useWorldSpace = true; Ring.loop = true; Ring.positionCount = 48; Ring.numCapVertices = 2; Ring.widthMultiplier = 0.10f;
            Ring.startColor = new Color(0.72f, 0.88f, 1f, 0.95f); Ring.endColor = new Color(1f, 1f, 1f, 0.75f);
        }

        private void Update()
        {
            float progress = Mathf.Clamp01((Time.time - Born) / Lifetime);
            float radius = Mathf.Lerp(0.18f, Radius, 1f - (1f - progress) * (1f - progress));
            Vector3 center = transform.position + Vector3.up * 0.06f;
            for (int i = 0; i < Ring.positionCount; ++i)
            {
                float angle = i * Mathf.PI * 2f / Ring.positionCount;
                Ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            Ring.widthMultiplier = Mathf.Lerp(0.13f, 0.015f, progress);
            if (progress >= 1f) Destroy(gameObject);
        }

        private void OnDestroy() { if (Material != null) Destroy(Material); }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class OverdrawPayloadCharacterHitPatch
    {
        private static void Prefix(Character __instance, HitData hit) => OverdrawPayloadService.TryApplyFromHit(__instance, hit);
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    [HarmonyPriority(Priority.Last)]
    internal static class OverdrawPayloadHitPatch
    {
        private static void Prefix(Projectile __instance, Collider collider, Vector3 hitPoint) => OverdrawPayloadService.TryApply(__instance, collider, hitPoint);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddStaggerDamage))]
    [HarmonyPriority(Priority.First)]
    internal static class OverdrawCarapaceStaggerPatch
    {
        private static void Prefix(Character __instance, ref float damage)
        {
            if (damage > 0f && TargetEffectService.TryGet(__instance, "shell_break", out TargetEffect effect)) damage *= 1f + effect.Strength;
        }
    }
}
#endif
