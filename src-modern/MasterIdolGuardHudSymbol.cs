using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Runs inside the native displayed-HUD loop, after its two level indicators.
    internal static class MasterIdolGuardHudSymbol
    {
        private static readonly ConditionalWeakTable<EnemyHud.HudData, MasterIdolGuardHudSymbolState> States =
            new ConditionalWeakTable<EnemyHud.HudData, MasterIdolGuardHudSymbolState>();
        private static ZNet AssetSession;
        private static long AssetWorld;
        private static bool AssetTried;
        private static Sprite Asset;

        internal static bool Identity(Character actor, out ZDO data)
        {
            data = null;
            if (actor == null || actor is Player || actor.m_nview?.IsValid() != true) return false;
            data = actor.m_nview.GetZDO();
            return data != null && ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data) &&
                MasterIdolForestGuardState.ModuleBorn(data);
        }
        internal static bool Graphics => !Application.isBatchMode &&
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && MasteryPlugin.Settings.Enabled.Value;

        private static Sprite Sprite()
        {
            var net = ZNet.instance;
            long world = net?.GetWorld() == null ? 0 : net.GetWorldUID();
            if (AssetSession != net || AssetWorld != world)
            { AssetSession = net; AssetWorld = world; AssetTried = false; Asset = null; }
            if (!AssetTried)
            { AssetTried = true; Asset = ApprovedUiAssetBank.Enchantment("BlackForest"); }
            return Asset;
        }
        internal static void Apply(EnemyHud.HudData hud)
        {
            if (hud == null) return;
            States.TryGetValue(hud, out var state);
            try
            {
            if (!Graphics || hud.m_gui == null || !Identity(hud.m_character, out var data) ||
                hud.m_level2 == null || hud.m_level3 == null)
            { if (state != null) state.RestoreAfterNative(hud); return; }
            var sprite = Sprite();
            if (sprite == null) { if (state != null) state.RestoreAfterNative(hud); return; }
            if (state == null || state.gameObject != hud.m_gui)
            {
                if (state != null) state.Restore();
                state = hud.m_gui.GetComponent<MasterIdolGuardHudSymbolState>() ??
                    hud.m_gui.AddComponent<MasterIdolGuardHudSymbolState>();
                States.Remove(hud); States.Add(hud, state);
            }
            state.Show(hud, data, sprite);
            }
            catch { if (state != null) state.Restore(); } // Cosmetic failure cannot interrupt native health/name HUD updates.
        }

        internal static IEnumerable<CodeInstruction> Insert(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>();
            foreach (var instruction in instructions) code.Add(new CodeInstruction(instruction));
            var name = AccessTools.Field(typeof(EnemyHud.HudData), "m_name");
            var level = AccessTools.Field(typeof(EnemyHud.HudData), "m_level3");
            var setActive = AccessTools.Method(typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) });
            var gameObject = AccessTools.PropertyGetter(typeof(Component), nameof(Component.gameObject));
            var implicitObject = AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit", new[] { typeof(UnityEngine.Object) });
            int join = -1, matches = 0;
            for (int i = 1; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldfld && Equals(code[i].operand, name)) { join = i - 1; matches++; }
            // Pin the actual native two-star branch and shared false/true join, not an offset/local number.
            if (matches != 1 || join < 11 || !LoadLocal(code[join]) ||
                !SameLoad(code[join], code[join - 11]) || !SameLoad(code[join], code[join - 7]) ||
                code[join - 10].opcode != OpCodes.Ldfld || !Equals(code[join - 10].operand, level) ||
                !code[join - 9].Calls(implicitObject) ||
                (code[join - 8].opcode != OpCodes.Brfalse && code[join - 8].opcode != OpCodes.Brfalse_S) ||
                !(code[join - 8].operand is Label target) || !code[join].labels.Contains(target) ||
                code[join - 6].opcode != OpCodes.Ldfld || !Equals(code[join - 6].operand, level) ||
                !code[join - 5].Calls(gameObject) || !LoadLocal(code[join - 4]) ||
                code[join - 3].opcode != OpCodes.Ldc_I4_3 || code[join - 2].opcode != OpCodes.Ceq ||
                !code[join - 1].Calls(setActive) || code[join].blocks.Count != 0)
                throw new InvalidOperationException("Guard HUD: native level/name join mismatch");
            var load = new CodeInstruction(code[join].opcode, code[join].operand);
            load.labels.AddRange(code[join].labels); code[join].labels.Clear();
            code.InsertRange(join, new[] { load, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(MasterIdolGuardHudSymbol), nameof(Apply))) });
            return code;
        }
        private static bool LoadLocal(CodeInstruction code) => code.opcode == OpCodes.Ldloc || code.opcode == OpCodes.Ldloc_S ||
            code.opcode == OpCodes.Ldloc_0 || code.opcode == OpCodes.Ldloc_1 || code.opcode == OpCodes.Ldloc_2 || code.opcode == OpCodes.Ldloc_3;
        private static bool SameLoad(CodeInstruction a, CodeInstruction b) => a.opcode == b.opcode && Equals(a.operand, b.operand);
    }

    internal sealed class MasterIdolGuardHudSymbolState : MonoBehaviour
    {
        private EnemyHud.HudData Hud;
        private Character Actor;
        private ZDO Data;
        private ZNet Session;
        private ZDOMan Manager;
        private long World;
        private ulong Generation;
        private ushort Revision;
        private RectTransform Level2, Level3;
        private readonly Vector3[] Corners = new Vector3[4];
        private bool Was2, Was3, Showing;
        private Image Icon;

        internal void Show(EnemyHud.HudData hud, ZDO data, Sprite sprite)
        {
            // Native has just refreshed the star roots. Never replay an old binding's active flags over it.
            if (Hud != hud || Level2 != hud.m_level2 || Level3 != hud.m_level3) RestoreAfterNative(hud);
            Hud = hud; Actor = hud.m_character; Data = data; Session = ZNet.instance; Manager = ZDOMan.instance;
            World = Session.GetWorldUID(); Generation = MasterIdolLoadIdentity.Generation; Revision = data.OwnerRevision;
            Level2 = hud.m_level2; Level3 = hud.m_level3;
            Was2 = Level2.gameObject.activeSelf; Was3 = Level3.gameObject.activeSelf;
            var gui = hud.m_gui.transform as RectTransform;
            var health = hud.m_healthFast?.transform.parent as RectTransform;
            var name = hud.m_name?.rectTransform;
            if (gui == null || health == null || name == null || !health.IsChildOf(gui) || !name.IsChildOf(gui) ||
                !Bounds(gui, health, out var healthBounds) || !Bounds(gui, name, out var nameBounds) ||
                !TryLayout(healthBounds, nameBounds, out var center))
            { RestoreAfterNative(hud); return; }
            if (Icon == null)
            {
                var own = new GameObject("VM_ForestGuardSymbol", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                own.SetActive(false); own.layer = Level2.gameObject.layer;
                Icon = own.GetComponent<Image>(); Icon.raycastTarget = false; Icon.preserveAspect = true;
            }
            var rect = Icon.rectTransform;
            if (rect.parent != gui) rect.SetParent(gui, false);
            // Anchor at the parent's pivot: converted GUI-local (0,0) stays the actual anchor origin.
            rect.anchorMin = rect.anchorMax = gui.pivot; rect.pivot = new Vector2(.5f,.5f);
            rect.sizeDelta = new Vector2(SymbolSize,SymbolSize); rect.anchoredPosition3D = new Vector3(center.x,center.y,0);
            rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one;
            Icon.sprite = sprite; Icon.color = Color.white;
            Showing = true; Level2.gameObject.SetActive(false); Level3.gameObject.SetActive(false);
            Icon.gameObject.SetActive(true);
        }
        internal const float SymbolSize = 16f, Padding = 2f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private bool Bounds(RectTransform gui, RectTransform widget, out Rect bounds)
        {
            bounds = default; widget.GetWorldCorners(Corners);
            float minX=float.PositiveInfinity,minY=float.PositiveInfinity,maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
            for(int i=0;i<Corners.Length;i++)
            {
                var point=gui.InverseTransformPoint(Corners[i]);if(!Finite(point.x)||!Finite(point.y))return false;
                minX=Mathf.Min(minX,point.x);maxX=Mathf.Max(maxX,point.x);minY=Mathf.Min(minY,point.y);maxY=Mathf.Max(maxY,point.y);
            }
            bounds=Rect.MinMaxRect(minX,minY,maxX,maxY);return bounds.width>0&&bounds.height>0;
        }
        internal static bool TryLayout(Rect health,Rect name,out Vector2 center)
        {
            center=default;
            if(!Finite(health.xMin)||!Finite(health.xMax)||!Finite(health.yMin)||!Finite(health.yMax)||
                !Finite(name.xMin)||!Finite(name.xMax)||!Finite(name.yMin)||!Finite(name.yMax)||
                health.width<=0||health.height<=0||name.width<=0||name.height<=0)return false;
            float half=SymbolSize*.5f,x=health.xMax+Padding+half,y=health.center.y;
            // The native name RectTransform is a 400x52 layout container, not its visible glyph bounds.
            // Raising the symbol over that empty container separated it from the health bar.
            // Keep the rune aligned to the health bar, outside its right edge.
            if(!Finite(x)||!Finite(y))return false;center=new Vector2(x,y);return true;
        }
        internal void Restore(bool nativeFresh = false)
        {
            if (Showing && !nativeFresh)
            {
                if (Level2 != null) Level2.gameObject.SetActive(Was2);
                if (Level3 != null) Level3.gameObject.SetActive(Was3);
            }
            Showing = false;
            if (Icon != null) { Icon.gameObject.SetActive(false); Icon.sprite = null; }
            Hud = null; Actor = null; Data = null;
        }
        internal void RestoreAfterNative(EnemyHud.HudData fresh)
        {
            // Restore detached old widgets, but never overwrite the native result on current widgets.
            if (Showing)
            {
                if (Level2 != null && Level2 != fresh.m_level2) Level2.gameObject.SetActive(Was2);
                if (Level3 != null && Level3 != fresh.m_level3) Level3.gameObject.SetActive(Was3);
            }
            Restore(true);
        }
        private void LateUpdate()
        {
            if (!Showing) return;
            if (!MasterIdolGuardHudSymbol.Graphics || Session != ZNet.instance || Manager != ZDOMan.instance ||
                Session?.GetWorld() == null || World != Session.GetWorldUID() || Generation != MasterIdolLoadIdentity.Generation ||
                Hud?.m_gui != gameObject || Hud.m_character != Actor || Hud.m_level2 != Level2 || Hud.m_level3 != Level3 ||
                !MasterIdolGuardHudSymbol.Identity(Actor, out var current) || !ReferenceEquals(current, Data) ||
                Data.OwnerRevision != Revision || Icon == null || Icon.sprite == null) Restore();
        }
        private void OnDisable() => Restore();
        private void OnDestroy()
        {
            Restore();
            if (Icon != null) Destroy(Icon.gameObject);
        }
    }
    [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
    internal static class MasterIdolGuardHudSymbolPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            MasterIdolGuardHudSymbol.Insert(instructions);
    }
}
