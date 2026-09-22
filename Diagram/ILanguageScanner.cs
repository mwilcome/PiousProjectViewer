using System.Collections.Generic;
using System.Linq;

namespace pious_project_viewer.Diagram;

public interface ILanguageScanner
{
    string Name { get; }
    bool SupportsComplexity { get; }
    bool SupportsCrap { get; }
    bool CanScan(string folder);
    string TestCommand(string folder);
    DiagramDocument Scan(string folder);
}

public static class Scanners
{
    static readonly ILanguageScanner[] Known = [new CSharpScanner()];

    public static IReadOnlyList<ILanguageScanner> All => Known;

    public static ILanguageScanner? For(string folder) =>
        Known.FirstOrDefault(scanner => scanner.CanScan(folder));

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
