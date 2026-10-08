using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Commands carry identities only. Only the server copies its own native ZDO
    // record; no client-supplied creature, health, inventory or spawn count.
    internal static class SummonRosterCommands
    {
        private const string RequestRpc = "VM_SummonRoster", ResultRpc = "VM_SummonRosterResult", FocusAckRpc = "VM_SummonRosterFocusAck", RejectRpc = "VM_SummonRosterReject", FocusRejectRpc = "VM_SummonRosterFocusReject";
        internal const float DismissHoldSeconds = 5f / 1.4f;
        private const float DismissDurationEpsilon = 0.10f; // allow one network/frame boundary, never skip the actual hold
        internal const string AuthorKey = "vm.skeleton.master";
        private sealed class Receipt { internal long Author; internal ZDOID Source, Replacement; internal float Expires; }
        private sealed class Focus { internal ZDOID Target; internal int Staff; internal string Token; internal float Started; }
        private static readonly Dictionary<string, Receipt> Receipts = new Dictionary<string, Receipt>();
        private static readonly Dictionary<long, Focus> FocusByAuthor = new Dictionary<long, Focus>();
        private static readonly Dictionary<string, Receipt> SentCommands = new Dictionary<string, Receipt>();
        private static readonly List<string> ExpiredCommands = new List<string>();
        private static float NextPrune;
        private static ZNet Session;
        private static ZDOID Aimed;
        private static float Held, NextAim;
        private static float FocusRequestedAt;
        private static float FocusAcknowledgedAt;
        private static string FocusToken;
        private static bool Awaiting, FocusAcknowledged;
        private static Player LocalOwner;

        internal static void Register(ZRpc rpc)
        {
            rpc.Register<ZPackage>(RequestRpc, Receive);
            rpc.Register<string, ZDOID, ZDOID>(ResultRpc, Result);
            rpc.Register<string, ZDOID, ZDOID>(FocusAckRpc, FocusAcknowledgedRpc);
            rpc.Register<string, ZDOID, ZDOID, bool>(RejectRpc, RejectedRpc);
            rpc.Register<string, ZDOID, ZDOID>(FocusRejectRpc, FocusRejectedRpc);
        }
        private static void ResetSession()
        {
            if (Session == ZNet.instance) return;
            Receipts.Clear(); FocusByAuthor.Clear(); SentCommands.Clear(); Session = ZNet.instance;
            ResetFocus(); LocalOwner = null;
        }
        private static void ResetFocus()
        { Aimed = default; Held = NextAim = FocusRequestedAt = FocusAcknowledgedAt = 0f; FocusToken = null; Awaiting = FocusAcknowledged = false; SummonDismissHud.Hide(); }
        internal static bool Allowed(ZDO data)
        {
            GameObject prefab = data == null ? null : ZNetScene.instance?.GetPrefab(data.GetPrefab());
            if (prefab?.GetComponent<MonsterAI>() == null || data.GetLong(AuthorKey, 0) == 0) return false;
            // Never capture hostile skeletons just because their name starts with Skeleton.
            return prefab.name == "Skeleton_Friendly" || prefab.name == "Skeleton_Friendly_Archer" ||
                prefab.name == "VM_MuspelSurtling70" || prefab.name == "VM_Torbjorn70";
        }
        private static string StaffFor(ZDO data) => data?.GetPrefab() == "VM_MuspelSurtling70".GetStableHashCode()
            ? "StaffFireball" : "StaffSkeleton";
        private static bool HasMastery(ZRpc rpc, WorkshopActor actor, ZDO data)
        {
            bool fire = StaffFor(data) == "StaffFireball";
            var skill = fire ? Skills.SkillType.ElementalMagic : Skills.SkillType.BloodMagic;
            int level = fire || data.GetPrefab() == "VM_Torbjorn70".GetStableHashCode() ? 70 : 35;
            return rpc == null ? PerkRuntimeService.HasPerk(Player.m_localPlayer, skill, level) :
                OwnerSkillAuthority.Has(rpc, actor.CharacterId, actor.GetPlayerID(), skill, level);
        }
        internal static void PortalRequest(ZDOID id, Vector3 point, string token)
        {
            ResetSession();
            Player player = Player.m_localPlayer;
            if (!MagicSkillPassives.OwnerReady(player) || player.IsTeleporting()) return;
            Trace("portal request id=" + id + " token=" + token);
            Send(0, id, point, token, 0);
        }
        private static void Send(int action, ZDOID id, Vector3 point, string token, int staff)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null) return;
            if (action != 1)
            {
                PrunePending();
                if (!SentCommands.ContainsKey(token) && SentCommands.Count >= 64) return;
                SentCommands[token] = new Receipt { Author = player.GetPlayerID(), Source = id, Expires = Time.time + 60f };
            }
            OwnerSkillAuthority.SendNow();
            ZPackage packet = new ZPackage(); packet.Write(action); packet.Write(player.GetZDOID());
            packet.Write(id); packet.Write(point); packet.Write(token); packet.Write(staff);
            if (ZNet.instance.IsServer()) Receive(null, packet);
            else ZNet.instance.GetServerRPC()?.Invoke(RequestRpc, packet);
        }
        private static void Receive(ZRpc rpc, ZPackage packet)
        {
            if (ZNet.instance?.IsServer() != true || packet == null || packet.Size() > 256) return;
            ResetSession(); packet.SetPos(0);
            int action, staff; ZDOID casterId, id; Vector3 point; string token;
            try { action = packet.ReadInt(); casterId = packet.ReadZDOID(); id = packet.ReadZDOID();
                point = packet.ReadVector3(); token = packet.ReadString(); staff = packet.ReadInt(); } catch { return; }
            WorkshopActor actor = WorkshopActor.Resolve(rpc);
            if (action < 0 || action > 2 || !Guid.TryParseExact(token, "N", out _) || actor == null ||
                actor.CharacterId != casterId || !actor.Available || actor.IsDead() || actor.IsTeleporting() ||
                (rpc != null && ZNet.instance.GetPeer(rpc)?.IsReady() != true))
            { Trace("reject action=" + action + " stage=actor-binding token=" + token); RejectRequest(rpc, action, token, casterId, id); return; }
            long author = actor.GetPlayerID();
            if (Receipts.TryGetValue(token, out Receipt receipt))
            {
                if (receipt.Author == author && receipt.Source == id) Reply(rpc, token, id, receipt.Replacement);
                return;
            }
            if (Receipts.Count >= 4096) return; // Keep exactly-once receipts for the whole world session.
            ZDO data = ZDOMan.instance?.GetZDO(id);
            bool allowedSource = Allowed(data);
            long sourceAuthor = data?.GetLong(AuthorKey, 0) ?? 0;
            float sourceHealth = data?.GetFloat("health", 1f) ?? 0f;
            if (!allowedSource || sourceAuthor != author || sourceHealth <= 0f)
            {
                string prefabName = data != null ? ZNetScene.instance?.GetPrefab(data.GetPrefab())?.name ?? "unknown" : "missing";
                Trace("reject action=" + action + " stage=source-binding id=" + id + " author=" + author +
                    " sourceAuthor=" + sourceAuthor + " health=" + sourceHealth + " allowed=" + allowedSource + " prefab=" + prefabName);
                RejectRequest(rpc, action, token, casterId, id,
                    discardPortalFollower: action == 0 && (data == null || sourceHealth <= 0f));
                return;
            }
            // Losing a few skill levels after death must not trap an existing
            // owned summon. Dismissal needs its matching staff, not a new unlock.
            if (action == 0 && !HasMastery(rpc, actor, data))
            { Trace("reject action=portal stage=mastery id=" + id + " author=" + author); RejectRequest(rpc, action, token, casterId, id); return; }
            if (action != 0)
            {
                ZDO caster = ZDOMan.instance.GetZDO(casterId);
                int requiredStaff = StaffFor(data).GetStableHashCode();
                Player hostActor = rpc == null ? Player.m_localPlayer : null;
                bool exactHostActor = hostActor != null && hostActor.GetZDOID() == casterId && hostActor.GetPlayerID() == author;
                int heldQuality = exactHostActor
                    ? hostActor.GetCurrentWeapon()?.m_quality ?? 0
                    : Magic70Carrier.GetEquippedStaffQuality(caster, requiredStaff);
                bool correctStaff = exactHostActor
                    ? PerkRuntimeService.ItemPrefabName(hostActor.GetCurrentWeapon()) == StaffFor(data)
                    : Magic70Carrier.HasEquippedStaff(caster, requiredStaff) && heldQuality > 0;
                if (staff != requiredStaff || !correctStaff ||
                    (data.GetPosition() - actor.Position).sqrMagnitude > 15f * 15f)
                { Trace("reject action=" + action + " stage=staff-or-range id=" + id + " author=" + author); RejectRequest(rpc, action, token, casterId, id); return; }
                if (action == 1)
                {
                    FocusByAuthor[author] = new Focus { Target = id, Staff = staff, Token = token, Started = Time.time };
                    Trace("focus accepted id=" + id + " author=" + author + " token=" + token);
                    if (rpc == null) OnFocusAcknowledged(token, casterId, id);
                    else rpc.Invoke(FocusAckRpc, token, casterId, id);
                    return;
                }
                bool hasFocus = FocusByAuthor.TryGetValue(author, out Focus focus);
                float elapsed = hasFocus ? Time.time - focus.Started : -1f;
                bool targetMatch = hasFocus && focus.Target == id;
                bool staffMatch = hasFocus && focus.Staff == staff;
                bool tokenMatch = hasFocus && focus.Token == token;
                bool durationMatch = hasFocus && elapsed + DismissDurationEpsilon >= DismissHoldSeconds && elapsed <= 12f;
                if (!targetMatch || !staffMatch || !tokenMatch || !durationMatch)
                {
                    Trace("reject action=dismiss stage=focus-or-duration id=" + id + " author=" + author +
                        " hasFocus=" + hasFocus + " targetMatch=" + targetMatch + " staffMatch=" + staffMatch +
                        " tokenMatch=" + tokenMatch + " elapsed=" + elapsed.ToString("F3") +
                        " required=" + DismissHoldSeconds.ToString("F3") + " epsilon=" + DismissDurationEpsilon.ToString("F3"));
                    RejectRequest(rpc, action, token, casterId, id); return;
                }
                FocusByAuthor.Remove(author);
                // Despawn is not death: no loot, explosion or post-death surcharge.
                Receipts[token] = new Receipt { Author = author, Source = id, Replacement = default };
                Retire(data);
                Reply(rpc, token, id, default);
                Trace("dismissed " + id);
                return;
            }
            if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z) ||
                (point - actor.Position).sqrMagnitude > 8f * 8f ||
                (data.GetPosition() - actor.Position).sqrMagnitude < 25f * 25f)
            { Trace("reject action=portal stage=range id=" + id + " author=" + author); RejectRequest(rpc, action, token, casterId, id); return; }
            // Snapshot all native creature state, including equipment, health and
            // stable cargo link. Do not call death or rebuild a naked prefab.
            ZPackage state = new ZPackage(); data.Serialize(state); state.SetPos(0);
            if (state.Size() > 65536) return;
            int prefab = data.GetPrefab();
            ZDO replacement = ZDOMan.instance.CreateNewZDO(point, prefab);
            try
            {
                replacement.Deserialize(state); replacement.SetPosition(point);
                replacement.Set("vm.summon.replaced", id);
                replacement.Set(AuthorKey, author);
                replacement.SetOwner(rpc == null ? ZNet.GetUID() : ZNet.instance.GetPeer(rpc).m_uid);
                Receipts[token] = new Receipt { Author = author, Source = id, Replacement = replacement.m_uid };
            }
            catch (Exception error)
            {
                replacement.SetOwner(ZNet.GetUID()); ZDOMan.instance.DestroyZDO(replacement);
                MasteryPlugin.Log.LogWarning("[SummonRoster] copy failed; original retained: " + error.GetType().Name);
                return;
            }
            ZDOID newId = replacement.m_uid;
            if (prefab == Magic70Carrier.PrefabName.GetStableHashCode())
            {
                // The bag lives independently. Only update its body reference;
                // never move or take ownership of an inventory currently open.
                ZDO cargo = ZDOMan.instance.GetZDO(replacement.GetZDOID(Magic70Carrier.CargoKey));
                if (cargo?.GetPrefab() == Magic70Carrier.CargoPrefabName.GetStableHashCode() &&
                    cargo.GetLong(Magic70Carrier.CargoAuthorKey, 0) == author)
                {
                    // The native inventory payload remains on this independent
                    // persistent cargo ZDO. Relocate only the closed proxy; never
                    // mutate items or move a chest while a client has it open.
                    int inUse = cargo.GetInt(ZDOVars.s_inUse, 0);
                    if (inUse == 0)
                    {
                        cargo.Set(Magic70Carrier.CargoCarrierKey, newId);
                        cargo.SetPosition(point);
                        Container proxy = ZNetScene.instance?.FindInstance(cargo.m_uid)?.GetComponent<Container>();
                        if (proxy != null)
                        {
                            proxy.transform.position = point;
                            proxy.GetComponent<ZSyncTransform>()?.SyncNow();
                        }
                        ZDOMan.instance.ForceSendZDO(cargo.m_uid);
                        byte[] itemBytes = cargo.GetByteArray(ZDOVars.s_items, null);
                        Trace("portal cargo rebound carrier=" + newId + " cargo=" + cargo.m_uid +
                            " open=0 itemBytes=" + (itemBytes == null ? 0 : itemBytes.Length));
                    }
                    else Trace("portal cargo left-in-use carrier=" + newId + " cargo=" + cargo.m_uid + " inUse=" + inUse);
                }
                else Trace("portal cargo link-invalid carrier=" + newId + " cargo=" + replacement.GetZDOID(Magic70Carrier.CargoKey));
            }
            Retire(data); // Flush native tombstone before publishing the new body.
            ZDOMan.instance.ForceSendZDO(newId);
            if (rpc != null) ZDOMan.instance.ForceSendZDO(ZNet.instance.GetPeer(rpc).m_uid, newId);
            Reply(rpc, token, id, newId);
            Trace("portal replacement " + id + " -> " + newId);
        }
        private static void Retire(ZDO data)
        {
            // Explicit server-only retirement of this already-authorized summon.
            // Native tombstones remove the old model on every peer, no OnDeath.
            data.SetOwner(ZNet.GetUID()); ZDOMan.instance.DestroyZDO(data); ZDOMan.instance.SendDestroyed();
        }
        private static void Reply(ZRpc rpc, string token, ZDOID oldId, ZDOID newId)
        { if (rpc == null) Finish(token, oldId, newId); else rpc.Invoke(ResultRpc, token, oldId, newId); }
        private static void Result(ZRpc rpc, string token, ZDOID oldId, ZDOID newId)
        { if (ZNet.instance?.IsServer() == false && rpc == ZNet.instance.GetServerRPC()) Finish(token, oldId, newId); }
        private static void FocusAcknowledgedRpc(ZRpc rpc, string token, ZDOID actorId, ZDOID targetId)
        {
            if (ZNet.instance?.IsServer() == false && rpc == ZNet.instance.GetServerRPC())
                OnFocusAcknowledged(token, actorId, targetId);
        }
        private static void RejectRequest(ZRpc rpc, int action, string token, ZDOID actorId, ZDOID targetId, bool discardPortalFollower = false)
        {
            if (!Guid.TryParseExact(token, "N", out _)) return;
            if (action == 1)
            {
                if (rpc == null) OnFocusRejected(token, actorId, targetId);
                else rpc.Invoke(FocusRejectRpc, token, actorId, targetId);
            }
            else if (action == 0 || action == 2)
            {
                if (rpc == null) OnRejected(token, actorId, targetId, discardPortalFollower);
                else rpc.Invoke(RejectRpc, token, actorId, targetId, discardPortalFollower);
            }
        }
        private static void RejectedRpc(ZRpc rpc, string token, ZDOID actorId, ZDOID targetId, bool discardPortalFollower)
        {
            if (ZNet.instance?.IsServer() == false && rpc == ZNet.instance.GetServerRPC())
                OnRejected(token, actorId, targetId, discardPortalFollower);
        }
        private static void FocusRejectedRpc(ZRpc rpc, string token, ZDOID actorId, ZDOID targetId)
        {
            if (ZNet.instance?.IsServer() == false && rpc == ZNet.instance.GetServerRPC())
                OnFocusRejected(token, actorId, targetId);
        }
        private static void OnFocusRejected(string token, ZDOID actorId, ZDOID targetId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || Session != ZNet.instance || LocalOwner != player || token == null ||
                token != FocusToken || actorId != player.GetZDOID() || targetId != Aimed) return;
            Trace("focus rejected id=" + targetId + " token=" + token);
            ResetFocus();
            NextAim = Time.time + .5f;
        }
        private static void OnRejected(string token, ZDOID actorId, ZDOID targetId, bool discardPortalFollower)
        {
            Player player = Player.m_localPlayer;
            if (player == null || token == null || actorId != player.GetZDOID() ||
                !SentCommands.TryGetValue(token, out Receipt sent) || sent.Author != player.GetPlayerID() || sent.Source != targetId) return;
            SentCommands.Remove(token);
            bool focusReject = token == FocusToken;
            if (!focusReject) Skeleton35Travel.PortalRejected(targetId, discardPortalFollower);
            if (focusReject) ResetFocus();
            Trace("request rejected id=" + targetId + " token=" + token);
        }
        private static void OnFocusAcknowledged(string token, ZDOID actorId, ZDOID targetId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || Session != ZNet.instance || LocalOwner != player || token == null || token != FocusToken || targetId != Aimed ||
                actorId != player.GetZDOID() || Awaiting) return;
            FocusAcknowledged = true;
            FocusAcknowledgedAt = Time.time;
            Held = 0f;
            Trace("focus acknowledged id=" + targetId + " token=" + token);
        }
        private static bool FocusTargetStillAimed(Player player)
        {
            Character target = ZNetScene.instance?.FindInstance(Aimed)?.GetComponent<Character>();
            if (player == null || target == null || target.IsDead() || target.GetZDOID() != Aimed) return false;
            Vector3 eye = player.GetEyePoint();
            Vector3 point = target.transform.position + Vector3.up;
            Vector3 toTarget = point - eye;
            if (toTarget.sqrMagnitude > 15f * 15f || Vector3.Dot(player.GetAimDir(eye).normalized, toTarget.normalized) < .90f)
                return false;
            int mask = LayerMask.GetMask("character", "character_net", "terrain", "static_solid", "piece", "Default");
            if (!Physics.Raycast(eye, toTarget.normalized, out RaycastHit hit, toTarget.magnitude + .25f,
                mask, QueryTriggerInteraction.Ignore)) return false;
            return hit.collider?.GetComponentInParent<Character>()?.GetZDOID() == Aimed;
        }
        private static void Finish(string token, ZDOID oldId, ZDOID newId)
        {
            if (!SentCommands.TryGetValue(token, out Receipt sent) || sent.Source != oldId ||
                Player.m_localPlayer?.GetPlayerID() != sent.Author) return;
            SentCommands.Remove(token);
            Skeleton35Travel.Replaced(oldId, newId);
            if (token == FocusToken) ResetFocus();
        }
        internal static void Tick(Player player)
        {
            if (player != Player.m_localPlayer) return;
            ResetSession();
            PrunePending();
            if (LocalOwner != player) { ResetFocus(); SentCommands.Clear(); LocalOwner = player; }
            if (!MagicSkillPassives.OwnerReady(player) || player.IsTeleporting() || !player.m_blocking ||
                player.InAttack() || player.IsStaggering() || player.InMinorAction()) { ResetFocus(); return; }
            string equipped = PerkRuntimeService.ItemPrefabName(player.GetCurrentWeapon());
            if (equipped != "StaffSkeleton" && equipped != "StaffFireball") { ResetFocus(); return; }
            if (FocusToken != null && !FocusTargetStillAimed(player))
            { Trace("focus cancelled; target left aim cone or line of sight id=" + Aimed); ResetFocus(); NextAim = Time.time + .25f; return; }
            // Once focus is requested, keep the selected summon locked through
            // ACK and hold. Overlapping followers otherwise make raycast aim
            // oscillate and reset the progress bar every 100 ms.
            if (FocusToken == null && Time.time >= NextAim)
            {
                NextAim = Time.time + .1f;
                Vector3 eye = player.GetEyePoint();
                Character target = Physics.Raycast(eye, player.GetAimDir(eye), out RaycastHit hit, 15f,
                    LayerMask.GetMask("character", "character_net", "terrain", "static_solid", "piece", "Default"),
                    QueryTriggerInteraction.Ignore) ? hit.collider?.GetComponentInParent<Character>() : null;
                ZDO data = target?.m_nview?.GetZDO();
                ZDOID aim = target != null && !target.IsDead() && Allowed(data) &&
                    data.GetLong(AuthorKey, 0) == player.GetPlayerID() && StaffFor(data) == equipped ? data.m_uid : default;
                if (aim != Aimed)
                {
                    ResetFocus(); Aimed = aim;
                    if (aim != default)
                    { FocusToken = Guid.NewGuid().ToString("N"); FocusRequestedAt = Time.time; Send(1, Aimed, Vector3.zero, FocusToken, equipped.GetStableHashCode()); }
                }
            }
            if (Aimed == default || Awaiting || !FocusAcknowledged)
            {
                if (Aimed != default && !FocusAcknowledged && Time.time - FocusRequestedAt > 5f)
                { Trace("focus ACK timeout id=" + Aimed + " token=" + FocusToken); ResetFocus(); }
                return;
            }
            // Measure elapsed time after ACK, not summed frame deltas. The ACK
            // may arrive inside this very Tick; adding that frame's delta counts
            // time BEFORE admission and sends completion early on a local host.
            Held = Mathf.Max(0f, Time.time - FocusAcknowledgedAt);
            SummonDismissHud.Show(Held / DismissHoldSeconds);
            // Allow measured server hold time to complete even if request RTT
            // shifts start receipt slightly. One request only; no per-frame RPC.
            if (Held >= DismissHoldSeconds)
            { Awaiting = true; Trace("dismiss requested id=" + Aimed + " token=" + FocusToken); Send(2, Aimed, Vector3.zero, FocusToken, equipped.GetStableHashCode()); }
        }
        private static void Trace(string message)
        { if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogInfo("[SummonRoster] " + message); }
        private static void PrunePending()
        {
            if (Time.time < NextPrune) return;
            NextPrune = Time.time + 2f; ExpiredCommands.Clear();
            foreach (var pair in SentCommands) if (Time.time >= pair.Value.Expires) ExpiredCommands.Add(pair.Key);
            foreach (string token in ExpiredCommands) SentCommands.Remove(token);
            ExpiredCommands.Clear();
        }
    }
    internal sealed class SummonDismissHud : MonoBehaviour
    {
        private static SummonDismissHud Instance;
        private Image Fill;
        private CanvasGroup Group;
        private float Seen;
        internal static void Hide() { if (Instance != null) Instance.Group.alpha = 0f; }
        internal static void Show(float progress)
        {
            if (Hud.instance == null) return;
            if (Instance == null)
            {
                var root = new GameObject("VM_SummonDismissHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
                root.transform.SetParent(Hud.instance.transform, false);
                Instance = root.AddComponent<SummonDismissHud>(); Instance.Build();
            }
            Instance.Seen = Time.unscaledTime; Instance.Group.alpha = 1f;
            var rect = Instance.Fill.rectTransform; rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f); rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private void Build()
        {
            var canvas = GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2200;
            var scaler = GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            Group = GetComponent<CanvasGroup>(); Group.blocksRaycasts = Group.interactable = false;
            var bar = new GameObject("Focus", typeof(RectTransform), typeof(Image)); bar.transform.SetParent(transform, false);
            var rect = bar.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, -100); rect.sizeDelta = new Vector2(190, 7);
            bar.GetComponent<Image>().color = new Color(.12f, .04f, .16f, .8f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(bar.transform, false);
            Fill = fill.GetComponent<Image>(); Fill.color = new Color(.7f, .3f, 1f, .9f);
            var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(bar.transform, false);
            var label = text.GetComponent<TextMeshProUGUI>(); label.fontSize = 17; label.alignment = TextAlignmentOptions.Center;
            label.text = Localization.instance?.GetSelectedLanguage() == "Ukrainian" ? "Відпустити супутника" : "Release companion";
            label.rectTransform.sizeDelta = new Vector2(260, 26); label.rectTransform.anchoredPosition = new Vector2(0, 18);
            if (MessageHud.instance?.m_messageCenterText?.font != null) label.font = MessageHud.instance.m_messageCenterText.font;
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        }
        private void Update() { if (Time.unscaledTime - Seen > .25f) Group.alpha = 0f; }
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class SummonRosterInputPatch
    { private static void Postfix(Player __instance) => SummonRosterCommands.Tick(__instance); }
}
