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
        var file = Directory.EnumerateFiles(folder, "coverage.cobertura.xml", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
        return file is null ? null : Load(file.FullName);
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
}
