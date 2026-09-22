using System;
using System.Linq;

namespace PiousProjectViewer.Diagram;

public static class MetricApply
{
    public static void Apply(DiagramDocument document, string folder)
    {
        var coverage = CoverageReport.Find(folder);
        var mutation = MutationReport.Find(folder);
        document.CoverageReady = coverage is not null;
        document.MutationReady = mutation is not null;
        foreach (var node in document.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.File)))
        {
            var logic = node.Members.Where(member => member.Kind is not ("field" or "html" or "scss")).OrderBy(member => member.Line).ToList();
            for (var i = 0; i < logic.Count; i++)
            {
                var end = i + 1 < logic.Count ? logic[i + 1].Line - 1 : logic[i].Line + 40;
                if (coverage is not null)
                {
                    var percent = coverage.Percent(node.File!, logic[i].Line, end);
                    logic[i].Coverage = Round1(percent);
                    logic[i].Crap = Round1(CrapMath.Score(logic[i].Cc, percent));
                }
                if (mutation is not null)
                {
                    var counts = mutation.Counts(node.File!, logic[i].Line, end);
                    logic[i].Killed = counts.Killed;
                    logic[i].Survived = counts.Survived;
                    logic[i].Uncovered = counts.Uncovered;
                }
            }
            if (coverage is null)
                continue;
            var rollup = CrapMath.Rollup(logic.Select(member => member.Crap));
            if (rollup is null)
                continue;
            node.CrapMu = rollup.Value.Mu;
            node.CrapMax = rollup.Value.Max;
            node.CrapSigma = rollup.Value.Sigma;
        }
        foreach (var package in document.Nodes.Where(node => string.IsNullOrWhiteSpace(node.File) && node.Kind != "foreign").OrderByDescending(node => node.Id.Length))
        {
            var children = document.Nodes.Where(node => node.Parent == package.Id && node.CrapMu is not null).ToList();
            var rollup = CrapMath.Rollup(children.Select(node => node.CrapMu));
            if (rollup is null)
                continue;
            package.CrapMu = rollup.Value.Mu;
            package.CrapMax = rollup.Value.Max;
            package.CrapSigma = rollup.Value.Sigma;
        }
    }

    static double? Round1(double? value) =>
        value is null ? null : Math.Round(value.Value, 1, MidpointRounding.AwayFromZero);
}
