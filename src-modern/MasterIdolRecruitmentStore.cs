using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
namespace ValheimMastery
{
    // Nonfinancial timers only. Ordinary eventual persistence, never a forced world/character save.
    internal sealed class MasterIdolRecruitmentStore
    {
        private readonly string Path;private readonly long World;private Task Worker;
        private static readonly object Gate=new object();
        internal bool Ready{get;private set;}
        internal Dictionary<string,string[]> Members{get;private set;}=new Dictionary<string,string[]>(StringComparer.Ordinal);
        internal MasterIdolRecruitmentStore(string path,long world){Path=path;World=world;}
        internal Dictionary<string,double> Load()
        {
            Ready=false;Members.Clear();var result=new Dictionary<string,double>(StringComparer.Ordinal);
            try
            {
                lock(Gate)
                {
                    if(!File.Exists(Path)){Ready=!File.Exists(Path+".tmp");return result;}
                    if(new FileInfo(Path).Length>262144)return result;
                    using(var reader=new BinaryReader(File.OpenRead(Path),Encoding.UTF8))
                    {
                        if(reader.ReadInt32()!=3||reader.ReadInt64()!=World)return result;
                        int count=reader.ReadInt32();if(count<0||count>1024)return result;
                        for(int n=0;n<count;n++){string key=reader.ReadString();double time=reader.ReadDouble();if(key.Length>160||!Valid(key)||double.IsNaN(time)||double.IsInfinity(time)||time<=0||result.ContainsKey(key))return new Dictionary<string,double>();result.Add(key,time);}
                                                int groups=reader.ReadInt32();if(groups<0||groups>512)return new Dictionary<string,double>();var all=new HashSet<string>(StringComparer.Ordinal);
                        for(int n=0;n<groups;n++)
                        {
                            string key=reader.ReadString();int size=reader.ReadInt32();if(!ValidGroup(key)||Members.ContainsKey(key)||size<0||size>FamilyLimit(key))return new Dictionary<string,double>();
                            var ids=new string[size];for(int j=0;j<size;j++){ids[j]=reader.ReadString();if(!MasterIdolNetworkQuota.ValidUid(ids[j])||!all.Add(ids[j]))return new Dictionary<string,double>();}Members.Add(key,ids);
                        }
                        if(reader.BaseStream.Position!=reader.BaseStream.Length)return new Dictionary<string,double>();
                    }
                    Ready=true;return result;
                }
            }
            catch{Ready=false;return new Dictionary<string,double>();}
        }
                private static int FamilyLimit(string key)=>MasterIdolResidentRoster.Limit(key.Substring(key.LastIndexOf('|')+1));
        private static bool ValidGroup(string key)
        {
            if(key.Length>160)return false;int split=key.LastIndexOf('|');if(split<0)return false;
            var parts=key.Substring(0,split).Split('/');return parts.Length==2&&MasterIdolNetworkQuota.ValidUid(parts[0])&&Guid.TryParseExact(parts[1],"N",out _)&&FamilyLimit(key)>0;
        }
        private static bool Valid(string key)
        {
            int slotSeparator=key.LastIndexOf('|');if(slotSeparator<0||!int.TryParse(key.Substring(slotSeparator+1),out int slot))return false;key=key.Substring(0,slotSeparator);
            int separator=key.LastIndexOf('|');if(separator<0)return false;
            string home=key.Substring(0,separator);var parts=home.Split('/');
            return parts.Length==2&&MasterIdolNetworkQuota.ValidUid(parts[0])&&Guid.TryParseExact(parts[1],"N",out _)&&slot>=0&&slot<MasterIdolResidentRoster.Limit(key.Substring(separator+1));
        }
                internal void Poll()
        {
            if(Worker==null||!Worker.IsCompleted)return;
            if(Worker.IsFaulted||Worker.IsCanceled){var observed=Worker.Exception;Ready=false;}
            Worker=null;
        }
        internal bool Queue(Dictionary<string,double> state,IDictionary<string,HashSet<string>> members=null)
        {
            Poll();if(!Ready||Worker!=null)return false;
            if(state.Count>1024||state.Any(x=>!Valid(x.Key)||double.IsNaN(x.Value)||double.IsInfinity(x.Value)||x.Value<=0)){Ready=false;return false;}
                        var snapshot=new Dictionary<string,double>(state,StringComparer.Ordinal);
            var snapshotMembers=members==null?new Dictionary<string,string[]>(StringComparer.Ordinal):members.ToDictionary(x=>x.Key,x=>x.Value.ToArray(),StringComparer.Ordinal);
            var unique=new HashSet<string>(StringComparer.Ordinal);
            if(snapshotMembers.Count>512||snapshotMembers.Any(x=>!ValidGroup(x.Key)||x.Value.Length>FamilyLimit(x.Key)||x.Value.Any(id=>!MasterIdolNetworkQuota.ValidUid(id)||!unique.Add(id)))){Ready=false;return false;}
            Worker=Task.Run(()=>
            {
                lock(Gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    using(var writer=new BinaryWriter(File.Create(Path+".tmp"),Encoding.UTF8))
                    {writer.Write(3);writer.Write(World);writer.Write(snapshot.Count);foreach(var pair in snapshot){writer.Write(pair.Key);writer.Write(pair.Value);}writer.Write(snapshotMembers.Count);foreach(var pair in snapshotMembers){writer.Write(pair.Key);writer.Write(pair.Value.Length);foreach(string id in pair.Value)writer.Write(id);}}
                    if(File.Exists(Path))File.Replace(Path+".tmp",Path,null);else File.Move(Path+".tmp",Path);
                }
            });return true;
        }
    }
}



