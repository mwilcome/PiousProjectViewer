using System;
using System.Diagnostics;

namespace PiousProjectViewer.Diagram;

public static class SourceEditor
{
    internal static Action<ProcessStartInfo>? Launcher { get; set; }

    public static string Open(string file, int line)
    {
        try
        {
            var start = Launch(file);
            if (Launcher is not null)
                Launcher(start);
            else
                Process.Start(start);
            return "Opened the file in the default app.";
        }
        catch (Exception ex)
        {
            return "Could not open the file. " + ex.Message;
        }
    }

    internal static ProcessStartInfo Launch(string file)
    {
        if (OperatingSystem.IsMacOS())
        {
            var open = new ProcessStartInfo { FileName = "/usr/bin/open", UseShellExecute = false };
            open.ArgumentList.Add(file);
            return open;
        }
        if (OperatingSystem.IsLinux())
        {
            var open = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
            open.ArgumentList.Add(file);
            return open;
        }
        return new ProcessStartInfo { FileName = file, UseShellExecute = true };
    }
}
