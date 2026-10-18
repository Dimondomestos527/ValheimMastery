using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
namespace ValheimMastery
{
    // Compatibility name only: this is a six-rune modal, never a map or travel authority.
    internal static class WorkbenchEnchantmentTravelMap
    {
        internal const int MaxDestinations=6,PageSize=6;
        internal static int CancelFrame=-1;
        internal static bool Active { get; private set; }
        private static readonly string[] Types={"Meadows","BlackForest","Swamp","Mountain","Plains","Mistlands"};
        private static GameObject Root,PriorSelection;
        private static RectTransform Panel,CanvasRect;
        private static Canvas Canvas;
        private static ZNet Session;
        private static ZDOMan Manager;
        private static Player Actor;
        private static ZDO ActorData;
        private static long World;
        private static ulong Generation;
        private static Action<ZDOID> Choose;
        private static Action CancelAction;
        private static int Revision;
        private static bool Closing;
        private static bool ReadingNativeMenu;
        private static string Text(string ua,string en)=>GoldUiLocalization.Text(en,ua);
        internal static int PageCount(int count)=>1;
        internal static bool Finite(Vector3 point)=>float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z);
        internal static bool Known(string type)=>Array.IndexOf(Types,type)>=0;
        internal static WorkbenchTravelDestination[] Freeze(IReadOnlyList<WorkbenchTravelDestination> list)
        {
            if(list==null||list.Count==0||list.Count>MaxDestinations)throw new ArgumentException("Invalid travel list");
            var copy=new WorkbenchTravelDestination[list.Count];var ids=new HashSet<ZDOID>();var types=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<list.Count;i++)
            {
                var item=list[i];
                if(item==null||item.Id==ZDOID.None||!Finite(item.Position)||!Known(item.EnchantmentId)||!ids.Add(item.Id)||!types.Add(item.EnchantmentId))
                    throw new ArgumentException("Ambiguous travel destination");
                copy[i]=item;
            }
            return copy;
        }
        private static bool LocalContext()=>Active&&Session!=null&&Session==ZNet.instance&&Manager==ZDOMan.instance&&
            Session.GetWorld()!=null&&Session.GetWorldUID()==World&&Generation==MasterIdolLoadIdentity.Generation&&
            Actor!=null&&Actor==Player.m_localPlayer&&!Actor.IsDead()&&!Actor.IsTeleporting()&&
            Actor.m_nview?.IsValid()==true&&ReferenceEquals(Actor.m_nview.GetZDO(),ActorData)&&
            ReferenceEquals(Manager?.GetZDO(ActorData.m_uid),ActorData)&&MasteryPlugin.Settings.Enabled.Value&&
            Canvas!=null&&Canvas.isActiveAndEnabled&&Root!=null&&Root.activeInHierarchy;
        internal static bool OwnsInput=>!ReadingNativeMenu&&LocalContext()&&!(UnifiedPopup.instance?.popupStack.Count>0);
        private static bool Context()=>LocalContext()&&WorkbenchEnchantmentTravel.Valid();
        internal static void Show(IReadOnlyList<WorkbenchTravelDestination> list,Action<ZDOID> choose,Action cancel)
        {
            Close();
            if(choose==null||cancel==null||Application.isBatchMode||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||
                Player.m_localPlayer==null||Hud.instance==null||ZNet.instance?.GetWorld()==null||EventSystem.current==null||
                InventoryGui.IsVisible()||Minimap.IsOpen()||Menu.IsVisible()||TextInput.IsVisible()||UnifiedPopup.IsVisible())
            {cancel?.Invoke();return;}
            try
            {
                var items=Freeze(list);var gui=InventoryGui.instance;
                Canvas=Hud.instance.GetComponentInParent<Canvas>();CanvasRect=Canvas?.GetComponent<RectTransform>();
                if(CanvasRect==null||!Canvas.isActiveAndEnabled||gui?.m_tabUpgrade==null||gui.m_recipeName==null)throw new InvalidOperationException("Native travel UI unavailable");
                var sprites=new Sprite[Types.Length];for(int i=0;i<sprites.Length;i++){sprites[i]=ApprovedUiAssetBank.Enchantment(Types[i]);if(sprites[i]==null)throw new InvalidOperationException("Approved travel rune unavailable");}
                Session=ZNet.instance;Manager=ZDOMan.instance;Actor=Player.m_localPlayer;ActorData=Actor.m_nview.GetZDO();
                World=Session.GetWorldUID();Generation=MasterIdolLoadIdentity.Generation;Choose=choose;CancelAction=cancel;
                PriorSelection=EventSystem.current.currentSelectedGameObject;int revision=++Revision;
                Root=new GameObject("VM_WorkbenchTravelMenu",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));Root.SetActive(false);
                var root=(RectTransform)Root.transform;root.SetParent(Canvas.transform,false);root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
                Root.GetComponent<Image>().color=new Color(0,0,0,.66f);
                var panel=new GameObject("Runes",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));Panel=(RectTransform)panel.transform;Panel.SetParent(root,false);
                Panel.anchorMin=Panel.anchorMax=new Vector2(.5f,.5f);Panel.pivot=new Vector2(.5f,.5f);Panel.anchoredPosition=Vector2.zero;Panel.sizeDelta=new Vector2(560,398);
                var donorImage=gui.m_tabUpgrade.GetComponent<Image>();var image=panel.GetComponent<Image>();image.sprite=donorImage?.sprite;image.type=Image.Type.Sliced;image.color=new Color(.15f,.13f,.11f,.98f);
                Label(gui.m_recipeName,Panel,Text("Давні шляхи","Ancient paths"),new Vector2(0,170),new Vector2(510,38),24);
                Button initial=null;var focusables=new List<Button>(7);
                for(int slot=0;slot<Types.Length;slot++)
                {
                    int at=slot;WorkbenchTravelDestination destination=null;
                    foreach(var item in items)if(item.EnchantmentId==Types[slot]){destination=item;break;}
                    var button=Button(gui.m_tabUpgrade,Panel,MasterIdolPlayerBuffs.DisplayName(Types[slot]),new Vector2((slot%3-1)*174,slot<3?69:-61),new Vector2(162,122));
                    button.interactable=destination!=null;
                    var text=button.GetComponentInChildren<TMP_Text>(true);text.rectTransform.anchorMin=text.rectTransform.anchorMax=new Vector2(.5f,0);text.rectTransform.anchoredPosition=new Vector2(0,23);text.rectTransform.sizeDelta=new Vector2(152,40);text.fontSizeMax=15;
                    var symbol=new GameObject("Rune",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));var rect=(RectTransform)symbol.transform;rect.SetParent(button.transform,false);
                    rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-7);rect.sizeDelta=new Vector2(66,66);
                    var icon=symbol.GetComponent<Image>();icon.sprite=sprites[at];icon.preserveAspect=true;icon.raycastTarget=false;icon.color=new Color(1,1,1,destination==null?.32f:1);
                    var target=destination;
                    button.onClick.AddListener(()=>{if(Active&&Revision==revision&&Context()&&target!=null){var action=Choose;CancelFrame=Time.frameCount;Close();action?.Invoke(target.Id);}});
                    if(initial==null&&destination!=null)initial=button;
                    if(destination!=null)focusables.Add(button);
                }
                var leave=Button(gui.m_tabUpgrade,Panel,Text("Скасувати","Cancel"),new Vector2(0,-166),new Vector2(188,34));
                leave.onClick.AddListener(()=>{if(Active&&Revision==revision)Cancel();});
                focusables.Add(leave);NavigationLinks(focusables);
                Root.AddComponent<WorkbenchTravelMenuLifetime>().Bind(Root,revision);Active=true;Resize();Root.SetActive(true);Root.transform.SetAsLastSibling();
                if(!Context())throw new InvalidOperationException("Travel context changed");
                EventSystem.current.SetSelectedGameObject((initial??leave).gameObject);
            }
            catch{if(Active)Cancel();else{Close();cancel();}}
        }
        private static TMP_Text Label(TMP_Text donor,Transform parent,string value,Vector2 position,Vector2 size,float max)
        {
            var label=UnityEngine.Object.Instantiate(donor,parent,false);label.gameObject.SetActive(true);label.enabled=true;label.text=value;label.raycastTarget=false;
            var r=label.rectTransform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;
            label.alignment=TextAlignmentOptions.Center;label.enableAutoSizing=true;label.fontSizeMin=10;label.fontSizeMax=max;return label;
        }
        private static Button Button(Button donor,Transform parent,string value,Vector2 position,Vector2 size)
        {
            var button=UnityEngine.Object.Instantiate(donor,parent,false);button.gameObject.SetActive(false);button.onClick=new Button.ButtonClickedEvent();
            foreach(var component in button.GetComponentsInChildren<Component>(true))
                if(component is Behaviour b&&!(component is Button)&&!(component is Graphic)&&!(component is CanvasGroup))
                {b.enabled=false;UnityEngine.Object.Destroy(component);}
            button.enabled=true;button.interactable=true;button.navigation=new Navigation{mode=Navigation.Mode.Automatic};
            var rect=button.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;
            var label=button.GetComponentInChildren<TMP_Text>(true);if(label==null)throw new InvalidOperationException("Native button label unavailable");
            label.gameObject.SetActive(true);label.enabled=true;label.text=value;label.alignment=TextAlignmentOptions.Center;label.enableAutoSizing=true;label.fontSizeMin=10;label.fontSizeMax=17;
            button.gameObject.SetActive(true);return button;
        }
        private static void NavigationLinks(List<Button> buttons)
        {
            for(int i=0;i<buttons.Count;i++)
            {
                var current=buttons[i];var previous=buttons[(i+buttons.Count-1)%buttons.Count];var next=buttons[(i+1)%buttons.Count];
                current.navigation=new Navigation{mode=Navigation.Mode.Explicit,
                    selectOnLeft=previous,selectOnRight=next,
                    selectOnUp=Vertical(current,buttons,1)??previous,selectOnDown=Vertical(current,buttons,-1)??next};
            }
        }
        private static Button Vertical(Button source,List<Button> buttons,int direction)
        {
            var origin=source.GetComponent<RectTransform>().anchoredPosition;Button nearest=null;float best=float.PositiveInfinity;
            foreach(var button in buttons)
            {
                var point=button.GetComponent<RectTransform>().anchoredPosition;float forward=(point.y-origin.y)*direction;
                if(forward<=0)continue;float score=forward+Mathf.Abs(point.x-origin.x)*2;
                if(score<best){best=score;nearest=button;}
            }
            return nearest;
        }
        private static void Resize()
        {if(Panel!=null&&CanvasRect!=null){float scale=Mathf.Min(1,Mathf.Min(CanvasRect.rect.width/584f,CanvasRect.rect.height/422f));if(Panel.localScale.x!=scale)Panel.localScale=Vector3.one*scale;}}
        internal static void Cancel()
        {if(!Active)return;var action=CancelAction;CancelFrame=Time.frameCount;Close();action?.Invoke();}
        internal static void CancelOwned(GameObject root,int revision)
        {if(Active&&Revision==revision&&ReferenceEquals(Root,root))Cancel();}
        internal static void Close()
        {
            if(Closing)return;Closing=true;Active=false;Revision++;Choose=null;CancelAction=null;
            var root=Root;Root=null;var system=EventSystem.current;var selected=system?.currentSelectedGameObject;
            bool owned=root!=null&&selected!=null&&selected.transform.IsChildOf(root.transform);
            try
            {
                if(root!=null){root.SetActive(false);UnityEngine.Object.Destroy(root);}
                if(owned&&system!=null)system.SetSelectedGameObject(PriorSelection!=null&&PriorSelection.activeInHierarchy?PriorSelection:null);
            }
            finally{PriorSelection=null;Panel=null;CanvasRect=null;Canvas=null;Actor=null;ActorData=null;Session=null;Manager=null;World=0;Closing=false;}
        }
        private static bool ForeignMenuVisible()
        {
            // Menu.IsVisible includes UnifiedPopup.IsVisible. Do not mistake our cursor lease for a foreign menu.
            bool previous=ReadingNativeMenu;ReadingNativeMenu=true;
            try{return Menu.IsVisible();}
            finally{ReadingNativeMenu=previous;}
        }
        internal static void Tick()
        {
            if(!Active)return;
            if(!Context()||InventoryGui.IsVisible()||Minimap.IsOpen()||ForeignMenuVisible()||TextInput.IsVisible()||
                UnifiedPopup.instance?.popupStack.Count>0||Chat.instance?.HasFocus()==true){Cancel();return;}
            Resize();
            if(ZInput.GetKeyDown(KeyCode.Escape,true)||ZInput.GetButtonDown("JoyButtonB"))
            {ZInput.ResetButtonStatus("JoyButtonB");Cancel();}
        }
    }
    internal sealed class WorkbenchTravelMenuLifetime:MonoBehaviour
    {
        private GameObject Root;private int Revision;
        internal void Bind(GameObject root,int revision){Root=root;Revision=revision;}
        private void OnDisable()=>WorkbenchEnchantmentTravelMap.CancelOwned(Root,Revision);
        private void OnDestroy()=>WorkbenchEnchantmentTravelMap.CancelOwned(Root,Revision);
    }
    [HarmonyPatch(typeof(Player),nameof(Player.TakeInput))]
    internal static class WorkbenchTravelMenuInputPatch
    {private static void Postfix(Player __instance,ref bool __result){if(__instance==Player.m_localPlayer&&(WorkbenchEnchantmentTravelMap.OwnsInput||Time.frameCount==WorkbenchEnchantmentTravelMap.CancelFrame))__result=false;}}
    [HarmonyPatch(typeof(UnifiedPopup),nameof(UnifiedPopup.IsVisible))]
    internal static class WorkbenchTravelMenuCursorPatch
    {private static void Postfix(ref bool __result){if(WorkbenchEnchantmentTravelMap.OwnsInput)__result=true;}}
    [HarmonyPatch(typeof(Menu),"Update")]
    internal static class WorkbenchTravelMapCancelFramePatch
    {private static bool Prefix()=>Time.frameCount!=WorkbenchEnchantmentTravelMap.CancelFrame;}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class WorkbenchTravelMapTickPatch
    {private static void Postfix()=>WorkbenchEnchantmentTravelMap.Tick();}
    [HarmonyPatch(typeof(MasteryPlugin),"OnDestroy")]
    internal static class WorkbenchTravelMapCleanupPatch
    {private static void Prefix()=>WorkbenchEnchantmentTravelMap.Close();}
}
