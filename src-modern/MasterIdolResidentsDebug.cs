using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(PerkDebugService),nameof(PerkDebugService.RegisterCommands))]
    internal static class MasterIdolResidentsDebug
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("vm_idol_lighting","Показати бюджет/лічильники світла; лише читання.",args=>Print(args,MasterIdolLighting.Describe()+"\n"+MasterIdolActivity.Describe()),false);
            new Terminal.ConsoleCommand("vm_idol_recruitment","Таймери вільних місць ідола; лише читання на сервері/хості.",args=>
            {
                if(ZNet.instance?.IsServer()!=true){Print(args,"Таймери поповнення перевіряються на сервері/хості.");return;}
                Print(args,MasterIdolRecruitment.Describe());
            },false);
            new Terminal.ConsoleCommand("vm_idol_social","Наведися на мешканця: показати поточне заняття; лише читання.",args=>
            {
                var player=Player.m_localPlayer;if(player==null){Print(args,"local player=none");return;}var eye=player.GetEyePoint();
                if(!Physics.Raycast(eye,player.GetAimDir(eye),out RaycastHit hit,60f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)){Print(args,"resident target=none");return;}
                Print(args,hit.collider.GetComponentInParent<MasterIdolSocial>()?.Describe()??"social resident=none");
            },false);
        }
        private static void Print(Terminal.ConsoleEventArgs args,string text){args.Context.AddString(text);MasteryPlugin.Log.LogInfo("[IdolTest] "+text);}
    }
}


