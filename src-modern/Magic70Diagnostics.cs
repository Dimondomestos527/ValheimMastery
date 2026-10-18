using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // Ephemeral observation only. No authority, persistent state or world scan.
    internal sealed class Magic70TraceBudget
    {
        private readonly Dictionary<string,float> Last = new Dictionary<string,float>();
        private float Start; private int Count; private long World;
        internal bool Allow(float now,long world,string key)
        {
            if(world!=World || now<Start || now-Start>=60f) { Last.Clear(); Count=0; Start=now; World=world; }
            if(Count>=64 || (Last.TryGetValue(key,out float at) && now-at<5f)) return false;
            Last[key]=now;Count++;return true;
        }
        internal void Clear(){Last.Clear();Count=0;Start=0;World=0;}
    }
    internal static class Magic70Diagnostics
    {
        private sealed class Watch { internal float At; internal int Samples; }
        private static readonly Magic70TraceBudget Budget = new Magic70TraceBudget();
        private static readonly Dictionary<ZDOID,Watch> Watches = new Dictionary<ZDOID,Watch>();
        private static float MadnessWindow;
        private static string Token(Character c) => c == null ? "none" : c.GetZDOID().GetHashCode().ToString("X8");
        private static bool Allow(string stage,string id)
            => Budget.Allow(Time.unscaledTime,Ashlands70Cast.CurrentWorld,stage+":"+id);
        internal static void Arm(ZDOID id)
        {
            try { MadnessWindow=Time.unscaledTime+60f;if(Watches.Count<16&&!Watches.ContainsKey(id))Watches.Add(id,new Watch{At=Time.unscaledTime}); } catch { }
        }
        internal static void Sample(ZDOID id,Character body)
        {
            try
            {
                if(!Watches.TryGetValue(id,out Watch watch)||Time.unscaledTime-watch.At<(watch.Samples==0?.6f:2f))return;
                watch.Samples++;if(watch.Samples>=2)Watches.Remove(id);
                string token=id.GetHashCode().ToString("X8"),stage="madness-sample"+watch.Samples;
                if(!Allow(stage,token))return;
                if(body==null){MasteryPlugin.Log.LogInfo("[Magic70Trace] "+stage+" id="+token+" body=unloaded");return;}
                var view=body.m_nview;var data=view?.IsValid()==true?view.GetZDO():null;
                var ai=body.GetComponent<MonsterAI>();var animal=body.GetComponent<AnimalAI>();
                Character target=ai!=null?ai.m_targetCreature:animal!=null?animal.m_target:null;
                var humanoid=body as Humanoid;
                MasteryPlugin.Log.LogInfo("[Magic70Trace] "+stage+" id="+token+" owner="+(view?.IsOwner()==true)+" active="+Ashlands70Madness.Active(body)+
                    " world="+(data!=null&&data.GetLong(Ashlands70Madness.WorldKey,0)==Ashlands70Cast.CurrentWorld)+
                    " expires="+(data!=null&&Ashlands70Rules.Active(Ashlands70Cast.Now,data.GetLong(Ashlands70Madness.UntilKey,0)))+
                    " ai="+(ai!=null?"monster":animal!=null?"animal":"none")+" tame="+body.IsTamed()+" cannotTarget="+body.m_aiCannotTargetOthers+
                    " weapon="+(humanoid!=null&&humanoid.GetCurrentWeapon()!=null)+" target="+Token(target)+" targetPlayer="+(target is Player)+
                    " sensed="+(ai!=null?ai.m_timeSinceSensedTargetCreature:-1f)+" sinceAttack="+(ai!=null?ai.m_timeSinceAttacking:-1f));
            } catch { } // Diagnostics must never interrupt gameplay.
        }
        internal static void Incoming(Character victim,HitData hit)
        {
            try
            {
                if(!(victim is Player)||Time.unscaledTime>MadnessWindow||hit==null)return;
                Character attacker=hit.GetAttacker();if(!Ashlands70Madness.Active(attacker))return;
                Event("madness-incoming",victim,attacker,"observed-native-damage-entry");
            } catch { }
        }
        internal static void Event(string stage,Character victim,Character attacker,string reason)
        {
            try { string id=Token(victim);if(Allow(stage,id))MasteryPlugin.Log.LogInfo("[Magic70Trace] "+stage+" victim="+id+" attacker="+Token(attacker)+" reason="+reason); } catch { }
        }
        internal static void Outcome(Ashlands70Combat.Scope scope,Exception error)
        {
            if(scope?.Own!=true)return;
            try { Event("orbit-native-event",scope.Victim,scope.Attacker,"error="+(error!=null)+",barrier="+scope.Absorbed+",block="+scope.Blocked+",health="+scope.LostHealth); } catch { }
        }
        internal static void Reset(){Watches.Clear();Budget.Clear();MadnessWindow=0;}
    }
}
