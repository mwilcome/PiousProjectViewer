using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PiousProjectViewer.Diagram;

public static class DiagramPublisher
{
    public const string FolderName = ".pious";
    public const string DiagramName = "diagram.json";
    public const string MailName = "to-agent.json";
    public const string RecipeName = "project.json";
    public const string ProposalName = "proposal.json";

    public static bool Publish(string projectFolder, DiagramDocument document)
    {
        var json = Canonical(document);
        var directory = Path.Combine(projectFolder, FolderName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DiagramName);
        if (File.Exists(path) && File.ReadAllText(path) == json)
            return false;
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
        return true;
    }

    public static string DiagramPath(string projectFolder) =>
        Path.Combine(projectFolder, FolderName, DiagramName);

    public static string RecipePath(string projectFolder) =>
        Path.Combine(projectFolder, FolderName, RecipeName);

    public static string ProposalPath(string projectFolder) =>
        Path.Combine(projectFolder, FolderName, ProposalName);

    public static string SavedTest(string projectFolder)
    {
        var path = Path.Combine(projectFolder, FolderName, RecipeName);
        if (!File.Exists(path))
            return "";
        try
        {
            var recipe = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return recipe is not null && recipe.TryGetValue("test", out var test) && WorthRunning(test) ? test : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public static bool TestsAreNone(string projectFolder)
    {
        var recipe = ReadRecipe(projectFolder);
        return recipe is not null && recipe.TryGetValue("tests", out var value) && value == "none";
    }

    public static void MarkTests(string projectFolder, bool none)
    {
        var path = RecipePath(projectFolder);
        if (!File.Exists(path))
            return;
        var recipe = ReadRecipe(projectFolder);
        if (recipe is null)
            return;
        if (none)
        {
            recipe.Remove("test");
            recipe.Remove("coverageFile");
            recipe["tests"] = "none";
        }
        else
            recipe.Remove("tests");
        WriteRecipeFile(path, recipe);
    }

    static Dictionary<string, string>? ReadRecipe(string projectFolder)
    {
        var path = RecipePath(projectFolder);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static void WriteRecipeFile(string path, Dictionary<string, string> recipe)
    {
        var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true });
        if (File.Exists(path) && File.ReadAllText(path) == json)
            return;
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static void WriteRecipe(string projectFolder, string testCommand, string scanCommand, string mutateCommand, string? coverageFile)
    {
        var directory = Path.Combine(projectFolder, FolderName);
        Directory.CreateDirectory(directory);
        IgnorePious(projectFolder);
        var path = Path.Combine(directory, RecipeName);
        var keptTest = "";
        var keptCoverage = "";
        if (File.Exists(path))
        {
            try
            {
                var previous = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (previous is not null)
                {
                    var scanChanged = previous.TryGetValue("scan", out var oldScan) && oldScan != scanCommand;
                    if (!scanChanged)
                    {
                        if (previous.TryGetValue("test", out var previousTest))
                            keptTest = previousTest;
                        if (previous.TryGetValue("coverageFile", out var previousCoverage))
                            keptCoverage = previousCoverage;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
        var test = WorthRunning(testCommand) ? testCommand : keptTest;
        var coverage = WorthRunning(testCommand) ? coverageFile : keptCoverage;
        var recipe = new Dictionary<string, string>();
        if (WorthRunning(test))
            recipe["test"] = test;
        if (WorthRunning(scanCommand))
            recipe["scan"] = scanCommand;
        if (WorthRunning(mutateCommand))
            recipe["mutate"] = mutateCommand;
        if (WorthRunning(test) && !string.IsNullOrWhiteSpace(coverage))
            recipe["coverageFile"] = coverage;
        var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true });
        if (File.Exists(path) && File.ReadAllText(path) == json)
            return;
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    static bool WorthRunning(string? command) =>
        !string.IsNullOrWhiteSpace(command)
        && !command.TrimStart().StartsWith("echo ", StringComparison.OrdinalIgnoreCase);

    static void IgnorePious(string projectFolder)
    {
        var path = Path.Combine(projectFolder, ".gitignore");
        if (!File.Exists(path))
            return;
        var text = File.ReadAllText(path);
        if (text.Contains(".pious", StringComparison.Ordinal))
            return;
        if (text.Length > 0 && !text.EndsWith('\n'))
            text += "\n";
        File.WriteAllText(path, text + ".pious/\n");
    }

    public static void PostUpdated(string projectFolder) =>
        Post(projectFolder, new MailItem { Op = "diagram-updated", Path = FolderName + "/" + DiagramName });

    public static void PostRefresh(string projectFolder, string testCommand, string scanCommand) =>
        Post(projectFolder, new MailItem { Op = "refresh", Test = testCommand, Scan = scanCommand });

    public static void PostRefreshNode(string projectFolder, DiagramNode node, string testCommand, string scanCommand) =>
        Post(projectFolder, new MailItem
        {
            Op = "refresh-node",
            NodeId = node.Id,
            Name = node.Name,
            Kind = node.Kind,
            File = node.File,
            Line = node.Line,
            Test = testCommand,
            Scan = scanCommand
        });

    public static void PostProposal(string projectFolder) =>
        Post(projectFolder, new MailItem { Op = "proposal", Path = FolderName + "/" + ProposalName });

    public static void PostStyleProposal(string projectFolder, string? name) =>
        Post(projectFolder, new MailItem { Op = "proposal", Path = FolderName + "/styles-proposal.json", Name = name });

    public static void PostContext(string projectFolder, DiagramNode node) =>
        Post(projectFolder, new MailItem
        {
            Op = "context",
            NodeId = node.Id,
            Name = node.Name,
            Kind = node.Kind,
            File = node.File,
            Line = node.Line
        });

    static void Post(string projectFolder, MailItem item)
    {
        var directory = Path.Combine(projectFolder, FolderName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, MailName);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        var mail = File.Exists(path)
            ? JsonSerializer.Deserialize<MailFile>(File.ReadAllText(path), options) ?? new MailFile()
            : new MailFile();
        item.Id = mail.NextId++;
        mail.Queue.Add(item);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(mail, options));
        File.Move(temporary, path, overwrite: true);
    }

    public static string Canonical(DiagramDocument document)
    {
        var snapshot = new
        {
            title = document.Title,
            scanner = document.ScannerName,
            supportsComplexity = document.SupportsComplexity,
            supportsCrap = document.SupportsCrap,
            coverageReady = document.CoverageReady,
            mutationReady = document.MutationReady,
            nodes = document.Nodes
                .OrderBy(node => node.Id, StringComparer.Ordinal)
                .Select(node => new
                {
                    id = node.Id,
                    name = node.Name,
                    parent = node.Parent,
                    kind = node.Kind,
                    role = node.Role,
                    selector = node.Selector,
                    @abstract = node.Abstract,
                    file = node.File,
                    line = node.Line,
                    worstCc = node.WorstCc,
                    crapMu = node.CrapMu,
                    crapMax = node.CrapMax,
                    crapSigma = node.CrapSigma,
                    rank = node.Rank,
                    members = (node.Members ?? new List<DiagramMember>())
                        .OrderBy(member => member.Line)
                        .Select(member => new
                        {
                            name = member.Name,
                            line = member.Line,
                            cc = member.Cc,
                            coverage = member.Coverage,
                            crap = member.Crap,
                            isPublic = member.IsPublic,
                            kind = member.Kind,
                            file = member.File,
                            killed = member.Killed,
                            survived = member.Survived,
                            uncovered = member.Uncovered
                        })
                }),
            edges = document.Edges
                .OrderBy(edge => edge.From, StringComparer.Ordinal)
                .ThenBy(edge => edge.FromMember, StringComparer.Ordinal)
                .ThenBy(edge => edge.To, StringComparer.Ordinal)
                .ThenBy(edge => edge.ToMember, StringComparer.Ordinal)
                .Select(edge => new
                {
                    from = edge.From,
                    fromMember = edge.FromMember,
                    to = edge.To,
                    toMember = edge.ToMember,
                    violating = edge.Violating
                })
        };
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
    }

    sealed class MailFile
    {
        public int NextId { get; set; } = 1;
        public List<MailItem> Queue { get; set; } = new();
    }

    sealed class MailItem
    {
        public int Id { get; set; }
        public string Op { get; set; } = "";
        public string? Path { get; set; }
        public string? NodeId { get; set; }
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public string? File { get; set; }
        public int? Line { get; set; }
        public string? Test { get; set; }
        public string? Scan { get; set; }
    }
}
