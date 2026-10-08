using System;
using System.Collections.Generic;
namespace ValheimMastery
{
    internal readonly struct MasterIdolQuotaEntry
    { internal readonly string Output,Network,Type;internal MasterIdolQuotaEntry(string output,string network,string type){Output=output;Network=network;Type=type;} }
    internal static class MasterIdolNetworkQuota
    {
        internal const int Limit=2;
        internal static bool TryUid(string value,out long user,out uint id)
        {
            user=0;id=0;var parts=value?.Split(':');
            return parts?.Length==2&&long.TryParse(parts[0],System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out user)&&
                uint.TryParse(parts[1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out id);
        }
        internal static bool ValidUid(string value)=>TryUid(value,out long user,out uint id)&&(user!=0||id!=0)&&
            value==user.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        private sealed class UidOrder:IComparer<string>
        {
            public int Compare(string a,string b)
            {
                if(TryUid(a,out long au,out uint ai)&&TryUid(b,out long bu,out uint bi)){int n=au.CompareTo(bu);return n!=0?n:ai.CompareTo(bi);}
                return StringComparer.Ordinal.Compare(a,b);
            }
        }

        internal static HashSet<string> Select(IEnumerable<MasterIdolQuotaEntry> candidates)
        {
            var groups=new Dictionary<string,SortedDictionary<string,string>>(StringComparer.Ordinal);
            foreach(var e in candidates)
            {if(e.Network==null||e.Output==null||MasterIdolProfiles.Find(e.Type)==null)continue;if(!groups.TryGetValue(e.Network,out var ids)){ids=new SortedDictionary<string,string>(new UidOrder());groups[e.Network]=ids;}ids[e.Output]=e.Type;}
            var selected=new HashSet<string>(StringComparer.Ordinal);
            foreach(var group in groups.Values){var types=new HashSet<string>(StringComparer.Ordinal);foreach(var entry in group){if(types.Count>=Limit)break;if(types.Add(entry.Value))selected.Add(entry.Key);}}
            return selected;
        }
    }
}
