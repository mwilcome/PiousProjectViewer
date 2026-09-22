using System;
using System.Collections.Generic;
using System.Linq;

namespace PiousProjectViewer.Diagram;

public readonly record struct CrapRollup(double Mu, double Max, double Sigma)
{
    public double MuPlusSigma => Mu + Sigma;

    public string Band => MuPlusSigma switch
    {
        <= 8 => "calm",
        <= 20 => "warning",
        _ => "hot"
    };
}

public static class CrapMath
{
    public static double? Score(int complexity, double? coveragePercent)
    {
        if (coveragePercent is null)
            return null;
        var uncovered = 1 - coveragePercent.Value / 100d;
        return complexity * complexity * uncovered * uncovered * uncovered + complexity;
    }

    public static CrapRollup? Rollup(IEnumerable<double?> scores)
    {
        var values = new List<double>();
        foreach (var score in scores)
        {
            if (score is double value)
                values.Add(value);
        }
        if (values.Count == 0)
            return null;

        var mu = values.Average();
        var max = values.Max();
        var variance = values.Sum(value => (value - mu) * (value - mu)) / values.Count;
        return new CrapRollup(Round1(mu), Round1(max), Round1(Math.Sqrt(variance)));
    }

    static double Round1(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
