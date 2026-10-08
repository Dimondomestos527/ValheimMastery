using System;

namespace ValheimMastery
{
    // Sparse, server-side diagnostics over the same model that calculates provisional Favor.
    internal static class GoldFavorTelemetry
    {
        private static GoldFavorModel Model = new GoldFavorModel();
        private static float Conversion = GoldFavorModel.DefaultFavorPerXp;
        private static float ChargeTimeMultiplier = GoldFavorModel.DefaultChargeTimeMultiplier;
        internal static bool Trace;
        internal static void Configure(float favorPerXp, float chargeTimeMultiplier = GoldFavorModel.DefaultChargeTimeMultiplier)
        {
            Conversion = GoldFavorModel.Finite(favorPerXp) && favorPerXp >= 0f && favorPerXp <= 10f
                ? favorPerXp : GoldFavorModel.DefaultFavorPerXp;
            ChargeTimeMultiplier = GoldFavorModel.Finite(chargeTimeMultiplier) && chargeTimeMultiplier >= 1f && chargeTimeMultiplier <= 100f
                ? chargeTimeMultiplier : GoldFavorModel.DefaultChargeTimeMultiplier;
            Reset();
        }
        internal static void Reset() { Model = new GoldFavorModel(Conversion, ChargeTimeMultiplier); Trace = false; }
        internal static GoldFavorModel.Result Observe(long player, string source, float validatedXp)
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            GoldFavorModel.Result result = Model.Observe(player, source, validatedXp, now);
            return result;
        }
        internal static void RecordAwarded(long player, float actualFavor) => Model.RecordAwarded(player, actualFavor);
        internal static void TraceAward(string source, long player, GoldFavorModel.Result result, float actualFavor)
        {
            if (!Trace || result == null || !result.Accepted) return;
            MasteryPlugin.Log.LogInfo("[GoldFavorTrace] source=" + source + " XP=" + result.RawXp.ToString("0.###") +
                " XP/min=" + result.RawXpPerMinute.ToString("0.##") + " diminishing=" + result.Diminishing.ToString("0.##") +
                " finalGain=" + actualFavor.ToString("0.##") + " player=" + player);
        }
        internal static string Describe(long player)
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            return Model.Describe(player, now);
        }
    }
}
