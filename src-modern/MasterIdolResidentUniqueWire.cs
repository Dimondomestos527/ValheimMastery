using System;
using System.IO;
using System.Text;
namespace ValheimMastery
{
    internal sealed class ResidentUniqueRequest
    {
        internal ResidentUniqueKind Kind;internal long World,Stamp;internal ZDOID Actor,Player;
        internal int ActorRevision,PlayerRevision,EmoteId;internal string Nonce;
    }
    internal sealed class ResidentUniqueTicket
    {
        internal ResidentUniqueKind Kind;internal byte Choice;internal long World,ActorOwner,Born,PlayerOwner,PlayerId,Stamp;
        internal int ActorRevision,PlayerRevision,EmoteId;internal ZDOID Actor,Player;
        internal string ActorNonce,Home,Nonce;
    }
    internal static class MasterIdolResidentUniqueWire
    {
        internal const string RequestRpc="VM_ResidentUnique_Request_v1",OwnerRpc="VM_ResidentUnique_Owner_v1",CueRpc="VM_ResidentUnique_Cue_v1";
        internal static byte[] Request(ResidentUniqueRequest value)=>Write(w=>
        {w.Write((byte)1);w.Write((byte)value.Kind);w.Write(value.World);Uid(w,value.Actor);w.Write(value.ActorRevision);Uid(w,value.Player);w.Write(value.PlayerRevision);w.Write(value.EmoteId);w.Write(value.Nonce);w.Write(value.Stamp);});
        internal static ResidentUniqueRequest Request(byte[] bytes)
        {
            using(var r=Reader(bytes,160))
            {
                Version(r);var value=new ResidentUniqueRequest{Kind=Kind(r),World=r.ReadInt64(),Actor=Uid(r),ActorRevision=Revision(r),Player=Uid(r),PlayerRevision=Revision(r),EmoteId=r.ReadInt32(),Nonce=Nonce(r),Stamp=r.ReadInt64()};
                if(value.World==0||value.Stamp<=0||value.Kind==ResidentUniqueKind.Family&&value.EmoteId!=0||value.Kind==ResidentUniqueKind.Gesture&&value.EmoteId<=0)throw new InvalidDataException("Unique request fields");
                End(r);return value;
            }
        }
        // Owner op0=server grant, op1=owner decision, op2=owner ended; Cue op0=begin, op1=denied, op2=cancel.
        internal static byte[] Ticket(byte op,ResidentUniqueTicket value,ResidentUniqueOutcome outcome=ResidentUniqueOutcome.Visual,byte reason=0,long cueStamp=0)=>Write(w=>
        {
            w.Write((byte)1);w.Write(op);w.Write((byte)value.Kind);w.Write(value.Choice);w.Write(value.World);Uid(w,value.Actor);w.Write(value.ActorOwner);w.Write(value.ActorRevision);w.Write(value.ActorNonce);w.Write(value.Born);w.Write(value.Home);
            Uid(w,value.Player);w.Write(value.PlayerOwner);w.Write(value.PlayerRevision);w.Write(value.PlayerId);w.Write(value.EmoteId);w.Write(value.Nonce);w.Write(value.Stamp);w.Write((byte)outcome);w.Write(reason);w.Write(cueStamp);
        });
        internal static ResidentUniqueTicket Ticket(byte[] bytes,out byte op,out ResidentUniqueOutcome outcome,out byte reason,out long cueStamp)
        {
            using(var r=Reader(bytes,MasterIdolResidentUniqueRules.MaximumPacket))
            {
                Version(r);op=r.ReadByte();if(op>2)throw new InvalidDataException("Unique operation");
                var value=new ResidentUniqueTicket{Kind=Kind(r),Choice=r.ReadByte(),World=r.ReadInt64(),Actor=Uid(r),ActorOwner=r.ReadInt64(),ActorRevision=Revision(r),ActorNonce=Nonce(r),Born=r.ReadInt64(),Home=MasterIdolForestRules.ReadText(r,128),
                    Player=Uid(r),PlayerOwner=r.ReadInt64(),PlayerRevision=Revision(r),PlayerId=r.ReadInt64(),EmoteId=r.ReadInt32(),Nonce=Nonce(r),Stamp=r.ReadInt64()};
                outcome=(ResidentUniqueOutcome)r.ReadByte();reason=r.ReadByte();cueStamp=r.ReadInt64();
                if(value.Choice>9||value.World==0||value.ActorOwner==0||value.PlayerOwner==0||value.PlayerId==0||value.Born<=0||value.Stamp<=0||!MasterIdolForestRules.Home(value.Home)||outcome>ResidentUniqueOutcome.HealRequested||reason>15||
                    value.Kind==ResidentUniqueKind.Family&&value.EmoteId!=0||value.Kind==ResidentUniqueKind.Gesture&&value.EmoteId<=0)throw new InvalidDataException("Unique ticket fields");
                End(r);return value;
            }
        }
        internal static bool Same(ResidentUniqueTicket a,ResidentUniqueTicket b)=>a!=null&&b!=null&&a.Kind==b.Kind&&a.Choice==b.Choice&&a.World==b.World&&a.Actor==b.Actor&&a.ActorOwner==b.ActorOwner&&a.ActorRevision==b.ActorRevision&&a.ActorNonce==b.ActorNonce&&a.Born==b.Born&&a.Home==b.Home&&
            a.Player==b.Player&&a.PlayerOwner==b.PlayerOwner&&a.PlayerRevision==b.PlayerRevision&&a.PlayerId==b.PlayerId&&a.EmoteId==b.EmoteId&&a.Nonce==b.Nonce&&a.Stamp==b.Stamp;
        private static BinaryReader Reader(byte[] bytes,int maximum){if(bytes==null||bytes.Length==0||bytes.Length>maximum)throw new InvalidDataException("Unique packet size");return new BinaryReader(new MemoryStream(bytes,false),Encoding.UTF8);}
        private static byte[] Write(Action<BinaryWriter> write)
        {using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))write(writer);if(stream.Length>MasterIdolResidentUniqueRules.MaximumPacket)throw new InvalidDataException("Unique packet size");return stream.ToArray();}}
        private static void Version(BinaryReader r){if(r.ReadByte()!=1)throw new InvalidDataException("Unique version");}
        private static ResidentUniqueKind Kind(BinaryReader r){byte value=r.ReadByte();if(value>1)throw new InvalidDataException("Unique kind");return (ResidentUniqueKind)value;}
        private static string Nonce(BinaryReader r){string value=MasterIdolForestRules.ReadText(r,32);if(!MasterIdolResidentUniqueRules.Nonce(value))throw new InvalidDataException("Unique nonce");return value;}
        private static int Revision(BinaryReader r){int value=r.ReadInt32();if(value<0||value>ushort.MaxValue)throw new InvalidDataException("Unique revision");return value;}
        private static void End(BinaryReader r){if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Unique trailing bytes");}
        private static void Uid(BinaryWriter w,ZDOID id){w.Write(id.UserID);w.Write(id.ID);}
        private static ZDOID Uid(BinaryReader r){var id=new ZDOID(r.ReadInt64(),r.ReadUInt32());if(id==ZDOID.None)throw new InvalidDataException("Unique UID");return id;}
    }
}
