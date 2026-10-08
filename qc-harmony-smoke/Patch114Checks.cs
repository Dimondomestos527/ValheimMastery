using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Patch114Checks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        MethodDefinition M(string type, string method) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method);
        bool Calls(string type, string method, string target, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == target && r.Name == name);
        bool Field(string type, string method, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is FieldReference f && f.Name == name);
        bool Float(string type, string method, float value) => M(type, method).Body.Instructions.Any(i => i.Operand is float f && f == value);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        Check(Calls("FireStaff35Charge", "ResolvePose", "Fire35PoseDriver", "Begin") &&
            !Calls("FireStaff35Charge", "ResolvePose", "ZSyncAnimation", "SetTrigger"), "charge starts isolated sampler, not a live native attack");
        Check(!Calls("FireStaff35Charge", "SamplePose", "Animator", "Play") &&
            Calls("FireStaff35Charge", "SamplePose", "Fire35PoseDriver", "Draw"), "charging never repeatedly freezes gameplay Animator");
        Check(Calls("Fire35PoseDriver", "Begin", "Animator", "set_fireEvents") &&
            !Calls("Fire35PoseDriver", "Clone", "Object", "Instantiate"), "renderless sampler has no cloned gameplay components/events");
        Check(Calls("FireStaff35Charge", "Input", "Character", "StartAttack") &&
            Calls("Fire35PoseDriver", "Release", "Animator", "ResetTrigger") &&
            Calls("Fire35PoseDriver", "Release", "Animator", "HasState"), "release admits one native attack before resuming verified windup");
        Check(Calls("FireStaff35Charge", "Input", "Character", "AddEitr") || Calls("FireStaff35Charge", "Input", "Player", "AddEitr"), "rejected native release refunds prepaid resource");
        var gate = module.Types.Single(t => t.Name == "Fire35ReleasePoseGate");
        Check(gate.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch" &&
            a.ConstructorArguments.Any(x => x.Value is TypeReference t && t.Name == "Player")), "release gate patches actual Player override, not only Humanoid base");
        Check(Field("OverdrawPenetrationService", "TryHandleHit", "m_skill") &&
            Field("OverdrawPenetrationService", "TryHandleHit", "m_skillRaiseAmount") &&
            Calls("OverdrawPenetrationService", "TryHandleHit", "Bows70WeakPointService", "ConsumeIfHit"), "piercing supplies native bow skill and consumes weak point on exact primary payload");
        Check(M("OverdrawPenetrationService", "TryHandleHit").Body.Instructions.Count(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == "Character" && r.Name == "Damage") == 1, "pierced target receives one direct damage dispatch");
        Check(Float("Bow70ArrowProfileService", "Apply", 1.25f), "enhanced charged payload adds twenty-five percent damage");
        var snowFilter = M("NativeSnowVisualAssets", "AcceptSnow").Body.Instructions;
        int acceptedAt = snowFilter.ToList().FindIndex(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference f && f.Name == "SnowSource");
        int mainTextureAt = snowFilter.ToList().FindIndex(i => i.Operand is MethodReference r && r.Name == "get_mainTexture");
        Check(acceptedAt >= 0 && (mainTextureAt < 0 || mainTextureAt > acceptedAt) &&
            !Calls("NativeSnowVisualAssets", "AcceptSnow", "ParticleSystemRenderer", "get_renderMode"),
            "native mesh flakes and non-mainTexture shaders are not rejected before selection");
        Check(Calls("NativeSnowVisualAssets", "CreateSnow", "NativeSoftVisualAssets", "Get") &&
            Calls("NativeSnowVisualAssets", "CreateSnow", "ParticleSystemRenderer", "SetActiveVertexStreams"), "native weather material fallback preserves renderer streams");
        Check(Calls("Stride70Service", "Update", "RoadRhythmPhaseVisual", "Set") &&
            Calls("RoadRhythmPhaseVisual", "EmitPhase", "VfxPool", "Spawn") &&
            Calls("RoadRhythmPhaseVisual", "EmitPhase", "VfxRecipeService", "Tune"), "discrete road cues preserve complete native landing prefab instead of partial emitter reconstruction");
        Check(Calls("WorkshopStationPrewarmPatch", "Postfix", "WorkshopStationProof", "Prewarm") &&
            Calls("WorkshopStationProof", "ReceiveWarmRequest", "WorkshopActor", "Resolve") &&
            Calls("WorkshopStationProof", "ReceiveWarmRequest", "WorkshopRemoteCraft", "ValidRecipeStation"), "prewarm validates authenticated player and exact selected station");
        Check(Calls("WorkshopStationProof", "ReceiveWarmProof", "ZDO", "GetOwner") &&
            Calls("WorkshopStationProof", "ReceiveWarmProof", "WorkshopStationProof", "WarmActorStillValid") &&
            Float("WorkshopStationProof", "ReceiveWarmProof", 8f), "prewarm proof keeps owner binding and eight-second expiry");
        Check(Field("WorkshopStationProof", "ReceiveWarmRequest", "WarmQueries") &&
            Calls("WorkshopStationProof", "Acquire", "WorkshopStationProof", "CancelWarmForActor") &&
            !Calls("WorkshopStationProof", "ReceiveWarmRequest", "Inventory", "RemoveItem") &&
            !Calls("WorkshopStationProof", "ReceiveWarmProof", "Inventory", "RemoveItem"),
            "warm query stays separate from real action and never spends inventory");
        Check(Calls("Magic70BloodDome", "Resume", "ZDO", "GetZDOID") &&
            Calls("Magic70BloodDome", "ReceiveTarget", "ZDO", "Set"), "cage caster identity uses native typed ZDOID persistence");
        Check(Calls("Magic70BloodCageMotionPatch", "Prefix", "Magic70BloodDome", "Resume") &&
            !Calls("Magic70BloodCageMotionPatch", "Prefix", "Character", "UpdateMotion"),
            "cage owner handoff resumes control without recursively calling native motion");
        var release = M("Magic70BloodCageHold", "Release").Body.Instructions;
        Check(release.Count(i => i.Operand is MethodReference r && r.DeclaringType.Name == "Character" && r.Name == "Damage") == 1 &&
            release.Any(i => i.Operand is FieldReference f && f.Name == "m_blunt") &&
            !release.Any(i => i.Operand is FieldReference f && f.Name == "m_spirit"),
            "cage emits one physical hit, not spirit damage ignored by living enemies");
        Check(Calls("Magic70BloodCageVisual", "Update", "ZDO", "GetLong") &&
            Calls("Magic70BloodCageVisual", "TryCreate", "PerkNativeFeedback", "CreateVisualOnly") &&
            Calls("Magic70BloodCageVisual", "OnDestroy", "Object", "Destroy"),
            "cage cosmetic observes network expiry and cleans owned shell and materials");
        Console.WriteLine(count + " patch114 STATIC structure checks passed; animation/rendering/speed remain LIVE_TEST_REQUIRED.");
        return 0;
    }
}
