using System;
using System.IO;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class Utility100InspirationDebug
    {
        private static Terminal Output;
        internal static void RegisterCommands()
        {
            if (!MasteryPlugin.Settings.UIDebugLogging.Value) return;
            new Terminal.ConsoleCommand("vm_inspiration_reset", "Скинути лише власне виснаження Вьолундра; адміністратор/debug.", args =>
            {
                Output = args.Context;
                var net = ZNet.instance;
                if (net?.GetWorld() == null || Player.m_localPlayer == null) { Output.AddString("Спочатку увійди у світ."); return; }
                var packet = new ZPackage(); packet.Write(net.GetWorldUID());
                if (net.IsServer()) { packet.SetPos(0); Receive(null, packet); }
                else net.GetServerRPC()?.Invoke("VM_Utility100_ResetInspiration_v1", packet);
            }, false);
        }
        internal static void Register(ZNetPeer peer)
        {
            peer?.m_rpc?.Register<ZPackage>("VM_Utility100_ResetInspiration_v1", Receive);
            peer?.m_rpc?.Register<string>("VM_Utility100_ResetReply_v1", Reply);
        }
        private static void Reply(ZRpc rpc, string text)
        {
            var net = ZNet.instance;
            if (text == null || text.Length > 256 || net?.GetWorld() == null ||
                (rpc != null && (net.IsServer() || rpc != net.GetServerRPC()))) return;
            Output?.AddString(text);
        }
        private static void Tell(ZRpc rpc, string text)
        { if (rpc == null) Reply(null, text); else rpc.Invoke("VM_Utility100_ResetReply_v1", text); }
        private static void Receive(ZRpc rpc, ZPackage packet)
        {
            var net = ZNet.instance;
            if (net?.IsServer() != true || net.GetWorld() == null || packet == null || packet.Size() != 8) return;
            try
            {
                if (packet.ReadLong() != net.GetWorldUID()) return;
                bool admin = rpc == null || net.IsAdmin(net.GetPeer(rpc)?.m_socket?.GetHostName() ?? "");
                var actor = WorkshopActor.Resolve(rpc);
                if (!admin || !MasteryPlugin.Settings.UIDebugLogging.Value || actor?.Available != true)
                { Tell(rpc, "Потрібні права адміністратора та UI.DebugLogging=true на сервері."); return; }
                var ledger = GoldCraftingService.ServerLedger;
                var session = Traverse.Create(typeof(GoldCraftingService)).Field("Session").GetValue<ZNet>();
                string path = ledger == null ? null : Traverse.Create(ledger).Field("_path").GetValue<string>();
                string expected = Path.GetFullPath(Path.Combine(BepInEx.Paths.ConfigPath, "ValheimMasteryGold", net.GetWorldUID() + ".bin"));
                if (!GoldCraftingService.Enabled || ledger?.IsAvailable != true || !ReferenceEquals(session, net) ||
                    !ReferenceEquals(ledger, GoldCraftingService.ServerLedger) || !string.Equals(path, expected, StringComparison.OrdinalIgnoreCase))
                { Tell(rpc, "Пам’ять покровителя зараз недоступна."); return; }
                long id = actor.GetPlayerID(); var state = ledger.Get(id);
                if (state?.Unlocked != true || state.Pending != null || state.Claims.Count != 0)
                { Tell(rpc, "Спершу заверши попередню дію покровителя."); return; }
                if (state.ExhaustionRemaining <= 0) { Tell(rpc, "Вьолундр уже готовий дарувати натхнення."); return; }
                if (!string.Equals(state.ExhaustedPatron, GoldCooldownPolicy.LegacyPatronId, StringComparison.Ordinal))
                { Tell(rpc, "Це виснаження належить іншому покровителю."); return; }
                if (!ledger.Elapse(id, state.ExhaustionRemaining) || !ledger.Flush())
                { Tell(rpc, "Не вдалося зберегти зміну. Скидання не підтверджено."); return; }
                GoldCraftingService.NotifyFavor(id);
                Tell(rpc, "КД натхнення Вьолундра скинуто.");
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Utility100Debug] Reset declined: " + error.Message); }
        }
    }
    [HarmonyPatch(typeof(PerkDebugService), nameof(PerkDebugService.RegisterCommands))]
    internal static class Utility100InspirationCommandPatch
    { private static void Postfix() => Utility100InspirationDebug.RegisterCommands(); }
    [HarmonyPatch(typeof(NetworkSync), nameof(NetworkSync.Register))]
    internal static class Utility100InspirationNetworkPatch
    { private static void Postfix(ZNetPeer peer) => Utility100InspirationDebug.Register(peer); }
}
