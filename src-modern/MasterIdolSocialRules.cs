using System;
using System.Linq;using System.Collections.Generic;
namespace ValheimMastery
{
    internal static class MasterIdolSocialRules
    {
        internal const double AbsenceSeconds=300;
        internal static bool Returned(double now,double lastSeen,bool wasPresent)
            =>!wasPresent&&lastSeen>0&&now>=lastSeen&&now-lastSeen>=AbsenceSeconds;
        internal static bool SeatLease(double now,double end,bool resident,bool battle)
            =>resident&&!battle&&!double.IsNaN(now)&&end>now&&end-now<=45;
        internal static bool AssignedSeat(uint seat,int rank,int count)
            =>count>0&&rank>=0&&rank<count&&seat%(uint)count==(uint)rank;
        internal const double SeatTurnSeconds=45;
        internal static long SeatTurn(double now)=>!double.IsNaN(now)&&!double.IsInfinity(now)&&now>=0?(long)Math.Floor(now/SeatTurnSeconds):-1;
        internal static bool AssignedSeatAt(uint seat,int rank,int count,double now){long turn=SeatTurn(now);return turn>=0&&count>0&&rank>=0&&rank<count&&((seat%(uint)count)+(uint)(turn%count))%(uint)count==(uint)rank;}
        internal static bool SeatStartWindow(double now){long turn=SeatTurn(now);return turn>=0&&now-turn*SeatTurnSeconds>=2&&(turn+1)*SeatTurnSeconds-now>=14;}
        internal static double SeatEnd(double now,double duration){return SeatStartWindow(now)?Math.Min(now+Math.Max(12,Math.Min(25,duration)),(SeatTurn(now)+1)*SeatTurnSeconds-2):0;}
    }
    internal static class MasterIdolRecruitmentRules
    {
        internal static int Stage(double now,double vacantSince,double dayLength)
        {
            if(double.IsNaN(now)||double.IsInfinity(now)||double.IsNaN(vacantSince)||double.IsInfinity(vacantSince)||double.IsNaN(dayLength)||double.IsInfinity(dayLength)||dayLength<=0||vacantSince<=0||now<vacantSince)return 0;
            double age=now-vacantSince;
            return age>=dayLength?2:age>=dayLength*.5?1:0;
        }
                        internal static double[] Reconcile(double[] timers,ISet<string> previous,ISet<string> current,int limit,double now)
        {
            int joined=current.Count(id=>!previous.Contains(id)),departed=previous.Count(id=>!current.Contains(id));
            var remaining=timers.Skip(Math.Min(joined,timers.Length)).Concat(Enumerable.Repeat(now,departed)).ToArray();
            return Slots(remaining,limit-current.Count,now);
        }
        internal static double[] Slots(double[] previous,int missing,double now)
        {
            if(missing<0||missing>4)throw new ArgumentOutOfRangeException(nameof(missing));
            var result=new double[missing];int retained=Math.Min(previous.Length,missing),skip=Math.Max(0,previous.Length-missing);
            for(int n=0;n<retained;n++)result[n]=now<previous[n+skip]?now:previous[n+skip];
            for(int n=retained;n<missing;n++)result[n]=now;
            return result;
        }
        internal static bool Needs(string family,int present)=>present>=0&&present<MasterIdolResidentRoster.Limit(family);
    }
}


