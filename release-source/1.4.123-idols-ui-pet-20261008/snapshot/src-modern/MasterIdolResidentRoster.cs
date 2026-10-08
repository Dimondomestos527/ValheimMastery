using System;
using System.Collections.Generic;
using System.Linq;
namespace ValheimMastery
{
    internal readonly struct MasterIdolResidentCandidate
    {
        internal readonly string Id,Type,Home;internal readonly bool Existing;
        internal MasterIdolResidentCandidate(string id,string type,bool existing,string home=null){Id=id;Type=type;Existing=existing;Home=home;}
    }
    internal static class MasterIdolResidentRoster
    {
        internal static int Limit(string type)=>type=="Greyling"?4:type=="Greydwarf"?2:type=="Greydwarf_Elite"||type=="Greydwarf_Shaman"?1:0;
        internal static bool MayJoin(MasterIdolResidentCandidate candidate,string home,ISet<string> activeHomes,IDictionary<string,string> previousHomes)
        {return previousHomes.TryGetValue(candidate.Id,out string previous)?previous==home:candidate.Home==null||!activeHomes.Contains(candidate.Home)||candidate.Home==home;}
        internal static Dictionary<string,string> Select(IEnumerable<MasterIdolResidentCandidate> candidates,ISet<string> previous,ISet<string> assigned)
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var item in candidates.OrderByDescending(x=>previous.Contains(x.Id)?2:x.Existing?1:0).ThenBy(x=>x.Id,StringComparer.Ordinal))
            {
                int limit=Limit(item.Type);if(limit==0||!MasterIdolNetworkQuota.ValidUid(item.Id)||assigned.Contains(item.Id)||result.ContainsKey(item.Id))continue;
                counts.TryGetValue(item.Type,out int count);if(count>=limit)continue;
                counts[item.Type]=count+1;result.Add(item.Id,item.Type);assigned.Add(item.Id);
            }
            return result;
        }
    }
}


