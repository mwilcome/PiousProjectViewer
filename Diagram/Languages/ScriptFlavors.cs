using System;
using System.IO;

namespace PiousProjectViewer.Diagram;

public sealed class ReactScanner : ScriptFlavorScanner
{
    public ReactScanner() : base("React", "react") { }
}

public sealed class VueScanner : ScriptFlavorScanner
{
    public VueScanner() : base("Vue", "vue") { }
}

public sealed class SvelteScanner : ScriptFlavorScanner
{
    public SvelteScanner() : base("Svelte", "svelte") { }
}

public class ScriptFlavorScanner : ILanguageScanner
{
    readonly string _package;

    public ScriptFlavorScanner(string name, string package)
    {
        Name = name;
        _package = package;
    }

    public string Name { get; }
    public bool SupportsComplexity => true;
    public bool SupportsCrap => true;
    public bool ShowsStyles => true;

    public bool CanScan(string folder)
    {
        if (!ScriptPackages.Depends(folder, _package))
            return false;
        if (ScriptPackages.IsAngular(folder))
            return false;
        if (Name == "React" && (ScriptPackages.Depends(folder, "vue") || ScriptPackages.Depends(folder, "svelte")))
            return false;
        if (Name == "Svelte" && ScriptPackages.Depends(folder, "vue"))
            return false;
        return true;
    }

    public string TestCommand(string folder) => "";

    public string MutateCommand(string folder) => "";

    public string? CoverageFile(string folder) => "lcov.info";

    public DiagramDocument Scan(string folder)
    {
        var document = new AngularScanner().Scan(folder);
        document.ScannerName = Name;
        return document;
    }
}

public static class ScriptPackages
{
    public static bool IsAngular(string folder) =>
        File.Exists(Path.Combine(folder, "angular.json")) || Depends(folder, "@angular/core");

    public static bool Depends(string folder, string package)
    {
        var path = Path.Combine(folder, "package.json");
        if (!File.Exists(path))
            return false;
        try
        {
            return File.ReadAllText(path).Contains("\"" + package + "\"", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
