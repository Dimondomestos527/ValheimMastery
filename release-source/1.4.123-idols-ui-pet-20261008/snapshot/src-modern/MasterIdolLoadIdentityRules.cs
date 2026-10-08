using System;
namespace ValheimMastery
{
    internal static class MasterIdolLoadIdentityRules
    {
        internal static bool Match(bool current,bool unique,bool loaded,bool sameOutput,string token,string frozenToken,string type,string frozenType,long creator,long payer,float distance)
            =>current&&unique&&(sameOutput||loaded)&&Guid.TryParseExact(token,"N",out _)&&token==frozenToken&&type==frozenType&&creator==payer&&payer!=0&&!float.IsNaN(distance)&&!float.IsInfinity(distance)&&distance<=.1f;
    }
}
