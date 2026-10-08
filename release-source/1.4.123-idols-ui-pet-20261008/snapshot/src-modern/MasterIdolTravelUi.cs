using System;using System.Collections.Generic;using HarmonyLib;using UnityEngine;
namespace ValheimMastery
{
 internal static class MasterIdolTravelUi
 {
  internal static int CancelFrame=-1;private static long Revision;private static YesNoPopup Owned;private static bool Closed;private static int Index,Page,Total;
  private static List<MasterIdolTravelDestination> Choices;private static Action<MasterIdolTravelDestination> Choose;private static Action NextPage;
  internal static void Show(List<MasterIdolTravelDestination> choices,int page,int total,Action<MasterIdolTravelDestination> choose,Action nextPage){Close();Choices=choices;Page=page;Total=total;Index=0;Choose=choose;NextPage=nextPage;Closed=false;Display();}
  private static void Display(){if(Closed||Choices==null||Choices.Count==0||UnifiedPopup.instance==null)return;var target=Choices[Index];var profile=MasterIdolProfiles.Find(target.Type);string name=GoldUiLocalization.Text(profile.English,profile.Ukrainian);float distance=Vector3.Distance(Player.m_localPlayer.transform.position,target.Point);string body=name+" · "+Mathf.RoundToInt(distance)+GoldUiLocalization.Text(" m"," м")+"\n"+(Page*32+Index+1)+" / "+Total+"\n"+GoldUiLocalization.Text("Choose this destination, browse onward, or press Esc to leave.","Обери цей шлях, переглянь наступний або натисни Esc, щоб відійти.");long revision=++Revision;Owned=new YesNoPopup(GoldUiLocalization.Text("The idols paths","Шляхи ідолів"),body,()=>{if(Closed||Revision!=revision)return;var action=Choose;Close();if(MasterIdolTravel.Valid())action?.Invoke(target);},()=>{if(Closed||Revision!=revision)return;PopOwned();if(!MasterIdolTravel.Valid()){Close();return;}Index++;if(Index<Choices.Count)Display();else{var next=NextPage;Close();next?.Invoke();}},true,true);UnifiedPopup.Push(Owned);}
  private static void PopOwned(){if(Owned==null||UnifiedPopup.instance==null){Owned=null;return;}var ui=UnifiedPopup.instance;if(ui.popupStack.Count>0&&ReferenceEquals(ui.popupStack.Peek(),Owned)){UnifiedPopup.Pop();Owned=null;return;}var displaced=new List<PopupBase>();while(ui.popupStack.Count>0){var entry=ui.popupStack.Pop();if(ReferenceEquals(entry,Owned)){break;}displaced.Add(entry);}for(int i=displaced.Count-1;i>=0;i--)ui.popupStack.Push(displaced[i]);Owned=null;}
  internal static void Tick(){if(Owned==null||UnifiedPopup.instance==null)return;if(UnifiedPopup.instance.popupStack.Count==0||!UnifiedPopup.instance.popupStack.Contains(Owned)){Closed=true;Owned=null;MasterIdolTravel.Cancel();return;}if(ReferenceEquals(UnifiedPopup.instance.popupStack.Peek(),Owned)&&(ZInput.GetKeyDown(KeyCode.Escape,true)||ZInput.GetButtonDown("JoyButtonB"))){CancelFrame=Time.frameCount;ZInput.ResetButtonStatus("JoyButtonB");MasterIdolTravel.Cancel();}}
  internal static void Close(){Closed=true;PopOwned();Choices=null;Choose=null;NextPage=null;}
  internal static void Labels(YesNoPopup popup){if(!ReferenceEquals(popup,Owned))return;UnifiedPopup.instance.buttonRightText.text=GoldUiLocalization.Text("Travel","Подорож");UnifiedPopup.instance.buttonLeftText.text=GoldUiLocalization.Text("Next","Наступний");}
 }
 [HarmonyPatch(typeof(UnifiedPopup),"ShowYesNo")]internal static class MasterIdolTravelUiLabelsPatch{private static void Postfix(YesNoPopup __0)=>MasterIdolTravelUi.Labels(__0);}
 [HarmonyPatch(typeof(MasteryPlugin),"Update")]internal static class MasterIdolTravelUiCancelPatch{private static void Postfix()=>MasterIdolTravelUi.Tick();}
 [HarmonyPatch(typeof(Menu),"Update")]internal static class MasterIdolTravelMenuCancelFramePatch{private static bool Prefix()=>Time.frameCount!=MasterIdolTravelUi.CancelFrame;}
}



