using System;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class GoldCharacterSave
    {
        [ThreadStatic] private static bool Saving;
        [ThreadStatic] private static bool SawWriter;
        [ThreadStatic] private static bool Failed;
        internal static bool Persist(Player player)
        {
            if (Saving || player == null || player != Player.m_localPlayer) return false;
            var profile = Game.instance?.GetPlayerProfile(); if (profile == null) return false;
            Saving = true; SawWriter = Failed = false;
            try
            {
                profile.SavePlayerData(player);
                // Native SavePlayerToDisk returns true even after a failed cloud writer!
                // Require the actual synchronous FileWriter.Finish result, not just that boolean.
                return profile.Save() && SawWriter && !Failed;
            }
            catch (Exception e) { MasteryPlugin.Log.LogError("[Gold100] Character save failed: " + e.Message); return false; }
            finally { Saving = false; }
        }
        internal static void Observe(FileWriter writer)
        {
            if (!Saving || writer == null) return;
            SawWriter = true;
            // Audited native WriterStatus: 2 = completed; 3 = failed, including cloud chunks.
            if ((int)writer.Status != 2) Failed = true;
        }
    }
    [HarmonyPatch(typeof(FileWriter), nameof(FileWriter.Finish))]
    internal static class GoldCharacterWriterPatch
    {
        private static void Postfix(FileWriter __instance) => GoldCharacterSave.Observe(__instance);
    }
}
