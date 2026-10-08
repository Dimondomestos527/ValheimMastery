using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class CrossbowTurretRuntime : MonoBehaviour
    {
        internal const string AimKey = "vm.crossbow70.aim";
        internal const string ShotKey = "vm.crossbow70.shot";
        internal const string LastShotKey = "vm.crossbow70.lastshot";
        internal const string PhaseKey = "vm.crossbow70.phase";
        private Container Chest;
        private Character Target;
        private float NextTick, NextScan, TargetSince, NextSearchTrace, NextPhaseTrace;
        private Vector3 Aim;
        private int CachedRevision = -1;
        private CrossbowTurretStock CachedStock;
        private readonly RaycastHit[] Sight = new RaycastHit[32];
        internal static void Attach(Container chest)
        {
            if (chest.GetComponent<CrossbowTurretRuntime>() != null) return;
            var runtime = chest.gameObject.AddComponent<CrossbowTurretRuntime>(); runtime.Chest = chest;
            runtime.Aim = chest.m_nview.GetZDO().GetVec3(AimKey, chest.transform.forward).normalized;
            if (runtime.Aim.sqrMagnitude < .01f) runtime.Aim = chest.transform.forward;
            if (!Application.isBatchMode) chest.gameObject.AddComponent<CrossbowTurretPresentation>();
            Turret donor = ZNetScene.instance?.GetPrefab("piece_turret")?.GetComponent<Turret>();
            BoxCollider native = donor?.GetComponent<BoxCollider>();
            if (native != null)
            {
                foreach (Collider old in chest.GetComponentsInChildren<Collider>(true)) if (!old.isTrigger) old.enabled = false;
                GameObject node = new GameObject("Mastery_FieldTurret_GameplayCollision"); node.transform.SetParent(chest.transform, false);
                node.transform.localRotation = donor.transform.localRotation; node.transform.localScale = Vector3.one * .65f;
                node.layer = chest.gameObject.layer;
                BoxCollider box = node.AddComponent<BoxCollider>(); box.center = native.center; box.size = native.size;
                box.sharedMaterial = native.sharedMaterial;
            }
        }
        internal static bool Enemy(Player owner, Character target) => owner != null && target != null && target != owner &&
            !owner.IsDead() && !target.IsDead() && !target.IsPlayer() && !target.IsTamed() && BaseAI.IsEnemy(owner, target);
        private Player Owner(ZDO record)
        {
            long id = record.GetLong(CrossbowTurretCustody.OwnerKey, 0);
            Player local = Player.m_localPlayer;
            if (local?.GetPlayerID() == id && OwnerSkillAuthority.Has(local, Skills.SkillType.Crossbows, 70)) return local;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                Player player = OwnerSkillAuthority.ResolvePlayer(peer);
                if (player?.GetPlayerID() == id && OwnerSkillAuthority.Has(player, Skills.SkillType.Crossbows, 70)) return player;
            }
            return null;
        }
        private bool Visible(Character target, Vector3 origin)
        {
            Vector3 delta = target.GetCenterPoint() - origin;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, Sight, delta.magnitude, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == Sight.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Sight[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                if (collider.GetComponentInParent<Character>() != target) return false;
            }
            return true;
        }
        private bool InArc(Vector3 point, Vector3 origin, Turret native)
        {
            Vector3 local = transform.InverseTransformDirection(point - origin);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            return native.m_horizontalAngle < 0f || Mathf.Abs(yaw) <= native.m_horizontalAngle;
        }
        private void Phase(ZDO record, int phase)
        {
            if (record.GetOwner() != ZNet.GetUID() || record.GetInt(PhaseKey, -1) == phase) return;
            record.Set(PhaseKey, phase);
            if (MasteryPlugin.Settings.VerboseLogging.Value && Time.time >= NextPhaseTrace)
            {
                NextPhaseTrace = Time.time + 2f;
                MasteryPlugin.Log.LogInfo("[Crossbow70] phase turret=" + record.m_uid + " phase=" + phase +
                    " target=" + Target?.m_nview?.GetZDO()?.m_uid);
            }
        }
        private void SearchMotion(ZDO record, Turret native)
        {
            float arc = native.m_horizontalAngle < 0f ? 180f : native.m_horizontalAngle;
            float yaw = CrossbowTurretRules.SearchYaw(Time.time, native.m_noTargetScanRate, arc);
            Vector3 desired = Quaternion.AngleAxis(yaw, Vector3.up) * transform.forward;
            Aim = Vector3.RotateTowards(Aim, desired, Mathf.Max(10f, native.m_turnRate) * Mathf.Deg2Rad * .2f, 0f);
            record.Set(AimKey, Aim);
        }
        private void FixedUpdate()
        {
            if (Time.time < NextTick) return; NextTick = Time.time + .2f;
            if (Chest == null || ZNet.instance?.IsServer() != true) return;
            ZDO record = Chest.m_nview?.GetZDO();
            if (!CrossbowTurretCustody.Marked(record)) return;
            if (!CrossbowTurretContainerGuards.Locked(record) && record.GetOwner() != ZNet.GetUID() &&
                OwnerSkillAuthority.ResolvePlayerId(ZNet.instance.GetPeer(record.GetOwner())) != record.GetLong(CrossbowTurretCustody.OwnerKey, 0))
                record.SetOwner(ZNet.GetUID());
            // Pending is a cooperative transfer, not permission to seize the client's unsaved cargo.
            if (!CrossbowTurretHandoff.IsPending(record) &&
                (CrossbowTurretRules.LocksStock(CrossbowTurretCustody.State(record)) || record.GetBool(CrossbowTurretCustody.QuarantineKey, false)) &&
                record.GetOwner() != ZNet.GetUID()) record.SetOwner(ZNet.GetUID());
            if (!MasteryPlugin.Settings.Enabled.Value || CrossbowTurretCustody.State(record) != CrossbowTurretState.Active ||
                record.GetBool(CrossbowTurretCustody.QuarantineKey, false) || record.GetOwner() != ZNet.GetUID()) { Target = null; Phase(record, 0); return; }
            Player owner = Owner(record);
            if (owner == null || owner.IsDead() || owner.IsTeleporting()) { Target = null; Phase(record, 1); return; }
            Turret native = ZNetScene.instance.GetPrefab("piece_turret")?.GetComponent<Turret>();
            if (native == null) { Phase(record, 6); return; }
            byte[] bytes = record.GetByteArray(ZDOVars.s_items, null);
            int revision = record.GetInt(CrossbowTurretCustody.RevisionKey, -1);
            if (CachedRevision != revision)
            {
                CachedRevision = revision;
                CrossbowTurretStock.TryRead(bytes, Chest, out CachedStock, out _);
            }
            var stock = CachedStock;
            if (stock == null || !CrossbowTurretProjectile.CanSpawn(stock) && stock.Bolts > 0) { Target = null; Phase(record, 6); return; }
            if (stock.Bolts == 0) { Target = null; Phase(record, 5); return; }
            Vector3 origin = transform.position + Vector3.up * 1.35f + Aim * 1.15f;
            float range = Mathf.Clamp(native.m_viewDistance, 5f, 80f);
            if (!Enemy(owner, Target) || (Target.GetCenterPoint() - origin).sqrMagnitude > range * range || !InArc(Target.GetCenterPoint(), origin, native) || !Visible(Target, origin)) Target = null;
            if (Time.time >= NextScan)
            {
                // Portable platform must not wait a full native far-scan interval (often 10 seconds).
                NextScan = Time.time + Mathf.Clamp(Target == null ? native.m_updateTargetIntervalFar : native.m_updateTargetIntervalNear, .5f, 1f);
                Character best = null; float distance = range * range;
                int hostiles = 0, nearby = 0, frontal = 0, visible = 0;
                Character nearest = null; float nearestDistance = range * range;
                foreach (Character candidate in Character.GetAllCharacters())
                {
                    if (!Enemy(owner, candidate)) continue;
                    hostiles++;
                    float value = (candidate.GetCenterPoint() - origin).sqrMagnitude;
                    if (value >= range * range) continue;
                    nearby++;
                    if (value < nearestDistance) { nearest = candidate; nearestDistance = value; }
                    if (!InArc(candidate.GetCenterPoint(), origin, native)) continue;
                    frontal++;
                    if (!Visible(candidate, origin)) continue;
                    visible++;
                    if (value < distance) { best = candidate; distance = value; }
                }
                if (Target != best) { Target = best; TargetSince = Time.time; }
                if (best == null && MasteryPlugin.Settings.VerboseLogging.Value && Time.time >= NextSearchTrace)
                {
                    NextSearchTrace = Time.time + 10f;
                    MasteryPlugin.Log.LogInfo("[Crossbow70] search turret=" + record.m_uid + " hostile=" + hostiles +
                        " nearby=" + nearby + " frontal=" + frontal + " visible=" + visible + " range=" + range + " arc=" + native.m_horizontalAngle +
                        " baseForward=" + transform.forward + " aim=" + Aim + " nearest=" + nearest?.m_nview?.GetZDO()?.m_uid +
                        " nearestLocal=" + (nearest == null ? Vector3.zero : transform.InverseTransformDirection(nearest.GetCenterPoint() - origin)));
                }
            }
            if (Target == null) { Phase(record, 2); SearchMotion(record, native); return; }
            Vector3 desired = (CrossbowTurretProjectile.AimPoint(stock, Target, origin, native.m_predictionModifier) - origin).normalized;
            if (!InArc(origin + desired, origin, native)) desired = (Target.GetCenterPoint() - origin).normalized;
            Aim = Vector3.RotateTowards(Aim, desired, Mathf.Max(10f, native.m_turnRate) * Mathf.Deg2Rad * .2f, 0f);
            record.Set(AimKey, Aim);
            if (Vector3.Angle(Aim, desired) > CrossbowTurretRules.AimTolerance(native.m_shootWhenAimDiff) || Time.time - TargetSince < Mathf.Max(.25f, native.m_attackWarmup)) { Phase(record, 3); return; }
            origin = transform.position + Vector3.up * 1.35f + Aim * 1.15f;
            double now = ZNet.instance.GetTimeSeconds();
            float cooldown = Mathf.Max(.5f, native.m_attackCooldown, stock.Weapon.GetWeaponLoadingTime());
            if (now - record.GetLong(LastShotKey, 0) / 1000d < cooldown) { Phase(record, 4); return; }
            if (!Enemy(owner, Target) || !Visible(Target, origin)) return;
            if (!CrossbowTurretCustody.TryCommitShot(record, revision, bytes, Chest, out var weapon, out var bolt, out string error)) return;
            Phase(record, 4);
            record.Set(LastShotKey, (long)(now * 1000d)); record.Set(ShotKey, record.GetInt(ShotKey, 0) + 1);
            try { CrossbowTurretProjectile.Fire(owner, weapon, bolt, origin, Aim, native.m_hitNoise); }
            catch (Exception exception)
            {
                // No blind rollback/refund: commit may already have produced a projectile.
                Recover(record); MasteryPlugin.Log.LogError("[Crossbow70] committed shot failed; cargo retained, no replay: " + exception);
            }
            ZDOMan.instance.ForceSendZDO(record.m_uid);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Crossbow70] shot turret=" + record.m_uid + " seq=" + record.GetInt(ShotKey, 0) + " bolts=" + (stock.Bolts - 1) +
                    " target=" + Target.m_nview?.GetZDO()?.m_uid + " origin=" + origin + " aim=" + Aim);
        }
        internal static void Recover(ZDO record)
        {
            if (!CrossbowTurretCustody.Marked(record) || record.GetOwner() != ZNet.GetUID() || ZNet.instance?.IsServer() != true) return;
            record.Set(CrossbowTurretCustody.StateKey, (int)CrossbowTurretState.Recovery);
            record.Set(CrossbowTurretCustody.RevisionKey, record.GetInt(CrossbowTurretCustody.RevisionKey, 0) + 1);
            ZDOMan.instance.ForceSendZDO(record.m_uid);
        }
    }
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class CrossbowTurretRestorePatch
    { private static void Postfix(Container __instance) { if (CrossbowTurretCustody.Marked(__instance.m_nview?.GetZDO())) CrossbowTurretRuntime.Attach(__instance); } }
    [HarmonyPatch(typeof(Container), "CheckForChanges")]
    internal static class CrossbowTurretLateRestorePatch
    { private static void Postfix(Container __instance) { if (CrossbowTurretCustody.Marked(__instance.m_nview?.GetZDO())) CrossbowTurretRuntime.Attach(__instance); } }
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy))]
    internal static class CrossbowTurretDestructionPatch
    {
        private static bool Prefix(WearNTear __instance)
        {
            ZDO record = __instance.m_nview?.GetZDO();
            if (!CrossbowTurretCustody.Marked(record)) return true;
            if (__instance.m_nview.IsOwner())
            {
                record.Set(ZDOVars.s_health, __instance.m_health);
                if (ZNet.instance?.IsServer() == true) CrossbowTurretRuntime.Recover(record);
                else if (!CrossbowTurretContainerGuards.Locked(record))
                {
                    // Inactive native client custody may preserve/recover its own container,
                    // never retire a server-owned ACTIVE turret or copy its cargo.
                    record.Set(CrossbowTurretCustody.StateKey, (int)CrossbowTurretState.Recovery);
                    record.Set(CrossbowTurretCustody.RevisionKey, record.GetInt(CrossbowTurretCustody.RevisionKey, 0) + 1);
                }
            }
            // Keep SAME custody object; never call native chest DropAllItems or destroy ZDO.
            return false;
        }
    }
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class CrossbowTurretHoverPatch
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            ZDO record = __instance.m_nview?.GetZDO(); if (!CrossbowTurretCustody.Marked(record)) return;
            bool ua = CrossbowTurretCommands.Ukrainian;
            string state = CrossbowTurretCustody.State(record) == CrossbowTurretState.Active ? (ua ? "Працює" : "Active") : (ua ? "Контейнер зброї" : "Weapon container");
            if (CrossbowTurretCustody.State(record) == CrossbowTurretState.Active)
                switch (record.GetInt(CrossbowTurretRuntime.PhaseKey, 0))
                {
                    case 1: state = ua ? "Пауза" : "Paused"; break;
                    case 2: state = ua ? "Пошук цілі" : "Searching"; break;
                    case 3: state = ua ? "Наведення" : "Aiming"; break;
                    case 4: state = ua ? "Перезарядка" : "Reloading"; break;
                    case 5: state = ua ? "Немає болтів" : "Empty"; break;
                    case 6: state = ua ? "Перевір зброю та болти" : "Check weapon and bolts"; break;
                }
            long bolts = 0;
            foreach (var item in __instance.GetInventory().GetAllItems()) if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) bolts += item.m_stack;
            if (CrossbowTurretCustody.State(record) == CrossbowTurretState.Active && bolts == 0) state = ua ? "Немає болтів" : "Empty magazine";
            __result = (ua ? "Польова турель — " : "Field turret — ") + state + "\n" +
                (ua ? "Болти: " : "Bolts: ") + bolts + "\n" +
                (ua ? "Порожні руки + вторинна атака: запуск / зупинка\n[Взаємодія] Вміст зупиненої турелі" : "Empty hands + secondary: start / stop\n[Interact] Stopped turret cargo");
        }
    }
}
