using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal enum BossId { Eikthyr, Elder, Bonemass, Moder, Yagluth, Queen, Fader }

    internal sealed class BossProgressSnapshot
    {
        private readonly Dictionary<BossId, bool> _defeated;
        internal readonly float Multiplier;

        internal BossProgressSnapshot(Dictionary<BossId, bool> defeated, float multiplier)
        {
            _defeated = defeated;
            Multiplier = multiplier;
        }

        internal bool IsDefeated(BossId boss) => _defeated.TryGetValue(boss, out bool value) && value;
    }

    internal static class WorldProgressionXpService
    {
        private static readonly Dictionary<BossId, string> BossKeys = new Dictionary<BossId, string>
        {
            { BossId.Eikthyr, "defeated_eikthyr" },
            { BossId.Elder, "defeated_gdking" },
            { BossId.Bonemass, "defeated_bonemass" },
            { BossId.Moder, "defeated_dragon" },
            { BossId.Yagluth, "defeated_goblinking" },
            { BossId.Queen, "defeated_queen" },
            { BossId.Fader, "defeated_fader" }
        };

        private static BossProgressSnapshot _snapshot = EmptySnapshot();
        private static bool _prefabsInspected;
        private static long _loggedWorldUid = long.MinValue;

        internal static float GetBossXpMultiplier()
        {
            return MasteryRuntime.WorldBossXpScalingEnabled ? _snapshot.Multiplier : 1f;
        }

        internal static bool IsBossDefeated(BossId boss) => _snapshot.IsDefeated(boss);
        internal static BossProgressSnapshot GetCurrentProgress() => _snapshot;

        internal static void InspectBossPrefabs(ZNetScene scene)
        {
            if (scene == null || _prefabsInspected)
                return;
            _prefabsInspected = true;
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;
                Character character = prefab.GetComponent<Character>();
                if (character == null || !character.m_boss || string.IsNullOrWhiteSpace(character.m_defeatSetGlobalKey))
                    continue;
                if (TryIdentifyBoss(prefab.name, character.m_defeatSetGlobalKey, out BossId boss))
                {
                    BossKeys[boss] = character.m_defeatSetGlobalKey;
                    if (MasteryPlugin.Settings.VerboseLogging.Value)
                        MasteryPlugin.Log.LogInfo("World XP boss key: " + boss + " prefab=" + prefab.name + " key=" + character.m_defeatSetGlobalKey);
                }
            }
        }

        internal static void Refresh(bool logOnce = false)
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null)
                return;

            Dictionary<BossId, bool> defeated = new Dictionary<BossId, bool>();
            int early = 0;
            int late = 0;
            foreach (BossId boss in Enum.GetValues(typeof(BossId)))
            {
                bool value = BossKeys.TryGetValue(boss, out string key) && zone.GetGlobalKey(key);
                defeated[boss] = value;
                if (!value)
                    continue;
                if (boss == BossId.Queen || boss == BossId.Fader)
                    late++;
                else
                    early++;
            }
            float multiplier = 1f + early * MasteryRuntime.EarlyBossXpBonus + late * MasteryRuntime.LateBossXpBonus;
            _snapshot = new BossProgressSnapshot(defeated, multiplier);

            long worldUid = 0L;
            ZNet net = ZNet.instance;
            if (net != null && net.GetWorld() != null)
                worldUid = net.GetWorldUID();
            if (logOnce && _loggedWorldUid != worldUid)
            {
                _loggedWorldUid = worldUid;
                MasteryPlugin.Log.LogInfo(FormatSnapshot(_snapshot));
            }
        }

        internal static bool IsRelevantKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;
            foreach (string bossKey in BossKeys.Values)
                if (string.Equals(key, bossKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool TryIdentifyBoss(string prefabName, string key, out BossId boss)
        {
            string value = ((prefabName ?? "") + "|" + (key ?? "")).ToLowerInvariant();
            if (value.Contains("eikthyr")) boss = BossId.Eikthyr;
            else if (value.Contains("gd_king") || value.Contains("gdking") || value.Contains("elder")) boss = BossId.Elder;
            else if (value.Contains("bonemass")) boss = BossId.Bonemass;
            else if (value.Contains("dragon") || value.Contains("moder")) boss = BossId.Moder;
            else if (value.Contains("goblinking") || value.Contains("yagluth")) boss = BossId.Yagluth;
            else if (value.Contains("queen") || value.Contains("seekerqueen")) boss = BossId.Queen;
            else if (value.Contains("fader")) boss = BossId.Fader;
            else { boss = default; return false; }
            return true;
        }

        private static BossProgressSnapshot EmptySnapshot()
        {
            Dictionary<BossId, bool> defeated = new Dictionary<BossId, bool>();
            foreach (BossId boss in Enum.GetValues(typeof(BossId)))
                defeated[boss] = false;
            return new BossProgressSnapshot(defeated, 1f);
        }

        private static string FormatSnapshot(BossProgressSnapshot snapshot)
        {
            return "World boss XP progression: Eikthyr=" + Yn(snapshot, BossId.Eikthyr) +
                ", Elder=" + Yn(snapshot, BossId.Elder) + ", Bonemass=" + Yn(snapshot, BossId.Bonemass) +
                ", Moder=" + Yn(snapshot, BossId.Moder) + ", Yagluth=" + Yn(snapshot, BossId.Yagluth) +
                ", Queen=" + Yn(snapshot, BossId.Queen) + ", Fader=" + Yn(snapshot, BossId.Fader) +
                " -> BossXPModifier=" + snapshot.Multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Yn(BossProgressSnapshot snapshot, BossId boss) => snapshot.IsDefeated(boss) ? "Y" : "N";
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class WorldXpPrefabDiscoveryPatch
    {
        private static void Postfix(ZNetScene __instance) => WorldProgressionXpService.InspectBossPrefabs(__instance);
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SetGlobalKey), new[] { typeof(string) })]
    internal static class WorldXpSetKeyPatch
    {
        private static void Postfix(string name)
        {
            if (WorldProgressionXpService.IsRelevantKey(name))
                WorldProgressionXpService.Refresh();
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.RemoveGlobalKey), new[] { typeof(string) })]
    internal static class WorldXpRemoveKeyPatch
    {
        private static void Postfix(string name)
        {
            if (WorldProgressionXpService.IsRelevantKey(name))
                WorldProgressionXpService.Refresh();
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "RPC_GlobalKeys")]
    internal static class WorldXpAllKeysPatch
    {
        private static void Postfix() => WorldProgressionXpService.Refresh(true);
    }

    [HarmonyPatch(typeof(ZoneSystem), "RPC_SetGlobalKey")]
    internal static class WorldXpRpcSetKeyPatch
    {
        private static void Postfix(string name)
        {
            if (WorldProgressionXpService.IsRelevantKey(name))
                WorldProgressionXpService.Refresh();
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "RPC_RemoveGlobalKey")]
    internal static class WorldXpRpcRemoveKeyPatch
    {
        private static void Postfix(string name)
        {
            if (WorldProgressionXpService.IsRelevantKey(name))
                WorldProgressionXpService.Refresh();
        }
    }
}
