using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PiousProjectViewer.Diagram;

public sealed class JavaScanner : ILanguageScanner
{
    static readonly Regex PackagePattern = new(@"^\s*package\s+([\w.]+)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);
    static readonly Regex TypePattern = new(@"\b(?:class|interface|enum|record)\s+(\w+)", RegexOptions.Compiled);
    static readonly Regex MethodPattern = new(@"^\s*(?:public|private|protected)\s+(?:static\s+|final\s+|synchronized\s+)*(?:[\w.<>,\[\]]+\s+)+(\w+)\s*\(", RegexOptions.Compiled);
    static readonly Regex ImportPattern = new(@"^\s*import\s+(?:static\s+)?([\w.]+)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);
    static readonly Regex DecisionPattern = new(@"\b(if|for|while|catch|case)\b|&&|\|\||\?", RegexOptions.Compiled);

    public string Name => "Java";
    public bool SupportsComplexity => true;
    public bool SupportsCrap => true;

    public bool CanScan(string folder) =>
        File.Exists(Path.Combine(folder, "pom.xml"))
        || File.Exists(Path.Combine(folder, "build.gradle"))
        || File.Exists(Path.Combine(folder, "build.gradle.kts"));

    public string TestCommand(string folder)
    {
        if (File.Exists(Path.Combine(folder, "pom.xml")))
            return "mvn -q org.jacoco:jacoco-maven-plugin:0.8.13:prepare-agent test org.jacoco:jacoco-maven-plugin:0.8.13:report";
        if (File.Exists(Path.Combine(folder, "gradlew.bat")))
            return "gradlew.bat test jacocoTestReport";
        if (File.Exists(Path.Combine(folder, "gradlew")))
            return "./gradlew test jacocoTestReport";
        return "gradle test jacocoTestReport";
    }

    public string MutateCommand(string folder) => "";

    public string? CoverageFile(string folder) => "jacoco.xml";

    public DiagramDocument Scan(string folder)
    {
        var files = Walk(folder).Where(path => !IsTest(path)).ToList();
        var types = files.SelectMany(ReadFile).ToList();
        var document = new DiagramDocument
        {
            Title = Path.GetFileName(folder),
            ScannerName = Name,
            SupportsComplexity = true,
            SupportsCrap = true
        };
        foreach (var space in types.Select(type => type.Space).Distinct())
        {
            var worst = types.Where(type => type.Space == space).SelectMany(type => type.Members).Select(member => member.Cc).DefaultIfEmpty(0).Max();
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
        var names = types.GroupBy(type => type.Name).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First().Id);
        foreach (var type in types)
        {
            foreach (Match import in ImportPattern.Matches(File.ReadAllText(type.File)))
            {
                var simple = import.Groups[1].Value.Split('.').Last();
                if (names.TryGetValue(simple, out var target) && target != type.Id)
                    document.Edges.Add(new DiagramEdge { From = type.Id, To = target });
            }
        }
        DiagramNodes.EnsureParents(document);
        MetricApply.Apply(document, folder);
        LevelRank.Apply(document, folder);
        return document;
    }

    static IEnumerable<Found> ReadFile(string file)
    {
        var lines = File.ReadAllLines(file);
        var text = string.Join('\n', lines);
        var package = PackagePattern.Match(text);
        var spaceName = package.Success ? package.Groups[1].Value : "default";
        var space = "ns:" + spaceName;
        foreach (Match match in TypePattern.Matches(text))
        {
            var name = match.Groups[1].Value;
            var line = text[..match.Index].Count(ch => ch == '\n') + 1;
            var methods = new List<DiagramMember>();
            var starts = new List<int>();
            for (var i = line; i < lines.Length; i++)
            {
                var method = MethodPattern.Match(lines[i]);
                if (method.Success)
                    starts.Add(i);
                else if (Regex.IsMatch(lines[i], @"^\s*(?:public|private|protected)?\s*" + Regex.Escape(name) + @"\s*\("))
                    starts.Add(i);
            }
            for (var i = 0; i < starts.Count; i++)
            {
                var end = i + 1 < starts.Count ? starts[i + 1] : lines.Length;
                var body = string.Join('\n', lines.Skip(starts[i]).Take(end - starts[i]));
                var named = MethodPattern.Match(lines[starts[i]]);
                methods.Add(new DiagramMember
                {
                    Name = (named.Success ? named.Groups[1].Value : name) + "()",
                    Line = starts[i] + 1,
                    Cc = 1 + DecisionPattern.Matches(body).Count,
                    IsPublic = lines[starts[i]].Contains("public", StringComparison.Ordinal),
                    Kind = "method"
                });
            }
            yield return new Found(name, space, "type:" + spaceName + "." + name, file, line, methods);
        }
    }

    static string? Parent(string space)
    {
        var name = space["ns:".Length..];
        var dot = name.LastIndexOf('.');
        return dot < 0 ? null : "ns:" + name[..dot];
    }

    static bool IsTest(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("Test.java", StringComparison.Ordinal)
        || path.EndsWith("Tests.java", StringComparison.Ordinal);

    static IEnumerable<string> Walk(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var child in Directory.EnumerateFileSystemEntries(dir))
            {
                var name = Path.GetFileName(child);
                if (Directory.Exists(child))
                {
                    if (name is not ("target" or "build" or ".git" or "node_modules"))
                        pending.Push(child);
                    continue;
                }
                if (child.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
                    yield return child;
            }
        }
    }

    sealed record Found(string Name, string Space, string Id, string File, int Line, List<DiagramMember> Members);
}
