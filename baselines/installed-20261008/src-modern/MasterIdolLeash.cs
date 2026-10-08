using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolLeash:MonoBehaviour
    {
        private const string HomeKey="vm.idol.home.v1";
        private BaseAI Ai;private Character Creature;private ZNetView View;
        private string Home;private bool Forest,Override;private bool OriginalAttack,OriginalFire;
        private float Next,NextCalm;private Collider[] Threats=MasterIdolPilotA.Enabled?null:new Collider[64];private int ThreatEpoch=MasterIdolPilotA.Generation;
        private MasterIdolEffectZone Zone;
        private readonly Dictionary<long,bool> Permissions=new Dictionary<long,bool>();
        internal bool Owned=>View?.IsValid()==true&&View.IsOwner();
        internal MasterIdolEffectZone SocialHome { get { return Resident ? Zone : null; } }
        internal bool DiagnosticResident=>MasterIdolEffectZones.Ready&&Forest&&Zone!=null;
        internal bool Resident{get{if(!MasterIdolEffectZones.Ready)return false;Refresh();return Forest&&Zone!=null;}}
        internal static MasterIdolLeash For(Character character)=>character?.GetComponent<MasterIdolLeash>();
        private void Awake(){Ai=GetComponent<BaseAI>();Creature=GetComponent<Character>();View=GetComponent<ZNetView>();}
        private void Update(){ResetThreatBuffer();Refresh();}
        private void ResetThreatBuffer(){int epoch=MasterIdolPilotA.Generation;if(epoch==ThreatEpoch)return;ThreatEpoch=epoch;Threats=MasterIdolPilotA.Enabled?null:new Collider[64];}
        private void OnDestroy(){Restore();}
        private void Restore()
        {if(!Override)return;if(Ai is MonsterAI monster)monster.m_attackPlayerObjects=OriginalAttack;if(Ai!=null)Ai.m_afraidOfFire=OriginalFire;Override=false;}
        private void Refresh()
        {
            if(Ai==null||Creature==null||View?.IsValid()!=true)return;
            MasterIdolNativeTaming.SyncOwner(Creature);
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+1f;Permissions.Clear();
            Forest=MasterIdolResidents.Family(Creature);
            string type=Forest?"BlackForest":Creature.IsTamed()?"Meadows":null;
            Home=View.GetZDO().GetString(HomeKey,"");Zone=Forest?MasterIdolEffectZones.ResidentHome(View.GetZDO().m_uid.ToString(),Utils.GetPrefabName(Creature.gameObject)):type==null?null:MasterIdolEffectZones.Find(Home);
            if(Zone?.Type!=type)Zone=null;
            if(Forest&&Zone!=null){if(Owned)MasterIdolNativeTaming.Admit(Creature,Zone);Home=View.GetZDO().GetString(HomeKey,"");if(!MasterIdolNativeTaming.Assigned(View.GetZDO())||!MasterIdolNativeTaming.SameHome(Home,Zone.Id))Zone=null;}
            if(Zone==null&&type!=null&&!Forest)Zone=MasterIdolEffectZones.At(type,transform.position);
            if(Zone!=null&&Owned&&!Forest&&Home!=Zone.Id){Home=Zone.Id;View.GetZDO().Set(HomeKey,Home);}
            if(!Owned||!Forest||!MasterIdolNativeTaming.Friend(Creature)){Restore();return;}
            if(!Override){OriginalAttack=(Ai as MonsterAI)?.m_attackPlayerObjects??false;OriginalFire=Ai.m_afraidOfFire;Override=true;}
            if(Ai is MonsterAI resident){resident.m_attackPlayerObjects=false;resident.m_targetStatic=null;if(resident.m_targetCreature!=null&&ProtectedTarget(resident.m_targetCreature))resident.m_targetCreature=null;}
            Ai.m_afraidOfFire=false;Calm();
        }
                internal void Calm()
        {
            ResetThreatBuffer();
            if(!Owned||!Forest||Zone==null||Ai is not MonsterAI monster||!monster.IsAlerted()||Time.unscaledTime<NextCalm)return;
            NextCalm=Time.unscaledTime+1f;
            if(Creature.IsDead()||Creature.InAttack()||Creature.IsStaggering()||Ai.m_timeSinceHurt<8||monster.m_targetStatic!=null)return;
            if(monster.m_targetCreature!=null&&!ProtectedTarget(monster.m_targetCreature))return;
            int mask=LayerMask.GetMask("character","character_net","character_ghost");if(mask==0)return;
            if(Threats==null)Threats=new Collider[64];
            int count=Physics.OverlapSphereNonAlloc(transform.position,80,Threats,mask,QueryTriggerInteraction.Ignore);if(count>=Threats.Length)return;
            for(int n=0;n<count;n++)
            {
                var other=Threats[n]?.GetComponentInParent<Character>();if(other==null||other==Creature||other.IsDead()||!Zone.Contains(other.transform.position,15)||ProtectedTarget(other))continue;
                if(BaseAI.IsEnemy(Creature,other)||BaseAI.IsEnemy(other,Creature))return;
            }
            monster.m_targetCreature=null;monster.SetTargetInfo(ZDOID.None);monster.SetAlerted(false);
            if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogInfo("[IdolTest] resident calm npc="+View.GetZDO().m_uid+" home="+Home+" hurtAge="+Ai.m_timeSinceHurt);
        }
        internal bool ProtectedTarget(Character target)=>MasterIdolResidentPolicy.Friendly(Creature,target);
        internal void ClearTargets(MonsterAI monster)
        {
            Refresh();if(!Owned)return;
            if(MasterIdolNativeTaming.Friend(Creature)){if(!Override){OriginalAttack=monster.m_attackPlayerObjects;OriginalFire=Ai.m_afraidOfFire;Override=true;}monster.m_attackPlayerObjects=false;monster.m_targetStatic=null;if(monster.m_targetCreature!=null&&ProtectedTarget(monster.m_targetCreature))monster.m_targetCreature=null;}
            if(!MasterIdolEffectZones.Ready)return;
            if(monster.m_targetCreature!=null&&(For(monster.m_targetCreature)?.ProtectedTarget(Creature)==true||Forest&&ProtectedTarget(monster.m_targetCreature)||Zone!=null&&!Zone.Contains(monster.m_targetCreature.transform.position,Forest?15f:0f)))monster.m_targetCreature=null;
            if(Zone==null)return;
            if(monster.m_targetStatic!=null&&(Forest||!Zone.Contains(monster.m_targetStatic.transform.position)))monster.m_targetStatic=null;
        }
        internal bool ReturnHome(float dt,out bool processed)
        {
            processed=false;
            if(!MasterIdolEffectZones.Ready)return false;Refresh();if(!Owned||Zone==null||Zone.Contains(transform.position))return false;
            if(CombatExcursion)return false;
            if(Ai is MonsterAI monster){monster.m_targetCreature=null;monster.m_targetStatic=null;}
            processed=MasterIdolBaseUpdateBridge.Run(Ai,dt);
            if(processed)Ai.MoveTo(dt,Zone.Inside(transform.position),1f,true);return true;
        }
        private bool CombatExcursion=>Forest&&Zone!=null&&Ai is MonsterAI monster&&monster.m_targetCreature!=null&&!ProtectedTarget(monster.m_targetCreature)&&Zone.Contains(transform.position,15f)&&Zone.Contains(monster.m_targetCreature.transform.position,15f);
        internal Vector3 Destination(Vector3 point)
        {if(!MasterIdolEffectZones.Ready)return point;Refresh();float margin=CombatExcursion?15f:0f;return Owned&&Zone!=null&&!Zone.Contains(point,margin)?Zone.Inside(point,margin):point;}
    }
    [HarmonyPatch(typeof(BaseAI),"Awake")]
    internal static class MasterIdolAiAttachPatch
    {private static void Postfix(BaseAI __instance){if(__instance.GetComponent<MasterIdolLeash>()==null)__instance.gameObject.AddComponent<MasterIdolLeash>();}}
    [HarmonyPatch(typeof(MonsterAI),nameof(MonsterAI.UpdateAI))]
    internal static class MasterIdolAiLeashPatch
    {private static bool Prefix(MonsterAI __instance,float __0,ref bool __result){var leash=__instance.GetComponent<MasterIdolLeash>();leash?.ClearTargets(__instance);if(leash!=null&&leash.ReturnHome(__0,out bool processed)){__result=processed;return false;}return true;}}
    [HarmonyPatch(typeof(AnimalAI),nameof(AnimalAI.UpdateAI))]
    internal static class MasterIdolAnimalLeashPatch
    {private static bool Prefix(AnimalAI __instance,float __0,ref bool __result){var leash=__instance.GetComponent<MasterIdolLeash>();if(leash!=null&&leash.ReturnHome(__0,out bool processed)){__result=processed;return false;}return true;}}
    [HarmonyPatch(typeof(MonsterAI),"UpdateTarget")]
    internal static class MasterIdolTargetLeashPatch
    {private static void Postfix(MonsterAI __instance)=>__instance.GetComponent<MasterIdolLeash>()?.ClearTargets(__instance);}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.MoveTo))]
    internal static class MasterIdolDestinationPatch
    {private static void Prefix(BaseAI __instance,ref Vector3 __1){var leash=__instance.GetComponent<MasterIdolLeash>();if(leash!=null)__1=leash.Destination(__1);}}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.MoveAndAvoid))]
    internal static class MasterIdolAvoidDestinationPatch
    {private static void Prefix(BaseAI __instance,ref Vector3 __1){var leash=__instance.GetComponent<MasterIdolLeash>();if(leash!=null)__1=leash.Destination(__1);}}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.UpdateAI))]
    internal static class MasterIdolBaseUpdateBridge
    {
        [HarmonyReversePatch]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Run(BaseAI instance,float dt)=>throw new NotImplementedException("Native base AI reverse patch was not installed.");
    }
}





