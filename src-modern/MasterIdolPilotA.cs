using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using UnityEngine;
namespace ValheimMastery
{
    // Process-local opt-in only. No network/world/AI clock or gameplay state is cached.
    internal static class MasterIdolPilotA
    {
        private sealed class Slots { internal readonly Dictionary<Type,Component> Values=new Dictionary<Type,Component>(); }
        private static ConditionalWeakTable<Component,Slots> Cache=new ConditionalWeakTable<Component,Slots>();
        private static ConfigEntry<bool> Setting;
        private static bool Mode;
        private static int Epoch;
        private static void Reset(){Cache=new ConditionalWeakTable<Component,Slots>();unchecked{Epoch++;}}
        private static void Changed(object sender,EventArgs args){Mode=Setting?.Value==true;Reset();}
        internal static bool Enabled
        {
            get
            {
                var current=MasteryPlugin.Settings?.ExperimentalOptimization;
                if(!ReferenceEquals(current,Setting)){if(Setting!=null)Setting.SettingChanged-=Changed;Setting=current;if(Setting!=null)Setting.SettingChanged+=Changed;Mode=Setting?.Value==true;Reset();}
                bool value=Setting?.Value==true;if(value!=Mode){Mode=value;Reset();}return value;
            }
        }
        internal static int Generation {get{bool observed=Enabled;return Epoch;}}
        internal static T Get<T>(Component actor) where T:Component
        {
            if(!Enabled)return actor.GetComponent<T>();
            var slots=Cache.GetValue(actor,_=>new Slots());
            if(slots.Values.TryGetValue(typeof(T),out var component)&&component!=null)return (T)component;
            var found=actor.GetComponent<T>();
            // Never negative-cache: late native attachment and destroyed components remain observable.
            if(found!=null)slots.Values[typeof(T)]=found;else slots.Values.Remove(typeof(T));
            return found;
        }
    }
}
