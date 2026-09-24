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
            var start = new ProcessStartInfo { FileName = file, UseShellExecute = true };
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
}
