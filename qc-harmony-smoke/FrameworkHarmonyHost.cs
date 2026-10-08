// Standalone .NET Framework host, deliberately excluded from the net8 project.
// Uses the actual runtime Harmony and runtime dependencies, never a replacement.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class FrameworkHarmonyHost
{
    private static string managed, core, plugin;
    private const string Owner = "valheimmastery.qc.framework";
    private static Type harmonyType;
    private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public static int Main(string[] args)
    {
        if (args.Length < 3 || args.Length > 4) { Console.Error.WriteLine("Required: plugin managed core [transpilers]"); return 2; }
        plugin = Path.GetFullPath(args[0]); managed = Path.GetFullPath(args[1]); core = Path.GetFullPath(args[2]);
        Console.WriteLine("HOST CLR=" + Environment.Version + " bits=" + (IntPtr.Size * 8) + " Mono=" + (Type.GetType("Mono.Runtime") != null));
        Console.WriteLine("PLUGIN " + plugin); Console.WriteLine("MANAGED " + managed); Console.WriteLine("CORE " + core);
        Console.WriteLine("Exception.PrepForRemoting=" + (typeof(Exception).GetMethod("PrepForRemoting", Flags) != null));
        Console.WriteLine("Exception.FixRemotingException=" + (typeof(Exception).GetMethod("FixRemotingException", Flags) != null));
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            Assembly h = Assembly.LoadFrom(Path.Combine(core, "0Harmony.dll"));
            Console.WriteLine("HARMONY " + h.FullName + " path=" + h.Location);
            Type access = h.GetType("HarmonyLib.AccessTools", true);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(access.TypeHandle);
            Console.WriteLine("HARMONY_INIT PASS");
            Assembly a = Assembly.LoadFrom(plugin);
            Console.WriteLine("GAME " + Assembly.LoadFrom(Path.Combine(managed, "assembly_valheim.dll")).Location);
            foreach (string name in new[] { "Skeleton35SpawnAuthorPatch", "Magic70CarrierSpawnCapPatch" })
            {
                Type t = a.GetType("ValheimMastery." + name, true);
                MethodInfo m = t.GetMethod("TargetMethod", Flags);
                object resolved = m.Invoke(null, null);
                Console.WriteLine("DYNAMIC " + name + " -> " + (resolved == null ? "NULL" : resolved.ToString()));
                if (resolved == null) throw new Exception("Missing dynamic target " + name);
            }
            if(args.Length == 4 && args[3] == "transpilers") return TranspilerChecks(a,h);
            harmonyType = h.GetType("HarmonyLib.Harmony", true);
            object harmony = Activator.CreateInstance(harmonyType, new object[] { Owner });
            harmonyType.GetMethod("PatchAll", new[] { typeof(Assembly) }).Invoke(harmony, new object[] { a });
            Registrations();
            Console.WriteLine("PATCHALL PASS");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            if (error is ReflectionTypeLoadException)
                foreach (Exception loader in ((ReflectionTypeLoadException)error).LoaderExceptions) Console.Error.WriteLine(loader.ToString());
            try { Registrations(); } catch (Exception e) { Console.Error.WriteLine("REGISTRATION_UNAVAILABLE " + e); }
            return 1;
        }
    }
    private static int TranspilerChecks(Assembly a, Assembly h)
    {
        int failed=0;
        string[] names={"PickableCanonicalBonusFeedbackPatch","WoodInventoryLoadPatch","Skeleton35SpawnAuthorPatch","Magic70CarrierSpawnCapPatch"};
        string[] replacements={"PlayPickableBonus","ClampPersistedStack","TagSpawnedSummon","GetNrOfInstancesWithCarrierSlot"};
        Type processor=h.GetType("HarmonyLib.PatchProcessor",true);
        MethodInfo read=processor.GetMethods(Flags).Single(m=>m.Name=="GetOriginalInstructions" && m.GetParameters().Length==2 && m.GetParameters()[1].ParameterType.IsByRef);
        for(int i=0;i<names.Length;i++)
        {
            try
            {
                Type patch=a.GetType("ValheimMastery."+names[i],true);
                MethodBase original=null;
                MethodInfo dynamicTarget=patch.GetMethod("TargetMethod",Flags);
                if(dynamicTarget!=null) original=(MethodBase)dynamicTarget.Invoke(null,null);
                else
                {
                    object attribute=patch.GetCustomAttributes(false).Single(x=>x.GetType().FullName=="HarmonyLib.HarmonyPatch");
                    object info=attribute.GetType().GetField("info",Flags).GetValue(attribute);
                    Type target=(Type)info.GetType().GetField("declaringType",Flags).GetValue(info);
                    string method=(string)info.GetType().GetField("methodName",Flags).GetValue(info);
                    Type[] types=(Type[])info.GetType().GetField("argumentTypes",Flags).GetValue(info);
                    original=types==null ? target.GetMethods(Flags).Single(m=>m.Name==method) : target.GetMethod(method,Flags,null,types,null);
                }
                if(original==null) throw new Exception("Missing target "+names[i]);
                object[] values={original,null};
                IEnumerable input=(IEnumerable)read.Invoke(null,values);
                object output=patch.GetMethod("Transpiler",Flags).Invoke(null,new object[]{input});
                int count=0,matches=0;
                foreach(object instruction in (IEnumerable)output)
                {
                    count++;
                    object operand=instruction.GetType().GetField("operand",Flags).GetValue(instruction);
                    MethodInfo called=operand as MethodInfo;
                    if(called!=null && called.Name==replacements[i])matches++;
                }
                if(matches!=1)throw new Exception("Expected exactly one replacement, found="+matches);
                Console.WriteLine("TRANSPILER PASS "+names[i]+" original="+original+" outputInstructions="+count+" replacement="+replacements[i]);
            }
            catch(Exception error){failed++;Console.Error.WriteLine("TRANSPILER FAIL "+names[i]+" "+error);}
        }
        Console.WriteLine("TRANSPILER_EXECUTION checked="+names.Length+" failed="+failed+"; no native detours/emitted-wrapper/live claim");
        return failed==0?0:1;
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string path in new[] { managed, core, Path.GetDirectoryName(plugin) })
        {
            string candidate = Path.Combine(path, name);
            if (File.Exists(candidate)) { Console.WriteLine("RESOLVE " + name + " " + candidate); return Assembly.LoadFrom(candidate); }
        }
        return null;
    }
    private static void Registrations()
    {
        if (harmonyType == null) { Console.WriteLine("REGISTRATION_UNAVAILABLE Harmony init incomplete"); return; }
        IEnumerable methods = (IEnumerable)harmonyType.GetMethod("GetAllPatchedMethods", Flags).Invoke(null, null);
        int targets = 0, count = 0, transpilers = 0;
        Dictionary<string, int> entries = new Dictionary<string, int>();
        foreach (MethodBase original in methods)
        {
            object info = harmonyType.GetMethod("GetPatchInfo", Flags).Invoke(null, new object[] { original });
            bool ours = false;
            foreach (string kind in new[] { "Prefixes", "Postfixes", "Transpilers", "Finalizers" })
            {
                IEnumerable patches = (IEnumerable)info.GetType().GetField(kind, Flags).GetValue(info);
                foreach (object patch in patches)
                {
                    string owner = (string)patch.GetType().GetField("owner", Flags).GetValue(patch);
                    if (owner != Owner) continue;
                    ours = true; count++; if (kind == "Transpilers") transpilers++;
                    MethodInfo method = (MethodInfo)patch.GetType().GetProperty("PatchMethod", Flags).GetValue(patch, null);
                    string key = original.DeclaringType.FullName + "::" + original + "|" + kind + "|" + method.DeclaringType.FullName + "::" + method;
                    if (!entries.ContainsKey(key)) entries[key] = 0;
                    entries[key]++;
                    Console.WriteLine("PATCH " + key);
                }
            }
            if (ours) targets++;
        }
        int duplicate = entries.Count(x => x.Value > 1);
        Console.WriteLine("REGISTRATIONS targets=" + targets + " patches=" + count + " transpilers=" + transpilers + " duplicates=" + duplicate);
        foreach (KeyValuePair<string,int> entry in entries) if (entry.Value > 1) Console.WriteLine("DUPLICATE " + entry.Key);
    }
}
