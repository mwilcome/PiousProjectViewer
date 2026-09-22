using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PiousProjectViewer.Diagram;

public static class LevelRank
{
    public static void Apply(DiagramDocument document, string projectFolder)
    {
        var groups = Load(Path.Combine(projectFolder, ".pious", "levels.json"));
        if (groups.Count == 0)
            return;
        foreach (var node in document.Nodes)
        {
            if (node.Kind == "foreign")
                continue;
            if (RankOf(node.Id, groups) is int rank)
                node.Rank = rank;
        }
        var byId = document.Nodes.ToDictionary(node => node.Id);
        foreach (var edge in document.Edges)
        {
            if (!byId.TryGetValue(edge.From, out var from) || !byId.TryGetValue(edge.To, out var to))
                continue;
            if (from.Kind == "foreign" || to.Kind == "foreign")
                continue;
            edge.Violating = from.Rank < to.Rank;
        }
    }

    static List<string[]> Load(string path)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (!json.RootElement.TryGetProperty("innerFirst", out var groups))
                return [];
            var parsed = new List<string[]>();
            foreach (var group in groups.EnumerateArray())
            {
                parsed.Add(group.EnumerateArray()
                    .Select(item => item.GetString() ?? "")
                    .Where(name => name.Length > 0)
                    .ToArray());
            }
            return parsed;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    static int? RankOf(string id, List<string[]> groups)
    {
        var colon = id.IndexOf(':');
        var body = colon < 0 ? id : id[(colon + 1)..];
        int? best = null;
        var bestLength = -1;
        for (var rank = 0; rank < groups.Count; rank++)
        {
            foreach (var name in groups[rank])
            {
                if (body.Equals(name, StringComparison.Ordinal) || body.StartsWith(name + ".", StringComparison.Ordinal))
                {
                    if (name.Length > bestLength)
                    {
                        bestLength = name.Length;
                        best = rank;
                    }
                }
            }
        }
        return best;
    }
}
