using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PiousProjectViewer.Diagram;

public readonly record struct PackageScore(
    string Id,
    string Name,
    int Types,
    int AbstractTypes,
    int Afferent,
    int Efferent,
    double Abstractness,
    double Instability,
    double Distance)
{
    public static string Color(double distance) =>
        distance <= 0.1 ? "#3DDC97" : distance <= 0.3 ? "#F0C14A" : "#FF5C7A";

    public string Meaning()
    {
        var stable = Instability < 0.5;
        var mostlyAbstract = Abstractness >= 0.5;
        if (Distance <= 0.1)
            return stable
                ? "On the line. Stable, and mostly abstract."
                : "On the line. Unstable, and mostly concrete.";
        if (stable && !mostlyAbstract)
            return "Off the line. Stable concrete code. Other packages depend on it, and little of it is abstract.";
        if (!stable && mostlyAbstract)
            return "Off the line. Abstract, but it depends on other packages.";
        return "Off the line.";
    }
}

public static class MartinDistance
{
    public static IReadOnlyList<PackageScore> Measure(DiagramDocument document)
    {
        var types = document.Nodes.Where(IsType).Where(type => !IsTest(type)).ToList();
        var typeIds = types.Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        var names = document.Nodes.ToDictionary(node => node.Id, node => node.Name, StringComparer.Ordinal);
        var insideOf = types.GroupBy(type => type.Parent ?? "").Where(group => group.Key.Length > 0).ToList();
        var links = document.Edges
            .Where(edge => edge.From != edge.To && typeIds.Contains(edge.From) && typeIds.Contains(edge.To))
            .Select(edge => (edge.From, edge.To))
            .Distinct()
            .ToList();
        var scores = new List<PackageScore>();
        foreach (var group in insideOf)
        {
            var inside = group.Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
            var afferent = new HashSet<string>(StringComparer.Ordinal);
            var efferent = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (from, to) in links)
            {
                var fromInside = inside.Contains(from);
                var toInside = inside.Contains(to);
                if (fromInside && !toInside)
                    efferent.Add(to);
                else if (!fromInside && toInside)
                    afferent.Add(from);
            }
            var count = group.Count();
            var abstractTypes = group.Count(type => type.Abstract);
            var abstractness = count == 0 ? 0 : (double)abstractTypes / count;
            var instability = afferent.Count + efferent.Count == 0
                ? 0
                : (double)efferent.Count / (afferent.Count + efferent.Count);
            var distance = Math.Abs(abstractness + instability - 1);
            names.TryGetValue(group.Key, out var name);
            scores.Add(new PackageScore(
                group.Key,
                string.IsNullOrWhiteSpace(name) ? Leaf(group.Key) : name,
                count,
                abstractTypes,
                afferent.Count,
                efferent.Count,
                Round(abstractness),
                Round(instability),
                Round(distance)));
        }
        return scores.OrderByDescending(score => score.Distance).ThenBy(score => score.Name, StringComparer.Ordinal).ToList();
    }

    static bool IsType(DiagramNode node) =>
        node.Kind != "foreign" && !string.IsNullOrWhiteSpace(node.File);

    static bool IsTest(DiagramNode node)
    {
        var parent = node.Parent ?? "";
        var file = node.File ?? "";
        return parent.Contains(".Tests", StringComparison.Ordinal)
            || parent.EndsWith("Tests", StringComparison.Ordinal)
            || file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.Contains("/tests/", StringComparison.OrdinalIgnoreCase);
    }

    static string Leaf(string id)
    {
        var body = id.StartsWith("ns:", StringComparison.Ordinal) ? id[3..] : id;
        var dot = body.LastIndexOf('.');
        return dot < 0 ? body : body[(dot + 1)..];
    }

    static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}

public static class TypeCoupling
{
    public static int Count(DiagramDocument document, string id)
    {
        var others = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in document.Edges)
        {
            if (edge.From == id && edge.To != id && IsType(document, edge.To))
                others.Add(edge.To);
            else if (edge.To == id && edge.From != id && IsType(document, edge.From))
                others.Add(edge.From);
        }
        return others.Count;
    }

    public static string Color(int count) => count switch
    {
        <= 2 => "#3DDC97",
        <= 6 => "#F0C14A",
        _ => "#FF5C7A"
    };

    public static string Word(int count) => count switch
    {
        0 => "no links",
        1 => "1 link",
        _ => count + " links"
    };

    static bool IsType(DiagramDocument document, string id)
    {
        var node = document.Nodes.FirstOrDefault(item => item.Id == id);
        return node is not null && node.Kind != "foreign" && !string.IsNullOrWhiteSpace(node.File);
    }
}
