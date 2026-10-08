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
        {if(!__result)return;var first=MasterIdolLeash.For(__0);var second=MasterIdolLeash.For(__1);if(first?.ProtectedTarget(__1)==true||second?.ProtectedTarget(__0)==true)__result=false;}
    }
    [HarmonyPatch(typeof(MonsterAI),nameof(MonsterAI.HuntPlayer))]
    internal static class MasterIdolResidentHuntPatch
    {private static void Postfix(MonsterAI __instance,ref bool __result){if(MasterIdolResidents.Resident(__instance))__result=false;}}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.HaveFriendInRange),new[]{typeof(float)})]
    internal static class MasterIdolResidentHealingPatch
    {private static bool Prefix(BaseAI __instance,ref Character __result){if(!MasterIdolResidents.Resident(__instance))return true;__result=null;return false;}}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.HaveHurtFriendInRange),new[]{typeof(float)})]
    internal static class MasterIdolResidentHurtHealingPatch
    {private static bool Prefix(BaseAI __instance,ref Character __result){if(!MasterIdolResidents.Resident(__instance))return true;__result=null;return false;}}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.HaveFriendsInRange))]
    internal static class MasterIdolResidentCombinedHealingPatch
    {private static bool Prefix(BaseAI __instance,ref Character __1,ref Character __2){if(!MasterIdolResidents.Resident(__instance))return true;__1=__2=null;return false;}}
    // Phase1 has no resident spell/healing policy yet. Suppress even an already-started native AoE.
    [HarmonyPatch(typeof(Aoe),"ShouldHit")]
    internal static class MasterIdolResidentSpellPatch
    {private static bool Prefix(Aoe __instance,ref bool __result){if(MasterIdolLeash.For(__instance.m_owner)?.Resident!=true)return true;__result=false;return false;}}
    [HarmonyPatch(typeof(Character),"RPC_Damage")]
    internal static class MasterIdolResidentFriendlyDamagePatch
    {private static bool Prefix(Character __instance,HitData __1)=>MasterIdolLeash.For(__1?.GetAttacker())?.ProtectedTarget(__instance)!=true;}
}
