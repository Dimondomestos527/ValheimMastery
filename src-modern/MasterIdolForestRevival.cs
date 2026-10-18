using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static partial class MasterIdolForestGuardState
    {
        private static readonly Dictionary<int,ForestBirthClaim> BirthClaims=new Dictionary<int,ForestBirthClaim>();
        private static readonly List<int> ExpiredBirths=new List<int>(4);
        internal static void RevivalWarning(string message)
        {try{MasteryPlugin.Log?.LogWarning(message);}catch(Exception){/* Reporting cannot reverse applied birth or interrupt native shutdown. */}}
        private static bool SameBefore(MasterIdolForestGuardRecord current,MasterIdolForestGuardRecord snapshot)
            =>current==null?snapshot==null:snapshot!=null&&current.Home==snapshot.Home&&current.Family==snapshot.Family&&current.Uid==snapshot.Uid&&current.Nonce==snapshot.Nonce&&
                current.Born==snapshot.Born&&current.Death==snapshot.Death&&current.Respawn==snapshot.Respawn&&current.Name==snapshot.Name&&current.Valid();
        private static bool NativeDay(out int day)
        {
            day=-1;var env=EnvMan.instance;double now=Session?.GetTimeSeconds()??double.NaN;
            if(env==null||env.m_dayLengthSec<=0||!double.IsFinite(now)||now<0||now/env.m_dayLengthSec>int.MaxValue)return false;
            day=env.GetDay();return day>=0;
        }
        private static bool LivingShaman(MasterIdolEffectZone zone,out MasterIdolForestGuardRecord record,out ZDO data)
        {
            data=null;
            if(!Store.Guards.TryGetValue(MasterIdolForestRules.Families[3],out record)||record.Death!=0||!record.Valid()||!MasterIdolNativeTaming.SameHome(record.Home,zone.Id))return false;
            data=Data(record.Uid);float health=data?.GetFloat(ZDOVars.s_health,1)??float.NaN;
            return Matches(record,data)&&MasterIdolNativeTaming.SameHome(data.GetString(MasterIdolNativeTaming.HomeKey,""),zone.Id)&&float.IsFinite(health)&&health>0;
        }
        private static bool BirthContext(ForestBirthClaim claim)
            =>claim!=null&&!claim.Completed&&!claim.Released&&claim.Store==Store&&claim.Net==Session&&claim.Manager==Manager&&claim.Generation==Generation&&claim.World==World&&
                Session==ZNet.instance&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&Session?.IsServer()==true&&Session.GetWorld()!=null&&Session.GetWorldUID()==World&&
                Time.unscaledTime<=claim.Until&&float.IsFinite(Time.unscaledTime);
        private static void PruneBirthClaims()
        {
            ExpiredBirths.Clear();
            foreach(var pair in BirthClaims)if(!pair.Value.Creating&&!BirthContext(pair.Value))ExpiredBirths.Add(pair.Key);
            foreach(int family in ExpiredBirths){BirthClaims[family].Released=true;BirthClaims.Remove(family);}
        }
        private static int Reserved(MasterIdolRevivalQuota quota)
        {
            int count=0;foreach(var claim in BirthClaims.Values)if(!claim.Completed&&!claim.Released&&claim.Cause==ForestBirthCause.ShamanRevival&&claim.Quota==quota)count++;return count;
        }
        internal static bool TryBirthClaim(MasterIdolEffectZone zone,int family,MasterIdolForestGuardRecord previous,out ForestBirthClaim claim)
        {
            claim=null;if(!Ensure()||zone?.Type!="BlackForest"||family<0||family>3||!MasterIdolForestRules.Home(zone.Id))return false;
            PruneBirthClaims();if(BirthClaims.ContainsKey(family))return false;
            Store.Guards.TryGetValue(MasterIdolForestRules.Families[family],out var current);
            if(!ReferenceEquals(current,previous)||previous!=null&&(!previous.Valid()||previous.Death==0))return false;
            string token=MasterIdolNativeTaming.Token(zone.Id);if(token==null||previous!=null&&!MasterIdolNativeTaming.SameHome(previous.Home,zone.Id))return false;
            var replay=MasterIdolGuardBirthReplay.BirthAdmission(zone,family,previous,out var replayNonce);if(replay==ReplayBirthAdmission.Hold)return false;
            ForestBirthCause cause;int day=-1;MasterIdolRevivalQuota quota=null;MasterIdolForestGuardRecord shaman=null;ZDO shamanData=null;
            if(replay==ReplayBirthAdmission.Ready)cause=ForestBirthCause.ExplicitReplay;
            else if(previous==null)cause=ForestBirthCause.Initial;
            else if(family==3){if(Session.GetTime().Ticks<previous.Respawn)return false;cause=ForestBirthCause.ShamanTimer;}
            else
            {
                if(!LivingShaman(zone,out shaman,out shamanData)||!NativeDay(out day))return false;
                if(!Store.Quotas.TryGetValue(token,out quota))
                {
                    if(Store.Quotas.Count>=512)return false;
                    quota=new MasterIdolRevivalQuota{Token=token,Day=day};Store.Quotas.Add(token,quota);Dirty=true;
                }
                if(day<quota.Day)return false;
                if(day>quota.Day){if(Reserved(quota)>0)return false;quota.Day=day;quota.Used=0;Dirty=true;}
                if(quota.Used+Reserved(quota)>=2)return false;cause=ForestBirthCause.ShamanRevival;
            }
            claim=new ForestBirthClaim{Net=Session,Manager=Manager,Generation=Generation,World=World,Store=Store,Home=zone.Id,Token=token,Nonce=Guid.NewGuid().ToString("N"),Family=family,
                Before=previous,Snapshot=previous?.Copy(),Cause=cause,ReplayNonce=replayNonce,Day=day,Quota=quota,Until=Time.unscaledTime+6,
                Shaman=shaman,ShamanData=shamanData,ShamanOwner=shamanData?.GetOwner()??0,ShamanRevision=shamanData?.OwnerRevision??0};
            BirthClaims.Add(family,claim);return true;
        }
        internal static bool BirthValid(ForestBirthClaim claim,MasterIdolEffectZone zone,MasterIdolForestGuardRecord previous)
        {
            if(!Ensure()||!BirthContext(claim)||!BirthClaims.TryGetValue(claim.Family,out var reserved)||!ReferenceEquals(reserved,claim)||zone?.Id!=claim.Home||
                zone.Type!="BlackForest"||MasterIdolNativeTaming.Token(zone.Id)!=claim.Token||!ReferenceEquals(previous,claim.Before)||!SameBefore(previous,claim.Snapshot))return false;
            Store.Guards.TryGetValue(MasterIdolForestRules.Families[claim.Family],out var current);if(!ReferenceEquals(current,previous))return false;
            var replay=MasterIdolGuardBirthReplay.BirthAdmission(zone,claim.Family,previous,out var nonce);
            if(claim.Cause==ForestBirthCause.ExplicitReplay)return replay==ReplayBirthAdmission.Ready&&nonce==claim.ReplayNonce;
            if(replay!=ReplayBirthAdmission.None)return false;
            if(claim.Cause==ForestBirthCause.Initial)return previous==null;
            if(previous==null||previous.Death==0)return false;
            if(claim.Cause==ForestBirthCause.ShamanTimer)return claim.Family==3&&Session.GetTime().Ticks>=previous.Respawn;
            return claim.Cause==ForestBirthCause.ShamanRevival&&claim.Family<3&&NativeDay(out int day)&&day==claim.Day&&Store.Quotas.TryGetValue(claim.Token,out var quota)&&ReferenceEquals(quota,claim.Quota)&&
                quota.Day==day&&quota.Used+Reserved(quota)<=2&&LivingShaman(zone,out var shaman,out var data)&&ReferenceEquals(shaman,claim.Shaman)&&ReferenceEquals(data,claim.ShamanData)&&
                data.GetOwner()==claim.ShamanOwner&&data.OwnerRevision==claim.ShamanRevision;
        }
        internal static bool CompleteBirth(ForestBirthClaim claim,MasterIdolForestGuardRecord record)
        {
            if(claim==null)return false;if(claim.Completed)return true;
            // Native success is irreversible here. Never call BirthValid after replacing the old slot.
            if(record==null||claim.Store==null||!claim.Store.Guards.TryGetValue(record.Family,out var current)||!ReferenceEquals(current,record)||
                record.Family!=MasterIdolForestRules.Families[claim.Family]||record.Uid==claim.Before?.Uid||!record.Valid()||MasterIdolNativeTaming.Token(record.Home)!=claim.Token)return false;
            if(claim.Cause==ForestBirthCause.ShamanRevival)
            {
                // Reservation prevents a third applied success; a writer fault cannot refund this counter.
                if(claim.Quota==null||claim.Quota.Used>=2)return false;
                claim.Quota.Used++;
            }
            claim.Completed=true;claim.Creating=false;
            if(BirthClaims.TryGetValue(claim.Family,out var reserved)&&ReferenceEquals(reserved,claim))BirthClaims.Remove(claim.Family);
            if(ReferenceEquals(Store,claim.Store))Dirty=true;
            else RevivalWarning("[GuardRevival] Applied birth belongs to a closed context; occupied record and quota retained, persistence unconfirmed");
            return true;
        }
        internal static void ReleaseBirth(ForestBirthClaim claim)
        {
            if(claim==null||claim.Completed||claim.Released)return;
            claim.Released=true;claim.Creating=false;
            if(BirthClaims.TryGetValue(claim.Family,out var reserved)&&ReferenceEquals(reserved,claim))BirthClaims.Remove(claim.Family);
        }
        internal static void Close()
        {
            var closing=Store;if(closing==null)return;
            Store=null;
            try
            {
                if(!closing.FlushLatest(LegacyTimers,LegacyMembers,closing.Guards,closing.Quotas))
                    RevivalWarning("[GuardRevival] Resident snapshot final flush failed; committed file preserved, latest persistence unconfirmed");
            }
            catch(Exception error){RevivalWarning("[GuardRevival] Resident final flush failed: "+error.Message);}
            finally
            {
                closing.Retire();foreach(var claim in BirthClaims.Values){claim.Released=true;claim.Creating=false;}BirthClaims.Clear();
                Session=null;Manager=null;Generation=0;World=0;Dirty=false;Witnesses.Clear();Pending.Clear();
            }
        }
    }
    [HarmonyPatch(typeof(ZNet),"Shutdown",new[]{typeof(bool)})]
    internal static class MasterIdolForestClosePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()=>MasterIdolForestGuardState.Close();
    }
}
