using System;
namespace ValheimMastery
{
    // Numeric policy only: actual normals must come from independent support
    // rays, never from a zero-distance capsule sweep's synthetic normal.
    internal static class ShieldRushTerrainRules
    {
        internal const float MinimumNormalY = .65f;
        internal static bool CanFollow(float underfootY, float aheadY, float rise, float distance) =>
            float.IsFinite(underfootY) && float.IsFinite(aheadY) && float.IsFinite(rise) && float.IsFinite(distance) &&
            underfootY > MinimumNormalY && aheadY > MinimumNormalY && distance > 0f &&
            Math.Abs(rise) <= distance * 1.16913f + .08f;
        internal static bool CanReplaceSweepNormal(float distance, float normalY, bool supported) =>
            float.IsFinite(distance) && float.IsFinite(normalY) && distance >= 0f && supported &&
            (distance <= .001f || normalY > MinimumNormalY);
    }
}
