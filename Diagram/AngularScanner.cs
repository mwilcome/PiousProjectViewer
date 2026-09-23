using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PiousProjectViewer.Diagram;

public sealed class AngularScanner : ILanguageScanner
{
    static readonly string[] Skip = ["if", "for", "while", "switch", "catch", "function", "return", "super", "new"];
    static readonly Regex ClassPattern = new(@"(?:export\s+(?:default\s+)?)?class\s+(\w+)", RegexOptions.Compiled);
    static readonly Regex MethodPattern = new(@"^\s*(?:(?:public|private|protected|async|static|readonly|override|get|set)\s+)*([A-Za-z_]\w*)\s*(?:<[^>]+>)?\s*\(", RegexOptions.Compiled);
    static readonly Regex FieldPattern = new(@"^\s*(?:public|private|protected)\s+(?:readonly\s+)?(\w+)\s*=", RegexOptions.Compiled);
    static readonly Regex ImportPattern = new(@"import\s+\{([^}]+)\}\s+from\s+['""]([^'""]+)['""]", RegexOptions.Compiled);
    static readonly Regex InjectPattern = new(@"inject\(\s*(\w+)", RegexOptions.Compiled);
    static readonly Regex SelectorPattern = new(@"selector\s*:\s*['""]([^'""]+)['""]", RegexOptions.Compiled);
    static readonly Regex DecisionPattern = new(@"\b(if|for|while|catch|case)\b|&&|\|\||\?", RegexOptions.Compiled);

    public string Name => "Angular";
    public bool SupportsComplexity => true;
    public bool SupportsCrap => true;

    public bool CanScan(string folder)
    {
        if (File.Exists(Path.Combine(folder, "angular.json")))
            return true;
        var package = Path.Combine(folder, "package.json");
        return File.Exists(package) && File.ReadAllText(package).Contains("\"@angular/core\"", StringComparison.Ordinal);
    }

    public string TestCommand(string folder) =>
        "cmd /c npx ng test --watch=false --coverage --coverage-reporters=lcov";

    public string MutateCommand(string folder) => "";

    public string? CoverageFile(string folder) => "lcov.info";

    public DiagramDocument Scan(string folder)
    {
        if (TryTypeScript(folder) is DiagramDocument parsed)
            return parsed;
        return ScanText(folder);
    }

    DiagramDocument? TryTypeScript(string folder)
    {
        var script = FindScript();
        if (script is null)
            return null;
        var start = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = Path.GetDirectoryName(script)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.GetFileName(script));
        start.ArgumentList.Add(folder);
        using var process = Process.Start(start);
        if (process is null)
            return null;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = stdout.Result;
        _ = stderr.Result;
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            return null;
        var parsed = JsonSerializer.Deserialize<AngularParse>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (parsed?.Types is null || parsed.Types.Count == 0)
            return null;
        return FromParse(folder, parsed);
    }

    static string? FindScript()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "scanners", "angular", "scan.mjs");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }

    DiagramDocument FromParse(string folder, AngularParse parsed)
    {
        var document = new DiagramDocument
        {
            Title = Path.GetFileName(folder),
            ScannerName = Name,
            SupportsComplexity = true,
            SupportsCrap = true
        };
        var types = parsed.Types;
        foreach (var spaceName in types.Select(type => type.Space).Distinct())
        {
            var space = "ns:" + spaceName;
            var here = types.Where(type => type.Space == spaceName).ToList();
            var worst = here.SelectMany(type => type.Members).Where(member => member.Kind == "method").Select(member => member.Cc).DefaultIfEmpty(0).Max();
            document.Nodes.Add(new DiagramNode
            {
                Id = space,
                Name = spaceName.Split('.').Last(),
                Parent = Parent("ns:" + spaceName),
                Kind = "package",
                WorstCc = worst == 0 ? null : worst
            });
        }
        foreach (var type in types)
        {
            var members = type.Members.Select(member => new DiagramMember
            {
                Name = member.Name,
                Line = member.Line,
                Cc = member.Cc,
                Kind = string.IsNullOrWhiteSpace(member.Kind) ? "method" : member.Kind,
                IsPublic = member.IsPublic,
                File = member.File
            }).ToList();
            var worst = members.Where(member => member.Kind == "method").Select(member => member.Cc).DefaultIfEmpty(0).Max();
            document.Nodes.Add(new DiagramNode
            {
                Id = "type:" + type.Space + "." + type.Name,
                Name = type.Name,
                Parent = "ns:" + type.Space,
                Kind = "package",
                File = type.File,
                Line = type.Line,
                Role = KnownRole(type.Role),
                Selector = string.IsNullOrWhiteSpace(type.Selector) ? null : type.Selector,
                WorstCc = worst == 0 ? null : worst,
                Members = members
            });
        }
        var names = types.GroupBy(type => type.Name).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => "type:" + group.First().Space + "." + group.Key);
        foreach (var type in types)
        {
            var from = "type:" + type.Space + "." + type.Name;
            foreach (var imported in type.Imports ?? [])
                Use(document, names, from, imported, null);
            foreach (var injected in type.Injected ?? [])
                Use(document, names, from, injected, "constructor()");
        }
        AddTemplateWires(document);
        DiagramNodes.EnsureParents(document);
        MetricApply.Apply(document, folder);
        LevelRank.Apply(document, folder);
        return document;
    }

    internal DiagramDocument ScanText(string folder)
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
            SupportsCrap = true
        };
        Add(document, types);
        var names = types.GroupBy(type => type.Name).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First().Id);
        foreach (var type in types)
        {
            var node = document.Nodes.First(item => item.Id == type.Id);
            node.Role = RoleOf(type.Text);
            node.Selector = SelectorOf(type.Text);
            foreach (Match import in ImportPattern.Matches(type.Text))
            {
                foreach (var imported in import.Groups[1].Value.Split(','))
                    Use(document, names, type.Id, imported.Trim().Split(' ')[0], null);
            }
            foreach (Match inject in InjectPattern.Matches(type.Text))
                Use(document, names, type.Id, inject.Groups[1].Value, "constructor()");
        }
        AddTemplateWires(document);
        DiagramNodes.EnsureParents(document);
        MetricApply.Apply(document, folder);
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
            methods.AddRange(Fields(lines, line));
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

    static List<DiagramMember> Fields(string[] lines, int classLine)
    {
        var fields = new List<DiagramMember>();
        for (var i = classLine; i < lines.Length; i++)
        {
            var match = FieldPattern.Match(lines[i]);
            if (!match.Success)
                continue;
            fields.Add(new DiagramMember
            {
                Name = match.Groups[1].Value,
                Line = i + 1,
                IsPublic = !lines[i].Contains("private", StringComparison.Ordinal) && !lines[i].Contains("protected", StringComparison.Ordinal),
                Kind = "field"
            });
        }
        return fields;
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

    static string? KnownRole(string? role) =>
        role is "component" or "injectable" or "directive" or "pipe" or "ngmodule" ? role : null;

    static string? RoleOf(string text)
    {
        if (text.Contains("@Component", StringComparison.Ordinal))
            return "component";
        if (text.Contains("@Injectable", StringComparison.Ordinal))
            return "injectable";
        if (text.Contains("@Directive", StringComparison.Ordinal))
            return "directive";
        if (text.Contains("@Pipe", StringComparison.Ordinal))
            return "pipe";
        if (text.Contains("@NgModule", StringComparison.Ordinal))
            return "ngmodule";
        return null;
    }

    static string? SelectorOf(string text)
    {
        var match = SelectorPattern.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    static void Use(DiagramDocument document, Dictionary<string, string> names, string from, string name, string? member)
    {
        if (!names.TryGetValue(name, out var target) || target == from)
            return;
        if (document.Edges.Any(edge => edge.From == from && edge.To == target && edge.FromMember == member))
            return;
        document.Edges.Add(new DiagramEdge { From = from, To = target, FromMember = member });
    }

    static void AddTemplateWires(DiagramDocument document)
    {
        var selectors = document.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Selector))
            .GroupBy(node => node.Selector!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        if (selectors.Count == 0)
            return;
        foreach (var node in document.Nodes)
        {
            var template = (node.Members ?? []).FirstOrDefault(member => member.Kind == "html" && !string.IsNullOrWhiteSpace(member.File));
            if (template?.File is null || !File.Exists(template.File))
                continue;
            string html;
            try
            {
                html = File.ReadAllText(template.File);
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var pair in selectors)
            {
                if (pair.Value == node.Id || !UsesTag(html, pair.Key))
                    continue;
                if (document.Edges.Any(edge => edge.From == node.Id && edge.To == pair.Value && edge.FromMember == "template"))
                    continue;
                document.Edges.Add(new DiagramEdge { From = node.Id, To = pair.Value, FromMember = "template" });
            }
        }
    }

    static bool UsesTag(string html, string selector)
    {
        var needle = "<" + selector;
        var index = 0;
        while ((index = html.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var after = index + needle.Length;
            if (after >= html.Length || !IsTagChar(html[after]))
                return true;
            index = after;
        }
        return false;
    }

    static bool IsTagChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-';

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

    sealed class AngularParse
    {
        public List<AngularTypeDto> Types { get; set; } = new();
    }

    sealed class AngularTypeDto
    {
        public string Name { get; set; } = "";
        public string Space { get; set; } = "";
        public string File { get; set; } = "";
        public int Line { get; set; }
        public string? Role { get; set; }
        public string? Selector { get; set; }
        public bool UsesAngular { get; set; }
        public List<string>? Imports { get; set; }
        public List<string>? Injected { get; set; }
        public List<AngularMemberDto> Members { get; set; } = new();
    }

    sealed class AngularMemberDto
    {
        public string Name { get; set; } = "";
        public int Line { get; set; }
        public int Cc { get; set; }
        public string Kind { get; set; } = "method";
        public bool IsPublic { get; set; }
        public string? File { get; set; }
    }
}
