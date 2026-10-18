using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class Ashlands70Madness
    {
        internal const string UntilKey = "vm.ash70.maduntil", WorldKey = "vm.ash70.madworld";
        private static readonly Dictionary<ZDOID, long> Wanted = new Dictionary<ZDOID, long>();
        private static readonly HashSet<ZDOID> WasActive = new HashSet<ZDOID>();
        private static readonly List<ZDOID> Expired = new List<ZDOID>();
        internal static IEnumerable<ZDOID> Targets => Wanted.Keys;
        internal static bool Active(Character body)
            => body != null && !body.IsDead() && SourceMarked(body);
        // Only outgoing death-ability hooks use this predicate; dead targets never acquire AI/status.
        internal static bool SourceMarked(Character body)
        {
            return Ashlands70Hostility.SourceMarked(body);
        }
        internal static bool HostilePair(Character first, Character second) => Ashlands70Hostility.HostilePair(first, second);
        internal static bool OffensivePair(Character source, Character target) => Ashlands70Hostility.OffensivePair(source, target);
        internal static bool SourceMarkedCurrentData(ZDO data) => Ashlands70Hostility.SourceMarkedCurrentData(data);
        internal static void Track(Character body)
        {
            if (Active(body)) Wanted[body.GetZDOID()] = body.m_nview.GetZDO().GetLong(UntilKey, 0);
        }
        internal static void Apply(Ashlands70Grant grant)
        {
            foreach (ZDOID id in grant.Targets)
                if (!Wanted.TryGetValue(id, out long until) || until < grant.Until) Wanted[id] = grant.Until;
            foreach (ZDOID id in grant.Targets) Magic70Diagnostics.Arm(id);
            Tick();
        }
        internal static void Tick()
        {
            long now = Ashlands70Cast.Now; Expired.Clear();
            foreach (var row in Wanted)
            {
                Character body = Ashlands70World.Resolve(row.Key);
                if (Ashlands70Rules.Active(now, row.Value) && body != null && !body.IsPlayer() && !body.IsDead() && body.m_nview?.IsValid() == true && body.m_nview.IsOwner())
                {
                    ZDO data = body.m_nview.GetZDO();
                    if (data.GetLong(WorldKey, 0) != Ashlands70Cast.CurrentWorld || data.GetLong(UntilKey, 0) < row.Value)
                    { data.Set(WorldKey, Ashlands70Cast.CurrentWorld); data.Set(UntilKey, row.Value); }
                }
                bool active = Active(body);
                if (body?.m_nview?.IsOwner() == true)
                {
                    if (active && WasActive.Add(row.Key)) Retarget(body, true);
                    else if (!active && WasActive.Remove(row.Key)) Retarget(body, false);
                }
                Magic70Diagnostics.Sample(row.Key, body);
                if (!Ashlands70Rules.Active(now, row.Value)) Expired.Add(row.Key);
            }
            foreach (ZDOID id in Expired) { Wanted.Remove(id); WasActive.Remove(id); }
        }
        internal static bool TamedForTargeting(Character actor) => !Active(actor) && actor.IsTamed();
        internal static bool CannotTargetOthers(Character actor) => !Active(actor) && actor.m_aiCannotTargetOthers;
        internal static bool HitSame(Aoe aoe) => aoe.m_hitSame || SourceMarked(aoe.m_owner);
        internal static bool OutgoingEnemy(Character source, Character target)
        { if (source == null || target == null || source == target || target.IsDead()) return false; return SourceMarked(source) || BaseAI.IsEnemy(source, target); }
        internal static bool ExcludePlayer(Character target, Magic70SurtlingLife life) => target.IsPlayer() && !Active(life.GetComponent<Character>());
        internal static bool ExcludePlayerAtDeath(Character target, Magic70SurtlingLife life) => target.IsPlayer() && !SourceMarked(life.GetComponent<Character>());
        private static void Retarget(Character body, bool entering)
        {
            MonsterAI monster = body.GetComponent<MonsterAI>();
            if (monster != null)
            {
                monster.m_targetCreature = null; monster.m_targetStatic = null; monster.m_updateTargetTimer = 0f;
                monster.m_timeSinceSensedTargetCreature = 0f; monster.m_timeSinceAttacking = 0f;
                monster.SetTargetInfo(ZDOID.None); monster.SetAlerted(entering);
                if (entering) monster.Wakeup();
            }
            AnimalAI animal = body.GetComponent<AnimalAI>();
            if (animal != null) { animal.m_target = null; animal.m_updateTargetTimer = 0f; if (entering) animal.m_inDangerTimer = 0f; animal.SetAlerted(entering); }
        }
        internal static bool ManagedSummon(Character body) => body?.m_nview?.IsValid() == true &&
            body.m_nview.GetZDO().GetLong(Magic70Surtling.LootAuthorKey, 0) != 0;
        internal static bool NativeSummon(Character body)
        {
            if (body?.m_nview?.IsValid() != true) return false;
            ZDO z = body.m_nview.GetZDO(); string staff = StaffMagicXpRules.SummonStaff(body.gameObject.name);
            int kind = staff == "StaffGreenRoots" ? StaffMagicXpRules.Roots :
                staff == "StaffRedTroll" ? StaffMagicXpRules.Troll : staff == "StaffSpiritCaller" ? StaffMagicXpRules.Spirit : 0;
            if (kind == 0 || z.GetLong("vm.staffxp.author", 0) == 0 || z.GetZDOID("vm.staffxp.caster") == ZDOID.None) return false;
            // Skeletons use the existing unconditional native-birth roster stamp,
            // checked by DamageMultiplier before this supplemental proof path.
            return StaffMagicXpContext.Proof(z, kind, z.GetLong("vm.staffxp.author", 0), z.GetZDOID("vm.staffxp.caster"), Ashlands70Cast.CurrentWorld);
        }
        internal static void Reset() { Wanted.Clear(); WasActive.Clear(); }
    }
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
    internal static class Ashlands70HostilityPatch
    {
        [HarmonyPriority(int.MinValue)] private static void Postfix(Character __0, Character __1, ref bool __result)
        { if (__0 != null && __1 != null && __0 != __1 && (Ashlands70Madness.Active(__0) || Ashlands70Madness.Active(__1))) __result = true; }
    }
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character) })]
    internal static class Ashlands70InstanceHostilityPatch
    {
        [HarmonyPriority(int.MinValue)] private static void Postfix(BaseAI __instance, Character __0, ref bool __result)
        { Character own = __instance.GetComponent<Character>(); if (own != null && __0 != null && own != __0 && (Ashlands70Madness.Active(own) || Ashlands70Madness.Active(__0))) __result = true; }
    }
    [HarmonyPatch]
    internal static class Ashlands70TameTargetingPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MonsterAI), "SetTarget", new[] { typeof(Character) });
            yield return AccessTools.Method(typeof(MonsterAI), "UpdateTarget", new[] { typeof(Humanoid), typeof(float), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() });
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo native = AccessTools.Method(typeof(Character), nameof(Character.IsTamed));
            MethodInfo scoped = AccessTools.Method(typeof(Ashlands70Madness), nameof(Ashlands70Madness.TamedForTargeting));
            FieldInfo gate = AccessTools.Field(typeof(Character), nameof(Character.m_aiCannotTargetOthers));
            MethodInfo scopedGate = AccessTools.Method(typeof(Ashlands70Madness), nameof(Ashlands70Madness.CannotTargetOthers));
            int matches = 0, gates = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = scoped; matches++; }
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, gate)) { instruction.opcode = OpCodes.Call; instruction.operand = scopedGate; gates++; }
                yield return instruction;
            }
            bool update = __originalMethod.Name == "UpdateTarget";
            if (matches != (update ? 2 : 1) || gates != (update ? 1 : 0)) throw new InvalidOperationException("Ashlands70 native tame targeting callsite changed");
        }
    }
    [HarmonyPatch(typeof(Aoe), "ShouldHit")]
    internal static class Ashlands70AoeSameSpeciesPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo native = AccessTools.Field(typeof(Aoe), nameof(Aoe.m_hitSame));
            MethodInfo scoped = AccessTools.Method(typeof(Ashlands70Madness), nameof(Ashlands70Madness.HitSame));
            MethodInfo enemy = AccessTools.Method(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) });
            MethodInfo outgoing = AccessTools.Method(typeof(Ashlands70Madness), nameof(Ashlands70Madness.OutgoingEnemy));
            int matches = 0, enemies = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, native))
                { instruction.opcode = OpCodes.Call; instruction.operand = scoped; matches++; }
                if (instruction.Calls(enemy)) { instruction.opcode = OpCodes.Call; instruction.operand = outgoing; enemies++; }
                yield return instruction;
            }
            if (matches != 1 || enemies != 1) throw new InvalidOperationException("Ashlands70 native same-species gate changed");
        }
    }
    [HarmonyPatch]
    internal static class Ashlands70SurtlingMadnessPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return AccessTools.Method(typeof(Magic70SurtlingLife), "Update"); yield return AccessTools.Method(typeof(Magic70SurtlingLife), "Explode"); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            bool death = __originalMethod.Name == "Explode";
            MethodInfo isPlayer = AccessTools.Method(typeof(Character), nameof(Character.IsPlayer));
            MethodInfo scoped = AccessTools.Method(typeof(Ashlands70Madness), death ? nameof(Ashlands70Madness.ExcludePlayerAtDeath) : nameof(Ashlands70Madness.ExcludePlayer));
            MethodInfo enemy = AccessTools.Method(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) });
            MethodInfo outgoing = AccessTools.Method(typeof(Ashlands70Madness), nameof(Ashlands70Madness.OutgoingEnemy));
            int players = 0, enemies = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(isPlayer))
                {
                    var source = new CodeInstruction(OpCodes.Ldarg_0);
                    source.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    source.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return source;
                    instruction.opcode = OpCodes.Call; instruction.operand = scoped; players++;
                }
                if (death && instruction.Calls(enemy)) { instruction.opcode = OpCodes.Call; instruction.operand = outgoing; enemies++; }
                yield return instruction;
            }
            if (players != 1 || enemies != (death ? 1 : 0)) throw new InvalidOperationException("Ashlands70 Jarik targeting gate changed");
        }
    }
}

