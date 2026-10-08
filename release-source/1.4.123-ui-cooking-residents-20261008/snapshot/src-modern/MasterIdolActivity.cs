using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolActivity
    {
        private static readonly List<Vector3> Players=new List<Vector3>(32);internal static long NearIdle,FarIdle,Wakes,PathQueries;private static float Next;private static ZNet Session;
        internal static bool Near(Vector3 point,float radius=64)
        {
            Refresh();float squared=radius*radius;foreach(var player in Players)if((player-point).sqrMagnitude<squared)return true;return false;
        }
        internal static bool Near(MasterIdolEffectZone zone){Refresh();foreach(var player in Players)if(zone.Contains(player,32))return true;return false;}
        internal static string Describe()=>"peaceful nearIdle="+NearIdle+" farIdle="+FarIdle+" wakes="+Wakes+" pathQueries="+PathQueries+" playerCache="+Players.Count+" cacheCadence=0.5s";
        private static void Refresh()
        {
            if(Session!=ZNet.instance){Session=ZNet.instance;Players.Clear();Next=0;}
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;Players.Clear();
            foreach(var player in Player.GetAllPlayers())if(player!=null&&!player.IsDead()&&!player.InGhostMode()&&!player.IsDebugFlying())Players.Add(player.transform.position);
            if(Session?.IsServer()==true)foreach(var peer in Session.GetPeers())if(peer.IsReady()&&peer.m_playerID!=0&&peer.m_characterID!=ZDOID.None){var character=ZDOMan.instance?.GetZDO(peer.m_characterID);if(character!=null&&!character.GetBool(ZDOVars.s_debugFly,false)&&character.GetLong(ZDOVars.s_playerID,0)==peer.m_playerID)Players.Add(character.GetPosition());}
        }
    }
}


