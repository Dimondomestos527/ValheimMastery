using System;
using UnityEngine;

namespace ValheimMastery
{
    internal static class GoldMasterworkPresentation
    {
        // Native manifest: Player/fx/fx_forge_hammer and fx_invupgrade.
        internal static void Begin(InventoryGui gui, Player player, Recipe recipe, Func<bool> currentIntent)
        {
            try
            {
                if (!Local(player) || gui == null || currentIntent?.Invoke() != true) return;
                PlayAudio(player.GetCurrentCraftingStation()?.m_craftItemEffects ?? gui.m_craftItemEffects,
                    "crafting100_working", "sfx_gui_craftitem", player.transform.position);
                var cue = gui.GetComponent<GoldMasterworkWorkingCue>() ?? gui.gameObject.AddComponent<GoldMasterworkWorkingCue>();
                cue.Arm(player, currentIntent);
            }
            catch (Exception error) { Warn(error); }
        }
        // Only after durable Applied save, outside the transaction rollback region.
        internal static void Play(Player player, Recipe recipe)
        {
            try
            {
                if (!Local(player)) return;
                var station = player.GetCurrentCraftingStation();
                EffectList effects = recipe?.GetRequiredStation(1) != null
                    ? station?.m_craftItemDoneEffects : InventoryGui.instance?.m_craftItemDoneEffects;
                PlayAudio(effects, "crafting100_created", "sfx_gui_craftitem_end", player.transform.position);
                if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
                ZNet session = ZNet.instance;
                Vector3 position = Anchor(player);
                GameObject prefab = NativePerkAssetResolver.Resolve("fx_invupgrade");
                if (prefab != null) SpawnVisual(prefab, position, .7f, 1.2f, 96);
                else NativeVfxSafeFrame.Request("fx_invupgrade", loaded =>
                {
                    if (!Local(player) || ZNet.instance != session) return true;
                    return SpawnVisual(loaded, position, .7f, 1.2f, 96);
                });
            }
            catch (Exception error) { Warn(error); }
        }
        internal static bool Local(Player player) => GoldCraftingService.Enabled && !Application.isBatchMode && player != null &&
            player == Player.m_localPlayer && ZNet.instance != null && !player.IsDead() && !player.IsTeleporting();
        internal static Vector3 Anchor(Player player) => player.transform.position + Vector3.up * .95f + player.transform.forward * .35f;
        internal static bool WorkingPulse(Player player)
        {
            if (!Local(player) || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return true;
            GameObject prefab = NativePerkAssetResolver.Resolve("fx_forge_hammer");
            return prefab != null && SpawnVisual(prefab, Anchor(player), .65f, .65f, 48);
        }
        private static bool SpawnVisual(GameObject prefab, Vector3 position, float scale, float lifetime, int particleCap)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return true;
            GameObject visual = VfxPool.Spawn(prefab, position, Quaternion.identity, lifetime);
            if (visual == null) return false;
            VfxPoolBaseline baseline = visual.GetComponent<VfxPoolBaseline>();
            if (baseline?.NativeRoot != null) baseline.NativeRoot.localScale *= scale;
            foreach (var particle in baseline?.Particles ?? Array.Empty<ParticleSystem>())
            {
                if (particle == null) continue;
                var main = particle.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = Mathf.Min(main.maxParticles, particleCap);
                main.startLifetimeMultiplier = Mathf.Min(main.startLifetimeMultiplier, lifetime);
            }
            visual.SetActive(true);
            baseline?.RestartParticles();
            return true;
        }
        private static void PlayAudio(EffectList effects, string cue, string fallback, Vector3 position)
        {
            if (!MasteryPlugin.Settings.EnablePerkSFX.Value) return;
            int count = 0;
            foreach (var effect in effects?.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>())
            {
                if (!effect.m_enabled || effect.m_prefab == null) continue;
                if (++count > 4) break;
                if (PerkAudioService.PlayPrefab(cue, effect.m_prefab, position, .5f)) return;
            }
            PerkAudioService.Play(cue, fallback, position, .5f);
        }
        internal static void Warn(Exception error) => MasteryPlugin.Log.LogWarning("[Gold100] Optional crafting presentation failed: " + error.Message);
    }
    internal sealed class GoldMasterworkWorkingCue : MonoBehaviour
    {
        private Player _player;
        private ZNet _session;
        private Func<bool> _currentIntent;
        private float _next, _until;
        private int _pulses;
        internal void Arm(Player player, Func<bool> currentIntent)
        {
            _player = player; _session = ZNet.instance; _currentIntent = currentIntent;
            _next = Time.unscaledTime; _until = _next + 5f; _pulses = 0;
            enabled = true;
            Update();
        }
        private void Update()
        {
            try
            {
                if (!GoldMasterworkPresentation.Local(_player) || ZNet.instance != _session ||
                    _currentIntent?.Invoke() != true || Time.unscaledTime >= _until || _pulses >= 4)
                { Stop(); return; }
                if (Time.unscaledTime < _next) return;
                if (GoldMasterworkPresentation.WorkingPulse(_player))
                { _pulses++; _next = Time.unscaledTime + 1f; }
                else _next = Time.unscaledTime + .1f;
            }
            catch (Exception error) { Stop(); GoldMasterworkPresentation.Warn(error); }
        }
        private void Stop() { _currentIntent = null; _player = null; enabled = false; }
        private void OnDisable() { _currentIntent = null; _player = null; }
    }
}
