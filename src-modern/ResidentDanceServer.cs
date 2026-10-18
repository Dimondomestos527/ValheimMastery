using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal static partial class MasterIdolResidentDance
    {
        private sealed class ActivePair{internal ResidentDanceTicket Ticket;internal ZDO First,Second;internal bool ReadyFirst,ReadySecond;internal double Until;}
        private sealed class PlayerProof{internal ZDO Data;internal ZNetPeer Peer;internal long Id;}
        private static ActivePair Active;
        private static readonly ResidentDancePresence Presence=new ResidentDancePresence();private static ResidentDanceCadence Cadence=new ResidentDanceCadence();
        private static readonly Dictionary<ZDOID,PlayerProof> Players=new Dictionary<ZDOID,PlayerProof>();private static readonly HashSet<ZDOID> PlayerConflicts=new HashSet<ZDOID>();
        private static readonly Dictionary<ZRpc,double> Queries=new Dictionary<ZRpc,double>();private static readonly List<ZRpc> OldQueries=new List<ZRpc>(128);
        private static double NextServer,SnapshotAt;
        private static float Interval()=>UnityEngine.Random.Range(ResidentDanceRules.MinimumInterval,ResidentDanceRules.MaximumInterval);
        private static void ResetServer(){Active=null;Presence.Reset();Cadence=new ResidentDanceCadence();Players.Clear();PlayerConflicts.Clear();Queries.Clear();NextServer=SnapshotAt=0;}
        private static void AddPlayer(ZDO data,long id,ZNetPeer peer)
        {
            if(data==null||PlayerConflicts.Contains(data.m_uid))return;
            if(Players.TryGetValue(data.m_uid,out var existing))
            {if(!ReferenceEquals(existing.Data,data)||!ReferenceEquals(existing.Peer,peer)||existing.Id!=id){Players.Remove(data.m_uid);PlayerConflicts.Add(data.m_uid);}return;}
            if(Players.Count<128)Players.Add(data.m_uid,new PlayerProof{Data=data,Id=id,Peer=peer});
        }
        private static void SamplePlayers(MasterIdolEffectZone home,ZDO first,ZDO second)
        {
            Players.Clear();PlayerConflicts.Clear();
            if(ResidentDanceData.LocalPlayer(Player.m_localPlayer,home,out var local,out long localId))AddPlayer(local,localId,null);
            int count=0;foreach(var peer in Net.GetPeers())
            {if(count++>=128)break;if(ResidentDanceData.PeerPlayer(peer,home,out var data,out long id))AddPlayer(data,id,peer);}
            bool friendly=false;
            foreach(var player in Players.Values)if(ResidentDanceData.FriendlyToKnownPlayer(home,first,second,player.Data,player.Id,player.Peer)){friendly=true;break;}
            Presence.Begin(MasterIdolNativeTaming.Token(home.Id),Net.GetTimeSeconds());
            foreach(var player in Players.Values)Presence.Observe(player.Data,player.Id,player.Peer,friendly);Presence.End();SnapshotAt=Time.unscaledTimeAsDouble;
        }
        private static bool Qualified(MasterIdolEffectZone home,ZDO first,ZDO second)
        {
            if(Time.unscaledTimeAsDouble-SnapshotAt>1.25)return false;
            foreach(var player in Players.Values)
                if(Presence.Qualified(player.Data,player.Id,player.Peer)&&ResidentDanceData.FriendlyToKnownPlayer(home,first,second,player.Data,player.Id,player.Peer))return true;
            return false;
        }
        private static bool ActiveCurrent(out MasterIdolEffectZone home,out ZDO first,out ZDO second)
        {
            home=null;first=second=null;var pair=Active;
            return pair!=null&&ResidentDanceData.SameRoster(pair.Ticket,out home,out first,out second)&&ReferenceEquals(first,pair.First)&&ReferenceEquals(second,pair.Second)&&Qualified(home,first,second);
        }
        private static void TickServer()
        {
            if(Time.unscaledTimeAsDouble<NextServer)return;NextServer=Time.unscaledTimeAsDouble+.5;
            if(!ResidentDanceData.TryRoster(out var home,out var first,out var second,out var a,out var b))
            {Presence.Reset();Players.Clear();if(Active!=null)EndServer(ResidentDanceReason.Context);return;}
            Cadence.Home(MasterIdolNativeTaming.Token(home.Id));SamplePlayers(home,first,second);
            if(Active!=null)
            {
                if(!ActiveCurrent(out _,out _,out _)||Time.unscaledTimeAsDouble>Active.Until){EndServer(ResidentDanceReason.Context);return;}
                var ticket=Active.Ticket;long now=Net.GetTime().Ticks;
                if(ticket.Start==0)
                {if(now>ticket.PrepareUntil)EndServer(ResidentDanceReason.Timeout);else TryStart();}
                else if(now>=ticket.Start+(long)((ResidentDanceRules.DanceSeconds+ResidentDanceRules.BlendSeconds+.1f)*TimeSpan.TicksPerSecond))EndServer(ResidentDanceReason.Completed);
                return;
            }
            bool qualified=Qualified(home,first,second);
            if(Cadence.NeedsInterval&&qualified){Cadence.Arm(Time.unscaledTimeAsDouble,Interval());return;}
            if(!Cadence.Due(Time.unscaledTimeAsDouble,qualified))return;
            TryOffer(home,first,second,a,b);
        }
        private static bool OwnerAvailable(long owner)
        {
            if(owner==0)return false;if(owner==ZNet.GetUID())return true;
            foreach(var peer in Net.GetPeers())if(peer.m_uid==owner&&NetworkSync.HasPeerSettings(peer))return true;return false;
        }
        private static ResidentDanceMember Member(ZDO data,MasterIdolForestGuardRecord record,Vector3 goal)
            =>new ResidentDanceMember{Uid=data.m_uid,Born=record.Born,BirthNonce=record.Nonce,Owner=data.GetOwner(),Revision=data.OwnerRevision,Goal=goal};
        private static void TryOffer(MasterIdolEffectZone home,ZDO first,ZDO second,MasterIdolForestGuardRecord a,MasterIdolForestGuardRecord b)
        {
            var from=first.GetPosition();var to=second.GetPosition();var direction=to-from;direction.y=0;
            if(direction.sqrMagnitude<.01f)direction=Vector3.right;else direction=direction.normalized;
            var middle=(from+to)*.5f;var firstGoal=middle-direction*(ResidentDanceRules.Separation*.5f);var secondGoal=middle+direction*(ResidentDanceRules.Separation*.5f);
            firstGoal.y=from.y;secondGoal.y=to.y;
            if(!home.Contains(firstGoal)||!home.Contains(secondGoal))
            {firstGoal=from;secondGoal=from+direction*ResidentDanceRules.Separation;secondGoal.y=to.y;}
            if(!ResidentDanceRules.Finite(firstGoal)||!ResidentDanceRules.Finite(secondGoal)||!home.Contains(firstGoal)||!home.Contains(secondGoal)||Mathf.Abs(from.y-to.y)>1.5f||!OwnerAvailable(first.GetOwner())||!OwnerAvailable(second.GetOwner())||!Qualified(home,first,second))
            {Cadence.Arm(Time.unscaledTimeAsDouble,Interval());return;}
            long now=Net.GetTime().Ticks;
            if(now<=0||now>DateTime.MaxValue.Ticks-TimeSpan.TicksPerMinute){Cadence.Arm(Time.unscaledTimeAsDouble,Interval());return;}
            var ticket=new ResidentDanceTicket{World=World,Session=ServerSession,Nonce=Guid.NewGuid().ToString("N"),Home=home.Id,Issued=now,PrepareUntil=now+(long)(ResidentDanceRules.PrepareSeconds*TimeSpan.TicksPerSecond),First=Member(first,a,firstGoal),Second=Member(second,b,secondGoal)};
            if(!Cadence.Begin(ticket.Nonce))return;
            var pair=new ActivePair{Ticket=ticket,First=first,Second=second,Until=Time.unscaledTimeAsDouble+ResidentDanceRules.PrepareSeconds+1};Active=pair;
            try
            {
                if(!SendControl(ticket.First.Owner,ResidentDanceControl.Prepare,ticket)){EndServer(ResidentDanceReason.Context);return;}
                if(!ReferenceEquals(Active,pair))return;
                if(ticket.Second.Owner!=ticket.First.Owner&&!SendControl(ticket.Second.Owner,ResidentDanceControl.Prepare,ticket))EndServer(ResidentDanceReason.Context);
            }
            catch(Exception){if(ReferenceEquals(Active,pair))EndServer(ResidentDanceReason.Context);}
        }
        private static bool Near(ZDO data,Vector3 goal)
        {var difference=data.GetPosition()-goal;float height=Mathf.Abs(difference.y);difference.y=0;return difference.sqrMagnitude<=1&&height<=2.5f;}
        private static void TryStart()
        {
            var pair=Active;if(pair==null||pair.Ticket.Start!=0||!pair.ReadyFirst||!pair.ReadySecond)return;
            if(!ActiveCurrent(out _,out var first,out var second)){EndServer(ResidentDanceReason.Context);return;}
            long now=Net.GetTime().Ticks;if(now>pair.Ticket.PrepareUntil){EndServer(ResidentDanceReason.Timeout);return;}
            // Ready RPC may precede its native position replication; wait inside the original bounded deadline.
            if(!Near(first,pair.Ticket.First.Goal)||!Near(second,pair.Ticket.Second.Goal))return;
            pair.Ticket.Start=now+(long)(ResidentDanceRules.StartLead*TimeSpan.TicksPerSecond);
            pair.Until=Time.unscaledTimeAsDouble+ResidentDanceRules.StartLead+ResidentDanceRules.DanceSeconds+1;
            Broadcast(ResidentDanceControl.Start,pair.Ticket);
        }
        private static void EndServer(ResidentDanceReason reason)
        {
            var pair=Active;if(pair==null)return;Active=null;Cadence.Terminal(pair.Ticket.Nonce,Time.unscaledTimeAsDouble,Interval());Broadcast(ResidentDanceControl.Cancel,pair.Ticket,reason);
        }
        private static void Answer(ZNetPeer peer,ZPackage packet)
        {
            if(!ResidentDanceWire.ReadReply(packet,out var answer)||answer.World!=World)return;
            if(answer.Kind==ResidentDanceReply.Query)
            {
                if(peer==null)return;
                if(Queries.TryGetValue(peer.m_rpc,out double next)&&Time.unscaledTimeAsDouble<next)return;
                if(Queries.Count>=128&&!Queries.ContainsKey(peer.m_rpc))
                {OldQueries.Clear();foreach(var rpc in Queries.Keys)if(Net.GetPeer(rpc)==null)OldQueries.Add(rpc);foreach(var rpc in OldQueries)Queries.Remove(rpc);if(Queries.Count>=128)return;}
                Queries[peer.m_rpc]=Time.unscaledTimeAsDouble+1;
                if(Active?.Ticket.Start>0&&ActiveCurrent(out _,out _,out _))peer.m_rpc.Invoke(ResidentDanceWire.ControlRpc,ResidentDanceWire.Control(ResidentDanceControl.Snapshot,Active.Ticket));
                return;
            }
            var pair=Active;var ticket=pair?.Ticket;
            if(ticket==null||answer.Session!=ServerSession||answer.Nonce!=ticket.Nonce)return;
            var member=ticket.Member(answer.Actor);long sender=peer?.m_uid??ZNet.GetUID();
            if(member==null||member.Owner!=sender||member.Revision!=answer.Revision)return;
            var current=Manager.GetZDO(member.Uid);var pinned=member.Uid==ticket.First.Uid?pair.First:pair.Second;
            if(!ReferenceEquals(current,pinned)||current==null||current.GetOwner()!=sender||current.OwnerRevision!=member.Revision||!MasterIdolForestGuardState.ModuleBorn(current)||
                current.GetLong(MasterIdolForestGuardState.BornKey,0)!=member.Born||current.GetString(MasterIdolForestGuardState.NonceKey,"")!=member.BirthNonce)return;
            if(answer.Kind!=ResidentDanceReply.Ready)
            {
                bool completed=answer.Reason==ResidentDanceReason.Completed&&ticket.Start>0&&Net.GetTime().Ticks>=ticket.Start+(long)(ResidentDanceRules.DanceSeconds*TimeSpan.TicksPerSecond);
                EndServer(completed?ResidentDanceReason.Completed:ResidentDanceReason.Context);return;
            }
            if(ticket.Start!=0||Net.GetTime().Ticks>ticket.PrepareUntil)return;
            if(member.Uid==ticket.First.Uid)pair.ReadyFirst=true;else pair.ReadySecond=true;TryStart();
        }
    }
}
