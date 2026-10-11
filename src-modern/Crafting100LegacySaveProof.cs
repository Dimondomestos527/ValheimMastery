using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;

namespace ValheimMastery
{
    // Observers only. Never invoke native Load/Save or change profile/inventory/receipt data.
    internal static class Crafting100LegacySaveProof
    {
        internal sealed class ProfileStamp { internal byte[] Blob; internal string Hash; internal long Player; internal bool Used; }
        private sealed class LoadedProof
        {
            internal PlayerProfile Profile; internal ProfileStamp Stamp; internal ZNet Session;
            internal long World, Player; internal ZDOID Character; internal string Inventory;
            internal Dictionary<string, string> Receipts;
            internal long Epoch;
        }
        private static readonly ConditionalWeakTable<PlayerProfile, ProfileStamp> Profiles = new ConditionalWeakTable<PlayerProfile, ProfileStamp>();
        private static readonly ConditionalWeakTable<Player, LoadedProof> Loaded = new ConditionalWeakTable<Player, LoadedProof>();
        private static long NextEpoch;
        internal sealed class NativeLoadInput
        { internal Player Player; internal PlayerProfile Profile; internal ProfileStamp Stamp; internal ZPackage Package; internal string Hash;
          internal ZNet Session; internal long World, Id; internal ZDOID Character; }
        internal static NativeLoadInput BeforePlayerLoad(Player player, ZPackage package)
        {
            if (player != null) Loaded.Remove(player); // Failed/skipped loads cannot retain an earlier proof.
            PlayerProfile profile = Game.instance?.GetPlayerProfile();
            ZNet session = ZNet.instance;
            if (player == null || player != Player.m_localPlayer || session?.GetWorld() == null || package == null || package.GetPos() != 0 || profile == null ||
                !Profiles.TryGetValue(profile, out var stamp) || stamp.Used || !CurrentBlob(profile, stamp)) return null;
            string inputHash = Hash(package.GetArray());
            return inputHash == stamp.Hash ? new NativeLoadInput { Player = player, Profile = profile, Stamp = stamp, Package = package, Hash = inputHash,
                Session = session, World = session.GetWorldUID(), Id = player.GetPlayerID(), Character = player.m_nview?.GetZDO()?.m_uid ?? ZDOID.None } : null;
        }
        private static bool CurrentBlob(PlayerProfile profile, ProfileStamp stamp) => profile != null && stamp != null &&
            ReferenceEquals(profile.m_playerData, stamp.Blob) && Hash(profile.m_playerData) == stamp.Hash;
        internal static byte[] BeforeProfileLoad(PlayerProfile profile)
        { if (profile == null) return null; Profiles.Remove(profile); return profile.m_playerData; }
        internal static void AfterProfileLoad(PlayerProfile profile, bool result, byte[] previous)
        {
            if (profile == null) return; Profiles.Remove(profile);
            byte[] blob = profile.m_playerData;
            // Native parser can catch an error and still return true. Never certify a stale blob.
            if (!result || blob == null || blob.Length == 0 || ReferenceEquals(blob, previous) || profile.GetPlayerID() == 0) return;
            string hash = Hash(blob); if (hash == null) return;
            Profiles.Add(profile, new ProfileStamp { Blob = blob, Hash = hash, Player = profile.GetPlayerID() });
        }
        internal static void AfterPlayerLoad(Player player, ZPackage package, NativeLoadInput input, bool ranOriginal)
        {
            if (player == null) return; Loaded.Remove(player);
            ZNet session = ZNet.instance; PlayerProfile profile = Game.instance?.GetPlayerProfile();
            ZDO data = player.m_nview?.GetZDO();
            if (!ranOriginal || input == null || input.Player != player || input.Profile != profile || !ReferenceEquals(input.Package, package) ||
                package == null || Hash(package.GetArray()) != input.Hash ||
                session?.GetWorld() == null || input.Session != session || input.World != session.GetWorldUID() || input.Id != player.GetPlayerID() ||
                data == null || input.Character != data.m_uid || player != Player.m_localPlayer || profile == null || data.m_uid == ZDOID.None ||
                player.GetPlayerID() == 0 || player.GetPlayerID() != profile.GetPlayerID() || player.m_isLoading ||
                !Profiles.TryGetValue(profile, out var stamp) || stamp.Used || stamp.Player != player.GetPlayerID() ||
                !ReferenceEquals(stamp, input.Stamp) || !CurrentBlob(profile, stamp)) return;
            string inventory = InventoryHash(player); if (inventory == null) return;
            var receipts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in player.m_customData)
                if (pair.Key.StartsWith("VM_Gold_Receipt_", StringComparison.Ordinal) && pair.Key.Length <= 80 && pair.Value?.Length <= 1030) receipts.Add(pair.Key, pair.Value);
            stamp.Used = true; // Respawn/memory reload cannot manufacture a fresh persistent-load epoch.
            Loaded.Add(player, new LoadedProof { Profile = profile, Stamp = stamp, Session = session, World = session.GetWorldUID(),
                Player = player.GetPlayerID(), Character = data.m_uid, Inventory = inventory, Receipts = receipts, Epoch = ++NextEpoch });
        }
        internal static long RecordedEpoch(Player player) => player != null && Loaded.TryGetValue(player, out var proof) ? proof.Epoch : 0;
        internal static bool CurrentEpoch(Player player, out long epoch)
        {
            epoch = 0;
            if (player == null || !Loaded.TryGetValue(player, out var proof) || player != Player.m_localPlayer ||
                ZNet.instance?.GetWorld() == null || proof.Session != ZNet.instance || proof.World != ZNet.instance.GetWorldUID() ||
                proof.Player != player.GetPlayerID() || player.m_nview?.GetZDO()?.m_uid != proof.Character ||
                Game.instance?.GetPlayerProfile() != proof.Profile || !Profiles.TryGetValue(proof.Profile, out var stamp) || !ReferenceEquals(stamp, proof.Stamp) || !CurrentBlob(proof.Profile, stamp) || InventoryHash(player) != proof.Inventory) return false;
            epoch = proof.Epoch; return true;
        }
        internal static bool Matches(Player player, GoldAction action, int phase)
        {
            ZNet session = ZNet.instance;
            if (player == null || action == null || phase < 1 || phase > 3 || player != Player.m_localPlayer || session?.GetWorld() == null ||
                !Loaded.TryGetValue(player, out var proof) || !ReferenceEquals(proof.Session, session) || proof.World != session.GetWorldUID() ||
                proof.Player != player.GetPlayerID() || player.m_nview?.GetZDO()?.m_uid != proof.Character ||
                !ReferenceEquals(Game.instance?.GetPlayerProfile(), proof.Profile) || !Profiles.TryGetValue(proof.Profile, out var stamp) || !ReferenceEquals(stamp, proof.Stamp) || !CurrentBlob(proof.Profile, stamp)) return false;
            string key = "VM_Gold_Receipt_" + proof.World, raw = phase + ":" + action.Encode();
            return proof.Receipts.TryGetValue(key, out string loaded) && loaded == raw &&
                player.m_customData.TryGetValue(key, out string current) && current == raw && InventoryHash(player) == proof.Inventory;
        }
        private static string InventoryHash(Player player)
        {
            try
            {
                var items = player.GetInventory()?.GetAllItems(); if (items == null || items.Count > 10000) return null;
                var rows = new List<string>(items.Count);
                foreach (var item in items)
                {
                    if (item?.m_shared == null || item.m_customData == null) return null;
                    using (var stream = new MemoryStream())
                    using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true)))
                    {
                        writer.Write(TierDatabase.ItemKey(item) ?? ""); writer.Write(item.m_shared.m_name ?? "");
                        writer.Write(item.m_quality); writer.Write(item.m_variant); writer.Write(item.m_stack);
                        writer.Write(item.m_crafterID); writer.Write(item.m_crafterName ?? ""); writer.Write(item.m_durability); writer.Write(item.m_worldLevel);
                        writer.Write(item.m_gridPos.x); writer.Write(item.m_gridPos.y);
                        var keys = new List<string>(item.m_customData.Keys); keys.Sort(StringComparer.Ordinal); writer.Write(keys.Count);
                        foreach (string key in keys) { writer.Write(key); writer.Write(item.m_customData[key] ?? ""); }
                        writer.Flush(); rows.Add(Convert.ToBase64String(stream.ToArray()));
                    }
                }
                rows.Sort(StringComparer.Ordinal); return Hash(Encoding.UTF8.GetBytes(string.Join("\n", rows)));
            }
            catch { return null; }
        }
        private static string Hash(byte[] blob)
        {
            if (blob == null || blob.Length > 8 * 1024 * 1024) return null;
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(blob));
        }
    }
    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Load))]
    internal static class Crafting100PersistentProfileLoadProofPatch
    {
        private static void Prefix(PlayerProfile __instance, out byte[] __state) => __state = Crafting100LegacySaveProof.BeforeProfileLoad(__instance);
        private static void Postfix(PlayerProfile __instance, bool __result, byte[] __state) => Crafting100LegacySaveProof.AfterProfileLoad(__instance, __result, __state);
    }
    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    internal static class Crafting100LoadedInventoryProofPatch
    {
        private static void Prefix(Player __instance, ZPackage __0, out Crafting100LegacySaveProof.NativeLoadInput __state)
            => __state = Crafting100LegacySaveProof.BeforePlayerLoad(__instance, __0);
        private static void Postfix(Player __instance, ZPackage __0, Crafting100LegacySaveProof.NativeLoadInput __state, bool __runOriginal)
            => Crafting100LegacySaveProof.AfterPlayerLoad(__instance, __0, __state, __runOriginal);
    }
}
