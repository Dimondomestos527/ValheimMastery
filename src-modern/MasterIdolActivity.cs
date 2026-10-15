using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolActivity
    {
        private static readonly List<Vector3> Players=new List<Vector3>(32);internal static long NearIdle,FarIdle,Wakes,PathQueries;private static float Next;private static ZNet Session;
        private static int NativeSeen,NativeEligible,NativeDead,NativeGhost,NativeFly,PeerSeen,PeerVerified,PeerRejected,PeerNoPlayerId,PeerFly,PeerIdValid,PeerLegacyZero,PeerLegacyDifferent;
        private static float RefreshedAt,SpawnObservedAt;private static string SpawnWitness;
        internal static bool Near(Vector3 point,float radius=64)
        {
            Refresh();float squared=radius*radius;foreach(var player in Players)if((player-point).sqrMagnitude<squared)return true;return false;
        }
        internal static bool Near(MasterIdolEffectZone zone){Refresh();foreach(var player in Players)if(zone.Contains(player,32))return true;return false;}
        // Same eligibility and strict 3D predicate as Near; inspect its existing cached positions once.
        internal static bool SpawnNear(Vector3 point,float radius)
        {
            Refresh();float nearest=float.PositiveInfinity,horizontal=float.PositiveInfinity,squared=radius*radius;bool near=false;
            foreach(var player in Players){var delta=player-point;float distance=delta.sqrMagnitude,xz=delta.x*delta.x+delta.z*delta.z;if(distance<squared)near=true;if(distance<nearest)nearest=distance;if(xz<horizontal)horizontal=xz;}
            string reason=near?"inside-active-ward":Players.Count>0?"outside-active-ward":PeerFly+NativeFly>0?"excluded-debug-fly":
                PeerNoPlayerId>0?"peer-character-player-id-missing":PeerRejected>0?"peer-character-binding-rejected":
                NativeDead>0?"native-dead":NativeGhost>0?"native-ghost":"no-player-records";
            SpawnObservedAt=Time.unscaledTime;
            SpawnWitness="state="+reason+" eligibleCache="+Players.Count+" nativeSeen="+NativeSeen+" nativeEligible="+NativeEligible+" nativeDead="+NativeDead+" nativeGhost="+NativeGhost+
                " peers="+PeerSeen+" verifiedPeer="+PeerVerified+" characterPlayerIDValid="+PeerIdValid+" debugFly="+(PeerFly+NativeFly)+
                " missingCharacterPlayerID="+PeerNoPlayerId+" characterBindingRejected="+PeerRejected+" legacyZeroEligible="+PeerLegacyZero+" legacyDifferentEligible="+PeerLegacyDifferent+
                " center=("+Number(point.x)+","+Number(point.y)+","+Number(point.z)+") radius="+Number(radius)+" nearest3DSq="+Number(nearest)+" nearestXZSq="+Number(horizontal)+" cacheAge="+Number(Time.unscaledTime-RefreshedAt);
            // Fixed state labels let empty->eligible and exclusion changes appear within the existing32-key cap.
            MasterIdolForestSpawnDiagnostics.Trace("near",reason,SpawnWitness);return near;
        }
        private static string Number(float value)=>float.IsPositiveInfinity(value)?"none":value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
        internal static string DescribeNear()=>SpawnWitness==null?"spawnNear=not-observed":"spawnNearAge="+Number(Time.unscaledTime-SpawnObservedAt)+" "+SpawnWitness;
        internal static string Describe()=>"peaceful nearIdle="+NearIdle+" farIdle="+FarIdle+" wakes="+Wakes+" pathQueries="+PathQueries+" playerCache="+Players.Count+" cacheCadence=0.5s";
        private static void Refresh()
        {
            if(Session!=ZNet.instance){Session=ZNet.instance;Players.Clear();Next=0;SpawnWitness=null;}
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;Players.Clear();
            NativeSeen=NativeEligible=NativeDead=NativeGhost=NativeFly=PeerSeen=PeerVerified=PeerRejected=PeerNoPlayerId=PeerFly=PeerIdValid=PeerLegacyZero=PeerLegacyDifferent=0;RefreshedAt=Time.unscaledTime;
            foreach(var player in Player.GetAllPlayers())
            {
                NativeSeen++;if(player==null)continue;if(player.IsDead()){NativeDead++;continue;}if(player.InGhostMode()){NativeGhost++;continue;}if(player.IsDebugFlying()){NativeFly++;continue;}
                Players.Add(player.transform.position);NativeEligible++;
            }
            if(Session?.IsServer()==true)foreach(var peer in Session.GetPeers())
            {
                PeerSeen++;var character=OwnerSkillAuthority.ResolveCharacterData(peer);if(character==null){PeerRejected++;continue;}PeerVerified++;
                if(character.GetBool(ZDOVars.s_debugFly,false)){PeerFly++;continue;}long playerId=character.GetLong(ZDOVars.s_playerID,0);if(playerId==0){PeerNoPlayerId++;continue;}
                PeerIdValid++;if(peer.m_playerID==0)PeerLegacyZero++;else if(peer.m_playerID!=playerId)PeerLegacyDifferent++;Players.Add(character.GetPosition());
            }
        }
    }
}


