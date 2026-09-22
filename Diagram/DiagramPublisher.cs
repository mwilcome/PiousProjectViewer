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

    public static void WriteRecipe(string projectFolder, string testCommand, string scanCommand)
    {
        var directory = Path.Combine(projectFolder, FolderName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, RecipeName);
        var json = JsonSerializer.Serialize(new
        {
            test = testCommand,
            scan = scanCommand,
            coverageFile = "coverage.cobertura.xml",
            mutation = "not used"
        }, new JsonSerializerOptions { WriteIndented = true });
        if (File.Exists(path) && File.ReadAllText(path) == json)
            return;
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static void PostUpdated(string projectFolder) =>
        Post(projectFolder, new MailItem { Op = "diagram-updated", Path = FolderName + "/" + DiagramName });

    public static void PostRefresh(string projectFolder, string testCommand, string scanCommand) =>
        Post(projectFolder, new MailItem { Op = "refresh", Test = testCommand, Scan = scanCommand });

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
            nodes = document.Nodes
                .OrderBy(node => node.Id, StringComparer.Ordinal)
                .Select(node => new
                {
                    id = node.Id,
                    name = node.Name,
                    parent = node.Parent,
                    kind = node.Kind,
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
                            kind = member.Kind
                        })
                }),
            edges = document.Edges
                .OrderBy(edge => edge.From, StringComparer.Ordinal)
                .ThenBy(edge => edge.To, StringComparer.Ordinal)
                .Select(edge => new
                {
                    from = edge.From,
                    to = edge.To,
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
