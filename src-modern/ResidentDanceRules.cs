using System;
using UnityEngine;
namespace ValheimMastery
{
    internal enum ResidentDanceControl:byte { Prepare=1,Start=2,Cancel=3,Snapshot=4 }
    internal enum ResidentDanceReply:byte { Ready=1,Decline=2,Cancel=3,Query=4 }
    internal enum ResidentDanceReason:byte { None,Busy,Route,Timeout,Context,Presentation,Presence,Completed }
    internal sealed class ResidentDanceMember
    {
        internal ZDOID Uid;internal string BirthNonce;internal long Born,Owner;internal ushort Revision;internal Vector3 Goal;
    }
    internal sealed class ResidentDanceTicket
    {
        internal long World,Issued,PrepareUntil,Start;internal string Session,Nonce,Home;
        internal ResidentDanceMember First,Second;
        internal ResidentDanceMember Member(ZDOID uid)=>First?.Uid==uid?First:Second?.Uid==uid?Second:null;
        internal ResidentDanceMember Partner(ZDOID uid)=>First?.Uid==uid?Second:Second?.Uid==uid?First:null;
    }
    internal sealed class ResidentDanceAnswer
    {
        internal long World;internal string Session,Nonce;internal ZDOID Actor;internal ushort Revision;
        internal ResidentDanceReply Kind;internal ResidentDanceReason Reason;
    }
    internal static class ResidentDanceRules
    {
        internal const int MaximumPacket=768;
        internal const float PresenceSeconds=30,PrepareSeconds=10,DanceSeconds=30,StartLead=.35f,BlendSeconds=.35f;
        internal const float MinimumInterval=180,MaximumInterval=360,Separation=1.9f,ArrivalDistance=.45f;
        internal static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)&&Mathf.Abs(p.x)<=100000&&Mathf.Abs(p.y)<=100000&&Mathf.Abs(p.z)<=100000;
        internal static bool Nonce(string value)=>MasterIdolForestRules.Nonce(value);
        internal static bool Same(ResidentDanceTicket a,ResidentDanceTicket b)
            =>a!=null&&b!=null&&a.World==b.World&&a.Session==b.Session&&a.Nonce==b.Nonce&&a.Home==b.Home&&a.Issued==b.Issued&&a.PrepareUntil==b.PrepareUntil&&Members(a.First,b.First)&&Members(a.Second,b.Second);
        private static bool Members(ResidentDanceMember a,ResidentDanceMember b)
            =>a!=null&&b!=null&&a.Uid==b.Uid&&a.BirthNonce==b.BirthNonce&&a.Born==b.Born&&a.Owner==b.Owner&&a.Revision==b.Revision&&a.Goal.Equals(b.Goal);
        internal static bool CurrentStart(ResidentDanceTicket ticket,long now)
            =>ticket!=null&&ticket.Start>0&&now>=ticket.Start-TimeSpan.TicksPerSecond&&now-ticket.Start<=(long)((DanceSeconds+BlendSeconds+1)*TimeSpan.TicksPerSecond);
    }
}
