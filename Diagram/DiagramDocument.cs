using System.Collections.Generic;

namespace pious_project_viewer.Diagram;

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
    public int? WorstCc { get; set; }
    public double? CrapMu { get; set; }
    public double? CrapMax { get; set; }
    public double? CrapSigma { get; set; }
    public int Rank { get; set; }
}

public sealed class DiagramEdge
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool Violating { get; set; }
}
