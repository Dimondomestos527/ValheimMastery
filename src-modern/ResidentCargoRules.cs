using System;
namespace ValheimMastery
{
    internal static class ResidentCargoRules
    {
        internal static bool Living(float value)=>float.IsFinite(value)&&value>0;
        internal static bool RevisionReached(uint current,uint expected)=>unchecked((int)(current-expected))>=0;
        internal static bool ConsentReady(long owner,long executor,bool requested,bool acknowledged,uint current,uint saved)
            =>owner==executor&&(!requested||acknowledged&&RevisionReached(current,saved));
    }
}
