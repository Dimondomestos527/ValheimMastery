using System;
using System.Collections.Generic;
namespace ValheimMastery
{
    internal static class MasterIdolFinalBiomeRules
    {
        internal const string FoodPrefix="vm.idol.plains.food.v1.";
        private static readonly HashSet<string> Positive=new HashSet<string>(StringComparer.Ordinal){"GP_Eikthyr","GP_TheElder","GP_Bonemass","GP_Moder","GP_Yagluth","GP_Queen","GP_Fader","Potion_barleywine","Potion_frostresist","Potion_poisonresist"};
        internal static bool Approved(string name)=>name!=null&&Positive.Contains(name);
        internal static bool Duration(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value)&&value>0&&value<=172800;
        internal static float RefreshTime(float elapsed,float appliedMaximum,float nativeMaximum)
        {if(!Duration(appliedMaximum)||!Duration(nativeMaximum)||float.IsNaN(elapsed)||float.IsInfinity(elapsed)||elapsed<0||elapsed>=appliedMaximum)return elapsed;return Math.Min(elapsed,Math.Max(0,appliedMaximum-Math.Min(appliedMaximum,nativeMaximum)));}
    }
}
