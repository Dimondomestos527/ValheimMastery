using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Execute the real perk source against bounded native boundary doubles, then
// inspect the real candidate/native IL. This is not a Unity/runtime test.
namespace ValheimMastery
{
    internal static class MiningChecks
    {
        static int passed;
        internal static void Check(bool ok, string text) { if (!ok) throw new Exception(text); passed++; Console.WriteLine("PASS " + text); }
        static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        internal static Pickaxes70HitState BeginArea(MineRock5 rock, int index, HitData hit)
        {
            object[] values = { rock, index, hit, null };
            Method(typeof(Pickaxes70SuperHitPatch), "Prefix").Invoke(null, values);
            return (Pickaxes70HitState)values[3];
        }
        internal static void EndArea(MineRock5 rock, HitData hit, Pickaxes70HitState state) => Method(typeof(Pickaxes70SuperHitPatch), "Postfix").Invoke(null, new object[] { rock, hit, state });
        static Pickaxes70DamageScope BeginRpc(MineRock5 rock, HitData hit)
        {
            object[] values = { rock, hit, null };
            Method(typeof(Pickaxes70DamageScopePatch), "Prefix").Invoke(null, values);
            return (Pickaxes70DamageScope)values[2];
        }
        static Exception EndRpc(Pickaxes70DamageScope state, Exception error = null) => (Exception)Method(typeof(Pickaxes70DamageScopePatch), "Finalizer").Invoke(null, new object[] { error, state });
        static HitData Hit(float damage = 10f) => new HitData { Attacker = new Player(), m_damage = new HitData.DamageTypes { m_pickaxe = damage }, m_skillRaiseAmount = 1f };
        static MineRock5 Rock(params float[] health) => new MineRock5 { m_hitAreas = health.Select((h, i) => new MineRock5.HitArea { m_health = h, m_collider = new UnityEngine.Collider { bounds = new UnityEngine.Bounds { Point = new UnityEngine.Vector3(i, 0, 0) } } }).ToList() };
        static void Reset(params bool[] rolls) { PerkRuntimeService.Rolls = new Queue<bool>(rolls); PerkRuntimeService.Chances.Clear(); PerkRuntimeService.ForceProc = false; PerkVisualService.Cues.Clear(); UnityEngine.Random.Extra = 1; }
        static void Rpc(MineRock5 rock, HitData hit)
        {
            var scope = BeginRpc(rock, hit);
            Exception failure = null;
            try { bool originalKilled = rock.DamageArea(0, hit); if (originalKilled && rock.m_supportCheck) rock.CheckSupport(); }
            catch (Exception error) { failure = error; }
            var result = EndRpc(scope, failure);
            if (result != null) throw result;
        }
        static int Main(string[] args)
        {
            Reset(true, false); var rock = Rock(100, 100, 100); var hit = Hit(); Rpc(rock, hit);
            Check(rock.m_hitAreas[0].m_health == 90 && rock.m_hitAreas[1].m_health == 95 && rock.m_hitAreas[2].m_health == 100, "Звичайний proc: основний удар10, один сусід5, без гарантованого добивання");
            Check(PerkRuntimeService.Chances.SequenceEqual(new[] { .20f, .05f }), "Окремі20%/5% rolls і номінальний1% обвал");
            Check(rock.SupportChecks == 0 && rock.Drops == 0, "Нелетальна бонусна шкода не запускає support scan/дроп");
            Check(rock.GeneratedSkill.All(x => x == 0) && hit.m_skillRaiseAmount == 1, "Generated skillRaise0, оригінальний hit не переписано");
            Check(PerkVisualService.Cues.SequenceEqual(new[] { "pickaxes_70" }), "Лише підтверджена звичайна шкода має fracture cue");

            Reset(true, false); rock = Rock(100, 100, 100); rock.m_damageModifiers.Factor = .5f; hit = Hit(); Rpc(rock, hit);
            Check(rock.m_hitAreas[0].m_health == 95 && rock.m_hitAreas[1].m_health == 97.5f, "Опір.5 застосовується один раз до оригіналу і бонусу");
            Check(hit.m_damage.m_pickaxe == 5f, "Клон сусіда не змінює після-native damage оригіналу");
            Reset(true, false); UnityEngine.Random.Extra = 2; rock = Rock(100, 100, 100, 100); Rpc(rock, Hit());
            Check(rock.m_hitAreas[1].m_health == 95 && rock.m_hitAreas[2].m_health == 95 && rock.m_hitAreas[3].m_health == 100, "Два найближчі сусіди отримують по половині удару");

            Reset(true, false); rock = Rock(5, 4, 200); rock.m_hitAreas[2].Unsupported = true; Rpc(rock, Hit());
            Check(rock.SupportChecks == 1 && rock.m_hitAreas[2].m_health == 100, "Основний летальний удар: рівно одна native перевірка високого HP");
            Check(!PerkVisualService.Cues.Contains("pickaxes_70_collapse"), "Часткове руйнування не отримує fullcollapse cue");
            Reset(true, false); rock = Rock(100, 4, 200); rock.m_hitAreas[2].Unsupported = true; Rpc(rock, Hit());
            Check(rock.SupportChecks == 1 && rock.m_hitAreas[0].m_health == 90 && rock.m_hitAreas[2].m_health == 100, "Оригінал вижив, сусід загинув: одна негайна native support перевірка");
            Reset(true, false); rock = Rock(100, 4, 100); rock.m_hitAreas[0].Unsupported = rock.m_hitAreas[2].Unsupported = true; Rpc(rock, Hit());
            Check(rock.SupportChecks == 1 && Pickaxes70SuperHitPatch.AllBroken(rock.m_hitAreas.ToArray()) && PerkVisualService.Cues.Count(x => x == "pickaxes_70_collapse") == 1,
                "Нелетальний оригінал/летальний бонус: support cascade підтверджено в тому самому RPC один раз");
            Reset(true, false); rock = Rock(100, 4, 200); rock.m_supportCheck = false; rock.m_hitAreas[2].Unsupported = true; Rpc(rock, Hit());
            Check(rock.SupportChecks == 0 && rock.m_hitAreas[2].m_health == 200, "m_supportCheck=false не отримує нової support політики");
            Reset(true, false); rock = Rock(100, 4, 200); rock.m_nview.Valid = false; Rpc(rock, Hit());
            Check(rock.SupportChecks == 0, "Невалідний nview не отримує explicit support scan");

            Reset(true, false); rock = Rock(5, 100, 100); rock.m_hitAreas[1].Unsupported = rock.m_hitAreas[2].Unsupported = true; Rpc(rock, Hit());
            Check(Pickaxes70SuperHitPatch.AllBroken(rock.m_hitAreas.ToArray()) && rock.SupportChecks == 1, "Native support cascade завершується до RPC confirmation");
            Check(PerkVisualService.Cues.Count(x => x == "pickaxes_70_collapse") == 1, "Підтверджений support cascade має рівно один collapse cue");
            Pickaxes70SuperHitPatch.ConfirmCollapse(rock, rock.m_hitAreas.ToArray(), new Player(), default);
            Check(PerkVisualService.Cues.Count(x => x == "pickaxes_70_collapse") == 1, "Weak receipt не дозволяє повторний collapse cue тієї самої жили");

            Reset(true, true); rock = Rock(100, 100, 100); rock.m_damageModifiers.Factor = .5f; Rpc(rock, Hit());
            Check(Pickaxes70SuperHitPatch.AllBroken(rock.m_hitAreas.ToArray()) && rock.Drops == 3, "Лише deliberate fullcollapse гарантує всі native kills/drops з опором");
            Check(rock.SupportChecks == 0 && PerkVisualService.Cues.Count(x => x == "pickaxes_70_collapse") == 1, "Повний deliberate обвал: без зайвої support шкоди, cueonce");
            Reset(false); rock = Rock(100, 100); Rpc(rock, Hit());
            Check(rock.m_hitAreas[1].m_health == 100 && PerkRuntimeService.Chances.Count == 1 && PerkVisualService.Cues.Count == 0, "Невдалий proc: немає другого roll/шкоди/ефекту");
            Reset(true, false); rock = Rock(100, 100); rock.m_nview.Owner = false; Rpc(rock, Hit());
            Check(PerkRuntimeService.Chances.Count == 0 && PerkVisualService.Cues.Count == 0, "Не-owner не виконує perk proc");
            Reset(true, false); rock = Rock(100, 100); hit = Hit(); hit.Attacker = null; Rpc(rock, hit);
            Check(PerkRuntimeService.Chances.Count == 0, "Native support/non-player hit не запускає perk");
            Reset(true, false); rock = Rock(100, 100); rock.m_hitAreas[1].m_collider.bounds.Point = new UnityEngine.Vector3(4, 0, 0); Rpc(rock, Hit());
            Check(rock.m_hitAreas[1].m_health == 100 && PerkVisualService.Cues.Count == 0, "Поза3м немає бонусу чи хибного cue");
            Reset(true, false); rock = Rock(100, 100, 100); hit = Hit(); rock.DamageArea(0, hit); rock.DamageArea(2, hit);
            Check(PerkRuntimeService.Chances.Count == 2, "Багатосегментний HitData має одну пару rolls");

            Reset(); var first = BeginRpc(Rock(100), Hit()); var second = BeginRpc(Rock(100), Hit());
            EndRpc(second); Check(ReferenceEquals(Pickaxes70DamageScope.Current, first), "Nested RPC відновлює попередній scope");
            var error = new Exception("native failure"); Check(ReferenceEquals(EndRpc(first, error), error) && Pickaxes70DamageScope.Current == null, "Finalizer очищає scope та зберігає native exception");
            Reset(true, false); rock = Rock(100, 4, 200); rock.m_hitAreas[2].Unsupported = true; Rpc(rock, Hit());
            Reset(false); Rpc(rock, Hit(200));
            Check(Pickaxes70SuperHitPatch.AllBroken(rock.m_hitAreas.ToArray()), "Пізніший vanilla удар справді обвалив залишок у сценарії attribution");
            Check(!PerkVisualService.Cues.Contains("pickaxes_70_collapse"), "Пізніший непов'язаний vanilla удар не успадковує proc attribution");
            Reset(); PerkRuntimeService.ForceProc = true; rock = Rock(100, 100); Rpc(rock, Hit());
            Check(Pickaxes70SuperHitPatch.AllBroken(rock.m_hitAreas.ToArray()), "ForceProc лишається явним debug override обох rolls");
            Check(Pickaxes70DamageScope.Current == null, "Після завершення RPC немає retained scope/timer");

            if (args.Length != 2) throw new ArgumentException("candidate DLL and real native assembly_valheim required");
            using var plugin = ModuleDefinition.ReadModule(args[0]); using var native = ModuleDefinition.ReadModule(args[1]);
            MethodDefinition P(string type, string method) => plugin.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method);
            bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
            Check(!plugin.Types.Any(t => t.Name == "Pickaxes70CollapseWitness"), "У кандидата немає2с witness/MonoBehaviour polling");
            Check(Calls(P("Pickaxes70DamageScopePatch", "Finalizer"), "Pickaxes70SuperHitPatch", "ConfirmCollapse"), "Реальний кандидат підтверджує cue після RPC native pipeline");
            Check(Calls(P("Pickaxes70SuperHitPatch", "DamageNeighbour"), "MineRock5", "DamageArea") && !Calls(P("Pickaxes70SuperHitPatch", "DamageNeighbour"), "HitData", "ApplyResistance"), "Кандидат делегує опір native DamageArea, без повторного ручного опору");
            Check(Calls(P("Pickaxes70SuperHitPatch", "Postfix"), "MineRock5", "CheckSupport"), "Кандидат має локальний bounded support resolution");
            var mine = native.Types.Single(t => t.Name == "MineRock5"); var rpc = mine.Methods.Single(m => m.Name == "RPC_Damage");
            Check(Calls(rpc, "MineRock5", "DamageArea") && Calls(rpc, "MineRock5", "CheckSupport"), "Реальний native RPC містить DamageArea та умовний support scan");
            var instructions = rpc.Body.Instructions.ToList(); int damage = instructions.FindIndex(i => i.Operand is MethodReference r && r.Name == "DamageArea");
            int support = instructions.FindIndex(i => i.Operand is MethodReference r && r.Name == "CheckSupport");
            Check(instructions[damage + 1].OpCode == OpCodes.Stloc_0 && instructions[damage + 2].OpCode == OpCodes.Ldloc_0 &&
                instructions[damage + 3].Operand is Instruction skip && skip.Offset > instructions[support].Offset,
                "Native support admission залежить від original DamageArea result");
            Check(Calls(mine.Methods.Single(m => m.Name == "CheckSupport"), "MineRock5", "DamageArea"), "Реальний native support використовує DamageArea, а не стороннє видалення/drop");
            Check(Calls(mine.Methods.Single(m => m.Name == "DamageArea"), "HitData", "ApplyResistance"), "Реальний native DamageArea змінює damage через resistance");
            Check(Calls(P("PerkVisualService", "PlayAtWorldPosition"), "NetworkSync", "SendProcFeedback"), "Headless feedback лишився native owner relay");
            bool Text(MethodDefinition method, string value) => method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, value));
            const string english = "A pickaxe strike sometimes damages neighbouring pieces of the same vein. Rarely, a mighty strike crumbles the entire deposit with subterranean thunder.";
            const string ukrainian = "Удар кайла інколи пошкоджує сусідні частини тієї самої жили. Зрідка могутній удар розсипає всю жилу, супроводжуваний підземним громом.";
            Check(Text(P("PerkLocalization", "Register"), "vm_perk_pickaxes_70_desc") && Text(P("PerkLocalization", "Register"), english) &&
                Text(P("PerkLocalization", "AddUkrainianPerkWords"), ukrainian), "Кандидат має узгоджені EN/UA ключі: шкода сусідам, рідкісний обвал");
            Check(Text(P("PerkNarrativeService", ".cctor"), english) && Text(P("PerkNarrativeService", ".cctor"), ukrainian), "Narrative і fallback описують ту саму нову механіку без старогоx5/15%");
            Console.WriteLine($"PASS {passed} actual-source deterministic/native contracts; Unity/live mining remains untested."); return 0;
        }
    }
    internal static class PerkRuntimeService
    {
        internal static Queue<bool> Rolls = new(); internal static List<float> Chances = new(); internal static bool ForceProc;
        internal static bool HasPerk(Player p, Skills.SkillType skill, int milestone) => p.Skill >= milestone;
        internal static bool RollChance(float chance) { Chances.Add(chance); return ForceProc || Rolls.Dequeue(); }
    }
    internal static class PerkVisualService { internal static List<string> Cues = new(); internal static void PlayProc(Player p, string cue, UnityEngine.Vector3 point, bool prominent, bool show) => Cues.Add(cue); }
    internal static class MasteryPlugin { internal static Settings Settings = new(); internal static Log Log = new(); }
    internal sealed class Settings { internal Flag VerboseLogging = new(); } internal sealed class Flag { internal bool Value; } internal sealed class Log { internal void LogInfo(string value) { } }
}
namespace HarmonyLib { [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } } }
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x*x+y*y+z*z; public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct Bounds { public Vector3 Point; public Vector3 ClosestPoint(Vector3 point) => Point; }
    public class Collider { public Bounds bounds; }
    public static class Mathf { public static int Min(int a, int b) => Math.Min(a,b); }
    public static class Random { public static int Extra = 1; public static int Range(int a,int b) => Extra; }
}
public class Player { public int Skill = 70; }
public static class Skills { public enum SkillType { Pickaxes } }
public class HitData
{
    public enum DamageModifier { Normal }
    public struct DamageTypes { public float m_pickaxe; }
    public DamageTypes m_damage; public Player Attacker; public UnityEngine.Vector3 m_point; public float m_skillRaiseAmount;
    public Player GetAttacker() => Attacker; public HitData Clone() => (HitData)MemberwiseClone();
    public void ApplyResistance(DamageModifiers mods, out DamageModifier ignored) { m_damage.m_pickaxe *= mods.Factor; ignored = DamageModifier.Normal; }
    public float GetTotalDamage() => m_damage.m_pickaxe;
}
public sealed class DamageModifiers { public float Factor = 1f; }
public class ZNetView { public bool Owner = true, Valid = true; public bool IsOwner() => Owner; public bool IsValid() => Valid; }
public class MineRock5
{
    public sealed class HitArea { public float m_health; public UnityEngine.Collider m_collider; public bool Unsupported; }
    public List<HitArea> m_hitAreas = new(); public ZNetView m_nview = new(); public bool m_supportCheck = true;
    public DamageModifiers m_damageModifiers = new(); public int SupportChecks, Drops; public List<float> GeneratedSkill = new();
    public void LoadHealth() { } public HitArea GetHitArea(int i) => m_hitAreas[i];
    public bool DamageArea(int index, HitData hit)
    {
        var state = ValheimMastery.MiningChecks.BeginArea(this,index,hit); var area = m_hitAreas[index]; bool killed = false;
        if (m_nview.IsOwner() && area.m_health > 0) { hit.ApplyResistance(m_damageModifiers, out _); area.m_health -= hit.GetTotalDamage(); killed = area.m_health <= 0; if (killed) Drops++; }
        if (state == null) GeneratedSkill.Add(hit.m_skillRaiseAmount);
        ValheimMastery.MiningChecks.EndArea(this,hit,state); return killed;
    }
    public void CheckSupport() { SupportChecks++; foreach (var area in m_hitAreas) if (area.m_health > 0 && area.Unsupported) { area.m_health -= 100; if (area.m_health <= 0) Drops++; } }
}
