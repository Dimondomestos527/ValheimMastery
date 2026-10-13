using System;
namespace ValheimMastery {
// Native tamed bit is the permanent friendship state; active roster is work authority.
internal static class MasterIdolNativeTaming {
 internal const string HomeKey="vm.idol.home.v1";
 // Pure validation: preserve canonical UID and raw GUID-token comparison without temporary strings.
 private static bool TryTokenStart(string home, out int start)
 {
  start=0;if(home==null||home.Length>128)return false;
  int slash=home.IndexOf('/');if(slash<1||home.IndexOf('/',slash+1)>=0)return false;
  var uid=home.AsSpan(0,slash);int colon=uid.IndexOf(':');
  if(colon<0||uid.Slice(colon+1).IndexOf(':')>=0||
   !long.TryParse(uid.Slice(0,colon),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out long user)||
   !uint.TryParse(uid.Slice(colon+1),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out uint id)||(user==0&&id==0))return false;
  Span<char> canonical=stackalloc char[32];
  if(!user.TryFormat(canonical,out int written,provider:System.Globalization.CultureInfo.InvariantCulture))return false;
  canonical[written++]=':';
  if(!id.TryFormat(canonical.Slice(written),out int digits,provider:System.Globalization.CultureInfo.InvariantCulture)||!uid.SequenceEqual(canonical.Slice(0,written+digits)))return false;
  start=slash+1;return Guid.TryParseExact(home.AsSpan(start),"N",out _);
 }
 internal static string Token(string home)=>TryTokenStart(home,out int start)?home.Substring(start):null;
 internal static bool SameHome(string first,string second)=>TryTokenStart(first,out int a)&&TryTokenStart(second,out int b)&&first.AsSpan(a).SequenceEqual(second.AsSpan(b));
 internal static bool Assigned(ZDO data)=>data!=null&&MasterIdolResidentRoster.Limit(ZNetScene.instance?.GetPrefab(data.GetPrefab())?.name)>0&&TryTokenStart(data.GetString(HomeKey,""),out _)&&data.GetBool(ZDOVars.s_tamed,false);
 internal static bool Friend(Character actor){var view=actor?.m_nview;var data=view?.GetZDO();return actor!=null&&MasterIdolResidents.Family(actor)&&view?.IsValid()==true&&data!=null&&ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data)&&data.GetBool(ZDOVars.s_tamed,false);}
 internal static void SyncOwner(Character actor){MasterIdolResidentInteraction.Ensure(actor);var view=actor?.m_nview;if(view?.IsValid()!=true||!view.IsOwner()||!MasterIdolResidents.Family(actor))return;var data=view.GetZDO();if(!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data))return;bool native=data.GetBool(ZDOVars.s_tamed,false);if(native&&!actor.IsTamed()&&actor.GetComponent<MonsterAI>() is MonsterAI ai){ai.SetHuntPlayer(false);ai.MakeTame();}else if(!native&&actor.IsTamed())actor.m_tamed=false;}
 internal static bool Admit(Character actor,MasterIdolEffectZone home){var view=actor?.m_nview;var data=view?.GetZDO();if(!GoldCraftingService.Enabled||home?.Type!="BlackForest"||view?.IsValid()!=true||!view.IsOwner()||data==null||!data.Persistent||actor.IsDead()||!MasterIdolResidents.Family(actor)||!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data)||MasterIdolEffectZones.ResidentHome(data.m_uid.ToString(),Utils.GetPrefabName(actor.gameObject))!=home)return false;
 if(actor.IsTamed())return SameHome(data.GetString(HomeKey,""),home.Id);
 if(actor.GetComponent<MonsterAI>() is not MonsterAI ai||data.GetBool(ZDOVars.s_eventCreature,ai.m_eventCreature)||data.GetBool(ZDOVars.s_despawnInDay,ai.m_despawnInDay))return false;
 data.Set(HomeKey,home.Id);ai.SetHuntPlayer(false);ai.MakeTame();MasterIdolWorldIndex.Observe(data);MasterIdolResidentMetrics.Count("resident.native-tamed");MasterIdolResidentInteraction.Ensure(actor);return actor.IsTamed();}
}
}
