using System;
using System.Collections.Generic;
namespace ValheimMastery
{
    // Consumes one already-authorized player snapshot; never discovers players or assumes friendliness.
    internal sealed class ResidentDancePresence
    {
        private sealed class Entry{internal ZDO Data;internal ZNetPeer Peer;internal long Owner,Player;internal ushort Revision;internal double Since;internal int Sample;}
        private readonly Dictionary<ZDOID,Entry> Entries=new Dictionary<ZDOID,Entry>();
        private readonly HashSet<ZDOID> Conflicts=new HashSet<ZDOID>();private readonly List<ZDOID> Missing=new List<ZDOID>(128);
        private string Token;private int Sample;private double Now,Previous=double.NaN;
        internal void Reset(){Entries.Clear();Conflicts.Clear();Missing.Clear();Token=null;Sample=0;Previous=double.NaN;}
        internal bool Begin(string token,double now)
        {
            if(!ResidentDanceRules.Nonce(token)||!double.IsFinite(now)||now<0){Reset();return false;}
            if(Token!=token||!double.IsFinite(Previous)||now<Previous||now-Previous>1.25||Sample==int.MaxValue){Entries.Clear();Sample=0;}
            Token=token;Now=now;Previous=now;Sample++;Conflicts.Clear();return true;
        }
        internal void Observe(ZDO data,long playerId,ZNetPeer peer,bool authorizedFriendlyInsideAlive)
        {
            if(Sample==0||data==null||data.m_uid==ZDOID.None)return;
            var id=data.m_uid;if(!authorizedFriendlyInsideAlive||playerId==0){Entries.Remove(id);Conflicts.Add(id);return;}
            if(Conflicts.Contains(id))return;
            long owner=data.GetOwner();ushort revision=data.OwnerRevision;
            if(owner==0){Entries.Remove(id);Conflicts.Add(id);return;}
            if(!Entries.TryGetValue(id,out var entry))
            {
                if(Entries.Count>=128)return;
                Entries.Add(id,new Entry{Data=data,Peer=peer,Owner=owner,Revision=revision,Player=playerId,Since=Now,Sample=Sample});return;
            }
            bool same=ReferenceEquals(entry.Data,data)&&ReferenceEquals(entry.Peer,peer)&&entry.Owner==owner&&entry.Revision==revision&&entry.Player==playerId;
            if(entry.Sample==Sample&&!same){Entries.Remove(id);Conflicts.Add(id);return;}
            if(!same||entry.Sample!=Sample-1&&entry.Sample!=Sample)entry.Since=Now;
            entry.Data=data;entry.Peer=peer;entry.Owner=owner;entry.Revision=revision;entry.Player=playerId;entry.Sample=Sample;
        }
        internal void End()
        {
            Missing.Clear();foreach(var entry in Entries)if(entry.Value.Sample!=Sample)Missing.Add(entry.Key);
            foreach(var id in Missing)Entries.Remove(id);
        }
        internal bool Qualified(ZDO data,long playerId,ZNetPeer peer)
            =>data!=null&&Entries.TryGetValue(data.m_uid,out var entry)&&entry.Sample==Sample&&ReferenceEquals(entry.Data,data)&&ReferenceEquals(entry.Peer,peer)&&entry.Player==playerId&&entry.Owner==data.GetOwner()&&entry.Revision==data.OwnerRevision&&Now-entry.Since>=ResidentDanceRules.PresenceSeconds;
    }
    internal sealed class ResidentDanceCadence
    {
        private string Token,InFlight,LastTerminal;private double Next=double.NaN;
        internal bool NeedsInterval=>!double.IsFinite(Next)&&InFlight==null;
        internal void Home(string token){if(Token==token)return;Token=token;Next=double.NaN;InFlight=LastTerminal=null;}
        internal bool Arm(double now,float interval)
        {if(!ResidentDanceRules.Nonce(Token)||!double.IsFinite(now)||now<0||!float.IsFinite(interval)||interval<ResidentDanceRules.MinimumInterval||interval>ResidentDanceRules.MaximumInterval)return false;Next=now+interval;return true;}
        internal bool Due(double now,bool qualified)=>qualified&&InFlight==null&&double.IsFinite(now)&&double.IsFinite(Next)&&now>=Next;
        internal bool Begin(string nonce)
        {if(!double.IsFinite(Next)||InFlight!=null||!ResidentDanceRules.Nonce(nonce)||LastTerminal==nonce)return false;InFlight=nonce;return true;}
        internal bool Terminal(string nonce,double now,float interval)
        {if(nonce!=InFlight||nonce==LastTerminal||!Arm(now,interval))return false;InFlight=null;LastTerminal=nonce;return true;}
    }
}
