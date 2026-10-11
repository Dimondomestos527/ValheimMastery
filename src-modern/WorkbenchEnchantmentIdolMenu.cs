using HarmonyLib;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(MasterIdolPieces),nameof(MasterIdolPieces.RefreshRecipes))]
    internal static class WorkbenchEnchantmentIdolMenu
    {
        static void Postfix()
        {
            var scene=ZNetScene.instance;var table=ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            if(scene==null)return;
            foreach(var profile in MasterIdolProfiles.All)
            {var prefab=scene.GetPrefab(profile.Prefab);if(prefab==null)continue;var piece=prefab.GetComponent<Piece>();if(piece!=null)piece.m_enabled=false;table?.m_pieces.Remove(prefab);}
        }
    }
    [HarmonyPatch(typeof(Player),nameof(Player.TryPlacePiece))]
    internal static class WorkbenchEnchantmentNoFreshIdolPatch
    {
        [HarmonyPriority(Priority.First+200)]
        static bool Prefix(Piece piece,ref bool __result)
        {if(MasterIdolPieces.Profile(piece)==null)return true;__result=false;return false;}
    }
    [HarmonyPatch(typeof(MasterIdolPlacement),nameof(MasterIdolPlacement.ServerHold))]
    internal static class WorkbenchEnchantmentNoFreshIdolServerPatch
    {
        [HarmonyPriority(Priority.First+200)]
        static bool Prefix(Piece piece,ref bool __result)
        {if(MasterIdolPieces.Profile(piece)==null)return true;__result=false;return false;}
    }
}
