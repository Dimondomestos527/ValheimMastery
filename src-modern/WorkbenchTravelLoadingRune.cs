using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
namespace ValheimMastery
{
    // Presentation lease only. Utility owns the grant, native teleport and transit completion.
    internal static class WorkbenchTravelLoadingRune
    {
        private static Player Actor;
        private static ZDO Data;
        private static ZNet Session;
        private static ZDOMan Manager;
        private static long World;
        private static ulong Generation;
        private static Sprite Sprite;
        private static float Started;
        private static int Revision;
        private static bool Leased;
        private static WorkbenchTravelLoadingRuneView View;
        internal static bool Active=>Leased;
        internal static void Begin(Player player,string destinationType)
        {
            End();
            if(Application.isBatchMode||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||player==null||
                player!=Player.m_localPlayer||player.IsDead()||!player.IsTeleporting()||!player.ShowTeleportAnimation()||
                !WorkbenchEnchantmentTravelMap.Known(destinationType)||ZNet.instance?.GetWorld()==null||player.m_nview?.IsValid()!=true)return;
            var sprite=ApprovedUiAssetBank.Enchantment(destinationType);if(sprite==null)return; // Preserve native swirl on unready/missing approved rune.
            var data=player.m_nview.GetZDO();if(data==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data))return;
            Actor=player;Data=data;Session=ZNet.instance;Manager=ZDOMan.instance;World=Session.GetWorldUID();
            Generation=MasterIdolLoadIdentity.Generation;Sprite=sprite;Started=Time.unscaledTime;Revision++;Leased=true;
        }
        internal static bool Current(Player player)=>Actor!=null&&Actor==player&&Actor==Player.m_localPlayer&&Session==ZNet.instance&&
            Manager==ZDOMan.instance&&Session?.GetWorld()!=null&&World==Session.GetWorldUID()&&Generation==MasterIdolLoadIdentity.Generation&&
            Actor.m_nview?.IsValid()==true&&ReferenceEquals(Actor.m_nview.GetZDO(),Data)&&ReferenceEquals(Manager?.GetZDO(Data.m_uid),Data)&&
            !Actor.IsDead()&&Actor.IsTeleporting()&&Actor.ShowTeleportAnimation()&&MasteryPlugin.Settings.Enabled.Value&&Sprite!=null;
        internal static void Update(Hud hud,Player player)
        {
            if(!Active)return;
            if(!Current(player)){End();return;}
            if(hud==null||hud!=Hud.instance||hud.m_teleportingProgress==null||!hud.m_teleportingProgress.activeInHierarchy)return;
            try
            {
                if(View==null||View.gameObject!=hud.gameObject)
                {
                    if(View!=null)View.Release();
                    View=hud.GetComponent<WorkbenchTravelLoadingRuneView>()??hud.gameObject.AddComponent<WorkbenchTravelLoadingRuneView>();
                }
                if(!View.Draw(hud,Sprite,Started,Revision))End();
            }
            catch{End();} // Visual failure never changes native teleport state.
        }
        internal static void End()
        {
            Leased=false;Actor=null;Data=null;Session=null;Manager=null;World=0;Sprite=null;Revision++;
            var view=View;View=null;try{if(view!=null)view.Release();}catch{} // Teardown remains presentation-only.
        }
        internal static void ViewLost(WorkbenchTravelLoadingRuneView view)
        {if(ReferenceEquals(View,view))End();}
        internal static float Breath(float elapsed)=>.5f-.5f*Mathf.Cos(elapsed*(2*Mathf.PI/3.2f));
    }
    internal sealed class WorkbenchTravelLoadingRuneView:MonoBehaviour
    {
        private Hud Hud;
        private Transform TeleportRoot,Swirl;
        private Image Icon;
        private RectTransform Legacy;
        private bool WasActive,Owned;
        private int Revision;
        internal bool Draw(Hud hud,Sprite sprite,float started,int revision)
        {
            if(!Owned||Hud!=hud||TeleportRoot!=hud.m_teleportingProgress.transform||Revision!=revision)
            {
                Release();
                var root=hud.m_teleportingProgress.transform;var swirl=root.Find("Swirl");var legacy=root.Find("loading") as RectTransform;
                if(swirl==null||swirl.childCount!=7||legacy==null||legacy.GetComponent<Image>()==null||
                    legacy.sizeDelta!=new Vector2(64,64)||legacy.anchorMin!=new Vector2(1,0)||legacy.anchorMax!=new Vector2(1,0)||
                    legacy.anchoredPosition!=new Vector2(-50,50))return false;
                for(int i=1;i<=7;i++)if(swirl.Find("layer"+i)==null)return false;
                Hud=hud;TeleportRoot=root;Swirl=swirl;Legacy=legacy;Revision=revision;
                if(Icon==null)
                {
                    var owned=new GameObject("VM_WorkbenchDestinationRune",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));owned.SetActive(false);
                    Icon=owned.GetComponent<Image>();Icon.raycastTarget=false;Icon.preserveAspect=true;
                }
                var rect=Icon.rectTransform;rect.SetParent(root,false);rect.anchorMin=legacy.anchorMin;rect.anchorMax=legacy.anchorMax;
                rect.pivot=legacy.pivot;rect.sizeDelta=legacy.sizeDelta;rect.anchoredPosition3D=legacy.anchoredPosition3D;rect.localRotation=legacy.localRotation;
                Icon.sprite=sprite;WasActive=swirl.gameObject.activeSelf;Owned=true;swirl.gameObject.SetActive(false);Icon.gameObject.SetActive(true);
            }
            // Another mod visibly changed the suppressed node: relinquish, never fight its active state.
            if(Swirl==null||Swirl.gameObject.activeSelf||Icon==null||Icon.sprite!=sprite)return false;
            float wave=WorkbenchTravelLoadingRune.Breath(Time.unscaledTime-started);
            Icon.rectTransform.localScale=Legacy.localScale*Mathf.Lerp(.93f,1.07f,wave);
            Icon.color=new Color(1,1,1,Mathf.Lerp(.68f,1,wave));return true;
        }
        internal void Release()
        {
            bool current=Owned&&Hud!=null&&Hud==global::Hud.instance&&Hud.m_teleportingProgress!=null&&
                Hud.m_teleportingProgress.transform==TeleportRoot&&Swirl!=null&&Swirl.parent==TeleportRoot&&
                TeleportRoot.Find("Swirl")==Swirl;
            if(current&&!Swirl.gameObject.activeSelf)Swirl.gameObject.SetActive(WasActive);
            Owned=false;if(Icon!=null){Icon.gameObject.SetActive(false);Icon.sprite=null;}
            Hud=null;TeleportRoot=null;Swirl=null;Legacy=null;
        }
        private void OnDisable(){Release();WorkbenchTravelLoadingRune.ViewLost(this);}
        private void OnDestroy(){Release();WorkbenchTravelLoadingRune.ViewLost(this);if(Icon!=null)Destroy(Icon.gameObject);}
    }
    [HarmonyPatch(typeof(Hud),"UpdateBlackScreen")]
    internal static class WorkbenchTravelLoadingRunePatch
    {private static void Postfix(Hud __instance,Player __0)=>WorkbenchTravelLoadingRune.Update(__instance,__0);}
    [HarmonyPatch(typeof(MasteryPlugin),"OnDestroy")]
    internal static class WorkbenchTravelLoadingRuneCleanupPatch
    {private static void Prefix()=>WorkbenchTravelLoadingRune.End();}
}
