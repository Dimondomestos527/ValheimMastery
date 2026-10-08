using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

// Inspection only: no Harmony instance, PatchAll, patch mutation or game-state mutation.
[BepInPlugin("domestos.valheim.mastery.qc.observer", "Mastery Unity QC Observer", "1.0.0")]
[BepInDependency("domestos.valheim.mastery", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Observer : BaseUnityPlugin
{
    const string Owner = "domestos.valheim.mastery";
    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
    static string Identity(MethodBase method)
    {
        return method.DeclaringType.FullName + "::" + method + " [" + method.Module.ModuleVersionId + ":" + method.MetadataToken + "]";
    }
    void Awake()
    {
        var report = new StringBuilder();
        try
        {
            var plugin = Chainloader.PluginInfos[Owner];
            report.AppendLine("UTC=" + DateTime.UtcNow.ToString("o"));
            report.AppendLine("GAME_ROOT=" + Paths.GameRootPath);
            report.AppendLine("CONFIG_PATH=" + Paths.ConfigPath);
            report.AppendLine("PERSISTENT_DATA_PATH=" + UnityEngine.Application.persistentDataPath);
            report.AppendLine("BATCH_MODE=" + UnityEngine.Application.isBatchMode);
            report.AppendLine("COMMAND_LINE=" + Environment.CommandLine);
            report.AppendLine("MASTERY_VERSION=" + plugin.Metadata.Version);
            report.AppendLine("MASTERY_PATH=" + plugin.Instance.GetType().Assembly.Location);
            report.AppendLine("MASTERY_SHA256=" + Hash(plugin.Instance.GetType().Assembly.Location));
            report.AppendLine("MASTERY_ENABLED=" + plugin.Instance.enabled);
            var harmonyField = plugin.Instance.GetType().GetField("_harmony", BindingFlags.NonPublic | BindingFlags.Instance);
            report.AppendLine("MASTERY_HARMONY_ASSIGNED=" + (harmonyField != null && harmonyField.GetValue(plugin.Instance) != null));
            report.AppendLine("AWAKE_ORDER=observer hard-depends on Mastery; corroborate completion with Mastery success log markers");
            var harmonyAssembly = typeof(Harmony).Assembly;
            report.AppendLine("HARMONY_IDENTITY=" + harmonyAssembly.FullName);
            report.AppendLine("HARMONY_PATH=" + harmonyAssembly.Location);
            report.AppendLine("HARMONY_SHA256=" + Hash(harmonyAssembly.Location));
            var rows = new List<string>();
            var tuples = new Dictionary<string, int>();
            int originals = 0, prefixes = 0, postfixes = 0, transpilers = 0, finalizers = 0;
            int skeleton = 0, carrier = 0;
            foreach (var original in Harmony.GetAllPatchedMethods().OrderBy(Identity))
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null || !info.Owners.Contains(Owner)) continue;
                originals++;
                var kinds = new[] { "PREFIX", "POSTFIX", "TRANSPILER", "FINALIZER" };
                var sets = new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers };
                for (int i = 0; i < sets.Length; i++)
                {
                    foreach (var patch in sets[i].Where(p => p.owner == Owner))
                    {
                        if (i == 0) prefixes++; else if (i == 1) postfixes++; else if (i == 2) transpilers++; else finalizers++;
                        var tuple = Identity(original) + " | " + kinds[i] + " | " + Identity(patch.PatchMethod);
                        tuples[tuple] = tuples.ContainsKey(tuple) ? tuples[tuple] + 1 : 1;
                        rows.Add(tuple + " | priority=" + patch.priority + " index=" + patch.index + " before=" + string.Join(",", patch.before) + " after=" + string.Join(",", patch.after));
                        if (patch.PatchMethod.DeclaringType.Name == "Skeleton35SpawnAuthorPatch") skeleton++;
                        if (patch.PatchMethod.DeclaringType.Name == "Magic70CarrierSpawnCapPatch") carrier++;
                    }
                }
                if (original.DeclaringType.FullName.Contains("SpawnAbility") && original.Name == "MoveNext")
                {
                    report.AppendLine("SPAWN_ORIGINAL=" + Identity(original));
                    // Sorting is inspection of the installed complete pipeline, not execution/repatching.
                    var sorter = typeof(PatchProcessor).GetMethod("GetSortedPatchMethods", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null, new[] { typeof(MethodBase), typeof(Patch[]) }, null);
                    if (sorter == null) throw new MissingMethodException("PatchProcessor.GetSortedPatchMethods");
                    var sorted = (IEnumerable<MethodInfo>)sorter.Invoke(null, new object[] { original, info.Transpilers.ToArray() });
                    int order = 0;
                    foreach (var method in sorted) report.AppendLine("SPAWN_TRANSPILER_ORDER_" + (++order) + "=" + Identity(method));
                }
            }
            report.AppendLine("UNIQUE_ORIGINALS=" + originals);
            report.AppendLine("PREFIXES=" + prefixes);
            report.AppendLine("POSTFIXES=" + postfixes);
            report.AppendLine("TRANSPILERS=" + transpilers);
            report.AppendLine("FINALIZERS=" + finalizers);
            report.AppendLine("DUPLICATE_TUPLES=" + tuples.Count(p => p.Value > 1));
            report.AppendLine("SKELETON_DYNAMIC_REGISTRATIONS=" + skeleton);
            report.AppendLine("CARRIER_DYNAMIC_REGISTRATIONS=" + carrier);
            foreach (var tuple in tuples.Where(p => p.Value > 1)) report.AppendLine("DUPLICATE=" + tuple.Value + "x " + tuple.Key);
            report.AppendLine("REGISTRATIONS_BEGIN");
            foreach (var row in rows) report.AppendLine(row);
            report.AppendLine("REGISTRATIONS_END");
            report.AppendLine("OBSERVER_INSPECTION_COMPLETED=true");
        }
        catch (Exception error)
        {
            report.AppendLine("OBSERVER_EXCEPTION=" + error);
            Logger.LogError(error);
        }
        File.WriteAllText(Path.Combine(Paths.GameRootPath, "QC_OBSERVER_REPORT.txt"), report.ToString(), Encoding.UTF8);
        Logger.LogInfo("QC observer evidence written; no patches installed by observer.");
    }
}
