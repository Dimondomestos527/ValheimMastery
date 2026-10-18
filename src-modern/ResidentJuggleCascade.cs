using System;
namespace ValheimMastery
{
    internal readonly struct ResidentJuggleBall
    {
        internal readonly int ThrowHand,Holder,Cycle;internal readonly float FlightU;internal readonly bool Airborne;
        internal ResidentJuggleBall(int from,int holder,int cycle,float u,bool air){ThrowHand=from;Holder=holder;Cycle=cycle;FlightU=u;Airborne=air;}
    }
    internal readonly struct ResidentJuggleHand
    {
        internal readonly float Across,Lift;
        internal ResidentJuggleHand(float across,float lift){Across=across;Lift=lift;}
    }
    // Three existing cosmetic stones, one alternating release every .4s.
    // Fixed 1s flight + .2s receiving-hand dwell; no physics or transform ownership.
    internal static class ResidentJuggleCascade
    {
        internal const double Interval=.4,Flight=1,Cycle=1.2,Warmup=.35,Exit=.4;
        internal const float ArcHeight=.34f;
        private const double Epsilon=.0000001;
        private static void Validate(float elapsed,float duration)
        {if(!float.IsFinite(elapsed)||!float.IsFinite(duration)||elapsed<0||elapsed>duration||duration<3)throw new ArgumentOutOfRangeException("juggle clock");}
        private static double Cutoff(float duration)=>duration-Warmup-Flight-Exit;
        internal static ResidentJuggleBall Ball(float elapsed,int index,float duration)
        {
            Validate(elapsed,duration);if(index<0||index>2)throw new ArgumentOutOfRangeException(nameof(index));
            double time=elapsed-Warmup,first=index*Interval;
            if(time+Epsilon<first)return new ResidentJuggleBall(index&1,index&1,0,0,false);
            int last=(int)Math.Floor((Cutoff(duration)-first+Epsilon)/Cycle);
            if(last<0)return new ResidentJuggleBall(index&1,index&1,0,0,false);
            int cycle=Math.Min(last,Math.Max(0,(int)Math.Floor((time-first+Epsilon)/Cycle)));
            double phase=Math.Max(0,time-first-cycle*Cycle);int from=(index+3*cycle)&1;
            if(phase+Epsilon>=Flight)return new ResidentJuggleBall(from,1-from,cycle,1,false);
            return new ResidentJuggleBall(from,1-from,cycle,(float)(phase/Flight),true);
        }
        private static float Smooth(float u)=>u*u*(3-2*u);
        internal static ResidentJuggleHand Hand(float elapsed,int side,float duration)
        {
            Validate(elapsed,duration);if(side<0||side>1)throw new ArgumentOutOfRangeException(nameof(side));
            double time=Math.Max(0,elapsed-Warmup),cutoff=Cutoff(duration);
            int last=(int)Math.Floor((cutoff+Epsilon)/Interval);if((last&1)!=side)last--;
            if(time>last*Interval+.2+Epsilon)
            {
                int incoming=(int)Math.Floor((Math.Min(time-Flight,cutoff)+Epsilon)/Interval);
                if((incoming&1)==side)incoming--;
                double afterCatch=time-(incoming*Interval+Flight);
                float lift=incoming>=0&&afterCatch>=0&&afterCatch<.18?-.065f*(float)Math.Sin(Math.PI*afterCatch/.18):0;
                return new ResidentJuggleHand(1,lift); // Final catches stay held; no empty-hand throw.
            }
            double phase=time-side*Interval;phase-=Math.Floor((phase+Epsilon)/.8)*.8;
            if(phase<0)phase=0;
            if(phase>=.6-Epsilon)
            {float u=(float)Math.Max(0,Math.Min(1,(phase-.6)/.2));return new ResidentJuggleHand(1-Smooth(u),-.065f*(float)Math.Sin(Math.PI*u));}
            if(phase<.2)
            {float u=(float)(phase/.2);return new ResidentJuggleHand(Smooth(u),.035f*(float)Math.Sin(Math.PI*u));}
            return new ResidentJuggleHand(1,0);
        }
        internal static float Height(float u)=>4*ArcHeight*u*(1-u);
        internal static float Envelope(float elapsed,float duration)
            =>Math.Max(0,Math.Min(1,Math.Min(elapsed/(float)Warmup,(duration-elapsed)/(float)Exit)));
    }
}
