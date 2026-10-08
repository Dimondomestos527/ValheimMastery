using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimMastery
{
    // Pure managed conversion/diminishing model. Input XP must already be validated by the server.
    internal sealed class GoldFavorModel
    {
        internal const float DefaultFavorPerXp = 0.5f; // Provisional until timed live calibration.
        internal const float DefaultChargeTimeMultiplier = 4f;
        internal const float MaxFavorPerEvent = 100f;
        private const int MaxPlayers = 4096;
        private const int MaxSourcesPerPlayer = 64;
        private const int MaxSamplesPerSource = 128;
        private const double RollingSeconds = 300d;
        private readonly float _favorPerXp;
        private readonly float _chargeTimeMultiplier;
        private readonly Dictionary<long, PlayerSamples> _players = new Dictionary<long, PlayerSamples>();

        internal GoldFavorModel(float favorPerXp = DefaultFavorPerXp, float chargeTimeMultiplier = DefaultChargeTimeMultiplier)
        {
            if (!Finite(favorPerXp) || favorPerXp < 0f || favorPerXp > 10f) throw new ArgumentOutOfRangeException("favorPerXp");
            if (!Finite(chargeTimeMultiplier) || chargeTimeMultiplier < 1f || chargeTimeMultiplier > 100f) throw new ArgumentOutOfRangeException("chargeTimeMultiplier");
            _favorPerXp = favorPerXp;
            _chargeTimeMultiplier = chargeTimeMultiplier;
        }

        private sealed class PlayerSamples
        {
            internal double Started;
            internal double RawXp;
            internal double DiminishedXp;
            internal double FavorAwarded;
            internal int Events;
            internal readonly Dictionary<string, SourceSamples> Recent = new Dictionary<string, SourceSamples>(StringComparer.Ordinal);
        }

        private sealed class SourceSamples
        {
            internal double LastSeen;
            internal readonly Queue<double> Events = new Queue<double>();
        }

        internal sealed class Result
        {
            internal bool Accepted;
            internal float RawXp;
            internal float Diminishing;
            internal float DiminishedXp;
            internal float FavorGain;
            internal double RawXpPerMinute;
            internal double DiminishedXpPerMinute;
            internal int Events;
        }

        internal Result Observe(long playerId, string source, float validatedXp, double nowUtc)
        {
            if (playerId == 0 || String.IsNullOrWhiteSpace(source) || source.Length > 160 ||
                !Finite(validatedXp) || validatedXp <= 0f || validatedXp > 100000f || !Finite(nowUtc) || nowUtc < 0d) return new Result();

            PlayerSamples player;
            if (!_players.TryGetValue(playerId, out player))
            {
                if (_players.Count >= MaxPlayers) return new Result();
                _players.Add(playerId, player = new PlayerSamples { Started = nowUtc });
            }

            SourceSamples recent;
            if (!player.Recent.TryGetValue(source, out recent))
            {
                if (player.Recent.Count >= MaxSourcesPerPlayer) EvictLeastRecentlyUsed(player, nowUtc);
                player.Recent.Add(source, recent = new SourceSamples());
            }
            recent.LastSeen = nowUtc;
            while (recent.Events.Count > 0 && (nowUtc < recent.Events.Peek() || nowUtc - recent.Events.Peek() > RollingSeconds)) recent.Events.Dequeue();
            float coefficient = recent.Events.Count < 3 ? 1f : recent.Events.Count < 10 ? .5f : .2f;
            if (recent.Events.Count < MaxSamplesPerSource) recent.Events.Enqueue(nowUtc);

            float diminished = validatedXp * coefficient;
            float gain = Math.Min(MaxFavorPerEvent, diminished * _favorPerXp) / _chargeTimeMultiplier;
            player.RawXp += validatedXp;
            player.DiminishedXp += diminished;
            player.Events++;
            double minutes = Math.Max(1d, nowUtc - player.Started) / 60d;
            return new Result
            {
                Accepted = true, RawXp = validatedXp, Diminishing = coefficient, DiminishedXp = diminished,
                FavorGain = gain, RawXpPerMinute = player.RawXp / minutes,
                DiminishedXpPerMinute = player.DiminishedXp / minutes, Events = player.Events
            };
        }

        private static void EvictLeastRecentlyUsed(PlayerSamples player, double nowUtc)
        {
            string candidate = null;
            double oldestIdle = Double.MaxValue;
            double oldestUse = Double.MaxValue;
            foreach (var pair in player.Recent)
            {
                double idle = nowUtc - pair.Value.LastSeen;
                if (idle > RollingSeconds && pair.Value.LastSeen < oldestIdle)
                { candidate = pair.Key; oldestIdle = pair.Value.LastSeen; }
            }
            if (candidate == null)
            {
                foreach (var pair in player.Recent)
                    if (pair.Value.LastSeen < oldestUse) { candidate = pair.Key; oldestUse = pair.Value.LastSeen; }
            }
            if (candidate != null) player.Recent.Remove(candidate);
        }

        internal string Describe(long playerId, double nowUtc)
        {
            PlayerSamples p;
            if (playerId == 0 || !_players.TryGetValue(playerId, out p) || !Finite(nowUtc)) return "No validated Crafting100 XP samples yet.";
            double minutes = Math.Max(1d, nowUtc - p.Started) / 60d;
            return "events=" + p.Events.ToString(CultureInfo.InvariantCulture) +
                " minutes=" + minutes.ToString("0.0", CultureInfo.InvariantCulture) +
                " rawXP/min=" + (p.RawXp / minutes).ToString("0.00", CultureInfo.InvariantCulture) +
                " diminishedXP/min=" + (p.DiminishedXp / minutes).ToString("0.00", CultureInfo.InvariantCulture) +
                " FavorAwarded=" + p.FavorAwarded.ToString("0.##", CultureInfo.InvariantCulture) +
                "; Favor conversion=" + _favorPerXp.ToString("0.0", CultureInfo.InvariantCulture) + "/XP provisional; charge time x" +
                _chargeTimeMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + "; timed live calibration required.";
        }

        internal void RecordAwarded(long playerId, float actualFavor)
        {
            PlayerSamples p;
            if (playerId != 0 && _players.TryGetValue(playerId, out p) && Finite(actualFavor) && actualFavor > 0f)
                p.FavorAwarded += actualFavor;
        }

        internal static bool Finite(float value) => !Single.IsNaN(value) && !Single.IsInfinity(value);
        internal static bool Finite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
    }
}
