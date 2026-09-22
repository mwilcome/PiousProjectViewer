using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PiousProjectViewer.Diagram;

public sealed class MutationReport
{
    readonly Dictionary<string, List<(int Line, string Status)>> _mutants = new(StringComparer.OrdinalIgnoreCase);

    public static MutationReport? Find(string folder)
    {
        if (!Directory.Exists(folder))
            return null;
        var file = Directory.EnumerateFiles(folder, "mutation-report.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
        return file is null ? null : Load(file.FullName);
    }

    public static MutationReport Load(string path)
    {
        var report = new MutationReport();
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        if (!json.RootElement.TryGetProperty("files", out var files))
            return report;
        foreach (var file in files.EnumerateObject())
        {
            if (!file.Value.TryGetProperty("mutants", out var mutants))
                continue;
            var key = file.Name.Replace('\\', '/');
            var rows = new List<(int Line, string Status)>();
            foreach (var mutant in mutants.EnumerateArray())
            {
                var status = mutant.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
                if (!mutant.TryGetProperty("location", out var location)
                    || !location.TryGetProperty("start", out var start)
                    || !start.TryGetProperty("line", out var lineValue)
                    || status is null)
                    continue;
                rows.Add((lineValue.GetInt32(), status));
            }
            report._mutants[key] = rows;
        }
        return report;
    }

    public (int Killed, int Survived, int Uncovered) Counts(string sourceFile, int startLine, int endLine)
    {
        var file = sourceFile.Replace('\\', '/');
        var key = _mutants.Keys.FirstOrDefault(name => file.EndsWith(name, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        if (key is null || !_mutants.TryGetValue(key, out var rows))
            return (0, 0, 0);
        var killed = 0;
        var survived = 0;
        var uncovered = 0;
        foreach (var (line, status) in rows)
        {
            if (line < startLine || line > endLine)
                continue;
            switch (status)
            {
                case "Killed":
                case "Timeout":
                    killed++;
                    break;
                case "Survived":
                    survived++;
                    break;
                case "NoCoverage":
                    uncovered++;
                    break;
            }
        }
        return (killed, survived, uncovered);
    }
}
