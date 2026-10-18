using System;
namespace ValheimMastery
{
    internal sealed class MasterIdolRevivalQuota
    {
        internal string Token;internal int Day;internal byte Used;
        internal bool Valid()=>Token!=null&&Token.Length==32&&Guid.TryParseExact(Token,"N",out _)&&Day>=0&&Used<=2;
        internal MasterIdolRevivalQuota Copy()=>new MasterIdolRevivalQuota{Token=Token,Day=Day,Used=Used};
    }
    internal enum ForestBirthCause:byte { Initial,ShamanTimer,ShamanRevival,ExplicitReplay }
    internal enum ReplayBirthAdmission:byte { None,Hold,Ready }
    // Server-only, never deserialized from a peer. Before pins remain independent of the physical lease.
    internal sealed class ForestBirthClaim
    {
        internal ZNet Net;internal ZDOMan Manager;internal ulong Generation;internal long World;
        internal MasterIdolRecruitmentStore Store;internal string Home,Token,Nonce,ReplayNonce;
        internal int Family,Day;internal float Until;internal ForestBirthCause Cause;
        internal MasterIdolForestGuardRecord Before,Snapshot,Shaman;internal ZDO ShamanData;
        internal long ShamanOwner;internal ushort ShamanRevision;
        internal MasterIdolRevivalQuota Quota;internal bool Creating,Completed,Released;
    }
}
