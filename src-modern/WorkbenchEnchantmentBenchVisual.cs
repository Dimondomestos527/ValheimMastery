using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    // Local cosmetic projection only; no ZDO writes, lights, colliders or world scan.
    internal sealed class WorkbenchEnchantmentBenchVisual : MonoBehaviour
    {
        static readonly LinkedList<WorkbenchEnchantmentBenchVisual> Loaded=new LinkedList<WorkbenchEnchantmentBenchVisual>();
        static readonly Dictionary<string,Material> GlowMaterials=new Dictionary<string,Material>(StringComparer.Ordinal);static float Next;
        LinkedListNode<WorkbenchEnchantmentBenchVisual> node;ZNetView view;ZDO identity;ZDOID uid;
        string marker,home,choice;SpriteRenderer symbol;float RetryAsset;
        static bool Graphics=>!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null;
        internal static void Attach(CraftingStation station)
        {
            if(!Graphics||station==null||Utils.GetPrefabName(station.gameObject)!="piece_workbench")return;
            if(station.GetComponent<WorkbenchEnchantmentBenchVisual>()==null)station.gameObject.AddComponent<WorkbenchEnchantmentBenchVisual>();
        }
        void OnEnable(){if(Graphics&&node==null)node=Loaded.AddLast(this);}
        void OnDisable(){if(node!=null){Loaded.Remove(node);node=null;}Hide();}
        void OnDestroy(){OnDisable();if(symbol!=null)Destroy(symbol.gameObject);}
        void Hide(){if(symbol!=null)symbol.enabled=false;choice=null;}
        internal static void Tick()
        {
            if(!Graphics||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.1f;
            int budget=Math.Min(16,Loaded.Count);
            for(int i=0;i<budget;i++){var entry=Loaded.First;var item=entry.Value;Loaded.Remove(entry);Loaded.AddLast(entry);if(item!=null)item.Refresh();}
        }
        void Refresh()
        {
            if(Player.m_localPlayer==null||!MasteryPlugin.Settings.Enabled.Value||(Player.m_localPlayer.transform.position-transform.position).sqrMagnitude>4096){Hide();return;}
            if(view==null)view=GetComponent<ZNetView>();var data=view?.GetZDO();
            if(view?.IsValid()!=true||data?.GetPrefab()!="piece_workbench".GetStableHashCode()){Hide();return;}
            string current=data.GetString(WorkbenchEnchantmentRuntime.StationKey,"");
            if(!ReferenceEquals(identity,data)||uid!=data.m_uid||marker!=current)
            {identity=data;uid=data.m_uid;marker=current;home=Guid.TryParseExact(marker,"N",out _)?uid+"/"+marker:null;Hide();}
            var zone=MasterIdolEffectZones.Find(home);
            if(zone==null||!WorkbenchEnchantmentRules.Known(zone.Type)){Hide();return;}
            if(choice==zone.Type&&symbol!=null&&symbol.enabled)return;
            if(Time.unscaledTime<RetryAsset)return;RetryAsset=Time.unscaledTime+1f;
            var sprite=ApprovedUiAssetBank.Enchantment(zone.Type);if(sprite==null){Hide();return;}
            if(symbol==null&&!CreateSymbol())return;
            var material=GlowMaterial(zone.Type,sprite);if(material==null){Hide();return;}
            symbol.sprite=sprite;symbol.sharedMaterial=material;symbol.color=Color.white;
            float size=Math.Max(sprite.bounds.size.x,sprite.bounds.size.y);if(size<=0){Hide();return;}
            symbol.transform.localScale=Vector3.one*(.28f/size);symbol.enabled=true;choice=zone.Type;
        }
        bool CreateSymbol()
        {
            // Only the inspected native workbench model is supported; unknown models fail closed.
            if(transform.Find("New/high")==null||transform.Find("collider")==null)return false;
            var root=new GameObject("VM_ActiveEnchantmentSymbol");root.layer=gameObject.layer;root.transform.SetParent(transform,false);
            root.transform.localPosition=new Vector3(0f,1.01f,0f);root.transform.localRotation=Quaternion.Euler(-90f,0f,0f);
            symbol=root.AddComponent<SpriteRenderer>();symbol.shadowCastingMode=ShadowCastingMode.Off;symbol.receiveShadows=false;symbol.enabled=false;return true;
        }
        Material GlowMaterial(string type,Sprite sprite)
        {
            if(GlowMaterials.TryGetValue(type,out var cached)&&cached!=null)return cached;
            var native=transform.Find("New/high")?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if(native?.shader?.name!="Custom/Piece"||!native.HasProperty("_EmissionMap")||!native.HasProperty("_EmissionColor")||!native.HasProperty("_MainTex")||!native.HasProperty("_Cutoff"))return null;
            var copy=new Material(native){name="VM_WorkbenchEnchantmentGlow_"+type};
            copy.SetTexture("_MainTex",sprite.texture);copy.SetTexture("_EmissionMap",sprite.texture);
            copy.SetTextureScale("_MainTex",Vector2.one);copy.SetTextureOffset("_MainTex",Vector2.zero);
            copy.SetTextureScale("_EmissionMap",Vector2.one);copy.SetTextureOffset("_EmissionMap",Vector2.zero);
            copy.SetColor("_EmissionColor",Palette(type)*2.4f);copy.SetFloat("_Cutoff",.02f);
            if(copy.HasProperty("_Color"))copy.SetColor("_Color",Color.white);
            if(copy.HasProperty("_Cull"))copy.SetFloat("_Cull",0);
            copy.EnableKeyword("_EMISSION");GlowMaterials[type]=copy;return copy;
        }
        static Color Palette(string id)
        {
            switch(id){case "Meadows":return new Color(1,.82f,.18f);case "BlackForest":return new Color(.18f,.8f,.38f);case "Swamp":return new Color(.64f,1,.12f);case "Mountain":return new Color(.35f,.8f,1);case "Plains":return new Color(1,.25f,.025f);default:return new Color(.72f,.35f,1);}
        }
        internal static void Clear(){foreach(var item in Loaded){if(item==null)continue;item.Hide();if(item.symbol!=null)UnityEngine.Object.Destroy(item.symbol.gameObject);item.symbol=null;}foreach(var material in GlowMaterials.Values)if(material!=null)UnityEngine.Object.Destroy(material);GlowMaterials.Clear();Next=0;}
    }
    [HarmonyPatch(typeof(CraftingStation),nameof(CraftingStation.Start))]
    internal static class WorkbenchEnchantmentVisualAttachPatch
    {static void Postfix(CraftingStation __instance)=>WorkbenchEnchantmentBenchVisual.Attach(__instance);}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class WorkbenchEnchantmentVisualTickPatch
    {static void Postfix()=>WorkbenchEnchantmentBenchVisual.Tick();}
    [HarmonyPatch(typeof(ZNetScene),"OnDestroy")]
    internal static class WorkbenchEnchantmentVisualCleanupPatch
    {static void Postfix()=>WorkbenchEnchantmentBenchVisual.Clear();}
}
