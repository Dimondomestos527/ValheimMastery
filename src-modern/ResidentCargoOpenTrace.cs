using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class ResidentCargoTraceBudget
    {
        private readonly Dictionary<string,float> Last=new Dictionary<string,float>();
        private int Count;private float Start;private long World;
        internal bool Allow(float now,long world,string key)
        {
            if(world!=World||now<Start||now-Start>=60f){Last.Clear();Count=0;Start=now;World=world;}
            if(Count>=32||Last.TryGetValue(key,out float at)&&now-at<5f)return false;
            Last[key]=now;Count++;return true;
        }
        internal void Clear(){Last.Clear();Count=0;Start=0;World=0;}
    }
    internal static class ResidentCargoOpenTrace
    {
        private static readonly ResidentCargoTraceBudget Budget=new ResidentCargoTraceBudget();
        internal static void Write(bool manual,string stage,string nonce=null)
        {
            if(!manual)return;
            try
            {
                string token=Guid.TryParseExact(nonce,"N",out _)?nonce.Substring(0,8):"local";
                long world=ZNet.instance?.GetWorld()!=null?ZNet.instance.GetWorldUID():0;
                if(Budget.Allow(Time.unscaledTime,world,stage+":"+token))MasteryPlugin.Log?.LogInfo("[CargoOpenTrace] stage="+stage+" attempt="+token);
            }catch{} // Observations cannot change native cargo/UI control flow.
        }
        internal static void Reset()=>Budget.Clear();
    }
}
