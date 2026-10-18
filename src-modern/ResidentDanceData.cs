using System;
using UnityEngine;
namespace ValheimMastery
{
    // Narrow server proofs for THIS optional resident-to-player interaction. Marker false alone is never friendship.
    internal static class ResidentDanceData
    {
        private static bool Ready()
            =>ZNet.instance?.IsServer()==true&&ZNet.instance.GetWorld()!=null&&ZDOMan.instance!=null&&ZNetScene.instance!=null&&MasterIdolLoadIdentity.LoadReady&&GoldCraftingService.Enabled&&
                WorkbenchEnchantmentRuntime.Ready&&MasterIdolEffectZones.Ready&&MasterIdolForestGuardState.Ready&&double.IsFinite(ZNet.instance.GetTimeSeconds())&&ZNet.instance.GetTimeSeconds()>0;
        private static bool Live(ZDO data)
        {
            if(data==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data)||!ResidentDanceRules.Finite(data.GetPosition()))return false;
            float health=data.GetFloat(ZDOVars.s_health,1);return float.IsFinite(health)&&health>0;
        }
        private static bool Resident(MasterIdolForestGuardRecord record,ZDO data,int family)
        {
            var prefab=ZNetScene.instance?.GetPrefab(MasterIdolForestRules.Families[family]);var native=prefab?.GetComponent<Character>();
            return record!=null&&record.Valid()&&record.Death==0&&record.Family==MasterIdolForestRules.Families[family]&&data?.GetOwner()!=0&&Live(data)&&
                prefab!=null&&native!=null&&!(native is Player)&&prefab.name==record.Family&&data.GetPrefab()==record.Family.GetStableHashCode()&&
                MasterIdolForestGuardState.ModuleBorn(data)&&MasterIdolForestGuardState.Matches(record,data);
        }
        internal static bool TryRoster(out MasterIdolEffectZone home,out ZDO first,out ZDO second,out MasterIdolForestGuardRecord firstRecord,out MasterIdolForestGuardRecord secondRecord)
        {
            home=null;first=second=null;firstRecord=secondRecord=null;if(!Ready())return false;
            var slots=MasterIdolForestGuardState.Slots;
            if(slots==null||!slots.TryGetValue(MasterIdolForestRules.Families[0],out firstRecord)||!slots.TryGetValue(MasterIdolForestRules.Families[1],out secondRecord)||firstRecord==null||secondRecord==null)return false;
            first=MasterIdolForestGuardState.Data(firstRecord.Uid);second=MasterIdolForestGuardState.Data(secondRecord.Uid);
            if(!Resident(firstRecord,first,0)||!Resident(secondRecord,second,1))return false;
            home=MasterIdolEffectZones.ResidentHome(firstRecord.Uid,firstRecord.Family);var other=MasterIdolEffectZones.ResidentHome(secondRecord.Uid,secondRecord.Family);
            if(home?.Type!="BlackForest"||other?.Id!=home.Id||!MasterIdolForestRules.Home(home.Id)||!home.Contains(first.GetPosition())||!home.Contains(second.GetPosition())||
                !MasterIdolNativeTaming.SameHome(firstRecord.Home,home.Id)||!MasterIdolNativeTaming.SameHome(secondRecord.Home,home.Id)||
                !MasterIdolNativeTaming.SameHome(first.GetString(MasterIdolNativeTaming.HomeKey,""),home.Id)||!MasterIdolNativeTaming.SameHome(second.GetString(MasterIdolNativeTaming.HomeKey,""),home.Id))return false;
            return true; // Known roster alone is not a proven resident-to-player friendship.
        }
        internal static bool SameRoster(ResidentDanceTicket ticket,out MasterIdolEffectZone home,out ZDO first,out ZDO second)
        {
            home=null;first=second=null;
            return ticket!=null&&ticket.World==ZNet.instance?.GetWorldUID()&&TryRoster(out home,out first,out second,out var a,out var b)&&home.Id==ticket.Home&&
                Member(ticket.First,first,a)&&Member(ticket.Second,second,b);
        }
        private static bool Member(ResidentDanceMember member,ZDO data,MasterIdolForestGuardRecord record)
            =>member!=null&&data!=null&&data.m_uid==member.Uid&&data.GetOwner()==member.Owner&&data.OwnerRevision==member.Revision&&record.Born==member.Born&&record.Nonce==member.BirthNonce;
        private static bool PlayerData(ZDO data,MasterIdolEffectZone home,out long playerId)
        {
            playerId=0;if(!Ready()||home?.Type!="BlackForest"||!Live(data)||data.GetOwner()==0)return false;
            var prefab=ZNetScene.instance.GetPrefab(data.GetPrefab());if(prefab?.GetComponent<Player>()==null||data.GetBool(ZDOVars.s_debugFly,false)||!home.Contains(data.GetPosition()))return false;
            playerId=data.GetLong(ZDOVars.s_playerID,0);return playerId!=0;
        }
        internal static bool PeerPlayer(ZNetPeer peer,MasterIdolEffectZone home,out ZDO data,out long playerId)
        {
            data=null;playerId=0;var net=ZNet.instance;
            if(peer?.m_rpc==null||peer.m_uid==0||net?.GetPeer(peer.m_rpc)!=peer||!NetworkSync.HasPeerSettings(peer))return false;
            data=OwnerSkillAuthority.ResolveCharacterData(peer);
            return data!=null&&data.m_uid==peer.m_characterID&&data.GetOwner()==peer.m_uid&&PlayerData(data,home,out playerId);
        }
        internal static bool LocalPlayer(Player player,MasterIdolEffectZone home,out ZDO data,out long playerId)
        {
            data=null;playerId=0;
            if(player==null||player!=Player.m_localPlayer||player.m_nview?.IsValid()!=true||!player.m_nview.IsOwner()||player.IsDead()||player.InGhostMode()||player.IsDebugFlying())return false;
            data=player.m_nview.GetZDO();return data?.GetOwner()==ZNet.GetUID()&&PlayerData(data,home,out playerId)&&player.GetPlayerID()==playerId;
        }
        internal static bool FriendlyToKnownPlayer(MasterIdolEffectZone home,ZDO first,ZDO second,ZDO player,long playerId,ZNetPeer peer)
        {
            if(!TryRoster(out var currentHome,out var a,out var b,out _,out _)||currentHome.Id!=home?.Id||!ReferenceEquals(a,first)||!ReferenceEquals(b,second))return false;
            ZDO current;long currentId;
            bool known=peer!=null?PeerPlayer(peer,currentHome,out current,out currentId):LocalPlayer(Player.m_localPlayer,currentHome,out current,out currentId);
            if(!known||!ReferenceEquals(current,player)||playerId!=currentId)return false;
            // Current roster/native prefabs AND authenticated live player/home facts precede negating the marker.
            return !Ashlands70Madness.SourceMarkedCurrentData(first)&&!Ashlands70Madness.SourceMarkedCurrentData(second);
        }
    }
}
