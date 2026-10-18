using System;
using System.IO;
using System.Text;
using UnityEngine;
namespace ValheimMastery
{
    internal static class ResidentDanceWire
    {
        internal const string ControlRpc="VM_ResidentDanceControl_v1",ReplyRpc="VM_ResidentDanceReply_v1";
        private static void Uid(BinaryWriter writer,ZDOID uid){writer.Write(uid.UserID);writer.Write(uid.ID);}
        private static ZDOID Uid(BinaryReader reader)=>new ZDOID(reader.ReadInt64(),reader.ReadUInt32());
        private static string Text(BinaryReader reader,int cap)=>MasterIdolForestRules.ReadText(reader,cap);
        private static void WriteMember(BinaryWriter writer,ResidentDanceMember value)
        {Uid(writer,value.Uid);writer.Write(value.BirthNonce);writer.Write(value.Born);writer.Write(value.Owner);writer.Write((int)value.Revision);writer.Write(value.Goal.x);writer.Write(value.Goal.y);writer.Write(value.Goal.z);}
        private static ResidentDanceMember ReadMember(BinaryReader reader)
        {
            var value=new ResidentDanceMember{Uid=Uid(reader),BirthNonce=Text(reader,32),Born=reader.ReadInt64(),Owner=reader.ReadInt64()};int revision=reader.ReadInt32();
            value.Goal=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            if(value.Uid==ZDOID.None||!ResidentDanceRules.Nonce(value.BirthNonce)||value.Born<=0||value.Born>DateTime.MaxValue.Ticks||value.Owner==0||revision<0||revision>ushort.MaxValue||!ResidentDanceRules.Finite(value.Goal))throw new InvalidDataException("Dance member");
            value.Revision=(ushort)revision;return value;
        }
        internal static ZPackage Control(ResidentDanceControl kind,ResidentDanceTicket value,ResidentDanceReason reason=ResidentDanceReason.None)
        {
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
                {
                    writer.Write((byte)kind);writer.Write((byte)reason);writer.Write(value.World);writer.Write(value.Session);writer.Write(value.Nonce);writer.Write(value.Home);
                    writer.Write(value.Issued);writer.Write(value.PrepareUntil);writer.Write(value.Start);WriteMember(writer,value.First);WriteMember(writer,value.Second);
                }
                if(stream.Length>ResidentDanceRules.MaximumPacket)throw new InvalidDataException("Dance packet cap");return new ZPackage(stream.ToArray());
            }
        }
        internal static bool ReadControl(ZPackage packet,out ResidentDanceControl kind,out ResidentDanceTicket ticket,out ResidentDanceReason reason)
        {
            kind=default;ticket=null;reason=default;
            try
            {
                if(packet==null||packet.Size()>ResidentDanceRules.MaximumPacket)return false;
                using(var reader=new BinaryReader(new MemoryStream(packet.GetArray()),Encoding.UTF8))
                {
                    kind=(ResidentDanceControl)reader.ReadByte();reason=(ResidentDanceReason)reader.ReadByte();
                    if(kind<ResidentDanceControl.Prepare||kind>ResidentDanceControl.Snapshot||reason>ResidentDanceReason.Completed)return false;
                    var value=new ResidentDanceTicket{World=reader.ReadInt64(),Session=Text(reader,32),Nonce=Text(reader,32),Home=Text(reader,128),Issued=reader.ReadInt64(),PrepareUntil=reader.ReadInt64(),Start=reader.ReadInt64()};
                    value.First=ReadMember(reader);value.Second=ReadMember(reader);
                    if(reader.BaseStream.Position!=reader.BaseStream.Length||value.World==0||!ResidentDanceRules.Nonce(value.Session)||!ResidentDanceRules.Nonce(value.Nonce)||!MasterIdolForestRules.Home(value.Home)||
                        value.First.Uid==value.Second.Uid||value.First.Born>value.Issued||value.Second.Born>value.Issued||value.Issued<=0||value.Issued>DateTime.MaxValue.Ticks-TimeSpan.TicksPerMinute||value.PrepareUntil<=value.Issued||
                        value.PrepareUntil-value.Issued>(long)(ResidentDanceRules.PrepareSeconds*TimeSpan.TicksPerSecond)||value.Start<0||value.Start>value.PrepareUntil+TimeSpan.TicksPerSecond||value.Start!=0&&value.Start<value.Issued||
                        (kind==ResidentDanceControl.Start||kind==ResidentDanceControl.Snapshot)&&value.Start==0||kind==ResidentDanceControl.Prepare&&value.Start!=0||kind!=ResidentDanceControl.Cancel&&reason!=ResidentDanceReason.None)return false;
                    ticket=value;return true;
                }
            }
            catch(Exception){return false;}
        }
        internal static ZPackage Reply(ResidentDanceAnswer value)
        {
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
                {writer.Write((byte)value.Kind);writer.Write((byte)value.Reason);writer.Write(value.World);writer.Write(value.Session??"");writer.Write(value.Nonce??"");Uid(writer,value.Actor);writer.Write((int)value.Revision);}
                return new ZPackage(stream.ToArray());
            }
        }
        internal static bool ReadReply(ZPackage packet,out ResidentDanceAnswer answer)
        {
            answer=null;
            try
            {
                if(packet==null||packet.Size()>ResidentDanceRules.MaximumPacket)return false;
                using(var reader=new BinaryReader(new MemoryStream(packet.GetArray()),Encoding.UTF8))
                {
                    var value=new ResidentDanceAnswer{Kind=(ResidentDanceReply)reader.ReadByte(),Reason=(ResidentDanceReason)reader.ReadByte(),World=reader.ReadInt64(),Session=Text(reader,32),Nonce=Text(reader,32),Actor=Uid(reader)};int revision=reader.ReadInt32();
                    if(reader.BaseStream.Position!=reader.BaseStream.Length||value.World==0||value.Kind<ResidentDanceReply.Ready||value.Kind>ResidentDanceReply.Query||value.Reason>ResidentDanceReason.Completed||revision<0||revision>ushort.MaxValue)return false;
                    if(value.Kind==ResidentDanceReply.Query)
                    {if(value.Session!=""||value.Nonce!=""||value.Actor!=ZDOID.None||revision!=0||value.Reason!=ResidentDanceReason.None)return false;}
                    else if(!ResidentDanceRules.Nonce(value.Session)||!ResidentDanceRules.Nonce(value.Nonce)||value.Actor==ZDOID.None||value.Kind==ResidentDanceReply.Ready&&value.Reason!=ResidentDanceReason.None||value.Kind!=ResidentDanceReply.Ready&&value.Reason==ResidentDanceReason.None)return false;
                    value.Revision=(ushort)revision;answer=value;return true;
                }
            }
            catch(Exception){return false;}
        }
    }
}
