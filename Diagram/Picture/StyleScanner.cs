using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PiousProjectViewer.Diagram;

public sealed class StylePicture
{
    public List<StyleSheet> Sheets { get; set; } = [];
    public List<StyleTemplate> Templates { get; set; } = [];
    public List<StyleSelector> Selectors { get; set; } = [];
}

public sealed class StyleSheet
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public bool Global { get; set; }
    public string? TemplateId { get; set; }
}

public sealed class StyleTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public List<string> Classes { get; set; } = [];
    public List<string> Unstyled { get; set; } = [];
}

public sealed class StyleSelector
{
    public string Name { get; set; } = "";
    public string SheetId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Mark { get; set; } = "one home";
    public string Color { get; set; } = "#3DDC97";
    public List<string> Hits { get; set; } = [];
}

public static class StyleScanner
{
    static readonly string[] Skip = ["node_modules", "dist", ".git", "coverage", ".angular", "bin", "obj"];
    static readonly Regex RulePattern = new(@"([^{}/]+)\{([^{}]*)\}", RegexOptions.Compiled);
    static readonly Regex ClassInSelector = new(@"\.([A-Za-z_][\w-]*)", RegexOptions.Compiled);
    static readonly Regex ClassInHtml = new(@"class\s*=\s*[""']([^""']+)[""']|\[class\.([A-Za-z_][\w-]*)\]", RegexOptions.Compiled);
    static readonly Regex DeclPattern = new(@"([A-Za-z-]+)\s*:\s*([^;]+);", RegexOptions.Compiled);

    public static StylePicture Scan(string folder)
    {
        var sheets = new List<(StyleSheet Sheet, string Text)>();
        var templates = new List<(StyleTemplate Template, HashSet<string> Classes)>();
        var seenSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GlobalFiles(folder))
            AddSheet(path, true, null);
        foreach (var path in Walk(folder, ".scss").Concat(Walk(folder, ".css")))
        {
            if (seenSheets.Contains(path))
                continue;
            var html = SiblingHtml(path);
            AddSheet(path, false, html);
        }
        foreach (var html in Walk(folder, ".html"))
        {
            if (templates.Any(item => item.Template.File.Equals(html, StringComparison.OrdinalIgnoreCase)))
                continue;
            var classes = ClassesIn(File.ReadAllText(html));
            templates.Add((new StyleTemplate
            {
                Id = "html:" + Relative(folder, html),
                Name = Path.GetFileName(html),
                File = html,
                Classes = classes.OrderBy(name => name, StringComparer.Ordinal).ToList()
            }, classes));
        }

        var rules = new List<Rule>();
        foreach (var (sheet, text) in sheets)
        {
            foreach (Match match in RulePattern.Matches(Strip(text)))
            {
                var selector = PrimaryClass(match.Groups[1].Value);
                var body = Body(match.Groups[2].Value);
                if (selector is null || body.Count == 0)
                    continue;
                var hits = templates.Where(item => item.Classes.Contains(selector)).Select(item => item.Template.Id).ToList();
                rules.Add(new Rule(selector, sheet, body, hits));
            }
        }
        Mark(rules);
        var known = rules.Select(rule => rule.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (template, classes) in templates)
            template.Unstyled = classes.Where(name => !known.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();

        return new StylePicture
        {
            Sheets = sheets.Select(item => item.Sheet).ToList(),
            Templates = templates.Select(item => item.Template).ToList(),
            Selectors = rules.Select(rule => new StyleSelector
            {
                Name = "." + rule.Name,
                SheetId = rule.Sheet.Id,
                FileName = rule.Sheet.Name,
                Mark = rule.Mark,
                Color = rule.Color,
                Hits = rule.Hits
            }).OrderBy(item => item.Color == "#FF5C7A" ? 0 : item.Color == "#F0C14A" ? 1 : 2).ThenBy(item => item.Name, StringComparer.Ordinal).ToList()
        };

        void AddSheet(string path, bool global, string? html)
        {
            if (!seenSheets.Add(path) || !File.Exists(path))
                return;
            var id = (global ? "global:" : "local:") + Relative(folder, path);
            string? templateId = html is null ? null : "html:" + Relative(folder, html);
            sheets.Add((new StyleSheet
            {
                Id = id,
                Name = Path.GetFileName(path),
                File = path,
                Global = global,
                TemplateId = templateId
            }, File.ReadAllText(path)));
            if (html is not null && File.Exists(html) && templates.All(item => !item.Template.File.Equals(html, StringComparison.OrdinalIgnoreCase)))
            {
                var classes = ClassesIn(File.ReadAllText(html));
                templates.Add((new StyleTemplate
                {
                    Id = "html:" + Relative(folder, html),
                    Name = Path.GetFileName(html),
                    File = html,
                    Classes = classes.OrderBy(name => name, StringComparer.Ordinal).ToList()
                }, classes));
            }
        }
    }

    static void Mark(List<Rule> rules)
    {
        var byName = rules.GroupBy(rule => rule.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Select(rule => rule.Sheet.Id).Distinct().Count(), StringComparer.Ordinal);
        var chunks = rules.Where(rule => rule.Body.Count >= 6)
            .GroupBy(rule => string.Join(";", rule.Body))
            .Where(group => group.Select(rule => rule.Sheet.Id).Distinct(StringComparer.Ordinal).Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();
        foreach (var rule in rules)
        {
            var own = rule.Sheet.TemplateId;
            var leaks = !rule.Sheet.Global && own is not null && rule.Hits.Any(hit => hit != own);
            if (byName[rule.Name] > 1)
                Set(rule, "same name in " + byName[rule.Name] + " files", "#FF5C7A");
            else if (chunks.Contains(rule))
                Set(rule, "same chunk, different name", "#FF5C7A");
            else if (rule.Hits.Count == 0)
                Set(rule, "hits nothing", "#F0C14A");
            else if (leaks)
                Set(rule, "local, hits other templates", "#F0C14A");
            else if (rule.Sheet.Global && rule.Hits.Count == 1)
                Set(rule, "global, one template", "#F0C14A");
            else
                Set(rule, "one home", "#3DDC97");
        }
    }

    static void Set(Rule rule, string mark, string color)
    {
        rule.Mark = mark;
        rule.Color = color;
    }

    static List<string> Body(string text)
    {
        var lines = new List<string>();
        foreach (Match match in DeclPattern.Matches(text))
        {
            var value = Regex.Replace(match.Groups[2].Value, @"\s+", " ").Trim().TrimEnd(';');
            if (value.Length == 0 || match.Groups[1].Value.StartsWith("--", StringComparison.Ordinal))
                continue;
            lines.Add(match.Groups[1].Value.Trim() + ":" + value);
        }
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    static string? PrimaryClass(string selector)
    {
        selector = selector.Trim();
        if (selector.Length == 0 || selector.StartsWith('@') || selector.StartsWith(':'))
            return null;
        var classes = ClassInSelector.Matches(selector);
        return classes.Count == 0 ? null : classes[^1].Groups[1].Value;
    }

    static HashSet<string> ClassesIn(string html)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ClassInHtml.Matches(html))
        {
            if (match.Groups[2].Success)
                names.Add(match.Groups[2].Value);
            foreach (var part in match.Groups[1].Value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                names.Add(part);
        }
        return names;
    }

    static string Strip(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(text, @"//.*?$", " ", RegexOptions.Multiline);
    }

    static IEnumerable<string> GlobalFiles(string folder)
    {
        var angular = Path.Combine(folder, "angular.json");
        if (File.Exists(angular))
        {
            foreach (Match block in Regex.Matches(File.ReadAllText(angular), @"""styles""\s*:\s*\[(.*?)\]", RegexOptions.Singleline))
            {
                foreach (Match file in Regex.Matches(block.Groups[1].Value, @"""([^""]+\.(?:scss|css))"""))
                {
                    var path = Path.GetFullPath(Path.Combine(folder, file.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(path))
                        yield return path;
                }
            }
        }
    }

    static IEnumerable<string> Walk(string folder, string extension)
    {
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(current);
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var entry in children)
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    if (!Skip.Contains(name, StringComparer.OrdinalIgnoreCase))
                        pending.Push(entry);
                    continue;
                }
                if (entry.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    yield return entry;
            }
        }
    }

    static string? SiblingHtml(string style)
    {
        var stem = Path.GetFileName(style);
        var marker = stem.IndexOf(".component.", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return null;
        var candidate = Path.Combine(Path.GetDirectoryName(style)!, stem[..marker] + ".component.html");
        return File.Exists(candidate) ? candidate : null;
    }

    static string Relative(string folder, string path)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..].Replace('\\', '/') : full;
    }

    sealed class Rule
    {
        public Rule(string name, StyleSheet sheet, List<string> body, List<string> hits)
        {
            Name = name;
            Sheet = sheet;
            Body = body;
            Hits = hits;
        }

        public string Name { get; }
        public StyleSheet Sheet { get; }
        public List<string> Body { get; }
        public List<string> Hits { get; }
        public string Mark { get; set; } = "one home";
        public string Color { get; set; } = "#3DDC97";
    }
}

public static class StyleFiles
{
    public const string DiagramName = "styles-diagram.json";

    public static string DiagramPath(string folder) => Path.Combine(folder, ".pious", DiagramName);

    public static void Write(string folder, StylePicture picture)
    {
        var pious = Path.Combine(folder, ".pious");
        if (!Directory.Exists(pious))
            return;
        File.WriteAllText(DiagramPath(folder), JsonSerializer.Serialize(picture, new JsonSerializerOptions { WriteIndented = true }));
    }
}
