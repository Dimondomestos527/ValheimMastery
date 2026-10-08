using System;
using System.Runtime.InteropServices;
using System.Threading;

// External QC launcher helper, not loaded into Unity. Only the owned scratch console is signalled.
public static class GracefulConsoleStop
{
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GenerateConsoleCtrlEvent(uint evt, uint group);
    public static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        uint pid = uint.Parse(args[0]);
        FreeConsole();
        if (!AttachConsole(pid)) return 10;
        SetConsoleCtrlHandler(IntPtr.Zero, true);
        if (!GenerateConsoleCtrlEvent(0, 0)) return 11;
        Thread.Sleep(1000);
        FreeConsole();
        return 0;
    }
}
