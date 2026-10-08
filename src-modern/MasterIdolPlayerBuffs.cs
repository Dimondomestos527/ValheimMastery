using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Pure presentation marker: no stat modifiers, timed refresh, resource writes or gameplay grants.
    internal static class MasterIdolPlayerBuffs
    {
        private static readonly string[] Types={"Swamp","Mountain","Plains","Mistlands"};
        private sealed class Lease{internal StatusEffect Template,Marker;}
        private static readonly Dictionary<string,Lease> Owned=new Dictionary<string,Lease>(StringComparer.Ordinal);
        private static Player Player;private static SEMan Manager;private static ZNetScene Scene;private static float Next;
        internal static bool Applies(string type,Vector3 point)=>type=="Swamp"?MasterIdolSwamp.Contains(point):MasterIdolEffectZones.At(type,point)!=null;
        private static void Remove(string type)
        {if(!Owned.TryGetValue(type,out var lease))return;var marker=lease.Marker;try{if(Manager?.m_character!=null&&marker!=null&&Manager.GetStatusEffects().Contains(marker))Manager.RemoveStatusEffect(marker.NameHash(),true);}finally{if(lease.Template!=null)UnityEngine.Object.Destroy(lease.Template);Owned.Remove(type);}}
        internal static void Cleanup(){foreach(string type in Types)Remove(type);Player=null;Manager=null;Scene=null;Next=0;}
        internal static void Tick()
        {
            var player=global::Player.m_localPlayer;
            if(!ReferenceEquals(Player,player)||!ReferenceEquals(Scene,ZNetScene.instance)||!ReferenceEquals(Manager,player?.m_seman)){Cleanup();Player=player;Manager=player?.m_seman;Scene=ZNetScene.instance;}
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;
            bool eligible=MasterIdolPlains.Owned(player)&&!player.IsDead()&&!player.IsTeleporting()&&Manager!=null&&Scene!=null;
            foreach(string type in Types)
            {
                if(!eligible||!Applies(type,player.transform.position)){Remove(type);continue;}
                int hash=("VM_Idol_Blessing_"+type).GetStableHashCode();
                if(Owned.TryGetValue(type,out var existing)&&Manager.GetStatusEffects().Contains(existing.Marker))continue;Remove(type);
                var profile=MasterIdolProfiles.Find(type);var icon=Scene.GetPrefab(profile.Prefab)?.GetComponent<Piece>()?.m_icon;if(icon==null)continue;
                var template=ScriptableObject.CreateInstance<StatusEffect>();template.name="VM_Idol_Blessing_"+type;
                template.m_name=GoldUiLocalization.Text(profile.English,profile.Ukrainian);template.m_tooltip=MasterIdolEffectsSummary.For(type);template.m_icon=icon;template.m_ttl=0;template.m_cooldownIcon=false;template.m_flashIcon=false;
                var marker=Manager.AddStatusEffect(template,false,0,0,0);if(marker!=null)Owned[type]=new Lease{Template=template,Marker=marker};else UnityEngine.Object.Destroy(template);
            }
        }
    }
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolPlayerBuffTickPatch
    {private static void Postfix()=>MasterIdolPlayerBuffs.Tick();}
    [HarmonyPatch(typeof(MasteryPlugin),"OnDestroy")]
    internal static class MasterIdolPlayerBuffCleanupPatch
    {private static void Prefix()=>MasterIdolCleanup.Run("idol-player-icons",MasterIdolPlayerBuffs.Cleanup,(stage,error)=>MasteryPlugin.Log?.LogWarning("[MasterIdols] "+stage+": "+error));}
}


