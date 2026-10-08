using System;using HarmonyLib;using UnityEngine;
namespace ValheimMastery{
internal sealed class MasterIdolResidentSpellScope:MonoBehaviour{
 private Aoe Spell;private Character Caster;private ZDO Data;private long Owner;private ushort OwnerRevision;private ZNet Session;private ZDOMan Manager;private ulong Generation;private string HomeId;private bool Bound;
 internal MasterIdolEffectZone Home=>MasterIdolEffectZones.Find(HomeId);
 internal void Bind(Aoe spell){Spell=spell;Caster=spell.m_owner;Bound=false;if(!MasterIdolResidentPolicy.Shaman(Caster,out var home)||Caster.m_nview?.IsOwner()!=true||spell.m_nview?.IsOwner()!=true)return;Data=Caster.m_nview.GetZDO();Owner=Data.GetOwner();OwnerRevision=Data.OwnerRevision;Session=ZNet.instance;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;HomeId=home.Id;Bound=true;}
 internal bool Valid(){return Bound&&Session==ZNet.instance&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&Spell!=null&&Spell.m_nview?.IsOwner()==true&&Spell.m_owner==Caster&&Caster?.m_nview?.IsValid()==true&&ReferenceEquals(Caster.m_nview.GetZDO(),Data)&&Data.GetOwner()==Owner&&Data.OwnerRevision==OwnerRevision&&Caster.m_nview.IsOwner()&&MasterIdolResidentPolicy.Shaman(Caster,out var home)&&home.Id==HomeId&&home.Contains(Caster.transform.position,15);}
 internal bool Allows(UnityEngine.Collider collider){var target=Projectile.FindHitObject(collider)?.GetComponent<Character>();return Valid()&&MasterIdolResidentPolicy.CurrentTarget(target)&&Home!=null&&Home.Contains(target.transform.position,15)&&!MasterIdolResidentPolicy.Friendly(Caster,target)&&BaseAI.IsEnemy(Caster,target);}
 private void Update(){Spell=Spell??GetComponent<Aoe>();if(Spell?.m_nview?.IsOwner()!=true)return;if(!Valid())Spell.m_nview.Destroy();}
}
internal static class MasterIdolResidentSpellPrefab{
 private static ZNetScene Scene;private static GameObject Template,Root;internal const string Name="VM_Resident_Shaman_Poison_Aoe";
 internal static GameObject Get(ZNetScene scene){if(scene==null)return null;if(Scene==scene&&Template!=null)return Template;Scene=scene;Template=null;var native=scene.GetPrefab("shaman_attack_aoe");if(native==null)return null;Root=new GameObject("VM_InactiveResidentSpell");Root.SetActive(false);Root.transform.SetParent(scene.transform,false);var copy=UnityEngine.Object.Instantiate(native,Root.transform,false);copy.name=Name;var spell=copy.GetComponent<Aoe>();var view=copy.GetComponent<ZNetView>();if(spell==null||view==null){UnityEngine.Object.Destroy(copy);return null;}spell.m_hitProps=spell.m_hitTerrain=false;spell.m_canRaiseSkill=false;copy.AddComponent<MasterIdolResidentSpellScope>();int hash=Name.GetStableHashCode();if(scene.m_namedPrefabs.ContainsKey(hash)){UnityEngine.Object.Destroy(copy);return null;}scene.m_namedPrefabs.Add(hash,copy);Template=copy;MasterIdolResidentMetrics.Count("spell.prefab-ready");return copy;}
}
[HarmonyPatch(typeof(ZNetScene),"Awake")]internal static class MasterIdolResidentSpellRegisterPatch{private static void Postfix(ZNetScene __instance)=>MasterIdolResidentSpellPrefab.Get(__instance);}
[HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]internal static class MasterIdolResidentSpellAttackPatch{
 private static void Prefix(Humanoid __instance)=>__instance.GetComponent<MasterIdolResidentPresentation>()?.Stop();
 private static void Postfix(Humanoid __instance,bool __result){if(!__result||__instance.m_nview?.IsOwner()!=true||!MasterIdolResidentPolicy.Shaman(__instance,out _))return;var attack=__instance.m_currentAttack;if(attack?.m_attackProjectile==null||Utils.GetPrefabName(attack.m_attackProjectile)!="shaman_attack_aoe")return;attack.m_attackProjectile=MasterIdolResidentSpellPrefab.Get(ZNetScene.instance);MasterIdolResidentMetrics.Count("spell.scoped-attack");}
}
[HarmonyPatch(typeof(Aoe),nameof(Aoe.Setup))]internal static class MasterIdolResidentSpellSetupPatch{private static void Postfix(Aoe __instance)=>__instance.GetComponent<MasterIdolResidentSpellScope>()?.Bind(__instance);}
}

namespace ValheimMastery{
[HarmonyPatch(typeof(Aoe),"OnHit")]internal static class MasterIdolResidentSpellActualHitPatch{
 private static bool Prefix(Aoe __instance,UnityEngine.Collider __0,ref bool __result,out bool __state){__state=__instance.m_useTriggers;var scope=__instance.GetComponent<MasterIdolResidentSpellScope>();if(scope!=null){if(scope.Allows(__0)){__instance.m_useTriggers=false;return true;}__result=false;MasterIdolResidentMetrics.Count("spell.hit-denied");return false;}if(!MasterIdolResidentPolicy.TryHome(__instance.m_owner,out _))return true;__result=false;return false;}
 private static System.Exception Finalizer(Aoe __instance,bool __state,System.Exception __exception){__instance.m_useTriggers=__state;return __exception;}
}
}
