#if MASTERY_SHIELD_RUSH_EXPERIMENT
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class ShieldRush70 : MonoBehaviour
    {
        private const string UntilKey = "vm.shieldrush.until";
        private const string ImpactSound = "sfx_metal_shield_blocked";
        private Player Player;
        private ItemDrop.ItemData Shield;
        private Vector3 Direction;
        private float Until, Speed, Force, ImpactWidth;
        private float NextSpark;
        private int SparkCount;
        private long Episode;
        private bool Heavy;
        private bool GroundTraced;
        private readonly Collider[] ImpactTargets = new Collider[96];
        private readonly Collider[] Targets = new Collider[96];
        private readonly RaycastHit[] Walls = new RaycastHit[24];
        private readonly HashSet<ZDOID> Hit = new HashSet<ZDOID>();
        private readonly HashSet<Collider> IgnoredBodies = new HashSet<Collider>();
        internal bool Active => Player != null && Time.time < Until;
        internal static bool TryStart(Player player)
        {
            ItemDrop.ItemData shield = player?.GetCurrentBlocker();
            if (!MagicSkillPassives.OwnerReady(player) || !player.IsBlocking() ||
                shield?.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Blocking, 70) || player.IsStaggering() ||
                player.IsTeleporting() || player.InAttack() || player.InDodge() || !player.IsOnGround() ||
                player.IsEncumbered() || !player.HaveStamina(25f) ||
                PerkCooldownStateService.GetRemainingSeconds(player, "shieldrush70") > 0d) return false;
            var rush = player.GetComponent<ShieldRush70>() ?? player.gameObject.AddComponent<ShieldRush70>();
            if (rush.Active || !PerkCooldownStateService.TryConsume(player, "shieldrush70", 20d)) return false;
            rush.Player = player; rush.Shield = shield; rush.Hit.Clear();
            float parry = shield.m_shared.m_timedBlockBonus;
            rush.Heavy = parry <= 1f;
            // Preserve the two existing durations and distinguish profiles by
            // native parry capability: round/buckler are light; tower is heavy.
            float duration = parry <= 1f ? .30f : .60f;
            float distance = parry <= 1f ? 8.25f : 16.5f;
            float impact = parry <= 1f ? 1.3f : .8f;
            rush.ImpactWidth = parry <= 1f ? 1.35f : .8f;
            rush.Direction = player.transform.forward; rush.Direction.y = 0f; rush.Direction.Normalize();
            rush.Speed = distance / duration; rush.Until = Time.time + duration;
            float pressure = Mathf.Clamp01(player.GetStaggerPercentage());
            // Rush impact is a physical shove, not hidden stagger damage.
            // Minimum 35 force gives even a low-tier silver shield a readable push.
            rush.Force = Mathf.Clamp(20f * impact * (1f + 2f * pressure), 35f, 80f);
            player.UseStamina(25f); player.m_queuedDodgeTimer = 0f;
            player.m_dodgeInvincible = player.m_inDodge = false;
            rush.NextSpark = Time.time; rush.SparkCount = 0; rush.GroundTraced = false;
            rush.Episode = ZNet.instance.GetTime().AddSeconds(duration + .75f).Ticks;
            player.m_nview.GetZDO().Set(UntilKey, rush.Episode);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[ShieldRush] start actor=" + player.GetZDOID() + " episode=" + rush.Episode +
                    " tower=" + rush.Heavy + " distance=" + distance.ToString("F2") + " duration=" + duration.ToString("F2") +
                    " position=" + player.transform.position + " direction=" + rush.Direction);
            PerkNativeFeedback.PlayVfx("fx_land", player.transform.position, .7f, .8f);
            // Exact user-selected metal shield cue, not the mace's ground thump.
            PerkAudioService.Play("shieldrush70", ImpactSound, player.GetCenterPoint(), .2f, .4f, 1.05f);
            var marker = ScriptableObject.CreateInstance<SE_Stats>();
            marker.name = "VM_ShieldRushCooldown"; marker.m_name = "Натиск щита";
            marker.m_tooltip = "Щит відновлює силу для наступного ривка. Блокуй і натисни ухил, коли відновиться.";
            marker.m_ttl = 20f; marker.m_icon = shield.GetIcon();
            player.GetSEMan().AddStatusEffect(marker, true);
            Object.Destroy(marker); // SEMan owns a clone, not this template.
            return true;
        }
        internal static bool IsActive(Player player) => player?.GetComponent<ShieldRush70>()?.Active == true;
        private void Stop(string reason)
        {
            if (MasteryPlugin.Settings.VerboseLogging.Value && Player != null)
                MasteryPlugin.Log.LogInfo("[ShieldRush] stop actor=" + Player.GetZDOID() + " episode=" + Episode +
                    " reason=" + reason + " position=" + Player.transform.position + " velocityBefore=" + Player.m_body?.linearVelocity +
                    " grounded=" + Player.IsOnGround() + " blocking=" + Player.IsBlocking() +
                    " attack=" + Player.InAttack() + " dodge=" + Player.InDodge() + " stagger=" + Player.IsStaggering());
            CapsuleCollider capsule = Player != null ? Player.GetCollider() : null;
            if (capsule != null)
                foreach (Collider body in IgnoredBodies)
                    if (body != null) Physics.IgnoreCollision(capsule, body, false);
            IgnoredBodies.Clear();
            Until = 0f; Hit.Clear();
            if (Player?.m_nview?.IsOwner() == true) Player.m_nview.GetZDO().Set(UntilKey, 0L);
            if (Player?.m_body != null)
            { Vector3 velocity = Player.m_body.linearVelocity; velocity.x = velocity.z = 0f; Player.m_body.linearVelocity = velocity; }
            // Release the forced block pose; native input owns it again. Holding
            // block intentionally keeps the pose, releasing it must not latch it.
            if (Player != null && Player == Player.m_localPlayer)
                Player.m_blocking = ZInput.GetButton("Block");
            if (MasteryPlugin.Settings.VerboseLogging.Value && Player != null)
                MasteryPlugin.Log.LogInfo("[ShieldRush] stopped actor=" + Player.GetZDOID() + " episode=" + Episode +
                    " active=" + Active + " ignoredBodies=" + IgnoredBodies.Count + " velocityAfter=" + Player.m_body?.linearVelocity +
                    " grounded=" + Player.IsOnGround() + " blocking=" + Player.IsBlocking() +
                    " attack=" + Player.InAttack() + " dodge=" + Player.InDodge() + " stagger=" + Player.IsStaggering());
        }
        private void OnDisable() { if (Until != 0f) Stop("disabled"); }
        internal void Tick(float dt)
        {
            if (!Active) { if (Until != 0f) Stop("expired"); return; }
            if (!MagicSkillPassives.OwnerReady(Player) || Player.GetCurrentBlocker() != Shield ||
                (!Heavy && Player.IsStaggering()) || Player.IsTeleporting() || !MasteryPlugin.Settings.Enabled.Value) { Stop("owner-equipment-state"); return; }
            Player.m_blocking = true; Player.m_queuedDodgeTimer = 0f; Player.m_dodgeInvincible = false;
            CapsuleCollider capsule = Player.GetCollider(); if (capsule == null || dt <= 0f) { Stop("capsule-or-dt"); return; }
            Vector3 center = capsule.transform.TransformPoint(capsule.center);
            float radius = Mathf.Max(.1f, Player.GetRadius());
            float half = Mathf.Max(0f, capsule.height * Mathf.Abs(capsule.transform.lossyScale.y) * .5f - radius);
            float sweepDistance = Speed * dt + .08f;
            Vector3 feet = center - Vector3.up * (half + radius);
            Vector3 travel = Direction;
            bool followingGround = false;
            int count = Physics.CapsuleCastNonAlloc(center + Vector3.up * half, center - Vector3.up * half,
                radius * .95f, Direction, Walls, sweepDistance,
                LayerMask.GetMask("terrain", "static_solid", "piece", "Default"), QueryTriggerInteraction.Ignore);
            if (count == Walls.Length) { Stop("wall-buffer-full"); return; }
            for (int i = 0; i < count; i++)
            {
                if (Walls[i].collider == null || Walls[i].collider == capsule ||
                    Walls[i].collider.GetComponentInParent<Character>() != null) continue;
                if (TryFollowGround(Walls[i], feet, radius + sweepDistance, out Vector3 normal))
                {
                    travel = Vector3.ProjectOnPlane(Direction, normal).normalized;
                    followingGround = true;
                    if (!GroundTraced && MasteryPlugin.Settings.VerboseLogging.Value)
                    {
                        GroundTraced = true;
                        MasteryPlugin.Log.LogInfo("[ShieldRush] ground-follow episode=" + Episode + " collider=" + Walls[i].collider.name +
                            " sweepDistance=" + Walls[i].distance.ToString("F3") + " realNormal=" + normal + " travel=" + travel);
                    }
                    continue;
                }
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[ShieldRush] contact episode=" + Episode + " collider=" + Walls[i].collider.name +
                        " layer=" + LayerMask.LayerToName(Walls[i].collider.gameObject.layer) +
                        " normal=" + Walls[i].normal + " distance=" + Walls[i].distance.ToString("F3"));
                Stop("world-contact"); return;
            }
            if (SparkCount < 14 && Time.time >= NextSpark)
            {
                PerkNativeFeedback.PlayVfx("vfx_HitSparks", center - Direction * .45f, .30f, .45f);
                SparkCount++; NextSpark = Time.time + .045f;
            }
            Vector3 velocity = Player.m_body.linearVelocity;
            velocity.x = travel.x * Speed; velocity.z = travel.z * Speed;
            if (followingGround) velocity.y = travel.y * Speed;
            Player.m_body.linearVelocity = velocity;
            int targets = Physics.OverlapCapsuleNonAlloc(Player.GetCenterPoint(),
                Player.GetCenterPoint() + Direction * (Speed * dt + .7f), radius + .35f,
                Targets, LayerMask.GetMask("character", "character_net", "Default"), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < targets; i++)
            {
                Collider contact = Targets[i];
                Character target = contact?.GetComponentInParent<Character>(); Targets[i] = null;
                if (target == null || target == Player || target.IsDead() || target.IsPlayer() || target.IsTamed() ||
                    !BaseAI.IsEnemy(Player, target)) continue;
                // Removing mobs from the wall query alone does not remove
                // rigidbody contacts. Ignore only these hostile contacts during
                // the rush, then restore them even on cancellation/disable.
                if (Heavy)
                    foreach (Collider body in target.GetComponentsInChildren<Collider>(true))
                        if (!body.isTrigger && !Physics.GetIgnoreCollision(capsule, body) && IgnoredBodies.Add(body))
                            Physics.IgnoreCollision(capsule, body, true);
                if (Hit.Contains(target.GetZDOID())) continue;
                ImpactArea(target.GetCenterPoint());
                if (!Heavy) { Stop("light-mob-impact"); return; }
            }
        }
        private bool TryFollowGround(RaycastHit sweep, Vector3 feet, float distance, out Vector3 normal)
        {
            normal = Vector3.up;
            if (!Player.IsOnGround() || sweep.collider == null) return false;
            // Do not reinterpret a positive-distance vertical wall as a floor.
            if (sweep.distance > .001f && sweep.normal.y <= ShieldRushTerrainRules.MinimumNormalY) return false;
            // The SAME collider must have standable support below us and ahead.
            // A separate door/rock/wall cannot borrow the terrain's ground normal.
            if (!sweep.collider.Raycast(new Ray(feet + Vector3.up * .6f, Vector3.down), out RaycastHit current, 1.2f) ||
                !sweep.collider.Raycast(new Ray(feet + Direction * distance + Vector3.up * .6f, Vector3.down), out RaycastHit ahead, 1.2f)) return false;
            bool supported = ShieldRushTerrainRules.CanFollow(current.normal.y, ahead.normal.y,
                ahead.point.y - current.point.y, distance);
            if (!ShieldRushTerrainRules.CanReplaceSweepNormal(sweep.distance, sweep.normal.y, supported)) return false;
            normal = ahead.normal;
            return true;
        }
        private void ImpactArea(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, Heavy ? 3f : 2.2f,
                ImpactTargets, LayerMask.GetMask("character", "character_net", "Default"));
            for (int i = 0; i < count; i++)
            {
                Character target = ImpactTargets[i]?.GetComponentInParent<Character>(); ImpactTargets[i] = null;
                if (target == null || target == Player || target.IsDead() || target.IsPlayer() || target.IsTamed() ||
                    !BaseAI.IsEnemy(Player, target) || !Hit.Add(target.GetZDOID())) continue;
                var packet = new ZPackage(); packet.Write(Player.GetZDOID()); packet.Write(Episode);
                packet.Write(PerkRuntimeService.GetActualSkillLevel(Player, Skills.SkillType.Blocking));
                packet.Write(Force); packet.Write(point); packet.Write(Direction);
                target.m_nview?.InvokeRPC("VM_ShieldRushImpact", packet);
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[ShieldRush] impact-dispatch actor=" + Player.GetZDOID() + " episode=" + Episode +
                        " tower=" + Heavy + " target=" + target.GetZDOID() + " targetOwner=" + (target.m_nview?.IsOwner() == true) + " force=" + Force.ToString("F2"));
            }
            ShieldRushImpactVisual.Play(point, Heavy ? 3f : 2.2f);
            PerkAudioService.Play("shieldrush_impact", ImpactSound, point, .2f, .65f, .95f);
        }
        internal static void Register(Character target)
        {
            if (target?.m_nview?.IsValid() != true) return;
            var seen = new Dictionary<ZDOID, long>();
            target.m_nview.Register<ZPackage>("VM_ShieldRushImpact", (sender, packet) =>
            {
                if (packet == null || packet.Size() > 96) return;
                ZDOID sourceId; long until; float level, force; Vector3 origin, travel;
                try { sourceId = packet.ReadZDOID(); until = packet.ReadLong(); level = packet.ReadSingle(); force = packet.ReadSingle();
                    origin = packet.ReadVector3(); travel = packet.ReadVector3(); }
                catch (System.Exception) { return; }
                ZDO source = ZDOMan.instance?.GetZDO(sourceId);
                if (!MasteryPlugin.Settings.Enabled.Value || !target.m_nview.IsOwner() || target.IsPlayer() || target.IsDead() || target.IsTamed() ||
                    source == null || source.GetOwner() != sender ||
                    ZNetScene.instance.GetPrefab(source.GetPrefab())?.GetComponent<Player>() == null ||
                    (source.GetPosition() - origin).sqrMagnitude > 484f ||
                    (origin - target.GetCenterPoint()).sqrMagnitude > 16f || !float.IsFinite(origin.x) ||
                    !float.IsFinite(origin.y) || !float.IsFinite(origin.z) || !float.IsFinite(travel.sqrMagnitude) ||
                    travel.sqrMagnitude < .5f || travel.sqrMagnitude > 1.5f ||
                    !OwnerSkillAuthority.Valid(level) || level < 70f || !float.IsFinite(force) || force < 35f || force > 80f) return;
                // Sender is the authenticated owner of this native character.
                // Episode travels in the RPC: waiting for an earlier ZDO update
                // would lose impacts when the short dash has already finished.
                if (until <= ZNet.instance.GetTime().Ticks || until > ZNet.instance.GetTime().AddSeconds(2f).Ticks ||
                    (seen.TryGetValue(sourceId, out long previous) && previous >= until)) return;
                GameObject equipped = ZNetScene.instance.GetPrefab(source.GetInt(ZDOVars.s_leftItem, 0));
                if (equipped?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield) return;
                if (seen.Count >= 64 && !seen.ContainsKey(sourceId)) return;
                seen[sourceId] = until;
                Vector3 direction = target.transform.position - source.GetPosition(); direction.y = 0f; direction.Normalize();
                CreatureClass kind = Fists70TargetClassifier.Classify(target);
                float fraction = kind == CreatureClass.Boss ? .12f : kind == CreatureClass.Heavy ? .28f : 1.05f;
                HitData hit = new HitData(); hit.m_skill = Skills.SkillType.Blocking;
                hit.m_point = target.GetCenterPoint(); hit.m_dir = direction; hit.m_variant = 1270;
                Character actor = ZNetScene.instance.FindInstance(sourceId)?.GetComponent<Character>();
                if (actor != null) hit.SetAttacker(actor);
                target.AddStaggerDamage(Mathf.Max(1f, target.GetStaggerTreshold() * fraction), direction, hit);
                bool tower = equipped.GetComponent<ItemDrop>().m_itemData.m_shared.m_timedBlockBonus <= 1f;
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[ShieldRush] impact-accepted actor=" + sourceId + " episode=" + until +
                        " target=" + target.GetZDOID() + " tower=" + tower + " kind=" + kind + " staggerFraction=" + fraction.ToString("F2") +
                        " staggering=" + target.IsStaggering());
                if (tower)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, travel.normalized);
                    if (Vector3.Dot(target.GetCenterPoint() - origin, side) < 0f) side = -side;
                    // Native pushback divides by target mass. Give ordinary
                    // large enemies a visible lateral shove without increasing
                    // damage or making boss bodies universally movable.
                    float push = kind == CreatureClass.Boss ? force : Mathf.Max(force, (target.m_body?.mass ?? 1f) * 1.2f);
                    target.ApplyPushback(side, push);
                }
            });
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.Dodge))]
    internal static class ShieldRush70InputPatch
    { private static bool Prefix(Player __instance) => !ShieldRush70.IsActive(__instance) && !ShieldRush70.TryStart(__instance); }
    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class ShieldRush70MovementPatch
    { private static void Postfix(Character __instance, float dt) { if (__instance is Player p) p.GetComponent<ShieldRush70>()?.Tick(dt); } }
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class ShieldRush70AttackExclusionPatch
    { private static bool Prefix(Player __instance) => !ShieldRush70.IsActive(__instance); }
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class ShieldRush70ImpactRegistrationPatch
    { private static void Postfix(Character __instance) => ShieldRush70.Register(__instance); }
}
#endif
