using System;
namespace ValheimMastery
{
    internal static class MasterIdolWelcomeRules
    {
        internal const float Duration=2.5f;
        internal static bool Accept(long world,long currentWorld,long sender,long owner,long started,long now,string home,string expected)
        {
            if(world==0||world!=currentWorld||owner==0||sender!=owner||home==null||home.Length>128||home!=expected||started<=0||now<=0)return false;
            try{long elapsed=checked(now-started);return elapsed>=-TimeSpan.TicksPerSecond/2&&elapsed<=TimeSpan.TicksPerSecond*5/2;}catch(OverflowException){return false;}
        }
    }
}
