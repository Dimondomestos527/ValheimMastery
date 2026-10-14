using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Run70MapMarkerPresentation
    {
        private static readonly FieldInfo PinsField = AccessTools.Field(typeof(Minimap), "m_pins");
        private static readonly FieldInfo NamePinField = AccessTools.Field(typeof(Minimap), "m_namePin");
        private static readonly FieldInfo NameInputField = AccessTools.Field(typeof(Minimap), "m_nameInput");
        private sealed class Capture
        {
            internal Minimap Map; internal Vector3 Position; internal string Label, Category;
            internal Sprite Sprite;
            internal Minimap.PinData Pin; internal bool Ambiguous;
        }
        private sealed class Entry { internal string Name, Category; internal Sprite Sprite, NativeSprite; internal float RetryAt; }
        private static Capture _capture;
        private static Minimap _map;
        private static ZNet _session;
        private static Player _player;
        private static long _world;
        private static readonly Dictionary<Minimap.PinData, Entry> Owned = new Dictionary<Minimap.PinData, Entry>();
        private static readonly List<Minimap.PinData> Stale = new List<Minimap.PinData>();
        private static Func<string, Sprite> _icon;
        private static bool _reportedCaptureFailure;
        internal static Sprite EnchantmentIcon(string choice) => ApprovedUiAssetBank.Enchantment(choice);
        internal static void ReleaseAssets()
        {
            // Never let a presentation failure block domain shutdown or unpatching.
            try { WorkbenchEnchantmentUi.Shutdown(); }
            catch (Exception error) { CleanupError("UI", error); }
            try { Clear(); }
            catch (Exception error) { CleanupError("map", error); }
            try { ApprovedUiAssetBank.Shutdown(); }
            catch (Exception error) { CleanupError("assets", error); }
        }
        private static void CleanupError(string stage, Exception error)
        {
            try { MasteryPlugin.Log?.LogWarning("[MasteryPresentation] Cleanup " + stage + " failed: " + error); }
            catch { } // A failing logger must not abort the remaining cleanup stages.
        }
        internal static void Configure(Func<string, Sprite> icon) => _icon = icon;
        private static bool Current(Minimap map) => !Application.isBatchMode && map != null && map == Minimap.instance &&
            Player.m_localPlayer != null && Run70MapMarkerState.World != 0;
        private static void Bind(Minimap map)
        {
            long world = Run70MapMarkerState.World;
            if (_map == map && _world == world && _session == ZNet.instance && _player == Player.m_localPlayer) return;
            RestoreOwnedIcons(); Owned.Clear(); Stale.Clear(); _capture = null;
            _reportedCaptureFailure = false;
            _map = map; _world = world; _session = ZNet.instance; _player = Player.m_localPlayer;
            Restore(map);
        }
        private static void Restore(Minimap map)
        {
            if (!Current(map) || !(PinsField?.GetValue(map) is List<Minimap.PinData> pins)) return;
            foreach (var pin in pins)
                if (Run70MapMarkerState.TryRead(pin, _world, out string category, out _))
                    Owned[pin] = new Entry { Name = pin.m_name, Category = category, NativeSprite = pin.m_icon };
        }
        internal static bool TryDiscoverRun70(Minimap map, Vector3 position, string category, string label)
        {
            if (!Current(map) || _capture != null || !Run70MapMarkerState.Finite(position)) return false;
            string encoded = Run70MapMarkerState.Encode(Run70MapMarkerState.World, category, label);
            if (encoded == null) return false;
            Bind(map);
            foreach (var pair in Owned)
                if (pair.Key.m_name == pair.Value.Name && pair.Value.Category == category &&
                    Utils.DistanceXZ(pair.Key.m_pos, position) < 1f) return false;
            var capture = new Capture { Map = map, Position = position, Label = label, Category = category };
            _capture = capture;
            bool created;
            try { created = map.DiscoverLocation(position, Minimap.PinType.Icon4, label, false); }
            finally { if (ReferenceEquals(_capture, capture)) _capture = null; }
            var pin = capture.Pin;
            if (!created) return false;
            if (capture.Ambiguous || pin == null || !Current(map) || _world != Run70MapMarkerState.World ||
                pin.m_name != label || pin.m_type != Minimap.PinType.Icon4 || !pin.m_save || pin.m_ownerID != 0)
            {
                if (!_reportedCaptureFailure)
                {
                    _reportedCaptureFailure = true;
                    try { MasteryPlugin.Log?.LogWarning("[Run70Map] Native discovery succeeded without exact category capture; kept native pin."); } catch { }
                }
                return true; // Native success stops the scan; cosmetic proof is a separate boundary.
            }
            pin.m_name = encoded;
            Owned[pin] = new Entry { Name = encoded, Category = category, Sprite = capture.Sprite, RetryAt = Time.unscaledTime + 1f, NativeSprite = pin.m_icon };
            // Existing label object is refreshed without a global name or icon override.
            if (pin.m_NamePinData?.PinNameText != null)
                pin.m_NamePinData.PinNameText.text = label;
            Render(map); return true;
        }
        internal static void CaptureNew(Minimap map, Minimap.PinData pin)
        {
            var context = _capture;
            if (context == null || context.Map != map || pin == null || pin.m_name != context.Label ||
                pin.m_type != Minimap.PinType.Icon4 || (pin.m_pos - context.Position).sqrMagnitude > .0001f) return;
            if (context.Pin != null) context.Ambiguous = true;
            else
            {
                context.Pin = pin;
                // Resolve once inside this synchronous native discovery; both consumers
                // use this exact reference. Missing assets retain native fallback.
                try { context.Sprite = _icon != null ? _icon(context.Category) : ApprovedUiAssetBank.Run70(context.Category); }
                catch { }
            }
        }
        internal static void NotificationIcon(Character actor, MessageHud.MessageType type, string text, ref Sprite icon)
        {
            var context = _capture;
            var pin = context?.Pin;
            if (context == null || context.Ambiguous || context.Sprite == null || pin == null ||
                actor != Player.m_localPlayer || type != MessageHud.MessageType.TopLeft ||
                !Current(context.Map) || context.Map != Minimap.instance ||
                pin.m_name != context.Label || pin.m_type != Minimap.PinType.Icon4 || !pin.m_save || pin.m_ownerID != 0 ||
                text != "$msg_pin_added: " + context.Label) return;
            icon = context.Sprite;
        }
        internal static void Reload(Minimap map)
        {
            if (!Current(map)) { Clear(); return; }
            _map = null; Bind(map);
        }
        internal static void Removed(Minimap.PinData pin) { if (pin != null) Owned.Remove(pin); }
        internal static void Render(Minimap map)
        {
            if (!Current(map)) { Clear(); return; }
            Bind(map); Stale.Clear();
            foreach (var pair in Owned)
            {
                var pin = pair.Key; var entry = pair.Value;
                if (pin.m_name != entry.Name || pin.m_type != Minimap.PinType.Icon4 || pin.m_ownerID != 0)
                {
                    // Dropped/edited provenance returns this pin to its own native icon.
                    RestoreIcon(pin, entry);
                    Stale.Add(pin); continue;
                }
                if (entry.Sprite == null && Time.unscaledTime >= entry.RetryAt)
                {
                    entry.RetryAt = Time.unscaledTime + 1f;
                    try { entry.Sprite = _icon != null ? _icon(entry.Category) : ApprovedUiAssetBank.Run70(entry.Category); } catch { }
                }
                if (entry.Sprite != null)
                {
                    pin.m_icon = entry.Sprite;
                    if (pin.m_iconElement != null && pin.m_iconElement.sprite != entry.Sprite)
                        pin.m_iconElement.sprite = entry.Sprite;
                }
            }
            foreach (var pin in Stale) Owned.Remove(pin);
            Stale.Clear();
        }
        internal static void PlainRenameInput(Minimap map)
        {
            if (!Current(map) || !(NamePinField?.GetValue(map) is Minimap.PinData pin) ||
                !(NameInputField?.GetValue(map) is TMP_InputField input) || !input.gameObject.activeInHierarchy ||
                input.text != pin.m_name || !Run70MapMarkerState.TryRead(pin, Run70MapMarkerState.World, out _, out string label)) return;
            input.SetTextWithoutNotify(label);
        }
        private static void RestoreIcon(Minimap.PinData pin, Entry entry)
        {
            if (entry.NativeSprite == null || entry.Sprite == null) return;
            if (pin.m_icon == entry.Sprite) pin.m_icon = entry.NativeSprite;
            if (pin.m_iconElement != null && pin.m_iconElement.sprite == entry.Sprite)
                pin.m_iconElement.sprite = entry.NativeSprite;
        }
        private static void RestoreOwnedIcons()
        { foreach (var pair in Owned) RestoreIcon(pair.Key, pair.Value); }
        internal static void Clear()
        { RestoreOwnedIcons(); Owned.Clear(); Stale.Clear(); _capture = null; _map = null; _session = null; _player = null; _world = 0; }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.Message), new[] { typeof(MessageHud.MessageType), typeof(string), typeof(int), typeof(Sprite), typeof(bool) })]
    internal static class Run70DiscoveryNotificationIconPatch
    {
        private static void Prefix(Player __instance, MessageHud.MessageType __0, string __1, ref Sprite __3)
            => Run70MapMarkerPresentation.NotificationIcon(__instance, __0, __1, ref __3);
    }
    [HarmonyPatch(typeof(Minimap), "AddPin")]
    internal static class Run70MapMarkerCapturePatch
    {
        private static void Postfix(Minimap __instance, Minimap.PinData __result) => Run70MapMarkerPresentation.CaptureNew(__instance, __result);
    }
    [HarmonyPatch(typeof(Minimap), "SetMapData")]
    internal static class Run70MapMarkerReloadPatch
    {
        private static void Postfix(Minimap __instance) => Run70MapMarkerPresentation.Reload(__instance);
    }
    [HarmonyPatch(typeof(Minimap), "RemovePin", new[] { typeof(Minimap.PinData) })]
    internal static class Run70MapMarkerRemovePatch
    {
        private static void Postfix(Minimap.PinData __0) => Run70MapMarkerPresentation.Removed(__0);
    }
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Run70MapMarkerRenderPatch
    {
        private static void Postfix(Minimap __instance) => Run70MapMarkerPresentation.Render(__instance);
    }
    [HarmonyPatch(typeof(Minimap), "OnDestroy")]
    internal static class Run70MapMarkerShutdownPatch
    {
        private static void Prefix() => Run70MapMarkerPresentation.Clear();
    }
    [HarmonyPatch(typeof(Minimap), "Update")]
    internal static class Run70MapMarkerRenameInputPatch
    { private static void Postfix(Minimap __instance) => Run70MapMarkerPresentation.PlainRenameInput(__instance); }
    [HarmonyPatch(typeof(MasteryPlugin), "OnDestroy")]
    internal static class Run70ApprovedAssetsShutdownPatch
    { private static void Prefix() => Run70MapMarkerPresentation.ReleaseAssets(); }
}
