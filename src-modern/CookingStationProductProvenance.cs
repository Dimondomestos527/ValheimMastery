using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class CookingStationProductProvenance
    {
        internal sealed class Admission { internal int Slot; internal string Input; internal long Author; }
        internal static Admission Before(CookingStation station,long sender,string input)
        {
            if(station?.m_nview?.IsOwner()!=true || !station.IsItemAllowed(input))return null;
            int slot=station.GetFreeSlot();long author=ProcessingStationProgressionService.ResolveSender(sender);
            return slot<0?null:new Admission{Slot=slot,Input=input,Author=author};
        }
        internal static void Accepted(CookingStation station,Admission state)
        {
            if(state==null || station.m_nview.IsOwner()!=true)return;
            station.GetSlot(state.Slot,out string item,out float elapsed,out CookingStation.Status status,out bool cheated);
            if(item!=state.Input)return;
            var zdo=station.m_nview.GetZDO();
            int tier=state.Author==0?0:CookingAuthorNetwork.Tier(station.m_nview,state.Author);
            if(tier!=0 && tier!=35 && tier!=70)tier=0;
            zdo.Set("vm.cook.slot.product."+state.Slot,"1");
            zdo.Set("vm.cook.slot.product.author."+state.Slot,state.Author);
            zdo.Set("vm.cook.slot.product.tier."+state.Slot,tier);
            zdo.Set("vm.cook.slot.product.name."+state.Slot,Player.GetPlayer(state.Author)?.GetPlayerName()??"");
        }
        internal sealed class Output {internal long Author;internal int Tier;internal string Name;}
        internal static Output BeforeOutput(CookingStation station,int slot)
        {
            var zdo=station?.m_nview?.GetZDO();
            if(zdo==null || !station.m_nview.IsOwner())return null;
            if(zdo.GetString("vm.cook.slot.product."+slot,"")!="1")
            {
                // Preserve a previously stored legacy slot grant; never infer it
                // from the collector's present skill or an unknown slot.
                long author=zdo.GetLong("vm.cook.slot.author."+slot,0);
                int tier=zdo.GetInt("vm.cook.slot.tier."+slot,0);
                if(author==0 || (tier!=0 && tier!=35 && tier!=70))return null;
                return new Output{Author=author,Tier=tier,Name=Player.GetPlayer(author)?.GetPlayerName()??""};
            }
            return new Output{Author=zdo.GetLong("vm.cook.slot.product.author."+slot,0),Tier=zdo.GetInt("vm.cook.slot.product.tier."+slot,0),Name=zdo.GetString("vm.cook.slot.product.name."+slot,"")};
        }
        internal static void StampActual(CookingStation station,int slot,ItemDrop drop)
        {
            var state=BeforeOutput(station,slot);
            if(state==null || drop?.m_itemData==null || !CookingProductIdentity.Relevant(drop.m_itemData))return;
            drop.m_itemData.m_crafterID=state.Author;drop.m_itemData.m_crafterName=state.Name;
            drop.m_itemData.m_customData[CookingProductIdentity.VersionKey]="1";
            drop.m_itemData.m_customData[Cooking35Service.CookMasteryKey]=state.Tier.ToString(CultureInfo.InvariantCulture);
            drop.Save();
            // RPC_RemoveDoneItem may spawn several actual outputs from this slot.
            // Keep source until native slot reuse; Accepted replaces it on next input.
        }

    }
    [HarmonyPatch(typeof(CookingStation),"RPC_AddItem")]
    internal static class CookingStationFreshAdmissionPatch
    {
        private static void Prefix(CookingStation __instance,long sender,string itemName,out CookingStationProductProvenance.Admission __state)=>__state=CookingStationProductProvenance.Before(__instance,sender,itemName);
        [HarmonyPriority(Priority.Last)]private static void Postfix(CookingStation __instance,CookingStationProductProvenance.Admission __state)=>CookingStationProductProvenance.Accepted(__instance,__state);
    }
    [HarmonyPatch(typeof(CookingStation),"SpawnItem")]
    internal static class CookingStationFreshOutputPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();
            var anchors=Enumerable.Range(0,code.Count).Where(i=>code[i].operand is MethodInfo method &&
                method.Name=="GetComponent" && method.IsGenericMethod && method.GetGenericArguments().Length==1 &&
                method.GetGenericArguments()[0]==typeof(Rigidbody)).ToList();
            if(anchors.Count!=1)throw new InvalidOperationException("CookingStation actual output native contract changed.");
            int at=anchors[0];
            var actual=code.Take(at).Select((instruction,index)=>new{instruction,index}).LastOrDefault(row=>
                row.instruction.operand is MethodInfo method && method.Name=="GetComponent" && method.IsGenericMethod &&
                method.GetGenericArguments()[0]==typeof(ItemDrop));
            if(actual==null || actual.index+1>=code.Count || code[actual.index+1].opcode!=OpCodes.Stloc_3)
                throw new InvalidOperationException("CookingStation actual ItemDrop local changed.");
            var first=new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[at].labels);code[at].labels.Clear();
            first.blocks.AddRange(code[at].blocks);code[at].blocks.Clear();
            code.InsertRange(at,new[]{first,new CodeInstruction(OpCodes.Ldarg_2),new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CookingStationProductProvenance),nameof(CookingStationProductProvenance.StampActual)))});
            return code;
        }

    }
}

