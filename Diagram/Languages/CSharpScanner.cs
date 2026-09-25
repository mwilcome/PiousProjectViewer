using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PiousProjectViewer.Diagram;

public sealed class CSharpScanner : ILanguageScanner
{
    public string Name => "C#";
    public bool SupportsComplexity => true;
    public bool SupportsCrap => true;
    public bool ShowsStyles => false;

    public bool CanScan(string folder) =>
        Directory.Exists(folder)
        && (Directory.EnumerateFiles(folder, "*.sln").Any()
            || Directory.EnumerateFiles(folder, "*.slnx").Any()
            || Directory.EnumerateFiles(folder, "*.csproj").Any());

    public string TestCommand(string folder)
    {
        var solution = FindSolutions(folder);
        if (solution.Count == 1)
            return DotNetTest(Path.GetRelativePath(folder, solution[0]));
        var tests = FindTestProjects(folder);
        if (tests.Count == 0)
            return "";
        return string.Join(" && ", tests.Select(path => DotNetTest(Path.GetRelativePath(folder, path))));
    }

    static string DotNetTest(string relativePath) =>
        "dotnet test \"" + relativePath + "\" --collect:\"XPlat Code Coverage\"";

    public string? CoverageFile(string folder) => "coverage.cobertura.xml";

    public string MutateCommand(string folder)
    {
        if (FindSolutions(folder).Count == 0 && FindTestProjects(folder).Count == 0)
            return "";
        return "dotnet stryker --reporter json";
    }

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
        {
            var one = ScanProject(projects[0]);
            LevelRank.Apply(one, folder);
            return one;
        }

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
        MetricApply.FillParents(combined);
        combined.MutationReady = combined.Nodes.Any(node => node.Members.Any(member => member.Killed is not null));
        LevelRank.Apply(combined, folder);
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

    public static List<string> FindSolutions(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*.sln*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> FindTestProjects(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories)
            .Where(path => IsTestProject(Path.GetFileName(path)) && !IsBuildOutput(folder, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        AddRazor(root, types);
        var bySimpleName = types.GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.Select(type => type.Id).ToList());
        foreach (var type in types)
            ReadUses(type, bySimpleName);

        var coverage = CoverageReport.Find(root);
        if (coverage is not null)
            ApplyCoverage(types, coverage);
        var mutation = MutationReport.Find(root);
        if (mutation is not null)
            ApplyMutation(types, mutation);

        var document = new DiagramDocument
        {
            Title = Path.GetFileNameWithoutExtension(projectFile),
            ScannerName = "C#",
            SupportsComplexity = true,
            SupportsCrap = true,
            CoverageReady = coverage is not null,
            MutationReady = mutation is not null
        };
        AddNamespaces(document, types);
        AddTypes(document, types);
        AddEdges(document, types);
        if (coverage is null)
            MetricApply.AssumeUncovered(document);
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

    static bool IsBuildOutput(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

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
            var logic = methods.Where(method => method.Kind != "field").ToList();
            yield return new TypeFact
            {
                Id = "type:" + space + "." + type.Identifier.ValueText,
                Name = type.Identifier.ValueText,
                NamespaceId = "ns:" + space,
                File = path,
                Line = type.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                WorstCc = logic.Count == 0 ? 0 : logic.Max(method => method.Cc),
                Abstract = type is InterfaceDeclarationSyntax || type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AbstractKeyword)),
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
            if (member is MethodDeclarationSyntax method)
            {
                if (method.Body is null && method.ExpressionBody is null)
                    continue;
                spans.Add(Span(member, type, "method", Complexity(member)));
            }
            else if (member is ConstructorDeclarationSyntax constructor)
            {
                if (constructor.Body is null && constructor.ExpressionBody is null)
                    continue;
                spans.Add(Span(member, type, "method", Complexity(member)));
            }
            else if (member is PropertyDeclarationSyntax property)
            {
                var logic = property.ExpressionBody is not null
                    || property.AccessorList?.Accessors.Any(accessor => accessor.Body is not null || accessor.ExpressionBody is not null) == true;
                spans.Add(Span(member, type, logic ? "method" : "field", logic ? Complexity(member) : 0));
            }
        }
        AddFields(type, spans);
        return spans;
    }

    static void AddFields(TypeDeclarationSyntax type, List<MethodSpan> spans)
    {
        if (type is RecordDeclarationSyntax record && record.ParameterList is not null)
        {
            foreach (var parameter in record.ParameterList.Parameters)
            {
                if (parameter.Identifier.ValueText.Length == 0)
                    continue;
                var line = parameter.GetLocation().GetLineSpan();
                var hidden = parameter.Modifiers.Any(token =>
                    token.IsKind(SyntaxKind.PrivateKeyword)
                    || token.IsKind(SyntaxKind.InternalKeyword)
                    || token.IsKind(SyntaxKind.ProtectedKeyword));
                spans.Add(new MethodSpan
                {
                    Name = parameter.Identifier.ValueText,
                    StartLine = line.StartLinePosition.Line + 1,
                    EndLine = line.EndLinePosition.Line + 1,
                    Cc = 0,
                    IsPublic = !hidden,
                    Kind = "field"
                });
            }
        }
        foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
        {
            var isPublic = field.Modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword));
            foreach (var variable in field.Declaration.Variables)
            {
                var line = variable.GetLocation().GetLineSpan();
                spans.Add(new MethodSpan
                {
                    Name = variable.Identifier.ValueText,
                    StartLine = line.StartLinePosition.Line + 1,
                    EndLine = line.EndLinePosition.Line + 1,
                    Cc = 0,
                    IsPublic = isPublic,
                    Kind = "field"
                });
            }
        }
    }

    static MethodSpan Span(MemberDeclarationSyntax member, TypeDeclarationSyntax type, string kind, int cc)
    {
        var line = member.GetLocation().GetLineSpan();
        return new MethodSpan
        {
            Name = MemberName(member, type.Identifier.ValueText),
            StartLine = line.StartLinePosition.Line + 1,
            EndLine = line.EndLinePosition.Line + 1,
            Cc = cc,
            IsPublic = member.Modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword)),
            Kind = kind
        };
    }

    static string MemberName(MemberDeclarationSyntax member, string typeName) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText + Parameters(method.ParameterList),
        ConstructorDeclarationSyntax constructor => typeName + Parameters(constructor.ParameterList),
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        _ => "member"
    };

    static string Parameters(ParameterListSyntax list) =>
        "(" + string.Join(", ", list.Parameters.Select(parameter => TypeName(parameter.Type))) + ")";

    static string TypeName(TypeSyntax? syntax) => syntax switch
    {
        PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText + "<" + string.Join(", ", generic.TypeArgumentList.Arguments.Select(TypeName)) + ">",
        NullableTypeSyntax nullable => TypeName(nullable.ElementType) + "?",
        ArrayTypeSyntax array => TypeName(array.ElementType) + "[]",
        QualifiedNameSyntax qualified => TypeName(qualified.Right),
        _ => syntax?.ToString() ?? "?"
    };

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

    static void AddRazor(string root, List<TypeFact> types)
    {
        IEnumerable<string> razorFiles;
        try
        {
            razorFiles = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
                .Where(path => !IsGeneratedTree(root, path))
                .ToList();
        }
        catch (IOException)
        {
            return;
        }
        foreach (var razor in razorFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(razor);
            var dir = Path.GetDirectoryName(razor);
            var codeBehind = types.FirstOrDefault(type =>
                type.Name == stem
                && string.Equals(Path.GetDirectoryName(type.File), dir, StringComparison.OrdinalIgnoreCase)
                && (Path.GetFileName(type.File).Equals(stem + ".razor.cs", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(type.File).Equals(stem + ".cs", StringComparison.OrdinalIgnoreCase)));
            var template = new MethodSpan { Name = "template", Kind = "html", IsPublic = true, StartLine = 1, File = razor };
            if (codeBehind is not null)
            {
                codeBehind.Role = "component";
                codeBehind.Methods.Add(template);
                continue;
            }
            string text;
            try
            {
                text = File.ReadAllText(razor);
            }
            catch (IOException)
            {
                continue;
            }
            var match = Regex.Match(text, @"@namespace\s+([\w.]+)");
            var space = match.Success ? match.Groups[1].Value : RazorSpace(root, razor);
            types.Add(new TypeFact
            {
                Id = "type:" + space + "." + stem,
                Name = stem,
                NamespaceId = "ns:" + space,
                File = razor,
                Line = 1,
                Role = "component",
                Methods = [template]
            });
        }
    }

    static string RazorSpace(string root, string razor)
    {
        var relative = Path.GetRelativePath(root, Path.GetDirectoryName(razor) ?? root);
        if (relative is "." or "")
            return Path.GetFileName(root);
        return relative.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
    }

    static void ReadUses(TypeFact type, Dictionary<string, List<string>> bySimpleName)
    {
        if (type.Declaration is null)
            return;
        if (type.Declaration.BaseList is not null)
        {
            foreach (var baseType in type.Declaration.BaseList.Types)
                Remember(type, null, baseType.Type, bySimpleName);
        }
        foreach (var field in type.Declaration.Members.OfType<FieldDeclarationSyntax>())
            Remember(type, null, field.Declaration.Type, bySimpleName);
        foreach (var member in type.Declaration.Members)
        {
            var span = type.Methods.FirstOrDefault(method => method.Name == MemberName(member, type.Name));
            if (span is null || span.Kind == "field")
                continue;
            if (UsesAvalonia(member))
                type.AvaloniaMembers.Add(span.Name);
            foreach (var syntax in member.DescendantNodes().OfType<TypeSyntax>())
                Remember(type, span, syntax, bySimpleName);
            foreach (var call in member.DescendantNodes().OfType<InvocationExpressionSyntax>())
                RememberCall(type, span, call, bySimpleName);
        }
    }

    static void Remember(TypeFact type, MethodSpan? method, TypeSyntax? syntax, Dictionary<string, List<string>> bySimpleName)
    {
        var simple = SimpleName(syntax);
        if (simple is null || simple == type.Name || !bySimpleName.TryGetValue(simple, out var matches) || matches.Count != 1)
            return;
        if (matches[0] == type.Id)
            return;
        if (method is null)
            type.ProjectRefs.Add(matches[0]);
        else
            method.Refs.Add(matches[0]);
    }

    static void RememberCall(TypeFact type, MethodSpan method, InvocationExpressionSyntax call, Dictionary<string, List<string>> bySimpleName)
    {
        string? called = null;
        string? targetName = null;
        switch (call.Expression)
        {
            case IdentifierNameSyntax identifier:
                called = identifier.Identifier.ValueText;
                break;
            case MemberAccessExpressionSyntax access:
                called = access.Name.Identifier.ValueText;
                if (access.Expression is IdentifierNameSyntax target)
                    targetName = target.Identifier.ValueText;
                else if (access.Expression is not (ThisExpressionSyntax or BaseExpressionSyntax))
                    return;
                break;
            default:
                return;
        }
        if (called is null)
            return;
        if (targetName is not null && targetName != type.Name)
        {
            if (bySimpleName.TryGetValue(targetName, out var matches) && matches.Count == 1 && matches[0] != type.Id)
                method.Targets.Add((matches[0], called));
            return;
        }
        var callee = type.Methods.FirstOrDefault(other =>
            other.Kind != "field"
            && other.Name != method.Name
            && DiagramScene.DisplayName(other.Name) == called);
        if (callee is not null)
            method.Calls.Add(callee.Name);
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
            var logic = type.Methods.Where(method => method.Kind != "field").ToList();
            foreach (var method in logic)
            {
                method.Coverage = coverage.Percent(type.File, method.StartLine, method.EndLine);
                method.Crap = CrapMath.Score(method.Cc, method.Coverage);
            }
            var rollup = CrapMath.Rollup(logic.Select(method => method.Crap));
            if (rollup is null)
                continue;
            type.CrapMu = rollup.Value.Mu;
            type.CrapMax = rollup.Value.Max;
            type.CrapSigma = rollup.Value.Sigma;
        }
    }

    static void ApplyMutation(List<TypeFact> types, MutationReport mutation)
    {
        foreach (var type in types)
        {
            foreach (var method in type.Methods.Where(method => method.Kind != "field"))
            {
                var counts = mutation.Counts(type.File, method.StartLine, method.EndLine);
                method.Killed = counts.Killed;
                method.Survived = counts.Survived;
                method.Uncovered = counts.Uncovered;
            }
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
                File = type.File,
                Line = type.Line,
                WorstCc = type.WorstCc == 0 ? null : type.WorstCc,
                Role = type.Role,
                Abstract = type.Abstract,
                CrapMu = type.CrapMu,
                CrapMax = type.CrapMax,
                CrapSigma = type.CrapSigma,
                Rank = 0,
                Members = type.Methods.Select(method => new DiagramMember
                {
                    Name = method.Name,
                    Line = method.StartLine,
                    Cc = method.Cc,
                    Coverage = Round1(method.Coverage),
                    Crap = Round1(method.Crap),
                    IsPublic = method.IsPublic,
                    Kind = method.Kind,
                    File = method.File,
                    Killed = method.Killed,
                    Survived = method.Survived,
                    Uncovered = method.Uncovered
                }).ToList()
            });
        }
    }

    static double? Round1(double? value) =>
        value is null ? null : Math.Round(value.Value, 1, MidpointRounding.AwayFromZero);

    static void AddEdges(DiagramDocument document, List<TypeFact> types)
    {
        var usesAvalonia = false;
        foreach (var type in types)
        {
            var seen = new HashSet<(string From, string? FromMember, string To, string? ToMember)>();
            void Add(string? fromMember, string to, string? toMember)
            {
                if (seen.Add((type.Id, fromMember, to, toMember)))
                    document.Edges.Add(new DiagramEdge { From = type.Id, FromMember = fromMember, To = to, ToMember = toMember });
            }
            foreach (var target in type.ProjectRefs)
                Add(null, target, null);
            foreach (var method in type.Methods)
            {
                var aimed = method.Targets.Select(target => target.TypeId).ToHashSet();
                foreach (var target in method.Refs)
                {
                    if (!aimed.Contains(target))
                        Add(method.Name, target, null);
                }
                foreach (var target in method.Targets)
                    Add(method.Name, target.TypeId, target.Member);
                foreach (var call in method.Calls)
                    Add(method.Name, type.Id, call);
            }
            if (!type.UsesAvalonia && type.AvaloniaMembers.Count == 0)
                continue;
            usesAvalonia = true;
            if (type.AvaloniaMembers.Count == 0)
                Add(null, "foreign:Avalonia", null);
            else
            {
                foreach (var name in type.AvaloniaMembers)
                    Add(name, "foreign:Avalonia", null);
            }
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
        public int Line { get; init; }
        public int WorstCc { get; init; }
        public bool Abstract { get; init; }
        public string? Role { get; set; }
        public double? CrapMu { get; set; }
        public double? CrapMax { get; set; }
        public double? CrapSigma { get; set; }
        public bool UsesAvalonia { get; init; }
        public TypeDeclarationSyntax? Declaration { get; init; }
        public List<MethodSpan> Methods { get; init; } = new();
        public HashSet<string> ProjectRefs { get; } = new();
        public List<string> AvaloniaMembers { get; } = new();
    }

    sealed class MethodSpan
    {
        public string Name { get; init; } = "";
        public int StartLine { get; init; }
        public int EndLine { get; init; }
        public int Cc { get; init; }
        public bool IsPublic { get; init; }
        public string Kind { get; init; } = "method";
        public string? File { get; init; }
        public double? Coverage { get; set; }
        public double? Crap { get; set; }
        public int? Killed { get; set; }
        public int? Survived { get; set; }
        public int? Uncovered { get; set; }
        public HashSet<string> Refs { get; } = new();
        public HashSet<string> Calls { get; } = new();
        public List<(string TypeId, string Member)> Targets { get; } = new();
    }
}
