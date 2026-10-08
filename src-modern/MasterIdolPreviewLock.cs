using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolPreviewLock
    {
        private sealed class Snapshot
        {
            internal Player Player; internal Piece Piece; internal GameObject Ghost; internal ZNet Session;
            internal Vector3 Origin,Direction,Position;internal Quaternion Rotation;
            internal float PlaceRotation,Degrees;internal int Snap;
            internal bool Alt,JoyAlt,PlayerAlt;internal long World;
        }
        [ThreadStatic] internal static Player RayPlayer;
        private static Snapshot Last,Locked;
        private static Vector3 Origin,Direction;
        private static Player SeenPlayer;
        internal static void Reset() { Last=Locked=null; SeenPlayer=null; RayPlayer=null; }
        private static bool RawChange() => ZInput.GetMouseScrollWheel()!=0 || ZInput.GetButtonDown("TabLeft") || ZInput.GetButtonDown("TabRight") ||
            ZInput.GetButtonUp("JoyPrevSnap") || ZInput.GetButtonUp("JoyNextSnap") || ZInput.GetButton("JoyRotate") || ZInput.GetButton("JoyRotateRight");
        internal static bool Matches(Player player,Piece piece)
        {
            var s=Locked;
            return s!=null && player!=null && s.Player==player && s.Session==ZNet.instance && s.World==ZNet.instance?.GetWorldUID() &&
                s.Piece==piece && player.GetSelectedPiece()==piece && !player.IsDead() && !player.IsTeleporting() && s.Ghost==player.m_placementGhost && !Hud.IsPieceSelectionVisible() && !RawChange() &&
                s.PlaceRotation==player.m_placeRotation && s.Degrees==player.m_placeRotationDegrees && s.Snap==player.m_manualSnapPoint &&
                s.PlayerAlt==player.m_altPlace && s.Alt==ZInput.GetButton("AltPlace") && s.JoyAlt==ZInput.GetButton("JoyAltPlace");
        }
        internal static bool Lock(Player player,Piece piece)
        {
            var s=Last;
            if(s==null || s.Player!=player || s.Session!=ZNet.instance || s.World!=ZNet.instance?.GetWorldUID() || s.Piece!=piece ||
                s.Ghost!=player.m_placementGhost || Vector3.Distance(s.Position,s.Ghost.transform.position)>.001f || Quaternion.Angle(s.Rotation,s.Ghost.transform.rotation)>.01f)return false;
            Locked=s;return Matches(player,piece);
        }
        internal static bool Raycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float distance,int mask)
        {
            var player=RayPlayer;
            if(player==Player.m_localPlayer && player!=null && MasterIdolPieces.Profile(player.GetSelectedPiece())!=null)
            {
                if(MasterIdolPlacement.PreviewPending(player) && Locked!=null) { origin=Locked.Origin;direction=Locked.Direction; }
                else { Locked=null; Origin=origin;Direction=direction;SeenPlayer=player; }
            }
            return Physics.Raycast(origin,direction,out hit,distance,mask);
        }
        internal static void Capture(Player player)
        {
            if(player==null || player!=Player.m_localPlayer || player!=SeenPlayer || MasterIdolPlacement.PreviewPending(player) ||
                player.m_placementGhost==null || player.m_placementStatus!=Player.PlacementStatus.Valid || MasterIdolPieces.Profile(player.GetSelectedPiece())==null)return;
            Last=new Snapshot {Player=player,Piece=player.GetSelectedPiece(),Ghost=player.m_placementGhost,Session=ZNet.instance,World=ZNet.instance.GetWorldUID(),
                Origin=Origin,Direction=Direction,Position=player.m_placementGhost.transform.position,Rotation=player.m_placementGhost.transform.rotation,
                PlaceRotation=player.m_placeRotation,Degrees=player.m_placeRotationDegrees,Snap=player.m_manualSnapPoint,
                Alt=ZInput.GetButton("AltPlace"),JoyAlt=ZInput.GetButton("JoyAltPlace"),PlayerAlt=player.m_altPlace};
        }
    }
    [HarmonyPatch(typeof(Player),"PieceRayTest")]
    internal static class MasterIdolPreviewRayPatch
    {
        private static void Prefix(Player __instance,out Player __state) { __state=MasterIdolPreviewLock.RayPlayer;MasterIdolPreviewLock.RayPlayer=__instance; }
        private static Exception Finalizer(Player __state,Exception __exception) { MasterIdolPreviewLock.RayPlayer=__state;return __exception; }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            var target=typeof(Physics).GetMethod(nameof(Physics.Raycast),BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Vector3),typeof(Vector3),typeof(RaycastHit).MakeByRefType(),typeof(float),typeof(int)},null);
            var replacement=typeof(MasterIdolPreviewLock).GetMethod(nameof(MasterIdolPreviewLock.Raycast),BindingFlags.Static|BindingFlags.NonPublic);int count=0;
            foreach(var code in source) { if(code.Calls(target)){code.opcode=OpCodes.Call;code.operand=replacement;count++;}yield return code; }
            if(count!=1)throw new InvalidOperationException("Master Idol native aim ray contract changed.");
        }
    }
    [HarmonyPatch(typeof(Player),"UpdatePlacementGhost")]
    internal static class MasterIdolPreviewCapturePatch
    { private static void Postfix(Player __instance)=>MasterIdolPreviewLock.Capture(__instance); }
}
