using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class WorkshopConnectionVisual
    {
        private static GameObject Template;
        private static float Next;
        private static float NextDiagnostic;
        private static Recipe SelectedRecipe;
        private static CraftingStation SelectedStation;
        internal static void ResetSelection()
        { SelectedRecipe = null; SelectedStation = null; Next = 0f; }
        internal static void Select(InventoryGui gui)
        {
            ResetSelection();
            if (gui == null || gui.m_currentContainer != null || !InventoryGui.IsVisible()) return;
            SelectedStation = Player.m_localPlayer?.GetCurrentCraftingStation();
            if (SelectedStation != null) SelectedRecipe = gui.m_selectedRecipe.Recipe;
        }
        private static void Diagnose(Recipe recipe, string status)
        {
            if (!MasteryPlugin.Settings.VerboseLogging.Value || Time.time < NextDiagnostic) return;
            NextDiagnostic = Time.time + 5f;
            MasteryPlugin.Log.LogInfo("[WorkshopPreview] recipe=" + recipe.name + " " + status);
        }
        internal static void Update(InventoryGui gui)
        {
            var player = Player.m_localPlayer;
            var station = player?.GetCurrentCraftingStation();
            // InventoryGui remembers its last recipe even in container mode.
            // Only an explicit recipe selection in this station session opts in.
            if (gui == null || !InventoryGui.IsVisible() || gui.m_currentContainer != null ||
                station == null || station != SelectedStation || SelectedRecipe == null ||
                gui.m_selectedRecipe.Recipe != SelectedRecipe || gui.m_crafting == null ||
                !gui.m_crafting.gameObject.activeInHierarchy)
            { ResetSelection(); return; }
            if (Time.time < Next || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            Next = Time.time + 1f;
            var recipe = gui?.m_selectedRecipe.Recipe;
            int quality = gui?.m_selectedRecipe.ItemData?.m_quality + 1 ?? 1;
            int amount = gui != null && gui.m_multiCrafting ? gui.m_multiCraftAmount : 1;
            if (recipe == null) return;
            if (!WorkshopRemoteCraft.TryCosts(recipe, quality, amount, out var needed))
            { Diagnose(recipe, "unsupported costs; quality=" + quality + ", amount=" + amount); return; }
            if (!WorkshopStoragePreview.Refresh(player))
            {
                Diagnose(recipe, "preview unavailable; ready=" + WorkshopRemoteCraft.Ready +
                    ", busy=" + WorkshopRemoteCraft.ClientBusy + ", recovery=" + WorkshopRecovery.Outstanding +
                    ", component=" + (player == null ? -1 : WorkshopNetwork.CoveredComponent(player.transform.position)));
                return;
            }
            if (Template == null && StationExtension.m_allExtensions != null)
                foreach (var extension in StationExtension.m_allExtensions)
                    if (extension != null && extension.m_connectionPrefab != null) { Template = extension.m_connectionPrefab; break; }
            if (Template == null) { Diagnose(recipe, "no station connection asset"); return; }
            var keys = new List<string>(needed.Keys);
            foreach (string item in keys) needed[item] = Mathf.Max(0, needed[item] - player.GetInventory().CountItems(item, -1, false));
            Vector3 destination = station.GetConnectionEffectPoint();
            int shown = 0, spawned = 0;
            foreach (var chest in WorkshopStoragePreview.Contributors)
            {
                if (chest == null) continue;
                bool contributes = false;
                foreach (string item in keys)
                {
                    int take = Mathf.Min(needed[item], chest.GetInventory().CountItems(item, -1, false));
                    if (take > 0) { needed[item] -= take; contributes = true; }
                }
                if (!contributes) continue;
                Vector3 origin = chest.transform.position + Vector3.up * .5f;
                Vector3 delta = destination - origin;
                if (delta.sqrMagnitude < .01f) continue;
                var effect = VfxPool.Spawn(Template, origin, Quaternion.LookRotation(delta), .9f);
                if (effect != null)
                {
                    spawned++;
                    effect.transform.localScale = new Vector3(1f, 1f, delta.magnitude);
                    effect.SetActive(true); effect.GetComponent<VfxPoolBaseline>()?.RestartParticles();
                }
                if (++shown >= 6) break;
            }
            var stock = new List<string>();
            if (MasteryPlugin.Settings.VerboseLogging.Value && Time.time >= NextDiagnostic)
                foreach (var key in keys)
                    stock.Add(key + ": inventory=" + player.GetInventory().CountItems(key, -1, false) +
                        ", chests=" + WorkshopStoragePreview.StoredCount(key) + ", stillNeeded=" + needed[key]);
            Diagnose(recipe, "chests=" + WorkshopStoragePreview.Contributors.Count + ", beams=" + spawned +
                ", quality=" + quality + ", amount=" + amount + "; " + string.Join("; ", stock));
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnSelectedRecipe")]
    internal static class WorkshopSourceSelectionPatch
    {
        private static void Postfix(InventoryGui __instance) => WorkshopConnectionVisual.Select(__instance);
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class WorkshopSourceOpenResetPatch
    {
        private static void Prefix() => WorkshopConnectionVisual.ResetSelection();
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class WorkshopSourceCloseResetPatch
    {
        private static void Prefix() => WorkshopConnectionVisual.ResetSelection();
    }
}
