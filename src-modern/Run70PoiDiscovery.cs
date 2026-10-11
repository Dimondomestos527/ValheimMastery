using System;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Run70PoiDiscovery
    {
        private const float Radius = 120f;
        private static float _nextScan;

        internal static void Update(Player player)
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 2f;
            if (!MovementPerkService.Valid(player) || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Run, 70) ||
                !player.m_run || player.GetMoveDir().sqrMagnitude <= .01f || Minimap.instance == null || ZoneSystem.instance == null)
                return;

            float radiusSquared = Radius * Radius;
            foreach (LocationProxy proxy in NearbyLocationRegistry.Snapshot())
            {
                if (proxy == null || proxy.m_instance == null || proxy.m_nview == null) continue;
                Vector3 delta = proxy.transform.position - player.transform.position;
                if (delta.x * delta.x + delta.z * delta.z > radiusSquared) continue;
                ZDO zdo = proxy.m_nview.GetZDO();
                if (zdo == null) continue;
                ZoneSystem.ZoneLocation location = ZoneSystem.instance.GetLocation(zdo.GetInt(ZDOVars.s_location, 0));
                if (location == null) continue;
                string category, token;
                if (!TryCategory(location.m_name, location.m_prefabName, (int)location.m_biome, ((int)location.m_biome & 64) != 0 && location.m_prefab.IsValid ? location.m_prefab.Name : null, out category, out token)) continue;
                string label = PerkLocalization.Localize("$" + token);
                if (Run70MapMarkerPresentation.TryDiscoverRun70(Minimap.instance, proxy.transform.position, category, label))
                    break;
            }
        }

        // Deep North identity comes from the resolved native location, never a stale prefab alias.
        internal static bool TryCategory(string name, string prefabName, int biome, string nativePrefabName, out string category, out string token)
        {
            category = null;
            token = null;
            if (string.Equals(nativePrefabName, "TheHole01", StringComparison.Ordinal)) category = "the_hole";
            else if (string.Equals(nativePrefabName, "MorkBorg", StringComparison.Ordinal)) category = "morkborg";
            else if (string.Equals(nativePrefabName, "BearCave", StringComparison.Ordinal)) category = "bear_cave";
            if (category != null)
            {
                if ((biome & 64) == 0) { category = null; return false; }
                token = "vm_poi_" + category;
                return true;
            }

            token = LabelToken(name, prefabName);
            switch (token)
            {
                case "vm_poi_burial_chambers": category = "burial_chambers"; break;
                case "vm_poi_troll_cave": category = "troll_cave"; break;
                case "vm_poi_sunken_crypt": category = "sunken_crypt"; break;
                case "vm_poi_frost_cave": category = "frost_cave"; break;
                case "vm_poi_infested_mine": category = "infested_mine"; break;
                case "vm_poi_ashlands_fortress": category = "ashlands_fortress"; break;
                default: return false;
            }
            return true;
        }

        internal static string LabelToken(string name, string prefabName)
        {
            string id = ((name ?? string.Empty) + "|" + (prefabName ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(id, "hildir", "trader", "vendor", "boss", "howlingcavern", "sealedtower", "smoulderingtomb")) return null;
            if (ContainsAny(id, "sunkencrypt")) return "vm_poi_sunken_crypt";
            if (ContainsAny(id, "trollcave")) return "vm_poi_troll_cave";
            if (ContainsAny(id, "mountaincave")) return "vm_poi_frost_cave";
            if (ContainsAny(id, "dvergrtownentrance", "infestedmine")) return "vm_poi_infested_mine";
            if (ContainsAny(id, "charredfortress", "ashlandsfortress")) return "vm_poi_ashlands_fortress";
            if (ContainsAny(id, "burialchamber", "crypt2", "crypt3", "crypt4")) return "vm_poi_burial_chambers";
            return null;
        }

        private static bool ContainsAny(string value, params string[] fragments)
        {
            foreach (string fragment in fragments)
                if (value.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                    return true;
            return false;
        }
    }
}
