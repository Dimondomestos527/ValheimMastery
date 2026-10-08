using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // No destruction/support patch: observe accepted manual removal and player
    // weapon destruction only. Natural collapse, unloading and ruins are untouched.
    internal static class CraftingBuildReuseService
    {
        private const string RpcName = "VM_Gold_BuildReuseV1";
        private sealed class Removal { internal Player Player; internal string Kind; internal ZDOID Piece; internal Vector3 Position; }
        [ThreadStatic] private static Removal Removing;
        private static readonly Dictionary<long, CraftingBuildReuseModel> Server = new Dictionary<long, CraftingBuildReuseModel>();
        private static readonly Dictionary<string, double> RemovedIds = new Dictionary<string, double>(StringComparer.Ordinal);
        private static ZNet Session;
        private static Player Owner;
        private static CraftingBuildReuseModel Local = new CraftingBuildReuseModel();
        private static string ProfileKey;
        private static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;

        internal static void Register(ZRpc rpc) => rpc.Register<ZPackage>(RpcName, Receive);
        internal static void Tick() => EnsureSession();
        private static void EnsureSession()
        {
            if (Session != ZNet.instance)
            { Session = ZNet.instance; Server.Clear(); RemovedIds.Clear(); Owner = null; Removing = null; }
            Player player = Player.m_localPlayer;
            if (player == Owner) return;
            Owner = player; ProfileKey = Session != null ? "VM_BuildReuse_" + Session.GetWorldUID() : null;
            Local = player != null && ProfileKey != null && player.m_customData.TryGetValue(ProfileKey, out string raw)
                ? CraftingBuildReuseModel.Decode(raw, Now) : new CraftingBuildReuseModel();
        }
        private static void StoreLocal()
        {
            if (Owner != null && ProfileKey != null) Owner.m_customData[ProfileKey] = Local.Encode(Now);
            // Custom data participates in the normal native profile autosave/logout
            // save. No synchronous disk flush on every fast hammer interaction.
        }
        internal static bool AllowPlacement(Player player, Piece piece)
        {
            if (player == null || player != Player.m_localPlayer || !MasteryPlugin.Settings.Enabled.Value) return true;
            EnsureSession();
            string kind = Kind(piece);
            if (!Local.ConsumeReplacement(kind, Now)) return true;
            StoreLocal();
            Send(1, kind, ZDOID.None, Vector3.zero);
            return false; // before XP/first-unique action context: therefore no Favor event either.
        }
        internal static bool ConsumeServerReplacement(long id, string kind) =>
            Server.TryGetValue(id, out CraftingBuildReuseModel model) && model.ConsumeReplacement(kind, Now);

        private static string Kind(Piece piece) => piece != null ? Utils.GetPrefabName(piece.gameObject.name) : null;
        private static void Capture(Piece piece)
        {
            if (Removing == null || Removing.Kind != null || piece == null || piece.GetComponent<Plant>() != null || !piece.IsPlacedByPlayer()) return;
            Removing.Kind = Kind(piece); Removing.Position = piece.transform.position;
            Removing.Piece = piece.m_nview?.GetZDO()?.m_uid ?? ZDOID.None;
        }
        private static void Record(Removal removal)
        {
            if (removal?.Player == null || removal.Player != Player.m_localPlayer || String.IsNullOrEmpty(removal.Kind)) return;
            EnsureSession();
            if (Local.Removed(removal.Kind, Now))
            { StoreLocal(); Send(0, removal.Kind, removal.Piece, removal.Position); }
        }
        private static void Send(int kind, string prefab, ZDOID piece, Vector3 position)
        {
            if (Session == null) return;
            var package = new ZPackage(); package.Write(kind); package.Write(prefab); package.Write(piece); package.Write(position);
            try
            {
                if (Session.IsServer()) { package.SetPos(0); Receive(null, package); }
                else Session.GetServerRPC()?.Invoke(RpcName, package);
            }
            catch (Exception error)
            {
                // Keep the local penalty even if a disconnect prevents its mirror;
                // a bookkeeping message must never break the native hammer action.
                if (MasteryPlugin.Settings.UIDebugLogging.Value) MasteryPlugin.Log.LogWarning("[BuildReuse] Mirror postponed: " + error.Message);
            }
        }
        private static void Receive(ZRpc rpc, ZPackage package)
        {
            if (!MasteryPlugin.Settings.Enabled.Value || ZNet.instance?.IsServer() != true || package == null || package.Size() > 512) return;
            EnsureSession();
            WorkshopActor actor = WorkshopActor.Resolve(rpc);
            if (actor == null || !actor.Available) return;
            try
            {
                int operation = package.ReadInt(); string prefab = package.ReadString(); ZDOID pieceId = package.ReadZDOID();
                Vector3 position = package.ReadVector3();
                if (operation < 0 || operation > 1 || String.IsNullOrWhiteSpace(prefab) || prefab.Length > 160) return;
                long id = actor.GetPlayerID();
                if (operation == 1) { ConsumeServerReplacement(id, prefab); return; }
                if (!GoldFavorModel.Finite(position.x) || !GoldFavorModel.Finite(position.y) || !GoldFavorModel.Finite(position.z) ||
                    Vector3.Distance(actor.Position, position) > Math.Max(15f, actor.PlaceDistance + 2f) ||
                    !WorkshopWorldRecords.WardAllows(id, position)) return;
                GameObject template = ZNetScene.instance?.GetPrefab(prefab);
                if (template?.GetComponent<Piece>() == null || template.GetComponent<Plant>() != null) return;
                // A removal report can only add a penalty to its authenticated
                // sender, never mint Favor or penalize another player. Native removal
                // may delete the ZDO before this ordered RPC arrives, so an absent
                // ZDO is not treated as a missing-character failure.
                ZDO data = ZDOMan.instance?.GetZDO(pieceId);
                if (data != null && (data.GetPrefab() != template.name.GetStableHashCode() || data.GetLong(ZDOVars.s_creator, 0) == 0)) return;
                double now = Now;
                var expired = new List<string>();
                foreach (var pair in RemovedIds) if (pair.Value <= now) expired.Add(pair.Key);
                foreach (string key in expired) RemovedIds.Remove(key);
                string receipt = id + ":" + pieceId;
                if (pieceId != ZDOID.None && RemovedIds.ContainsKey(receipt)) return;
                if (RemovedIds.Count >= 4096 || (!Server.ContainsKey(id) && Server.Count >= 4096)) return;
                if (!Server.TryGetValue(id, out CraftingBuildReuseModel model)) Server.Add(id, model = new CraftingBuildReuseModel());
                if (model.Removed(prefab, now) && pieceId != ZDOID.None) RemovedIds[receipt] = now + CraftingBuildReuseModel.WindowSeconds;
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[BuildReuse] Invalid penalty report: " + error.Message); }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RemovePiece))]
        private static class RemovePiecePatch
        {
            private static void Prefix(Player __instance)
            {
                if (__instance == Player.m_localPlayer && MasteryPlugin.Settings.Enabled.Value)
                { EnsureSession(); Removing = new Removal { Player = __instance }; }
            }
            private static void Postfix(bool __result)
            { Removal removal = Removing; Removing = null; if (__result) Record(removal); }
            private static Exception Finalizer(Exception __exception) { Removing = null; return __exception; }
        }
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
        private static class CaptureManualRemovalPatch
        { private static void Prefix(WearNTear __instance) => Capture(__instance?.m_piece); }
        [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
        private static class CaptureDestructionPatch
        {
            private static void Prefix(Piece __instance, HitData hitData)
            {
                if (!MasteryPlugin.Settings.Enabled.Value || __instance == null) return;
                if (Removing != null) { Capture(__instance); return; }
                Player attacker = hitData?.GetAttacker() as Player;
                if (attacker == null || attacker != Player.m_localPlayer || __instance.GetComponent<Plant>() != null ||
                    !__instance.IsPlacedByPlayer() || __instance.m_nview?.IsOwner() != true) return;
                Record(new Removal { Player = attacker, Kind = Kind(__instance), Position = __instance.transform.position,
                    Piece = __instance.m_nview.GetZDO()?.m_uid ?? ZDOID.None });
            }
        }
    }
}
