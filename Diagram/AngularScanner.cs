using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PiousProjectViewer.Diagram;

public sealed class AngularScanner : ILanguageScanner
{
    static readonly string[] Skip = ["if", "for", "while", "switch", "catch", "function", "return", "super", "new"];
    static readonly Regex ClassPattern = new(@"export\s+class\s+(\w+)", RegexOptions.Compiled);
    static readonly Regex MethodPattern = new(@"^\s*(?:(?:public|private|protected|async|static|readonly|override|get|set)\s+)*([A-Za-z_]\w*)\s*(?:<[^>]+>)?\s*\(", RegexOptions.Compiled);
    static readonly Regex ImportPattern = new(@"import\s+\{([^}]+)\}\s+from\s+['""]([^'""]+)['""]", RegexOptions.Compiled);
    static readonly Regex DecisionPattern = new(@"\b(if|for|while|catch|case)\b|&&|\|\||\?", RegexOptions.Compiled);

    public string Name => "Angular";
    public bool SupportsComplexity => true;
    public bool SupportsCrap => false;

    public bool CanScan(string folder)
    {
        if (File.Exists(Path.Combine(folder, "angular.json")))
            return true;
        var package = Path.Combine(folder, "package.json");
        return File.Exists(package) && File.ReadAllText(package).Contains("\"@angular/core\"", StringComparison.Ordinal);
    }

    public string TestCommand(string folder) =>
        File.Exists(Path.Combine(folder, "angular.json"))
            ? "npx ng test --watch=false --code-coverage"
            : "npm test";

    public string MutateCommand(string folder) => "echo No mutator for this language.";

    public DiagramDocument Scan(string folder)
    {
        var source = Directory.Exists(Path.Combine(folder, "src")) ? Path.Combine(folder, "src") : folder;
        var files = Walk(source, ".ts").Where(path => !path.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)).ToList();
        var types = new List<FoundType>();
        foreach (var file in files)
            types.AddRange(ReadFile(file, source));
        var document = new DiagramDocument
        {
            Title = Path.GetFileName(folder),
            ScannerName = Name,
            SupportsComplexity = true,
            SupportsCrap = false
        };
        Add(document, types);
        var names = types.GroupBy(type => type.Name).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First().Id);
        var angular = false;
        foreach (var type in types)
        {
            foreach (Match import in ImportPattern.Matches(type.Text))
            {
                if (import.Groups[2].Value.StartsWith("@angular/", StringComparison.Ordinal))
                    angular = true;
                foreach (var imported in import.Groups[1].Value.Split(','))
                {
                    var name = imported.Trim().Split(' ')[0];
                    if (names.TryGetValue(name, out var target) && target != type.Id)
                        document.Edges.Add(new DiagramEdge { From = type.Id, To = target });
                }
            }
        }
        if (angular)
        {
            document.Nodes.Add(new DiagramNode { Id = "foreign:Angular", Name = "Angular", Kind = "foreign" });
            foreach (var type in types.Where(type => type.Text.Contains("@angular/", StringComparison.Ordinal)))
                document.Edges.Add(new DiagramEdge { From = type.Id, To = "foreign:Angular" });
        }
        LevelRank.Apply(document, folder);
        return document;
    }

    static IEnumerable<FoundType> ReadFile(string file, string source)
    {
        var lines = File.ReadAllLines(file);
        var text = string.Join('\n', lines);
        var rel = Path.GetRelativePath(source, Path.GetDirectoryName(file) ?? source);
        var space = rel == "." ? "app" : rel.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        foreach (Match match in ClassPattern.Matches(text))
        {
            var name = match.Groups[1].Value;
            var line = text[..match.Index].Count(ch => ch == '\n') + 1;
            var methods = Methods(lines, line);
            yield return new FoundType(name, "ns:" + space, "type:" + space + "." + name, file, line, text, methods);
        }
    }

    static List<DiagramMember> Methods(string[] lines, int classLine)
    {
        var found = new List<(int Line, string Name)>();
        for (var i = classLine; i < lines.Length; i++)
        {
            var match = MethodPattern.Match(lines[i]);
            if (!match.Success || Skip.Contains(match.Groups[1].Value))
                continue;
            found.Add((i + 1, match.Groups[1].Value + "()"));
        }
        var members = new List<DiagramMember>();
        for (var i = 0; i < found.Count; i++)
        {
            var end = i + 1 < found.Count ? found[i + 1].Line - 1 : lines.Length;
            var body = string.Join('\n', lines.Skip(found[i].Line - 1).Take(end - found[i].Line + 1));
            members.Add(new DiagramMember
            {
                Name = found[i].Name,
                Line = found[i].Line,
                Cc = 1 + DecisionPattern.Matches(body).Count,
                IsPublic = true,
                Kind = "method"
            });
        }
        return members;
    }

    static void Add(DiagramDocument document, List<FoundType> types)
    {
        foreach (var space in types.Select(type => type.Space).Distinct())
        {
            var here = types.Where(type => type.Space == space).ToList();
            var worst = here.SelectMany(type => type.Members).Select(member => member.Cc).DefaultIfEmpty(0).Max();
            document.Nodes.Add(new DiagramNode
            {
                Id = space,
                Name = space["ns:".Length..].Split('.').Last(),
                Parent = Parent(space),
                Kind = "package",
                WorstCc = worst == 0 ? null : worst
            });
        }
        foreach (var type in types)
        {
            var worst = type.Members.Select(member => member.Cc).DefaultIfEmpty(0).Max();
            document.Nodes.Add(new DiagramNode
            {
                Id = type.Id,
                Name = type.Name,
                Parent = type.Space,
                Kind = "package",
                File = type.File,
                Line = type.Line,
                WorstCc = worst == 0 ? null : worst,
                Members = type.Members
            });
        }
    }

    static string? Parent(string space)
    {
        var name = space["ns:".Length..];
        var dot = name.LastIndexOf('.');
        return dot < 0 ? null : "ns:" + name[..dot];
    }

    static IEnumerable<string> Walk(string root, string extension)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
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
                    if (name is not ("node_modules" or "dist" or ".angular" or "coverage"))
                        pending.Push(child);
                    continue;
                }
                if (child.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    yield return child;
            }
        }
    }

    sealed record FoundType(string Name, string Space, string Id, string File, int Line, string Text, List<DiagramMember> Members);
}
