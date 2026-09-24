using System;
using System.IO;
using System.Linq;

namespace PiousProjectViewer.Diagram;

public static class ScanCommand
{
    public static bool TryRun(string[] args)
    {
        var scanAt = Array.IndexOf(args, "--scan");
        if (scanAt < 0)
            return false;
        var folder = scanAt + 1 < args.Length ? args[scanAt + 1] : Directory.GetCurrentDirectory();
        var language = "auto";
        var languageAt = Array.IndexOf(args, "--language");
        if (languageAt >= 0 && languageAt + 1 < args.Length)
            language = args[languageAt + 1];
        var scanner = Scanners.Resolve(folder, language);
        if (scanner is null)
        {
            Console.Error.WriteLine("No scanner for this folder.");
            Environment.ExitCode = 1;
            return true;
        }
        DiagramPublisher.Publish(folder, scanner.Scan(folder));
        return true;
    }

    public static string CommandLine(string folder, string language)
    {
        var exe = Environment.ProcessPath ?? "PiousProjectViewer";
        return "\"" + exe + "\" --scan \"" + folder + "\" --language " + language;
    }

    public static string TestCommandFor(string folder) =>
        Scanners.For(folder)?.TestCommand(folder) ?? "echo No scanner for this folder.";

    public static string MutateCommandFor(string folder) =>
        Scanners.For(folder)?.MutateCommand(folder) ?? "";

    public static string? CoverageFileFor(string folder) =>
        Scanners.For(folder)?.CoverageFile(folder);
}
