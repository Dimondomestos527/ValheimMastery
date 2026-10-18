using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Work first, one visible idle slot, then occasional physical spacing. No social tours.
    internal sealed class MasterIdolSocial : MonoBehaviour
    {
        private Character Creature; private BaseAI Ai; private ZNetView View;
        private MasterIdolLeash Leash; private ZDO Data; private ZNet Session;
        private ZDOMan Manager; private ulong Generation; private long Owner; private ushort Revision;
        private float NextSpread, End; private string Home, Spot;
        private Vector3 Goal; private bool Moving; private int Step;
        private readonly List<Vector3> Route = new List<Vector3>(32);
        private readonly List<MasterIdolSocialRoutes.Interval> Ranges = new List<MasterIdolSocialRoutes.Interval>(32);
        internal Character SceneCreature => Creature;
        internal BaseAI SceneAi => Ai;
        internal ZNetView SceneView => View;
        internal MasterIdolEffectZone SceneHome => Leash?.SocialHome;
        internal bool SceneAvailable => !Moving && Creature != null && Ai != null && View?.IsValid() == true && View.IsOwner() && MasterIdolResidentGrounding.Slow(Creature) &&
            MasterIdolResidentPolicy.TryHome(Creature, out var home) && home.Contains(transform.position) &&
            !MasterIdolResidentUniqueActions.Busy(Creature) && !MasterIdolResidentWork.Busy(Creature) && GetComponent<MasterIdolResidentPresentation>()?.Playing != true && !MasterIdolResidentBirthPresentation.Playing(Creature) &&
            !MasterIdolResidentSceneAnimation.Playing(Creature) && GetComponent<MasterIdolSocialSeats>()?.Active != true &&
            GetComponent<MasterIdolWelcome>()?.IsPresenting != true && !MasterIdolSocialSeats.Battle(Ai, Creature) &&
            Ai.m_timeSinceHurt >= 8 && !Ai.IsSleeping() && !Creature.InDodge() && !Creature.InEmote();
        private void Awake() { Creature = GetComponent<Character>(); Ai = GetComponent<BaseAI>(); View = GetComponent<ZNetView>(); Leash = GetComponent<MasterIdolLeash>(); }
        private bool Context()
        {
            Leash = Leash ?? GetComponent<MasterIdolLeash>();
            if (View?.IsValid() != true || ZNet.instance?.GetWorld() == null || !MasterIdolLoadIdentity.LoadReady) { Reset(); return false; }
            var data = View.GetZDO();
            if (!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data)) { Reset(); return false; }
            if (!ReferenceEquals(Data, data) || Session != ZNet.instance || Manager != ZDOMan.instance || Generation != MasterIdolLoadIdentity.Generation || Owner != data.GetOwner() || Revision != data.OwnerRevision)
            {
                Reset(); Data = data; Session = ZNet.instance; Manager = ZDOMan.instance; Generation = MasterIdolLoadIdentity.Generation;
                Owner = data.GetOwner(); Revision = data.OwnerRevision; NextSpread = Time.unscaledTime + 2;
            }
            return View.IsOwner();
        }
        private void Update()
        {
            if (MasterIdolResidentBirthPresentation.Playing(Creature)) return;
            MasterIdolSocialSeats.Attach(Creature);
            if (!Context() || !MasterIdolResidentPolicy.TryHome(Creature, out var home) || !home.Contains(transform.position) ||
                !MasterIdolActivity.Near(transform.position) || MasterIdolSocialSeats.Battle(Ai, Creature) ||
                MasterIdolResidentWork.Busy(Creature) || GetComponent<MasterIdolWelcome>()?.IsPresenting == true) { Reset(); return; }
            if (Home != home.Id) { Reset(); Home = home.Id; }
        }
        internal bool Idle(float dt)
        {
            using var scope = new MasterIdolPerf.Scope("social.idle");
            if (MasterIdolResidentBirthPresentation.FreshIdleHold(Creature)) { Ai.StopMoving(); return true; }
            if (MasterIdolResidentBirthPresentation.Playing(Creature)) return false;
            if (!Context() || !MasterIdolResidentPolicy.TryHome(Creature, out var home) || !home.Contains(transform.position) ||
                MasterIdolSocialSeats.Battle(Ai, Creature) || GetComponent<MasterIdolWelcome>()?.IsPresenting == true) return false;
            if (MasterIdolResidentWork.Idle(Creature, dt)) { MasterIdolResidentDance.Cancel(Creature,"work");StopSpread(); MasterIdolSocialScenes.Cancel(this); return true; }
            if (MasterIdolResidentUniqueActions.Idle(Creature, dt)) { MasterIdolResidentDance.Cancel(Creature,"unique");StopSpread(); MasterIdolSocialScenes.Cancel(this); return true; }
            if (MasterIdolResidentCargo.Idle(Creature, dt)) { MasterIdolResidentDance.Cancel(Creature,"cargo");StopSpread(); MasterIdolSocialScenes.Cancel(this); return true; }
            if(MasterIdolResidentDance.Idle(Creature,dt)){StopSpread();MasterIdolSocialScenes.Cancel(this);return true;}
            if (!MasterIdolActivity.Near(transform.position)) { Reset(); return false; }
            if (MasterIdolResidentSceneAnimation.Idle(Creature, dt)) { StopSpread(); MasterIdolSocialScenes.Cancel(this); return true; }
            if(MasterIdolResidentEmote.Idle(this,home,dt)){StopSpread();return true;}
            if(MasterIdolSocialSeats.Idle(this,home,dt)){StopSpread();return true;}
            if (Moving)
            {
                if (Time.unscaledTime >= End || !SameHome(home) || !MasterIdolSocialRoutes.Inside(home, transform.position, Goal, Route, Ranges)) { StopSpread(); return false; }
                while (Step < Route.Count && (transform.position - Route[Step]).sqrMagnitude < 1.44f) Step++;
                if ((transform.position - Goal).sqrMagnitude < 2.25f) { StopSpread(); return false; }
                Ai.MoveTo(dt, Step < Route.Count ? Route[Step] : Goal, 1.2f, false); return true;
            }
            if (Time.unscaledTime >= NextSpread && Crowded(home))
            {
                NextSpread = Time.unscaledTime + MasterIdolResidentIdleRules.SpreadCadence;
                MasterIdolSocialScenes.Cancel(this);
                if (BeginSpread(home)) return true;
            }
            return MasterIdolSocialScenes.Idle(this, home, dt, true);
        }
        private bool SameHome(MasterIdolEffectZone home) => Home == home.Id && MasterIdolResidentPolicy.TryHome(Creature, out var current) && current.Id == home.Id;
        private bool Crowded(MasterIdolEffectZone home)
        {
            if (MasterIdolResidentSpacing.MustYield(Creature, home, transform.position, out _)) return true;
            foreach (var member in home.Residents)
            {
                if (!MasterIdolNetworkQuota.TryUid(member.Key, out long user, out uint id)) continue;
                var other = ZNetScene.instance?.FindInstance(new ZDOID(user, id))?.GetComponent<Character>();
                if (other != null && other != Creature && (other.transform.position - transform.position).sqrMagnitude < 16 &&
                    string.CompareOrdinal(member.Key, View.GetZDO().m_uid.ToString()) < 0) return true;
            }
            return false;
        }
        private bool BeginSpread(MasterIdolEffectZone home)
        {
            if (home.Residents.Count > 8 || home.Circles.Length == 0 || ZoneSystem.instance == null || Pathfinding.instance == null) return false;
            int rank = MasterIdolResidentSpacing.Rank(Creature, home);
            var circle = home.Circles[rank % home.Circles.Length]; float angle = rank * Mathf.PI * .25f;
            var point = home.Inside(circle.Point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (circle.Radius * .65f));
            if (!ZoneSystem.instance.IsZoneLoaded(point) || !MasterIdolResidentSpacing.Allowed(Creature, home, point, false)) return false;
            ZoneSystem.instance.GetGroundData(ref point, out var normal, out var biome, out var area, out var heightmap);
            if (heightmap == null || normal.y < .65f || point.y < ZoneSystem.instance.m_waterLevel + .15f ||
                Physics.CheckSphere(point + Vector3.up, .6f, LayerMask.GetMask("piece", "static_solid"), QueryTriggerInteraction.Ignore) || !MasterIdolResidentBudget.Path()) return false;
            Route.Clear();
            if (!Pathfinding.instance.GetPath(transform.position, point, Route, Ai.m_pathAgentType, true, false, false) ||
                !MasterIdolSocialRoutes.Inside(home, transform.position, point, Route, Ranges)) return false;
            Home = home.Id; Goal = point; Moving = true; Step = 0; End = Time.unscaledTime + 25; Spot = Guid.NewGuid().ToString("N");
            GetComponent<MasterIdolResidentWork>()?.Spot(Home, Spot, Goal, 25, false, true); MasterIdolResidentMetrics.Count("resident.spread-start"); return true;
        }
        private void StopSpread()
        {
            Moving = false; Route.Clear();
            if (Spot != null)
            {
                MasterIdolResidentSpacing.Remove(Creature, Home, Spot);
                if (View?.IsValid() == true && View.IsOwner()) GetComponent<MasterIdolResidentWork>()?.Spot(Home, Spot, Goal, 0, false, false);
            }
            Spot = null;
        }
        private void Reset() { MasterIdolSocialScenes.Cancel(this); StopSpread(); Home = null; }
        internal string Describe() => "social=simple-visible-idle spread=" + Moving + " work=" + MasterIdolResidentWork.Busy(Creature) + " home=" + (Home ?? "none");
        private void OnDisable() => Reset(); private void OnDestroy() => Reset();
    }
    [HarmonyPatch(typeof(BaseAI), "Awake")]
    internal static class MasterIdolSocialAttachPatch
    { private static void Postfix(BaseAI __instance) { if (MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject)) > 0 && __instance.GetComponent<MasterIdolSocial>() == null) __instance.gameObject.AddComponent<MasterIdolSocial>(); } }
    [HarmonyPatch(typeof(BaseAI), "IdleMovement")]
    internal static class MasterIdolSocialIdlePatch
    { [HarmonyPriority(Priority.Low)] private static bool Prefix(BaseAI __instance, float __0) => MasterIdolPilotA.Get<MasterIdolWelcome>(__instance)?.IsPresenting == true || MasterIdolPilotA.Get<MasterIdolSocial>(__instance)?.Idle(__0) != true; }
}
