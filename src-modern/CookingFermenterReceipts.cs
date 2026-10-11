using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ValheimMastery
{
    // Producer transactions and output tombstones, not XP grants. The XP owner
    // supplies stable identity/admission/recovery through one bound stage contract.
    internal static class CookingFermenterReceipts
    {
        internal const string SourceKey="vm.cooking.fermenter.source_v1", BatchKey="vm.cooking.fermenter.batch_v1",
            PendingKey="vm.cooking.fermenter.pending_v1", ActorPrefix="vm.cooking.fermenter.actor_v1.",
            OutputIdKey="vm.cooking.output_id", PendingItemKey="vm.cooking.pending_output";
        internal const int MaxActors=128, MaxOutputs=32;
        internal enum Phase { None, Reserved, Committing, Accepted, Rejected, Uncertain }
        internal sealed class Input
        {
            internal long Actor, Sequence, Deadline, XpSerial;
            internal int Hash;
            internal bool Cheated;
            internal string Request, Source;
            internal Phase State;
            internal bool Same(Input other)=>other!=null && Actor==other.Actor && Sequence==other.Sequence && Request==other.Request &&
                Hash==other.Hash && Cheated==other.Cheated && Source==other.Source;
            internal bool Valid=>Actor!=0 && Sequence>0 && Guid.TryParseExact(Request,"N",out _) && Hash!=0 && !string.IsNullOrEmpty(Source);
        }
        internal sealed class Output
        {
            internal string Station, Id, Batch, Source, Prefab;
            internal long XpSerial, Actor, Due;
            internal int InputHash, Count;
            internal bool Cheated, Bonus, Accepted, Released;
            // 0 never issued, 1 ambiguous issue in progress, 2 durably completed.
            // A missing drop can never revoke a completed tombstone.
            internal byte[] Slots;
            internal string SlotId(int index)=>Station+"/"+Id+"/"+index.ToString(CultureInfo.InvariantCulture);
            internal bool Complete=>Slots!=null && Slots.Length==Count && Slots.All(state=>state==2);
        }
        internal static string Encode(Input row)=>Pack(w=>{
            w.Write(2);w.Write(row.Actor);w.Write(row.Sequence);w.Write(row.Deadline);w.Write(row.Hash);w.Write(row.Cheated);
            w.Write(row.Request);w.Write(row.Source);w.Write((int)row.State);w.Write(row.XpSerial);
        });
        internal static Input DecodeInput(string raw)
        {
            if(string.IsNullOrEmpty(raw))return null;
            return Unpack(raw,r=>{
                if(r.ReadInt32()!=2)throw new FormatException("Unknown input receipt schema.");
                var row=new Input{Actor=r.ReadInt64(),Sequence=r.ReadInt64(),Deadline=r.ReadInt64(),Hash=r.ReadInt32(),Cheated=r.ReadBoolean(),
                    Request=r.ReadString(),Source=r.ReadString(),State=(Phase)r.ReadInt32(),XpSerial=r.ReadInt64()};
                if(!row.Valid || row.State<Phase.Reserved || row.State>Phase.Uncertain)throw new FormatException("Invalid input receipt.");
                CookingProductRecord.Decode(row.Source);return row;
            });
        }
        internal static string Encode(Output row)=>Pack(w=>{
            w.Write(2);w.Write(row.Station);w.Write(row.Id);w.Write(row.Batch??"");w.Write(row.Source);w.Write(row.Prefab);
            w.Write(row.XpSerial);w.Write(row.Actor);w.Write(row.Due);w.Write(row.InputHash);w.Write(row.Count);w.Write(row.Cheated);w.Write(row.Bonus);w.Write(row.Accepted);w.Write(row.Released);
            w.Write(row.Slots.Length);w.Write(row.Slots);
        });
        internal static Output DecodeOutput(string raw)
        {
            if(string.IsNullOrEmpty(raw))return null;
            return Unpack(raw,r=>{
                if(r.ReadInt32()!=2)throw new FormatException("Unknown output receipt schema.");
                var row=new Output{Station=r.ReadString(),Id=r.ReadString(),Batch=r.ReadString(),Source=r.ReadString(),Prefab=r.ReadString(),
                    XpSerial=r.ReadInt64(),Actor=r.ReadInt64(),Due=r.ReadInt64(),InputHash=r.ReadInt32(),Count=r.ReadInt32(),Cheated=r.ReadBoolean(),Bonus=r.ReadBoolean(),Accepted=r.ReadBoolean(),Released=r.ReadBoolean()};
                int length=r.ReadInt32();if(length<1 || length>MaxOutputs || length!=row.Count)throw new FormatException("Invalid output count.");
                row.Slots=r.ReadBytes(length);
                if(row.Slots.Length!=length || row.Slots.Any(state=>state>2) || row.Station.Length>160 ||
                    !Guid.TryParseExact(row.Id,"N",out _) || row.InputHash==0 || row.Prefab.Length>128)throw new FormatException("Invalid output identity.");
                CookingProductRecord.Decode(row.Source);return row;
            });
        }
        private static string Pack(Action<BinaryWriter> write)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
            {write(writer);writer.Flush();if(stream.Length>16384)throw new InvalidOperationException("Receipt bound exceeded.");return Convert.ToBase64String(stream.ToArray());}
        }
        private static T Unpack<T>(string raw,Func<BinaryReader,T> read)
        {
            if(raw.Length>24576)throw new FormatException("Receipt bound exceeded.");
            using(var stream=new MemoryStream(Convert.FromBase64String(raw)))using(var reader=new BinaryReader(stream))
            {var row=read(reader);if(stream.Position!=stream.Length)throw new FormatException("Trailing receipt bytes.");return row;}
        }
        internal static Phase Reserve(Input old,Input request,bool empty,bool capacity,long now)
        {
            if(request?.Valid!=true)return Phase.Rejected;
            if(old!=null && request.Sequence<=old.Sequence)
                return old.Same(request)?old.State:Phase.Uncertain;
            if(old!=null && (old.State==Phase.Reserved || old.State==Phase.Committing || old.State==Phase.Uncertain))return Phase.Rejected;
            return empty && capacity?Phase.Reserved:Phase.Rejected;
        }
        internal static bool CanCommit(Input stored,Input requested,long now)=>stored?.Same(requested)==true &&
            stored.State==Phase.Reserved && stored.Deadline>=now;
        internal static void Issue(Output row,int index)
        {
            if(index<0 || index>=row.Count || row.Slots[index]!=0)throw new InvalidOperationException("Output is already issued or completed.");
            row.Slots[index]=1;
        }
        internal static void CompleteSlot(Output row,int index)
        {
            if(index<0 || index>=row.Count || row.Slots[index]!=1)throw new InvalidOperationException("Output completion lacks issue receipt.");
            row.Slots[index]=2;
        }
    }
}
