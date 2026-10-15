using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Compatibility name, now a bounded one-actor idle rotation. No pairs, props, paths or pause ZDOs.
    internal static class MasterIdolSocialScenes
    {
        private sealed class Slot
        { internal MasterIdolSocial Actor; internal ZDO Data,ReactionData; internal Player ReactionPlayer;internal ushort ReactionRevision;internal int ReactionId; internal long Owner; internal ushort Revision; internal float Until, Next, Keep; internal int Cursor = -1, Turn; }
        private static readonly Dictionary<string, Slot> Homes = new Dictionary<string, Slot>(StringComparer.Ordinal);
        private static readonly List<MasterIdolSocial> Candidates = new List<MasterIdolSocial>(8);
        private static readonly List<string> Stale = new List<string>(64);
        private static ZNet Session; private static ZDOMan Manager; private static ulong Generation; private static long World; private static float PruneAt;
        private static void Context()
        {
            long world = ZNet.instance?.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
            if (Session == ZNet.instance && Manager == ZDOMan.instance && Generation == MasterIdolLoadIdentity.Generation && World == world) return;
            foreach (var slot in Homes.Values) End(slot, false);
            Homes.Clear(); Candidates.Clear(); Session = ZNet.instance; Manager = ZDOMan.instance; Generation = MasterIdolLoadIdentity.Generation; World = world; PruneAt = 0;
        }
        private static bool Valid(string home, Slot slot)
        {
            var actor = slot.Actor; var data = actor?.SceneView?.GetZDO();
            return MasterIdolLoadIdentity.LoadReady && data != null && actor != null && actor.SceneAvailable && ReferenceEquals(data, slot.Data) && ReferenceEquals(Manager?.GetZDO(data.m_uid), data) &&
                data.GetOwner() == slot.Owner && data.OwnerRevision == slot.Revision && MasterIdolResidentIdleRules.Current(Time.unscaledTime, slot.Until) &&
                MasterIdolResidentPolicy.TryHome(actor.SceneCreature, out var zone) && zone.Id == home &&
                (slot.ReactionPlayer!=null?MasterIdolResidentEmote.Current(slot.ReactionPlayer,slot.ReactionData,slot.ReactionRevision,slot.ReactionId)&&
                    zone.Contains(slot.ReactionPlayer.transform.position)&&(actor.transform.position-slot.ReactionPlayer.transform.position).sqrMagnitude<=64&&
                    MasterIdolResidentPolicy.Friendly(actor.SceneCreature,slot.ReactionPlayer):MasterIdolResidentVisibility.Observed(actor.SceneCreature));
        }
        private static void End(Slot slot, bool gap)
        {
            var actor = slot.Actor; slot.Actor = null; slot.Data = slot.ReactionData = null;slot.ReactionPlayer=null;
            if (gap) slot.Next = Time.unscaledTime + MasterIdolResidentIdleRules.GapSeconds;
            if (actor != null) { try { MasterIdolVisibleGesture.Cancel(actor.SceneCreature); } catch (Exception) { MasterIdolResidentMetrics.Count("idle.cleanup-failed"); } }
        }
        internal static void Tick()
        {
            Context(); if (Homes.Count == 0) return;
            Stale.Clear();
            foreach (var pair in Homes)
            {
                if (pair.Value.Actor != null && !Valid(pair.Key, pair.Value)) End(pair.Value, true);
                if (Time.unscaledTime >= PruneAt && pair.Value.Actor == null && pair.Value.Keep <= Time.unscaledTime) Stale.Add(pair.Key);
            }
            foreach (string key in Stale) Homes.Remove(key);
            if (Time.unscaledTime >= PruneAt) PruneAt = Time.unscaledTime + 5;
        }
        internal static bool Active(MasterIdolSocial actor)
        { Context(); if (actor == null || !MasterIdolResidentPolicy.TryHome(actor.SceneCreature, out var home)) return false; return Homes.TryGetValue(home.Id, out var slot) && ReferenceEquals(slot.Actor, actor) && Valid(home.Id, slot); }
        internal static void Cancel(MasterIdolSocial actor)
        { Context(); if (actor == null) return; foreach (var slot in Homes.Values) if (ReferenceEquals(slot.Actor, actor)) { End(slot, true); return; } }
        internal static void CancelHome(string home)
        { Context(); if (home != null && Homes.TryGetValue(home, out var slot) && slot.Actor != null) End(slot, true); }
        internal static bool React(MasterIdolSocial actor,Player player)
        {
            Context();
            if(actor==null||player==null||!actor.SceneAvailable||!MasterIdolResidentPolicy.TryHome(actor.SceneCreature,out var home)||
                !home.Contains(player.transform.position)||!MasterIdolResidentPolicy.Friendly(actor.SceneCreature,player)||
                (actor.transform.position-player.transform.position).sqrMagnitude>64||
                MasterIdolResidentSceneAnimation.HomeActive(home))return false;
            if(!Homes.TryGetValue(home.Id,out var slot))
            {if(Homes.Count>=MasterIdolResidentIdleRules.MaxHomeRecords)return false;Homes.Add(home.Id,slot=new Slot());}
            int active=0;foreach(var value in Homes.Values)if(value.Actor!=null&&value!=slot)active++;
            if(active>=MasterIdolResidentIdleRules.MaxActiveHomes)return false;
            if(slot.Actor!=null)End(slot,false);
            slot.Actor=actor;slot.Data=actor.SceneView.GetZDO();slot.Owner=slot.Data.GetOwner();slot.Revision=slot.Data.OwnerRevision;
            slot.ReactionPlayer=player;slot.ReactionData=player.m_nview.GetZDO();slot.ReactionRevision=slot.ReactionData.OwnerRevision;slot.ReactionId=slot.ReactionData.GetInt(ZDOVars.s_emoteID,0);
            slot.Until=Time.unscaledTime+MasterIdolResidentIdleRules.SlotSeconds;slot.Keep=slot.Until+30;
            actor.SceneAi.StopMoving();actor.SceneAi.LookAt(player.transform.position);
            try
            {
                if(MasterIdolVisibleGesture.TryStart(actor.SceneCreature,true,MasterIdolResidentIdleRules.SlotSeconds,true))return true;
            }
            catch(Exception){MasterIdolResidentMetrics.Count("emote.start-failed");}
            End(slot,false);return false;
        }
        internal static bool Idle(MasterIdolSocial actor, MasterIdolEffectZone zone, float dt, bool mayStart)
        {
            Context(); if (actor == null || zone == null || World == 0 || !MasterIdolLoadIdentity.LoadReady) return false;
            if (!Homes.TryGetValue(zone.Id, out var slot))
            { if (Homes.Count >= MasterIdolResidentIdleRules.MaxHomeRecords) return false; Homes.Add(zone.Id, slot = new Slot()); }
            slot.Keep = Time.unscaledTime + 30;
            if (slot.Actor != null && !Valid(zone.Id, slot)) End(slot, true);
            if (slot.Actor == null && mayStart && MasterIdolResidentIdleRules.Due(Time.unscaledTime, slot.Next))
            {
                slot.Next = Time.unscaledTime + MasterIdolResidentIdleRules.ElectionCadence;
                int active = 0; foreach (var value in Homes.Values) if (value.Actor != null) active++;
                if (!MasterIdolResidentSceneAnimation.HomeActive(zone) && active < MasterIdolResidentIdleRules.MaxActiveHomes && zone.Residents.Count <= MasterIdolResidentIdleRules.MaxResidents)
                {
                    Candidates.Clear();
                    foreach (var member in zone.Residents)
                    {
                        if (!MasterIdolNetworkQuota.TryUid(member.Key, out long user, out uint id)) continue;
                        var candidate = ZNetScene.instance?.FindInstance(new ZDOID(user, id))?.GetComponent<MasterIdolSocial>();
                        if (candidate != null && candidate.SceneAvailable && MasterIdolResidentPolicy.TryHome(candidate.SceneCreature, out var home) && home.Id == zone.Id && MasterIdolResidentVisibility.Observed(candidate.SceneCreature)) Candidates.Add(candidate);
                    }
                    Candidates.Sort((left, right) => string.CompareOrdinal(left.SceneView.GetZDO().m_uid.ToString(), right.SceneView.GetZDO().m_uid.ToString()));
                    slot.Cursor = MasterIdolResidentIdleRules.Next(slot.Cursor, Candidates.Count);
                    if (slot.Cursor >= 0)
                    {
                        slot.Actor = Candidates[slot.Cursor]; slot.Data = slot.Actor.SceneView.GetZDO(); slot.Owner = slot.Data.GetOwner(); slot.Revision = slot.Data.OwnerRevision;
                        slot.Until = Time.unscaledTime + MasterIdolResidentIdleRules.SlotSeconds;
                        try
                        {
                            MasterIdolVisibleGesture.Start(slot.Actor.SceneCreature, MasterIdolResidentIdleRules.Vibe(Utils.GetPrefabName(slot.Actor.gameObject), slot.Turn++), MasterIdolResidentIdleRules.SlotSeconds);
                            MasterIdolResidentMetrics.Count("idle.rotation-start");
                        }
                        catch (Exception) { End(slot, true); MasterIdolResidentMetrics.Count("idle.start-failed"); }
                    }
                }
            }
            if (!ReferenceEquals(slot.Actor, actor) || !Valid(zone.Id, slot)) return false;
            actor.SceneAi.StopMoving();if(slot.ReactionPlayer!=null)actor.SceneAi.LookAt(slot.ReactionPlayer.transform.position); return true;
        }
    }
    [HarmonyPatch(typeof(MasteryPlugin), "Update")]
    internal static class MasterIdolSocialScenesTickPatch { private static void Postfix() => MasterIdolSocialScenes.Tick(); }
}
