namespace ValheimMastery
{
    internal static class CraftingProgressionXpPolicy
    {
        internal static float RecipeMultiplier => MasteryPlugin.Settings.Enabled.Value ? 1.25f : 1f;
        internal static float ProcessingUnitXp => MasteryPlugin.Settings.Enabled.Value ? .30f : .25f;

        internal static float BuildingMultiplier(Piece piece)
        {
            // A required nearby workbench does not make ordinary furniture a station.
            if (!MasteryPlugin.Settings.Enabled.Value || piece == null || piece.GetComponent<CraftingStation>() != null ||
                piece.GetComponent<StationExtension>() != null || piece.GetComponent<Smelter>() != null ||
                piece.GetComponent<CookingStation>() != null || piece.GetComponent<Fermenter>() != null ||
                piece.GetComponent<Incinerator>() != null || piece.GetComponent<Windmill>() != null ||
                piece.GetComponent<Ship>() != null || piece.GetComponent<Vagon>() != null) return 1f;
            return .5f;
        }
    }
}
