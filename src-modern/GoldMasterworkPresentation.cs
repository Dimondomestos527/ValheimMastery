using System;
using UnityEngine;

namespace ValheimMastery
{
    internal static class GoldMasterworkPresentation
    {
        // Native manifest: fx_forge_hammer; potential-forge success uses fx_UpgradeStation_Success and sfx_forgeofpotential_success.
        internal static void Begin(InventoryGui gui, Player player, Recipe recipe, Func<bool> currentIntent, int observerOperation = 0)
        {
            try
            {
                if (!Local(player) || gui == null || currentIntent?.Invoke() != true) return;
                GoldMasterworkSuccessSound.Prewarm();
                PlayAudio(player.GetCurrentCraftingStation()?.m_craftItemEffects ?? gui.m_craftItemEffects,
                    "crafting100_working", "sfx_gui_craftitem", player.transform.position);
                var cue = gui.GetComponent<GoldMasterworkWorkingCue>() ?? gui.gameObject.AddComponent<GoldMasterworkWorkingCue>();
                cue.Arm(player, currentIntent, observerOperation);
            }
            catch (Exception error) { Warn(error); }
        }
        // Only after durable Applied save, outside the transaction rollback region.
        internal static void Play(Player player, Recipe recipe)
        {
            try
            {
                if (!Local(player)) return;
                if (MasteryPlugin.Settings.EnablePerkSFX.Value)
                    GoldMasterworkSuccessSound.Request(player);
                if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
                ZNet session = ZNet.instance;
                Vector3 position = player.transform.position;
                GameObject prefab = NativePerkAssetResolver.Resolve("fx_UpgradeStation_Success");
                if (prefab != null) SpawnSuccessVisual(prefab, position);
                else NativeVfxSafeFrame.Request("fx_UpgradeStation_Success", loaded =>
                {
                    if (!Local(player) || ZNet.instance != session) return true;
                    return SpawnSuccessVisual(loaded, position);
                });
            }
            catch (Exception error) { Warn(error); }
        }
        // Observer-only adapters: no player/HUD/gameplay mutation and no local-actor predicate.
        internal static bool ObserverWorkingPulse(Vector3 anchor)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return true;
            var prefab=NativePerkAssetResolver.Resolve("fx_forge_hammer");
            return prefab!=null&&SpawnVisual(prefab,anchor,.65f,.65f,48);
        }
        internal static bool ObserverSuccessVisual(Vector3 position)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return true;
            var prefab=NativePerkAssetResolver.Resolve("fx_UpgradeStation_Success");
            return prefab!=null&&SpawnSuccessVisual(prefab,position);
        }
        internal static bool ObserverSuccessAudio(Vector3 position)
        {
            if (!MasteryPlugin.Settings.EnablePerkSFX.Value) return true;
            var prefab=NativePerkAssetResolver.Resolve("sfx_forgeofpotential_success");
            if(prefab==null)return false;
            return PerkAudioService.PlayPrefab("crafting100_created",prefab,position,.5f);
        }
        internal static void ObserverWorkingAudio(CraftingStation station,Vector3 position)
        {PlayAudio(station?.m_craftItemEffects,"crafting100_working","sfx_gui_craftitem",position);}
        internal static bool Local(Player player) => MasteryPlugin.Settings.Enabled.Value && !Application.isBatchMode && player != null &&
            player == Player.m_localPlayer && ZNet.instance != null && !player.IsDead() && !player.IsTeleporting();
        internal static Vector3 Anchor(Player player) => player.transform.position + Vector3.up * .95f + player.transform.forward * .35f;
        internal static bool WorkingPulse(Player player)
        {
            if (!Local(player) || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return true;
            GameObject prefab = NativePerkAssetResolver.Resolve("fx_forge_hammer");
            return prefab != null && SpawnVisual(prefab, Anchor(player), .65f, .65f, 48);
        }
        private static bool SpawnSuccessVisual(GameObject prefab, Vector3 position)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return true;
            // Preserve the native success particle/scale parameters; pool owns instance lifetime.
            GameObject visual = VfxPool.Spawn(prefab, position, Quaternion.identity, 4f);
            if (visual == null) return false;
            visual.SetActive(true);
            visual.GetComponent<VfxPoolBaseline>()?.RestartParticles();
            return true;
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
        private string _observerCueId;
        internal void Arm(Player player, Func<bool> currentIntent, int observerOperation = 0)
        {
            StopObserver();
            _player = player; _session = ZNet.instance; _currentIntent = currentIntent;
            _next = Time.unscaledTime; _until = _next + 5f; _pulses = 0;
            if(observerOperation>=1&&observerOperation<=3)
            {
                _observerCueId=Guid.NewGuid().ToString("N");
                ObserverCueNetwork.BeginCrafting100Work(player,player.GetCurrentCraftingStation()?.m_nview?.GetZDO()?.m_uid??ZDOID.None,_observerCueId,observerOperation);
            }
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
        private void StopObserver(){var id=_observerCueId;_observerCueId=null;if(id!=null)ObserverCueNetwork.EndCrafting100Work(id);}
        private void Stop() { StopObserver(); _currentIntent = null; _player = null; enabled = false; }
        private void OnDisable() { StopObserver(); _currentIntent = null; _player = null; }
    }
}
