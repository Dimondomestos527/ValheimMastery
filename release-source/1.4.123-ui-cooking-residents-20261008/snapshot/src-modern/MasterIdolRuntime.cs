using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(MasteryPlugin), "OnDestroy")]
    internal static class MasterIdolShutdownPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            MasterIdolCleanup.Run("interaction.reset",MasterIdolInteraction.Reset,Report);
            MasterIdolCleanup.Run("placement.shutdown",MasterIdolPlacement.Shutdown,Report);
            MasterIdolCleanup.Run("pieces.suspend",MasterIdolPieces.Suspend,Report);
            MasterIdolCleanup.Run("artwork.shutdown",MasterIdolArtwork.Shutdown,Report);
            // Reporting cannot interrupt the cleanup sequence; retain both exceptions and use Unity after all stages.
            if(MasterIdolCleanup.ReportingFailed)Debug.LogError(MasterIdolCleanup.Describe());
        }
        private static void Report(string stage,Exception error)
        {
            string diagnostic="[MasterIdols] Cleanup failed stage="+stage+"\n"+error;
            if(MasteryPlugin.Log!=null)MasteryPlugin.Log.LogWarning(diagnostic);else Debug.LogError(diagnostic);
        }
    }
    [HarmonyPatch(typeof(MasteryPlugin), "Update")]
    internal static class MasterIdolTickPatch
    {
        private static void Postfix()
        {
            try { MasterIdolPlacement.Tick(); MasterIdolDismantle.Tick(); }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[MasterIdols] Update deferred: " + error.Message); }
        }
    }
    [HarmonyPatch(typeof(NetworkSync), nameof(NetworkSync.Register))]
    internal static class MasterIdolNetworkPatch
    { private static void Postfix(ZNetPeer peer) { MasterIdolPlacement.Register(peer); MasterIdolInteraction.Register(peer); MasterIdolDismantle.Register(peer); } }
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class MasterIdolTryPlacePatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        { if (MasterIdolPlacement.BeforeTry(__instance, piece)) return true; __result = false; return false; }
        private static void Postfix(Piece piece, bool __result) => MasterIdolPlacement.Tried(piece, __result);
    }
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class MasterIdolPlaceEntryPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static void Prefix(Player __instance, Piece piece, Vector3 pos, Quaternion rot) => MasterIdolPlacement.Enter(__instance, piece, pos, rot);
    }
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class MasterIdolNativePaidPatch
    {
        private static void Prefix(Player __instance, out string __state) => __state = MasterIdolPlacement.NativeAttempt(__instance);
        private static Exception Finalizer(Player __instance, string __state, Exception __exception)
        { MasterIdolPlacement.NativeCompleted(__state, __instance, __exception); return __exception; }
    }
    [HarmonyPatch(typeof(WorkshopRemoteCraft), "ExecuteBuild")]
    internal static class MasterIdolWorkshopPaidPatch
    {
        private static void Prefix(object state, Player player, out string __state) => __state = MasterIdolPlacement.WorkshopAttempt(state, player);
        private static void Postfix(object state, Player player, string __state) => MasterIdolPlacement.WorkshopCompleted(__state, state, player);
    }
    [HarmonyPatch(typeof(WorkshopBuildBridge), nameof(WorkshopBuildBridge.ServerEligible))]
    internal static class MasterIdolWorkshopAuthorityPatch
    {
        private static void Postfix(WorkshopActor player, Piece piece, Vector3 point, ref bool __result)
        { if (__result) __result = MasterIdolPlacement.ServerHold(player, piece, point); }
    }
    [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
    internal static class MasterIdolDestroyedPatch
    {
        private sealed class Observation {internal string Token,Type,Output;internal long Creator,World;internal ZNet Session;internal ZDOMan Manager;internal ZDO Object;internal ZDOID LiveUid;internal ulong Generation;}
        private static void Prefix(ZDOMan __instance, ZDOID uid, out Observation __state)
        {
            __state=null;if(ZNet.instance?.IsServer()!=true)return;
            var zdo=__instance.GetZDO(uid);var profile=MasterIdolWorldRegistry.Profile(zdo?.GetPrefab()??0);if(profile==null)return;
            string token=zdo.GetString(MasterIdolWorldRegistry.TokenKey,"");var entry=MasterIdolWorldRegistry.Store?.Find(token);
            if(!MasterIdolLoadIdentity.Matches(entry,zdo))return;
            __state=new Observation{Token=token,Type=profile.Id,Creator=zdo.GetLong(ZDOVars.s_creator,0),Output=entry.Output,Session=ZNet.instance,World=ZNet.instance.GetWorldUID(),Manager=__instance,Object=zdo,LiveUid=uid,Generation=MasterIdolLoadIdentity.Generation};
        }
        private static void Postfix(ZDOMan __instance, ZDOID uid, Observation __state)
        {
            // Native destruction pools/resets the ZDO (including m_uid); compare only the frozen locator after return.
            if (__state==null || !ReferenceEquals(__state.Session,ZNet.instance)||ZNet.instance?.IsServer()!=true||ZNet.instance.GetWorld()==null||__state.World!=ZNet.instance.GetWorldUID()||
                !ReferenceEquals(__state.Manager,__instance)||!ReferenceEquals(__instance,ZDOMan.instance)||__state.Generation!=MasterIdolLoadIdentity.Generation||__state.LiveUid!=uid||__instance.GetZDO(uid)!=null) return;
            MasterIdolWorldRegistry.Store?.Destroyed(__state.Output,__state.Token,__state.Type,__state.Creator);
            MasterIdolWorldRegistry.Removed(uid); MasterIdolDismantle.Destroyed(uid);
        }
    }
    [HarmonyPatch(typeof(PerkDebugService), nameof(PerkDebugService.RegisterCommands))]
    internal static class MasterIdolDebugPatch
    {
        private static void Postfix()
        {
            if (!MasteryPlugin.Settings.UIDebugLogging.Value) return;
            new Terminal.ConsoleCommand("vm_idols", "Показати серверний реєстр ідолів; лише читання.", args =>
            {
                if (ZNet.instance?.IsServer() != true) { args.Context.AddString("Діагностика реєстру доступна на сервері/хості."); return; }
                args.Context.AddString(MasterIdolWorldRegistry.Describe());
            }, true);
        }
    }
    [HarmonyPatch(typeof(MasterIdolVisual),"Update")]
    internal static class MasterIdolVisualTimingPatch
    {
        private static void Prefix(out long __state)=>__state=MasteryPlugin.Settings.UIDebugLogging.Value?MasterIdolTiming.Start():0;
        private static Exception Finalizer(long __state,Exception __exception){MasterIdolTiming.Finish("visual.cpu-update",__state);return __exception;}
    }
    [HarmonyPatch(typeof(Player),"UpdatePlacement")]
    internal static class MasterIdolFrameTimingPatch
    {
        private static void Prefix(Player __instance,out long __state)=>__state=MasteryPlugin.Settings.UIDebugLogging.Value && MasterIdolPieces.Profile(__instance.GetSelectedPiece())!=null?MasterIdolTiming.Start():0;
        private static Exception Finalizer(long __state,Exception __exception){MasterIdolTiming.Finish("placement.native-frame",__state);return __exception;}
    }

}
