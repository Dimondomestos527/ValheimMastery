using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

// Infrastructure launcher only. Never replaces or loads the game's executable.
internal static class MasteryLauncher
{
    internal static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') result.Append('\\', slashes * 2 + 1);
            else result.Append('\\', slashes);
            result.Append(c); slashes = 0;
        }
        result.Append('\\', slashes * 2); result.Append('"');
        return result.ToString();
    }
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string python = Path.Combine(root, ".mastery-installer", "runtime", "python", "python.exe");
            string core = Path.Combine(root, ".mastery-installer", "updater", "mastery_installer.py");
            if (!File.Exists(python) || !File.Exists(core))
                throw new IOException("Run Install-Mastery.cmd once to prepare this launcher.");
            var arguments = new StringBuilder();
            string[] prefix = { "-X", "utf8", core, "startup", "--target", root, "--variant", "Client", "--" };
            foreach (string arg in prefix) arguments.Append(Quote(arg)).Append(' ');
            foreach (string arg in args) arguments.Append(Quote(arg)).Append(' ');
            var info = new ProcessStartInfo(python, arguments.ToString());
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.EnvironmentVariables.Remove("PYTHONPATH");
            info.EnvironmentVariables["PYTHONHOME"] = Path.GetDirectoryName(python);
            info.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
            info.WorkingDirectory = root;
            using (Process child = Process.Start(info)) { child.WaitForExit(); return child.ExitCode; }
        }
        catch (Exception e)
        {
            try
            {
                string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string directory = Path.Combine(root, ".mastery-installer");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "launcher-error.log"), e.ToString());
            }
            catch { }
            return 1;
        }
    }
}
