#if MASTERY_SPEAR35_EXPERIMENT
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Controlled network slice, opt-in until two-player ownership tests pass.
    // Character skill follows vanilla owner trust; the server authorizes the
    // target, exact world item, movement owner, cooldown and completion token.
    internal static class Spear70HookService
    {
        internal static bool Enabled
        {
            get
            {
#if MASTERY_SPEAR70_EXPERIMENT
                return true;
#else
                return false;
#endif
            }
        }
        private sealed class Hook
        {
            internal string Token, MotionToken;
            internal long PlayerId, PeerUid, MoverUid;
            internal ZDOID Player, Target, Drop;
            internal float Expires;
            internal bool Active, Grapple;
        }
        private static readonly Dictionary<long, Hook> Hooks = new Dictionary<long, Hook>();
        private static readonly Dictionary<long, float> Cooldowns = new Dictionary<long, float>();
        private static readonly Dictionary<ZRpc, float> Requests = new Dictionary<ZRpc, float>();
        private static readonly HashSet<ZRpc> MotionPeers = new HashSet<ZRpc>();
        private static ZRpc _serverRpc;
        private static bool _serverSupportsMotion;
        private static int _helloAttempts;
        private static float _nextHello;
        internal static bool Ready => Enabled && ZNet.instance != null && (ZNet.instance.IsServer() ||
            (_serverSupportsMotion && _serverRpc == ZNet.instance.GetServerRPC()));
        private static ZNet _session;
        private static float _nextSweep;
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<int>("VM_SpearHookProtocol", Protocol);
            if (!Enabled) return;
            rpc.Register<ZPackage>("VM_SpearHookRequest", Request);
            rpc.Register<ZPackage>("VM_SpearHookCommand", Command);
        }
        private static void Protocol(ZRpc rpc, int version)
        {
            var net = ZNet.instance;
            if (net == null) return;
            if (net.IsServer())
            {
                if (net.GetPeer(rpc)?.IsReady() != true) return;
                bool compatible = Enabled && version == 3;
                if (compatible) MotionPeers.Add(rpc); else MotionPeers.Remove(rpc);
                rpc.Invoke("VM_SpearHookProtocol", compatible ? 3 : -1);
            }
            else if (rpc == net.GetServerRPC())
            { _serverRpc = rpc; _serverSupportsMotion = Enabled && version == 3; _helloAttempts = 3; }
        }
        internal static void Tick()
        {
            if (!Enabled) return;
            if (_session != ZNet.instance)
            {
                Hooks.Clear(); Cooldowns.Clear(); Requests.Clear(); MotionPeers.Clear();
                _serverRpc = null; _serverSupportsMotion = false; _helloAttempts = 0; _nextHello = 0f;
                _session = ZNet.instance; _nextSweep = 0f;
            }
            if (_session != null && !_session.IsServer() && NetworkSync.HasServerSettings)
            {
                var rpc = _session.GetServerRPC();
                if (_serverRpc != rpc) { _serverRpc = rpc; _serverSupportsMotion = false; _helloAttempts = 0; _nextHello = 0f; }
                if (rpc != null && !_serverSupportsMotion && _helloAttempts < 3 && Time.time >= _nextHello)
                { _helloAttempts++; _nextHello = Time.time + 3f; rpc.Invoke("VM_SpearHookProtocol", 3); }
            }
            if (_session == null || !_session.IsServer() || Time.time < _nextSweep) return;
            _nextSweep = Time.time + 1f;
            foreach (var pair in new List<KeyValuePair<long, Hook>>(Hooks))
            {
                var hook = pair.Value;
                var player = Find<Player>(hook.Player); var target = Find<Character>(hook.Target);
                var drop = Find<ItemDrop>(hook.Drop);
                if (!ValidAttachment(hook, player, target, drop)) Finish(hook, false);
                else if (hook.Active && Time.time > hook.Expires) Retry(hook);
            }
            foreach (var id in new List<long>(Cooldowns.Keys)) if (Time.time >= Cooldowns[id]) Cooldowns.Remove(id);
            foreach (var rpc in new List<ZRpc>(Requests.Keys)) if (_session.GetPeer(rpc) == null) Requests.Remove(rpc);
            MotionPeers.RemoveWhere(rpc => _session.GetPeer(rpc) == null);
        }
        internal static void Track(SpearThrowRecord record)
        {
            if (!Ready || record.Owner != Player.m_localPlayer || record.HitCharacter == null ||
                !PerkRuntimeService.HasPerk(record.Owner, Skills.SkillType.Spears, 70) ||
                !BaseAI.IsEnemy(record.Owner, record.HitCharacter)) return;
            var anchor = record.Owner.GetComponent<Spear70Attachment>();
            if (anchor == null) anchor = record.Owner.gameObject.AddComponent<Spear70Attachment>();
            anchor.Track(record);
        }
        internal static bool EmptyHands(Player player) => player != null && player.GetRightItem() == null && player.GetLeftItem() == null;
        internal static bool IsPulling(Player player) => EmptyHands(player) &&
            player.GetComponent<Spear70Attachment>()?.Busy == true;
        internal static bool OwnsSecondary(Player player) => EmptyHands(player) &&
            player.GetComponent<Spear70Attachment>()?.Attached == true;
        internal static void ReleaseForRecall(Player player) => player?.GetComponent<Spear70Attachment>()?.CancelForRecall();
        internal static bool TryBegin(Player player)
        {
            var record = SpearThrowLifecycleService.Latest(player);
            if (record == null || record.Returning || record.HitCharacter == null || record.HitCharacter.IsDead() ||
                !BaseAI.IsEnemy(player, record.HitCharacter) || !EmptyHands(player) ||
                player.IsDead() || player.IsTeleporting() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Spears, 70)) return false;
            // An attached spear owns this input, even while registration is pending.
            // Never silently fall through to recall or kick because of RPC latency.
            if (!Ready || record.DroppedItem == null)
            {
                Trace("hook registration pending");
                return true;
            }
            var anchor = player.GetComponent<Spear70Attachment>();
            if (anchor == null || !anchor.Tracks(record)) { Track(record); anchor = player.GetComponent<Spear70Attachment>(); }
            return anchor?.TryUse() == true;
        }
        internal static string Describe(Player player) => !Enabled ? "disabled" :
            !Ready ? "server-not-ready" : player?.GetComponent<Spear70Attachment>()?.Status ?? "no-attachment";
        internal static void Trace(string text)
        { if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogInfo("[Spear70] " + text); }
        internal static float PullSpeed(float level) => 10f * (1f + Mathf.Clamp(level, 0f, 100f) / 100f);
        internal static void Send(ZPackage package)
        {
            if (ZNet.instance == null) return;
            if (ZNet.instance.IsServer()) Request(null, new ZPackage(package.GetArray()));
            else ZNet.instance.GetServerRPC()?.Invoke("VM_SpearHookRequest", package);
        }
        private static void Deliver(long uid, ZPackage package)
        {
            if (uid == ZNet.GetUID()) Command(null, new ZPackage(package.GetArray()));
            else ZNet.instance.GetPeer(uid)?.m_rpc.Invoke("VM_SpearHookCommand", package);
        }
        internal static ZDOID Id(Component component) => component?.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None;
        internal static T Find<T>(ZDOID id) where T : Component => ZNetScene.instance?.FindInstance(id)?.GetComponent<T>();
        private static bool Alive(Character c) => c != null && !c.IsDead() && c.m_nview?.IsValid() == true;
        private static bool ValidAttachment(Hook hook, Player player, Character target, ItemDrop spear) =>
            Alive(player) && !player.IsTeleporting() && Alive(target) && BaseAI.IsEnemy(player, target) &&
            spear != null && spear.GetComponent<ZNetView>()?.GetZDO()?.GetOwner() == hook.PeerUid &&
            (hook.PeerUid == ZNet.GetUID() || ZNet.instance.GetPeer(hook.PeerUid)?.IsReady() == true);
        internal static bool Heavy(Character target)
        {
            if (target.IsBoss() || MasteryClassificationService.GetCreatureClass(target) == CreatureClass.Heavy) return true;
            // Trolls have less than the shared 1000-HP cutoff. Size also covers
            // Lox/golems and new large creatures without dragging them like greylings.
            return target.GetRadius() >= 0.8f || target.GetCollider()?.bounds.size.y >= 3f;
        }
        private static void Request(ZRpc rpc, ZPackage package)
        {
            ZNet net = ZNet.instance;
            if (!Enabled || net == null || !net.IsServer() || package == null || package.Size() > 512) return;
            try
            {
                int operation = package.ReadInt(); string token = package.ReadString();
                if (!Guid.TryParseExact(token, "N", out _)) return;
                long uid = rpc == null ? ZNet.GetUID() : net.GetPeer(rpc)?.m_uid ?? 0;
                if (uid == 0) return;
                if (operation == 2)
                {
                    bool arrived = package.ReadBool();
                    foreach (var hook in new List<Hook>(Hooks.Values))
                        if (hook.Active && hook.MotionToken == token && hook.MoverUid == uid)
                        {
                            var p = Find<Player>(hook.Player); var target = Find<Character>(hook.Target);
                            // Owner completion is not permission to strike from afar.
                            bool near = Alive(p) && Alive(target) &&
                                Vector3.Distance(p.GetCenterPoint(), target.GetCollider().ClosestPoint(p.GetCenterPoint())) <= 4f;
                            if (arrived && near) Finish(hook, true);
                            else if (ValidAttachment(hook, p, target, Find<ItemDrop>(hook.Drop))) Retry(hook);
                            else Finish(hook, false);
                            return;
                        }
                    return;
                }
                if (rpc != null)
                {
                    if (Requests.TryGetValue(rpc, out float last) && Time.time - last < 0.1f) return;
                    Requests[rpc] = Time.time;
                }
                var peer = rpc == null ? null : net.GetPeer(rpc);
                Player player = rpc == null ? Player.m_localPlayer : OwnerSkillAuthority.ResolvePlayer(peer);
                if (!Alive(player) || player.IsTeleporting() || !OwnerSkillAuthority.Has(player, Skills.SkillType.Spears, 70)) return;
                long playerId = player.GetPlayerID();
                if (operation == 0)
                {
                    ZDOID dropId = package.ReadZDOID(), targetId = package.ReadZDOID();
                    ItemDrop drop = Find<ItemDrop>(dropId); Character target = Find<Character>(targetId);
                    var dropZdo = drop?.GetComponent<ZNetView>()?.GetZDO();
                    if (dropZdo == null || dropZdo.GetOwner() != uid || dropId.UserID != uid || !Alive(target) ||
                        target.IsPlayer() || !BaseAI.IsEnemy(player, target) ||
                        drop.m_itemData?.m_shared?.m_skillType != Skills.SkillType.Spears ||
                        Vector3.Distance(drop.transform.position, target.GetCollider().ClosestPoint(drop.transform.position)) > 2f) return;
                    if (Hooks.TryGetValue(playerId, out var previous))
                    {
                        if (previous.Active) return;
                        if (previous.Token == token && previous.Drop == dropId && previous.Target == targetId)
                        { Reply(previous, 0, false); return; }
                    }
                    var hook = new Hook { Token = token, PlayerId = playerId, PeerUid = uid, Player = Id(player),
                        Target = targetId, Drop = dropId, Expires = Time.time + 120f };
                    Hooks[playerId] = hook; Reply(hook, 0, false); return;
                }
                if (!Hooks.TryGetValue(playerId, out var current) || current.Token != token) return;
                if (operation == 3) { Finish(current, false); return; }
                if (operation != 1 || current.Active) return;
                Character victim = Find<Character>(current.Target); ItemDrop spear = Find<ItemDrop>(current.Drop);
                if (!ValidAttachment(current, player, victim, spear)) { Finish(current, false); return; }
                ZDO equipped = player.m_nview.GetZDO();
                if (!EmptyHands(player) || equipped.GetInt(ZDOVars.s_rightItem, 0) != 0 ||
                    equipped.GetInt(ZDOVars.s_leftItem, 0) != 0 ||
                    (Cooldowns.TryGetValue(playerId, out float until) && Time.time < until) ||
                    Vector3.Distance(spear.transform.position, victim.GetCollider().ClosestPoint(spear.transform.position)) > 2f ||
                    Vector3.Distance(player.transform.position, victim.transform.position) > 256f ||
                    SpearHookMotion.BlockedLine(player, victim, spear.gameObject))
                { Reply(current, 6, false); return; }
                current.Grapple = Heavy(victim);
                Character mover = current.Grapple ? player : victim;
                long moverUid = mover.m_nview.GetZDO().GetOwner();
                if (moverUid == 0 || (moverUid != ZNet.GetUID() && (net.GetPeer(moverUid)?.IsReady() != true ||
                    !MotionPeers.Contains(net.GetPeer(moverUid).m_rpc))))
                { Reply(current, 6, false); return; }
                foreach (var other in Hooks.Values)
                    if (other.Active && (other.Grapple ? other.Player : other.Target) == Id(mover))
                    { Reply(current, 6, false); return; }
                current.Active = true; current.MoverUid = moverUid;
                current.MotionToken = Guid.NewGuid().ToString("N");
                float speed = PullSpeed(OwnerSkillAuthority.Level(player, Skills.SkillType.Spears));
                current.Expires = Time.time + Vector3.Distance(player.transform.position, victim.transform.position) / speed + 6f;
                Cooldowns[playerId] = Time.time + 12f;
                Reply(current, 3, current.Grapple); // Owner input/visual lock precedes motion.
                var command = new ZPackage(); command.Write(1); command.Write(current.MotionToken);
                command.Write(current.Player); command.Write(current.Target); command.Write(current.Drop); command.Write(current.Grapple);
                command.Write(speed);
                Deliver(moverUid, command);
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Spear70] Rejected network action: " + error.Message); }
        }
        private static void Reply(Hook hook, int code, bool value)
        { var reply = new ZPackage(); reply.Write(code); reply.Write(hook.Token); reply.Write(value); Deliver(hook.PeerUid, reply); }
        private static void Retry(Hook hook)
        {
            if (hook.Active)
            {
                var stop = new ZPackage(); stop.Write(4); stop.Write(hook.MotionToken); stop.Write(false); Deliver(hook.MoverUid, stop);
                Cooldowns.Remove(hook.PlayerId);
            }
            hook.Active = false; hook.MotionToken = null;
            Reply(hook, 6, false);
        }
        private static void Finish(Hook hook, bool arrived)
        {
            Hooks.Remove(hook.PlayerId);
            if (hook.Active)
            {
                var stop = new ZPackage(); stop.Write(4); stop.Write(hook.MotionToken); stop.Write(false); Deliver(hook.MoverUid, stop);
            }
            // Failure must not masquerade as a successful hook followed by recall.
            Reply(hook, arrived ? 2 : 5, arrived && hook.Grapple);
        }
        private static void Command(ZRpc rpc, ZPackage package)
        {
            ZNet net = ZNet.instance;
            if (!Enabled || net == null || package == null || package.Size() > 512 ||
                (net.IsServer() ? rpc != null : rpc != net.GetServerRPC())) return;
            try
            {
                int code = package.ReadInt(); string token = package.ReadString();
                if (!Guid.TryParseExact(token, "N", out _)) return;
                if (code == 1)
                {
                    Player player = Find<Player>(package.ReadZDOID()); Character target = Find<Character>(package.ReadZDOID());
                    ItemDrop spear = Find<ItemDrop>(package.ReadZDOID()); bool grapple = package.ReadBool(); float speed = package.ReadSingle();
                    if (!Alive(player) || !Alive(target) || spear == null || (!grapple && Heavy(target))) { Complete(token, false); return; }
                    Character mover = grapple ? player : target;
                    if (mover.m_nview.IsOwner() != true) { Complete(token, false); return; }
                    var motion = mover.GetComponent<SpearHookMotion>() ?? mover.gameObject.AddComponent<SpearHookMotion>();
                    if (!motion.Begin(token, player, target, spear, grapple, speed)) Complete(token, false);
                    return;
                }
                if (code == 4) { SpearHookMotion.StopToken(token); return; }
                bool value = package.ReadBool();
                Player.m_localPlayer?.GetComponent<Spear70Attachment>()?.Reply(token, code, value);
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Spear70] Invalid server command: " + error.Message); }
        }
        internal static void Complete(string token, bool arrived)
        { var package = new ZPackage(); package.Write(2); package.Write(token); package.Write(arrived); Send(package); }
    }

    internal sealed class Spear70Attachment : MonoBehaviour
    {
        private SpearThrowRecord _record;
        private string _token;
        private Rigidbody _body;
        private bool _wasKinematic, _ready, _pending, _active, _queued;
        private Vector3 _offset;
        private float _created, _nextRequest, _deadline;
        internal string Status => _record == null ? "idle" : _active ? "pulling" : _pending ? "awaiting-server" : _ready ? "attached" : "registering";
        internal bool Busy => _queued || _pending || _active;
        internal bool Attached => _record != null && !_record.Returning &&
            _record.DroppedItem != null && _record.HitCharacter != null && !_record.HitCharacter.IsDead();
        internal bool Tracks(SpearThrowRecord record) => ReferenceEquals(_record, record);
        internal void CancelForRecall()
        {
            if (_active) return;
            if (_record != null && Spear70HookService.Ready)
            { var package = new ZPackage(); package.Write(3); package.Write(_token); Spear70HookService.Send(package); }
            Release();
        }
        internal void Track(SpearThrowRecord record)
        {
            Release(); _record = record; _token = Guid.NewGuid().ToString("N");
            _created = Time.time; _nextRequest = 0f;
            _offset = record.HitCharacter.transform.InverseTransformPoint(record.HitPoint);
            _body = record.DroppedItem.GetComponent<Rigidbody>();
            if (_body != null) { _wasKinematic = _body.isKinematic; _body.linearVelocity = Vector3.zero; _body.isKinematic = true; }
            enabled = true;
        }
        private void Update()
        {
            if (_record == null) return;
            if (_record.Owner == null || _record.Owner.IsDead() || _record.Owner.IsTeleporting() ||
                _record.DroppedItem == null || _record.HitCharacter == null || _record.HitCharacter.IsDead() ||
                _record.DroppedItem.GetComponent<ZNetView>()?.IsOwner() != true)
            { Release(); return; }
            if (_pending && Time.time > _deadline)
            {
                // A lost reply is not evidence that the attachment was broken.
                _pending = _active = _queued = _ready = false;
                _record.HookTaut = false; _created = Time.time; _nextRequest = Time.time + .5f;
            }
            if (_record.Returning) { Release(); return; }
            Vector3 position = _record.HitCharacter.transform.TransformPoint(_offset);
            if (_body != null) _body.position = position; else _record.DroppedItem.transform.position = position;
            if (!_ready && Time.time >= _nextRequest)
            {
                if (Time.time - _created > 15f)
                {
                    if (_queued) Spear70HookService.Trace("registration expired");
                    _created = Time.time; _nextRequest = Time.time + 3f; return;
                }
                _nextRequest = Time.time + 0.5f;
                var package = new ZPackage(); package.Write(0); package.Write(_token);
                package.Write(Spear70HookService.Id(_record.DroppedItem)); package.Write(Spear70HookService.Id(_record.HitCharacter));
                Spear70HookService.Send(package);
            }
            if (_ready && _queued && !_record.Owner.InAttack()) TryUse();
        }
        internal bool TryUse()
        {
            if (_record == null) return false;
            if (!Spear70HookService.EmptyHands(_record.Owner)) { _queued = false; return false; }
            if (_pending || _active) return true;
            if (!_ready || _record.Owner.InAttack()) { _queued = true; return true; }
            if (PerkCooldownStateService.GetRemainingSeconds(_record.Owner, "spears_70") > 0d) { _queued = false; return true; }
            _queued = false;
            _pending = true; _deadline = Time.time + 8f;
            var package = new ZPackage(); package.Write(1); package.Write(_token); Spear70HookService.Send(package);
            return true;
        }
        internal void Reply(string token, int code, bool value)
        {
            if (_record == null || _token != token) return;
            if (code == 0) { _ready = true; if (_queued) TryUse(); }
            if (code == 2) Return(value);
            if (code == 6)
            {
                if (_active) PerkCooldownStateService.ReduceRemaining(_record.Owner, "spears_70", 0f);
                _pending = _active = _queued = false; _ready = true; _record.HookTaut = false;
                Spear70HookService.Trace("motion deferred; attachment retained for next press");
                return;
            }
            if (code == 5)
            {
                Spear70HookService.Trace("hook refused or obstructed");
                Release(); return;
            }
            if (code == 3)
            {
                _pending = true; _active = true; _record.HookTaut = true;
                PerkCooldownStateService.TryConsume(_record.Owner, "spears_70", 12d);
                _deadline = Time.time + Vector3.Distance(_record.Owner.transform.position, _record.HitCharacter.transform.position) / 10f + 7f;
                PerkFeedbackService.Play(_record.Owner, "spears_70_hook_start", _record.Owner.GetCenterPoint(), false);
            }
        }
        private void Return(bool thrust)
        {
            if (_record == null) return;
            Player owner = _record.Owner; Character target = _record.HitCharacter;
            if (thrust && owner != null && target != null)
                _record.OnCaught = (player, item) =>
                {
                    if (target == null || target.IsDead() || player.IsDead() || player.InAttack() ||
                        !ReferenceEquals(player.GetRightItem(), item) || item.m_shared.m_skillType != Skills.SkillType.Spears ||
                        Vector3.Distance(player.GetCenterPoint(), target.GetCollider().ClosestPoint(player.GetCenterPoint())) > 2.5f ||
                        SpearHookMotion.BlockedLine(player, target, null)) return;
                    Vector3 facing = target.GetCenterPoint() - player.GetCenterPoint(); facing.y = 0f;
                    if (facing.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(facing);
                    if (player.StartAttack(target, false) && player.m_currentAttack != null && !target.IsBoss())
                        player.m_currentAttack.m_staggerMultiplier *= 2f;
                };
            var record = _record;
            Release();
            bool started = false;
            if (owner != null && owner == Player.m_localPlayer) SpearThrowLifecycleService.TryRecall(owner, out started);
            if (!started) record.OnCaught = null;
        }
        private void Release()
        {
            if (_body != null) _body.isKinematic = _wasKinematic;
            if (_record != null) _record.HookTaut = false;
            _body = null; _record = null; _token = null; _ready = _pending = _active = _queued = false; enabled = false;
        }
        private void OnDestroy() => Release();
    }

    internal sealed class SpearHookMotion : MonoBehaviour
    {
        private static readonly Dictionary<string, SpearHookMotion> Active = new Dictionary<string, SpearHookMotion>();
        private static readonly Dictionary<Character, SpearHookMotion> ByMover = new Dictionary<Character, SpearHookMotion>();
        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private string _token;
        private Player _player;
        private Character _target, _mover;
        private ItemDrop _spear;
        private bool _grapple;
        private float _deadline, _speed;
        internal bool Begin(string token, Player player, Character target, ItemDrop spear, bool grapple, float speed)
        {
            if (_token != null || Active.ContainsKey(token) || ByMover.ContainsKey(grapple ? player : target)) return false;
            _token = token; _player = player; _target = target; _spear = spear; _grapple = grapple;
            _mover = grapple ? player : target;
            _speed = Mathf.Clamp(speed, 10f, 20f);
            _deadline = Time.time + Vector3.Distance(player.transform.position, target.transform.position) / _speed + 5f;
            Active.Add(token, this);
            ByMover.Add(_mover, this);
            if (!grapple && !target.IsBoss()) target.Stagger(player.transform.position - target.transform.position);
            return true;
        }
        internal static void StopToken(string token) { if (Active.TryGetValue(token, out var motion)) motion.Stop(false, false); }
        internal static void StepFor(Character character, float dt)
        { if (ByMover.Count != 0 && ByMover.TryGetValue(character, out var motion)) motion.Step(dt); }
        internal void Step(float dt)
        {
            if (_token == null) return;
            if (_player == null || _target == null || _spear == null || _player.IsDead() || _target.IsDead() ||
                _player.IsTeleporting() || _mover.m_nview?.IsOwner() != true || Time.time > _deadline ||
                !Spear70HookService.EmptyHands(_player) || !BaseAI.IsEnemy(_player, _target) ||
                (!_grapple && Spear70HookService.Heavy(_target)))
            {
                Spear70HookService.Trace("motion cancelled: state/ownership/deadline; grapple=" + _grapple +
                    ", owner=" + (_mover?.m_nview?.IsOwner() == true) + ", expired=" + (Time.time > _deadline));
                Stop(false); return;
            }
            Character destination = _grapple ? _target : _player;
            Vector3 center = _mover.GetCenterPoint();
            Vector3 closest = destination.GetCollider().ClosestPoint(center);
            Vector3 delta = closest - center;
            float gap = delta.magnitude - _mover.GetRadius();
            if (gap <= 0.9f)
            {
                if (!_grapple && !_target.IsBoss()) _target.Stagger(_player.transform.position - _target.transform.position);
                Stop(true); return;
            }
            // Motion is target-relative, never camera/facing-relative. Discard the
            // downward component against ground so a slope doesn't cancel every cast.
            if (_mover.IsOnGround() && delta.y < 0f) delta.y = 0f;
            if (delta.sqrMagnitude < .001f) { Stop(true); return; }
            Vector3 step = delta.normalized * Mathf.Min(_speed * Mathf.Clamp(dt, 0f, 0.05f), gap - 0.8f);
            Bounds bounds = _mover.GetCollider().bounds;
            float radius = Mathf.Max(0.1f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f);
            Vector3 bottom = bounds.center - Vector3.up * Mathf.Max(0f, bounds.extents.y - radius);
            Vector3 top = bounds.center + Vector3.up * Mathf.Max(0f, bounds.extents.y - radius);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, step.normalized, _hits, step.magnitude + 0.08f, ~0, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length) { Spear70HookService.Trace("motion cancelled: collision buffer full"); Stop(false); return; }
            for (int i = 0; i < count; i++)
                if (!Ignore(_hits[i].collider, _player, _target, _spear.gameObject) && _hits[i].normal.y < .6f)
                {
                    Spear70HookService.Trace("motion blocked: collider=" + _hits[i].collider.name +
                        ", layer=" + _hits[i].collider.gameObject.layer + ", normal=" + _hits[i].normal + ", gap=" + gap.ToString("0.00"));
                    Stop(false); return;
                }
            _mover.m_body.linearVelocity = Vector3.zero;
            _mover.m_body.MovePosition(_mover.m_body.position + step);
        }
        internal static bool BlockedLine(Player player, Character target, GameObject spear)
        {
            Vector3 from = player.GetCenterPoint(), delta = target.GetCenterPoint() - from;
            if (delta.sqrMagnitude < 0.01f) return false;
            foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignore(hit.collider, player, target, spear)) return true;
            return false;
        }
        private static bool Ignore(Collider c, Player player, Character target, GameObject spear) => c == null ||
            c.GetComponentInParent<Character>() == player || c.GetComponentInParent<Character>() == target ||
            (spear != null && c.transform.IsChildOf(spear.transform));
        private void Stop(bool arrived, bool notify = true)
        {
            string token = _token; if (token == null) return;
            Active.Remove(token); _token = null;
            if (!ReferenceEquals(_mover, null)) ByMover.Remove(_mover);
            if (_mover != null && _mover.m_nview?.IsOwner() == true) _mover.m_body.linearVelocity = Vector3.zero;
            if (notify) Spear70HookService.Complete(token, arrived);
        }
        private void OnDestroy() => Stop(false);
    }
    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class SpearHookOwnerMotionPatch
    {
        private static void Postfix(Character __instance, float dt)
        {
            if (Spear70HookService.Enabled) SpearHookMotion.StepFor(__instance, dt);
        }
    }
}
#endif
