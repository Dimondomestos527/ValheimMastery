using HarmonyLib;

namespace ValheimMastery
{
    internal static class PotentialForgeRepair
    {
        internal const string Secret = "\n<color=#69b9d9>Таємниця майстра: ідоли замінюють матеріали покращення. До звичайної межі спорядження не ламається, а невдача трапляється рідко. Гарантія до Рагнароку відкриває ремонт будь-якого ремонтованого спорядження.</color>";
        private static ZNetScene Scene;
        private static string ForgeName;
        internal static bool Discovered(Player player)
        {
            if (player == null || ZNetScene.instance == null) return false;
            if (Scene != ZNetScene.instance)
            {
                Scene = ZNetScene.instance;
                ForgeName = null;
                foreach (var prefab in Scene.m_prefabs)
                {
                    CraftingStation station = prefab != null ? prefab.GetComponent<CraftingStation>() : null;
                    if (station != null && station.m_upgrader) { ForgeName = station.m_name; break; }
                }
            }
            return ForgeName != null && player.m_knownStations.ContainsKey(ForgeName);
        }
        internal static bool Enabled(Player player, CraftingStation station) =>
            player != null && station != null && station.m_upgrader &&
            PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70);
    }

    // Reuse the native repair button and native one-item-per-click repair path.
    // Do not unlock recipes, change station levels or introduce Repair All.
    [HarmonyPatch(typeof(InventoryGui), "UpdateRepair")]
    internal static class PotentialForgeRepairButtonPatch
    {
        private static void Prefix(out CraftingStation __state)
        {
            CraftingStation station = Player.m_localPlayer?.GetCurrentCraftingStation();
            __state = PotentialForgeRepair.Enabled(Player.m_localPlayer, station) && !station.m_canRepair ? station : null;
            if (__state != null) __state.m_canRepair = true;
        }
        private static void Postfix(CraftingStation __state) { if (__state != null) __state.m_canRepair = false; }
        private static System.Exception Finalizer(CraftingStation __state, System.Exception __exception)
        { Postfix(__state); return __exception; }
    }

    [HarmonyPatch(typeof(InventoryGui), "CanRepair")]
    internal static class PotentialForgeCanRepairPatch
    {
        private static void Postfix(ItemDrop.ItemData item, ref bool __result)
        {
            if (PotentialForgeRepair.Enabled(Player.m_localPlayer, Player.m_localPlayer?.GetCurrentCraftingStation()) &&
                item?.m_shared != null && item.m_shared.m_canBeReparied && item.m_shared.m_useDurability)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverText))]
    internal static class PotentialForgeSecretHoverPatch
    {
        private static void Postfix(CraftingStation __instance, ref string __result)
        {
            if (__instance == null || !__instance.m_upgrader || !__instance.InUseDistance(Player.m_localPlayer)) return;
            __result += PotentialForgeRepair.Secret;
        }
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
    internal static class PotentialForgeProtectedButtonPatch
    {
        private static void Postfix(InventoryGui __instance, Player player)
        {
            var item = __instance.m_selectedRecipe.ItemData;
            if (player?.GetCurrentCraftingStation()?.m_upgrader != true || item?.m_shared == null ||
                !PotentialForgeSafetyPatch.ProtectsTarget(item.m_quality, item.m_shared.m_maxQuality)) return;
            var label = __instance.m_craftButton?.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null) label.text = Localization.instance.Localize("$inventory_upgradebutton");
        }
    }
}
