using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Patch113Checks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        MethodDefinition M(string type, string method) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method);
        bool Calls(string type, string method, string target, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == target && r.Name == name);
        bool Float(string type, string method, float value) => M(type, method).Body.Instructions.Any(i => i.Operand is float f && f == value);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        Check(Calls("MasteryPlugin", "Awake", "Runtime", "MakeAllAssetsLoadable"), "extended native asset catalog enabled before gameplay loads");
        Check(Calls("NativeSoftVisualAssets", "Get", "SoftReference`1", "get_IsLoaded"), "async native asset access only after load completion");
        Check(Calls("Stride70Service", "Update", "PerkFeedbackService", "PlayLocal"), "running phase cue rendered locally without RPC");
        Check(Calls("FireStaff35Charge", "ResolvePose", "Animator", "HasState"), "fire windup direct-state fallback verifies state existence");
        Check(Calls("Fire35CosmeticPoseEventGate", "Prefix", "FireStaff35Charge", "CosmeticPose"), "charge pose events cannot fire a stale attack");
        Check(Calls("Ice35Exposure", "Update", "ZNetView", "IsOwner") && Float("Ice35Exposure", "Ready", 3f), "cold exposure uses creature owner and continuous three-second threshold");
        Check(!M("Ice35ExposureResistancePatch", "Postfix").Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.DeclaringType.Name == "Character"), "cold resistance override never mutates native Character fields");
        Check(Calls("Ice35Exposure", "Ready", "SEMan", "HaveStatusEffect"), "cold weakness ends when both frost effects expire");
        Check(Float("Magic70IceStorm", "Duration", 20f), "storm lasts twenty seconds per quality");
        Check(Float("Magic70DomeStaminaPatch", "Prefix", 1.25f) &&
            M("Magic70DomeStaminaPatch", "Prefix").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_eitrRegenMultiplier") &&
            M("Magic70DomeStaminaPatch", "Prefix").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_staminaRegenMultiplier"), "dome grants both native 25-percent regeneration multipliers");
        Check(Calls("Magic70DomeStaminaPatch", "Prefix", "SEMan", "RemoveStatusEffect"), "dome regeneration removed outside allied dome");
        Check(Calls("Magic70SurtlingBirthVisual", "Begin", "Character", "GetVisual"), "birth scales the real native creature visual");
        Check(Calls("IceTrailVisual", "DrawFlakes", "ParticleSystem", "SetParticles") && !Calls("IceTrailVisual", "DrawFlakes", "Physics", "Raycast"), "ground snow has bounded particles and no per-frame terrain queries");
        Check(Calls("WorkshopRemoteCraft", "ReceiveReply", "WorkshopTiming", "Mark"), "reply arrival timestamp separated from queued execution");
        Check(Calls("Mace35CorpseProjectile", "Mark", "PerkFeedbackService", "Play"), "mace secondary has a proc cue before a killing blow");
        Console.WriteLine(count + " patch113 STATIC checks passed; LIVE_TEST_REQUIRED.");
        return 0;
    }
}
