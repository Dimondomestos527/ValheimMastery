using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Cooking70FeastRetrofitRuntime
    {
        internal const long TargetWorld = -2407519883L;
        internal const string EpochKey = "vm_cooking70_feast_retrofit_20261013_v1";
        private sealed class Cursor { internal float Next; internal int Index; }
        private static ConditionalWeakTable<Inventory,Cursor> Cursors = new ConditionalWeakTable<Inventory,Cursor>();
        private static readonly Queue<Feast> Pending = new Queue<Feast>();
        private static readonly HashSet<Feast> Queued = new HashSet<Feast>();
        private static ZNet Session;
        private static ZDOMan Manager;
        private static ulong Generation;
        private static float NextTick;
        private static bool Mutating;

        private static bool Context()
        {
            var net = ZNet.instance;
            if (net == null || net.GetWorld() == null || net.GetWorldUID() != TargetWorld || ZDOMan.instance == null)
            {
                if (Session != null) { Session = null; Manager = null; Pending.Clear(); Queued.Clear(); Cursors = new ConditionalWeakTable<Inventory,Cursor>(); NextTick = 0f; }
                return false;
            }
            if (!ReferenceEquals(Session, net) || !ReferenceEquals(Manager, ZDOMan.instance) || Generation != MasterIdolLoadIdentity.Generation)
            {
                Session = net; Manager = ZDOMan.instance; Generation = MasterIdolLoadIdentity.Generation;
                Pending.Clear(); Queued.Clear(); Cursors = new ConditionalWeakTable<Inventory,Cursor>(); NextTick = 0f;
            }
            return !ZNet.m_loadError;
        }
        private static bool Armed() => Context() && MasterIdolLoadIdentity.LoadReady &&
            ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(EpochKey);
        internal static void Tick()
        {
            if (!Context() || !MasterIdolLoadIdentity.LoadReady || ZoneSystem.instance == null || Time.time < NextTick) return;
            NextTick = Time.time + 1f;
            if (Session.IsServer() && !ZoneSystem.instance.GetGlobalKey(EpochKey))
                ZoneSystem.instance.SetGlobalKey(EpochKey);
            if (!Armed()) return;
            int count = Math.Min(8, Pending.Count);
            while (count-- > 0)
            {
                var feast = Pending.Dequeue();
                if (feast == null) { Queued.Remove(feast); continue; }
                if (TryPlaced(feast)) Queued.Remove(feast);
                else Pending.Enqueue(feast);
            }
            // ARMED does not certify that dormant/offline objects have loaded.
        }
        internal static void Observe(Feast feast)
        {
            if (feast == null || !Context() || !Cooking70FeastRetrofit.ExactPrefab(feast.gameObject.name.Replace("(Clone)", ""))) return;
            if (!TryPlaced(feast) && Queued.Add(feast)) Pending.Enqueue(feast);
        }
        private static bool NativeOwner(ZNetView view, ZDO record) => view != null && view.IsValid() && view.IsOwner() &&
            record != null && ReferenceEquals(view.GetZDO(), record) && ReferenceEquals(Manager.GetZDO(record.m_uid), record);
        internal static bool TryPlaced(Feast feast)
        {
            if (!Armed() || Mutating || feast == null || CookingFeastProvenance.Current != null) return false;
            var view = feast.m_nview != null ? feast.m_nview : feast.GetComponent<ZNetView>();
            var record = view?.GetZDO();
            if (!NativeOwner(view, record)) return false;
            Cooking70Service.EnsurePlacedRecord(feast);
            if (!Cooking70FeastRetrofit.ExactPlaced(record)) return false;
            if (record.GetString(Cooking70FeastRetrofit.BirthKey, "").Length != 0 ||
                record.GetString(Cooking70FeastRetrofit.GrantKey, "").Length != 0) return true;
            // Separate grant never rewrites the strong producer record, tier or author.
            if (!Armed() || !NativeOwner(view, record) || WorkshopAtomicDebit.IsLocked(record)) return false;
            record.Set(Cooking70FeastRetrofit.GrantKey, "1:" + TargetWorld + ":" + Cooking70FeastRetrofit.Epoch);
            return true;
        }
        private static bool CallbackIdle() => !Mutating && CookingProductionScope.Current == null &&
            CookingDebitProvenance.Current == null && CookingFeastProvenance.Current == null && WorkshopHostCrafting.Current == null;
        private static bool ReceiptsIdle() => !WorkshopRemoteCraft.ClientBusy && !WorkshopRecovery.Outstanding &&
            !Crafting100PermitService.Outstanding && !GoldDivineTransactions.Outstanding;
        private static bool LocalIdle(Player player, Inventory inventory) => Armed() && CallbackIdle() && ReceiptsIdle() &&
            player != null && player == Player.m_localPlayer && player.GetPlayerID() != 0 &&
            NativeOwner(player.m_nview, player.m_nview?.GetZDO()) && ReferenceEquals(player.GetInventory(), inventory) &&
            !WorkshopChestLease.Pending(player.GetPlayerID()) && !WorkshopAtomicDebit.IsLocked(inventory) &&
            (InventoryGui.instance == null || InventoryGui.instance.m_craftTimer < 0f);
        private static bool ChestIdle(Container chest, Inventory inventory, ZDO record) => Armed() && CallbackIdle() && ReceiptsIdle() &&
            chest != null && NativeOwner(chest.m_nview, record) && !chest.m_loading && !chest.IsInUse() &&
            ReferenceEquals(chest.GetInventory(), inventory) && !WorkshopAtomicDebit.IsLocked(inventory) &&
            !WorkshopAtomicDebit.IsLocked(record) && !WorkshopChestLease.BlocksAccess(chest, 0) &&
            record.GetString(WorkshopRemoteCraft.EscrowKey, "").Length == 0 && record.GetString(WorkshopRecovery.SettlementKey, "").Length == 0;
        private static bool Apply(ItemDrop.ItemData item, Func<bool> current, Action changed)
        {
            if (!CallbackIdle() || !current() || !Cooking70FeastRetrofit.TryPrepare(item, TargetWorld, out var prepared)) return false;
            var before = item.m_customData;
            var snapshot = new Dictionary<string,string>(before);
            var shared = item.m_shared; long author = item.m_crafterID; string name = item.m_crafterName;
            int stack = item.m_stack, quality = item.m_quality;
            // No await/continuation; callback may only run after the synchronous CAS.
            if (!current() || !ReferenceEquals(item.m_customData, before) || !ReferenceEquals(item.m_shared, shared) ||
                item.m_stack != stack || item.m_quality != quality || item.m_crafterID != author || item.m_crafterName != name ||
                before.Count != snapshot.Count || snapshot.Any(pair => !before.TryGetValue(pair.Key, out string value) || value != pair.Value)) return false;
            Mutating = true;
            try { item.m_customData = prepared; changed(); }
            finally { Mutating = false; }
            return true;
        }
        private static void InventoryPass(Inventory inventory, Func<bool> current)
        {
            if (inventory == null || !current()) return;
            var cursor = Cursors.GetOrCreateValue(inventory);
            if (Time.time < cursor.Next) return;
            cursor.Next = Time.time + 1f;
            var items = inventory.GetAllItems();
            int count = Math.Min(8, items.Count);
            while (count-- > 0 && items.Count > 0)
            {
                if (cursor.Index >= items.Count) cursor.Index = 0;
                var item = items[cursor.Index++];
                Apply(item, () => current() && inventory.GetAllItems().Contains(item), () => inventory.Changed());
            }
        }
        internal static void PlayerPass(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            var inventory = player.GetInventory(); InventoryPass(inventory, () => LocalIdle(player, inventory));
        }
        internal static void ChestPass(Container chest)
        {
            if (chest == null) return;
            var record = chest.m_nview?.GetZDO(); var inventory = chest.GetInventory();
            InventoryPass(inventory, () => ChestIdle(chest, inventory, record));
        }
        internal static void DropPass(ItemDrop drop)
        {
            // The ItemDrop attached to a placed Feast is its native serving source,
            // not a loose physical item. Never migrate that shared-food path here.
            if (drop == null || drop.GetComponent<Feast>() != null) return;
            var item = drop.m_itemData; var record = drop.m_nview?.GetZDO();
            Apply(item, () => Armed() && CallbackIdle() && ReceiptsIdle() && NativeOwner(drop.m_nview, record) &&
                ReferenceEquals(drop.m_itemData, item) && !WorkshopAtomicDebit.IsLocked(record) && !WorkshopAtomicDebit.IsLocked(drop), drop.Save);
        }
    }
    [HarmonyPatch(typeof(ZoneSystem), "Update")]
    internal static class Cooking70FeastEpochPatch { private static void Postfix() => Cooking70FeastRetrofitRuntime.Tick(); }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Cooking70FeastInventoryRetrofitPatch { private static void Postfix(Player __instance) => Cooking70FeastRetrofitRuntime.PlayerPass(__instance); }
    [HarmonyPatch(typeof(Container), "CheckForChanges")]
    internal static class Cooking70FeastChestRetrofitPatch { private static void Postfix(Container __instance) => Cooking70FeastRetrofitRuntime.ChestPass(__instance); }
    [HarmonyPatch(typeof(ItemDrop), "SlowUpdate")]
    internal static class Cooking70FeastDropRetrofitPatch { private static void Postfix(ItemDrop __instance) => Cooking70FeastRetrofitRuntime.DropPass(__instance); }
    [HarmonyPatch(typeof(Feast), "Start")]
    internal static class Cooking70FeastLoadedRetrofitPatch { private static void Postfix(Feast __instance) => Cooking70FeastRetrofitRuntime.Observe(__instance); }
    [HarmonyPatch(typeof(Feast), "RPC_TryEat")]
    internal static class Cooking70FeastBeforeServingRetrofitPatch { private static void Prefix(Feast __instance) => Cooking70FeastRetrofitRuntime.TryPlaced(__instance); }
}
