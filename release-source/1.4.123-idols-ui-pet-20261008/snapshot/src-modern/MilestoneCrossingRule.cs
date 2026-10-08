namespace ValheimMastery
{
    // Pure progression rule shared with the regression harness. Durable unlock
    // flags deliberately do not participate in repeat threshold presentation.
    internal static class MilestoneCrossingRule
    {
        internal static bool Crossed(float before, float after, int milestone) =>
            !float.IsNaN(before) && !float.IsInfinity(before) &&
            !float.IsNaN(after) && !float.IsInfinity(after) &&
            before < milestone && after >= milestone;
    }
}
