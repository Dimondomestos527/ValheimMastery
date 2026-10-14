using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace ValheimMastery
{
    // Existing nonfinancial store; schema3 timers/members retained, schema4 adds four world slots.
    internal sealed class MasterIdolRecruitmentStore
    {
        private readonly string Path;private readonly long World;private Task Worker;private volatile bool Retired;
        private static readonly object Gate=new object();
        internal bool Ready{get;private set;}
        internal Dictionary<string,string[]> Members{get;private set;}=new Dictionary<string,string[]>(StringComparer.Ordinal);
        internal Dictionary<string,MasterIdolForestGuardRecord> Guards{get;private set;}=new Dictionary<string,MasterIdolForestGuardRecord>(StringComparer.Ordinal);
        internal MasterIdolRecruitmentStore(string path,long world){Path=path;World=world;}
        internal void Retire(){Retired=true;Ready=false;}
        internal Dictionary<string,double> Load()
        {
            Ready=false;Members.Clear();Guards.Clear();var result=new Dictionary<string,double>(StringComparer.Ordinal);
            try
            {
                lock(Gate)
                {
                    if(!File.Exists(Path)){Ready=!File.Exists(Path+".tmp");return result;}
                    if(new FileInfo(Path).Length>262144)return result;
                    using(var reader=new BinaryReader(File.OpenRead(Path),Encoding.UTF8))
                    {
                        int schema=reader.ReadInt32();if((schema!=3&&schema!=4)||reader.ReadInt64()!=World)return result;
                        int count=reader.ReadInt32();if(count<0||count>1024)return result;
                        for(int n=0;n<count;n++){string key=MasterIdolForestRules.ReadText(reader,640);double time=reader.ReadDouble();if(!Valid(key)||!double.IsFinite(time)||time<=0||result.ContainsKey(key))return Failed();result.Add(key,time);}
                        int groups=reader.ReadInt32();if(groups<0||groups>512)return Failed();var all=new HashSet<string>(StringComparer.Ordinal);
                        for(int n=0;n<groups;n++)
                        {
                            string key=MasterIdolForestRules.ReadText(reader,640);int size=reader.ReadInt32();if(!ValidGroup(key)||Members.ContainsKey(key)||size<0||size>FamilyLimit(key))return Failed();
                            var ids=new string[size];for(int j=0;j<size;j++){ids[j]=MasterIdolForestRules.ReadText(reader,64);if(!MasterIdolNetworkQuota.ValidUid(ids[j])||!all.Add(ids[j]))return Failed();}Members.Add(key,ids);
                        }
                        if(schema==4)
                        {
                            int slots=reader.ReadInt32();if(slots<0||slots>4)return Failed();var uids=new HashSet<string>(StringComparer.Ordinal);
                            for(int n=0;n<slots;n++)
                            {
                                var record=new MasterIdolForestGuardRecord{Home=MasterIdolForestRules.ReadText(reader,128),Family=MasterIdolForestRules.ReadText(reader,32),Uid=MasterIdolForestRules.ReadText(reader,64),Nonce=MasterIdolForestRules.ReadText(reader,32),Born=reader.ReadInt64(),Death=reader.ReadInt64(),Respawn=reader.ReadInt64(),Name=MasterIdolForestRules.ReadText(reader,512)};
                                if(!record.Valid()||Guards.ContainsKey(record.Family)||!uids.Add(record.Uid))return Failed();Guards.Add(record.Family,record);
                            }
                        }
                        if(reader.BaseStream.Position!=reader.BaseStream.Length)return Failed();
                    }
                    Ready=!Retired;return result;
                }
            }
            catch{return Failed();}
        }
        private Dictionary<string,double> Failed(){Ready=false;Members.Clear();Guards.Clear();return new Dictionary<string,double>();}
        private static int FamilyLimit(string key)=>MasterIdolResidentRoster.Limit(key.Substring(key.LastIndexOf('|')+1));
        private static bool ValidGroup(string key)
        {
            if(key==null||key.Length>160)return false;int split=key.LastIndexOf('|');return split>0&&MasterIdolForestRules.Home(key.Substring(0,split))&&FamilyLimit(key)>0;
        }
        private static bool Valid(string key)
        {
            if(key==null||key.Length>160)return false;int split=key.LastIndexOf('|');
            return split>0&&int.TryParse(key.Substring(split+1),out int slot)&&ValidGroup(key.Substring(0,split))&&slot>=0&&slot<FamilyLimit(key.Substring(0,split));
        }
        internal void Poll(){if(Worker==null||!Worker.IsCompleted)return;if(Worker.IsFaulted||Worker.IsCanceled){var observed=Worker.Exception;Ready=false;}Worker=null;}
        internal bool Queue(Dictionary<string,double> state,IDictionary<string,HashSet<string>> members=null,IDictionary<string,MasterIdolForestGuardRecord> guards=null)
        {
            Poll();if(!Ready||Retired||Worker!=null)return false;
            if(state.Count>1024||state.Any(x=>!Valid(x.Key)||!double.IsFinite(x.Value)||x.Value<=0)){Ready=false;return false;}
            var snapshot=new Dictionary<string,double>(state,StringComparer.Ordinal);
            var snapshotMembers=members==null?new Dictionary<string,string[]>(StringComparer.Ordinal):members.ToDictionary(x=>x.Key,x=>x.Value.ToArray(),StringComparer.Ordinal);
            var unique=new HashSet<string>(StringComparer.Ordinal);
            if(snapshotMembers.Count>512||snapshotMembers.Any(x=>!ValidGroup(x.Key)||x.Value.Length>FamilyLimit(x.Key)||x.Value.Any(id=>!MasterIdolNetworkQuota.ValidUid(id)||!unique.Add(id)))){Ready=false;return false;}
            var snapshotGuards=(guards??Guards).ToDictionary(x=>x.Key,x=>x.Value.Copy(),StringComparer.Ordinal);unique.Clear();
            if(snapshotGuards.Count>4||snapshotGuards.Any(x=>x.Key!=x.Value.Family||!x.Value.Valid()||!unique.Add(x.Value.Uid))){Ready=false;return false;}
            Worker=Task.Run(()=>
            {
                lock(Gate)
                {
                    if(Retired)return;Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    using(var writer=new BinaryWriter(File.Create(Path+".tmp"),Encoding.UTF8))
                    {
                        writer.Write(4);writer.Write(World);writer.Write(snapshot.Count);foreach(var pair in snapshot){writer.Write(pair.Key);writer.Write(pair.Value);}
                        writer.Write(snapshotMembers.Count);foreach(var pair in snapshotMembers){writer.Write(pair.Key);writer.Write(pair.Value.Length);foreach(string id in pair.Value)writer.Write(id);}
                        writer.Write(snapshotGuards.Count);foreach(var record in snapshotGuards.Values){writer.Write(record.Home);writer.Write(record.Family);writer.Write(record.Uid);writer.Write(record.Nonce);writer.Write(record.Born);writer.Write(record.Death);writer.Write(record.Respawn);writer.Write(record.Name);}
                    }
                    if(File.Exists(Path))File.Replace(Path+".tmp",Path,null);else File.Move(Path+".tmp",Path);
                }
            });return true;
        }
    }
}
