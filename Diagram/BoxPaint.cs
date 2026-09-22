namespace pious_project_viewer.Diagram;

public enum PaintMode
{
    Complexity,
    Crap
}

public static class BoxPaint
{
    public const string CrapNeutral = "#E4E0D8";
    public const string CrapCalm = "#8FB089";
    public const string CrapWarning = "#E0C36A";
    public const string CrapHot = "#D0928A";

    public static string Fill(DiagramNode node, PaintMode mode, bool coverageReady)
    {
        if (node.Kind == "foreign")
            return "#E7E2DA";
        if (mode == PaintMode.Complexity)
            return Heat.Color(node.WorstCc);
        if (!coverageReady || node.CrapMu is null || node.CrapSigma is null)
            return CrapNeutral;
        var band = new CrapRollup(node.CrapMu.Value, node.CrapMax ?? node.CrapMu.Value, node.CrapSigma.Value).Band;
        return band switch
        {
            "calm" => CrapCalm,
            "warning" => CrapWarning,
            _ => CrapHot
        };
    }
}
