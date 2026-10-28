// Zarrar.exe / Zarrar-Stop.exe: runs launcher\ZARRAR-Launcher.ps1 next to this executable.
// Build with launcher\build-exe.cmd (uses the C# compiler that ships with Windows).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

static class Launcher
{
    static int Main(string[] args)
    {
        var exe = Process.GetCurrentProcess().MainModule.FileName;
        var root = Path.GetDirectoryName(exe);
        var script = Path.Combine(root, "launcher", "ZARRAR-Launcher.ps1");

        if (!File.Exists(script))
        {
            Console.Error.WriteLine("Cannot find " + script);
            Console.Error.WriteLine("Keep Zarrar.exe in the ZARRAR folder, next to the 'launcher' folder.");
            Console.ReadLine();
            return 1;
        }

        var extra = args.Select(a => a.StartsWith("-") ? a : "\"" + a + "\"").ToList();
        if (Path.GetFileNameWithoutExtension(exe).IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0)
            extra.Insert(0, "-Stop");

        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " + string.Join(" ", extra))
        {
            UseShellExecute = false,
            WorkingDirectory = root
        };

        using (var p = Process.Start(psi))
        {
            p.WaitForExit();
            return p.ExitCode;
        }
    }
}
