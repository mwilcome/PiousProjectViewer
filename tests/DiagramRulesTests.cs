using System.Reflection;
using PiousProjectViewer.Diagram;

namespace PiousProjectViewer.Tests;

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
        Assert.Equal("very good", Heat.Word(1));
        Assert.Equal("very good", Heat.Word(4));
        Assert.Equal("good", Heat.Word(5));
        Assert.Equal("good", Heat.Word(7));
        Assert.Equal("med", Heat.Word(8));
        Assert.Equal("med", Heat.Word(10));
        Assert.Equal("bad", Heat.Word(11));
        Assert.Equal("bad", Heat.Word(20));
        Assert.Equal("very bad", Heat.Word(21));
        Assert.NotEqual(Heat.Color(4), Heat.Color(7));
        Assert.NotEqual(Heat.Color(7), Heat.Color(10));
        Assert.NotEqual(Heat.Color(10), Heat.Color(20));
        Assert.NotEqual(Heat.Color(20), Heat.Color(21));
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

    [Fact]
    public void ScannerReadsThisProjectAndSkipsTheTestProject()
    {
        var root = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(attribute => attribute.Key == "RepoRoot").Value!;
        var project = CSharpScanner.FindAppProject(root);
        Assert.NotNull(project);
        var scanner = new CSharpScanner();
        Assert.True(scanner.SupportsComplexity);
        Assert.True(scanner.SupportsCrap);
        var document = scanner.Scan(Path.GetDirectoryName(project)!);
        Assert.Contains(document.Nodes, node => node.Id == "ns:PiousProjectViewer.Diagram");
        Assert.DoesNotContain(document.Nodes, node => node.Name == "CrapMathTests");
        Assert.DoesNotContain(document.Nodes, node => node.Name == "TypeFact");
        Assert.Contains(document.Nodes, node => node.Name == "DiagramView" && node.WorstCc > 1 && node.File != null && node.File.EndsWith("DiagramView.cs") && node.Line > 0);
        Assert.Contains(document.Edges, edge => edge.To == "foreign:Avalonia");
        Assert.True(document.CoverageReady);
        Assert.Contains(document.Nodes, node => node.Name == "CrapMath" && node.CrapMu is not null);
        var testCommand = ScanCommand.TestCommandFor(root);
        Assert.Contains("dotnet test", testCommand);
        Assert.Contains(".sln", testCommand);
    }

    [Fact]
    public void DeclaredRelationshipsIgnoreLocalNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pious-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(dir, "Types.cs"), """
                namespace Demo;
                public class Alpha
                {
                    public Beta Field;
                    public Alpha(Beta value) {}
                    private class Hidden {}
                }
                public class Beta
                {
                    void Run() { Alpha local = null; }
                }
                """);
            var document = new CSharpScanner().Scan(dir);
            Assert.Contains(document.Edges, edge => edge.From.EndsWith("Alpha") && edge.To.EndsWith("Beta"));
            Assert.DoesNotContain(document.Edges, edge => edge.From.EndsWith("Beta") && edge.To.EndsWith("Alpha"));
            Assert.DoesNotContain(document.Nodes, node => node.Name == "Hidden");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CrapFillStaysNeutralWithoutCoverage()
    {
        var node = new DiagramNode { WorstCc = 22, Kind = "package" };
        Assert.Equal(BoxPaint.CrapNeutral, BoxPaint.Fill(node, PaintMode.Crap, false));
        Assert.NotEqual(Heat.Violation, BoxPaint.Fill(node, PaintMode.Complexity, false));
    }

    [Fact]
    public void CoverageReportFeedsCrap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pious-cov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(dir, "Types.cs"), """
                namespace Demo;
                public class Alpha
                {
                    public int Run(int value)
                    {
                        if (value > 0) return value;
                        return 0;
                    }
                }
                """);
            var results = Path.Combine(dir, "TestResults");
            Directory.CreateDirectory(results);
            File.WriteAllText(Path.Combine(results, "coverage.cobertura.xml"), """
                <coverage>
                  <packages><package><classes>
                    <class filename="Types.cs">
                      <lines>
                        <line number="5" hits="1"/>
                        <line number="6" hits="1"/>
                        <line number="7" hits="1"/>
                      </lines>
                    </class>
                  </classes></package></packages>
                </coverage>
                """);
            var document = new CSharpScanner().Scan(dir);
            var alpha = document.Nodes.Single(node => node.Name == "Alpha");
            Assert.True(document.CoverageReady);
            Assert.NotNull(alpha.CrapMu);
            Assert.Equal("calm", new CrapRollup(alpha.CrapMu!.Value, alpha.CrapMax!.Value, alpha.CrapSigma!.Value).Band);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PublishWritesOnceUntilThePictureChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pious-pub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var first = new DiagramDocument
            {
                Title = "Demo",
                Nodes = [new DiagramNode { Id = "a", Name = "A", WorstCc = 2 }]
            };
            Assert.True(DiagramPublisher.Publish(dir, first));
            Assert.False(DiagramPublisher.Publish(dir, first));
            first.Nodes[0].WorstCc = 9;
            Assert.True(DiagramPublisher.Publish(dir, first));
            DiagramPublisher.PostUpdated(dir);
            var mail = File.ReadAllText(Path.Combine(dir, ".pious", "to-agent.json"));
            Assert.Contains("diagram-updated", mail);
            Assert.Contains(".pious/diagram.json", mail);
            DiagramPublisher.PostContext(dir, new DiagramNode
            {
                Id = "type:Demo.Alpha",
                Name = "Alpha",
                Kind = "package",
                File = "Types.cs",
                Line = 4
            });
            mail = File.ReadAllText(Path.Combine(dir, ".pious", "to-agent.json"));
            Assert.Contains("\"op\": \"context\"", mail);
            Assert.Contains("Alpha", mail);
            Assert.Contains("Types.cs", mail);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
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
