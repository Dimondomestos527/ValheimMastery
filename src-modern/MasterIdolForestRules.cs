using System;
using System.IO;
using System.Text;
namespace ValheimMastery
{
    internal static class MasterIdolForestRules
    {
        internal static readonly string[] Families={"Greyling","Greydwarf","Greydwarf_Elite","Greydwarf_Shaman"};
        internal static int Family(string name)=>Array.IndexOf(Families,name);
        internal static bool Nonce(string value)=>value!=null&&value.Length==32&&Guid.TryParseExact(value,"N",out _);
        internal static bool Home(string value)=>MasterIdolNativeTaming.SameHome(value,value);
        internal static bool Deadline(long first,long daySeconds,out long deadline)
        {
            deadline=0;if(first<=0||daySeconds<=0)return false;
            try{deadline=checked(first+checked(daySeconds*TimeSpan.TicksPerSecond));return deadline<=DateTime.MaxValue.Ticks;}
            catch(OverflowException){return false;}
        }
        internal static bool Window(long stamp,long now,long born,int seconds=6)
            =>stamp>=born&&born>0&&now>=born&&stamp<=now+TimeSpan.TicksPerSecond&&now-stamp<=seconds*TimeSpan.TicksPerSecond;
        internal static bool Death(long stamp,long now,long born,float health,bool nativeZero,bool matchingTombstone,bool ownerWitness)
            =>ownerWitness&&float.IsFinite(health)&&health<=0&&Window(stamp,now,born)&&(nativeZero||matchingTombstone);
        internal static bool Guest(long first,long deadline,long now)=>first>0&&deadline>first&&now>=first-TimeSpan.TicksPerSecond&&deadline<=DateTime.MaxValue.Ticks;
        internal static bool Expired(long deadline,long now)=>deadline>0&&now>=deadline;
        internal static bool Outside(int ax,int ay,int bx,int by,int nativeRadius)
            =>nativeRadius>=0&&(Math.Abs(ax-bx)>nativeRadius+1||Math.Abs(ay-by)>nativeRadius+1);
        internal static bool Birth(float elapsed)=>float.IsFinite(elapsed)&&elapsed>=0&&elapsed<1.6f;
        internal static bool Name(string value)
        {if(value==null||value.Length>128)return false;foreach(char c in value)if(char.IsControl(c)||c=='<'||c=='>')return false;return true;}
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        internal static string ReadText(BinaryReader reader,int maximumBytes)
        {
            uint count=0;bool ended=false;
            for(int i=0;i<5;i++){byte next=reader.ReadByte();if(i==4&&(next&0xF8)!=0)throw new InvalidDataException("String length");count|=(uint)(next&127)<<(i*7);if((next&128)==0){ended=true;break;}}
            if(!ended||count>maximumBytes||count>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException("Bounded string");
            return Utf8.GetString(reader.ReadBytes((int)count));
        }
    }
    internal sealed class MasterIdolForestGuardRecord
    {
        internal string Home,Family,Uid,Nonce,Name="";
        internal long Born,Death,Respawn;
        internal bool Valid()=>MasterIdolForestRules.Home(Home)&&MasterIdolForestRules.Family(Family)>=0&&
            MasterIdolNetworkQuota.ValidUid(Uid)&&MasterIdolForestRules.Nonce(Nonce)&&Born>0&&Born<=DateTime.MaxValue.Ticks&&
            (Death==0?Respawn==0:Death>=Born&&Respawn>Death&&Respawn<=DateTime.MaxValue.Ticks)&&MasterIdolForestRules.Name(Name);
        internal MasterIdolForestGuardRecord Copy()=>new MasterIdolForestGuardRecord{Home=Home,Family=Family,Uid=Uid,Nonce=Nonce,Born=Born,Death=Death,Respawn=Respawn,Name=Name};
    }
}
