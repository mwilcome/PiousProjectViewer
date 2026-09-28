using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PiousProjectViewer.Diagram;

public sealed class JavaScanner : ILanguageScanner
{
    static readonly Regex DecisionPattern = new(@"\b(if|for|while|catch|case)\b|&&|\|\||\?", RegexOptions.Compiled);

    public string Name => "Java";
    public bool SupportsComplexity => true;
    public bool SupportsCrap => true;
    public bool ShowsStyles => false;

    public bool CanScan(string folder) =>
        File.Exists(Path.Combine(folder, "pom.xml"))
        || File.Exists(Path.Combine(folder, "build.gradle"))
        || File.Exists(Path.Combine(folder, "build.gradle.kts"));

    public string TestCommand(string folder)
    {
        if (File.Exists(Path.Combine(folder, "pom.xml")))
            return "mvn -q org.jacoco:jacoco-maven-plugin:0.8.13:prepare-agent test org.jacoco:jacoco-maven-plugin:0.8.13:report";
        var bat = File.Exists(Path.Combine(folder, "gradlew.bat"));
        var unix = File.Exists(Path.Combine(folder, "gradlew"));
        if (OperatingSystem.IsWindows())
        {
            if (bat)
                return "gradlew.bat test jacocoTestReport";
            if (unix)
                return "./gradlew test jacocoTestReport";
        }
        else if (unix)
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
                Abstract = type.Abstract,
                Members = type.Members
            });
        }
        var names = types.GroupBy(type => type.Name).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First().Id);
        foreach (var type in types)
        {
            foreach (var imported in type.Imports)
            {
                var simple = imported.Split('.').Last();
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
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var type in JavaSource.Parse(text, file))
            yield return type;
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
            List<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(dir).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (FileWalk.IsDirectory(child))
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

    sealed record Found(string Name, string Space, string Id, string File, int Line, bool Abstract, List<DiagramMember> Members, List<string> Imports);

    static class JavaSource
    {
        static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
        {
            "public", "private", "protected", "static", "final", "abstract", "sealed", "strictfp", "native", "default", "synchronized", "transitive"
        };
        static readonly HashSet<string> Controls = new(StringComparer.Ordinal)
        {
            "if", "for", "while", "switch", "catch", "return", "throw", "new", "else", "do", "try", "assert", "break", "continue", "synchronized"
        };

        public static List<Found> Parse(string text, string file)
        {
            var tokens = Tokenize(text);
            var package = "default";
            var imports = new List<string>();
            var found = new List<Found>();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "package")
                {
                    package = Dotted(tokens, ref i);
                    continue;
                }
                if (tokens[i].Text == "import")
                {
                    var imported = Dotted(tokens, ref i);
                    if (imported.Length > 0)
                        imports.Add(imported);
                    continue;
                }
                if (tokens[i].Text == "@")
                {
                    SkipAnnotation(tokens, ref i);
                    continue;
                }
                var modifiers = new List<string>();
                while (i < tokens.Count && Modifiers.Contains(tokens[i].Text))
                    modifiers.Add(tokens[i++].Text);
                if (i >= tokens.Count || tokens[i].Text is not ("class" or "interface" or "enum" or "record"))
                    continue;
                var keyword = tokens[i].Text;
                if (i + 1 >= tokens.Count || !IsWord(tokens[i + 1]))
                    continue;
                var name = tokens[i + 1].Text;
                var line = LineAt(text, tokens[i + 1].Index);
                var open = FindBrace(tokens, i + 2);
                if (open < 0)
                    continue;
                var close = MatchBrace(tokens, open);
                var methods = Methods(text, tokens, open, close, name);
                var spaceName = package;
                found.Add(new Found(
                    name,
                    "ns:" + spaceName,
                    "type:" + spaceName + "." + name,
                    file,
                    line,
                    keyword == "interface" || modifiers.Contains("abstract"),
                    methods,
                    imports));
                i = close;
            }
            return found;
        }

        static List<DiagramMember> Methods(string text, List<Tok> tokens, int open, int close, string typeName)
        {
            var methods = new List<DiagramMember>();
            var depth = 0;
            for (var i = open + 1; i < close; i++)
            {
                if (tokens[i].Text == "{")
                {
                    depth++;
                    continue;
                }
                if (tokens[i].Text == "}")
                {
                    depth--;
                    continue;
                }
                if (depth != 0 || tokens[i].Text == "@")
                {
                    if (tokens[i].Text == "@")
                        SkipAnnotation(tokens, ref i);
                    continue;
                }
                var cursor = i;
                var isPublic = false;
                while (cursor < close && Modifiers.Contains(tokens[cursor].Text))
                {
                    if (tokens[cursor].Text == "public")
                        isPublic = true;
                    cursor++;
                }
                var paren = IndexOf(tokens, cursor, close, "(", 0);
                if (paren < 0)
                    continue;
                var name = WordBefore(tokens, paren);
                if (name.Length == 0 || Controls.Contains(name) || !IsWord(tokens[paren - 1]))
                    continue;
                var bodyAt = AfterParen(tokens, paren, close);
                var end = bodyAt;
                string body = "";
                if (bodyAt >= 0 && bodyAt < close && tokens[bodyAt].Text == "{")
                {
                    end = MatchBrace(tokens, bodyAt);
                    body = text[tokens[bodyAt].Index..Math.Min(text.Length, tokens[Math.Min(end, tokens.Count - 1)].Index + 1)];
                    i = end;
                }
                else
                    i = paren;
                methods.Add(new DiagramMember
                {
                    Name = name + "()",
                    Line = LineAt(text, tokens[paren - 1].Index),
                    Cc = 1 + DecisionPattern.Matches(body).Count,
                    IsPublic = isPublic || name == typeName,
                    Kind = "method"
                });
            }
            return methods;
        }

        static int AfterParen(List<Tok> tokens, int paren, int limit)
        {
            var depth = 0;
            for (var i = paren; i < limit; i++)
            {
                if (tokens[i].Text == "(")
                    depth++;
                else if (tokens[i].Text == ")")
                {
                    depth--;
                    if (depth == 0)
                        return i + 1;
                }
            }
            return -1;
        }

        static string WordBefore(List<Tok> tokens, int index)
        {
            for (var i = index - 1; i >= 0; i--)
            {
                if (tokens[i].Text is "<" or ">" or "," or "[" or "]" or "?" or ".")
                    continue;
                return IsWord(tokens[i]) ? tokens[i].Text : "";
            }
            return "";
        }

        static int IndexOf(List<Tok> tokens, int start, int limit, string text, int parenDepth)
        {
            var paren = 0;
            var angle = 0;
            for (var i = start; i < limit; i++)
            {
                var token = tokens[i].Text;
                if (token == "(")
                    paren++;
                else if (token == ")")
                    paren--;
                else if (token == "<")
                    angle++;
                else if (token == ">")
                    angle = Math.Max(0, angle - 1);
                else if (token == text && paren == parenDepth && angle == 0)
                    return i;
                else if (token is "{" or ";" && paren == 0 && angle == 0)
                    return -1;
            }
            return -1;
        }

        static int FindBrace(List<Tok> tokens, int start)
        {
            var paren = 0;
            for (var i = start; i < tokens.Count; i++)
            {
                var token = tokens[i].Text;
                if (token == "(")
                    paren++;
                else if (token == ")")
                    paren--;
                else if (token == "{" && paren == 0)
                    return i;
                else if (token == ";" && paren == 0)
                    return -1;
            }
            return -1;
        }

        static int MatchBrace(List<Tok> tokens, int open)
        {
            var depth = 0;
            for (var i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "{")
                    depth++;
                else if (tokens[i].Text == "}")
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return tokens.Count - 1;
        }

        static string Dotted(List<Tok> tokens, ref int i)
        {
            var parts = new List<string>();
            for (i++; i < tokens.Count && tokens[i].Text != ";"; i++)
            {
                if (IsWord(tokens[i]))
                    parts.Add(tokens[i].Text);
            }
            return string.Join(".", parts);
        }

        static void SkipAnnotation(List<Tok> tokens, ref int i)
        {
            i++;
            while (i < tokens.Count && IsWord(tokens[i]))
                i++;
            if (i < tokens.Count && tokens[i].Text == "(")
            {
                var depth = 0;
                for (; i < tokens.Count; i++)
                {
                    if (tokens[i].Text == "(")
                        depth++;
                    else if (tokens[i].Text == ")")
                    {
                        depth--;
                        if (depth == 0)
                            break;
                    }
                }
            }
            if (i < tokens.Count)
                i--;
        }

        static bool IsWord(Tok token) => token.Kind == TokKind.Word;

        static int LineAt(string text, int index) => text[..Math.Clamp(index, 0, text.Length)].Count(ch => ch == '\n') + 1;

        static List<Tok> Tokenize(string text)
        {
            var tokens = new List<Tok>();
            for (var i = 0; i < text.Length;)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    i = text.IndexOf('\n', i);
                    if (i < 0)
                        break;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                    continue;
                }
                if (c == '"')
                {
                    if (text.AsSpan(i).StartsWith("\"\"\""))
                    {
                        var end = text.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                        i = end < 0 ? text.Length : end + 3;
                    }
                    else
                        i = SkipQuote(text, i, '"');
                    continue;
                }
                if (c == '\'')
                {
                    i = SkipQuote(text, i, '\'');
                    continue;
                }
                if (char.IsLetter(c) || c is '_' or '$')
                {
                    var start = i;
                    i++;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$'))
                        i++;
                    tokens.Add(new Tok(TokKind.Word, text[start..i], start));
                    continue;
                }
                if (char.IsDigit(c))
                {
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_'))
                        i++;
                    continue;
                }
                tokens.Add(new Tok(TokKind.Symbol, c.ToString(), i));
                i++;
            }
            return tokens;
        }

        static int SkipQuote(string text, int start, char quote)
        {
            for (var i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (text[i] == quote)
                    return i + 1;
            }
            return text.Length;
        }

        enum TokKind { Word, Symbol }

        readonly record struct Tok(TokKind Kind, string Text, int Index);
    }
}
