using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ValheimMastery
{
    // Metadata travels inside the native saved pin, not in an independently saved sidecar.
    // This is presentation provenance, not authentication against edited map files.
    internal static class Run70MapMarkerState
    {
        private const string Start = "<size=0>vm.run70.v1:";
        private const string End = "</size>";
        internal static bool Known(string category)
        {
            switch (category)
            {
                case "burial_chambers": case "troll_cave": case "sunken_crypt":
                case "frost_cave": case "infested_mine": case "ashlands_fortress":
                case "the_hole": case "morkborg": case "bear_cave": return true;
                default: return false;
            }
        }
        internal static long World => ZNet.instance?.GetWorld() == null ? 0 : ZNet.instance.GetWorldUID();
        internal static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static string Encode(long world, string category, string label)
        {
            if (world == 0 || !Known(category) || string.IsNullOrWhiteSpace(label) || label.Length > 160 ||
                label.IndexOf('<') >= 0 || label.IndexOf('\n') >= 0 || label.IndexOf('\r') >= 0) return null;
            string encoded = label + Start + world.ToString(CultureInfo.InvariantCulture) + ":" + category + ":" +
                Guid.NewGuid().ToString("N") + End;
            return Encoding.UTF8.GetByteCount(encoded) <= 512 ? encoded : null;
        }
        internal static bool TryRead(Minimap.PinData pin, long world, out string category, out string label)
        {
            category = label = null;
            if (world == 0 || pin == null || pin.m_type != Minimap.PinType.Icon4 || !pin.m_save || pin.m_ownerID != 0 ||
                !Finite(pin.m_pos) || pin.m_name == null || pin.m_name.Length > 320 ||
                !pin.m_name.EndsWith(End, StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(pin.m_name) > 512) return false;
            int offset = pin.m_name.LastIndexOf(Start, StringComparison.Ordinal);
            if (offset <= 0 || offset > 160 || pin.m_name.IndexOf(Start, StringComparison.Ordinal) != offset) return false;
            string payload = pin.m_name.Substring(offset + Start.Length,
                pin.m_name.Length - offset - Start.Length - End.Length);
            string[] parts = payload.Split(':');
            if (parts.Length != 3 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long savedWorld) ||
                savedWorld != world || !Known(parts[1]) || !Guid.TryParseExact(parts[2], "N", out _)) return false;
            category = parts[1]; label = pin.m_name.Substring(0, offset); return true;
        }
    }
}
