using System.Collections.Generic;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.UpdateSummon))]
    internal static class Skeleton35NativeDistanceExpiryPatch
    {
        private static void Prefix(Tameable __instance)
        {
            ZDO data = __instance?.GetComponent<ZNetView>()?.GetZDO();
            if (data == null || data.GetLong("vm.skeleton.master", 0L) == 0L) return;
            int prefab = data.GetPrefab();
            if (prefab != "Skeleton_Friendly".GetStableHashCode() &&
                prefab != "Skeleton_Friendly_Archer".GetStableHashCode()) return;
            // Persistence cannot prevent Tameable's explicit distance kill.
            // Keep its logout handling and all other native UpdateSummon logic.
            if (__instance.m_unsummonDistance > 0f && MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[SkeletonTravel] native distance expiry disabled id=" + data.m_uid);
            __instance.m_unsummonDistance = 0f;
        }
    }

    internal static class Skeleton35Travel
    {
        private const string AuthorKey = "vm.skeleton.master";
        private const string PersistencePulseKey = "vm.skeleton.persistence.pulse";
        private static readonly HashSet<ZDOID> Followers = new HashSet<ZDOID>();
        private static ZNetScene Scene;
        private static Player Owner;
        private static float Next;
        private static float PortalUntil;
        private static readonly Dictionary<ZDOID, string> PortalRoster = new Dictionary<ZDOID, string>();
        internal static bool PortalRecoveryPending => Time.time < PortalUntil && PortalRoster.Count > 0;
        private sealed class Progress { internal float BestDistance; internal float StalledSince; }
        private static readonly Dictionary<ZDOID, Progress> ProgressById = new Dictionary<ZDOID, Progress>();
        internal static void Portal()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            if (Scene != ZNetScene.instance || Owner != player)
            { Followers.Clear(); ProgressById.Clear(); PortalRoster.Clear(); Scene = ZNetScene.instance; Owner = player; }
            // Snapshot BEFORE the departure zone is evicted, not after arrival.
            foreach (Character creature in Character.GetAllCharacters())
            {
                MonsterAI ai = creature != null ? creature.GetComponent<MonsterAI>() : null;
                if (creature == null || creature.IsDead()) continue;
                if (ai?.GetFollowTarget() == player.gameObject) Tag(ai, player.gameObject);
                ZDO data = creature.m_nview?.GetZDO();
                if (SummonRosterCommands.Allowed(data) && data.GetLong(AuthorKey, 0) == player.GetPlayerID() && Followers.Count < 32)
                    Followers.Add(data.m_uid);
            }
            PortalRoster.Clear();
            // Followers is a long-lived client cache. Prune it against current
            // canonical records before snapshotting; otherwise dead/retired ids
            // are replayed forever and block every later portal attempt.
            List<ZDOID> stale = new List<ZDOID>();
            foreach (ZDOID id in Followers)
            {
                ZDO data = ZDOMan.instance?.GetZDO(id);
                if (data == null)
                {
                    // The client may have evicted a distant but live summon.
                    // Preserve its bounded identity and ask the server to validate it.
                    PortalRoster[id] = Guid.NewGuid().ToString("N");
                    continue;
                }
                if (!SummonRosterCommands.Allowed(data) || data.GetLong(AuthorKey, 0) != player.GetPlayerID() || data.GetFloat("health", 1f) <= 0f)
                { stale.Add(id); ProgressById.Remove(id); continue; }
                PortalRoster[id] = Guid.NewGuid().ToString("N");
            }
            foreach (ZDOID id in stale) Followers.Remove(id);
            PortalUntil = Time.time + 45f; Next = 0f;
        }

        // ZNetView copies its prefab m_persistent flag into the ZDO during Awake.
        // Summons are already alive by the time the player teleports, so set the
        // canonical ZDO flag before the departure sector is unloaded; otherwise
        // ZDOMan.RemoveOrphanNonPersistentZDOS destroys the follower before the
        // post-teleport roster RPC can replace it.
        internal static void PrepareForPortal(Player player)
        {
            if (player == null || player != Player.m_localPlayer || player.GetPlayerID() == 0) return;
            foreach (Character creature in Character.GetAllCharacters())
            {
                if (creature == null || creature.IsDead()) continue;
                ZNetView view = creature.m_nview;
                ZDO data = view?.GetZDO();
                if (view?.IsValid() != true || !view.IsOwner() || !SummonRosterCommands.Allowed(data) ||
                    data.GetLong(AuthorKey, 0) != player.GetPlayerID() ||
                    data.GetFloat("health", 1f) <= 0f) continue;
                PublishPersistentOwnedFollower(data);
                Followers.Add(data.m_uid);
            }
        }

        private static void PublishPersistentOwnedFollower(ZDO data)
        {
            if (data == null) return;
            // Persistent is serialized in ZDO's flags but its native setter does
            // not advance DataRevision. Touch a private, monotonic marker so the
            // owning peer publishes that flag change in a revisioned update.
            data.Persistent = true;
            long pulse = data.GetLong(PersistencePulseKey, 0L);
            data.Set(PersistencePulseKey, unchecked(pulse + 1L));
            ZDOMan.instance?.ForceSendZDO(data.m_uid);
        }
        internal static void PortalRejected(ZDOID id, bool discardFollower)
        {
            PortalRoster.Remove(id); ProgressById.Remove(id);
            if (discardFollower) Followers.Remove(id);
        }
        internal static void Replaced(ZDOID oldId, ZDOID newId)
        {
            Followers.Remove(oldId); ProgressById.Remove(oldId); PortalRoster.Remove(oldId);
            if (newId != default && Followers.Count < 32) Followers.Add(newId);
        }
#if MASTERY_MAGIC70_EXPERIMENT
        private static bool IsFollower(string name) => name != null && (name.StartsWith("Skeleton") || name.StartsWith(Magic70Surtling.PrefabName) || name.StartsWith("VM_Torbjorn70"));
        private static bool IsFire(ZDO data) => data?.GetPrefab() == Magic70Surtling.PrefabName.GetStableHashCode();
#else
        private static bool IsFollower(string name) => name != null && name.StartsWith("Skeleton");
        private static bool IsFire(ZDO data) => false;
#endif
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<ZPackage>("VM_SkeletonTravel", Receive);
            rpc.Register<ZPackage>("VM_SkeletonMove", MoveReply);
        }
        internal static void Tag(MonsterAI ai, GameObject follow)
        {
            Player player = follow != null ? follow.GetComponent<Player>() : null;
            Character creature = ai?.m_character;
            if (player == null || creature == null || !IsFollower(creature.name) ||
                creature.m_nview?.IsValid() != true || !creature.m_nview.IsOwner()) return;
            creature.m_nview.GetZDO().Set(AuthorKey, player.GetPlayerID());
            if (player == Player.m_localPlayer && Followers.Count < 32) Followers.Add(creature.GetZDOID());
        }

        // Bind native Skeleton_Friendly summons at the actual SpawnAbility birth
        // site. SetFollowTarget can execute on a non-owning replica, which cannot
        // publish the author key needed by portal/dismiss RPC validation.
        internal static void TagSpawnedSummon(SpawnAbility ability, GameObject spawned)
        {
            Player player = ability?.m_owner as Player;
            ItemDrop.ItemData weapon = ability?.m_weapon;
            if (player == null || player != Player.m_localPlayer || player.GetPlayerID() == 0 ||
                weapon?.m_dropPrefab == null || weapon.m_dropPrefab.name != "StaffSkeleton" || spawned == null ||
                !(spawned.name.StartsWith("Skeleton_Friendly", StringComparison.Ordinal))) return;
            ZNetView view = spawned.GetComponent<ZNetView>();
            if (view?.IsValid() != true || !view.IsOwner()) return;
            ZDO data = view.GetZDO();
            if (data == null || data.GetPrefab() != spawned.name.Replace("(Clone)", "").GetStableHashCode()) return;
            data.Set(AuthorKey, player.GetPlayerID());
            PublishPersistentOwnedFollower(data);
            if (Followers.Count < 32) Followers.Add(data.m_uid);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[SkeletonTravel] bound birth id=" + data.m_uid + " prefab=" + spawned.name + " author=" + player.GetPlayerID());
        }
        internal static void Tick(Player player)
        {
            if (player != Player.m_localPlayer) return;
            if (Scene != ZNetScene.instance || Owner != player)
            { Followers.Clear(); ProgressById.Clear(); PortalRoster.Clear(); PortalUntil = 0f; Scene = ZNetScene.instance; Owner = player; Next = 0f; }
            if (Time.time < Next) return;
            Next = Time.time + 2f;
            if (!MagicSkillPassives.OwnerReady(player) || player.IsTeleporting() ||
                (!PerkRuntimeService.HasPerk(player, Skills.SkillType.BloodMagic, 35) &&
                 !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 70))) return;
            foreach (Character creature in Character.GetAllCharacters())
            {
                if (creature == null || creature.IsDead() || !IsFollower(creature.name)) continue;
                ZDO zdo = creature.m_nview?.GetZDO();
                MonsterAI ai = creature.GetComponent<MonsterAI>();
                if (ai?.GetFollowTarget() == player.gameObject) Tag(ai, player.gameObject);
                else if (zdo?.GetLong(AuthorKey, 0) == player.GetPlayerID() && creature.m_nview.IsOwner()) ai?.SetFollowTarget(player.gameObject);
                if (zdo?.GetLong(AuthorKey, 0) == player.GetPlayerID() &&
                    zdo.GetPrefab() == Magic70Carrier.PrefabName.GetStableHashCode())
                    Magic70Carrier.RecordOwnerLink(player, zdo.m_uid);
                if (zdo?.GetLong(AuthorKey, 0) == player.GetPlayerID() && Followers.Count < 32) Followers.Add(creature.GetZDOID());
            }
            int sent = 0;
            foreach (ZDOID id in new List<ZDOID>(Followers))
            {
                ZDO zdo = ZDOMan.instance?.GetZDO(id);
                if (Time.time < PortalUntil && PortalRoster.TryGetValue(id, out string token))
                {
                    if (zdo != null && (zdo.GetPosition() - player.transform.position).sqrMagnitude < 25f * 25f)
                    { PortalRoster.Remove(id); continue; }
                    Vector3 candidate = player.transform.position - player.transform.forward * 2.5f + player.transform.right * (sent % 3 - 1);
                    if (Physics.Raycast(candidate + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 8f,
                        LayerMask.GetMask("Default", "static_solid", "piece", "terrain")) && floor.normal.y >= .65f)
                        SummonRosterCommands.PortalRequest(id, floor.point + Vector3.up * .1f, token);
                    if (++sent >= 16) break;
                    continue;
                }
                // Clients may evict distant ZDOs after a portal. Keep the bounded
                // identity list: the server still owns the authoritative record.
                if (zdo == null) continue;
                if (zdo.GetLong(AuthorKey, 0) != player.GetPlayerID() || zdo.GetFloat("health", 1f) <= 0f)
                { Followers.Remove(id); ProgressById.Remove(id); continue; }
                Skills.SkillType skillType = IsFire(zdo) ? Skills.SkillType.ElementalMagic : Skills.SkillType.BloodMagic;
                if (!PerkRuntimeService.HasPerk(player, skillType, IsFire(zdo) ? 70 : 35)) continue;
                if ((zdo.GetPosition() - player.transform.position).sqrMagnitude < 35f * 35f)
                { ProgressById.Remove(id); continue; }
                if (Time.time >= PortalUntil)
                {
                    float distance = Vector3.Distance(zdo.GetPosition(), player.transform.position);
                    if (!ProgressById.TryGetValue(id, out Progress progress))
                    { ProgressById[id] = new Progress { BestDistance = distance, StalledSince = Time.time }; continue; }
                    MonsterAI ai = ZNetScene.instance.FindInstance(id)?.GetComponent<MonsterAI>();
                    // Position jitter or walking into a wall is not progress. Reset
                    // only after making measurable net distance toward the owner.
                    if (distance < progress.BestDistance - 1f)
                    { progress.BestDistance = distance; progress.StalledSince = Time.time; }
                    // Let the native AI walk. Catch up only after sustained failure,
                    // or immediately after an actual player teleport.
                    if (Time.time - progress.StalledSince < 15f) continue;
                }
                Vector3 point = player.transform.position - player.transform.forward * 2.5f + player.transform.right * (sent % 3 - 1);
                if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f,
                    LayerMask.GetMask("Default", "static_solid", "piece", "terrain")) || ground.normal.y < .65f) continue;
                ZPackage request = new ZPackage(); request.Write(id); request.Write(ground.point + Vector3.up * .1f);
                request.Write(PerkRuntimeService.GetActualSkillLevel(player, skillType));
                if (ZNet.instance.IsServer()) Apply(id, ground.point + Vector3.up * .1f, player.GetPlayerID(), ZDOMan.GetSessionID());
                else ZNet.instance.GetServerRPC()?.Invoke("VM_SkeletonTravel", request);
                if (++sent >= 16) break;
            }
        }
        private static bool Valid(ZDO zdo, long author)
        {
            if (zdo == null || zdo.GetLong(AuthorKey, 0) != author || zdo.GetFloat("health", 1f) <= 0f) return false;
            GameObject prefab = ZNetScene.instance?.GetPrefab(zdo.GetPrefab());
            return prefab != null && SummonRosterCommands.Allowed(zdo);
        }
        private static void Receive(ZRpc rpc, ZPackage package)
        {
            if (ZNet.instance?.IsServer() != true) return;
            ZNetPeer peer = ZNet.instance.GetPeer(rpc);
            ZDO player = OwnerSkillAuthority.ResolveCharacterData(peer);
            if (player == null || player.GetBool(ZDOVars.s_dead, false)) return;
            long playerId = player.GetLong(ZDOVars.s_playerID, 0);
            if (playerId == 0 || package == null || package.Size() > 128) return;
            try
            {
                ZDOID id = package.ReadZDOID(); Vector3 point = package.ReadVector3(); float skill = package.ReadSingle();
                if (!OwnerSkillAuthority.Valid(skill) || skill < 35f ||
                    !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z) ||
                    (point - player.GetPosition()).sqrMagnitude > 8f * 8f) return;
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (IsFire(zdo) && skill < 70f) return;
                if (!Valid(zdo, playerId) || (zdo.GetPosition() - point).sqrMagnitude < 25f * 25f) return;
                GameObject live = ZNetScene.instance.FindInstance(id)?.gameObject;
                if (live == null || live.GetComponent<ZNetView>()?.IsOwner() == true)
                    Apply(id, point, playerId, peer.m_uid);
                else
                {
                    // Only its current owner can move a loaded rigidbody. Do not steal a live AI.
                    ZNetPeer controller = null;
                    foreach (ZNetPeer candidate in ZNet.instance.GetPeers())
                        if (candidate != null && candidate.IsReady() && candidate.m_uid == zdo.GetOwner()) { controller = candidate; break; }
                    if (controller == null) return;
                    ZPackage reply = new ZPackage(); reply.Write(id); reply.Write(point); reply.Write(playerId);
                    controller.m_rpc.Invoke("VM_SkeletonMove", reply);
                }
            }
            catch (System.Exception error) { if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogWarning("Skeleton travel rejected: " + error.GetType().Name); }
        }
        private static void MoveReply(ZRpc rpc, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || rpc != ZNet.instance.GetServerRPC() || Player.m_localPlayer == null) return;
            try
            {
                ZDOID id = package.ReadZDOID(); Vector3 point = package.ReadVector3(); long author = package.ReadLong();
                // The authenticated server already checked destination proximity to the
                // summoner. The object's owner may be a different player far away.
                ZNetView live = ZNetScene.instance.FindInstance(id)?.GetComponent<ZNetView>();
                if (live?.IsOwner() == true && float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z))
                    Apply(id, point, author, ZDOMan.GetSessionID());
            }
            catch (System.Exception) { }
        }
        private static void Apply(ZDOID id, Vector3 point, long author, long destinationOwner)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(id);
            if (!Valid(zdo, author)) return;
            ZNetView live = ZNetScene.instance.FindInstance(id)?.GetComponent<ZNetView>();
            if (live != null)
            {
                if (!live.IsOwner()) return;
                Character character = live.GetComponent<Character>();
                if (character?.m_body == null) return;
                character.m_body.linearVelocity = Vector3.zero; character.m_body.position = point;
                character.transform.position = point; live.GetComponent<ZSyncTransform>()?.SyncNow();
            }
            else
            {
                // Same ZDOID, health, equipment and native slot: no respawn or duplicate.
                zdo.SetOwner(destinationOwner); zdo.SetPosition(point); ZDOMan.instance.ForceSendZDO(id);
            }
        }
    }
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.SetFollowTarget))]
    internal static class Skeleton35AuthorPatch
    { private static void Postfix(MonsterAI __instance, GameObject go) => Skeleton35Travel.Tag(__instance, go); }

    [HarmonyPatch]
    internal static class Skeleton35SpawnAuthorPatch
    {
        private static MethodBase TargetMethod()
        {
            foreach (Type nested in typeof(SpawnAbility).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                if (nested.Name.Contains("<Spawn>") && AccessTools.Method(nested, "MoveNext") is MethodBase moveNext)
                    return moveNext;
            return null;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo bind = AccessTools.Method(typeof(Skeleton35Travel), nameof(Skeleton35Travel.TagSpawnedSummon),
                new[] { typeof(SpawnAbility), typeof(GameObject) });
            int found = 0, index = -1;
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (!(code[i].operand is MethodInfo called) || called.Name != "Instantiate" ||
                    called.DeclaringType != typeof(UnityEngine.Object) ||
                    !called.GetGenericArguments().Any(t => t == typeof(GameObject)) ||
                    code[i + 1].opcode != OpCodes.Stloc_2) continue;
                found++; index = i + 1;
            }
            if (found != 1 || bind == null)
            {
                MasteryPlugin.Log.LogError("[SkeletonTravel] birth author hook disabled; expected one Object.Instantiate<GameObject> stored in local2.");
                return code;
            }
            CodeInstruction first = new CodeInstruction(OpCodes.Ldloc_1);
            first.labels.AddRange(code[index + 1].labels); code[index + 1].labels.Clear();
            first.blocks.AddRange(code[index + 1].blocks); code[index + 1].blocks.Clear();
            code.Insert(index + 1, first);
            code.Insert(index + 2, new CodeInstruction(OpCodes.Ldloc_2));
            code.Insert(index + 3, new CodeInstruction(OpCodes.Call, bind));
            return code;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    internal static class Skeleton35PortalPatch
    {
        private static void Prefix(Player __instance)
        { if (__instance == Player.m_localPlayer) Skeleton35Travel.PrepareForPortal(__instance); }
        private static void Postfix(Player __instance, bool __result)
        { if (__result && __instance == Player.m_localPlayer) Skeleton35Travel.Portal(); }
    }
}
