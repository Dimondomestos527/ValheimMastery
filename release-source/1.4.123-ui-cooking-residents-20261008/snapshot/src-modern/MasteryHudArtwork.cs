using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace ValheimMastery
{
    // Presentation only. Resources owned by the plugin; never loaded on headless servers.
    internal sealed class MasteryHudArtwork : MonoBehaviour
    {
        private static MasteryHudArtwork Owner;
        private readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<Sprite, Sprite> Native = new Dictionary<Sprite, Sprite>();
        private readonly List<Texture2D> Textures = new List<Texture2D>();
        private readonly HashSet<string> FailedResources = new HashSet<string>();
        internal static readonly List<MasteryHudStatusSlot> Slots = new List<MasteryHudStatusSlot>();
        private static bool Warned;
        private sealed class ImageState {
            internal Sprite Sprite; internal Material Material; internal Image.Type Type; internal Color Tint; internal bool PreserveTint; internal bool PreserveAspect;
        }
        private readonly Dictionary<Image,ImageState> Images = new Dictionary<Image,ImageState>();
        private float NextPrune;
        private void Update() { if (!Ready) Cleanup(); }
        private void Preserve(Image image,bool preserveTint) {
            if(!Images.ContainsKey(image)) Images.Add(image,new ImageState { Sprite=image.sprite, Material=image.material, Type=image.type, Tint=image.color, PreserveTint=preserveTint, PreserveAspect=image.preserveAspect });
            if(Time.unscaledTime < NextPrune)return;
            NextPrune=Time.unscaledTime+10f;
            var dead=new List<Image>();foreach(var key in Images.Keys)if(key==null)dead.Add(key);foreach(var key in dead)Images.Remove(key);
        }
        internal static bool Ready => MasteryPlugin.Instance != null &&
            MasteryPlugin.Settings != null && MasteryPlugin.Settings.Enabled.Value &&
            !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
        private static readonly Dictionary<string,string> Icons = new Dictionary<string,string> {
            { "swords_35", "vm_icon_skill_swords_v002.png" },
            { "swords_70", "vm_icon_skill_swords_silver_v002.png" },
            { "swords_100", "vm_icon_skill_swords_silver_v001.png" },
            { "axes_35", "vm_icon_skill_axes_v003.png" },
            { "axes_70", "vm_icon_skill_axes_silver_v003.png" },
            { "axes_100", "vm_icon_skill_axes_gold_v002.png" },
            { "clubs_35", "vm_icon_skill_clubs_v002.png" },
            { "clubs_70", "vm_icon_skill_clubs_silver_v002.png" },
            { "clubs_100", "vm_icon_skill_clubs_gold_v005.png" },
            { "knives_35", "vm_icon_skill_knives_v001.png" },
            { "knives_70", "vm_icon_skill_knives_silver_v003.png" },
            { "knives_100", "vm_icon_skill_knives_gold_v001.png" },
            { "spears_35", "vm_icon_skill_spears_v001.png" },
            { "spears_70", "vm_icon_skill_spears_silver_v003.png" },
            { "spears_100", "vm_icon_skill_spears_silver_v001.png" },
            { "polearms_35", "vm_icon_skill_polearms_v002.png" },
            { "polearms_70", "vm_icon_skill_polearms_v001.png" },
            { "polearms_100", "vm_icon_skill_polearms_silver_v002.png" },
            { "bows_35", "vm_icon_skill_bows_v002.png" },
            { "bows_70", "vm_icon_skill_bows_v006.png" },
            { "bows_100", "vm_icon_skill_bows_silver_v002.png" },
            { "crossbows_35", "vm_icon_skill_crossbows_v002.png" },
            { "crossbows_70", "vm_icon_skill_crossbows_gold_v003.png" },
            { "crossbows_100", "vm_icon_skill_crossbows_deepnorth_v002.png" },
            { "fists_35", "vm_icon_skill_fists_v001.png" },
            { "fists_70", "vm_icon_skill_fists_silver_v001.png" },
            { "fists_100", "vm_icon_skill_fists_gold_v003.png" },
            { "blocking_35", "vm_icon_skill_blocking_v002.png" },
            { "blocking_70", "vm_icon_skill_blocking_silver_v002.png" },
            { "blocking_100", "vm_icon_skill_blocking_gold_v002.png" },
            { "dodge_35", "vm_icon_skill_dodge_v001.png" },
            { "dodge_70", "vm_icon_skill_dodge_v001.png" },
            { "dodge_100", "vm_icon_skill_dodge_v001.png" },
            { "sneak_35", "vm_icon_skill_sneak_v001.png" },
            { "sneak_70", "vm_icon_skill_sneak_v001.png" },
            { "sneak_100", "vm_icon_skill_sneak_v001.png" },
            { "run_35", "vm_icon_skill_run_v001.png" },
            { "run_70", "vm_icon_skill_run_v001.png" },
            { "run_100", "vm_icon_skill_run_v001.png" },
            { "jump_35", "vm_icon_skill_jump_v001.png" },
            { "jump_70", "vm_icon_skill_jump_v001.png" },
            { "jump_100", "vm_icon_skill_jump_v001.png" },
            { "swim_35", "vm_icon_skill_swim_v001.png" },
            { "swim_70", "vm_icon_skill_swim_v001.png" },
            { "swim_100", "vm_icon_skill_swim_v001.png" },
            { "ride_35", "vm_icon_skill_ride_v001.png" },
            { "ride_70", "vm_icon_skill_ride_v001.png" },
            { "ride_100", "vm_icon_skill_ride_v001.png" },
            { "elementalmagic_35", "vm_icon_skill_elementalmagic_v002.png" },
            { "elementalmagic_70", "vm_icon_skill_elementalmagic_silver_v002.png" },
            { "elementalmagic_100", "vm_icon_skill_elementalmagic_deepnorth_v002.png" },
            { "bloodmagic_35", "vm_icon_skill_bloodmagic_v006.png" },
            { "bloodmagic_70", "vm_icon_skill_bloodmagic_gold_v003.png" },
            { "bloodmagic_100", "vm_icon_skill_bloodmagic_deepnorth_v003.png" },
            { "woodcutting_35", "vm_icon_skill_woodcutting_v002.png" },
            { "woodcutting_70", "vm_icon_skill_woodcutting_v001.png" },
            { "woodcutting_100", "vm_icon_skill_woodcutting_silver_v001.png" },
            { "pickaxes_35", "vm_icon_skill_pickaxes_v002.png" },
            { "pickaxes_70", "vm_icon_skill_pickaxes_v001.png" },
            { "pickaxes_100", "vm_icon_skill_pickaxes_silver_v001.png" },
            { "fishing_35", "vm_icon_skill_fishing_v001.png" },
            { "fishing_70", "vm_icon_skill_fishing_v001.png" },
            { "fishing_100", "vm_icon_skill_fishing_v001.png" },
            { "farming_35", "vm_icon_skill_farming_v001.png" },
            { "farming_70", "vm_icon_skill_farming_silver_v002.png" },
            { "farming_100", "vm_icon_skill_farming_silver_v001.png" },
            { "cooking_35", "vm_icon_skill_cooking_v002.png" },
            { "cooking_70", "vm_icon_skill_cooking_silver_v004.png" },
            { "cooking_100", "vm_icon_skill_cooking_gold_v002.png" },
            { "crafting_35", "vm_icon_skill_crafting_v001.png" },
            { "crafting_70", "vm_icon_skill_crafting_v001.png" },
            { "crafting_100", "vm_icon_skill_crafting_v001.png" },
        };
        private static MasteryHudArtwork Get()
        {
            if (!Ready) return null;
            if (Owner == null) Owner = MasteryPlugin.Instance.gameObject.AddComponent<MasteryHudArtwork>();
            return Owner;
        }
        internal static Sprite Art(string file)
        {
            var owner = Get(); if (owner == null) return null;
            try { return owner.Load("ValheimMastery.Hud." + file, file); }
            catch (Exception error) { if (!Warned) { Warned = true; MasteryPlugin.Log?.LogWarning("HUD artwork fallback: " + error.Message); } return null; }
        }
        internal static Sprite Icon(string id, Sprite fallback)
        {
            var owner = Get();
            if (owner == null || id == null || !Icons.TryGetValue(id, out string file)) return fallback;
            try
            {
                var icon = owner.Load("ValheimMastery.Milestones." + file, "Icon_" + file);
                if (icon == null) return fallback;
                // Give each native fallback its own sprite identity; a shared artwork icon
                // can replace different staff/food/skill icons and must restore each exactly.
                string key = "__fallback_" + file + "_" + (fallback != null ? fallback.GetInstanceID().ToString() : "null");
                if (!owner.Sprites.TryGetValue(key, out var mapped) || mapped == null)
                {
                    mapped = Sprite.Create(icon.texture, icon.rect, new Vector2(.5f, .5f), icon.pixelsPerUnit);
                    mapped.name = icon.name;
                    owner.Sprites[key] = mapped;
                    owner.Native[mapped] = fallback;
                }
                return mapped;
            }
            catch (Exception error) { if (!Warned) { Warned = true; MasteryPlugin.Log?.LogWarning("HUD icon fallback: " + error.Message); } return fallback; }
        }
        private Sprite Load(string resource, string name)
        {
            if (Sprites.TryGetValue(resource, out var cached) && cached != null) return cached;
            if (FailedResources.Contains(resource)) return null;
            FailedResources.Add(resource);
            byte[] bytes;
            using (Stream stream = typeof(MasteryHudArtwork).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null || stream.Length <= 0 || stream.Length > 4 * 1024 * 1024) throw new InvalidDataException(resource);
                bytes = new byte[(int)stream.Length];
                int read = 0; while (read < bytes.Length) { int n = stream.Read(bytes, read, bytes.Length - read); if (n == 0) throw new EndOfStreamException(resource); read += n; }
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try {
                if (!ImageConversion.LoadImage(texture, bytes, true)) throw new InvalidDataException(resource);
                texture.name = "MasteryHud_" + name; texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
                Vector4 border = name.StartsWith("Icon_", StringComparison.Ordinal) || name == "fill.png" ? Vector4.zero :
                    name == "slot.png" ? new Vector4(10,10,10,10) :
                    name == "track.png" ? new Vector4(20,8,20,8) :
                    name == "proc.png" ? new Vector4(44,12,44,12) : new Vector4(24,24,24,24);
                var sprite = Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,border);
                Textures.Add(texture); FailedResources.Remove(resource); Sprites.Add(resource,sprite); return sprite;
            } catch { Destroy(texture); throw; }
        }
        internal static void Frame(Image image,string file)
        {
            var sprite=Art(file);if(image==null||sprite==null)return;
            Owner.Preserve(image,false);
            image.sprite=sprite;image.material=null;image.type=Image.Type.Sliced;
            image.color=Color.white;image.preserveAspect=false;
        }
        internal static void Fill(Image image)
        {
            var sprite=Art("fill.png");if(image==null||sprite==null)return;
            Owner.Preserve(image,true);
            image.sprite=sprite;image.material=null;image.type=Image.Type.Simple;
            image.preserveAspect=false; // Keep existing tint and fraction/anchors.
        }
        internal static void Proc(RectTransform panel,Image frame,Image backing)
        {
            if(panel==null)return;var sprite=Art("proc.png");if(sprite==null)return;
            var image=panel.gameObject.GetComponent<Image>()??panel.gameObject.AddComponent<Image>();
            Frame(image,"proc.png");image.raycastTarget=false;
            Frame(frame,"slot.png");if(backing!=null)backing.color=Color.clear;
        }
        internal static void PaintStatus(Hud hud,List<StatusEffect> effects)
        {
            Slots.RemoveAll(x=>x==null);
            if(hud==null||effects==null)return;
            int count=Mathf.Min(effects.Count,hud.m_statusEffects.Count);
            for(int i=0;i<count;i++) {
                var row=hud.m_statusEffects[i];if(row==null)continue;
                var effect=effects[i];var slot=row.GetComponent<MasteryHudStatusSlot>();
                bool owned=Ready&&effect!=null&&!effect.m_hidden&&effect.m_icon!=null&&Owner!=null&&Owner.Native.ContainsKey(effect.m_icon);
                if(!owned){slot?.Restore();continue;}
                if(slot==null)slot=row.gameObject.AddComponent<MasteryHudStatusSlot>();
                if(!Slots.Contains(slot))Slots.Add(slot);
                slot.Paint();
            }
        }
        internal static void Cleanup()
        {
            foreach(var slot in Slots)if(slot!=null)slot.Restore();Slots.Clear();
            MasteryHudTooltipStyle.Restore();
            if(Owner!=null){Owner.Release();Destroy(Owner);Owner=null;}
        }
        private void Release()
        {
            var manager=Player.m_localPlayer?.m_seman;
            if(manager!=null)foreach(var effect in manager.GetStatusEffects())
                if(effect!=null&&effect.m_icon!=null&&Native.TryGetValue(effect.m_icon,out var fallback))effect.m_icon=fallback;
            foreach(var pair in Images)if(pair.Key!=null) {
                pair.Key.sprite=pair.Value.Sprite;pair.Key.material=pair.Value.Material;pair.Key.type=pair.Value.Type;pair.Key.preserveAspect=pair.Value.PreserveAspect;
                if(!pair.Value.PreserveTint)pair.Key.color=pair.Value.Tint;
            }
            Images.Clear();
            foreach(var sprite in Sprites.Values)if(sprite!=null)Destroy(sprite);
            foreach(var texture in Textures)if(texture!=null)Destroy(texture);
            Sprites.Clear();Native.Clear();Textures.Clear();FailedResources.Clear();
        }
        private void OnDestroy(){Release();if(Owner==this)Owner=null;}
    }
    internal sealed class MasteryHudStatusSlot : MonoBehaviour
    {
        private Image FrameImage;private RectTransform IconRect;
        private Vector2 OldMin,OldMax,OldOffsetMin,OldOffsetMax;private bool Active;
        internal void Paint()
        {
            var icon=transform.Find("Icon")?.GetComponent<Image>();if(icon==null)return;
            var sprite=MasteryHudArtwork.Art("slot.png");if(sprite==null)return;
            if(!Active){IconRect=icon.rectTransform;OldMin=IconRect.anchorMin;OldMax=IconRect.anchorMax;OldOffsetMin=IconRect.offsetMin;OldOffsetMax=IconRect.offsetMax;Active=true;}
            if(FrameImage==null) {
                var go=new GameObject("VM_HudStatusFrame",typeof(RectTransform),typeof(Image));
                go.transform.SetParent(transform,false);FrameImage=go.GetComponent<Image>();FrameImage.raycastTarget=false;go.transform.SetAsFirstSibling();
            }
            var r=FrameImage.rectTransform;r.anchorMin=OldMin;r.anchorMax=OldMax;r.offsetMin=OldOffsetMin-new Vector2(2,2);r.offsetMax=OldOffsetMax+new Vector2(2,2);
            MasteryHudArtwork.Frame(FrameImage,"slot.png");FrameImage.gameObject.SetActive(true);
            IconRect.offsetMin=OldOffsetMin+new Vector2(4,4);IconRect.offsetMax=OldOffsetMax-new Vector2(4,4);
            // Native flash, cooldown object, label, timer and slot ordering remain untouched.
        }
        internal void Restore(){if(Active&&IconRect!=null){IconRect.anchorMin=OldMin;IconRect.anchorMax=OldMax;IconRect.offsetMin=OldOffsetMin;IconRect.offsetMax=OldOffsetMax;}Active=false;if(FrameImage!=null)FrameImage.gameObject.SetActive(false);}
        private void OnDestroy(){MasteryHudArtwork.Slots.Remove(this);}
    }
    internal sealed class MasteryHudTooltipTag : MonoBehaviour {}
    internal static class MasteryHudTooltipStyle
    {
        private static readonly System.Reflection.FieldInfo Current=AccessTools.Field(typeof(UITooltip),"m_current"),Tooltip=AccessTools.Field(typeof(UITooltip),"m_tooltip");
        private static Image Target;private static Sprite Original;private static Color Tint;private static Image.Type Kind;private static Material Material;private static bool Aspect;
        internal static void Restore(){if(Target!=null){Target.sprite=Original;Target.color=Tint;Target.type=Kind;Target.material=Material;Target.preserveAspect=Aspect;}Target=null;}
        internal static void Apply()
        {
            var current=Current?.GetValue(null) as UITooltip;var tooltip=Tooltip?.GetValue(null) as GameObject;
            bool owned=MasteryHudArtwork.Ready&&MasteryPlugin.Settings.EnableMasterySkillUI.Value&&current!=null&&current.GetComponent<MasteryHudTooltipTag>()!=null;
            if(!owned||tooltip==null){Restore();return;}
            if(Target!=null&&Target.transform.IsChildOf(tooltip.transform))return;
            Restore();Image best=null;float area=0;
            foreach(var image in tooltip.GetComponentsInChildren<Image>(true)){float a=Mathf.Abs(image.rectTransform.rect.width*image.rectTransform.rect.height);if(a>area){best=image;area=a;}}
            if(best==null||MasteryHudArtwork.Art("tooltip.png")==null)return;
            Target=best;Original=best.sprite;Tint=best.color;Kind=best.type;Material=best.material;Aspect=best.preserveAspect;
            MasteryHudArtwork.Frame(best,"tooltip.png");
        }
    }
    [HarmonyPatch(typeof(Hud),"UpdateStatusEffects")]
    internal static class MasteryHudStatusArtworkPatch
    {private static void Postfix(Hud __instance,List<StatusEffect> statusEffects)=>MasteryHudArtwork.PaintStatus(__instance,statusEffects);}
    [HarmonyPatch(typeof(UITooltip),"UpdateTextElements")]
    internal static class MasteryHudTooltipArtworkPatch
    {private static void Postfix()=>MasteryHudTooltipStyle.Apply();}
    [HarmonyPatch(typeof(MasteryPlugin),"OnDestroy")]
    internal static class MasteryHudArtworkCleanupPatch
    {private static void Prefix()=>MasteryHudArtwork.Cleanup();}
}
