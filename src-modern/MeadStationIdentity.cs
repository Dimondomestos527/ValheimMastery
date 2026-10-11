using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ValheimMastery
{
    // Runtime ZDOID is a locator. This one native-saved value is the identity of
    // the physical station, shared by XP and the producer receipt lifecycle.
    internal static class MeadStationIdentity
    {
        internal static readonly int Key = "valheim_mastery.mead_xp.station_identity_v2".GetStableHashCode();
        private sealed class Registry { internal readonly Dictionary<string, ZDOID> Stations = new Dictionary<string, ZDOID>(StringComparer.Ordinal); }
        private static readonly ConditionalWeakTable<ZNet, Registry> Sessions = new ConditionalWeakTable<ZNet, Registry>();
        internal static bool ValidToken(string token) => token != null && token.Length == 32 &&
            Guid.TryParseExact(token, "N", out Guid id) && id != Guid.Empty && token == id.ToString("N");
        internal static string LedgerKey(long world, string token) => world.ToString(CultureInfo.InvariantCulture) + ".station." + token;
        internal static bool IsNativeStation(ZDO zdo) => zdo != null && zdo.m_uid != ZDOID.None &&
            zdo.GetPrefab() == "fermenter".GetStableHashCode();
        private static bool Unique(long world, string token, ZDOID locator)
        {
            ZNet net = ZNet.instance;
            if (net == null || net.GetWorldUID() != world) return false;
            Registry registry = Sessions.GetOrCreateValue(net); string identity = LedgerKey(world, token);
            if (registry.Stations.TryGetValue(identity, out ZDOID known)) return known == locator;
            registry.Stations.Add(identity, locator); return true;
        }
        internal static bool TryGet(ZDO zdo, long world, out string token)
        {
            token = null;
            if (world == 0 || !IsNativeStation(zdo)) return false;
            string raw = zdo.GetString(Key, "");
            if (raw.Length > 64) return false;
            string[] fields = raw.Split(new[] { ';' }, 4);
            if (fields.Length != 3 || fields[0] != "2" || fields[1] != world.ToString(CultureInfo.InvariantCulture) ||
                !ValidToken(fields[2]) || !Unique(world, fields[2], zdo.m_uid)) return false;
            token = fields[2]; return true;
        }
        internal static bool TryEnsure(ZDO zdo, long world, out string token)
        {
            if (TryGet(zdo, world, out token)) return true;
            if (world == 0 || !IsNativeStation(zdo) || zdo.GetOwner() != ZNet.GetUID() ||
                !string.IsNullOrEmpty(zdo.GetString(Key, ""))) return false;
            // Unshipped v1 rows lack a recoverable stable identity. Never silently
            // rotate them into a new player watermark or erase their earned rows.
            string pending = zdo.GetString(MeadXpOutbox.Key, "");
            if (!string.IsNullOrEmpty(pending) && pending != "1\n") return false;
            token = Guid.NewGuid().ToString("N");
            if (!Unique(world, token, zdo.m_uid)) { token = null; return false; }
            zdo.Set(Key, "2;" + world.ToString(CultureInfo.InvariantCulture) + ";" + token);
            return true;
        }
        internal static bool Matches(ZDO zdo, long world, string token) => TryGet(zdo, world, out string saved) && saved == token;
    }
}
