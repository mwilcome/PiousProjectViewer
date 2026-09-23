using System;
using System.Collections.Generic;
using System.Linq;

namespace PiousProjectViewer.Diagram;

public static class DiagramNodes
{
    public static void EnsureParents(DiagramDocument document)
    {
        var ids = document.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var missing = document.Nodes
            .Select(node => node.Parent)
            .Where(parent => parent is not null && !ids.Contains(parent))
            .Cast<string>()
            .Distinct()
            .ToList();
        while (missing.Count > 0)
        {
            var id = missing[0];
            missing.RemoveAt(0);
            if (!ids.Add(id))
                continue;
            var body = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
            var dot = body.LastIndexOf('.');
            string? parent = null;
            if (dot > 0)
            {
                var prefix = id[..(id.IndexOf(':') + 1)];
                parent = prefix + body[..dot];
                if (!ids.Contains(parent))
                    missing.Add(parent);
            }
            document.Nodes.Add(new DiagramNode
            {
                Id = id,
                Name = body[(dot + 1)..],
                Parent = parent,
                Kind = "package"
            });
        }
    }
}

public sealed class DiagramDocument
{
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public string ScannerName { get; set; } = "";
    public bool SupportsComplexity { get; set; }
    public bool SupportsCrap { get; set; }
    public bool CoverageReady { get; set; }
    public bool MutationReady { get; set; }
    public List<DiagramNode> Nodes { get; set; } = new();
    public List<DiagramEdge> Edges { get; set; } = new();
}

public sealed class DiagramNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Parent { get; set; }
    public string Kind { get; set; } = "package";
    public string? Role { get; set; }
    public string? Selector { get; set; }
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? WorstCc { get; set; }
    public double? CrapMu { get; set; }
    public double? CrapMax { get; set; }
    public double? CrapSigma { get; set; }
    public int Rank { get; set; }
    public List<DiagramMember> Members { get; set; } = new();
}

public sealed class DiagramMember
{
    public string Name { get; set; } = "";
    public int Line { get; set; }
    public int Cc { get; set; }
    public double? Coverage { get; set; }
    public double? Crap { get; set; }
    public bool IsPublic { get; set; }
    public string Kind { get; set; } = "method";
    public string? File { get; set; }
    public int? Killed { get; set; }
    public int? Survived { get; set; }
    public int? Uncovered { get; set; }
}

public sealed class DiagramEdge
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string? FromMember { get; set; }
    public string? ToMember { get; set; }
    public bool Violating { get; set; }
}
