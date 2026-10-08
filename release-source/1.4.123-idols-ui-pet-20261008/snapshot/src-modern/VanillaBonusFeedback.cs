using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Actual game IL: InventoryGui.DoCrafting and CookingStation.OnInteract use
    // InventoryGui.m_craftBonusEffect immediately after DamageText.TextType.Bonus.
    // This runtime reference, not a guessed pickup/forge prefab name, is the source
    // of the bonus cue. The installed manifest names fx_BonusYield / BonusPopV2.wav;
    // the runtime diagnostic below confirms what the loaded game actually assigns.
    internal static class VanillaBonusFeedback
    {
        private static readonly HashSet<int> LoggedPrefabs = new HashSet<int>();
        private static bool _missingLogged;
        private static bool _failureLogged;

        internal static void Show(Player player, int count)
        {
            if (player == null || player != Player.m_localPlayer || count <= 0) return;
            Vector3 point = player.GetCenterPoint();
            DamageText.instance?.ShowText(DamageText.TextType.Bonus, point + Vector3.up * 0.5f,
                "+" + count.ToString(CultureInfo.InvariantCulture), true);
            Play(player);
        }

        internal static void Play(Player player, EffectList fallback = null)
        {
            if (player == null || player != Player.m_localPlayer) return;
            EffectList effects = InventoryGui.instance?.m_craftBonusEffect;
            if (!HasEntries(effects)) effects = fallback;
            if (!HasEntries(effects))
            {
                if (!_missingLogged && MasteryPlugin.Settings.VerboseLogging.Value)
                {
                    _missingLogged = true;
                    MasteryPlugin.Log.LogWarning("[BonusFeedback] No loaded vanilla bonus EffectList; +N is retained, no unrelated pickup sound substituted.");
                }
                return;
            }

            // Ore and falling wood may be distant. The reward belongs to this local
            // recipient, so its sound/text are placed by the player, not by the drop.
            Vector3 point = player.GetCenterPoint();
            foreach (EffectList.EffectData effect in effects.m_effectPrefabs)
            {
                if (effect == null || !effect.m_enabled || effect.m_prefab == null) continue;
                GameObject prefab = effect.m_prefab;
                if (MasteryPlugin.Settings.VerboseLogging.Value && LoggedPrefabs.Add(prefab.GetInstanceID()))
                {
                    List<string> clips = new List<string>();
                    foreach (ZSFX source in prefab.GetComponentsInChildren<ZSFX>(true))
                        if (source.m_audioClips != null)
                            foreach (AudioClip clip in source.m_audioClips)
                                if (clip != null && !clips.Contains(clip.name)) clips.Add(clip.name);
                    foreach (AudioSource source in prefab.GetComponentsInChildren<AudioSource>(true))
                        if (source.clip != null && !clips.Contains(source.clip.name)) clips.Add(source.clip.name);
                    MasteryPlugin.Log.LogInfo("[BonusFeedback] actual vanilla prefab=" + prefab.name +
                        "; audio clips=" + string.Join(",", clips.ToArray()) + "; local recipient cue");
                }
                if (MasteryPlugin.Settings.EnablePerkProcVFX.Value)
                {
                    try
                    {
                        GameObject visual = VfxPool.Spawn(prefab, point, Quaternion.identity, 2.5f);
                        if (visual != null)
                        {
                            visual.SetActive(true);
                            visual.GetComponent<VfxPoolBaseline>()?.RestartParticles();
                        }
                    }
                    catch (Exception error) { LogFailure(error); }
                }
                // VfxPool strips audio while inactive; this is the only audio owner.
                // Tight bursts from a cluster or vein keep all +N but coalesce sound.
                try { PerkAudioService.PlayPrefab("resource_bonus_confirmed", prefab, point, 0.10f); }
                catch (Exception error) { LogFailure(error); }
            }
        }

        private static bool HasEntries(EffectList effects)
        {
            if (effects?.m_effectPrefabs == null) return false;
            foreach (EffectList.EffectData effect in effects.m_effectPrefabs)
                if (effect != null && effect.m_enabled && effect.m_prefab != null) return true;
            return false;
        }

        // Signature preserves exactly the instance + arguments on the vanilla IL stack.
        // This replaces only Pickable's confirmed BONUS effect. Normal pickup effects,
        // bonus chance/count, inventory, XP, and RPC_Pick remain untouched.
        internal static GameObject[] PlayPickableBonus(EffectList original, Vector3 position,
            Quaternion rotation, Transform parent, float scale, int variant, ZDOID creator)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetZDOID() != creator)
                return original?.Create(position, rotation, parent, scale, variant, creator) ?? Array.Empty<GameObject>();
            try { Play(player, original); }
            catch (Exception error)
            {
                // Cosmetic failure must never cancel harvesting or the following RPC.
                LogFailure(error);
            }
            return Array.Empty<GameObject>();
        }

        private static void LogFailure(Exception error)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            MasteryPlugin.Log.LogWarning("[BonusFeedback] Cosmetic cue failed; gameplay retained: " + error.Message);
        }
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class PickableCanonicalBonusFeedbackPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo bonusField = typeof(Pickable).GetField("m_bonusEffect", members);
            MethodInfo create = typeof(EffectList).GetMethod(nameof(EffectList.Create), members, null,
                new[] { typeof(Vector3), typeof(Quaternion), typeof(Transform), typeof(float), typeof(int), typeof(ZDOID) }, null);
            MethodInfo replacement = typeof(VanillaBonusFeedback).GetMethod(nameof(VanillaBonusFeedback.PlayPickableBonus), members);
            int candidate = -1;
            int matches = 0;
            for (int i = 0; i < code.Count; ++i)
            {
                if (bonusField == null || code[i].opcode != OpCodes.Ldfld || !Equals(code[i].operand, bonusField)) continue;
                // Verified 1.0.16 client/server block has one straight-line Create call
                // within 12 instructions of m_bonusEffect. Fail open if that changes.
                for (int j = i + 1; j < code.Count && j <= i + 14; ++j)
                {
                    if (code[j].opcode.FlowControl == FlowControl.Branch ||
                        code[j].opcode.FlowControl == FlowControl.Cond_Branch || code[j].opcode == OpCodes.Ret) break;
                    if (create == null || (code[j].opcode != OpCodes.Call && code[j].opcode != OpCodes.Callvirt) ||
                        !Equals(code[j].operand, create)) continue;
                    candidate = j;
                    matches++;
                    break;
                }
            }
            if (matches == 1 && replacement != null)
            {
                code[candidate].opcode = OpCodes.Call;
                code[candidate].operand = replacement;
            }
            else MasteryPlugin.Log.LogWarning("[BonusFeedback] Pickable bonus IL layout changed; preserving vanilla effect (matches=" + matches + ").");
            return code;
        }
    }
}
