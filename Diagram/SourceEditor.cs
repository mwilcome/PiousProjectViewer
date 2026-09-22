using System;
using System.Diagnostics;
using System.IO;

namespace pious_project_viewer.Diagram;

public static class SourceEditor
{
    internal static Action<ProcessStartInfo>? Launcher { get; set; }

    internal static Func<string?>? CodeFinder { get; set; }

    public static string Open(string file, int line)
    {
        var code = CodeFinder is not null ? CodeFinder() : FindCode();
        var at = Math.Max(1, line);
        try
        {
            ProcessStartInfo start = code is null
                ? new ProcessStartInfo { FileName = file, UseShellExecute = true }
                : new ProcessStartInfo
                {
                    FileName = code,
                    Arguments = "--goto \"" + file + ":" + at + ":1\"",
                    UseShellExecute = true
                };
            if (Launcher is not null)
                Launcher(start);
            else
                Process.Start(start);
            return code is null
                ? "VS Code was not found, so the file opened in the default app. That app may not jump to the line."
                : "Opened in VS Code at line " + at + ".";
        }
        catch (Exception ex)
        {
            return "Could not open the editor. " + ex.Message;
        }
    }

    static string? FindCode()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;
                var cmd = Path.Combine(directory, "code.cmd");
                if (File.Exists(cmd))
                    return cmd;
                var exe = Path.Combine(directory, "code.exe");
                if (File.Exists(exe))
                    return exe;
            }
        }

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Microsoft VS Code",
            "bin",
            "code.cmd");
        return File.Exists(local) ? local : null;
    }
}
