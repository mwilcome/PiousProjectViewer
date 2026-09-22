using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PiousProjectViewer.Diagram;

public interface ILanguageScanner
{
    string Name { get; }
    bool SupportsComplexity { get; }
    bool SupportsCrap { get; }
    bool CanScan(string folder);
    string TestCommand(string folder);
    string MutateCommand(string folder);
    string? CoverageFile(string folder);
    DiagramDocument Scan(string folder);
}

public static class Scanners
{
    static readonly ILanguageScanner[] Known = [new CSharpScanner(), new AngularScanner(), new JavaScanner()];

    public static IReadOnlyList<ILanguageScanner> All => Known;

    public static ILanguageScanner? For(string folder)
    {
        var capable = Known.Where(scanner => scanner.CanScan(folder)).ToList();
        if (capable.Count <= 1)
            return capable.FirstOrDefault();
        return capable
            .OrderByDescending(scanner => SourceCount(folder, Extension(scanner.Name)))
            .ThenBy(scanner => scanner.Name, StringComparer.Ordinal)
            .First();
    }

    static string Extension(string language) => language switch
    {
        "C#" => ".cs",
        "Java" => ".java",
        _ => ".ts"
    };

    static int SourceCount(string folder, string extension)
    {
        if (!Directory.Exists(folder))
            return 0;
        var count = 0;
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0 && count < 4000)
        {
            var dir = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (Directory.Exists(child))
                {
                    if (name is not ("node_modules" or "dist" or "bin" or "obj" or "target" or ".git" or "coverage"))
                        pending.Push(child);
                    continue;
                }
                if (child.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    count++;
            }
        }
        return count;
    }

    public static ILanguageScanner? Resolve(string folder, string? choice)
    {
        if (!string.IsNullOrWhiteSpace(choice) && choice != "auto")
        {
            var named = Known.FirstOrDefault(scanner => scanner.Name == choice);
            return named is not null && named.CanScan(folder) ? named : null;
        }
        return For(folder);
    }
}

public static class Agents
{
    public static IReadOnlyList<string> Names { get; } = ["Grok"];
}
