using HarmonyLib;
using TMPro;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolNpcLabel:MonoBehaviour
    {
        private TextMeshProUGUI Label;private Character Target;private string Text;private float Next;
        private readonly Vector3[] Corners=new Vector3[4];
        internal bool Due(Character target){if(!ReferenceEquals(Target,target))return true;if(Time.unscaledTime<Next)return false;Next=Time.unscaledTime+.5f;return true;}
        internal void Set(Character target,string value,TextMeshProUGUI name,RectTransform health)
        {
            if(!ReferenceEquals(Target,target)){Target=target;Text=null;Next=Time.unscaledTime+.5f;if(Label!=null)Label.gameObject.SetActive(false);}
            if(string.IsNullOrEmpty(value)){if(Label!=null)Label.gameObject.SetActive(false);Text=null;return;}
            if(name==null||health==null){if(Label!=null)Label.gameObject.SetActive(false);return;}
            if(Label==null){Label=Instantiate(name,name.transform.parent,false);Label.name="VM_IdolInfluence";Label.raycastTarget=false;Label.enableAutoSizing=false;Label.enableWordWrapping=false;Label.overflowMode=TextOverflowModes.Ellipsis;Label.alignment=TextAlignmentOptions.Center;Label.color=new Color(1f,.82f,.36f);}
            if(Text!=value){Label.text=value;Text=value;}
            Label.fontSize=name.fontSize*.65f;
            var rect=Label.rectTransform;var parent=(RectTransform)name.transform.parent;
            float height=Mathf.Max(name.fontSize,Label.fontSize*1.4f);
            float minimumWidth=Mathf.Max(name.rectTransform.rect.width,140f);
            rect.anchorMin=rect.anchorMax=parent.pivot;rect.pivot=new Vector2(.5f,.5f);
            rect.sizeDelta=new Vector2(Mathf.Clamp(Label.preferredWidth+8f,minimumWidth,Mathf.Max(minimumWidth,380f)),height);
            // Reserve the native HP row plus the combat stagger bar below it.
            // All bounds share this label's parent coordinates; no native transform writes.
            rect.localPosition=new Vector3(name.rectTransform.localPosition.x,
                LabelY(Bottom(name.rectTransform,parent),Bottom(health,parent),height),0f);
            if(!Label.gameObject.activeSelf)Label.gameObject.SetActive(true);
        }
        internal static float LabelY(float nameBottom,float healthBottom,float height)=>Mathf.Min(nameBottom,healthBottom)-12f-height*.5f;
        private float Bottom(RectTransform rect,Transform parent){rect.GetWorldCorners(Corners);float bottom=float.PositiveInfinity;for(int i=0;i<4;i++)bottom=Mathf.Min(bottom,parent.InverseTransformPoint(Corners[i]).y);return bottom;}
        private void OnDisable(){if(Label!=null)Label.gameObject.SetActive(false);Target=null;Text=null;}
        private void OnDestroy(){if(Label!=null)Destroy(Label.gameObject);}
    }
    internal static class MasterIdolNpcHud
    {
        private static string Influence(Character target)
        {
            if(target==null||target.IsPlayer()||target.IsDead()||target.m_nview?.IsValid()!=true||!MasterIdolEffectZones.Ready)return null;
            string type=null;var leash=MasterIdolLeash.For(target);
            if(leash?.Resident==true&&leash.SocialHome?.Contains(target.transform.position,15)==true)type="BlackForest";
            else if(target.IsTamed()&&!MasterIdolResidents.Family(target)&&MasterIdolEffectZones.At("Meadows",target.transform.position)!=null)type="Meadows";
            if(type==null)return null;var profile=MasterIdolProfiles.Find(type);return GoldUiLocalization.Text("Blessing · ","Благословення · ")+GoldUiLocalization.Text(profile.English,profile.Ukrainian);
        }
        internal static void Draw(EnemyHud hud)
        {
            if(Application.isBatchMode||hud==null)return;using var scope=new MasterIdolPerf.Scope("npc.hud");
            foreach(var pair in hud.m_huds){var data=pair.Value;if(data?.m_gui==null)continue;var label=data.m_gui.GetComponent<MasterIdolNpcLabel>();if(label!=null&&!label.Due(pair.Key))continue;string text=MasteryPlugin.Settings.Enabled.Value?Influence(pair.Key):null;if(label==null&&text!=null)label=data.m_gui.AddComponent<MasterIdolNpcLabel>();label?.Set(pair.Key,text,data.m_name,data.m_healthFast?.GetComponent<RectTransform>());}
        }
    }
    [HarmonyPatch(typeof(EnemyHud),nameof(EnemyHud.UpdateHuds))]
    internal static class MasterIdolNpcHudPatch{private static void Postfix(EnemyHud __instance)=>MasterIdolNpcHud.Draw(__instance);}
}
