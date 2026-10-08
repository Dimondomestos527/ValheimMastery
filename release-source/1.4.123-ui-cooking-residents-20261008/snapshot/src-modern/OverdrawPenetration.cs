using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    internal sealed class OverdrawPenetrationState : MonoBehaviour
    {
        internal Player Owner;
        internal int ShotToken;
        internal int PenetrationCount;
        internal int DecayStep;
        internal float CurrentDamageMultiplier = 1f;
        internal Character LastTarget;
        internal bool LastWeakPointHit;
        internal int LastShockwaveVictims;
        internal readonly HashSet<ZDOID> HitTargets = new HashSet<ZDOID>();
    }

    internal static class Bow70DebugState
    {
        internal static bool Enabled;
        internal static Projectile Projectile;
        internal static int ShotToken;
        internal static int PenetrationCount;
        internal static float CurrentDamageMultiplier = 1f;
        internal static string LastTarget = "none";
        internal static bool WeakPointHit;
        internal static int ShockwaveVictims;

        internal static void RecordRelease(Projectile projectile, int token)
        {
            Projectile = projectile; ShotToken = token; PenetrationCount = 0; CurrentDamageMultiplier = 1f; LastTarget = "none"; WeakPointHit = false; ShockwaveVictims = 0;
        }
        internal static void RecordHit(Projectile projectile, OverdrawPenetrationState state)
        {
            Projectile = projectile; PenetrationCount = state.PenetrationCount; CurrentDamageMultiplier = state.CurrentDamageMultiplier;
            LastTarget = state.LastTarget != null ? state.LastTarget.gameObject.name : "none"; WeakPointHit = state.LastWeakPointHit; ShockwaveVictims = state.LastShockwaveVictims;
        }
        internal static string Summary(Player player)
        {
            OverdrawState draw = player != null ? MasteryStateStore.GetPlayerState<OverdrawState>(player) : null;
            float charge = draw?.FullDrawSince >= 0f ? Mathf.Max(0f, Time.time - draw.FullDrawSince) : 0f;
            return "State=" + (draw?.Phase.ToString() ?? "none") + " FullDrawTime=" + (draw?.FullDrawSince ?? -1f).ToString("0.00") +
                " ChargeTime=" + charge.ToString("0.00") + " Projectile=" + (Projectile != null ? Projectile.gameObject.name : "none") +
                " PenetrationCount=" + PenetrationCount + " CurrentDamageMultiplier=" + CurrentDamageMultiplier.ToString("0.00") +
                " LastTarget=" + LastTarget + " WeakPointHit=" + WeakPointHit + " ShockwaveVictims=" + ShockwaveVictims + " ProjectileAlive=" + (Projectile != null);
        }
    }

    internal static class OverdrawPenetrationService
    {
        private const int MaxPenetrations = 6;
        private const float ShockwaveLength = 4.75f;
        private const float ShockwaveHalfWidth = 1.75f;
        private static readonly HashSet<string> ProcessedShockwaves = new HashSet<string>();

        internal static bool TryHandleHit(Projectile projectile, Collider collider, Vector3 hitPoint)
        {
            OverdrawProjectileTag tag = projectile?.GetComponent<OverdrawProjectileTag>();
            if (projectile == null || tag == null || !tag.IsOverdraw) return false;
            if (projectile.m_nview != null && projectile.m_nview.IsValid() && !projectile.m_nview.IsOwner()) return false;
            Player owner = tag.Owner ?? projectile.m_owner as Player;
            Character target = collider?.GetComponentInParent<Character>();
            if (owner == null || target == null || target == owner || target.IsDead() || !BaseAI.IsEnemy(owner, target)) return false;

            OverdrawPenetrationState state = projectile.GetComponent<OverdrawPenetrationState>() ?? projectile.gameObject.AddComponent<OverdrawPenetrationState>();
            if (state.Owner == null) { state.Owner = owner; state.ShotToken = tag.ShotToken; }
            Vector3 direction = projectile.m_vel.sqrMagnitude > 0.01f ? projectile.m_vel.normalized : projectile.transform.forward;
            ZDOID targetId = target.GetZDOID();
            if (state.HitTargets.Contains(targetId))
            {
                ContinueProjectile(projectile, collider, target, hitPoint, direction);
                return true;
            }

            float appliedMultiplier = DamageMultiplierForIndex(state.DecayStep);
            HitData hit = projectile.m_originalHitData != null ? projectile.m_originalHitData.Clone() : new HitData();
            hit.m_damage = projectile.m_damage; hit.m_damage.Modify(appliedMultiplier); hit.m_point = hitPoint; hit.m_dir = direction; hit.m_ranged = true;
            // Native Projectile.OnHit normally supplies this. Piercing bypasses
            // it, and a None-skill synthesized hit fails the Bow35 consumer.
            hit.m_skill = projectile.m_skill;
            hit.m_skillRaiseAmount = projectile.m_raiseSkillAmount;
            hit.m_pushForce = Mathf.Max(hit.m_pushForce, projectile.m_attackForce); hit.SetAttacker(owner);
            MasteryAttackTagService.Add(hit, MasteryAttackTag.Overdraw); MasteryAttackTagService.SetAttackSerial(hit, state.ShotToken);
            // This hit is synthesized here instead of being passed through the
            // native projectile damage path. Consume Bow35 on this exact payload
            // before dispatch so the first pierced target cannot skip the perk.
            // The Character.Damage hook remains the fallback for ordinary arrows;
            // consuming the mark here clears it before that hook can see the hit.
            bool weakPoint = Bows70WeakPointService.ConsumeIfHit(owner, target, hit);
            target.Damage(hit);

            state.HitTargets.Add(targetId); state.PenetrationCount++; state.CurrentDamageMultiplier = appliedMultiplier;
            // Eyes of Huginn preserves momentum: the weak-point target keeps the current
            // multiplier and does not advance the decay step for the following target.
            if (!weakPoint) state.DecayStep++;
            state.LastTarget = target; state.LastWeakPointHit = weakPoint;
            state.LastShockwaveVictims = -1;
            SendShockwaveRequest(owner, projectile, target, hitPoint + direction * 0.20f, direction, weakPoint, state.ShotToken);
            Bow70DebugState.RecordHit(projectile, state);
            if (MasteryPlugin.Settings.VerboseLogging.Value || Bow70DebugState.Enabled)
                MasteryPlugin.Log.LogInfo("[Bows70] PENETRATION index=" + state.PenetrationCount + " target=" + target.gameObject.name + " multiplier=" + appliedMultiplier.ToString("0.00") + " weakPoint=" + weakPoint + " shockwaveVictims=" + state.LastShockwaveVictims);

            if (state.PenetrationCount >= MaxPenetrations) DestroyProjectile(projectile);
            else ContinueProjectile(projectile, collider, target, hitPoint, direction);
            return true;
        }

        private static float DamageMultiplierForIndex(int index)
        {
            switch (Mathf.Clamp(index, 0, MaxPenetrations - 1)) { case 0: return 1f; case 1: return 0.80f; case 2: return 0.65f; case 3: return 0.50f; case 4: return 0.40f; default: return 0.35f; }
        }

        private static void ContinueProjectile(Projectile projectile, Collider collider, Character target, Vector3 hitPoint, Vector3 direction)
        {
            Bounds bounds = collider != null ? collider.bounds : target.GetCollider().bounds;
            float projectedCenter = Vector3.Dot(bounds.center - hitPoint, direction);
            float projectedExtent = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.y) * bounds.extents.y + Mathf.Abs(direction.z) * bounds.extents.z;
            float distance = Mathf.Max(0.45f, projectedCenter + projectedExtent + 0.35f);
            projectile.transform.position = hitPoint + direction * distance;
            projectile.m_didHit = false; projectile.m_hitList?.Clear(); projectile.m_hitHistory?.Clear();
        }

        private static void DestroyProjectile(Projectile projectile)
        {
            if (projectile == null) return;
            if (ZNetScene.instance != null) ZNetScene.instance.Destroy(projectile.gameObject); else UnityEngine.Object.Destroy(projectile.gameObject);
        }

        private static int ApplyShockwave(Player owner, Character primary, Vector3 origin, Vector3 direction, float stagger, float pushForce)
        {
            int victims = 0;
            foreach (Character candidate in Character.GetAllCharacters())
            {
                if (candidate == null || candidate == owner || candidate == primary || candidate.IsDead() || candidate.IsPlayer() || !BaseAI.IsEnemy(owner, candidate)) continue;
                Vector3 offset = candidate.GetCenterPoint() - origin; float depth = Vector3.Dot(offset, direction);
                if (depth <= 0.10f || depth > ShockwaveLength) continue;
                Vector3 lateral = offset - direction * depth; float allowedWidth = Mathf.Lerp(0.70f, ShockwaveHalfWidth, depth / ShockwaveLength) + candidate.GetRadius() * 0.25f;
                if (lateral.sqrMagnitude > allowedWidth * allowedWidth) continue;
                HitData push = new HitData(); push.m_skill = Skills.SkillType.Bows; push.m_point = candidate.GetCenterPoint(); push.m_dir = direction; push.m_pushForce = pushForce; push.SetAttacker(owner);
                PerkHitContext context = PerkRuntimeService.GetHitContext(push); context.IsPerkGenerated = true; context.PerkId = "bows_70_shockwave"; context.AllowSelfProc = false; context.AllowOtherPerkProc = false; context.IgnoreOverdrawPayload = true;
                candidate.AddStaggerDamage(stagger, direction, push); candidate.Stagger(direction); candidate.Damage(push); victims++;
                if (victims >= 8) break;
            }
            return victims;
        }

        private static void SendShockwaveRequest(Player owner, Projectile projectile, Character primary, Vector3 origin, Vector3 direction, bool weakPoint, int token)
        {
            ZDOID id = primary.GetZDOID(); float stagger = Mathf.Max(12f, projectile.m_damage.GetTotalDamage() * 0.45f); float push = Mathf.Max(18f, projectile.m_attackForce * 0.45f);
            string payload = "bow70_hit:" + id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture) + ":" +
                direction.x.ToString("0.###", CultureInfo.InvariantCulture) + ":" + direction.y.ToString("0.###", CultureInfo.InvariantCulture) + ":" + direction.z.ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                origin.x.ToString("0.###", CultureInfo.InvariantCulture) + ":" + origin.y.ToString("0.###", CultureInfo.InvariantCulture) + ":" + origin.z.ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                stagger.ToString("0.###", CultureInfo.InvariantCulture) + ":" + push.ToString("0.###", CultureInfo.InvariantCulture) + ":" + token.ToString(CultureInfo.InvariantCulture) + ":" + (weakPoint ? "1" : "0");
            if (ZNet.instance != null && ZNet.instance.IsServer()) HandleAbility(owner, payload); else NetworkSync.SendClientAbility(payload);
        }

        internal static void HandleAbility(Player player, string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            if (!payload.StartsWith("bow70_hit:", StringComparison.Ordinal)) { HandleVisualRelay(player, payload); return; }
            if (ZNet.instance == null || !ZNet.instance.IsServer() || player == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Bows, 70)) return;
            string[] p = payload.Split(':');
            if (p.Length != 13 || !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) || !uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) ||
                !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float dx) || !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float dy) || !float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float dz) ||
                !float.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float ox) || !float.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float oy) || !float.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float oz) ||
                !float.TryParse(p[9], NumberStyles.Float, CultureInfo.InvariantCulture, out float stagger) || !float.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float push) ||
                !int.TryParse(p[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out int token) || (p[12] != "0" && p[12] != "1")) return;
            Character primary = ZNetScene.instance?.FindInstance(new ZDOID(user, id))?.GetComponent<Character>(); Vector3 origin = new Vector3(ox, oy, oz); Vector3 direction = new Vector3(dx, dy, dz).normalized;
            if (primary == null || primary == player || !BaseAI.IsEnemy(player, primary) || direction.sqrMagnitude < 0.5f || (origin - player.GetCenterPoint()).sqrMagnitude > 10000f) return;
            string key = player.GetPlayerID() + ":" + token + ":" + primary.GetZDOID(); if (ProcessedShockwaves.Count >= 4096) ProcessedShockwaves.Clear(); if (!ProcessedShockwaves.Add(key)) return;
            // Client values are telemetry only. Gameplay force is selected by the server so a
            // modified client cannot inflate shockwave stagger or knockback.
            const float authoritativeStagger = 35f, authoritativePush = 28f;
            int victims = ApplyShockwave(player, primary, origin, direction, authoritativeStagger, authoritativePush);
            if (player == Player.m_localPlayer) Bow70DebugState.ShockwaveVictims = victims;
            SendVisual(player, p[12] == "1" ? "bow70_weakfx" : "bow70_fx", origin, direction, victims);
        }

        internal static void SendReleaseVisual(Player owner, Vector3 origin, Vector3 direction)
        {
            SendVisual(owner, "bow70_releasefx", origin, direction, -1);
        }

        private static void SendVisual(Player owner, string kind, Vector3 origin, Vector3 direction, int victims)
        {
            string payload = kind + ":" + direction.x.ToString("0.###", CultureInfo.InvariantCulture) + ":" + direction.y.ToString("0.###", CultureInfo.InvariantCulture) + ":" + direction.z.ToString("0.###", CultureInfo.InvariantCulture) + ":" + origin.x.ToString("0.###", CultureInfo.InvariantCulture) + ":" + origin.y.ToString("0.###", CultureInfo.InvariantCulture) + ":" + origin.z.ToString("0.###", CultureInfo.InvariantCulture);
            if (victims >= 0) payload += ":" + victims.ToString(CultureInfo.InvariantCulture);
            if (ZNet.instance != null && ZNet.instance.IsServer()) HandleVisualRelay(owner, payload); else NetworkSync.SendClientAbility(payload);
        }

        internal static void HandleVisualRelay(Player player, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || player == null || !TryParseVisual(payload, out Vector3 direction, out Vector3 origin, out int victims)) return;
            if ((origin - player.GetCenterPoint()).sqrMagnitude > 10000f) return;
            if (victims >= 0 && player == Player.m_localPlayer) Bow70DebugState.ShockwaveVictims = victims;
            if (Player.m_localPlayer != null) PlayVisual(payload, origin, direction);
            NetworkSync.BroadcastProcFeedback(payload, origin);
        }

        internal static bool TryHandleVisual(string payload, Vector3 fallback)
        {
            if (!TryParseVisual(payload, out Vector3 direction, out Vector3 origin, out int victims)) return false;
            if (victims >= 0) Bow70DebugState.ShockwaveVictims = victims;
            PlayVisual(payload, origin == Vector3.zero ? fallback : origin, direction); return true;
        }
        private static void PlayVisual(string payload, Vector3 origin, Vector3 direction)
        {
            if (payload.StartsWith("bow70_releasefx:", StringComparison.Ordinal))
            {
                MasteryVfxMaterial.SpawnPrefab("vfx_arrowhit", origin, 0.22f);
                return;
            }
            bool weak = payload.StartsWith("bow70_weakfx:", StringComparison.Ordinal);
            Bow70ShockwaveVisual.Spawn(origin, direction, weak);
            MasteryVfxMaterial.SpawnPrefab(weak ? "vfx_HitSparks" : "vfx_arrowhit", origin, weak ? 0.52f : 0.20f);
            if (weak) PerkAudioService.Play("bows70_weak_penetration", "sfx_perfectblock", origin, 0.46f);
        }
        private static bool TryParseVisual(string payload, out Vector3 direction, out Vector3 origin, out int victims)
        {
            direction = Vector3.forward; origin = Vector3.zero; victims = -1;
            if (string.IsNullOrEmpty(payload) || !(payload.StartsWith("bow70_fx:", StringComparison.Ordinal) || payload.StartsWith("bow70_weakfx:", StringComparison.Ordinal) || payload.StartsWith("bow70_releasefx:", StringComparison.Ordinal))) return false;
            string[] p = payload.Split(':'); float x, y, z, ox, oy, oz;
            if ((p.Length != 7 && p.Length != 8) || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z) || !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out ox) || !float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out oy) || !float.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out oz)) return false;
            if (p.Length == 8) int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out victims);
            direction = new Vector3(x, y, z).normalized; origin = new Vector3(ox, oy, oz); return direction.sqrMagnitude > 0.5f;
        }
    }

    internal sealed class Bow70ShockwaveVisual : MonoBehaviour
    {
        private LineRenderer[] Lines; private Material[] Materials; private Vector3 Direction; private Vector3 Right; private Vector3 Up; private Vector3 Origin; private float Born; private bool WeakPoint;
        internal static void Spawn(Vector3 origin, Vector3 direction, bool weakPoint = false)
        {
            PerkAudioService.Play("bows70_shockwave", "sfx_arrow_hit", origin, 0.34f);
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            GameObject root = new GameObject("ValheimMastery_Bow70Shockwave"); root.transform.position = origin;
            Bow70ShockwaveVisual visual = root.AddComponent<Bow70ShockwaveVisual>(); visual.Initialize(origin, direction, weakPoint);
        }
        private void Initialize(Vector3 origin, Vector3 direction, bool weakPoint)
        {
            Origin = origin; Direction = direction.normalized; Right = Vector3.Cross(Direction, Vector3.up); if (Right.sqrMagnitude < 0.01f) Right = Vector3.Cross(Direction, Vector3.right); Right.Normalize(); Up = Vector3.Cross(Right, Direction).normalized; Born = Time.time; WeakPoint = weakPoint;
            Lines = new LineRenderer[3]; Materials = new Material[3];
            for (int i = 0; i < Lines.Length; ++i)
            {
                GameObject child = new GameObject("PressureArc" + i); child.transform.SetParent(transform, false); Lines[i] = child.AddComponent<LineRenderer>(); Materials[i] = MasteryVfxMaterial.CloneFromPrefab("vfx_arrowhit");
                Lines[i].material = Materials[i]; Lines[i].useWorldSpace = true; Lines[i].positionCount = 17; Lines[i].numCapVertices = 2; Lines[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (weakPoint) { Lines[i].startColor = Lines[i].endColor = new Color(1f, 0.62f, 0.18f, 0.92f); }
            }
        }
        private void Update()
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) { Destroy(gameObject); return; }
            float t = Mathf.Clamp01((Time.time - Born) / 0.42f);
            for (int ring = 0; ring < Lines.Length; ++ring)
            {
                float depth = Mathf.Lerp(0.30f + ring * 0.35f, 4.75f, t); float radius = Mathf.Lerp(0.25f, 1.75f, depth / 4.75f);
                for (int i = 0; i < 17; ++i) { float angle = Mathf.Lerp(-1.18f, 1.18f, i / 16f); Lines[ring].SetPosition(i, Origin + Direction * depth + Right * (Mathf.Sin(angle) * radius) + Up * (Mathf.Cos(angle) * radius * 0.32f)); }
                float alpha = (1f - t) * (0.72f - ring * 0.12f); Color color = WeakPoint ? new Color(1f, 0.62f, 0.18f, alpha) : new Color(0.78f, 0.84f, 0.86f, alpha); Lines[ring].startColor = Lines[ring].endColor = color; Lines[ring].widthMultiplier = Mathf.Lerp(0.065f, 0.012f, t);
            }
            if (t >= 1f) Destroy(gameObject);
        }
        private void OnDestroy() { if (Materials != null) foreach (Material material in Materials) if (material != null) Destroy(material); }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    [HarmonyPriority(Priority.First)]
    internal static class OverdrawPenetrationHitPatch
    {
        private static bool Prefix(Projectile __instance, Collider collider, Vector3 hitPoint)
        {
            return !OverdrawPenetrationService.TryHandleHit(__instance, collider, hitPoint);
        }
    }

    [HarmonyPatch(typeof(Projectile), "Update")]
    internal static class OverdrawRemoteTrailPatch
    {
        private static void Postfix(Projectile __instance)
        {
            if (__instance == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value ||
                Player.m_localPlayer == null || __instance.GetComponent<OverdrawArrowTrail>() != null) return;
            ZDO zdo = __instance.m_nview?.GetZDO();
            if (zdo?.GetBool(OverdrawProjectileTag.ActiveZdoKey, false) == true) __instance.gameObject.AddComponent<OverdrawArrowTrail>();
        }
    }

    internal static class Bow70DebugOverlay
    {
        private static GameObject Root; private static TextMeshProUGUI Text;
        internal static void Refresh(Player player)
        {
            if (!Bow70DebugState.Enabled) { if (Root != null) Root.SetActive(false); return; }
            Ensure(); if (Root == null || Text == null) return; Root.SetActive(true); Text.text = "<b>BOW70 OVERDRAW</b>\n" + Bow70DebugState.Summary(player).Replace(" ", "\n");
        }
        private static void Ensure()
        {
            if (Root != null || Player.m_localPlayer == null) return;
            Root = new GameObject("ValheimMastery_Bow70DebugOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); Canvas canvas = Root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 251;
            CanvasScaler scaler = Root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f);
            GameObject label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(Root.transform, false); RectTransform rect = label.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(1f, 1f); rect.anchoredPosition = new Vector2(-22f, -290f); rect.sizeDelta = new Vector2(560f, 330f);
            Text = label.GetComponent<TextMeshProUGUI>(); Text.fontSize = 17f; Text.alignment = TextAlignmentOptions.TopRight; Text.color = new Color(0.92f, 0.78f, 0.45f, 0.96f); Text.raycastTarget = false; TextMeshProUGUI vanilla = MessageHud.instance?.GetComponentInChildren<TextMeshProUGUI>(true); if (vanilla != null) Text.font = vanilla.font;
        }
    }
}
