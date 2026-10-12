using HarmonyLib;
namespace ValheimMastery
{
    // Read-only diagnostic, explicitly armed by the local human. No gameplay mutation.
    [HarmonyPatch(typeof(PerkDebugService),nameof(PerkDebugService.RegisterCommands))]
    internal static class Crafting100TraceCommand
    {
        static void Postfix()
        {
            new Terminal.ConsoleCommand("vm_c100trace","vm_c100trace arm | status | stop — one bounded Völund click trace",args=>
            {
                string action=args.Args.Length>1?args.Args[1].ToLowerInvariant():"arm";
                string result=action=="arm"?Crafting100AttemptTrace.Arm():action=="stop"?Crafting100AttemptTrace.Disarm():action=="status"?Crafting100AttemptTrace.Status():"vm_c100trace arm | status | stop";
                args.Context.AddString(result);
            },false);
        }
    }
}
