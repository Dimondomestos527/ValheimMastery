using System;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class CookingFeastProvenance
    {
        internal const string SourceKey="vm.cooking.feast.product_v1";
        internal const string EscrowSourceKey="vm.workshop.cooking.source_v1";
        internal sealed class Scope { internal Scope Previous; internal Placement Placement; }
        internal sealed class Placement
        {
            internal Player Player; internal Piece Definition, Created;
            internal string Material;
            internal CookingProductRecord Source;
            internal CookingDebitProvenance Trace;
            internal bool Successful, Paid;
        }
        [ThreadStatic] internal static Scope Current;
        internal static Scope Begin()=>Current=new Scope {Previous=Current};
        internal static void End(Scope state)
        {
            if(state==null)return;
            state.Placement?.Trace?.Dispose(); Current=state.Previous;
        }
        internal static string Material(Piece piece)
        {
            if(piece?.m_resources==null || piece.m_resources.Length!=1 || piece.GetComponent<Feast>()==null)return null;
            var cost=piece.m_resources[0]; if(cost?.m_resItem==null || cost.GetAmount(0)!=1)return null;
            string key=GatheringProgressionService.Normalize(cost.m_resItem.gameObject.name);
            return key.StartsWith("Feast",StringComparison.Ordinal) && key.EndsWith("_Material",StringComparison.Ordinal)?key:null;
        }
        internal static Placement Before(Player player,Piece piece)
        {
            string material=Material(piece);
            if(material==null || Current==null || player!=Player.m_localPlayer || player.NoCostCheat() || player.m_noPlacementCost ||
                ZoneSystem.instance?.GetGlobalKey(piece.FreeBuildKey())==true)return null;
            Current.Placement?.Trace?.Dispose();
            var state=new Placement {Player=player,Definition=piece,Material=material};
            if(WorkshopRemoteCraft.HasBuildCredit(player,piece))
            { state.Source=WorkshopRemoteCraft.BuildCookingSource(player,piece); state.Paid=state.Source!=null; }
            else
            {
                CookingDebitProvenance.Preflight(player.GetInventory(),piece.m_resources[0].m_resItem.m_itemData.m_shared.m_name);
                state.Trace=CookingDebitProvenance.Begin();
            }
            Current.Placement=state; return state;
        }
        internal static void Created(Piece piece)
        {
            var state=Current?.Placement;
            if(state!=null && piece.GetComponent<Feast>()!=null)state.Created=piece;
        }
        internal static void Success(Placement state,bool success)
        {
            if(state==null)return; state.Successful=success;
            if(success && state.Paid)Commit(state);
        }
        internal static void Payment(Player player,Piece.Requirement[] requirements)
        {
            var state=Current?.Placement;
            if(state==null || !state.Successful || state.Paid || state.Player!=player || requirements!=state.Definition.m_resources)return;
            state.Source=state.Trace.Single(state.Material); state.Paid=true; state.Trace.Dispose(); state.Trace=null; Commit(state);
        }
        private static void Commit(Placement state)
        {
            ZNetView view=state.Created?.GetComponent<ZNetView>();
            if(view?.IsOwner()!=true || state.Source==null || state.Source.Prefab!=state.Material)
                throw new InvalidOperationException("Paid feast producer could not be bound to placed object.");
            view.GetZDO().Set(SourceKey,state.Source.Encode());
            int tier=0;
            if(state.Source.Data.TryGetValue(Cooking35Service.CookMasteryKey,out string stored)) int.TryParse(stored,out tier);
            view.GetZDO().Set(Cooking70Service.FeastTierZdoKey,tier);
            view.GetZDO().Set(Cooking70Service.AuthorNameKey,state.Source.AuthorName);
            view.GetZDO().Set(Cooking70Service.FeastPrefabKey,state.Created.gameObject.name.Replace("(Clone)",""));
            Cooking70FeastRetrofit.TransferPaid(view.GetZDO(),state.Source);
        }
        internal static CookingProductRecord Read(Feast feast)
        {
            string raw=feast?.m_nview?.GetZDO()?.GetString(SourceKey,"")??"";
            return raw.Length==0?null:CookingProductRecord.Decode(raw);
        }
    }
    [HarmonyPatch(typeof(Player),"UpdatePlacement")]
    internal static class CookingFeastPlacementScopePatch
    {
        [HarmonyPriority(Priority.First)] private static void Prefix(out CookingFeastProvenance.Scope __state)=>__state=CookingFeastProvenance.Begin();
        [HarmonyPriority(Priority.Last)] private static Exception Finalizer(CookingFeastProvenance.Scope __state,Exception __exception)
        {CookingFeastProvenance.End(__state);return __exception;}
    }
    [HarmonyPatch(typeof(Player),nameof(Player.TryPlacePiece))]
    internal static class CookingFeastPlacementAcceptedPatch
    {
        [HarmonyPriority(Priority.Last)] private static bool Prefix(Player __instance,Piece piece,out CookingFeastProvenance.Placement __state,ref bool __result)
        {
            __state=null;
            try
            {
                if(piece?.GetComponent<Feast>()!=null && CookingFeastProvenance.Material(piece)==null &&
                    !__instance.NoCostCheat() && !__instance.m_noPlacementCost && ZoneSystem.instance?.GetGlobalKey(piece.FreeBuildKey())!=true)
                {__result=false;return false;}
                __state=CookingFeastProvenance.Before(__instance,piece);return true;
            }
            catch(Exception error){MasteryPlugin.Log.LogWarning("[CookingProducer] placement preflight: "+error.Message);__result=false;return false;}
        }
        private static void Postfix(CookingFeastProvenance.Placement __state,bool __result)=>CookingFeastProvenance.Success(__state,__result);
    }
    [HarmonyPatch(typeof(Piece),nameof(Piece.SetCreator))]
    internal static class CookingFeastCreatedPiecePatch
    { private static void Postfix(Piece __instance)=>CookingFeastProvenance.Created(__instance); }
    [HarmonyPatch(typeof(Player),nameof(Player.ConsumeResources))]
    internal static class CookingFeastMaterialPaidPatch
    { private static void Postfix(Player __instance,Piece.Requirement[] requirements)=>CookingFeastProvenance.Payment(__instance,requirements); }
}
