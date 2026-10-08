using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasteryVfxMaterial
    {
        internal static Material CloneFromPrefab(string prefabName)
        {
            GameObject prefab = ZNetScene.instance?.GetPrefab(prefabName);
            if (prefab == null) return null;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial != null) return new Material(renderer.sharedMaterial);
            return null;
        }

        internal static void AssignOwned(Renderer renderer, string prefabName)
        {
            if (renderer == null) return;
            Material material = CloneFromPrefab(prefabName);
            renderer.sharedMaterial = material;
            if (material == null) return;
            MasteryOwnedVfxMaterials owner = renderer.GetComponent<MasteryOwnedVfxMaterials>() ??
                renderer.gameObject.AddComponent<MasteryOwnedVfxMaterials>();
            owner.Track(material);
        }

        internal static void SpawnPrefab(string prefabName, Vector3 position, float scale)
        {
            PerkNativeFeedback.PlayVfx(prefabName, position, scale);
        }
    }

    // Only tracks the explicit clones created above, never a vanilla shared asset.
    // Renderer destruction does not dispose runtime Material objects.
    internal sealed class MasteryOwnedVfxMaterials : MonoBehaviour
    {
        private readonly List<Material> Materials = new List<Material>();

        internal void Track(Material material) { if (material != null) Materials.Add(material); }

        private void OnDestroy()
        {
            foreach (Material material in Materials)
                if (material != null) Destroy(material);
            Materials.Clear();
        }
    }

    internal static class ShadowStepVisualService
    {
        private static ShadowStepReadyAura Aura;

        internal static void ShowReady(Player player, float duration)
        {
            if (player == null || player != Player.m_localPlayer) return;
            if (Aura != null) UnityEngine.Object.Destroy(Aura.gameObject);
            GameObject root = new GameObject("ValheimMastery_ShadowStepReady");
            root.transform.SetParent(player.transform, false);
            Aura = root.AddComponent<ShadowStepReadyAura>();
            Aura.Initialize(player, duration);
            MasteryVfxMaterial.SpawnPrefab("fx_perfectdodge", player.transform.position + Vector3.up * 0.15f, 0.55f);
            PerkAudioService.Play("knives_35_ready", "sfx_knife_swing", player.transform.position, 0.65f);
            ShowStatus(player, duration, "Тіньовий крок готовий", "Наступний перекат безкоштовний і має +50% дистанції.", "knives_35");
        }

        internal static void PlayDodge(Player player)
        {
            if (player == null) return;
            if (Aura != null) { UnityEngine.Object.Destroy(Aura.gameObject); Aura = null; }
            MasteryVfxMaterial.SpawnPrefab("fx_perfectdodge", player.transform.position + Vector3.up * 0.25f, 1.10f);
            ShadowStepTrailService.Start(player);
            PerkAudioService.Play("knives_35_step", "sfx_dodge", player.transform.position, 0.45f);
        }

        internal static void PlayEnd(Player player)
        {
            if (player == null) return;
            MasteryVfxMaterial.SpawnPrefab("fx_perfectdodge", player.transform.position + Vector3.up * 0.20f, 0.65f);
        }

        private static void ShowStatus(Player player, float duration, string name, string tooltip, string perk)
        {
            if (player.m_seman == null) return;
            SE_Stats marker = ScriptableObject.CreateInstance<SE_Stats>();
            marker.m_name = name; marker.m_tooltip = tooltip; marker.m_icon = PerkUiIconService.ForPerk(perk, player.m_textIcon);
            marker.m_ttl = Mathf.Max(0.1f, duration); marker.m_flashIcon = true;
            player.m_seman.AddStatusEffect(marker, false, 0, marker.m_ttl, 0);
        }
    }

    internal sealed class ShadowStepReadyAura : MonoBehaviour
    {
        private Player Player; private float Until; private Light Glow; private LineRenderer Ring;
        internal void Initialize(Player player, float duration)
        {
            Player = player; Until = Time.time + duration;
            Glow = gameObject.AddComponent<Light>(); Glow.type = LightType.Point; Glow.color = new Color(0.22f, 0.52f, 0.58f); Glow.range = 2.2f; Glow.intensity = 1.6f;
            Ring = gameObject.AddComponent<LineRenderer>(); Ring.useWorldSpace = false; Ring.loop = true; Ring.positionCount = 32; Ring.widthMultiplier = 0.035f;
            MasteryVfxMaterial.AssignOwned(Ring, "fx_perfectdodge");
            for (int i = 0; i < 32; ++i) { float a = i * Mathf.PI * 2f / 32f; Ring.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.55f, 0.06f, Mathf.Sin(a) * 0.55f)); }
            Ring.startColor = Ring.endColor = new Color(0.30f, 0.58f, 0.62f, 0.70f);
        }
        private void Update()
        {
            if (Player == null || Time.time >= Until || !ShadowStep35Service.IsReady(Player)) { Destroy(gameObject); return; }
            transform.localPosition = Vector3.zero;
            float pulse = 0.75f + Mathf.PingPong(Time.time * 2.2f, 0.35f);
            if (Glow != null) Glow.intensity = 1.2f + pulse;
            if (Ring != null) Ring.widthMultiplier = 0.025f + pulse * 0.018f;
        }
    }

    internal static class ShadowStepTrailService
    {
        internal static void Start(Player player)
        {
            if (player == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            GameObject trailObject = new GameObject("ValheimMastery_ShadowStepTrail");
            trailObject.transform.SetParent(player.transform, false); trailObject.transform.localPosition = Vector3.up * 0.75f;
            TrailRenderer trail = trailObject.AddComponent<TrailRenderer>(); MasteryVfxMaterial.AssignOwned(trail, "fx_perfectdodge");
            trail.time = 0.40f; trail.minVertexDistance = 0.035f; trail.startWidth = 0.34f; trail.endWidth = 0.01f;
            trail.startColor = new Color(0.36f, 0.62f, 0.66f, 0.82f); trail.endColor = new Color(0.08f, 0.10f, 0.12f, 0f);
            UnityEngine.Object.Destroy(trailObject, 0.85f);
        }
    }

    internal static class AssassinBlinkVisualService
    {
        private static Character Candidate; private static KnifeTargetMarker Marker;

        internal static void SetCandidate(Character candidate)
        {
            if (Candidate == candidate && Marker != null) return;
            if (Marker != null) UnityEngine.Object.Destroy(Marker.gameObject);
            Candidate = candidate; Marker = null;
            if (candidate == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            GameObject root = new GameObject("ValheimMastery_Knife35Target"); root.transform.SetParent(candidate.transform, false);
            Marker = root.AddComponent<KnifeTargetMarker>(); Marker.Initialize(candidate);
        }

        internal static bool IsCandidate(Character character) => character != null && character == Candidate;

        internal static void PlayDeparture(Player player, Vector3 origin)
        {
            // Native shadow vapor at the departing body, without pickup glitter or
            // stacked geometric rings. Every prefab is an isolated, finite clone.
            PerkNativeFeedback.PlayVfx("vfx_ShadowPerson_death", origin + Vector3.up * 0.65f, 0.50f, 1.20f);
            PerkNativeFeedback.PlayVfx("fx_perfectdodge", origin + Vector3.up * 0.25f, 0.48f, 0.80f);
            PerkAudioService.Play("knives_35_blink_depart", "sfx_dodge", origin, 0.20f,
                volumeScale: 0.65f, pitchScale: 0.92f);
        }

        internal static void PlayArrival(Player player, Character target, Vector3 origin, Vector3 destination, float cooldown)
        {
            // Arrival reads as a body emerging behind the target. The actual hit
            // has its own later cue; do not show damage sparks before it happens.
            PerkNativeFeedback.PlayVfx("vfx_odin_despawn", destination + Vector3.up * 0.55f, 0.42f, 1.25f);
            PerkNativeFeedback.PlayVfx("vfx_ShadowPerson_hit", destination + Vector3.up * 0.85f, 0.72f, 0.85f);
            PerkAudioService.Play("knives_35_blink_arrive", "sfx_knife_swing", destination, 0.20f,
                volumeScale: 0.75f, pitchScale: 1.08f);
            ShowCooldown(player, cooldown);
            SetCandidate(null);
        }

        internal static void PlayImpact(Vector3 point)
        {
            PerkNativeFeedback.PlayVfx("vfx_ShadowPerson_hit", point, 0.85f, 0.80f);
            PerkNativeFeedback.PlayVfx("vfx_BloodHit", point, 0.90f, 0.65f);
            PerkNativeFeedback.PlayVfx("vfx_HitSparks", point, 0.48f, 0.50f);
            // There is no sfx_knife_hit in the current vanilla manifest.
            PerkAudioService.Play("knives_35_blink_impact", "sfx_sword_hit", point, 0.18f,
                volumeScale: 0.80f, pitchScale: 1.04f);
        }
        internal static void RefreshCooldown(Player player)
        {
            double seconds = PerkCooldownStateService.GetRemainingSeconds(player, "knives_35");
            if (seconds > 0d) ShowCooldown(player, (float)seconds);
        }

        private static void ShowCooldown(Player player, float duration)
        {
            if (player == null || player.m_seman == null) return;
            SE_Stats marker = ScriptableObject.CreateInstance<SE_Stats>(); marker.m_name = "Крок убивці";
            marker.m_tooltip = "Телепорт відновлюється."; marker.m_icon = PerkUiIconService.ForPerk("knives_35", player.m_textIcon);
            marker.m_ttl = Mathf.Max(0.1f, duration); marker.m_flashIcon = false;
            player.m_seman.AddStatusEffect(marker, false, 0, marker.m_ttl, 0);
        }
    }

    internal sealed class KnifeTargetMarker : MonoBehaviour
    {
        private Character Target; private Light Glow; private LineRenderer Left; private LineRenderer Right;
        internal void Initialize(Character target)
        {
            Target = target; float r = Mathf.Clamp(target.GetRadius(), 0.35f, 1.6f);
            transform.localPosition = Vector3.up * (r * 1.8f);
            Glow = gameObject.AddComponent<Light>(); Glow.type = LightType.Point; Glow.color = new Color(0.32f, 0.62f, 0.66f); Glow.range = Mathf.Clamp(r * 2f, 1f, 2.8f); Glow.intensity = 0.70f; Glow.shadows = LightShadows.None;
            Left = MakeLine(new Vector3(-r * 0.55f, r * 0.30f, 0f), new Vector3(-r * 0.18f, 0f, 0f));
            Right = MakeLine(new Vector3(r * 0.55f, r * 0.30f, 0f), new Vector3(r * 0.18f, 0f, 0f));
        }
        private LineRenderer MakeLine(Vector3 a, Vector3 b)
        {
            GameObject child = new GameObject("knife_bracket"); child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>(); MasteryVfxMaterial.AssignOwned(line, "vfx_HitSparks");
            line.useWorldSpace = false; line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); line.startWidth = 0.055f; line.endWidth = 0.018f;
            line.startColor = new Color(0.62f, 0.84f, 0.85f, 0.92f); line.endColor = new Color(0.18f, 0.36f, 0.40f, 0.65f); return line;
        }
        private void LateUpdate()
        {
            if (Target == null || Target.IsDead() || !AssassinBlinkVisualService.IsCandidate(Target) || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) { Destroy(gameObject); return; }
            Camera camera = Camera.main; if (camera != null) { Vector3 towardCamera = (camera.transform.position - Target.GetCenterPoint()).normalized; transform.position = Target.GetCenterPoint() + Vector3.up * (Target.GetRadius() * 0.72f) + towardCamera * (Target.GetRadius() + 0.18f); transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up); }
            if (Glow != null) Glow.intensity = 0.55f + Mathf.PingPong(Time.time * 0.9f, 0.30f);
        }
    }

    internal sealed class AxeClientVisualState
    {
        internal int Stacks; internal float StackUntil; internal float ExecutionUntil; internal Character Target;
        internal AxeWorldTargetMarker Marker;
    }

    internal static class AxeTargetVisualService
    {
        private static readonly Dictionary<ZDOID, AxeClientVisualState> States = new Dictionary<ZDOID, AxeClientVisualState>();
        private static readonly List<ZDOID> Expired = new List<ZDOID>();
        private static float NextCleanupAt;

        internal static void SendStacks(Player owner, Character target, int stacks, float duration)
        {
            if (owner == null || target == null) return;
            ZDOID id = target.GetZDOID();
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                if (Player.m_localPlayer != null) { Get(id).Target = target; ApplyStacks(id, stacks, duration); }
                NetworkSync.BroadcastProcFeedback(Pack("axe_stack", id, stacks, duration), target.GetCenterPoint());
                return;
            }
            Get(id).Target = target; ApplyStacks(id, stacks, duration);
        }

        internal static void SendExecutionReady(Player owner, Character target, float duration)
        {
            if (owner == null || target == null) return;
            ZDOID id = target.GetZDOID();
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                if (Player.m_localPlayer != null) { Get(id).Target = target; ApplyReady(id, duration); }
                NetworkSync.BroadcastProcFeedback(Pack("axe_ready", id, 0, duration), target.GetCenterPoint());
                return;
            }
            Get(id).Target = target; ApplyReady(id, duration);
        }

        internal static void SendExecution(Player owner, Character target)
        {
            if (owner == null || target == null) return;
            ZDOID id = target.GetZDOID();
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                if (Player.m_localPlayer != null) { Get(id).Target = target; ApplyExecution(id, target.GetCenterPoint()); }
                if (owner == Player.m_localPlayer) ShowExecutionStatus();
                else NetworkSync.SendProcFeedback(owner, Pack("axe_execute_owner", id, 0, 0f), target.GetCenterPoint());
                NetworkSync.BroadcastProcFeedback(Pack("axe_execute", id, 0, 0f), target.GetCenterPoint());
                return;
            }
            Get(id).Target = target; ApplyExecution(id, target.GetCenterPoint());
            if (owner == Player.m_localPlayer) ShowExecutionStatus();
        }

        private static string Pack(string kind, ZDOID id, int value, float duration) => kind + ":" + id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture) + ":" + value + ":" + duration.ToString("0.###", CultureInfo.InvariantCulture);

        internal static bool TryHandle(string procId, Vector3 fallbackPosition)
        {
            if (string.IsNullOrEmpty(procId) || !procId.StartsWith("axe_", StringComparison.Ordinal)) return false;
            string[] p = procId.Split(':');
            if (p.Length != 5 || !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) ||
                !uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) ||
                !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
                !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float duration)) return false;
            ZDOID zdoid = new ZDOID(user, id);
            if (p[0] == "axe_execute_owner") ShowExecutionStatus();
            else if (p[0] == "axe_stack") ApplyStacks(zdoid, value, duration);
            else if (p[0] == "axe_ready") ApplyReady(zdoid, duration);
            else if (p[0] == "axe_execute") ApplyExecution(zdoid, fallbackPosition);
            else return false;
            return true;
        }

        private static AxeClientVisualState Get(ZDOID id)
        {
            if (!States.TryGetValue(id, out AxeClientVisualState state)) States[id] = state = new AxeClientVisualState();
            if (state.Target == null) state.Target = ZNetScene.instance?.FindInstance(id)?.GetComponent<Character>();
            return state;
        }

        internal static void Maintain()
        {
            if (Time.time < NextCleanupAt) return;
            NextCleanupAt = Time.time + 1f;
            Expired.Clear();
            foreach (KeyValuePair<ZDOID, AxeClientVisualState> pair in States)
            {
                AxeClientVisualState state = pair.Value;
                bool dead = !ReferenceEquals(state.Target, null) && (state.Target == null || state.Target.IsDead());
                if (dead || (state.StackUntil < Time.time && state.ExecutionUntil < Time.time))
                {
                    DestroyMarker(state);
                    Expired.Add(pair.Key);
                    continue;
                }
                // An RPC may arrive before its exact target instance; never borrow a nearby enemy.
                if (state.Target == null)
                {
                    state.Target = ZNetScene.instance?.FindInstance(pair.Key)?.GetComponent<Character>();
                    if (state.Target != null && state.ExecutionUntil >= Time.time) EnsureMarker(state);
                }
            }
            foreach (ZDOID id in Expired) States.Remove(id);
            Expired.Clear();
        }

        internal static void Reset()
        {
            foreach (AxeClientVisualState state in States.Values) DestroyMarker(state);
            States.Clear();
            Expired.Clear();
            NextCleanupAt = 0f;
        }

        private static void DestroyMarker(AxeClientVisualState state)
        {
            if (state.Marker != null) UnityEngine.Object.Destroy(state.Marker.gameObject);
            state.Marker = null;
        }

        private static void ApplyStacks(ZDOID id, int stacks, float duration)
        {
            AxeClientVisualState state = Get(id); state.Stacks = Mathf.Clamp(stacks, 1, 3); state.StackUntil = Time.time + Mathf.Max(0.1f, duration);
            if (state.Target != null) { MasteryVfxMaterial.SpawnPrefab("vfx_BloodHit", state.Target.GetCenterPoint(), 0.85f + state.Stacks * 0.20f); PerkAudioService.Play("axes_stack_" + state.Stacks, "sfx_axe_hit", state.Target.GetCenterPoint(), 0.32f); }
            LogVisual("AXE_STACK", id, state);
        }

        private static void ApplyReady(ZDOID id, float duration)
        {
            AxeClientVisualState state = Get(id); state.ExecutionUntil = Time.time + Mathf.Max(0.1f, duration); EnsureMarker(state); MasteryHeldWeaponGlow.ShowAxeWindup(Player.m_localPlayer, duration);
            if (state.Target != null) { MasteryVfxMaterial.SpawnPrefab("vfx_perfectblock", state.Target.GetCenterPoint(), 1.15f); PerkAudioService.Play("axes_execute_ready", "sfx_perfectblock", state.Target.GetCenterPoint(), 0.75f); }
            LogVisual("AXE_READY", id, state);
        }

        private static void ApplyExecution(ZDOID id, Vector3 fallbackPosition)
        {
            AxeClientVisualState state = Get(id); state.ExecutionUntil = 0f;
            if (state.Target != null) { AxeExecutionSlashService.ShowAt(state.Target.GetCenterPoint(), state.Target.GetRadius()); if (state.Target.IsBoss()) AxeExecutionSlashService.ShowBossAt(state.Target.GetCenterPoint(), state.Target.GetRadius()); }
            else AxeExecutionSlashService.ShowAt(fallbackPosition, 0.9f);
            LogVisual("AXE_EXECUTE", id, state);
        }

        private static void ShowExecutionStatus()
        {
            Player local = Player.m_localPlayer;
            if (local?.m_seman != null)
            {
                SE_Stats marker = ScriptableObject.CreateInstance<SE_Stats>(); marker.m_name = "Кривавий кат";
                marker.m_tooltip = "+25% різальної шкоди на 10 с. Кат відновлюється 15 с."; marker.m_icon = PerkUiIconService.ForPerk("axes_70", local.m_textIcon);
                marker.m_ttl = 15f; marker.m_flashIcon = false; local.m_seman.AddStatusEffect(marker, false, 0, marker.m_ttl, 0);
            }
        }

        private static void EnsureMarker(AxeClientVisualState state)
        {
            if (state.Target == null) return;
            if (state.Marker == null) state.Marker = state.Target.GetComponentInChildren<AxeWorldTargetMarker>();
            if (state.Marker == null) { GameObject root = new GameObject("ValheimMastery_AxeTarget"); root.transform.SetParent(state.Target.transform, false); state.Marker = root.AddComponent<AxeWorldTargetMarker>(); }
            state.Marker.Bind(state.Target, state);
        }

        internal static bool TryGet(Character target, out AxeClientVisualState state)
        {
            state = null; if (target == null) return false;
            if (!States.TryGetValue(target.GetZDOID(), out state)) return false;
            if (state.Target == null)
            {
                state.Target = target;
                if (state.ExecutionUntil >= Time.time) EnsureMarker(state);
            }
            if (state.StackUntil < Time.time) state.Stacks = 0;
            return state.Stacks > 0 || state.ExecutionUntil >= Time.time;
        }

        private static void LogVisual(string phase, ZDOID id, AxeClientVisualState state)
        {
            if (!MasteryPlugin.Settings.VerboseLogging.Value) return;
            MasteryPlugin.Log.LogInfo("[" + phase + "] target=" + id + " resolved=" + (state.Target != null) + " stacks=" + state.Stacks + " renderers=" + (state.Target != null ? state.Target.GetComponentsInChildren<Renderer>(true).Length : 0));
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class AxeVisualWorldCleanupPatch
    {
        private static void Prefix()
        {
            AxeTargetVisualService.Reset();
            MasteryTargetHudPatch.Reset();
        }
    }

    internal sealed class AxeWorldTargetMarker : MonoBehaviour
    {
        private Character Target; private AxeClientVisualState State; private readonly List<LineRenderer> Lines = new List<LineRenderer>(); private Light ReadyLight;
        internal void Bind(Character target, AxeClientVisualState state) { Target = target; State = state; Build(); }
        private void Build()
        {
            foreach (LineRenderer line in Lines) if (line != null) Destroy(line.gameObject); Lines.Clear();
            float r = Mathf.Clamp(Target.GetRadius(), 0.35f, 2.4f);
            transform.localPosition = Vector3.up * (r * 1.35f);
            int count = 0; // Zarubky are shown only in the enemy HUD; no floating world slashes.
            for (int i = 0; i < count; ++i)
            {
                float x = (i - (count - 1) * 0.5f) * r * 0.24f;
                GameObject child = new GameObject("axe_gouge"); child.transform.SetParent(transform, false);
                LineRenderer line = child.AddComponent<LineRenderer>(); MasteryVfxMaterial.AssignOwned(line, "vfx_BloodHit"); line.useWorldSpace = false; line.positionCount = 2;
                line.SetPosition(0, new Vector3(x - r * 0.16f, r * 0.24f, 0f)); line.SetPosition(1, new Vector3(x + r * 0.16f, -r * 0.24f, 0f));
                line.startWidth = Mathf.Max(0.07f, r * 0.13f); line.endWidth = Mathf.Max(0.025f, r * 0.045f); line.startColor = new Color(1f, 0.28f, 0.02f, 0.95f); line.endColor = new Color(0.42f, 0.01f, 0f, 0.75f); Lines.Add(line);
            }
            if (ReadyLight == null) { ReadyLight = gameObject.AddComponent<Light>(); ReadyLight.type = LightType.Point; ReadyLight.color = new Color(1f, 0.02f, 0.01f); ReadyLight.shadows = LightShadows.None; }
            ReadyLight.enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value && State.ExecutionUntil >= Time.time;
        }
        private void LateUpdate()
        {
            if (Target == null || Target.IsDead() || State == null) { Destroy(gameObject); return; }
            if (State.StackUntil < Time.time) State.Stacks = 0;
            bool ready = State.ExecutionUntil >= Time.time;
            // Zarubky remain in the shared HUD state; this object only owns the brief ready light.
            if (!ready) { Destroy(gameObject); return; }
            ReadyLight.enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (!ReadyLight.enabled) return;
            Camera camera = Camera.main; if (camera != null) { Vector3 towardCamera = (camera.transform.position - Target.GetCenterPoint()).normalized; transform.position = Target.GetCenterPoint() + Vector3.up * (Target.GetRadius() * 0.72f) + towardCamera * (Target.GetRadius() + 0.18f); transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up); }
            float pulse = 0.72f + Mathf.PingPong(Time.time * (ready ? 5f : 2f), ready ? 0.55f : 0.20f);
            foreach (LineRenderer line in Lines) if (line != null) { Color c = ready ? new Color(1f, 0.02f, 0.01f, pulse) : new Color(1f, 0.28f, 0.02f, pulse); line.startColor = c; }
            ReadyLight.enabled = ready; ReadyLight.range = Mathf.Max(2.8f, Target.GetRadius() * 3f); ReadyLight.intensity = ready ? 3.5f + Mathf.PingPong(Time.time * 6f, 4.5f) : 0f;
        }
    }

    internal static class AxeExecutionSlashService
    {
        internal static void ShowBossAt(Vector3 position, float radius)
        {
            float scale = Mathf.Clamp(radius * 1.8f, 3f, 8f);
            MasteryVfxMaterial.SpawnPrefab("vfx_perfectblock", position + Vector3.up * radius, scale);
            MasteryVfxMaterial.SpawnPrefab("vfx_BloodHit", position + Vector3.up * radius * 0.5f, scale * 1.35f);
            MasteryVfxMaterial.SpawnPrefab("vfx_HitSparks", position + Vector3.up * radius * 1.4f, scale);
            PerkAudioService.Play("axes_70_boss_execute", "sfx_goblinbrute_death", position, 1.0f);
        }
        internal static void ShowAt(Vector3 position, float radius)
        {
            MasteryVfxMaterial.SpawnPrefab("vfx_BloodHit", position, 2.5f); MasteryVfxMaterial.SpawnPrefab("vfx_BloodHit", position + Vector3.up * 0.55f, 1.8f); MasteryVfxMaterial.SpawnPrefab("vfx_HitSparks", position, 2.25f); MasteryVfxMaterial.SpawnPrefab("vfx_perfectblock", position + Vector3.up * 0.35f, 1.65f);
            if (MasteryPlugin.Settings.EnablePerkProcVFX.Value)
            {
                GameObject root = new GameObject("ValheimMastery_AxeExecutionSlash"); AxeExecutionSlashVisual v = root.AddComponent<AxeExecutionSlashVisual>(); v.Initialize(position, Mathf.Clamp(radius, 0.65f, 2.5f));
            }
            PerkAudioService.Play("axes_70_execute", "sfx_axe_hit", position, 0.95f); PerkAudioService.Play("axes_70_execute_death", "sfx_goblinbrute_death", position, 0.82f);
        }
    }

    internal sealed class AxeExecutionSlashVisual : MonoBehaviour
    {
        private float Born; private LineRenderer Slash;
        internal void Initialize(Vector3 position, float radius)
        {
            Born = Time.time; Slash = gameObject.AddComponent<LineRenderer>(); MasteryVfxMaterial.AssignOwned(Slash, "vfx_BloodHit"); Slash.useWorldSpace = true; Slash.positionCount = 3;
            Vector3 right = Camera.main != null ? Camera.main.transform.right : Vector3.right; Vector3 diagonal = (right + Vector3.up * 0.65f).normalized;
            Slash.SetPosition(0, position - diagonal * radius); Slash.SetPosition(1, position); Slash.SetPosition(2, position + diagonal * radius);
            Slash.startWidth = radius * 0.16f; Slash.endWidth = radius * 0.025f; Slash.startColor = new Color(1f, 0.02f, 0.01f, 1f); Slash.endColor = new Color(0.35f, 0f, 0f, 0.72f); Destroy(gameObject, 0.55f);
        }
        private void Update() { if (Slash == null) return; float a = 1f - Mathf.Clamp01((Time.time - Born) / 0.50f); Slash.startColor = new Color(1f, 0.02f, 0.01f, a); Slash.endColor = new Color(0.35f, 0f, 0f, a * 0.6f); }
    }

    [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
    internal static class MasteryTargetHudPatch
    {
        private static readonly HashSet<GuiBar> Tinted = new HashSet<GuiBar>();
        private static readonly HashSet<GuiBar> Active = new HashSet<GuiBar>();
        private static readonly List<GuiBar> Stale = new List<GuiBar>();
        private static void Postfix(EnemyHud __instance)
        {
            AxeTargetVisualService.Maintain();
            if (__instance?.m_huds == null) return;
            Active.Clear();
            foreach (KeyValuePair<Character, EnemyHud.HudData> pair in __instance.m_huds)
            {
                Character target = pair.Key; EnemyHud.HudData hud = pair.Value; if (target == null || hud == null) continue;
                bool knife = AssassinBlinkVisualService.IsCandidate(target);
                bool axe = AxeTargetVisualService.TryGet(target, out AxeClientVisualState state);
                if (!knife && !axe) continue;
                bool ready = axe && state.ExecutionUntil >= Time.time;
                Color color = ready ? new Color(0.95f, 0.03f, 0.01f, 1f) : knife ? new Color(0.78f, 0.10f, 0.48f, 1f) : new Color(1f, 0.38f, 0.02f, 1f);
                Tint(hud.m_healthFast, color);
                Tint(hud.m_healthSlow, color);
                if (hud.m_name != null)
                {
                    string suffix = ready ? " <color=#FF2010>[КАТ]</color>" : knife ? " <color=#D52D8C>[ЦІЛЬ]</color>" :
                        " <color=#FF6A10>" + new string('╱', Mathf.Clamp(state.Stacks, 1, 3)) + "</color>";
                    if (!hud.m_name.text.Contains("[КАТ]") && !hud.m_name.text.Contains("[ЦІЛЬ]") && !hud.m_name.text.Contains("╱")) hud.m_name.text += suffix;
                }
            }
            Stale.Clear();
            foreach (GuiBar bar in Tinted) if (bar == null || !Active.Contains(bar)) Stale.Add(bar);
            foreach (GuiBar bar in Stale) { if (bar != null) bar.ResetColor(); Tinted.Remove(bar); }
            Stale.Clear();
        }

        private static void Tint(GuiBar bar, Color color)
        {
            if (bar == null) return;
            bar.SetColor(color);
            Active.Add(bar);
            Tinted.Add(bar);
        }

        internal static void Reset()
        {
            foreach (GuiBar bar in Tinted) if (bar != null) bar.ResetColor();
            Tinted.Clear();
            Active.Clear();
            Stale.Clear();
        }
    }

    internal static class PolearmSpinVisualService
    {
        private sealed class PulseState { internal int LastStack = -1; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, PulseState> PulseStates = new System.Runtime.CompilerServices.ConditionalWeakTable<Player, PulseState>();

        internal static GameObject Start(Player player, float baseRadius)
        {
            if (player == null) return null;
            // Defensive cleanup for interrupted/reloaded attacks: never allow persistent spin VFX to pile up.
            foreach (PolearmSpinVisual stale in player.GetComponentsInChildren<PolearmSpinVisual>(true))
                if (stale != null) UnityEngine.Object.Destroy(stale.gameObject);

            GameObject root = new GameObject("ValheimMastery_Polearm70ContinuousSpin");
            root.transform.SetParent(player.transform, false);
            root.transform.localPosition = Vector3.up * 0.08f;
            PolearmSpinVisual visual = root.AddComponent<PolearmSpinVisual>();
            visual.Initialize(player, baseRadius);

            // The perimeter/sweep persist; the native wing-wind accent is a single bounded lease.
            // Never instantiate the fallenvalkyrie_spin gameplay attack prefab.
            PulseState pulse = PulseStates.GetOrCreateValue(player); pulse.LastStack = -1;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Polearms70VFX] phase=START radius=" + baseRadius.ToString("0.00") + " renderers=" + root.GetComponentsInChildren<Renderer>(true).Length + " particles=" + root.GetComponentsInChildren<ParticleSystem>(true).Length);
            return root;
        }

        internal static void Pulse(Player player, int stacks)
        {
            if (player == null) return;
            PolearmSpinVisual visual = player.GetComponentInChildren<PolearmSpinVisual>();
            if (visual != null) visual.Pulse(stacks);

            PulseState pulse = PulseStates.GetOrCreateValue(player);
            bool stackAdvanced = stacks > pulse.LastStack;
            PerkAudioService.Play("polearms_70_cycle", "sfx_atgeir_attack_secondary", player.transform.position, stackAdvanced ? 0.18f : 0.42f);
            pulse.LastStack = Mathf.Max(pulse.LastStack, stacks);
            // Never enqueue the same HUD message every high-speed cycle at five stacks.
            if (stackAdvanced && stacks > 0) player.Message(MessageHud.MessageType.TopLeft, "Вихор " + stacks + "/5  |  швидкість +" + (stacks * 40) + "%");
        }
    }

    internal sealed class PolearmSpinVisual : MonoBehaviour
    {
        private Player Player;
        private LineRenderer Outer;
        private LineRenderer Sweep;
        private int Stacks;
        private float BaseRadius;
        private float ActualRadius;
        private float PulseUntil;
        private Material OuterMaterial;
        private Material SweepMaterial;
        private GameObject WindBurst;
        private NativeWindSwirl NativeWind;
        private float NextWindAt;
        private readonly Vector3[] SweepPoints = new Vector3[11];

        internal void Initialize(Player player, float baseRadius)
        {
            Player = player;
            BaseRadius = Mathf.Max(1f, baseRadius);
            Outer = MakeLine("exact_damage_radius", 40, true, 0.055f, out OuterMaterial);
            Sweep = MakeLine("rotating_attack_sweep", 11, false, 0.12f, out SweepMaterial);
            SetStacks(0);
            NativeWind = NativeWindSwirl.Create(transform);
            Outer.enabled = Sweep.enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
        }

        internal void SetStacks(int stacks)
        {
            int next = Mathf.Clamp(stacks, 0, 5);
            if (Stacks == next && ActualRadius > 0f) return;
            Stacks = next;
            ActualRadius = BaseRadius * (1f + 0.20f * Stacks);
            if (NativeWind != null) { NativeWind.Radius = ActualRadius; NativeWind.Strength = 1f + Stacks; }
            SetRingRadius(Outer, ActualRadius);
            Color color = Color.Lerp(new Color(0.66f, 0.78f, 0.86f, 0.35f), new Color(0.90f, 0.97f, 1f, 0.64f), Stacks / 5f);
            if (Outer != null) { Outer.widthMultiplier = 0.04f + 0.006f * Stacks; Outer.startColor = Outer.endColor = color; }
            if (Sweep != null)
            {
                Sweep.widthMultiplier = 0.08f + 0.010f * Stacks;
                Sweep.startColor = new Color(color.r, color.g, color.b, Mathf.Min(0.85f, color.a + 0.18f));
                Sweep.endColor = new Color(color.r, color.g, color.b, 0.02f);
                for (int i = 0; i < SweepPoints.Length; ++i)
                {
                    float a = -i * 7.5f * Mathf.Deg2Rad;
                    float r = ActualRadius * (1f - i * 0.012f);
                    SweepPoints[i] = new Vector3(Mathf.Cos(a) * r, 0.10f, Mathf.Sin(a) * r);
                }
                Sweep.SetPositions(SweepPoints);
            }
        }

        internal void Pulse(int stacks)
        {
            SetStacks(stacks);
            PulseUntil = Time.time + 0.16f;
            if (NativeWind != null) return;
            if (MasteryPlugin.Settings.EnablePerkProcVFX.Value && Time.time >= NextWindAt)
            {
                NextWindAt = Time.time + 0.90f;
                ReleaseOwnedWind();
                WindBurst = PerkNativeFeedback.PlayVfx("fx_fallenfalkyrie_spin", Player.transform.position + Vector3.up * 0.12f,
                    Mathf.Clamp(ActualRadius * 0.13f, 0.30f, 0.90f), 0.65f);
                if (WindBurst != null) WindBurst.transform.SetParent(transform, true);
            }
        }

        private void ReleaseOwnedWind()
        {
            // A finished lease is detached by VfxPool and may already belong to another
            // effect. Only destroy the still-parented lease owned by this spin.
            if (WindBurst != null && WindBurst.transform.parent == transform) Destroy(WindBurst);
            WindBurst = null;
        }

        private LineRenderer MakeLine(string name, int points, bool loop, float width, out Material ownedMaterial)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);
            LineRenderer ring = child.AddComponent<LineRenderer>();
            ownedMaterial = MasteryVfxMaterial.CloneFromPrefab("vfx_HitSparks");
            ring.material = ownedMaterial;
            ring.useWorldSpace = false; ring.loop = loop; ring.positionCount = points;
            ring.widthMultiplier = width; ring.numCornerVertices = 1; ring.numCapVertices = 1;
            return ring;
        }

        private static void SetRingRadius(LineRenderer ring, float radius)
        {
            if (ring == null) return;
            for (int i = 0; i < ring.positionCount; ++i)
            {
                float a = i * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.06f, Mathf.Sin(a) * radius));
            }
        }

        private void Update()
        {
            if (Player == null || !Polearms70ContinuousSpinService.IsActive(Player)) { Destroy(gameObject); return; }
            bool visible = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (Outer != null) Outer.enabled = visible && NativeWind == null;
            if (Sweep != null) Sweep.enabled = visible && NativeWind == null;
            if (!visible)
            {
                ReleaseOwnedWind();
                return;
            }
            if (Sweep != null)
                Sweep.transform.localRotation = Quaternion.Euler(0f, -Time.time * (260f + 70f * Stacks), 0f);
            if (Outer != null)
            {
                float pulse = Time.time < PulseUntil ? 1.65f : 1f;
                Outer.widthMultiplier = (0.04f + 0.006f * Stacks) * pulse;
            }
        }

        private void OnDestroy()
        {
            if (OuterMaterial != null) Destroy(OuterMaterial);
            if (SweepMaterial != null) Destroy(SweepMaterial);
        }
    }

    internal static class PolearmBurstVisualService
    {
        internal static void Play(Player player, float radius)
        {
            if (player == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            GameObject root = new GameObject("ValheimMastery_Polearm35PhantomArc");
            root.SetActive(false);
            root.transform.position = player.transform.position;
            root.transform.rotation = player.transform.rotation;
            PolearmBurstVisual burst = root.AddComponent<PolearmBurstVisual>();
            burst.Initialize(Mathf.Max(1f, radius), player);
            root.SetActive(true);
            PerkNativeFeedback.PlayVfx("fx_land", player.transform.position, .28f, .35f);
        }
    }

    internal sealed class PolearmBurstVisual : MonoBehaviour
    {
        private LineRenderer Ring;
        private Material OwnedMaterial;
        private float Radius;
        private float Born;
        private readonly List<Material> AfterimageMaterials = new List<Material>();
        private Animator AfterimageAnimator;
        private NativeWindSwirl NativeWind;
        private string AttackTrigger;
        private readonly Vector3[] UnitPoints = new Vector3[40];
        private readonly Vector3[] RingPoints = new Vector3[40];

        internal void Initialize(float radius, Player player)
        {
            Radius = radius; Born = Time.time;
            NativeWind = NativeWindSwirl.Create(transform);
            if (NativeWind != null) { NativeWind.Radius = radius; NativeWind.Strength = 3f; }
            Ring = gameObject.AddComponent<LineRenderer>();
            OwnedMaterial = MasteryVfxMaterial.CloneFromPrefab("vfx_HitSparks");
            Ring.material = OwnedMaterial; Ring.useWorldSpace = false; Ring.loop = true;
            // No placeholder full white ring. The actual attack is conveyed by
            // the dressed afterimage and native wind particles.
            Ring.enabled = false;
            Ring.positionCount = 40; Ring.numCornerVertices = 1; Ring.widthMultiplier = 0.12f;
            Ring.startColor = Ring.endColor = new Color(0.78f, 0.90f, 1f, 0.86f);
            for (int i = 0; i < UnitPoints.Length; ++i)
            {
                float angle = i * Mathf.PI * 2f / UnitPoints.Length;
                UnitPoints[i] = new Vector3(Mathf.Cos(angle), 0.10f + Mathf.Sin(angle * 3f) * 0.035f, Mathf.Sin(angle));
            }
            UpdateRing(Radius * 0.22f);
            CreateAfterimage(player);
        }

        private void CreateAfterimage(Player player)
        {
            if (player.m_visual == null) return;
            // Clone only the currently dressed visual beneath an inactive parent.
            // No Player, ZNetView, collision, audio or animation events can execute.
            GameObject visual = UnityEngine.Object.Instantiate(player.m_visual, transform, false);
            visual.name = "Polearm35_WindAfterimage";
            visual.transform.localPosition = player.m_visual.transform.localPosition;
            visual.transform.localRotation = Quaternion.identity;
            foreach (MonoBehaviour script in visual.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(script is ZNetView)) UnityEngine.Object.DestroyImmediate(script);
            foreach (ZNetView view in visual.GetComponentsInChildren<ZNetView>(true)) UnityEngine.Object.DestroyImmediate(view);
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
            foreach (AudioSource audio in visual.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(audio);
            foreach (ParticleSystem particles in visual.GetComponentsInChildren<ParticleSystem>(true))
                UnityEngine.Object.DestroyImmediate(particles.gameObject);
            AfterimageAnimator = visual.GetComponentInChildren<Animator>(true);
            foreach (Animator animator in visual.GetComponentsInChildren<Animator>(true))
            { animator.fireEvents = false; animator.applyRootMotion = false; animator.speed = 5f; }
            AttackTrigger = player.GetCurrentWeapon()?.m_shared?.m_secondaryAttack?.m_attackAnimation;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    Material clone = new Material(materials[i]);
                    clone.SetOverrideTag("RenderType", "Transparent");
                    if (clone.HasProperty("_Mode")) clone.SetFloat("_Mode", 2f);
                    if (clone.HasProperty("_SrcBlend")) clone.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (clone.HasProperty("_DstBlend")) clone.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (clone.HasProperty("_ZWrite")) clone.SetInt("_ZWrite", 0);
                    clone.DisableKeyword("_ALPHATEST_ON"); clone.EnableKeyword("_ALPHABLEND_ON");
                    clone.DisableKeyword("_ALPHAPREMULTIPLY_ON"); clone.renderQueue = 3000;
                    if (clone.HasProperty("_Color")) clone.color = new Color(.82f, .90f, .94f, .32f);
                    materials[i] = clone; AfterimageMaterials.Add(clone);
                }
                renderer.sharedMaterials = materials;
            }
        }

        private void OnEnable()
        {
            if (AfterimageAnimator != null && !string.IsNullOrEmpty(AttackTrigger))
                AfterimageAnimator.SetTrigger(AttackTrigger);
        }

        private void UpdateRing(float radius)
        {
            for (int i = 0; i < RingPoints.Length; ++i)
                RingPoints[i] = new Vector3(UnitPoints[i].x * radius, UnitPoints[i].y, UnitPoints[i].z * radius);
            Ring.SetPositions(RingPoints);
        }

        private void Update()
        {
            float t = Mathf.Clamp01((Time.time - Born) / 0.38f);
            float radius = Mathf.Lerp(Radius * 0.22f, Radius, 1f - Mathf.Pow(1f - t, 3f));
            if (NativeWind != null) { NativeWind.Radius = radius; NativeWind.Fade = 1f - t; }
            UpdateRing(radius);
            Color color = new Color(0.78f, 0.90f, 1f, (1f - t) * 0.86f);
            Ring.startColor = Ring.endColor = color;
            Ring.widthMultiplier = Mathf.Lerp(0.14f, 0.035f, t);
            foreach (Material material in AfterimageMaterials)
                if (material != null && material.HasProperty("_Color"))
                    material.color = new Color(.82f, .90f, .94f, (1f - t) * .32f);
            if (t >= 1f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (OwnedMaterial != null) Destroy(OwnedMaterial);
            foreach (Material material in AfterimageMaterials) if (material != null) Destroy(material);
            AfterimageMaterials.Clear();
        }
    }
}
