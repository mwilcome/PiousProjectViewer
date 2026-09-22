using pious_project_viewer.Diagram;

namespace pious_project_viewer.Tests;

public class CrapMathTests
{
    [Fact]
    public void UncoveredComplexityOfFiveScoresThirty()
    {
        Assert.Equal(30d, CrapMath.Score(5, 0));
    }

    [Fact]
    public void FullCoverageLeavesTheComplexity()
    {
        Assert.Equal(12d, CrapMath.Score(12, 100));
    }

    [Fact]
    public void PartialCoverageUsesTheCubedUncoveredFraction()
    {
        Assert.Equal(10.8, CrapMath.Score(10, 80)!.Value, 1);
    }

    [Fact]
    public void MissingCoverageIsLeftOutOfTheRollup()
    {
        Assert.Null(CrapMath.Score(8, null));
        var rollup = CrapMath.Rollup([CrapMath.Score(2, 100), CrapMath.Score(9, null)]);
        Assert.NotNull(rollup);
        Assert.Equal(2d, rollup.Value.Mu);
        Assert.Equal(0d, rollup.Value.Sigma);
        Assert.Equal("calm", rollup.Value.Band);
    }

    [Fact]
    public void UntestedComplexityOfFiveIsHot()
    {
        var rollup = CrapMath.Rollup([CrapMath.Score(5, 0)]);
        Assert.Equal(30d, rollup!.Value.Mu);
        Assert.Equal("hot", rollup.Value.Band);
    }

    [Fact]
    public void TestedLowComplexityStaysCalm()
    {
        var rollup = CrapMath.Rollup([
            CrapMath.Score(1, 100),
            CrapMath.Score(2, 100),
            CrapMath.Score(4, 100)
        ]);
        Assert.Equal("calm", rollup!.Value.Band);
    }

    [Fact]
    public void RollupUsesPopulationSigmaRoundedToOneDecimal()
    {
        var rollup = CrapMath.Rollup([4d, 20d, 14d]);
        Assert.Equal(12.7, rollup!.Value.Mu);
        Assert.Equal(20d, rollup.Value.Max);
        Assert.Equal(6.6, rollup.Value.Sigma);
    }

    [Fact]
    public void BandBreaksAtEightAndTwenty()
    {
        Assert.Equal("calm", new CrapRollup(4, 4, 4).Band);
        Assert.Equal("warning", new CrapRollup(6, 6, 6).Band);
        Assert.Equal("warning", new CrapRollup(10, 10, 10).Band);
        Assert.Equal("hot", new CrapRollup(10, 10, 10.1).Band);
    }
}

public class HeatTests
{
    [Fact]
    public void ComplexityColorIsNotTheViolationRed()
    {
        Assert.NotEqual(Heat.Violation, Heat.Color(3));
        Assert.NotEqual(Heat.Violation, Heat.Color(9));
        Assert.NotEqual(Heat.Violation, Heat.Color(16));
        Assert.NotEqual(Heat.Violation, Heat.Color(24));
    }

    [Fact]
    public void BandsMeetThePublishedLines()
    {
        Assert.Equal("cool", Heat.Word(5));
        Assert.Equal("warm", Heat.Word(6));
        Assert.Equal("warm", Heat.Word(10));
        Assert.Equal("hot", Heat.Word(11));
        Assert.Equal("hot", Heat.Word(20));
        Assert.Equal("hottest", Heat.Word(21));
    }
}

public class DiagramSceneTests
{
    [Fact]
    public void InnerRankSitsBelowOuterRank()
    {
        var scene = DiagramScene.Build(LoadSample(), null);
        Assert.True(scene.Boxes["domain"].Y > scene.Boxes["host"].Y);
    }

    [Fact]
    public void LibrarySitsOutsideTheFrame()
    {
        var scene = DiagramScene.Build(LoadSample(), null);
        Assert.True(scene.Boxes["avalonia"].X > scene.Frame.Right);
    }

    [Fact]
    public void ViolatingArrowStaysLeftOfBothBoxes()
    {
        var scene = DiagramScene.Build(LoadSample(), null);
        var edge = Assert.Single(scene.Edges, candidate => candidate.Violating);
        var left = Math.Min(scene.Boxes[edge.From].X, scene.Boxes[edge.To].X);
        Assert.Contains(edge.Points, point => point.X < left);
    }

    [Fact]
    public void DrillShowsOnlyTheChildren()
    {
        var scene = DiagramScene.Build(LoadSample(), "engine");
        Assert.Equal(["engine.layout", "engine.route"], scene.Boxes.Keys.OrderBy(id => id));
    }

    static DiagramDocument LoadSample()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "diagram.json");
            if (File.Exists(candidate))
                return DiagramLoader.Load(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("samples/diagram.json");
    }
}
