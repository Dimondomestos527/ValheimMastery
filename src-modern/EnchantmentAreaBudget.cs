using System;
namespace ValheimMastery {
 // Bounded max-min sharing: each admitted cue samples its whole union at the
 // density its share permits. Small requests keep their original profile.
 internal static class EnchantmentAreaBudget {
  internal static int[] Allocate(int capacity,int[] desired){
   if(capacity<0||desired==null)throw new ArgumentOutOfRangeException();
   var result=new int[desired.Length];var pending=new bool[desired.Length];int count=0;
   for(int i=0;i<desired.Length;i++){if(desired[i]<0)throw new ArgumentOutOfRangeException();if(desired[i]>0){pending[i]=true;count++;}}
   int remaining=capacity;
   while(count>0){int share=remaining/count;bool exact=false;
    for(int i=0;i<desired.Length;i++)if(pending[i]&&desired[i]<=share){result[i]=desired[i];remaining-=desired[i];pending[i]=false;count--;exact=true;}
    if(exact)continue;
    for(int i=0;i<desired.Length;i++)if(pending[i])result[i]=share;
    break;
   }
   return result;
  }
  internal static int Capacity(float rate,float life){
   if(!float.IsFinite(rate)||!float.IsFinite(life)||rate<0||life<=0)throw new ArgumentOutOfRangeException();
   double value=Math.Ceiling(rate*life)+Math.Ceiling(rate*.1f)+1;
   return value>=int.MaxValue?int.MaxValue:(int)value;
  }
  internal static float Rate(float requested,float life,int capacity){
   if(capacity<2)return 0;if(Capacity(requested,life)<=capacity)return requested;
   double lo=0,hi=requested;
   for(int i=0;i<32;i++){double mid=(lo+hi)*.5;if(Capacity((float)mid,life)<=capacity)lo=mid;else hi=mid;}
   return (float)(lo*.999999);
  }
 }
}
