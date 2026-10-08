using System;
namespace ValheimMastery
{
    internal enum MasterIdolResidentSceneKind { Pet=1, OccupiedSeat=2, Refilled=3 }
    internal static class MasterIdolResidentSceneRules
    {
        internal const int MaxPacketBytes=640,MaxHomeRecords=64,MaxSlotRecords=16,MaxGraphicsScenes=4;
        internal const float ObserverRadius=16f,TargetRadius=4f,CastDelay=.4f,CastDuration=2.266667f;
        internal const double HomeQuiet=90,SeatQuiet=240,PetQuiet=480;
        internal static bool Kind(int kind)=>kind>=1&&kind<=3;
        internal static float Duration(MasterIdolResidentSceneKind kind)=>kind==MasterIdolResidentSceneKind.Pet?2.5f:3.5f;
        internal static double Quiet(MasterIdolResidentSceneKind kind,float random)=>kind==MasterIdolResidentSceneKind.Pet?PetQuiet:180+Math.Max(0,Math.Min(1,random))*120;
        internal static bool NativePet(bool current,bool hold,bool alt,float before,float after)
            =>current&&!hold&&!alt&&float.IsFinite(before)&&float.IsFinite(after)&&after>before;
        internal static bool Nonce(string value)=>value!=null&&value.Length==32&&Guid.TryParseExact(value,"N",out _);
        internal static bool QuietAccepted(MasterIdolResidentSceneKind kind,float quiet)=>float.IsFinite(quiet)&&
            (kind==MasterIdolResidentSceneKind.Pet?quiet==PetQuiet:quiet>=180&&quiet<=300);
        internal static bool Window(long start,long now,long context,float duration)
            =>start>0&&context>=0&&start>context+TimeSpan.TicksPerSecond&&
              start<=now+TimeSpan.TicksPerSecond&&now-start<(long)(duration*TimeSpan.TicksPerSecond);
        internal static float Elapsed(long start,long now)=>Math.Max(0,(float)((now-start)/(double)TimeSpan.TicksPerSecond));
        internal static int SeatPhase(float elapsed)=>elapsed<.8f?0:elapsed<2?1:2;
        internal static bool CastWindow(float elapsed)=>float.IsFinite(elapsed)&&elapsed>=CastDelay&&elapsed<CastDelay+CastDuration;
        internal static bool Finite(double time)=>!double.IsNaN(time)&&!double.IsInfinity(time)&&time>=0;
    }
}
