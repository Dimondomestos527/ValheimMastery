using HarmonyLib;
namespace ValheimMastery
{
    internal static class MasterIdolResidents
    {
        internal static bool Family(Character creature)
        {string name=creature?.m_nview?.GetZDO()!=null?Utils.GetPrefabName(creature.gameObject):"";return name=="Greyling"||name=="Greydwarf"||name=="Greydwarf_Elite"||name=="Greydwarf_Shaman";}
        internal static bool Resident(BaseAI ai)=>ai?.GetComponent<MasterIdolLeash>()?.Resident==true;
    }
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.IsEnemy),new[]{typeof(Character),typeof(Character)})]
    internal static class MasterIdolResidentEnemyPatch
    {
        private static void Postfix(Character __0,Character __1,ref bool __result)
        {__result=MasterIdolResidentPolicy.Enemy(__0,__1,__result);}
    }
    [HarmonyPatch(typeof(MonsterAI),nameof(MonsterAI.HuntPlayer))]
    internal static class MasterIdolResidentHuntPatch
    {private static void Postfix(MonsterAI __instance,ref bool __result){if(MasterIdolNativeTaming.Friend(__instance.GetComponent<Character>()))__result=false;}}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.HaveFriendInRange),new[]{typeof(float)})]
    internal static class MasterIdolResidentHealingPatch
    {private static bool Prefix(BaseAI __instance,ref Character __result){if(!MasterIdolResidents.Resident(__instance)&&!MasterIdolNativeTaming.Friend(__instance.GetComponent<Character>()))return true;__result=null;return false;}}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.HaveHurtFriendInRange),new[]{typeof(float)})]
    internal static class MasterIdolResidentHurtHealingPatch
    {private static bool Prefix(BaseAI __instance,ref Character __result){if(!MasterIdolResidents.Resident(__instance)&&!MasterIdolNativeTaming.Friend(__instance.GetComponent<Character>()))return true;__result=null;return false;}}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.HaveFriendsInRange))]
    internal static class MasterIdolResidentCombinedHealingPatch
    {private static bool Prefix(BaseAI __instance,ref Character __1,ref Character __2){if(!MasterIdolResidents.Resident(__instance)&&!MasterIdolNativeTaming.Friend(__instance.GetComponent<Character>()))return true;__1=__2=null;return false;}}
    // Resident poison uses a marked native clone; resident healing does not enter the damage pipeline.
    [HarmonyPatch(typeof(Aoe),"ShouldHit")]
    internal static class MasterIdolResidentSpellPatch
    {
        private static bool Prefix(Aoe __instance,UnityEngine.Collider __0,ref bool __result,out bool __state)
        {
            __state=__instance.m_hitSame;var scope=__instance.GetComponent<MasterIdolResidentSpellScope>();
            if(scope!=null){var target=Projectile.FindHitObject(__0)?.GetComponent<Character>();if(!scope.Valid()||!MasterIdolResidentPolicy.CurrentTarget(target)||!scope.Home.Contains(target.transform.position,15)||MasterIdolResidentPolicy.Friendly(__instance.m_owner,target)||!BaseAI.IsEnemy(__instance.m_owner,target)){__result=false;return false;}__instance.m_hitSame=true;return true;}
            if(!MasterIdolNativeTaming.Friend(__instance.m_owner)&&!MasterIdolResidentPolicy.TryHome(__instance.m_owner,out _))return true;__result=false;return false;
        }
        private static System.Exception Finalizer(Aoe __instance,bool __state,System.Exception __exception){__instance.m_hitSame=__state;return __exception;}
    }
    [HarmonyPatch(typeof(Character),"RPC_Damage")]
    internal static class MasterIdolResidentFriendlyDamagePatch
    {private static bool Prefix(Character __instance,HitData __1)=>!MasterIdolResidentPolicy.Friendly(__1?.GetAttacker(),__instance);}
}
