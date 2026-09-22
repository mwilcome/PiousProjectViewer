using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PiousProjectViewer.Diagram;

public sealed class CoverageReport
{
    readonly Dictionary<string, Dictionary<int, int>> _lines = new(StringComparer.OrdinalIgnoreCase);

    public static CoverageReport? Find(string folder)
    {
        if (!Directory.Exists(folder))
            return null;
        var file = new[] { "coverage.cobertura.xml", "lcov.info", "jacoco.xml" }
            .SelectMany(name => Directory.EnumerateFiles(folder, name, SearchOption.AllDirectories))
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
        if (file is null)
            return null;
        var name = file.Name;
        if (name.Equals("lcov.info", StringComparison.OrdinalIgnoreCase))
            return LoadLcov(file.FullName);
        if (name.Equals("jacoco.xml", StringComparison.OrdinalIgnoreCase))
            return LoadJacoco(file.FullName);
        return Load(file.FullName);
    }

    public static CoverageReport Load(string path)
    {
        var report = new CoverageReport();
        var xml = XDocument.Load(path);
        foreach (var line in xml.Descendants("line"))
        {
            var number = (int?)line.Attribute("number");
            var hits = (int?)line.Attribute("hits");
            if (number is null || hits is null)
                continue;
            var file = line.Parent?.Attribute("filename")?.Value
                ?? line.Ancestors("class").FirstOrDefault()?.Attribute("filename")?.Value;
            if (string.IsNullOrWhiteSpace(file))
                continue;
            var key = file.Replace('\\', '/');
            if (!report._lines.TryGetValue(key, out var hitsByLine))
            {
                hitsByLine = new Dictionary<int, int>();
                report._lines[key] = hitsByLine;
            }
            hitsByLine[number.Value] = hits.Value;
        }
        return report;
    }

    public double? Percent(string sourceFile, int startLine, int endLine)
    {
        var key = _lines.Keys.FirstOrDefault(name => sourceFile.Replace('\\', '/').EndsWith(name, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(sourceFile.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (key is null || !_lines.TryGetValue(key, out var hitsByLine))
            return 0;
        var relevant = hitsByLine.Where(pair => pair.Key >= startLine && pair.Key <= endLine).ToList();
        if (relevant.Count == 0)
            return 0;
        return 100d * relevant.Count(pair => pair.Value > 0) / relevant.Count;
    }

    public static CoverageReport LoadLcov(string path)
    {
        var report = new CoverageReport();
        Dictionary<int, int>? hits = null;
        foreach (var raw in File.ReadLines(path))
        {
            if (raw.StartsWith("SF:", StringComparison.Ordinal))
            {
                var key = raw[3..].Trim().Replace('\\', '/');
                if (!report._lines.TryGetValue(key, out hits))
                {
                    hits = new Dictionary<int, int>();
                    report._lines[key] = hits;
                }
            }
            else if (raw.StartsWith("DA:", StringComparison.Ordinal) && hits is not null)
            {
                var parts = raw[3..].Split(',');
                if (parts.Length >= 2 && int.TryParse(parts[0], out var line) && int.TryParse(parts[1], out var count))
                    hits[line] = count;
            }
        }
        return report;
    }

    public static CoverageReport LoadJacoco(string path)
    {
        var report = new CoverageReport();
        var xml = XDocument.Load(path);
        foreach (var package in xml.Descendants("package"))
        {
            var packageName = (package.Attribute("name")?.Value ?? "").Replace('.', '/').Replace('\\', '/');
            foreach (var source in package.Descendants("sourcefile"))
            {
                var fileName = source.Attribute("name")?.Value ?? "";
                var key = string.IsNullOrEmpty(packageName) ? fileName : packageName + "/" + fileName;
                if (!report._lines.TryGetValue(key, out var hits))
                {
                    hits = new Dictionary<int, int>();
                    report._lines[key] = hits;
                }
                foreach (var line in source.Elements("line"))
                {
                    var number = (int?)line.Attribute("nr");
                    var covered = (int?)line.Attribute("ci") ?? 0;
                    var missed = (int?)line.Attribute("mi") ?? 0;
                    if (number is null || covered + missed == 0)
                        continue;
                    hits[number.Value] = covered;
                }
            }
        }
        return report;
    }
}
