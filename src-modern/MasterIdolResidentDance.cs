using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal static partial class MasterIdolResidentDance
    {
        private static ZNet Net;private static ZDOMan Manager;private static ZNetScene Scene;private static ulong Generation;private static long World;
        private static string ServerSession,RemoteSession;private static double NextQuery,NextRender;
        private static readonly HashSet<string> RetiredSessions=new HashSet<string>(StringComparer.Ordinal),Cancelled=new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<ZDOID,ResidentDanceOwnerLease> Owners=new Dictionary<ZDOID,ResidentDanceOwnerLease>();
        private static readonly HashSet<ZDOID> ReadySent=new HashSet<ZDOID>();private static readonly List<ResidentDanceOwnerLease> StaleOwners=new List<ResidentDanceOwnerLease>(2);
        private static ResidentDanceTicket RenderTicket,PendingRender;
        private static ResidentDancePresentation RenderA,RenderB;
        private static string Key(ResidentDanceTicket ticket)=>ticket.Session+"/"+ticket.Nonce;
        private static void RememberCancel(ResidentDanceTicket ticket)
        {if(Cancelled.Count>=64)Cancelled.Clear();Cancelled.Add(Key(ticket));}
        private static bool Context()
        {
            var net=ZNet.instance;var manager=ZDOMan.instance;var scene=ZNetScene.instance;
            if(net?.GetWorld()==null||manager==null||scene==null||!MasterIdolLoadIdentity.LoadReady||!GoldCraftingService.Enabled)
            {Reset();return false;}
            long world=net.GetWorldUID();ulong generation=MasterIdolLoadIdentity.Generation;
            if(Net!=net||Manager!=manager||Scene!=scene||World!=world||Generation!=generation)
            {Reset();Net=net;Manager=manager;Scene=scene;World=world;Generation=generation;ServerSession=net.IsServer()?Guid.NewGuid().ToString("N"):null;}
            if(!net.IsServer()&&!NetworkSync.HasCurrentServerSettings(net.GetServerRPC())){Reset();return false;}
            return true;
        }
        private static bool ContextCurrent()
            =>Net!=null&&Net==ZNet.instance&&Manager==ZDOMan.instance&&Scene==ZNetScene.instance&&Generation==MasterIdolLoadIdentity.Generation&&Net.GetWorld()!=null&&Net.GetWorldUID()==World&&MasterIdolLoadIdentity.LoadReady&&GoldCraftingService.Enabled&&
                (Net.IsServer()||NetworkSync.HasCurrentServerSettings(Net.GetServerRPC()));
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>(ResidentDanceWire.ControlRpc,ReceiveControl);peer?.m_rpc?.Register<ZPackage>(ResidentDanceWire.ReplyRpc,ReceiveReply);}
        internal static void Reset()
        {
            try{StopRender();}
            finally
            {
                foreach(var lease in Owners.Values)try{lease.ResetLocal();}catch(Exception){}
                Owners.Clear();ReadySent.Clear();PendingRender=null;Cancelled.Clear();RetiredSessions.Clear();RemoteSession=ServerSession=null;
                ResetServer();Net=null;Manager=null;Scene=null;World=0;Generation=0;NextQuery=NextRender=0;
            }
        }
        private static void StopRender()
        {
            var a=RenderA;var b=RenderB;RenderA=RenderB=null;RenderTicket=null;
            try{a?.Dispose();}catch(Exception){}
            try{b?.Dispose();}catch(Exception){}
        }
        private static void StopLocal(ResidentDanceTicket ticket,bool completed)
        {
            StaleOwners.Clear();foreach(var lease in Owners.Values)if(ResidentDanceRules.Same(lease.Ticket,ticket))StaleOwners.Add(lease);
            foreach(var lease in StaleOwners){Owners.Remove(lease.Member.Uid);ReadySent.Remove(lease.Member.Uid);try{lease.Dispose();}catch(Exception){}}
            if(ResidentDanceRules.Same(PendingRender,ticket))PendingRender=null;
            if(!completed&&ResidentDanceRules.Same(RenderTicket,ticket))StopRender();
            RememberCancel(ticket);
        }
        internal static void Cancel(Character actor,string reason)
        {
            try
            {
                if(actor==null||!Context())return;ResidentDanceTicket ticket=null;ResidentDanceMember member=null;
                foreach(var lease in Owners.Values)if(lease.OwnsBody(actor)){ticket=lease.Ticket;member=lease.Member;break;}
                if(ticket==null&&actor.m_nview?.IsOwner()==true&&RenderTicket!=null&&(RenderA?.CanYield(actor)==true||RenderB?.CanYield(actor)==true))
                {ticket=RenderTicket;member=ticket.Member(actor.m_nview.GetZDO().m_uid);}
                if(ticket==null||member==null)return;
                StopLocal(ticket,false);SendAnswer(new ResidentDanceAnswer{Kind=ResidentDanceReply.Cancel,Reason=ResidentDanceReason.Busy,World=ticket.World,Session=ticket.Session,Nonce=ticket.Nonce,Actor=member.Uid,Revision=member.Revision});
            }
            catch(Exception){/* Accepted gameplay must not fail because optional cosmetic cleanup failed. */}
        }
        internal static bool CanYield(Character actor)
        {
            try
            {
                if(actor==null||!ContextCurrent())return false;
                foreach(var lease in Owners.Values)if(lease.OwnsBody(actor)&&lease.IsCurrent())return RenderA?.CanYield(actor)==true||RenderB?.CanYield(actor)==true;
            }
            catch(Exception){}
            return false;
        }
        internal static bool Idle(Character actor,float dt)
        {
            try
            {
                if(actor==null||!Context())return false;
                foreach(var lease in Owners.Values)
                {
                    if(!lease.OwnsBody(actor))continue;
                    var step=lease.Step(dt);
                    if(step==ResidentDanceOwnerStep.Interrupted){Cancel(actor,"context");return false;}
                    if(step==ResidentDanceOwnerStep.Completed)
                    {
                        var ticket=lease.Ticket;var member=lease.Member;StopLocal(ticket,true);
                        SendAnswer(new ResidentDanceAnswer{Kind=ResidentDanceReply.Cancel,Reason=ResidentDanceReason.Completed,World=ticket.World,Session=ticket.Session,Nonce=ticket.Nonce,Actor=member.Uid,Revision=member.Revision});return false;
                    }
                    if(step==ResidentDanceOwnerStep.Ready&&ReadySent.Add(lease.Member.Uid))SendAnswer(new ResidentDanceAnswer{Kind=ResidentDanceReply.Ready,World=lease.Ticket.World,Session=lease.Ticket.Session,Nonce=lease.Ticket.Nonce,Actor=lease.Member.Uid,Revision=lease.Member.Revision});
                    return true;
                }
            }
            catch(Exception){Cancel(actor,"idle-failed");}
            return false;
        }
        internal static void Tick()
        {
            try
            {
                if(!Context())return;
                StaleOwners.Clear();foreach(var lease in Owners.Values)if(!lease.IsCurrent())StaleOwners.Add(lease);
                // Snapshot at most two references: StopLocal may remove both siblings.
                var first=StaleOwners.Count>0?StaleOwners[0]:null;var second=StaleOwners.Count>1?StaleOwners[1]:null;
                if(first!=null)CancelLease(first,ResidentDanceReason.Context);if(second!=null&&Owners.ContainsKey(second.Member.Uid))CancelLease(second,ResidentDanceReason.Context);
                if(Net.IsServer())TickServer();
                else if(Time.unscaledTimeAsDouble>=NextQuery&&NetworkSync.HasCurrentServerSettings(Net.GetServerRPC()))
                {NextQuery=Time.unscaledTimeAsDouble+1;SendAnswer(new ResidentDanceAnswer{Kind=ResidentDanceReply.Query,World=World});}
            }
            catch(Exception){Reset();}
        }
        private static void CancelLease(ResidentDanceOwnerLease lease,ResidentDanceReason reason)
        {
            var ticket=lease.Ticket;var member=lease.Member;StopLocal(ticket,false);
            SendAnswer(new ResidentDanceAnswer{Kind=ResidentDanceReply.Cancel,Reason=reason,World=ticket.World,Session=ticket.Session,Nonce=ticket.Nonce,Actor=member.Uid,Revision=member.Revision});
        }
        internal static void LateTick()
        {
            try
            {
                if(!Context())return;
                if(RenderTicket!=null)
                {
                    float elapsed=(float)((Net.GetTime().Ticks-RenderTicket.Start)/(double)TimeSpan.TicksPerSecond);
                    bool a=RenderA.Tick(elapsed),b=RenderB.Tick(elapsed);
                    if(!a&&!RenderA.Finished||!b&&!RenderB.Finished)
                    {var retry=RenderTicket;StopRender();if(!Cancelled.Contains(Key(retry))&&elapsed<ResidentDanceRules.DanceSeconds-ResidentDanceRules.BlendSeconds)PendingRender=retry;}
                    else if(RenderA.Finished&&RenderB.Finished){RenderA=RenderB=null;RenderTicket=null;}
                }
                var ticket=PendingRender;if(ticket==null||RenderTicket!=null||Time.unscaledTimeAsDouble<NextRender)return;
                float offset=(float)((Net.GetTime().Ticks-ticket.Start)/(double)TimeSpan.TicksPerSecond);
                if(offset<0)return;
                if(offset>=ResidentDanceRules.DanceSeconds-ResidentDanceRules.BlendSeconds||Cancelled.Contains(Key(ticket))){PendingRender=null;return;}
                NextRender=Time.unscaledTimeAsDouble+.25;
                var first=Scene.FindInstance(ticket.First.Uid)?.GetComponent<Character>();var second=Scene.FindInstance(ticket.Second.Uid)?.GetComponent<Character>();
                if(ResidentDancePresentation.TryPair(first,second,ticket,offset,out var aCue,out var bCue,out bool terminal))
                {RenderTicket=ticket;RenderA=aCue;RenderB=bCue;PendingRender=null;}
                else if(terminal){RememberCancel(ticket);PendingRender=null;}
            }
            catch(Exception){StopRender();PendingRender=null;}
        }
        private static bool Session(ResidentDanceTicket ticket)
        {
            if(Net.IsServer())return ticket.Session==ServerSession;
            if(RemoteSession==ticket.Session)return true;
            if(RetiredSessions.Contains(ticket.Session)||RetiredSessions.Count>=8)return false;
            if(RemoteSession!=null){RetiredSessions.Add(RemoteSession);StopRender();PendingRender=null;foreach(var lease in Owners.Values)try{lease.ResetLocal();}catch(Exception){}Owners.Clear();ReadySent.Clear();}
            RemoteSession=ticket.Session;return true;
        }
        private static void ReceiveControl(ZRpc rpc,ZPackage packet)
        {
            try{if(!Context()||Net.IsServer()||!NetworkSync.HasCurrentServerSettings(rpc))return;Control(packet);}
            catch(Exception){}
        }
        private static void Control(ZPackage packet)
        {
            if(!ResidentDanceWire.ReadControl(packet,out var kind,out var ticket,out var reason)||ticket.World!=World)return;
            long now=Net.GetTime().Ticks;
            bool fresh=kind==ResidentDanceControl.Prepare?ticket.Issued<=now+TimeSpan.TicksPerSecond&&now<=ticket.PrepareUntil:ticket.Start==0?now<=ticket.PrepareUntil:ResidentDanceRules.CurrentStart(ticket,now);
            if(!fresh||!Session(ticket))return;
            if(kind==ResidentDanceControl.Cancel){StopLocal(ticket,reason==ResidentDanceReason.Completed);return;}
            if(Cancelled.Contains(Key(ticket)))return;
            if(kind==ResidentDanceControl.Prepare)
            {
                PrepareMember(ticket,ticket.First);if(!Cancelled.Contains(Key(ticket)))PrepareMember(ticket,ticket.Second);return;
            }
            foreach(var lease in Owners.Values)if(ResidentDanceRules.Same(lease.Ticket,ticket)&&!lease.Schedule(ticket)){CancelLease(lease,ResidentDanceReason.Context);return;}
            if(ResidentDanceRules.Same(RenderTicket,ticket)){if(RenderTicket.Start!=ticket.Start)StopRender();else return;}
            PendingRender=ticket;NextRender=0;
        }
        private static void PrepareMember(ResidentDanceTicket ticket,ResidentDanceMember member)
        {
            if(member.Owner!=ZNet.GetUID())return;
            if(Owners.TryGetValue(member.Uid,out var previous)){if(ResidentDanceRules.Same(previous.Ticket,ticket))return;CancelLease(previous,ResidentDanceReason.Context);}
            if(Owners.Count>=2||!ResidentDanceOwnerLease.TryCreate(ticket,member,out var lease))
            {
                StopLocal(ticket,false);SendAnswer(new ResidentDanceAnswer{Kind=ResidentDanceReply.Decline,Reason=ResidentDanceReason.Route,World=ticket.World,Session=ticket.Session,Nonce=ticket.Nonce,Actor=member.Uid,Revision=member.Revision});return;
            }
            Owners.Add(member.Uid,lease);
        }
        private static void SendAnswer(ResidentDanceAnswer answer)
        {
            if(!Context())return;var packet=ResidentDanceWire.Reply(answer);
            if(Net.IsServer())Answer(null,packet);
            else if(NetworkSync.HasCurrentServerSettings(Net.GetServerRPC()))Net.GetServerRPC().Invoke(ResidentDanceWire.ReplyRpc,packet);
        }
        private static void ReceiveReply(ZRpc rpc,ZPackage packet)
        {
            try
            {
                if(!Context()||!Net.IsServer())return;var peer=Net.GetPeer(rpc);
                if(peer?.m_rpc!=rpc||!NetworkSync.HasPeerSettings(peer))return;Answer(peer,packet);
            }
            catch(Exception){}
        }
        private static bool SendControl(long owner,ResidentDanceControl kind,ResidentDanceTicket ticket,ResidentDanceReason reason=ResidentDanceReason.None)
        {
            var packet=ResidentDanceWire.Control(kind,ticket,reason);
            if(owner==ZNet.GetUID()){Control(new ZPackage(packet.GetArray()));return true;}
            foreach(var peer in Net.GetPeers())if(peer.m_uid==owner&&NetworkSync.HasPeerSettings(peer)){peer.m_rpc.Invoke(ResidentDanceWire.ControlRpc,packet);return true;}
            return false;
        }
        private static void Broadcast(ResidentDanceControl kind,ResidentDanceTicket ticket,ResidentDanceReason reason=ResidentDanceReason.None)
        {
            try{Control(ResidentDanceWire.Control(kind,ticket,reason));}catch(Exception){}
            foreach(var peer in Net.GetPeers())try{if(NetworkSync.HasPeerSettings(peer))peer.m_rpc.Invoke(ResidentDanceWire.ControlRpc,ResidentDanceWire.Control(kind,ticket,reason));}catch(Exception){}
        }
    }
}
