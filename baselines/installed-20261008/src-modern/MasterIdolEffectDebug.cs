using System;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(PerkDebugService),nameof(PerkDebugService.RegisterCommands))]
    internal static class MasterIdolEffectDebug
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("vm_idol_effects","Показати ефекти мережі тут і записати результат у лог; лише читання.",State,false);
            new Terminal.ConsoleCommand("vm_idol_target","Наведися на рослину чи істоту: перевірити ідольний ефект і записати в лог; лише читання.",Target,false);
        }
        private static void Print(Terminal.ConsoleEventArgs args,string text)
        {args.Context.AddString(text);MasteryPlugin.Log.LogInfo("[IdolTest] "+text);}
        private static string Zone(string type,Vector3 point)
        {var zone=MasterIdolEffectZones.At(type,point);return zone==null?"none":zone.Id+" circles="+zone.Circles.Length;}
        private static void State(Terminal.ConsoleEventArgs args)
        {
            Print(args,MasterIdolEffectZones.Diagnostics());var player=Player.m_localPlayer;
            if(player==null){Print(args,"local player=none");return;}
            Print(args,"player position="+player.transform.position+" Meadows="+Zone("Meadows",player.transform.position)+" BlackForest="+Zone("BlackForest",player.transform.position));
            var forest=MasterIdolEffectZones.At("BlackForest",player.transform.position);if(forest!=null)foreach(var member in forest.Residents)Print(args,"resident UID="+member.Key+" family="+member.Value);
        }
        private static void Target(Terminal.ConsoleEventArgs args)
        {
            var player=Player.m_localPlayer;
            if(player==null){Print(args,"local player=none");return;}
            Vector3 eye=player.GetEyePoint();
            if(!Physics.Raycast(eye,player.GetAimDir(eye),out RaycastHit hit,60f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)){Print(args,"target=none; aim at plant or creature");return;}
            var creature=hit.collider?.GetComponentInParent<Character>();var plant=hit.collider?.GetComponentInParent<Plant>();
            Print(args,MasterIdolEffectZones.Diagnostics());
            if(creature!=null)
            {
                var view=creature.m_nview;bool owner=view?.IsValid()==true&&view.IsOwner();var leash=MasterIdolLeash.For(creature);
                string home=view?.GetZDO()?.GetString("vm.idol.home.v1","")??"";var zone=MasterIdolEffectZones.Find(home);
                Print(args,"creature="+Utils.GetPrefabName(creature.gameObject)+" position="+creature.transform.position+" owner="+owner+" tamed="+creature.IsTamed()+" resident="+(leash?.DiagnosticResident==true)+" home="+home+" insideHome="+(zone?.Contains(creature.transform.position)==true));
                var meadow=MasterIdolEffectZones.At("Meadows",creature.transform.position);
                Print(args,"Meadows="+Zone("Meadows",creature.transform.position)+" tameModifiers="+(creature.IsTamed()&&meadow!=null)+" localDamageModifierApplied="+MasterIdolMeadows.Animal(creature)+" breeding/maturation/cap=2; incomingDamage=0.25 when eligible; attack=vanilla");
                if(creature.GetComponent<MonsterAI>() is MonsterAI ai)
                    Print(args,"target="+(ai.m_targetCreature==null?"none":Utils.GetPrefabName(ai.m_targetCreature.gameObject))+" targetInsideHome="+(ai.m_targetCreature!=null&&zone?.Contains(ai.m_targetCreature.transform.position)==true)+" staticTarget="+(ai.m_targetStatic!=null)+" attackPlayerObjects="+ai.m_attackPlayerObjects+" afraidOfFire="+ai.m_afraidOfFire);
                return;
            }
            if(plant!=null)
            {bool crop=MasterIdolMeadows.Crop(plant);bool active=crop&&MasterIdolEffectZones.At("Meadows",plant.transform.position)!=null;Print(args,"plant="+Utils.GetPrefabName(plant.gameObject)+" cropEligible="+crop+" Meadows="+Zone("Meadows",plant.transform.position)+" growthFactor="+(active?"2":"1"));return;}
            Print(args,"target is neither plant nor creature");
        }
    }
}


