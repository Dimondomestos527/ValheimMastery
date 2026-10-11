using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Excess friends may walk out; they are never untamed, renamed, deleted or recreated.
    internal sealed class MasterIdolResidentDeparture : MonoBehaviour
    {
        private Character Actor; private BaseAI Ai; private ZNetView View; private ZDO Data;
        private ZNet Session; private ZDOMan Manager; private ulong Generation; private long Owner; private ushort Revision;
        private bool Going; private string Home; private Vector3 Goal; private float Until, Next; private int Step;
        private readonly List<Vector3> Route = new List<Vector3>(32);
        private void Awake() { Actor = GetComponent<Character>(); Ai = GetComponent<BaseAI>(); View = GetComponent<ZNetView>(); }
        private bool Context()
        {
            if (View?.IsValid() != true || ZNet.instance?.GetWorld() == null || !MasterIdolLoadIdentity.LoadReady) { Stop(false); return false; }
            var data = View.GetZDO(); if (data == null || !ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data)) { Stop(false); return false; }
            if (!ReferenceEquals(Data, data) || Session != ZNet.instance || Manager != ZDOMan.instance || Generation != MasterIdolLoadIdentity.Generation || Owner != data.GetOwner() || Revision != data.OwnerRevision)
            { Stop(false); Data = data; Session = ZNet.instance; Manager = ZDOMan.instance; Generation = MasterIdolLoadIdentity.Generation; Owner = data.GetOwner(); Revision = data.OwnerRevision; Next = Time.unscaledTime + 2; }
            return View.IsOwner();
        }
        private bool Candidate(out MasterIdolEffectZone zone)
        {
            zone = null;
            if (!Context() || Actor == null || Ai == null || !MasterIdolNativeTaming.Friend(Actor) || !MasterIdolNativeTaming.Assigned(Data) ||
                !MasterIdolEffectZones.Ready || !MasterIdolActivity.Near(transform.position) || MasterIdolSocialSeats.Battle(Ai, Actor) || Ai.m_timeSinceHurt < 8 ||
                Ai.IsSleeping() || Actor.InDodge() || Actor.InEmote() || MasterIdolResidentWork.Busy(Actor) ||
                GetComponent<MasterIdolResidentPresentation>()?.Playing == true || MasterIdolResidentSceneAnimation.Playing(Actor) ||
                GetComponent<MasterIdolWelcome>()?.IsPresenting == true || GetComponent<MasterIdolSocialSeats>()?.Active == true || MasterIdolResidentPolicy.TryHome(Actor, out _)) return false;
            zone = MasterIdolEffectZones.Find(Data.GetString(MasterIdolNativeTaming.HomeKey, ""));
            if (zone?.Type != "BlackForest" || zone.Residents.ContainsKey(Data.m_uid.ToString()) || zone.Residents.Count > 8 ||
                !zone.Contains(transform.position, Going ? 15 : 0)) return false;
            string family = Utils.GetPrefabName(gameObject); int count = 0; foreach (var member in zone.Residents) if (member.Value == family) count++;
            return count >= MasterIdolResidentRoster.Limit(family);
        }
        internal static bool Idle(Character actor, float dt)
        { var departure = actor?.GetComponent<MasterIdolResidentDeparture>(); return departure != null && departure.StepOut(dt); }
        private bool StepOut(float dt)
        {
            if (!Candidate(out var zone)) { Stop(false); return false; }
            if (!Going)
            {
                if (Time.unscaledTime < Next) return false; Next = Time.unscaledTime + 30;
                if (!Plan(zone)) return false;
                MasterIdolSocialScenes.Cancel(GetComponent<MasterIdolSocial>()); MasterIdolVisibleGesture.Cancel(Actor);
            }
            if (zone.Id != Home || Time.unscaledTime >= Until) { Stop(false); return false; }
            if (!zone.Contains(transform.position, 5) || (transform.position - Goal).sqrMagnitude < 1) { Stop(true); return true; }
            while (Step < Route.Count && (transform.position - Route[Step]).sqrMagnitude < .36f) Step++;
            Ai.MoveTo(dt, Step < Route.Count ? Route[Step] : Goal, .6f, false); return true;
        }
        private bool Plan(MasterIdolEffectZone zone)
        {
            if (zone.Circles.Length == 0 || ZoneSystem.instance == null || Pathfinding.instance == null) return false;
            int hash = Data.m_uid.ToString().GetStableHashCode() & int.MaxValue;
            for (int index = 0; index < 3; index++)
            {
                var circle = zone.Circles[(hash % zone.Circles.Length + index) % zone.Circles.Length]; float angle = (hash % 360 + index * 120) * Mathf.Deg2Rad;
                var point = circle.Point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (circle.Radius + 6);
                if (zone.Contains(point, 5) || !ZoneSystem.instance.IsZoneLoaded(point) || MasterIdolEffectZones.At("BlackForest", point) != null) continue;
                ZoneSystem.instance.GetGroundData(ref point, out var normal, out var biome, out var area, out var heightmap);
                if (heightmap == null || normal.y < .7f || point.y < ZoneSystem.instance.m_waterLevel + .3f ||
                    Physics.CheckSphere(point + Vector3.up, .5f, LayerMask.GetMask("piece", "static_solid"), QueryTriggerInteraction.Ignore)) continue;
                if (!MasterIdolResidentBudget.Path()) return false; Route.Clear();
                if (!Pathfinding.instance.GetPath(transform.position, point, Route, Ai.m_pathAgentType, true, false, false)) continue;
                float length = 0; var last = transform.position; foreach (var step in Route) { length += Vector3.Distance(last, step); last = step; }
                length += Vector3.Distance(last, point); if (length > 128) continue;
                Home = zone.Id; Goal = point; Going = true; Step = 0; Until = Time.unscaledTime + 30; MasterIdolResidentMetrics.Count("resident.excess-walk-start"); return true;
            }
            return false;
        }
        private void Update() { if (Going && (!Candidate(out var zone) || zone.Id != Home || Time.unscaledTime >= Until)) Stop(false); }
        private void Stop(bool stopMoving) { Going = false; Route.Clear(); Home = null; if (stopMoving && View?.IsOwner() == true) Ai?.StopMoving(); }
        internal string Describe() => "excessDeparture=physical-only going=" + Going + " home=" + (Home ?? "none");
        private void OnDisable() => Stop(false); private void OnDestroy() => Stop(false);
    }
    [HarmonyPatch(typeof(BaseAI), "Awake")]
    internal static class MasterIdolResidentDepartureAttachPatch
    { private static void Postfix(BaseAI __instance) { if (MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject)) > 0 && __instance.GetComponent<MasterIdolResidentDeparture>() == null) __instance.gameObject.AddComponent<MasterIdolResidentDeparture>(); } }
    [HarmonyPatch(typeof(BaseAI), "IdleMovement")]
    internal static class MasterIdolResidentDepartureIdlePatch
    { [HarmonyPriority(Priority.Normal)] private static bool Prefix(BaseAI __instance, float __0) => !MasterIdolResidentDeparture.Idle(__instance.GetComponent<Character>(), __0); }
}
