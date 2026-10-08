using System;
using System.Security.Cryptography;
namespace ValheimMastery
{
    // Transport only: publish nothing until all authenticated bounded chunks form the exact digest.
    internal sealed class MasterIdolProjectionCodec
    {
        internal const int ChunkBytes=65536,MaxBytes=4*1024*1024,MaxChunks=64;
        private byte[][] Chunks;private int Received,Length;private float Until;private long Revision;private string Digest,Epoch;
        internal long SeenRevision=>Revision;internal string SeenEpoch=>Epoch;
        internal void Reset(){Chunks=null;Received=Length=0;Revision=0;Digest=Epoch=null;Until=0;}
        internal byte[] Add(string epoch,long revision,string digest,int length,int index,int count,byte[] bytes,float now)
        {
            if(!Guid.TryParseExact(epoch,"N",out _)||revision<=0||digest==null||digest.Length!=44||length<4||length>MaxBytes||count!=(length+ChunkBytes-1)/ChunkBytes||count<1||count>MaxChunks||index<0||index>=count||bytes==null||bytes.Length!=(index==count-1?length-index*ChunkBytes:ChunkBytes))throw new FormatException();
            if(Chunks!=null&&now>Until)Reset();
            if(Chunks!=null&&(Epoch!=epoch||revision>Revision)){Reset();}
            if(Chunks!=null&&revision<Revision)return null;
            if(Chunks==null){Epoch=epoch;Revision=revision;Digest=digest;Length=length;Chunks=new byte[count][];Until=now+6;}
            if(Digest!=digest||Length!=length||Chunks.Length!=count)throw new FormatException();
            if(Chunks[index]!=null){var old=Chunks[index];for(int i=0;i<old.Length;i++)if(old[i]!=bytes[i])throw new FormatException();return null;}
            Chunks[index]=bytes;Received++;if(Received!=count)return null;
            var body=new byte[Length];for(int i=0;i<count;i++)Buffer.BlockCopy(Chunks[i],0,body,i*ChunkBytes,Chunks[i].Length);
            using(var hash=SHA256.Create())if(Convert.ToBase64String(hash.ComputeHash(body))!=Digest)throw new FormatException();
            Chunks=null;return body;
        }
    }
}
