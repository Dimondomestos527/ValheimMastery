using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{

    // Replacement Blood 70 secondary: authenticated server admission, followed
    // by a command to the target's current network owner. Target motion and the
    // one direct release hit are never performed by a non-owner.
    internal static class Magic70BloodDome
    {
        private const string RequestRpc = "VM_BloodCageRequest";
        private const string TargetRpc = "VM_BloodCageTarget";
        private const string TargetResultRpc = "VM_BloodCageTargetResult";
        private const string ResultRpc = "VM_BloodCageResult";
        private const string VisualRpc = "VM_BloodCageVisual";
        internal const string RuptureRpc = "VM_BloodCageRupture";
        private const string CooldownId = "magic70_bloodcage";
        private const float EitrCost = 40f, Cooldown = 15f, CaptureRange = 15f, Duration = 15f;
        internal const string AuthorKey = "vm.bloodcage.author", CasterKey = "vm.bloodcage.caster";
        internal const string CapacityKey = "vm.bloodcage.capacity", UntilKey = "vm.bloodcage.until";
        private sealed class Lease { internal string Token; internal long Author, RequesterOwner; internal ZDOID Target; internal long TargetOwner; internal long Until, CooldownUntil; internal int Quality; internal ZRpc RequestRpc; }
        private static readonly Dictionary<long, Lease> ByCaster = new Dictionary<long, Lease>();
        private static readonly Dictionary<string, Lease> ByToken = new Dictionary<string, Lease>();
        private static readonly Dictionary<long, long> Cooldowns = new Dictionary<long, long>();
        private static readonly Dictionary<ZDOID, string> RuptureTokens = new Dictionary<ZDOID, string>();
        private static string Pending;
        private static float PendingAt, PendingCost;
        private static float NextLeaseSweep;
        private static ZNet Session;
        private static void CheckSession()
        {
            if (Session == ZNet.instance) return;
            ByCaster.Clear(); ByToken.Clear(); Cooldowns.Clear(); RuptureTokens.Clear(); Pending = null; PendingCost = 0f;
            NextLeaseSweep = 0f; Session = ZNet.instance;
        }

        internal static void Register(ZRpc rpc)
        {
            if (rpc == null) return;
            rpc.Register<ZPackage>(RequestRpc, ReceiveRequest);
            rpc.Register<ZPackage>(TargetResultRpc, ReceiveTargetResult);
            rpc.Register<ZPackage>(ResultRpc, ReceiveResult);
        }

        internal static bool Input(Player player)
        {
            CheckSession();
            if (player == null || !MagicSkillPassives.OwnerReady(player) ||
                PerkRuntimeService.ItemPrefabName(player.GetCurrentWeapon()) != "StaffShield" ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.BloodMagic, 70) ||
                (!player.m_secondaryAttack && !player.m_secondaryAttackHold)) return true;
            Shield35Renewal.CancelForSecondary(player); FireStaff35Charge.Cancel();
            player.m_queuedSecondAttackTimer = 0f;
            if (!player.m_secondaryAttack || player.InAttack() || player.InMinorAction() || player.IsStaggering() ||
                player.IsTeleporting() || player.m_dodgeInvincible || player.m_blocking || Pending != null ||
                !player.HaveEitr(EitrCost) || PerkCooldownStateService.GetRemainingSeconds(player, CooldownId) > 0d) return false;
            Character target = AimTarget(player);
            if (!ValidEnemy(player, target))
            { Trace("aim rejected target=" + (target != null ? target.name : "none")); return false; }
            float skill = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.BloodMagic);
            float capacity = ShieldCapacity(skill);
            if (!float.IsFinite(capacity) || capacity <= 0f || capacity > 1000000f)
            { Trace("native shield definition unavailable"); return false; }
            Pending = Guid.NewGuid().ToString("N"); PendingAt = Time.time; PendingCost = EitrCost;
            player.UseEitr(EitrCost); OwnerSkillAuthority.SendNow();
            Magic70CastAnimation.Play(player);
            ZPackage request = new ZPackage(); request.Write(Pending); request.Write(player.GetZDOID());
            request.Write(player.GetPlayerID()); request.Write(target.GetZDOID());
            request.Write(player.GetCurrentWeapon().m_quality); request.Write(skill); request.Write(capacity);
            Trace("request=" + Pending + " target=" + target.GetZDOID() + " capacity=" + capacity);
            if (ZNet.instance.IsServer()) ReceiveRequest(null, request);
            else ZNet.instance.GetServerRPC()?.Invoke(RequestRpc, request);
            return false;
        }

        private static Character AimTarget(Player player)
        {
            Vector3 eye = player.GetEyePoint(), direction = player.GetAimDir(eye).normalized;
            if (!Physics.Raycast(eye, direction, out RaycastHit hit, CaptureRange,
                LayerMask.GetMask("character", "character_net", "terrain", "static_solid", "piece", "Default"),
                QueryTriggerInteraction.Ignore)) return null;
            return hit.collider?.GetComponentInParent<Character>();
        }
        private static bool ValidEnemy(Character caster, Character target) => caster != null && target != null && target != caster &&
            !target.IsDead() && !target.IsPlayer() && !target.IsTamed() && !target.IsBoss() && BaseAI.IsEnemy(caster, target) &&
            target.m_nview?.IsValid() == true &&
            (target.GetCenterPoint() - caster.GetCenterPoint()).sqrMagnitude <= CaptureRange * CaptureRange;

        internal static float ShieldCapacity(float skill)
        {
            // StaffShield is an ITEM identity, not the native status identity.
            // Select the real blood barrier definition rather than guessing a hash.
            SE_Shield shield = null;
            if (ObjectDB.instance != null)
                foreach (StatusEffect effect in ObjectDB.instance.m_StatusEffects)
                    if (effect is SE_Shield candidate && candidate.m_levelUpSkillOnBreak == Skills.SkillType.BloodMagic &&
                        candidate.m_absorbDamage > 0f) { shield = candidate; break; }
            return shield == null ? 0f : shield.m_absorbDamage + shield.m_absorbDamagePerSkillLevel * skill +
                shield.m_absorbDamageWorldLevel * Mathf.Max(0, Game.m_worldLevel);
        }

        private static void ReceiveRequest(ZRpc rpc, ZPackage package)
        {
            if (ZNet.instance?.IsServer() != true || package == null || package.Size() > 512) return;
            CheckSession();
            // Host-local calls pass the writer itself, whose cursor is at its END.
            // Remote deserialization starts at zero; normalize both paths explicitly.
            package.SetPos(0);
            Trace("received request bytes=" + package.Size() + " host=" + (rpc == null));
            string token; ZDOID casterId, targetId; long playerId; int quality; float skill, capacity;
            try { token = package.ReadString(); casterId = package.ReadZDOID(); playerId = package.ReadLong();
                targetId = package.ReadZDOID(); quality = package.ReadInt(); skill = package.ReadSingle(); capacity = package.ReadSingle(); }
            catch (Exception error) { Trace("request decode failed: " + error.GetType().Name); return; }
            ZNetPeer peer = rpc == null ? null : ZNet.instance.GetPeer(rpc);
            WorkshopActor actor = WorkshopActor.Resolve(rpc);
            if (!Guid.TryParseExact(token, "N", out _) || actor == null || (rpc != null && peer?.IsReady() != true) ||
                actor.CharacterId != casterId || actor.GetPlayerID() != playerId || !actor.Available || actor.IsDead() || actor.IsTeleporting() ||
                !OwnerSkillAuthority.Valid(skill) || skill < 70f || quality < 1 || quality > 4 || !float.IsFinite(capacity) || capacity <= 0f || capacity > 1000000f ||
                !(rpc == null ? PerkRuntimeService.HasPerk(Player.m_localPlayer, Skills.SkillType.BloodMagic, 70) :
                    OwnerSkillAuthority.Has(rpc, casterId, playerId, Skills.SkillType.BloodMagic, 70))) { Reply(token, rpc, false); return; }
            float authoritativeCapacity = ShieldCapacity(skill);
            if (!float.IsFinite(authoritativeCapacity) || authoritativeCapacity <= 0f || authoritativeCapacity > 1000000f ||
                Mathf.Abs(capacity - authoritativeCapacity) > Mathf.Max(1f, authoritativeCapacity * .02f))
            { Reply(token, rpc, false); return; }
            ZDO casterData = ZDOMan.instance?.GetZDO(casterId), targetData = ZDOMan.instance?.GetZDO(targetId);
            if (casterData == null || targetData == null || casterData.GetInt(ZDOVars.s_rightItem, 0) != "StaffShield".GetStableHashCode() ||
                casterData.GetInt(ZDOVars.s_rightItemQuality, 1) != quality ||
                Vector3.Distance(actor.Position, targetData.GetPosition()) > CaptureRange + 1f ||
                (rpc != null && casterData.GetOwner() != peer.m_uid)) { Reply(token, rpc, false); return; }
            PruneLeases();
            if (Cooldowns.TryGetValue(playerId, out long cooldownUntil) && ZNet.instance.GetTime().Ticks < cooldownUntil)
            { Reply(token, rpc, false); return; }
            if (ByCaster.ContainsKey(playerId) || ByToken.ContainsKey(token) || ByToken.Count >= 8) { Reply(token, rpc, false); return; }
            if (ZNetScene.instance?.GetPrefab(targetData.GetPrefab())?.GetComponent<Character>() == null)
            { Reply(token, rpc, false); return; }
            long targetOwner = targetData.GetOwner(); if (targetOwner == 0) { Reply(token, rpc, false); return; }
            if (targetOwner != ZNet.GetUID() && ZNet.instance.GetPeer(targetOwner)?.IsReady() != true)
            { Reply(token, rpc, false); return; }
            foreach (Lease active in ByToken.Values) if (active.Target == targetId) { Reply(token, rpc, false); return; }
            Lease lease = new Lease { Token = token, Author = playerId, RequesterOwner = rpc == null ? ZNet.GetUID() : peer.m_uid, Quality = quality,
                Target = targetId, TargetOwner = targetOwner, RequestRpc = rpc,
                Until = ZNet.instance.GetTime().AddSeconds(Duration).Ticks,
                CooldownUntil = ZNet.instance.GetTime().AddSeconds(Cooldown).Ticks };
            ByCaster[playerId] = lease; ByToken[token] = lease;
            ZPackage command = new ZPackage(); command.Write(token); command.Write(casterId); command.Write(playerId);
            command.Write(skill); command.Write(authoritativeCapacity); command.Write(lease.Until); command.Write(lease.RequesterOwner); command.Write(quality);
            ZRoutedRpc.instance.InvokeRoutedRPC(targetOwner, targetId, TargetRpc, command);
        }

        internal static void RegisterTarget(Character target)
        {
            ZNetView view = target?.m_nview; if (view?.IsValid() != true) return;
            WarmVisualAssets();
            view.Register<ZPackage>(TargetRpc, (sender, package) => ReceiveTarget(target, sender, package));
            view.Register<bool>(VisualRpc, (sender, start) =>
            { if (view.GetZDO()?.GetOwner() == sender) CageVisual(target, start); });
            view.Register<ZPackage>(RuptureRpc, (sender, package) => ReceiveRupture(target, sender, package));
            Resume(target);
        }
        private static void ReceiveRupture(Character target, long sender, ZPackage package)
        {
            ZNetView view = target?.m_nview;
            if (view?.IsValid() != true || view.GetZDO().GetOwner() != sender ||
                package == null || package.Size() > 64 || Player.m_localPlayer == null) return;
            package.SetPos(0);
            string token; try { token = package.ReadString(); } catch { return; }
            if (!Guid.TryParseExact(token, "N", out _)) return;
            string current = view.GetZDO().GetString("vm.bloodcage.token", "");
            if (current.Length != 0 && current != token) return;
            CheckSession();
            ZDOID id = target.GetZDOID();
            if (RuptureTokens.TryGetValue(id, out string previous) && previous == token) return;
            if (RuptureTokens.Count >= 64) RuptureTokens.Clear();
            RuptureTokens[id] = token;
            // Snapshot presentation before the release hit can destroy the target.
            // The recipient never deals damage or changes the cage's replicated state.
            Magic70BloodCageBurst.Create(target.GetCenterPoint(), target.transform.rotation, VisualScale(target));
            CageVisual(target, false);
        }
        private static void WarmVisualAssets()
        {
            NativeSoftVisualAssets.GetPrefab("fx_shield_start");
            NativeSoftVisualAssets.GetPrefab("vfx_StaffShield");
            NativeSoftVisualAssets.GetPrefab("fx_StaffShield_Break");
            NativeSoftVisualAssets.Get<StatusEffect>("Assets/GameElements/StatusEffects/Staff_shield.asset");
        }
        private static void ReceiveTarget(Character target, long sender, ZPackage package)
        {
            if (target?.m_nview?.IsValid() != true || !target.m_nview.IsOwner() || package == null || package.Size() > 512 || ZNet.instance == null) return;
            long serverUid = ZNet.instance.IsServer() ? ZNet.GetUID() : ZNet.instance.GetServerPeer()?.m_uid ?? 0;
            if (sender != serverUid) return;
            package.SetPos(0);
            string token = null; ZDOID casterId; long playerId, until, claimantOwner; int quality; float skill, capacity;
            try { token = package.ReadString(); casterId = package.ReadZDOID(); playerId = package.ReadLong();
                skill = package.ReadSingle(); capacity = package.ReadSingle(); until = package.ReadLong(); claimantOwner = package.ReadLong(); quality = package.ReadInt(); }
            catch { SendTargetResult("", target, false); return; }
            ZDO casterData = ZDOMan.instance?.GetZDO(casterId);
            Character caster = ZNetScene.instance?.FindInstance(casterId)?.GetComponent<Character>();
            ItemDrop.ItemData staff = (caster as Player)?.GetCurrentWeapon();
            bool valid = Guid.TryParseExact(token, "N", out _) && target.GetZDOID() == target.m_nview.GetZDO().m_uid &&
                ValidEnemy(caster, target) && casterData != null && casterData.GetLong(ZDOVars.s_playerID, 0) == playerId &&
                casterData.GetOwner() == claimantOwner && casterData.GetInt(ZDOVars.s_rightItem, 0) == "StaffShield".GetStableHashCode() &&
                casterData.GetInt(ZDOVars.s_rightItemQuality, 1) == quality &&
                (staff == null || (PerkRuntimeService.ItemPrefabName(staff) == "StaffShield" && staff.m_quality == quality)) &&
                OwnerSkillAuthority.Valid(skill) && skill >= 70f && float.IsFinite(capacity) && capacity > 0f && capacity <= 1000000f &&
                Mathf.Abs(ShieldCapacity(skill) - capacity) <= Mathf.Max(1f, capacity * .02f) &&
                ZNet.instance.GetTime().Ticks < until && (target.GetCenterPoint() - caster.GetCenterPoint()).sqrMagnitude <= CaptureRange * CaptureRange &&
                target.GetComponent<Magic70BloodCageHold>() == null;
            if (valid)
            {
                ZDO cageData = target.m_nview.GetZDO();
                cageData.Set(AuthorKey, playerId); cageData.Set(CasterKey, casterId);
                cageData.Set(CapacityKey, capacity);
                cageData.Set(UntilKey, until); cageData.Set("vm.bloodcage.token", token);
                Magic70BloodCageHold hold = target.gameObject.AddComponent<Magic70BloodCageHold>();
                hold.Begin(target, caster, token, playerId, capacity, until);
                target.m_nview.InvokeRPC(ZNetView.Everybody, VisualRpc, true);
            }
            SendTargetResult(token, target, valid);
        }
        internal static void Resume(Character target)
        {
            ZDO data = target?.m_nview?.GetZDO();
            if (data == null || data.GetLong(AuthorKey, 0L) == 0 || ZNet.instance == null || data.GetLong(UntilKey, 0L) <= ZNet.instance.GetTime().Ticks) return;
            CageVisual(target, true);
            if (!target.m_nview.IsOwner() || target.GetComponent<Magic70BloodCageHold>() != null) return;
            ZDOID casterId = data.GetZDOID(CasterKey);
            Character caster = ZNetScene.instance?.FindInstance(casterId)?.GetComponent<Character>();
            target.gameObject.AddComponent<Magic70BloodCageHold>().Begin(target, caster,
                data.GetString("vm.bloodcage.token", ""), data.GetLong(AuthorKey, 0), data.GetFloat(CapacityKey, 0f), data.GetLong(UntilKey, 0L));
        }
        private static void SendTargetResult(string token, Character target, bool accepted)
        {
            if (string.IsNullOrEmpty(token) || target?.m_nview?.IsValid() != true) return;
            ZPackage packet = new ZPackage(); packet.Write(token); packet.Write(target.GetZDOID()); packet.Write(accepted);
            if (ZNet.instance.IsServer()) ReceiveTargetResult(null, packet);
            else ZNet.instance.GetServerRPC()?.Invoke(TargetResultRpc, packet);
        }
        private static void ReceiveTargetResult(ZRpc rpc, ZPackage packet)
        {
            if (ZNet.instance?.IsServer() != true || packet == null || packet.Size() > 256) return;
            packet.SetPos(0);
            string token; ZDOID targetId; bool accepted;
            try { token = packet.ReadString(); targetId = packet.ReadZDOID(); accepted = packet.ReadBool(); } catch { return; }
            if (!ByToken.TryGetValue(token, out Lease lease) || lease.Target != targetId) return;
            if (rpc == null ? lease.TargetOwner != ZNet.GetUID() : ZNet.instance.GetPeer(rpc)?.m_uid != lease.TargetOwner) return;
            if (!accepted) { ByToken.Remove(token); ByCaster.Remove(lease.Author); Reply(token, lease.RequestRpc, false); return; }
            Cooldowns[lease.Author] = lease.CooldownUntil;
            Reply(token, lease.RequestRpc, true);
        }
        private static void Reply(string token, ZRpc requestRpc, bool accepted)
        {
            Trace("result=" + token + " accepted=" + accepted);
            ZRpc client = requestRpc;
            if (client == null) { Finish(token, accepted); return; }
            ZPackage packet = new ZPackage(); packet.Write(token); packet.Write(accepted); client.Invoke(ResultRpc, packet);
        }
        private static void ReceiveResult(ZRpc rpc, ZPackage packet)
        {
            if (ZNet.instance?.IsServer() != false || rpc != ZNet.instance.GetServerRPC() || packet == null) return;
            packet.SetPos(0);
            try { Finish(packet.ReadString(), packet.ReadBool()); } catch { }
        }
        private static void Trace(string text)
        { if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogInfo("[BloodCage70] " + text); }
        private static void Finish(string token, bool accepted)
        {
            if (token != Pending || Player.m_localPlayer == null) return;
            Player player = Player.m_localPlayer; Pending = null;
            if (!accepted) { player.AddEitr(PendingCost); PendingCost = 0f; return; }
            PendingCost = 0f; PerkCooldownStateService.TryConsume(player, CooldownId, Cooldown);
            // The accepted target's native cast presentation includes the sound;
            // do not duplicate it at the caster or substitute a shield-hit sound.
        }
        private static void PruneLeases()
        {
            if (ZNet.instance == null) return; long now = ZNet.instance.GetTime().Ticks;
            List<string> remove = new List<string>();
            foreach (var pair in ByToken) if (now >= pair.Value.Until) remove.Add(pair.Key);
            foreach (string key in remove) { Lease value = ByToken[key]; ByToken.Remove(key); ByCaster.Remove(value.Author); }
            List<long> expiredCooldowns = new List<long>();
            foreach (var pair in Cooldowns) if (now >= pair.Value) expiredCooldowns.Add(pair.Key);
            foreach (long player in expiredCooldowns) Cooldowns.Remove(player);
        }
        internal static void Tick()
        {
            CheckSession();
            if (Time.time >= NextLeaseSweep) { NextLeaseSweep = Time.time + .5f; PruneLeases(); }
            if (Pending != null && Time.time - PendingAt > 8f)
            { Pending = null; PendingCost = 0f; } // Unknown outcome: never refund an accepted server request.
        }
        internal static void CageVisual(Character target, bool start)
        {
            if (!start) { target?.GetComponent<Magic70BloodCageVisual>()?.Stop(); return; }
            if (target == null || target.GetComponent<Magic70BloodCageVisual>() != null) return;
            target.gameObject.AddComponent<Magic70BloodCageVisual>().Begin(target);
        }
        internal static float VisualScale(Character target) => Mathf.Clamp(target.GetRadius() * 2f, .65f, 4f);
        internal static SE_Shield NativeShieldVisualDefinition()
        {
            var shared = NativePerkAssetResolver.Resolve("StaffShield")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
            if (shared?.m_attackStatusEffect is SE_Shield shield) return shield;
            return ObjectDB.instance?.GetStatusEffect("Staff_shield".GetStableHashCode()) as SE_Shield ??
                NativeSoftVisualAssets.Get<StatusEffect>("Assets/GameElements/StatusEffects/Staff_shield.asset") as SE_Shield;
        }
        internal static GameObject NativeShellPrefab()
        {
            foreach (var effect in NativeShieldVisualDefinition()?.m_startEffects?.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>())
                if (effect.m_enabled && effect.m_prefab?.name == "vfx_StaffShield") return effect.m_prefab;
            return NativePerkAssetResolver.Resolve("vfx_StaffShield");
        }
        internal static void CastFeedback(Vector3 point, Quaternion rotation, float scale, ref bool visual, ref bool sound)
        {
            if (!MasteryPlugin.Settings.Enabled.Value) { visual = sound = true; return; }
            var shared = NativePerkAssetResolver.Resolve("StaffShield")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
            EffectList trigger = shared?.m_attack?.m_triggerEffect;
            GameObject prefab = FindEffect(trigger, "fx_shield_start") ?? FindEffect(shared?.m_triggerEffect, "fx_shield_start") ??
                NativePerkAssetResolver.Resolve("fx_shield_start");
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) visual = true;
            if (!visual && SpawnCosmetic(prefab, point, rotation, scale, 2f)) visual = true;
            if (!MasteryPlugin.Settings.EnablePerkSFX.Value) sound = true;
            if (!sound) sound = PlayAudioList("bloodcage_cast", trigger, point, .65f) ||
                PlayAudioList("bloodcage_cast", shared?.m_triggerEffect, point, .65f) ||
                PerkAudioService.PlayPrefab("bloodcage_cast", prefab, point, .5f, .65f);
        }
        internal static void BreakFeedback(Vector3 point, Quaternion rotation, float scale, ref bool visual, ref bool sound)
        {
            if (!MasteryPlugin.Settings.Enabled.Value) { visual = sound = true; return; }
            EffectList effects = NativeShieldVisualDefinition()?.m_breakEffects;
            GameObject prefab = FindEffect(effects, "fx_StaffShield_Break") ?? NativePerkAssetResolver.Resolve("fx_StaffShield_Break");
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) visual = true;
            if (!visual && SpawnCosmetic(prefab, point, rotation, scale, 2.5f)) visual = true;
            if (!MasteryPlugin.Settings.EnablePerkSFX.Value) sound = true;
            if (!sound) sound = PlayAudioList("bloodcage_rupture", effects, point, .75f) ||
                PerkAudioService.PlayPrefab("bloodcage_rupture", prefab, point, .5f, .75f);
        }
        private static GameObject FindEffect(EffectList effects, string name)
        {
            foreach (var effect in effects?.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>())
                if (effect.m_enabled && effect.m_prefab?.name == name) return effect.m_prefab;
            return null;
        }
        private static bool PlayAudioList(string id, EffectList effects, Vector3 point, float volume)
        {
            foreach (var effect in effects?.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>())
                if (effect.m_enabled && effect.m_prefab != null && PerkAudioService.PlayPrefab(id, effect.m_prefab, point, .5f, volume)) return true;
            return false;
        }
        private static bool SpawnCosmetic(GameObject prefab, Vector3 point, Quaternion rotation, float scale, float lifetime)
        {
            if (prefab == null || Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value ||
                (Player.m_localPlayer.transform.position - point).sqrMagnitude > 80f * 80f) return false;
            GameObject instance = VfxPool.Spawn(prefab, point, rotation, lifetime);
            if (instance == null) return false;
            instance.transform.localScale *= scale;
            BoundParticles(instance, 256);
            instance.SetActive(true); instance.GetComponent<VfxPoolBaseline>()?.RestartParticles();
            return true;
        }
        internal static void BoundParticles(GameObject instance, int budget)
        {
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                int allowance = Mathf.Min(main.maxParticles, budget / (systems.Length - i));
                main.maxParticles = allowance; budget -= allowance;
                if (allowance != 0) continue;
                var emission = systems[i].emission; emission.enabled = false;
                systems[i].Clear(false);
            }
        }
    }

    internal sealed class Magic70BloodCageHold : MonoBehaviour
    {
        private Character _target, _caster; private string _token; private long _author, _until; private float _capacity;
        private bool Released;
        internal void Begin(Character target, Character caster, string token, long author, float capacity, long until)
        { _target = target; _caster = caster; _token = token; _author = author; _capacity = capacity; _until = until; }
        private void FixedUpdate()
        {
            if (_target?.m_nview?.IsValid() != true) return;
            if (!_target.m_nview.IsOwner()) { Destroy(this); return; }
            if (_target.IsDead() || _target.IsPlayer() || _target.IsTamed() || _target.IsBoss() ||
                (_caster != null && !BaseAI.IsEnemy(_caster, _target))) { Release(false); return; }
            Rigidbody body = _target.GetComponent<Rigidbody>();
            if (body != null) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            if (ZNet.instance == null || ZNet.instance.GetTime().Ticks < _until) return;
            Release(true);
        }
        internal bool Active => !Released && _target != null && _target.m_nview?.IsValid() == true && _target.m_nview.IsOwner() &&
            ZNet.instance != null && ZNet.instance.GetTime().Ticks < _until;
        internal static bool Owns(Character target)
        {
            if (target?.m_nview?.IsValid() != true || !target.m_nview.IsOwner()) return false;
            Magic70BloodDome.Resume(target);
            return target.GetComponent<Magic70BloodCageHold>()?.Active == true;
        }
        private void Release(bool explode)
        {
            if (Released || _target?.m_nview?.IsValid() != true || !_target.m_nview.IsOwner()) return;
            Released = true;
            // Clear replicated control before damage can kill/destroy the victim.
            _target.m_nview.GetZDO().Set(Magic70BloodDome.UntilKey, 0L);
            _target.m_nview.GetZDO().Set(Magic70BloodDome.AuthorKey, 0L);
            if (explode && !_target.IsDead() && !_target.IsPlayer() && !_target.IsTamed() && !_target.IsBoss() &&
                (_caster == null || BaseAI.IsEnemy(_caster, _target)))
            {
                ZPackage presentation = new ZPackage(); presentation.Write(_token);
                _target.m_nview.InvokeRPC(ZNetView.Everybody, Magic70BloodDome.RuptureRpc, presentation);
            }
            _target.m_nview.InvokeRPC(ZNetView.Everybody, "VM_BloodCageVisual", false);
            if (explode && !_target.IsDead() && !_target.IsPlayer() && !_target.IsTamed() && !_target.IsBoss() &&
                (_caster == null || BaseAI.IsEnemy(_caster, _target)))
            {
                HitData hit = new HitData { m_skill = Skills.SkillType.BloodMagic, m_point = _target.GetCenterPoint(),
                    m_dir = _caster == null ? Vector3.zero : (_target.GetCenterPoint() - _caster.GetCenterPoint()).normalized,
                    m_blockable = false, m_dodgeable = false, m_skillRaiseAmount = 0f, m_staggerMultiplier = 0f, m_variant = 1270 };
                hit.m_damage.m_blunt = _capacity * .75f; if (_caster != null) hit.SetAttacker(_caster);
                PerkHitContext context = PerkRuntimeService.GetHitContext(hit); context.IsPerkGenerated = true;
                context.PerkId = "magic70_bloodcage"; context.AllowSelfProc = false; context.AllowOtherPerkProc = false; context.XpMultiplier = 0f;
                _target.Damage(hit);
            }
            if (_target != null && _target.m_nview?.IsValid() == true)
                _target.m_nview.GetZDO().Set("vm.bloodcage.token", "");
            Destroy(this);
        }
    }

    internal sealed class Magic70BloodCageVisual : MonoBehaviour
    {
        private Character _target;
        private GameObject Shell;
        private float RetryAt;
        private float CastUntil, NextCastAttempt;
        private bool CastVisual, CastSound;
        internal void Begin(Character target) { _target = target; RetryAt = 0f; CastUntil = Time.time + 2f; }
        private void TryCreate()
        {
            if (Shell != null || Time.time < RetryAt || Player.m_localPlayer == null) return;
            RetryAt = Time.time + .5f;
            GameObject native = Magic70BloodDome.NativeShellPrefab();
            if (native == null) return; // Async native assets can arrive after the target RPC.
            Shell = PerkNativeFeedback.CreateVisualOnly(native);
            if (Shell == null) return;
            Shell.name = "VM_BloodCage_NativeShield";
            Shell.transform.SetParent(transform, false);
            Shell.transform.localScale *= Magic70BloodDome.VisualScale(_target);
            Magic70BloodDome.BoundParticles(Shell, 256);
            // Native red orb material/gradients and one-shot versus looping
            // particle settings are preserved; no recolored placeholder bubble.
            Shell.SetActive(true);
            GameObject readyShell = Shell;
            NativeVfxSafeFrame.AfterReady(readyShell, () =>
            {
                if (readyShell == null || !readyShell.activeInHierarchy) return;
                foreach (ParticleSystem particles in readyShell.GetComponentsInChildren<ParticleSystem>(true))
                    particles.Play(false);
            });
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[BloodCage70] native shield shell created on " + _target.GetZDOID());
        }
        private void Update()
        {
            ZDO data = _target?.m_nview?.GetZDO();
            if (data == null || _target.IsDead() || ZNet.instance == null ||
                data.GetLong(Magic70BloodDome.UntilKey, 0L) <= ZNet.instance.GetTime().Ticks)
            { Stop(); return; }
            if (Time.time <= CastUntil && Time.time >= NextCastAttempt && (!CastVisual || !CastSound))
            {
                NextCastAttempt = Time.time + .2f;
                Magic70BloodDome.CastFeedback(_target.GetCenterPoint(), _target.transform.rotation,
                    Magic70BloodDome.VisualScale(_target), ref CastVisual, ref CastSound);
            }
            bool visible = MasteryPlugin.Settings.Enabled.Value && MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (Shell != null) Shell.SetActive(visible);
            if (!visible) return;
            TryCreate();
            if (Shell != null) Shell.transform.position = _target.GetCenterPoint();
        }
        internal void Stop() { if (Shell != null) Shell.SetActive(false); Destroy(this); }
        private void OnDestroy()
        { if (Shell != null) Destroy(Shell); }
    }

    // Client-only snapshot survives a victim killed by the release hit and
    // retries pending SoftRef assets briefly without extending cage mechanics.
    internal sealed class Magic70BloodCageBurst : MonoBehaviour
    {
        private static int Active;
        private float Until, NextAttempt, Scale;
        private bool Visual, Sound, Counted;
        internal static void Create(Vector3 point, Quaternion rotation, float scale)
        {
            if (Active >= 16 || Player.m_localPlayer == null ||
                (Player.m_localPlayer.transform.position - point).sqrMagnitude > 80f * 80f) return;
            GameObject root = new GameObject("VM_BloodCageRupturePresentation");
            root.transform.SetPositionAndRotation(point, rotation);
            var burst = root.AddComponent<Magic70BloodCageBurst>();
            burst.Scale = scale; burst.Until = Time.time + 2f; burst.Counted = true; Active++;
            burst.TryPlay();
        }
        private void TryPlay()
        {
            NextAttempt = Time.time + .2f;
            Magic70BloodDome.BreakFeedback(transform.position, transform.rotation, Scale, ref Visual, ref Sound);
        }
        private void Update()
        {
            if (Time.time >= Until || (Visual && Sound)) { Destroy(gameObject); return; }
            if (Time.time >= NextAttempt) TryPlay();
        }
        private void OnDestroy() { if (Counted) { Counted = false; Active = Mathf.Max(0, Active - 1); } }
    }

    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class Magic70BloodCageCharacterPatch
    { private static void Postfix(Character __instance) => Magic70BloodDome.RegisterTarget(__instance); }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Magic70BloodCageTickPatch
    { private static void Postfix(Player __instance) { if (__instance == Player.m_localPlayer) Magic70BloodDome.Tick(); } }
    [HarmonyPatch(typeof(ZNet), "Update")]
    internal static class Magic70BloodCageServerTickPatch
    { private static void Postfix() { if (ZNet.instance?.IsServer() == true) Magic70BloodDome.Tick(); } }
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Magic70BloodCageInputPatch
    { [HarmonyPriority(Priority.First)] private static bool Prefix(Player __instance) => Magic70BloodDome.Input(__instance); }
    [HarmonyPatch(typeof(Character), "UpdateMotion")]
    internal static class Magic70BloodCageMotionPatch
    {
        private static bool Prefix(Character __instance)
        {
            Magic70BloodDome.Resume(__instance); // Also handles a live ownership handoff, not only a new spawn.
            if (!Magic70BloodCageHold.Owns(__instance)) return true;
            __instance.m_moveDir = Vector3.zero;
            return true; // Preserve native grounding, water and animation state updates.
        }
        private static void Postfix(Character __instance)
        {
            if (!Magic70BloodCageHold.Owns(__instance) || __instance.m_body == null) return;
            __instance.m_body.linearVelocity = Vector3.zero; __instance.m_body.angularVelocity = Vector3.zero;
        }
    }
}
