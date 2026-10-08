using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CrossbowTurretCommands
    {
        private static readonly Dictionary<ZRpc, float> LastRequest = new Dictionary<ZRpc, float>();
        private static ZNet Session;
        private static float LocalNext;
        internal static bool Ukrainian => Localization.instance?.GetSelectedLanguage() == "Ukrainian";
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<ZPackage>("VM_Crossbow70Command", Receive);
            rpc.Register<string>("VM_Crossbow70Result", Result);
        }
        private static void Result(ZRpc rpc, string text)
        {
            if (ZNet.instance?.IsServer() == false && rpc == ZNet.instance.GetServerRPC() && text?.Length <= 512)
                Feedback(text);
        }
        internal static bool Input(Player player)
        {
            if (player != Player.m_localPlayer || !MasteryPlugin.Settings.Enabled.Value || player.IsDead() ||
                player.IsTeleporting() || player.InAttack() || player.InDodge()) return false;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            Container target = player.GetHoverObject()?.GetComponentInParent<Container>();
            ZDO record = target?.m_nview?.GetZDO();
            bool personal = CrossbowTurretCustody.Marked(record) && record.GetLong(CrossbowTurretCustody.OwnerKey, 0) == player.GetPlayerID();
            bool bare = PerkRuntimeService.IsBareHands(weapon) && player.m_nview.GetZDO().GetInt(ZDOVars.s_leftItem, 0) == 0;
            bool crossbow = weapon?.m_shared?.m_skillType == Skills.SkillType.Crossbows && PerkRuntimeService.HasPerk(player, Skills.SkillType.Crossbows, 70);
            if (!(personal && bare) && !crossbow) return false;
            if (Time.time < LocalNext) return true;
            LocalNext = Time.time + .75f;
            var packet = new ZPackage();
            int operation = personal && bare ? (CrossbowTurretCustody.State(record) == CrossbowTurretState.Active ? 2 : 1) : 0;
            // Empty return/recovery can be removed without dropping/refunding anything.
            if (personal && bare && !CrossbowTurretContainerGuards.Locked(record) && target.GetInventory().NrOfItems() == 0) operation = 3;
            packet.Write(operation); packet.Write(personal ? record.m_uid : ZDOID.None);
            Vector3 point = player.transform.position + player.transform.forward * 2.5f;
            packet.Write(point); packet.Write(player.transform.forward);
            OwnerSkillAuthority.SendNow();
            if (ZNet.instance?.IsServer() == true)
            {
                // Direct host dispatch reuses the writer's stream; RPC deserialization normally resets it.
                packet.SetPos(0);
                Receive(null, packet);
            }
            else ZNet.instance?.GetServerRPC()?.Invoke("VM_Crossbow70Command", packet);
            return true;
        }
        private static void Reply(ZRpc rpc, string message)
        {
            if (rpc == null) Feedback(message);
            else rpc.Invoke("VM_Crossbow70Result", message);
        }
        private static void Feedback(string key)
        {
            string text;
            switch (key)
            {
                case "already_deployed": text = Ukrainian ? "Спершу прибери свою турель." : "Clear your existing turret first."; break;
                case "clearance": text = Ukrainian ? "Замало місця для турелі." : "Not enough clearance."; break;
                case "assets_unavailable": text = Ukrainian ? "Ресурси турелі недоступні." : "Turret assets unavailable."; break;
                case "cargo_locked": text = Ukrainian ? "Вміст заблоковано: відсутні ресурси предметів." : "Cargo locked: item definitions missing."; break;
                case "invalid_stock": text = Ukrainian ? "Потрібен один арбалет і сумісні болти." : "Supply one crossbow and compatible bolts."; break;
                case "close_container": text = Ukrainian ? "Закрий контейнер." : "Close the container."; break;
                case "handoff_failed": text = Ukrainian ? "Передача не завершена. Повтори запуск." : "Transfer incomplete. Retry activation."; break;
                default: return; // No raw technical status or success/tutorial banners.
            }
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, text);
        }
        internal static void CompleteFeedback(ZRpc rpc, string result)
        {
            if (result == "active" || result == "return_custody") return;
            Reply(rpc, result.StartsWith("custody_quarantined", StringComparison.Ordinal) ? "cargo_locked" :
                result.StartsWith("stock_rejected", StringComparison.Ordinal) || result == "invalid_cargo" ? "invalid_stock" : "handoff_failed");
        }
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        private static void Receive(ZRpc rpc, ZPackage packet)
        {
            ZNet net = ZNet.instance;
            if (net?.IsServer() != true || !MasteryPlugin.Settings.Enabled.Value || packet == null || packet.Size() > 128) return;
            if (Session != net) { Session = net; LastRequest.Clear(); }
            if (rpc != null)
            {
                if (LastRequest.TryGetValue(rpc, out float last) && Time.time - last < .5f) return;
                if (LastRequest.Count > 128) foreach (var key in new List<ZRpc>(LastRequest.Keys)) if (net.GetPeer(key) == null) LastRequest.Remove(key);
                LastRequest[rpc] = Time.time;
            }
            try
            {
                Player local = rpc == null ? Player.m_localPlayer : null;
                ZDO character = local?.m_nview?.GetZDO() ?? OwnerSkillAuthority.ResolveCharacterData(net.GetPeer(rpc));
                long owner = character?.GetLong(ZDOVars.s_playerID, 0) ?? 0;
                if (owner == 0 || character.GetBool(ZDOVars.s_dead, false)) return;
                int operation = packet.ReadInt(); ZDOID id = packet.ReadZDOID(); Vector3 point = packet.ReadVector3(), forward = packet.ReadVector3();
                if (!Finite(point) || !Finite(forward)) return;
                bool eligible = local != null ? OwnerSkillAuthority.Has(local, Skills.SkillType.Crossbows, 70) :
                    OwnerSkillAuthority.Has(rpc, character.m_uid, owner, Skills.SkillType.Crossbows, 70);
                if (operation == 0)
                {
                    if (!eligible || (point - character.GetPosition()).sqrMagnitude > 16f) return;
                    int equipped = character.GetInt(ZDOVars.s_rightItem, 0);
                    var held = ZNetScene.instance.GetPrefab(equipped)?.GetComponent<ItemDrop>();
                    if (local == null && held?.m_itemData?.m_shared?.m_skillType != Skills.SkillType.Crossbows) return;
                    foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
                        if (CrossbowTurretCustody.Marked(zdo) && zdo.GetLong(CrossbowTurretCustody.OwnerKey, 0) == owner)
                        { Reply(rpc, "already_deployed"); return; }
                    if (!Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 4f,
                        LayerMask.GetMask("terrain", "Default", "static_solid", "piece"), QueryTriggerInteraction.Ignore) ||
                        ground.collider.GetComponentInParent<Character>() != null || Vector3.Dot(ground.normal, Vector3.up) < .75f ||
                        !WorkshopNetworkStorage.WardAllows(owner, ground.point)) return;
                    // Verified scaled native root-box volume; exclude the supporting surface with a small bottom margin.
                    forward.y = 0f; if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
                    forward.Normalize();
                    if (Physics.CheckBox(ground.point + Vector3.up * .65f, new Vector3(.70f, .52f, .78f), Quaternion.LookRotation(forward.sqrMagnitude > .01f ? forward : Vector3.forward),
                        LayerMask.GetMask("terrain", "Default", "static_solid", "piece", "character", "character_net"), QueryTriggerInteraction.Ignore))
                    { Reply(rpc, "clearance"); return; }
                    GameObject donor = ZNetScene.instance.GetPrefab("piece_chest_wood");
                    if (donor?.GetComponent<Container>() == null || ZNetScene.instance.GetPrefab("piece_turret")?.GetComponent<Turret>() == null)
                    { Reply(rpc, "assets_unavailable"); return; }
                    GameObject instance = UnityEngine.Object.Instantiate(donor, ground.point, Quaternion.LookRotation(forward.normalized));
                    Container chest = instance.GetComponent<Container>(); ZDO record = chest.m_nview.GetZDO();
                    record.SetOwner(ZNet.GetUID()); record.Set(ZDOVars.s_creator, owner);
                    record.Set(CrossbowTurretCustody.OwnerKey, owner); record.Set(CrossbowTurretCustody.StateKey, (int)CrossbowTurretState.Supply);
                    record.Set(CrossbowTurretCustody.RevisionKey, 0); chest.Save();
                    record.Set(CrossbowTurretCustody.VersionKey, 1); record.Persistent = true;
                    CrossbowTurretRuntime.Attach(chest); ZDOMan.instance.ForceSendZDO(record.m_uid);
                    return;
                }
                ZDO current = ZDOMan.instance.GetZDO(id);
                if (!CrossbowTurretCustody.Marked(current) || current.GetLong(CrossbowTurretCustody.OwnerKey, 0) != owner ||
                    (character.GetPosition() - current.GetPosition()).sqrMagnitude > 36f || !WorkshopNetworkStorage.WardAllows(owner, current.GetPosition())) return;
                // Native visual-equipment ZDO is the authenticated owner's empty-hand pose.
                if (character.GetInt(ZDOVars.s_rightItem, 0) != 0 || character.GetInt(ZDOVars.s_leftItem, 0) != 0) return;
                if (operation == 1)
                {
                    if (current.GetBool(CrossbowTurretCustody.QuarantineKey, false))
                    {
                        // Only unlock after the original package becomes losslessly decodable again.
                        Container definition = ZNetScene.instance.GetPrefab(current.GetPrefab())?.GetComponent<Container>();
                        if (current.GetOwner() != ZNet.GetUID() || !CrossbowTurretStock.Lossless(current.GetByteArray(ZDOVars.s_items, null), definition))
                        { Reply(rpc, "cargo_locked"); return; }
                        current.Set(CrossbowTurretCustody.QuarantineKey, false);
                        current.Set(CrossbowTurretCustody.StateKey, (int)CrossbowTurretState.Recovery);
                        ZNetScene.instance.FindInstance(id)?.GetComponent<Container>()?.Load();
                    }
                    if (!eligible || !CrossbowTurretHandoff.Begin(rpc, current)) Reply(rpc, "invalid_stock");
                }
                else if (operation == 2 && current.GetOwner() == ZNet.GetUID() && !CrossbowTurretHandoff.IsPending(current))
                {
                    Container definition = ZNetScene.instance.GetPrefab(current.GetPrefab())?.GetComponent<Container>();
                    byte[] bytes = current.GetByteArray(ZDOVars.s_items, null);
                    if (CrossbowTurretCustody.TryTransition(current, current.GetInt(CrossbowTurretCustody.RevisionKey, -1), bytes, CrossbowTurretState.Return, definition, out _))
                    { /* Successful retirement is visible in hover/cargo state, no banner. */ }
                }
                else if (operation == 3 && !CrossbowTurretContainerGuards.Locked(current))
                {
                    if (!CrossbowTurretHandoff.Begin(rpc, current, true)) Reply(rpc, "close_container");
                }
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Crossbow70] Command rejected: " + error.GetType().Name); }
        }
        internal static bool RemoveEmpty(ZDO record)
        {
            if (ZNet.instance?.IsServer() != true || !CrossbowTurretCustody.Marked(record) ||
                record.GetOwner() != ZNet.GetUID() || CrossbowTurretContainerGuards.Locked(record)) return false;
            Container chest = ZNetScene.instance?.FindInstance(record.m_uid)?.GetComponent<Container>();
            if (chest == null || chest.IsInUse()) return false;
            byte[] bytes = record.GetByteArray(ZDOVars.s_items, null);
            if (!CrossbowTurretStock.Lossless(bytes, chest)) return false;
            chest.Load(); if (chest.GetInventory().NrOfItems() != 0) return false;
            var pack = new ZPackage(); chest.GetInventory().Save(pack);
            if (!System.Linq.Enumerable.SequenceEqual(pack.GetArray(), bytes)) return false;
            // Native WearNTear.Destroy would drop build resources; direct scene removal of verified EMPTY custody does not.
            ZNetScene.instance.Destroy(chest.gameObject); return true;
        }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    [HarmonyPriority(Priority.First)]
    internal static class CrossbowTurretSecondaryInputPatch
    {
        private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
        {
            if (!secondaryAttack || !(__instance is Player player) || !CrossbowTurretCommands.Input(player)) return true;
            __result = true; return false;
        }
    }
}
