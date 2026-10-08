using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Sap has no input queue. Its farming bonus belongs to the harvesting player,
    // not the builder, the nearest bystander, or the network owner of the collector.
    internal static class SapHarvestBonus
    {
        private sealed class Claim { internal float Level, Time; }
        private static readonly ConditionalWeakTable<SapCollector, Dictionary<long, Claim>> Claims =
            new ConditionalWeakTable<SapCollector, Dictionary<long, Claim>>();
        internal sealed class Harvest
        { internal int Units, Actual; internal float Level; internal SapCollector Collector; internal readonly HashSet<ItemDrop> Seen = new HashSet<ItemDrop>(); }
        [System.ThreadStatic] internal static Harvest Active;
        internal static void ObserveOutput(ItemDrop item, int stack)
        {
            Harvest harvest = Active;
            if (harvest == null || stack <= 0 || item?.m_itemData?.m_shared == null ||
                item.m_itemData.m_shared.m_name != harvest.Collector.m_spawnItem.m_itemData.m_shared.m_name ||
                (item.transform.position - harvest.Collector.m_spawnPoint.position).sqrMagnitude > 1f || !harvest.Seen.Add(item)) return;
            harvest.Actual++;
        }

        internal static void Register(SapCollector collector)
        {
            if (collector?.m_nview?.IsValid() != true) return;
            collector.m_nview.Register<float>("VM_SapHarvestSkill", (sender, level) =>
            {
                if (!collector.m_nview.IsOwner() || !OwnerSkillAuthority.Valid(level) ||
                    ProcessingStationProgressionService.ResolveSender(sender) == 0) return;
                var claims = Claims.GetOrCreateValue(collector);
                // Only live, short-lived interaction claims; no world-wide player cache.
                foreach (long key in new List<long>(claims.Keys))
                    if (Time.time - claims[key].Time > 10f) claims.Remove(key);
                claims[sender] = new Claim { Level = level, Time = Time.time };
            });
            collector.m_nview.Register<int>("VM_SapBonusFx", (sender, count) =>
            {
                Player player = Player.m_localPlayer;
                if (collector.m_nview.GetZDO()?.GetOwner() != sender || count < 1 || count > 4096 || player == null ||
                    (player.transform.position - collector.transform.position).sqrMagnitude > 900f) return;
                PerkVisualService.PlayRemoteProc(player, "resource_bonus:" + count, collector.transform.position);
            });
        }
        internal static void Send(SapCollector collector, Humanoid character, bool repeat)
        {
            if (repeat || !(character is Player player) || player != Player.m_localPlayer ||
                !MasteryPlugin.Settings.Enabled.Value || collector?.m_nview?.IsValid() != true || collector.GetLevel() <= 0) return;
            collector.m_nview.InvokeRPC("VM_SapHarvestSkill",
                PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Farming));
        }
        internal static Harvest Before(SapCollector collector, long caller)
        {
            if (!MasteryPlugin.Settings.Enabled.Value || collector?.m_nview?.IsOwner() != true ||
                collector.m_spawnItem == null || collector.m_spawnPoint == null) return null;
            var claims = Claims.GetOrCreateValue(collector);
            if (!claims.TryGetValue(caller, out Claim claim)) return null;
            claims.Remove(caller); // A repeated Extract cannot reuse the skill claim.
            if (Time.time - claim.Time > 10f || collector.GetLevel() <= 0) return null;
            return new Harvest { Units = collector.GetLevel(), Collector = collector, Level = claim.Level };
        }
        internal static void After(SapCollector collector, Harvest harvest)
        {
            if (harvest == null || collector.GetLevel() != 0) return;
            int actual = harvest.Actual;
            int bonus = 0;
            for (int i = 0; i < Mathf.Min(actual, harvest.Units); i++)
                if (PerkRuntimeService.RollChance(Mathf.Clamp01(harvest.Level * .005f))) bonus++;
            MasteryPlugin.Log.LogInfo("[SapBonus] units=" + harvest.Units + " actual=" + actual + " farming=" + harvest.Level + " bonus=" + bonus);
            int delivered = 0;
            while (bonus > 0)
            {
                int amount = Mathf.Min(bonus, Mathf.Max(1, collector.m_spawnItem.m_itemData.m_shared.m_maxStackSize));
                ItemDrop drop = Object.Instantiate(collector.m_spawnItem,
                    collector.m_spawnPoint.position, Quaternion.identity);
                if (drop == null) break;
                drop.SetStack(amount); bonus -= amount; delivered += amount;
            }
            if (delivered > 0) collector.m_nview.InvokeRPC(ZNetView.Everybody, "VM_SapBonusFx", delivered);
        }
    }
    [HarmonyPatch(typeof(SapCollector), "Awake")]
    internal static class SapHarvestRegistrationPatch
    { private static void Postfix(SapCollector __instance) => SapHarvestBonus.Register(__instance); }
    [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.Interact))]
    internal static class SapHarvestSkillPatch
    { private static void Prefix(SapCollector __instance, Humanoid character, bool repeat) => SapHarvestBonus.Send(__instance, character, repeat); }
    [HarmonyPatch(typeof(SapCollector), "RPC_Extract")]
    internal static class SapHarvestOutputPatch
    {
        internal sealed class Scope { internal SapHarvestBonus.Harvest Previous, Current; }
        private static void Prefix(SapCollector __instance, long caller, out Scope __state)
        {
            __state = new Scope { Previous = SapHarvestBonus.Active, Current = SapHarvestBonus.Before(__instance, caller) };
            SapHarvestBonus.Active = __state.Current;
        }
        private static void Postfix(SapCollector __instance, Scope __state)
        { SapHarvestBonus.Active = __state.Previous; SapHarvestBonus.After(__instance, __state.Current); }
        private static System.Exception Finalizer(Scope __state, System.Exception __exception)
        { if (__state != null) SapHarvestBonus.Active = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.SetStack))]
    internal static class SapNativeOutputObservationPatch
    { private static void Postfix(ItemDrop __instance, int __0) => SapHarvestBonus.ObserveOutput(__instance, __0); }
}
