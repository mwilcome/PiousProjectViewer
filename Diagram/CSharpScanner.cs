using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace pious_project_viewer.Diagram;

public sealed class CSharpScanner : ILanguageScanner
{
    public string Name => "C#";
    public bool SupportsComplexity => true;
    public bool SupportsCrap => true;

    public bool CanScan(string folder) => FindProjects(folder).Count > 0;

    public DiagramDocument Scan(string folder)
    {
        var projects = FindProjects(folder);
        if (projects.Count == 0)
        {
            return new DiagramDocument
            {
                Title = Path.GetFileName(folder),
                Note = "No C# project in this folder."
            };
        }
        if (projects.Count == 1)
            return ScanProject(projects[0]);

        var combined = new DiagramDocument
        {
            Title = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ScannerName = Name,
            SupportsComplexity = true,
            SupportsCrap = true
        };
        foreach (var project in projects)
            Graft(combined, ScanProject(project));
        combined.CoverageReady = combined.Nodes.Any(node => node.CrapMu is not null);
        return combined;
    }

    public static string? FindAppProject(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var match = dir.GetFiles("*.csproj").FirstOrDefault(file => !IsTestProject(file.Name));
            if (match is not null)
                return match.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    public static List<string> FindProjects(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsTestProject(Path.GetFileName(path)) && !IsGeneratedTree(folder, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static DiagramDocument ScanProject(string projectFile)
    {
        var root = Path.GetDirectoryName(projectFile) ?? ".";
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedTree(root, path))
            .ToList();
        var types = files.SelectMany(ReadTypes).ToList();
        var bySimpleName = types.GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.Select(type => type.Id).ToList());
        foreach (var type in types)
            ReadDeclared(type, bySimpleName);

        var coverage = CoverageReport.Find(root);
        if (coverage is not null)
            ApplyCoverage(types, coverage);

        var document = new DiagramDocument
        {
            Title = Path.GetFileNameWithoutExtension(projectFile),
            ScannerName = "C#",
            SupportsComplexity = true,
            SupportsCrap = true,
            CoverageReady = coverage is not null
        };
        AddNamespaces(document, types);
        AddTypes(document, types);
        AddEdges(document, types);
        return document;
    }

    static void Graft(DiagramDocument into, DiagramDocument part)
    {
        var projectId = "proj:" + part.Title;
        var scores = part.Nodes.Where(node => node.Kind != "foreign" && node.WorstCc is not null).Select(node => node.WorstCc!.Value).ToList();
        into.Nodes.Add(new DiagramNode
        {
            Id = projectId,
            Name = part.Title,
            Kind = "package",
            WorstCc = scores.Count == 0 ? null : scores.Max(),
            Rank = 0
        });
        foreach (var node in part.Nodes)
        {
            if (node.Kind == "foreign")
            {
                if (into.Nodes.All(existing => existing.Id != node.Id))
                    into.Nodes.Add(node);
                continue;
            }
            if (node.Parent is null)
                node.Parent = projectId;
            else
                node.Parent = projectId + "/" + node.Parent;
            node.Id = projectId + "/" + node.Id;
            into.Nodes.Add(node);
        }
        foreach (var edge in part.Edges)
        {
            edge.From = edge.From.StartsWith("foreign:", StringComparison.Ordinal) ? edge.From : projectId + "/" + edge.From;
            edge.To = edge.To.StartsWith("foreign:", StringComparison.Ordinal) ? edge.To : projectId + "/" + edge.To;
            into.Edges.Add(edge);
        }
    }

    static bool IsTestProject(string fileName) =>
        fileName.Contains(".Tests.", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase);

    static bool IsGeneratedTree(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static IEnumerable<TypeFact> ReadTypes(string path)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
        foreach (var type in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (type.Parent is TypeDeclarationSyntax)
                continue;
            var space = NamespaceOf(type);
            var methods = MethodSpans(type);
            yield return new TypeFact
            {
                Id = "type:" + space + "." + type.Identifier.ValueText,
                Name = type.Identifier.ValueText,
                NamespaceId = "ns:" + space,
                File = path,
                WorstCc = methods.Count == 0 ? 0 : methods.Max(method => method.Cc),
                UsesAvalonia = FileUsesAvalonia(type) || UsesAvalonia(type),
                Declaration = type,
                Methods = methods
            };
        }
    }

    static List<MethodSpan> MethodSpans(TypeDeclarationSyntax type)
    {
        var spans = new List<MethodSpan>();
        foreach (var member in type.Members)
        {
            if (member is not (MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax))
                continue;
            var line = member.GetLocation().GetLineSpan();
            spans.Add(new MethodSpan(line.StartLinePosition.Line + 1, line.EndLinePosition.Line + 1, Complexity(member)));
        }
        return spans;
    }

    static string NamespaceOf(SyntaxNode node)
    {
        var names = new List<string>();
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax space)
                names.Add(space.Name.ToString());
        }
        names.Reverse();
        return names.Count == 0 ? "global" : string.Join(".", names);
    }

    public static int Complexity(SyntaxNode member)
    {
        var score = 1;
        foreach (var node in member.DescendantNodes())
        {
            if (node is IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or DoStatementSyntax or CatchClauseSyntax or ConditionalExpressionSyntax or SwitchExpressionArmSyntax)
                score++;
            else if (node is BinaryExpressionSyntax binary && IsDecision(binary))
                score++;
            else if (node is CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax)
                score++;
        }
        return score;
    }

    static bool IsDecision(BinaryExpressionSyntax binary) =>
        binary.IsKind(SyntaxKind.LogicalAndExpression)
        || binary.IsKind(SyntaxKind.LogicalOrExpression)
        || binary.IsKind(SyntaxKind.CoalesceExpression);

    static bool FileUsesAvalonia(SyntaxNode node) =>
        node.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Any(directive => directive.Name?.ToString().StartsWith("Avalonia", StringComparison.Ordinal) == true);

    static bool UsesAvalonia(SyntaxNode node) =>
        node.DescendantNodes().Any(child =>
            child is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "Avalonia"
            || child is QualifiedNameSyntax qualified && qualified.ToString().StartsWith("Avalonia", StringComparison.Ordinal));

    static void ReadDeclared(TypeFact type, Dictionary<string, List<string>> bySimpleName)
    {
        if (type.Declaration.BaseList is not null)
        {
            foreach (var baseType in type.Declaration.BaseList.Types)
                Remember(type, baseType.Type, bySimpleName);
        }
        foreach (var field in type.Declaration.Members.OfType<FieldDeclarationSyntax>())
            Remember(type, field.Declaration.Type, bySimpleName);
        foreach (var constructor in type.Declaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
                Remember(type, parameter.Type, bySimpleName);
        }
    }

    static void Remember(TypeFact type, TypeSyntax? syntax, Dictionary<string, List<string>> bySimpleName)
    {
        var simple = SimpleName(syntax);
        if (simple is null || simple == type.Name || !bySimpleName.TryGetValue(simple, out var matches) || matches.Count != 1)
            return;
        if (matches[0] != type.Id)
            type.ProjectRefs.Add(matches[0]);
    }

    static string? SimpleName(TypeSyntax? syntax) => syntax switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        QualifiedNameSyntax qualified => SimpleName(qualified.Right),
        NullableTypeSyntax nullable => SimpleName(nullable.ElementType),
        ArrayTypeSyntax array => SimpleName(array.ElementType),
        _ => null
    };

    static void ApplyCoverage(List<TypeFact> types, CoverageReport coverage)
    {
        foreach (var type in types)
        {
            var scores = type.Methods
                .Select(method => CrapMath.Score(method.Cc, coverage.Percent(type.File, method.StartLine, method.EndLine)))
                .ToList();
            var rollup = CrapMath.Rollup(scores);
            if (rollup is null)
                continue;
            type.CrapMu = rollup.Value.Mu;
            type.CrapMax = rollup.Value.Max;
            type.CrapSigma = rollup.Value.Sigma;
        }
    }

    static void AddNamespaces(DiagramDocument document, List<TypeFact> types)
    {
        var spaces = types.Select(type => type.NamespaceId).Distinct().ToList();
        foreach (var space in spaces.ToList())
        {
            var parent = ParentNamespace(space);
            while (parent is not null && spaces.All(existing => existing != parent))
            {
                spaces.Add(parent);
                parent = ParentNamespace(parent);
            }
        }

        foreach (var space in spaces)
        {
            var typesHere = types.Where(type => type.NamespaceId == space || type.NamespaceId.StartsWith(space + ".", StringComparison.Ordinal)).ToList();
            var complexity = typesHere.Select(type => type.WorstCc).Where(score => score > 0).ToList();
            var rollup = document.CoverageReady
                ? CrapMath.Rollup(typesHere.Select(type => type.CrapMu))
                : null;
            document.Nodes.Add(new DiagramNode
            {
                Id = space,
                Name = Leaf(space),
                Parent = ParentNamespace(space),
                Kind = "package",
                WorstCc = complexity.Count == 0 ? null : complexity.Max(),
                CrapMu = rollup?.Mu,
                CrapMax = rollup?.Max,
                CrapSigma = rollup?.Sigma,
                Rank = 0
            });
        }
    }

    static string Leaf(string space)
    {
        var name = space.StartsWith("ns:", StringComparison.Ordinal) ? space[3..] : space;
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    static string? ParentNamespace(string space)
    {
        var name = space.StartsWith("ns:", StringComparison.Ordinal) ? space[3..] : space;
        var dot = name.LastIndexOf('.');
        return dot < 0 ? null : "ns:" + name[..dot];
    }

    static void AddTypes(DiagramDocument document, List<TypeFact> types)
    {
        foreach (var type in types)
        {
            document.Nodes.Add(new DiagramNode
            {
                Id = type.Id,
                Name = type.Name,
                Parent = type.NamespaceId,
                Kind = "package",
                WorstCc = type.WorstCc == 0 ? null : type.WorstCc,
                CrapMu = type.CrapMu,
                CrapMax = type.CrapMax,
                CrapSigma = type.CrapSigma,
                Rank = 0
            });
        }
    }

    static void AddEdges(DiagramDocument document, List<TypeFact> types)
    {
        var usesAvalonia = false;
        foreach (var type in types)
        {
            foreach (var target in type.ProjectRefs)
                document.Edges.Add(new DiagramEdge { From = type.Id, To = target });
            if (!type.UsesAvalonia)
                continue;
            usesAvalonia = true;
            document.Edges.Add(new DiagramEdge { From = type.Id, To = "foreign:Avalonia" });
        }
        if (!usesAvalonia)
            return;
        if (document.Nodes.All(node => node.Id != "foreign:Avalonia"))
        {
            document.Nodes.Add(new DiagramNode
            {
                Id = "foreign:Avalonia",
                Name = "Avalonia",
                Kind = "foreign",
                Rank = 0
            });
        }
    }

    sealed class TypeFact
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string NamespaceId { get; init; } = "";
        public string File { get; init; } = "";
        public int WorstCc { get; init; }
        public double? CrapMu { get; set; }
        public double? CrapMax { get; set; }
        public double? CrapSigma { get; set; }
        public bool UsesAvalonia { get; init; }
        public TypeDeclarationSyntax Declaration { get; init; } = null!;
        public List<MethodSpan> Methods { get; init; } = new();
        public HashSet<string> ProjectRefs { get; } = new();
    }

    readonly record struct MethodSpan(int StartLine, int EndLine, int Cc);
}
