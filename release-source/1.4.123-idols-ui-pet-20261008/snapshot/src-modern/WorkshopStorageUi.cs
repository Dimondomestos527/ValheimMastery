using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    internal static class WorkshopResourceRowPatch
    {
        private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player,
            bool craft, int quality, int craftMultiplier, bool __result)
        {
            if (!__result || req?.m_resItem == null || req.m_upgraderResource || elementRoot == null) return;
            var recipe = InventoryGui.instance?.m_selectedRecipe.Recipe;
            if (craft)
            { if (!WorkshopRemoteCraft.TryCosts(recipe, quality, craftMultiplier, out _)) return; }
            else
            {
                var piece = player?.GetSelectedPiece();
                if (piece == null || System.Array.IndexOf(piece.m_resources, req) < 0) return;
            }
            if (!WorkshopStoragePreview.Refresh(player)) return;
            string name = req.m_resItem.m_itemData.m_shared.m_name;
            long stored = WorkshopStoragePreview.StoredCount(name);
            var amount = elementRoot.Find("res_amount")?.GetComponent<TMP_Text>();
            long personal = player.GetInventory().CountItems(name, -1, false);
            long required = (long)req.GetAmount(quality) * craftMultiplier;
            if (amount != null) amount.color = personal + stored >= required ? Color.white : Color.red;
            var tip = elementRoot.GetComponent<UITooltip>();
            if (tip != null) tip.m_text += "\nПри собі: " + personal + "; у майстерні: " + stored +
                (personal + stored < required ? "\nБракує: " + (required - personal - stored) : "");
        }
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
    internal static class WorkshopPendingCraftUiPatch
    {
        private static void Postfix(InventoryGui __instance)
        {
            WorkshopConnectionVisual.Update(__instance);
            if (WorkshopRemoteCraft.Enabled && WorkshopRemoteCraft.ClientBusy && __instance.m_craftButton != null)
                __instance.m_craftButton.interactable = false;
        }
    }
}
