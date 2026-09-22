using System.Linq;

namespace pious_project_viewer.Diagram;

public interface ILanguageScanner
{
    string Name { get; }
    bool SupportsComplexity { get; }
    bool SupportsCrap { get; }
    bool CanScan(string folder);
    DiagramDocument Scan(string folder);
}

public static class Scanners
{
    static readonly ILanguageScanner[] Known = [new CSharpScanner()];

    public static ILanguageScanner? For(string folder) =>
        Known.FirstOrDefault(scanner => scanner.CanScan(folder));
}
