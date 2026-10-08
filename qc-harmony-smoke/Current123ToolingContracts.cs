using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Current123ToolingContracts
{
    // Current replacements, not mutations of historical patch113/114/115/116 assertions.
    internal static int Run(string plugin)
    {
        using var module=ModuleDefinition.ReadModule(plugin);
        TypeDefinition T(string name)=>module.Types.Single(t=>t.Name==name);
        MethodDefinition M(string type,string method)=>T(type).Methods.Single(m=>m.Name==method);
        bool Call(string type,string method,string target,string name)=>M(type,method).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name==target&&m.Name==name);
        bool Float(string type,string method,float value)=>M(type,method).Body.Instructions.Any(i=>i.Operand is float f&&f==value);
        int count=0;
        void Check(bool ok,string label){if(!ok)throw new Exception("CURRENT CONTRACT FAILURE "+label);count++;Console.WriteLine("PASS "+label);}
        Check(Call("Stride70Service","Update","PerkFeedbackService","Play")&&!Call("Stride70Service","Update","PerkFeedbackService","PlayLocal"),"patch113 replacement: one recipe-owned phase feedback path, not historical PlayLocal");
        Check(Call("VfxRecipeService","Spawn","NativePerkAssetResolver","ResolveLegacyRun"),"patch114 replacement: native historical run recipe resolver, not dormant RoadRhythmPhaseVisual.Set pipeline");
        Check(Float("ShieldRush70","TryStart",16.5f)&&Float("ShieldRush70","TryStart",8.25f),"patch115/116 replacement: light distance 16.5 and tower 8.25");
        Check(Call("Skeleton35Travel","Valid","SummonRosterCommands","Allowed")&&Call("Skeleton35Travel","Valid","ZDO","GetLong")&&Call("SummonRosterCommands","Allowed","GameObject","GetComponent"),"magic-foundation replacement: author checked locally, native prefab validation delegated");
        Check(Call("Pickaxes70SuperHitPatch","Postfix","Bounds","ClosestPoint")&&Call("Pickaxes70SuperHitPatch","Postfix","Pickaxes70SuperHitPatch","DamageNeighbour")&&Call("Pickaxes70SuperHitPatch","DamageNeighbour","MineRock5","DamageArea"),"current mining: bounds neighbours and ordinary bonus damage through native area path");
        Check(Float("Pickaxes70SuperHitPatch","Postfix",.20f)&&Float("Pickaxes70SuperHitPatch","Postfix",.05f)&&Float("Pickaxes70SuperHitPatch","Postfix",.5f)&&M("Pickaxes70SuperHitPatch","Postfix").Body.Instructions.Count(i=>i.Operand is MethodReference r&&r.Name=="RollChance")==2,"current mining:20% proc/5% conditional collapse/half incoming neighbour damage; live balance pending");
        Check(Float("FistsKickScalingPatch","Prefix",.030f)&&Float("FistsKickScalingPatch","Prefix",.0125f)&&Float("FistsKickScalingPatch","Prefix",.020f),"player-feedback replacement: current linear kick damage/stagger/force coefficients");
        Check(T("StationBulkLoadService").Methods.All(m=>m.Name!="ExtraToHalfCapacity")&&Float("StationBulkLoadService","ExtraToQuarterCapacity",.25f)&&Call("StationBulkLoadService","ExtraToQuarterCapacity","Mathf","CeilToInt"),"controlled-static replacement: quarter total capacity, rounded upward, old half-capacity API absent");
        Console.WriteLine(count+" CURRENT123 tooling contracts passed; no PatchAll/live claim.");
        return 0;
    }
}
