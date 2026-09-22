using System.Collections.Generic;

namespace PiousProjectViewer.Diagram;

public sealed class DiagramDocument
{
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public string ScannerName { get; set; } = "";
    public bool SupportsComplexity { get; set; }
    public bool SupportsCrap { get; set; }
    public bool CoverageReady { get; set; }
    public List<DiagramNode> Nodes { get; set; } = new();
    public List<DiagramEdge> Edges { get; set; } = new();
}

public sealed class DiagramNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Parent { get; set; }
    public string Kind { get; set; } = "package";
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
}

public sealed class DiagramEdge
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool Violating { get; set; }
}
