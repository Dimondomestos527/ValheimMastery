using System;
namespace ValheimMastery
{
    // Human-approved continuous V3 design A7B2B67449058769EC68A46C807BE25FB0832DB9F7660AF46947E105C82535C1.
    // One uninterrupted original native window per phrase. V2 chopped recipes are not retained.
    internal static class ResidentNativeVoiceRecipes
    {
        internal static readonly float[] NativeLengths={1.94675732f,1.50927436f,1.55575967f,1.89757371f,1.67895687f,1.78324258f,1.72140586f,2.1537416f};
        private static readonly float[] Basis={1.9f,1.25f,.9f,1.1f};
        private static readonly float[][] Contours={
            new[]{.86f,.95f,1.09f,1.18f},new[]{1.06f,1.04f,.96f,.88f},new[]{.92f,1.04f,1.1f,1.01f},
            new[]{.85f,1.05f,1.21f,1.16f},new[]{.99f,1.17f,1.12f,1.06f},new[]{1.02f,.99f,.86f,.78f},
            new[]{.89f,1.12f,1.15f,.94f},new[]{.88f,1f,1.16f,1.2f},new[]{.93f,1.11f,1.02f,.91f},new[]{1.12f,1.06f,.94f,.83f}};
        internal static ResidentContinuousVoiceRecipe For(int family,byte phrase,float maximumSeconds=float.PositiveInfinity)
        {
            if(family<0||family>3||phrase>9||float.IsNaN(maximumSeconds)||maximumSeconds<=.1f)return null;
            var result=new ResidentContinuousVoiceRecipe(7,.06f,1.75f,Basis[family],.8f,Contours[phrase]);
            // A short gesture gets one shorter continuous source window, never a chopped/restarted phrase.
            return result.Duration<=maximumSeconds?result:new ResidentContinuousVoiceRecipe(7,.06f,1.75f*maximumSeconds/result.Duration,Basis[family],.8f,Contours[phrase]);
        }
    }
    internal sealed class ResidentContinuousVoiceRecipe
    {
        internal readonly byte Clip;internal readonly float Start,Seconds,BasePitch,Gain;private readonly float[] Contour;
        private readonly float[] Times=new float[257];
        internal float Duration=>Times[Times.Length-1];
        internal ResidentContinuousVoiceRecipe(byte clip,float start,float seconds,float basis,float gain,float[] contour)
        {
            if(clip>=8||!float.IsFinite(start)||start<0||!float.IsFinite(seconds)||seconds<=0||start+seconds>ResidentNativeVoiceRecipes.NativeLengths[clip]||!float.IsFinite(basis)||basis<=0||!float.IsFinite(gain)||gain<=0||gain>1||contour==null||contour.Length!=4)throw new ArgumentException("Native voice recipe");
            foreach(float pitch in contour)if(!float.IsFinite(pitch)||pitch<=.5f||pitch>=1.5f)throw new ArgumentException("Voice pitch contour");
            Clip=clip;Start=start;Seconds=seconds;BasePitch=basis;Gain=gain;Contour=(float[])contour.Clone();
            for(int i=1;i<Times.Length;i++){float a=(i-1)/(float)(Times.Length-1),b=i/(float)(Times.Length-1);Times[i]=Times[i-1]+seconds*(b-a)*.5f*(1/Pitch(a)+1/Pitch(b));}
        }
        internal float Pitch(float progress)
        {
            float u=Math.Max(0,Math.Min(1,progress));int index=u<.33f?0:u<.7f?1:2;float start=index==0?0:index==1?.33f:.7f,end=index==0?.33f:index==1?.7f:1;
            float t=(u-start)/(end-start);t=t*t*(3-2*t);return BasePitch*(Contour[index]+(Contour[index+1]-Contour[index])*t);
        }
        internal float Progress(float elapsed)
        {
            if(elapsed<=0)return 0;if(elapsed>=Duration)return 1;int low=0,high=Times.Length-1;
            while(high-low>1){int middle=(low+high)/2;if(Times[middle]>elapsed)high=middle;else low=middle;}
            return (low+(elapsed-Times[low])/(Times[high]-Times[low]))/(Times.Length-1);
        }
        internal float Volume(float elapsed)=>Gain*Math.Min(Math.Max(0,Math.Min(1,elapsed/.035f)),Math.Max(0,Math.Min(1,(Duration-elapsed)/.05f)));
    }
}
