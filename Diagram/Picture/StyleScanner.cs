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
    public string Body { get; set; } = "";
    public string Home { get; set; } = "pages";
    public List<string> Hits { get; set; } = [];
}

public sealed class StyleProblem
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Brief { get; init; } = "";
    public string Kind { get; init; } = "";
    public int Pain { get; init; }
    public string Color { get; init; } = "#F0C14A";
    public List<string> Names { get; init; } = [];
}

public static class StyleProblems
{
    public static List<StyleProblem> Build(StylePicture picture)
    {
        var problems = new List<StyleProblem>();
        var selectors = picture.Selectors;
        var homes = selectors.GroupBy(selector => selector.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(selector => selector.SheetId).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
        foreach (var canon in selectors
            .Where(selector => homes.GetValueOrDefault(selector.Name) == 1 && selector.Hits.Count >= 3 && selector.Body.Length > 0
                && picture.Sheets.FirstOrDefault(sheet => sheet.Id == selector.SheetId) is { } canonSheet && StyleHomes.IsShared(canonSheet))
            .GroupBy(selector => selector.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(selector => selector.Hits.Count))
        {
            var bare = canon.Name.TrimStart('.');
            var copies = selectors.Where(selector =>
                !string.Equals(selector.Name, canon.Name, StringComparison.Ordinal)
                && selector.SheetId != canon.SheetId
                && CloseBody(selector.Body, canon.Body)
                && selector.Hits.All(hit => picture.Templates.FirstOrDefault(template => template.Id == hit)?.Classes.Contains(bare) != true))
                .ToList();
            if (copies.Count == 0)
                continue;
            var names = copies.Select(selector => selector.Name).Distinct(StringComparer.Ordinal).ToList();
            problems.Add(new StyleProblem
            {
                Id = "problem:share:" + canon.Name,
                Kind = "fold",
                Title = canon.Name,
                Brief = "shared in " + canon.FileName + " · " + copies.Count + (copies.Count == 1 ? " renamed copy" : " renamed copies"),
                Detail = "The shared class already exists. These pages built their own.",
                Pain = copies.Count * Math.Max(1, canon.Hits.Count),
                Color = Heat(copies.Count),
                Names = names.Prepend(canon.Name).ToList()
            });
        }
        foreach (var group in selectors
            .Where(selector => homes.GetValueOrDefault(selector.Name) > 1)
            .GroupBy(selector => selector.Name, StringComparer.Ordinal))
        {
            var defs = group.ToList();
            var files = defs.Select(selector => selector.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var hits = defs.SelectMany(selector => selector.Hits).Distinct(StringComparer.Ordinal).Count();
            var sharedSheet = defs.Select(selector => picture.Sheets.FirstOrDefault(sheet => sheet.Id == selector.SheetId))
                .FirstOrDefault(sheet => sheet is not null && StyleHomes.IsShared(sheet));
            var shared = sharedSheet is not null;
            problems.Add(new StyleProblem
            {
                Id = (shared ? "problem:fold:" : "problem:promote:") + group.Key,
                Kind = shared ? "fold" : "promote",
                Title = group.Key,
                Brief = shared
                    ? "shared in " + sharedSheet!.Name + " · " + Math.Max(0, files - 1) + " locals"
                    : "no shared file · " + files + " files · " + hits + (hits == 1 ? " template" : " templates"),
                Detail = shared
                    ? "A shared file already has this name. Other files define it too."
                    : "This name is copied and no shared file owns it.",
                Pain = files * Math.Max(1, hits),
                Color = Heat(files),
                Names = [group.Key]
            });
        }
        return problems.OrderByDescending(problem => problem.Pain).ThenBy(problem => problem.Title, StringComparer.Ordinal).ToList();
    }

    public static List<(string Name, string File)> Calm(StylePicture picture)
    {
        var tangled = Build(picture).SelectMany(problem => problem.Names).ToHashSet(StringComparer.Ordinal);
        return picture.Selectors
            .Where(selector => !tangled.Contains(selector.Name))
            .GroupBy(selector => selector.Name, StringComparer.Ordinal)
            .Select(group => (group.Key, group.First().FileName))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
    }

    static string Heat(int files) => files >= 6 ? "#FF5C7A" : files >= 3 ? "#F0C14A" : "#8B93A7";

    static bool CloseBody(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return false;
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;
        var a = Props(left);
        var b = Props(right);
        if (a.Count < 4 || b.Count < 4)
            return false;
        var keys = a.Keys.Union(b.Keys, StringComparer.Ordinal).ToList();
        var shared = keys.Count(key => a.ContainsKey(key) && b.ContainsKey(key));
        var diffs = keys.Count(key => !a.TryGetValue(key, out var av) || !b.TryGetValue(key, out var bv) || av != bv);
        return shared >= 4 && diffs <= 2;
    }

    static Dictionary<string, string> Props(string body)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
                continue;
            map[part[..colon]] = part[(colon + 1)..].Trim();
        }
        return map;
    }
}

public static class StyleHomes
{
    public static readonly string[] Order = ["helpers", "defaults", "pieces", "layout", "pages", "themes", "vendors"];

    public static string Label(string home) => home switch
    {
        "helpers" => "Helpers",
        "defaults" => "Defaults",
        "pieces" => "Reusable",
        "layout" => "Layout",
        "pages" => "Pages",
        "themes" => "Themes",
        "vendors" => "Vendors",
        _ => home
    };

    public static string Of(StyleSheet sheet, int hits)
    {
        var path = (sheet.File + "/" + sheet.Name).Replace('\\', '/').ToLowerInvariant();
        if (path.Contains("/abstracts/") || path.Contains("/utilities/") || path.Contains("/mixins/"))
            return "helpers";
        if (path.Contains("/vendor/") || path.Contains("/vendors/"))
            return "vendors";
        if (path.Contains("/themes/") || path.Contains("/theme/"))
            return "themes";
        if (path.Contains("/layout/") || path.Contains("/layouts/"))
            return "layout";
        if (path.Contains("/base/") || path.Contains("reset") || path.Contains("typography"))
            return "defaults";
        if (path.Contains("/pages/") || path.Contains(".page."))
            return "pages";
        if (IsShared(sheet) && hits >= 2)
            return "pieces";
        if (hits <= 1)
            return "pages";
        return "pieces";
    }

    public static bool IsShared(StyleSheet sheet)
    {
        var name = sheet.Name;
        if (name.Contains(".component.", StringComparison.OrdinalIgnoreCase))
            return false;
        if (sheet.Global || name.StartsWith('_'))
            return true;
        var path = sheet.File.Replace('\\', '/');
        return path.Contains("/styles/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/shared/", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class StyleProposal
{
    public string Summary { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string To { get; set; } = "";
    public List<string> From { get; set; } = [];
    public List<string> Drop { get; set; } = [];

    public static StyleProposal? Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StyleProposal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public static class StyleScanner
{
    static readonly string[] Skip = ["node_modules", "dist", ".git", "coverage", ".angular", "bin", "obj"];
    static readonly Regex RulePattern = new(@"([^{}/]+)\{([^{}]*)\}", RegexOptions.Compiled);
    static readonly Regex ClassInSelector = new(@"\.([A-Za-z_][\w-]*)", RegexOptions.Compiled);
    static readonly Regex ClassInHtml = new(@"class(?:Name)?\s*=\s*(?:[""']([^""']+)[""']|\{\s*[""'`]([^""'`]+)[""'`])|\[class\.([A-Za-z_][\w-]*)\]|class:([A-Za-z_][\w-]*)", RegexOptions.Compiled);
    static readonly Regex StyleBlock = new(@"<style\b[^>]*>(.*?)</style>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    static readonly Regex DeclPattern = new(@"([A-Za-z-]+)\s*:\s*([^;]+);", RegexOptions.Compiled);

    public static StylePicture Scan(string folder)
    {
        var sheets = new List<(StyleSheet Sheet, string Text)>();
        var templates = new List<(StyleTemplate Template, HashSet<string> Classes)>();
        var seenSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GlobalFiles(folder))
            AddSheet(path, true, null, null);
        foreach (var path in Walk(folder, ".scss").Concat(Walk(folder, ".css")))
        {
            if (seenSheets.Contains(path))
                continue;
            var html = SiblingMarkup(path);
            AddSheet(path, false, html, null);
        }
        foreach (var markup in MarkupFiles(folder))
        {
            if (templates.Any(item => item.Template.File.Equals(markup, StringComparison.OrdinalIgnoreCase)))
                continue;
            string text;
            try
            {
                text = File.ReadAllText(markup);
            }
            catch (IOException)
            {
                continue;
            }
            var style = StyleText(text);
            if (style is not null)
                AddSheet(markup, false, null, style);
            var classes = ClassesIn(text);
            templates.Add((new StyleTemplate
            {
                Id = "html:" + Relative(folder, markup),
                Name = Path.GetFileName(markup),
                File = markup,
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
                Body = string.Join(";", rule.Body),
                Home = StyleHomes.Of(rule.Sheet, rule.Hits.Count),
                Hits = rule.Hits
            }).OrderBy(item => item.Color == "#FF5C7A" ? 0 : item.Color == "#F0C14A" ? 1 : 2).ThenBy(item => item.Name, StringComparer.Ordinal).ToList()
        };

        void AddSheet(string path, bool global, string? html, string? text)
        {
            if (!seenSheets.Add(path) || !File.Exists(path))
                return;
            var id = (global ? "global:" : "local:") + Relative(folder, path);
            string? templateId = html is null ? null : "html:" + Relative(folder, html);
            var body = text;
            if (body is null)
            {
                try
                {
                    body = File.ReadAllText(path);
                }
                catch (IOException)
                {
                    return;
                }
            }
            sheets.Add((new StyleSheet
            {
                Id = id,
                Name = Path.GetFileName(path),
                File = path,
                Global = global,
                TemplateId = templateId
            }, body));
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
            AddClassList(names, match.Groups[1].Value);
            AddClassList(names, match.Groups[2].Value);
            if (match.Groups[3].Success)
                names.Add(match.Groups[3].Value);
            if (match.Groups[4].Success)
                names.Add(match.Groups[4].Value);
        }
        return names;
    }

    static void AddClassList(HashSet<string> names, string text)
    {
        foreach (var part in text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            names.Add(part);
    }

    static string? StyleText(string text)
    {
        var matches = StyleBlock.Matches(text);
        if (matches.Count == 0)
            return null;
        return string.Join("\n", matches.Select(match => match.Groups[1].Value));
    }

    static IEnumerable<string> MarkupFiles(string folder) =>
        Walk(folder, ".html").Concat(Walk(folder, ".tsx")).Concat(Walk(folder, ".jsx")).Concat(Walk(folder, ".vue")).Concat(Walk(folder, ".svelte"));

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
        foreach (var name in new[] { "src/styles.scss", "src/styles.css", "src/index.css", "src/main.css", "src/app/globals.css" })
        {
            var path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                yield return path;
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

    static string? SiblingMarkup(string style)
    {
        var dir = Path.GetDirectoryName(style)!;
        var stem = Path.GetFileName(style);
        foreach (var suffix in new[] { ".module.scss", ".module.css", ".component.scss", ".component.css", ".scss", ".css" })
        {
            if (!stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            stem = stem[..^suffix.Length];
            break;
        }
        foreach (var ext in new[] { ".component.html", ".html", ".tsx", ".jsx", ".vue", ".svelte" })
        {
            var candidate = Path.Combine(dir, stem + ext);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
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
