namespace pious_project_viewer.Diagram;

public enum PaintMode
{
    Complexity,
    Crap
}

public static class BoxPaint
{
    public const string CrapNeutral = "#2E3C44";
    public const string CrapCalm = "#2F6B4F";
    public const string CrapWarning = "#8A7040";
    public const string CrapHot = "#8C3E4E";
    public const string CrapLegend = "Calm is μ+σ at or under 8. Warning runs through 20. Hot is above 20.";

    public static string Fill(DiagramNode node, PaintMode mode, bool coverageReady)
    {
        if (node.Kind == "foreign")
            return CrapNeutral;
        if (mode == PaintMode.Complexity)
            return Heat.Color(node.WorstCc);
        return CrapFill(node, coverageReady);
    }

    static string CrapFill(DiagramNode node, bool coverageReady)
    {
        if (!coverageReady || node.CrapMu is null || node.CrapSigma is null)
            return CrapNeutral;
        var band = new CrapRollup(node.CrapMu.Value, node.CrapMax ?? node.CrapMu.Value, node.CrapSigma.Value).Band;
        return BandColor(band);
    }

    static string BandColor(string band) => band switch
    {
        "calm" => CrapCalm,
        "warning" => CrapWarning,
        _ => CrapHot
    };
}
