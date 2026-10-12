using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CookingFermenterOutput
    {
        internal sealed class Context
        {
            internal Context Previous;
            internal Fermenter Station;
            internal CookingFermenterReceipts.Output Receipt;
            internal CookingProductRecord NativeSource;
            internal int Next;
            internal bool Traced;
        }
        [ThreadStatic] private static Context Current;
        private static int TraceRemaining=16;
        private static void TraceProduct(Context context,ItemDrop.ItemData item)
        {
            if(context==null || context.Traced || TraceRemaining<=0)return;
            context.Traced=true;TraceRemaining--;
            try
            {
                var source=context.NativeSource ?? CookingProductRecord.Decode(context.Receipt.Source);
                source.Data.TryGetValue(CookingProductIdentity.VersionKey,out string sourceVersion);
                source.Data.TryGetValue(Cooking35Service.CookMasteryKey,out string sourceTier);
                item.m_customData.TryGetValue(CookingProductIdentity.VersionKey,out string bottleVersion);
                item.m_customData.TryGetValue(Cooking35Service.CookMasteryKey,out string bottleTier);
                MasteryPlugin.Log.LogInfo("[CookingMeadTrace] source="+source.Prefab+" version="+(sourceVersion??"none")+
                    " tier="+(sourceTier??"none")+" authorPresent="+(source.Author!=0)+
                    " bottle="+TierDatabase.ItemKey(item)+" version="+(bottleVersion??"none")+
                    " tier="+(bottleTier??"none")+" authorPresent="+(item.m_crafterID!=0)+
                    " authoredMastery="+CookingMeadTiming.HasAuthoredMastery(item));
            }
            catch { } // Diagnostic failure cannot affect a physical result.
        }
        private sealed class NativeBatch
        { internal int InputHash; internal CookingProductRecord Source; internal bool Accepted; }
        private static readonly ConditionalWeakTable<Fermenter,NativeBatch> NativeBatches=new ConditionalWeakTable<Fermenter,NativeBatch>();
        internal static bool NativeSourceActive=>Current?.NativeSource!=null;
        internal static void BeforeNativeTap(Fermenter f)
        {
            if(f?.m_nview?.IsOwner()!=true || (int)f.GetStatus()!=3)return;
            int input=f.GetContent();var conversion=f.GetItemConversion(input);
            if(input==0 || conversion?.m_from==null)return;
            string prefab=GatheringProgressionService.Normalize(conversion.m_from.gameObject.name);
            string raw=f.m_nview.GetZDO().GetString(CookingFermenterReceipts.SourceKey,"");
            var source=new CookingProductRecord{Prefab=prefab,AuthorName="",Data=new Dictionary<string,string>()};
            if(raw.Length>0)
            {
                // A malformed/stale strong record is unknown; never fall back to a
                // tapper or potentially unrelated legacy grant for another input.
                try{var stored=CookingProductRecord.Decode(raw);if(stored.Prefab==prefab)source=stored;}
                catch(FormatException){}
                catch(System.IO.IOException){}
            }
            else
            {
                source.Author=f.m_nview.GetZDO().GetLong(FermenterBonusService.AuthorKey,0);
                int tier=f.m_nview.GetZDO().GetInt(FermenterBonusService.CookingTierKey,0);
                if(tier>=35)source.Data[Cooking35Service.CookMasteryKey]=tier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            // Freeze before native RPC_Tap clears content; a following input may
            // change the station SourceKey before delayed bottles are initialized.
            NativeBatches.Remove(f);NativeBatches.Add(f,new NativeBatch{InputHash=input,Source=source});
        }
        internal static void AfterNativeTap(Fermenter f)
        {
            if(f==null || !NativeBatches.TryGetValue(f,out var batch) || batch.Accepted)return;
            if(f.GetContent()==0 && f.m_delayedTapItem==batch.InputHash)batch.Accepted=true;
            else NativeBatches.Remove(f);
        }
        internal static Context BeginNative(Fermenter f)
        {
            if(f==null || !NativeBatches.TryGetValue(f,out var batch) || !batch.Accepted ||
                f.m_delayedTapItem!=batch.InputHash)return null;
            var context=new Context{Previous=Current,Station=f,NativeSource=batch.Source};Current=context;return context;
        }
        private const string ReleasesKey="vm.cooking.output.releases_v2";
        private sealed class ReleaseProof {internal int Count;internal uint Mask;internal bool Complete=>Mask==(Count==32?uint.MaxValue:(1u<<Count)-1u);}
        private static readonly ConditionalWeakTable<ZNet,Dictionary<string,WeakReference<Fermenter>>> Stations=new ConditionalWeakTable<ZNet,Dictionary<string,WeakReference<Fermenter>>>();
        private static Dictionary<string,ReleaseProof> Releases(Fermenter f)
        {
            var result=new Dictionary<string,ReleaseProof>();
            foreach(string entry in f.m_nview.GetZDO().GetString(ReleasesKey,"").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
            {
                var fields=entry.Split(':');
                if(fields.Length!=3 || !Guid.TryParseExact(fields[0],"N",out _) || !int.TryParse(fields[1],out int count) || count<1 || count>CookingFermenterReceipts.MaxOutputs || !uint.TryParse(fields[2],out uint mask) || (count<32 && mask>=(1u<<count)) || result.Count>=128 || result.ContainsKey(fields[0]))throw new FormatException("Invalid release proof.");
                result.Add(fields[0],new ReleaseProof{Count=count,Mask=mask});
            }
            return result;
        }
        private static void SaveReleases(Fermenter f,Dictionary<string,ReleaseProof> rows)=>f.m_nview.GetZDO().Set(ReleasesKey,string.Join(";",rows.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>pair.Key+":"+pair.Value.Count+":"+pair.Value.Mask)));
        internal static bool CleanupCapacity(Fermenter f)=>Releases(f).Count<128;
        internal static bool Removable(Fermenter f)=>ProtectedStateAllows(f,false);
        // Reusing an empty station does not destroy its earned XP or cleanup
        // history. The protocol separately reserves bounded XP/output capacity.
        internal static bool CanAcceptInput(Fermenter f)=>f?.m_nview?.IsValid()==true &&
            f.GetContent()==0 && ProtectedStateAllows(f,true);
        private static bool ProtectedStateAllows(Fermenter f,bool input)
        {
            if(f?.m_nview?.IsValid()!=true)return true;
            try
            {
                // Earned XP is stored on this physical station until exact ACK.
                // Read native evidence directly: non-owner UI must also protect it.
                var zdo=f.m_nview.GetZDO();
                if(MeadXpOutbox.Read(zdo.GetString(MeadXpOutbox.Key,"")).Count>0 && !input ||
                    MeadXpOutbox.ReadDeferred(zdo.GetString(MeadXpOutbox.DeferredLoadKey,""),2)!=null ||
                    MeadXpOutbox.ReadDeferred(zdo.GetString(MeadXpOutbox.DeferredCompletionKey,""),3)!=null)return false;
                long serial=zdo.GetLong("valheim_mastery.mead_xp.serial",0),
                    pendingSerial=zdo.GetLong("valheim_mastery.mead_xp.tap_serial",0),
                    completed=zdo.GetLong("valheim_mastery.mead_xp.completed_serial",0);
                if(serial<0 || pendingSerial<0 || completed<0 || pendingSerial>serial || completed>serial || pendingSerial>completed)return false;
                var pending=Pending(f);if(pending!=null && !pending.Released ||
                    (input?Releases(f).Count>=128:Releases(f).Count>0))return false;
                foreach(string actor in f.m_nview.GetZDO().GetString("vm.cooking.fermenter.actors_v1","").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
                {
                    var row=CookingFermenterReceipts.DecodeInput(f.m_nview.GetZDO().GetString(CookingFermenterReceipts.ActorPrefix+actor,""));
                    if(row!=null && (row.State==CookingFermenterReceipts.Phase.Committing || row.State==CookingFermenterReceipts.Phase.Uncertain ||
                        row.State==CookingFermenterReceipts.Phase.Reserved && row.Deadline>=DateTime.UtcNow.Ticks))return false;
                }
                return true;
            }
            catch{return false;}
        }
        private static void RegisterStation(Fermenter f)
        {if(ZNet.instance!=null && MeadStationIdentity.TryGet(f.m_nview.GetZDO(),ZNet.instance.GetWorldUID(),out string token))Stations.GetOrCreateValue(ZNet.instance)[token]=new WeakReference<Fermenter>(f);}

        private sealed class Retry {internal float Last;}
        private static readonly ConditionalWeakTable<Fermenter,Retry> Retries=new ConditionalWeakTable<Fermenter,Retry>();
        internal static CookingFermenterReceipts.Output Pending(Fermenter f)=>CookingFermenterReceipts.DecodeOutput(f?.m_nview?.GetZDO()?.GetString(CookingFermenterReceipts.PendingKey,"")??"");
        private static void Save(Fermenter f,CookingFermenterReceipts.Output row)=>f.m_nview.GetZDO().Set(CookingFermenterReceipts.PendingKey,CookingFermenterReceipts.Encode(row));
        internal static CookingFermenterReceipts.Output BeforeTap(Fermenter f)
        {
            if(!CookingFermenterProtocol.NativeTapAllowed || !CookingFermenterProtocol.CanTap(f) || (int)f.GetStatus()!=3)return null;
            var conversion=f.GetItemConversion(f.GetContent());
            if(conversion?.m_to==null || conversion.m_producedItems<1)return null;
            if(!MeadStationIdentity.TryEnsure(f.m_nview.GetZDO(),ZNet.instance.GetWorldUID(),out string station))return null;
            string raw=f.m_nview.GetZDO().GetString(CookingFermenterReceipts.SourceKey,"");
            if(raw.Length==0)
            {
                // Preserve existing stored legacy attribution. Never infer producer
                // from the tapper/loader's present perks for unknown historical input.
                int tier=f.m_nview.GetZDO().GetInt(FermenterBonusService.CookingTierKey,0);
                var legacy=new CookingProductRecord{Prefab=GatheringProgressionService.Normalize(conversion.m_from.gameObject.name),
                    Author=f.m_nview.GetZDO().GetLong(FermenterBonusService.AuthorKey,0),AuthorName="",Data=new Dictionary<string,string>()};
                if(tier>=35)legacy.Data[Cooking35Service.CookMasteryKey]=tier.ToString();
                raw=legacy.Encode();
            }
            var xp=MeadStageExperience.BeforeTap(f);if(xp==null || xp.Blocked || xp.Serial<=0)return null;
            bool bonus=FermenterBonusService.PendingBonus(f);int count=conversion.m_producedItems+(bonus?1:0);
            if(count>CookingFermenterReceipts.MaxOutputs)return null;
            var row=new CookingFermenterReceipts.Output{Station=station,Id=Guid.NewGuid().ToString("N"),
                Batch=f.m_nview.GetZDO().GetString(CookingFermenterReceipts.BatchKey,""),Source=raw,
                InputHash=f.GetContent(),Prefab=GatheringProgressionService.Normalize(conversion.m_to.name),Count=count,
                Bonus=bonus,XpSerial=xp.Serial,Actor=CookingFermenterProtocol.ActiveTapActor,
                Due=DateTime.UtcNow.Ticks+TimeSpan.FromSeconds(Math.Max(0f,f.m_tapDelay)).Ticks,
                Cheated=f.m_nview.GetZDO().GetBool("vm.cooking.fermenter.cheated",false),Slots=new byte[count]};
            Save(f,row);return row;
        }
        internal static void AfterTap(Fermenter f,CookingFermenterReceipts.Output state)
        {
            if(state==null){AfterNativeTap(f);return;}
            if(f.GetContent()!=0 || f.m_delayedTapItem!=state.InputHash)
            {f.m_nview.GetZDO().Set(CookingFermenterReceipts.PendingKey,"");return;}
            state.Accepted=true;state.Cheated=f.m_delayedTapItemCheated;
            MeadStageExperience.ConfirmAcceptedTap(f,MeadStageExperience.EventIdentity(f,state.XpSerial,3),state.XpSerial,state.InputHash,state.Actor);
            Save(f,state);
        }
        internal static Context Begin(Fermenter f)
        {
            var row=Pending(f);
            if(!CookingFermenterProtocol.Owner(f) || row?.Accepted!=true || row.Released || f.m_delayedTapItem!=row.InputHash)return null;
            var context=new Context{Previous=Current,Station=f,Receipt=row};Current=context;
            FermenterBonusService.RestorePendingBonus(f,row.Bonus);
            return context;
        }
        internal static void End(Context context,bool successful)
        {
            if(context==null)return;
            try
            {
                if(context.NativeSource!=null)
                {
                    if(NativeBatches.TryGetValue(context.Station,out var batch) && ReferenceEquals(batch.Source,context.NativeSource))
                        NativeBatches.Remove(context.Station);
                }
                else if(successful && context.Receipt.Complete)Release(context.Station,context.Receipt);
            }
            finally{Current=context.Previous;}
        }
        // Same stack contract as the one native Instantiate<ItemDrop> in DelayedTap.
        internal static ItemDrop Instantiate(ItemDrop prefab,Vector3 point,Quaternion rotation)
        {
            var context=Current;
            // The native tap fallback deliberately has no custom output receipt.
            // Existing pending receipts are still blocked before DelayedTap, so a
            // null context here is safe native output rather than blind recovery.
            if(context==null)return UnityEngine.Object.Instantiate(prefab,point,rotation);
            if(context.NativeSource!=null)return UnityEngine.Object.Instantiate(prefab,point,rotation);
            int index=context.Next++;
            if(index>=context.Receipt.Count)throw new InvalidOperationException("Native output exceeds reserved result count.");
            byte status=context.Receipt.Slots[index];
            if(status==2)return null; // Completed tombstones survive pickup, merge and destruction.
            if(status==1)throw new InvalidOperationException("Ambiguous issued output requires reconciliation; no blind respawn.");
            CookingFermenterReceipts.Issue(context.Receipt,index);Save(context.Station,context.Receipt);
            ItemDrop drop=UnityEngine.Object.Instantiate(prefab,point,rotation);
            ItemDrop.OnCreateNew(drop,context.Receipt.Cheated);
            CookingProductRecord.Decode(context.Receipt.Source).Apply(drop.m_itemData);
            drop.m_itemData.m_customData[CookingFermenterReceipts.OutputIdKey]=context.Receipt.SlotId(index);
            drop.m_itemData.m_customData[CookingFermenterReceipts.PendingItemKey]=context.Receipt.SlotId(index);
            drop.Save();
            CookingFermenterReceipts.CompleteSlot(context.Receipt,index);Save(context.Station,context.Receipt);
            return drop;
        }
        internal static void Initialize(ItemDrop drop,bool cheated)
        {
            if(drop==null)return;
            ItemDrop.OnCreateNew(drop,cheated);
            if(Current?.NativeSource!=null)
            {
                Current.NativeSource.Apply(drop.m_itemData);
                drop.Save();
            }
            TraceProduct(Current,drop.m_itemData);
        }
        internal static bool Held(ItemDrop drop)
        {
            if(drop?.m_itemData?.m_customData?.ContainsKey(CookingFermenterReceipts.PendingItemKey)!=true)return false;
            // Native revision-aware load reads replicated cleanup even when the
            // station's temporary release history has already been retired.
            drop.Load();
            if(!drop.m_itemData.m_customData.TryGetValue(CookingFermenterReceipts.PendingItemKey,out string id))return false;
            if(ZNet.instance!=null)
            {
                var fields=id.Split('/');
                if(fields.Length==3 && Stations.GetOrCreateValue(ZNet.instance).TryGetValue(fields[0],out var reference) && reference.TryGetTarget(out var f) && f?.m_nview?.IsValid()==true)
                {
                    var proofs=Releases(f);
                    if(proofs.TryGetValue(fields[1],out var proof) && int.TryParse(fields[2],out int slot) && slot>=0 && slot<proof.Count)
                    {
                        // Clients wait for saved replicated cleanup; presence of
                        // station proof alone never permits an unacknowledged pickup.
                        if(!CookingFermenterProtocol.Owner(f) || drop.m_nview?.IsValid()!=true)return true;
                        if(!drop.m_nview.IsOwner())drop.m_nview.GetZDO().SetOwner(ZNet.GetUID());
                        if(!drop.m_nview.IsOwner())return true;
                        drop.m_itemData.m_customData.Remove(CookingFermenterReceipts.PendingItemKey);drop.Save();
                        proof.Mask|=1u<<slot;if(proof.Complete)proofs.Remove(fields[1]);SaveReleases(f,proofs);return false;
                    }
                }
            }
            return true;
        }
        private static bool Release(Fermenter f,CookingFermenterReceipts.Output row)
        {
            if(!row.Complete)return false;
            if(row.XpSerial<=0 || !MeadStageExperience.ConfirmAcceptedTap(f,MeadStageExperience.EventIdentity(f,row.XpSerial,3),row.XpSerial,row.InputHash,row.Actor) ||
                !MeadStageExperience.ConfirmAcceptedOutput(f,MeadStageExperience.EventIdentity(f,row.XpSerial,3)))return false;
            // Durable original slot proofs drive the XP owner's sole commit path.
            var proofs=Releases(f);if(!proofs.ContainsKey(row.Id) && proofs.Count>=128)return false;
            if(!proofs.ContainsKey(row.Id))proofs[row.Id]=new ReleaseProof{Count=row.Count};SaveReleases(f,proofs);
            row.Released=true;Save(f,row);Cleanup(row);CleanupReleased(f);
            return true;
        }
        private static void Cleanup(CookingFermenterReceipts.Output row)
        {
            foreach(var drop in ItemDrop.s_instances)
                if(drop!=null && drop.m_itemData.m_customData.TryGetValue(CookingFermenterReceipts.PendingItemKey,out string id) &&
                    id.StartsWith(row.Station+"/"+row.Id+"/",StringComparison.Ordinal))
                {
                    // Station ownership does not imply item ownership. Claim the
                    // original drop before clearing its persisted hold.
                    if(drop.m_nview?.IsValid()!=true)continue;
                    if(!drop.m_nview.IsOwner())drop.m_nview.GetZDO().SetOwner(ZNet.GetUID());
                    if(!drop.m_nview.IsOwner())continue;
                    drop.m_itemData.m_customData.Remove(CookingFermenterReceipts.PendingItemKey);drop.Save();
                }
        }
        private static bool ReconcileIssued(Fermenter f,CookingFermenterReceipts.Output row)
        {
            var source=CookingProductRecord.Decode(row.Source);
            for(int i=0;i<row.Count;i++)
            {
                if(row.Slots[i]!=1)continue;
                string id=row.SlotId(i);
                var matches=ItemDrop.s_instances.Where(drop=>drop!=null && drop.m_itemData.m_customData.TryGetValue(CookingFermenterReceipts.OutputIdKey,out string found) && found==id).ToList();
                // Missing or conflicting issued output stays quarantined. Never reissue.
                if(matches.Count!=1)return false;
                var item=matches[0].m_itemData;
                if(item.m_stack!=1 || TierDatabase.ItemKey(item)!=row.Prefab || item.m_crafterID!=source.Author || item.m_crafterName!=source.AuthorName ||
                    !item.m_customData.TryGetValue(CookingFermenterReceipts.PendingItemKey,out string held) || held!=id ||
                    source.Data.Any(pair=>!item.m_customData.TryGetValue(pair.Key,out string value) || value!=pair.Value))return false;
                CookingFermenterReceipts.CompleteSlot(row,i);Save(f,row);
            }
            return true;
        }
        private static void CleanupReleased(Fermenter f)
        {
            var proofs=Releases(f);if(proofs.Count==0)return;
            if(!MeadStationIdentity.TryGet(f.m_nview.GetZDO(),ZNet.instance.GetWorldUID(),out string token))return;
            foreach(var row in proofs.ToArray())
            {
                string prefix=token+"/"+row.Key+"/";
                
                foreach(var drop in ItemDrop.s_instances)
                {
                    if(drop==null || !drop.m_itemData.m_customData.TryGetValue(CookingFermenterReceipts.OutputIdKey,out string id) || !id.StartsWith(prefix,StringComparison.Ordinal) ||
                        !int.TryParse(id.Substring(prefix.Length),out int index) || index<0 || index>=row.Value.Count)continue;
                    if(drop.m_nview?.IsValid()!=true)continue;
                    if(!drop.m_nview.IsOwner())drop.m_nview.GetZDO().SetOwner(ZNet.GetUID());
                    if(!drop.m_nview.IsOwner())continue;
                    drop.m_itemData.m_customData.Remove(CookingFermenterReceipts.PendingItemKey);drop.Save();
                    row.Value.Mask|=1u<<index;
                    SaveReleases(f,proofs);
                }
                // Persist per-slot cleanup acknowledgement; pickup/merge afterwards cannot leak history.
                if(row.Value.Complete)proofs.Remove(row.Key);
            }
            SaveReleases(f,proofs);
        }
        internal static void Recover(Fermenter f)
        {
            if(f?.m_nview?.IsValid()!=true)return;RegisterStation(f);
            if(!CookingFermenterProtocol.Owner(f))return;
            var retry=Retries.GetOrCreateValue(f);if(Time.time-retry.Last<2f)return;retry.Last=Time.time;
            var row=Pending(f);if(row==null)return;
            try
            {
                CleanupReleased(f);
                if(row.Released){Cleanup(row);return;}
                if(!row.Accepted)
                {
                    // Original provisional tap plus native content removal is acceptance proof;
                    // no timer-only completion and no repeated RPC_Tap.
                    if(f.GetContent()!=0 || MeadStageExperience.BatchSerial(f)!=row.XpSerial ||
                        f.m_nview.GetZDO().GetString(CookingFermenterReceipts.BatchKey,"")!=row.Batch ||
                        !MeadStageExperience.ConfirmAcceptedTap(f,MeadStageExperience.EventIdentity(f,row.XpSerial,3),row.XpSerial,row.InputHash,row.Actor))return;
                    row.Accepted=true;Save(f,row);
                }
                if(!ReconcileIssued(f,row))return;
                if(row.Complete){MeadXpTransport.Drain(f,true);Release(f,row);return;}
                if(DateTime.UtcNow.Ticks<row.Due)return; // Uncertain issue remains durable, never reissued.
                f.m_delayedTapItem=row.InputHash;f.m_delayedTapItemCheated=row.Cheated;
                f.DelayedTap();
            }
            catch(Exception error){MasteryPlugin.Log.LogWarning("[CookingProducer] output recovery: "+error.Message);}
        }
    }
    [HarmonyPatch(typeof(Fermenter),"RPC_Tap")]
    internal static class CookingProducerTapReceiptPatch
    {
        [HarmonyPriority(Priority.Last)]private static bool Prefix(Fermenter __instance,out CookingFermenterReceipts.Output __state)
        {
            __state=null;
            if(!MasteryPlugin.Settings.Enabled.Value)return CookingFermenterOutput.Removable(__instance);
            if(!CookingFermenterProtocol.NativeTapAllowed)
            {
                if(CookingFermenterOutput.Pending(__instance)!=null)return false;
                CookingFermenterOutput.BeforeNativeTap(__instance);return true;
            }
            __state=CookingFermenterOutput.BeforeTap(__instance);return __state!=null;
        }
        [HarmonyPriority(Priority.Last)]private static void Postfix(Fermenter __instance,CookingFermenterReceipts.Output __state)=>CookingFermenterOutput.AfterTap(__instance,__state);
    }
    [HarmonyPatch(typeof(Fermenter),nameof(Fermenter.DelayedTap))]
    internal static class CookingProducerOutputReceiptPatch
    {
        [HarmonyPriority(Priority.First)]private static bool Prefix(Fermenter __instance,out CookingFermenterOutput.Context __state)
        {
            __state=null;
            if(!MasteryPlugin.Settings.Enabled.Value)return CookingFermenterOutput.Removable(__instance);
            if(CookingFermenterOutput.Pending(__instance)==null)
            {__state=CookingFermenterOutput.BeginNative(__instance);return true;}
            __state=CookingFermenterOutput.Begin(__instance);return __state!=null;
        }
        [HarmonyPriority(Priority.Last)]private static Exception Finalizer(CookingFermenterOutput.Context __state,Exception __exception)
        {CookingFermenterOutput.End(__state,__exception==null);return __exception;}
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int creates=0,initializers=0;var code=instructions.ToList();
            var nativeInit=AccessTools.Method(typeof(ItemDrop),nameof(ItemDrop.OnCreateNew),new[]{typeof(ItemDrop),typeof(bool)});
            foreach(var instruction in code)
            {
                if(instruction.operand is MethodInfo call && call.DeclaringType==typeof(UnityEngine.Object) && call.Name=="Instantiate" &&
                    call.IsGenericMethod && call.GetGenericArguments().Length==1 && call.GetGenericArguments()[0]==typeof(ItemDrop) && call.GetParameters().Length==3)
                {creates++;instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(CookingFermenterOutput),nameof(CookingFermenterOutput.Instantiate));}
                if(instruction.Calls(nativeInit))
                {initializers++;instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(CookingFermenterOutput),nameof(CookingFermenterOutput.Initialize));}
            }
            if(creates!=1 || initializers!=1)throw new InvalidOperationException("Native delayed output creation contract changed.");
            return code;
        }
    }
    [HarmonyPatch(typeof(Fermenter),"SlowUpdate")]
    internal static class CookingProducerOutputRecoveryPatch
    {private static void Postfix(Fermenter __instance)=>CookingFermenterOutput.Recover(__instance);}
    [HarmonyPatch(typeof(WearNTear),"CanBeRemoved")]
    internal static class CookingProducerStationRemovalGuard
    {private static void Postfix(WearNTear __instance,ref bool __result){if(!CookingFermenterOutput.Removable(__instance.GetComponent<Fermenter>()))__result=false;}}
    [HarmonyPatch(typeof(WearNTear),"Destroy")]
    internal static class CookingProducerStationDestroyGuard
    {private static bool Prefix(WearNTear __instance)=>CookingFermenterOutput.Removable(__instance.GetComponent<Fermenter>());}
    [HarmonyPatch(typeof(ItemDrop),"CanPickup")]
    internal static class CookingProducerOutputPickupGuard
    {private static void Postfix(ItemDrop __instance,ref bool __result){if(CookingFermenterOutput.Held(__instance))__result=false;}}
    [HarmonyPatch(typeof(ItemDrop),"Pickup")]
    internal static class CookingProducerOutputManualPickupGuard
    {private static bool Prefix(ItemDrop __instance)=>!CookingFermenterOutput.Held(__instance);}
}
