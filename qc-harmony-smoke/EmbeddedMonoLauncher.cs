// Isolated console launcher. No Valheim executable or Unity player is started.
using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class EmbeddedMonoLauncher
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool SetDllDirectory(string path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention=CallingConvention.Cdecl)] private static extern void mono_set_dirs(string assemblies, string config);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention=CallingConvention.Cdecl)] private static extern void mono_set_assemblies_path(string path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention=CallingConvention.Cdecl)] private static extern void mono_config_parse(string path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mono_jit_init_version(string name, string version);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mono_domain_assembly_open(IntPtr domain, string path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int mono_jit_exec(IntPtr domain, IntPtr assembly, int argc, IntPtr argv);
    public static int Main(string[] args)
    {
        if(args.Length < 5 || args.Length > 6){Console.Error.WriteLine("Required: monoRoot managed host plugin core [transpilers]");return 2;}
        string mono=Path.GetFullPath(args[0]), managed=Path.GetFullPath(args[1]), host=Path.GetFullPath(args[2]);
        if(!SetDllDirectory(Path.Combine(mono,"EmbedRuntime"))) throw new Exception("SetDllDirectory failed");
        Console.WriteLine("EMBEDDED_MONO " + Path.Combine(mono,"EmbedRuntime","mono-2.0-bdwgc.dll"));
        mono_set_dirs(managed,Path.Combine(mono,"etc"));
        mono_set_assemblies_path(managed+";"+args[4]+";"+Path.GetDirectoryName(host));
        mono_config_parse(null);
        IntPtr domain=mono_jit_init_version("MasteryIsolatedHarmonyQC","v4.0.30319");
        if(domain==IntPtr.Zero) throw new Exception("Mono initialization failed");
        IntPtr assembly=mono_domain_assembly_open(domain,host);
        if(assembly==IntPtr.Zero) throw new Exception("Mono cannot load isolated QC host");
        string[] argv=args.Length==6 ? new[]{host,args[3],managed,args[4],args[5]} : new[]{host,args[3],managed,args[4]};
        IntPtr[] strings=new IntPtr[argv.Length];
        IntPtr vector=Marshal.AllocHGlobal(IntPtr.Size*argv.Length);
        try {
            for(int i=0;i<argv.Length;i++){strings[i]=Marshal.StringToHGlobalAnsi(argv[i]);Marshal.WriteIntPtr(vector,i*IntPtr.Size,strings[i]);}
            return mono_jit_exec(domain,assembly,argv.Length,vector);
        }finally{foreach(IntPtr str in strings)if(str!=IntPtr.Zero)Marshal.FreeHGlobal(str);Marshal.FreeHGlobal(vector);}
    }
}
