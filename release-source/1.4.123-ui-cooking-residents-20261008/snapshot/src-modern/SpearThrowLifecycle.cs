#if MASTERY_SPEAR35_EXPERIMENT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class SpearThrowRecord
    {
        internal long Sequence;
        internal Player Owner;
        internal ItemDrop.ItemData OriginalItem;
        internal Projectile Projectile;
        internal ItemDrop DroppedItem;
        internal Character HitCharacter;
        internal Vector3 HitPoint;
        internal float ThrowTime;
        internal bool Returning;
        internal bool HookTaut;
        internal Action<Player, ItemDrop.ItemData> OnCaught;
    }

    // Experimental exact-item recall; excluded from installed releases pending live tests.
    internal static class SpearThrowLifecycleService
    {
        private static long _nextSequence;
        private static readonly ConditionalWeakTable<Projectile, SpearThrowRecord> ByProjectile =
            new ConditionalWeakTable<Projectile, SpearThrowRecord>();
        private static readonly ConditionalWeakTable<Player, List<SpearThrowRecord>> ByPlayer =
            new ConditionalWeakTable<Player, List<SpearThrowRecord>>();
        [ThreadStatic] private static Projectile _spawningProjectile;
        internal static bool IsReturning(Player player) => Latest(player)?.Returning == true;

        internal static bool CanRecallNow(Player player)
        {
            SpearThrowRecord record = Latest(player);
            if (record == null || record.Returning || player != Player.m_localPlayer ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Spears, 35) ||
                player.IsDead() || player.IsTeleporting() || player.InAttack() || player.GetRightItem() != null) return false;
            return record.DroppedItem != null || record.Projectile != null;
        }

        internal static void ObserveThrow(Projectile projectile, Character owner, ItemDrop.ItemData item)
        {
            Player player = owner as Player;
            if (projectile == null || player == null || item?.m_shared?.m_skillType != Skills.SkillType.Spears ||
                projectile.m_spawnItem == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Spears, 35) ||
                ByProjectile.TryGetValue(projectile, out _)) return;
            SpearThrowRecord record = new SpearThrowRecord
            {
                Sequence = Interlocked.Increment(ref _nextSequence), Owner = player,
                OriginalItem = item, Projectile = projectile, ThrowTime = Time.time
            };
            ByProjectile.Add(projectile, record);
            List<SpearThrowRecord> records = ByPlayer.GetOrCreateValue(player);
            records.RemoveAll(entry => entry.DroppedItem == null && entry.Projectile == null);
            records.Add(record);
            if (player == Player.m_localPlayer)
            {
                SpearTetherVisualController visual = player.GetComponent<SpearTetherVisualController>();
                if (visual == null) visual = player.gameObject.AddComponent<SpearTetherVisualController>();
                visual.Track(record);
            }
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Spear35] throw tracked id=" + record.Sequence +
                    " item=" + PerkRuntimeService.ItemPrefabName(item));
        }

        internal static Projectile BeginItemSpawn(Projectile projectile)
        {
            Projectile previous = _spawningProjectile;
            _spawningProjectile = projectile != null && ByProjectile.TryGetValue(projectile, out _) ? projectile : null;
            return previous;
        }

        internal static void EndItemSpawn(Projectile previous) => _spawningProjectile = previous;

        internal static void ObserveDroppedItem(ItemDrop.ItemData item, int amount, ItemDrop drop)
        {
            Projectile projectile = _spawningProjectile;
            if (projectile == null || drop == null || amount != 1 ||
                !ByProjectile.TryGetValue(projectile, out SpearThrowRecord record) ||
                !ReferenceEquals(item, projectile.m_spawnItem)) return;
            record.DroppedItem = drop;
            Spear70HookService.Track(record);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Spear35] same spear landed id=" + record.Sequence +
                    " quality=" + drop.m_itemData.m_quality +
                    " durability=" + drop.m_itemData.m_durability.ToString("0.#"));
        }

        internal static void ObserveImpact(Projectile projectile, Collider collider, Vector3 hitPoint)
        {
            if (projectile == null || !ByProjectile.TryGetValue(projectile, out SpearThrowRecord record)) return;
            Character target = collider?.GetComponentInParent<Character>();
            if (target == null || target == record.Owner) return;
            record.HitCharacter = target;
            record.HitPoint = hitPoint;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Spear35] impact id=" + record.Sequence +
                    " target=" + target.GetHoverName() + " point=" + hitPoint);
        }

        internal static SpearThrowRecord Latest(Player player)
        {
            if (player == null || !ByPlayer.TryGetValue(player, out List<SpearThrowRecord> records)) return null;
            records.RemoveAll(entry => entry.DroppedItem == null && entry.Projectile == null);
            for (int i = records.Count - 1; i >= 0; --i)
            {
                SpearThrowRecord entry = records[i];
                if (entry.DroppedItem != null || entry.Projectile != null) return entry;
            }
            return null;
        }

        internal static string DebugSummary(Player player)
        {
            SpearThrowRecord record = Latest(player);
            if (record == null) return "No locally tracked thrown spear. Tracking executes on the projectile owner.";
            ItemDrop.ItemData data = record.DroppedItem != null ? record.DroppedItem.m_itemData : record.OriginalItem;
            ZNetView projectileView = record.Projectile != null ? record.Projectile.GetComponent<ZNetView>() : null;
            ZNetView dropView = record.DroppedItem != null ? record.DroppedItem.GetComponent<ZNetView>() : null;
            return "Spear throw #" + record.Sequence +
                " age=" + (Time.time - record.ThrowTime).ToString("0.0") + "s" +
                " flying=" + (record.Projectile != null) +
                " landed=" + (record.DroppedItem != null) +
                " projectileOwner=" + (projectileView != null && projectileView.IsValid() && projectileView.IsOwner()) +
                " dropOwner=" + (dropView != null && dropView.IsValid() && dropView.IsOwner()) +
                " target=" + (record.HitCharacter != null ? record.HitCharacter.GetHoverName() : "none") +
                " hook70=" + Spear70HookDiagnostics.Describe(record) +
                " network70=" + Spear70HookService.Describe(player) +
                " item=" + PerkRuntimeService.ItemPrefabName(data) +
                " quality=" + (data?.m_quality ?? 0) +
                " durability=" + (data?.m_durability ?? 0f).ToString("0.#");
        }

        internal static string TryRecall(Player player) => TryRecall(player, out _);

        internal static string TryRecall(Player player, out bool started)
        {
            started = false;
            SpearThrowRecord record = Latest(player);
            if (record == null) return "No tracked thrown spear on this client.";
            if (record.Returning) return "That spear is already returning.";
            if (!PerkRuntimeService.HasPerk(player, Skills.SkillType.Spears, 35)) return "Spear35 is locked.";
            if (player.IsDead() || player.IsTeleporting() || player.InAttack() || player.GetRightItem() != null)
                return "The receiving hand must be free and the player must be ready.";
            Vector3 position = record.DroppedItem != null ? record.DroppedItem.transform.position :
                record.Projectile != null ? record.Projectile.transform.position : player.GetCenterPoint();
            ItemDrop.ItemData item = record.DroppedItem != null ? record.DroppedItem.m_itemData : record.OriginalItem;
            if (item == null || !player.GetInventory().CanAddItem(item, 1))
                return "Inventory is full; spear remains in the world.";

            if (record.DroppedItem == null)
            {
                Projectile projectile = record.Projectile;
                if (projectile == null || !projectile.m_respawnItemOnHit ||
                    !ReferenceEquals(projectile.m_spawnItem, record.OriginalItem) || ZNetScene.instance == null)
                    return "This projectile cannot safely return its original spear.";
                ZNetView projectileView = projectile.GetComponent<ZNetView>();
                if (projectileView == null || !projectileView.IsValid() || !projectileView.IsOwner())
                    return "This client does not own the flying spear.";
                // Use the game's own spear-drop path. The SpawnOnHit context records
                // the exact ItemDrop produced from this projectile's original ItemData.
                // Never destroy the projectile until that drop has been observed.
                try { projectile.SpawnOnHit(null, null, Vector3.up); }
                catch (Exception error)
                {
                    MasteryPlugin.Log.LogWarning("[Spear35] in-flight conversion failed: " + error.Message);
                    if (record.DroppedItem == null)
                        return "Item conversion failed; the projectile is still flying.";
                }
                if (record.DroppedItem == null)
                    return "Vanilla did not produce a dropped spear; original projectile remains.";
                // Disable any later vanilla spawn before removing this projectile.
                projectile.m_spawnItem = null;
                ZNetScene.instance.Destroy(projectile.gameObject);
                record.Projectile = null;
            }

            ZNetView view = record.DroppedItem.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner())
                return "This client does not own the dropped spear; the spear remains in the world.";

            SpearLandedRecallController controller = player.GetComponent<SpearLandedRecallController>();
            if (controller == null) controller = player.gameObject.AddComponent<SpearLandedRecallController>();
            if (!controller.Begin(record)) return "Recall controller is busy or the path is blocked.";
            record.Returning = true;
            started = true;
            return "Spear recall started; the original item is moving back.";
        }

        internal static void RecallOnBlockPress(Player player)
        {
            if (!CanRecallNow(player)) return;
            if (Spear70HookService.IsPulling(player)) return;
            Spear70HookService.ReleaseForRecall(player);
            string result = TryRecall(player, out bool started);
            if (!started && MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Spear35] recall refused: " + result);
        }
    }

    // Diagnostic only. Vanilla throws drop the spear instead of embedding it;
    // this must not move a target until a server-owned attachment protocol exists.
    internal static class Spear70HookDiagnostics
    {
        internal static string Describe(SpearThrowRecord record)
        {
            if (record?.Owner == null || !PerkRuntimeService.HasPerk(record.Owner, Skills.SkillType.Spears, 70))
                return "locked";
            Character target = record.HitCharacter;
            if (target == null) return "no-target";
            if (target.IsDead()) return "target-dead";
            if (!BaseAI.IsEnemy(record.Owner, target)) return "not-hostile";
            if (record.DroppedItem == null) return "no-landed-item";
            float radius = Mathf.Max(0.5f, target.GetRadius());
            float separation = Vector3.Distance(record.DroppedItem.transform.position, target.GetCenterPoint());
            if (separation > radius * 2f + 1.5f) return "item-separated:" + separation.ToString("0.0") + "m";
            CreatureClass kind = MasteryClassificationService.GetCreatureClass(target);
            bool targetOwnedHere = target.m_nview != null && target.m_nview.IsValid() && target.m_nview.IsOwner();
            return kind + ":target-owned-here=" + targetOwnedHere +
                ":radius=" + radius.ToString("0.0") +
                ":hp=" + target.GetMaxHealth().ToString("0");
        }
    }

    // One local cord while the actual spear flies or rests in the world. The
    // returning controller takes over once recall begins; no per-hit particles.
    internal sealed class SpearTetherVisualController : MonoBehaviour
    {
        private SpearThrowRecord _record;
        private GameObject _cordObject;
        private LineRenderer _cord;
        private Material _material;
        private float _nextUpdate;

        internal void Track(SpearThrowRecord record)
        {
            Clear();
            if (record?.Owner == null) return;
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            _record = record;
            _cordObject = new GameObject("VM_SpearTetherDuringThrow");
            _material = new Material(shader) { color = new Color(0.29f, 0.20f, 0.11f, 0.88f) };
            _cord = _cordObject.AddComponent<LineRenderer>();
            _cord.material = _material;
            _cord.useWorldSpace = true;
            _cord.positionCount = 7;
            _cord.widthMultiplier = 0.028f;
            _cord.numCapVertices = 2;
            _cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cord.receiveShadows = false;
            _cord.enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            _nextUpdate = 0f;
            enabled = true;
        }

        private void LateUpdate()
        {
            if (_record == null || _record.Owner == null || _record.Returning ||
                (_record.DroppedItem == null && _record.Projectile == null))
            { Clear(); return; }
            _cord.enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (!_cord.enabled) return;
            if (Time.time < _nextUpdate) return;
            _nextUpdate = Time.time + 0.05f;
            Player player = _record.Owner;
            Vector3 start = player.GetCenterPoint() + player.transform.right * 0.25f - Vector3.up * 0.20f;
            Vector3 end = _record.DroppedItem != null
                ? _record.DroppedItem.transform.position + Vector3.up * 0.08f
                : _record.Projectile.transform.position;
            float sag = _record.HookTaut ? 0.025f : Mathf.Min(0.8f, Vector3.Distance(start, end) * 0.04f);
            for (int i = 0; i < 7; ++i)
            {
                float t = i / 6f;
                _cord.SetPosition(i, Vector3.Lerp(start, end, t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * sag));
            }
        }

        private void Clear()
        {
            if (_cordObject != null) Destroy(_cordObject);
            if (_material != null) Destroy(_material);
            _record = null;
            _cordObject = null;
            _cord = null;
            _material = null;
            enabled = false;
        }

        private void OnDestroy() => Clear();
    }

    internal sealed class SpearLandedRecallController : MonoBehaviour
    {
        private SpearThrowRecord _record;
        private Rigidbody _body;
        private bool _wasKinematic;
        private float _startedAt;
        private float _returnSpeed;
        private float _maxDuration;
        private GameObject _cordObject;
        private LineRenderer _cord;
        private Material _cordMaterial;

        internal bool Begin(SpearThrowRecord record)
        {
            if (_record != null || record?.Owner == null || record.DroppedItem == null) return false;
            Vector3 from = record.DroppedItem.transform.position;
            Vector3 to = CatchPoint(record.Owner);
            _record = record;
            _startedAt = Time.time;
            _returnSpeed = Mathf.Max(18f, Vector3.Distance(from, to) / 3f);
            _maxDuration = Vector3.Distance(from, to) / _returnSpeed + 2f;
            _body = record.DroppedItem.GetComponent<Rigidbody>();
            if (_body != null) { _wasKinematic = _body.isKinematic; _body.linearVelocity = Vector3.zero; _body.isKinematic = true; }
            CreateCord();
            enabled = true;
            return true;
        }

        private void FixedUpdate()
        {
            if (_record == null) { enabled = false; return; }
            Player owner = _record.Owner;
            ItemDrop drop = _record.DroppedItem;
            if (owner == null || owner.IsDead() || drop == null || Time.time - _startedAt > _maxDuration ||
                owner.GetRightItem() != null || drop.GetComponent<ZNetView>()?.IsOwner() != true)
            { Stop(false); return; }

            Vector3 from = drop.transform.position;
            Vector3 to = CatchPoint(owner);
            Vector3 next = Vector3.MoveTowards(from, to, _returnSpeed * Time.fixedDeltaTime);
            if (PathBlocked(from, next, owner, drop.gameObject, _record.HitCharacter)) { Stop(false); return; }
            if (_body != null) _body.MovePosition(next);
            else drop.transform.position = next;
            if ((next - to).sqrMagnitude > 0.35f * 0.35f) return;

            // Vanilla Pickup is the only inventory transaction. A failed pickup
            // leaves the same world item at the player's feet; no second item is made.
            var exactItem = drop.m_itemData;
            var onCaught = _record.OnCaught;
            _record.OnCaught = null;
            bool pickedUp = owner.Pickup(drop.gameObject, true, false);
            if (!pickedUp && drop != null) drop.transform.position = owner.transform.position + owner.transform.forward * 0.7f;
            Stop(pickedUp);
            if (pickedUp) onCaught?.Invoke(owner, exactItem);
        }

        private void LateUpdate()
        {
            if (_cord == null || _record?.Owner == null || _record.DroppedItem == null) return;
            // Recall movement and pickup remain in FixedUpdate, independent of this cosmetic switch.
            _cord.enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (!_cord.enabled) return;
            Vector3 start = _record.Owner.GetCenterPoint() + _record.Owner.transform.right * 0.25f - Vector3.up * 0.20f;
            Vector3 end = _record.DroppedItem.transform.position + Vector3.up * 0.08f;
            float sag = Mathf.Min(0.8f, Vector3.Distance(start, end) * 0.04f);
            for (int i = 0; i < 7; ++i)
            {
                float t = i / 6f;
                _cord.SetPosition(i, Vector3.Lerp(start, end, t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * sag));
            }
        }

        private void CreateCord()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            _cordObject = new GameObject("VM_SpearTetherCord");
            _cordMaterial = new Material(shader) { color = new Color(0.29f, 0.20f, 0.11f, 0.88f) };
            _cord = _cordObject.AddComponent<LineRenderer>();
            _cord.material = _cordMaterial;
            _cord.useWorldSpace = true;
            _cord.positionCount = 7;
            _cord.widthMultiplier = 0.028f;
            _cord.numCapVertices = 2;
            _cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cord.receiveShadows = false;
            _cord.enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
        }

        private void Stop(bool pickedUp)
        {
            if (_body != null && !pickedUp) _body.isKinematic = _wasKinematic;
            if (_record != null) { _record.Returning = false; if (!pickedUp) _record.OnCaught = null; }
            if (_cordObject != null) Destroy(_cordObject);
            if (_cordMaterial != null) Destroy(_cordMaterial);
            _record = null;
            _body = null;
            _cordObject = null;
            _cord = null;
            _cordMaterial = null;
            enabled = false;
        }

        private void OnDestroy() { if (_record != null) Stop(false); }

        private static Vector3 CatchPoint(Player player) => player.GetCenterPoint() + player.transform.forward * 0.35f;

        internal static bool PathBlocked(Vector3 from, Vector3 to, Player player, GameObject spearObject,
            Character piercedTarget = null)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(from, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                Collider collider = hit.collider;
                if (collider == null || collider.GetComponentInParent<Player>() == player ||
                    collider.transform.IsChildOf(spearObject.transform) ||
                    (piercedTarget != null && collider.GetComponentInParent<Character>() == piercedTarget)) continue;
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class SpearThrowSetupTrackingPatch
    {
        private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item) =>
            SpearThrowLifecycleService.ObserveThrow(__instance, owner, item);
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.SpawnOnHit))]
    internal static class SpearThrowDropContextPatch
    {
        private static void Prefix(Projectile __instance, out Projectile __state) =>
            __state = SpearThrowLifecycleService.BeginItemSpawn(__instance);

        private static Exception Finalizer(Projectile __state, Exception __exception)
        {
            SpearThrowLifecycleService.EndItemSpawn(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class SpearThrowImpactTrackingPatch
    {
        private static void Prefix(Projectile __instance, Collider collider, Vector3 hitPoint) =>
            SpearThrowLifecycleService.ObserveImpact(__instance, collider, hitPoint);
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem),
        new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(Vector3), typeof(Quaternion) })]
    internal static class SpearThrowDroppedItemTrackingPatch
    {
        private static void Postfix(ItemDrop.ItemData item, int amount, ItemDrop __result) =>
            SpearThrowLifecycleService.ObserveDroppedItem(item, amount, __result);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class Spear35RecallBlockInputPatch
    {
        private sealed class InputState { internal bool Consumed; }
        private static readonly ConditionalWeakTable<Player, InputState> Inputs = new ConditionalWeakTable<Player, InputState>();
        private static void Prefix(Player __instance, bool block, ref bool secondaryAttack, ref bool secondaryAttackHold)
        {
            if (__instance != Player.m_localPlayer) return;
            var input = Inputs.GetOrCreateValue(__instance);
            // A delayed/rejected hook can remain queued while a different weapon
            // is equipped. Its gesture must never swallow that weapon's secondary.
            if (__instance.GetRightItem() != null || __instance.GetLeftItem() != null)
            {
                input.Consumed = false;
                if (block) SpearThrowLifecycleService.RecallOnBlockPress(__instance);
                return;
            }
            if (!secondaryAttack && !secondaryAttackHold) input.Consumed = false;
            // These are vanilla down-edge inputs, not held-button polling. Block
            // always recalls; secondary exclusively requests the attached hook.
            if (block) SpearThrowLifecycleService.RecallOnBlockPress(__instance);
            else if (secondaryAttack && Spear70HookService.TryBegin(__instance))
                input.Consumed = true;
            // An admitted hook owns this button gesture even if motion subsequently
            // fails. Never turn its remaining held frames into a vanilla kick.
            if (input.Consumed || Spear70HookService.IsPulling(__instance) || Spear70HookService.OwnsSecondary(__instance))
                secondaryAttack = secondaryAttackHold = false;
        }
    }
}
#endif
