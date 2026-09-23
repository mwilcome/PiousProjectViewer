using PiousProjectViewer.Diagram;

namespace PiousProjectViewer.Tests;

public class MartinDistanceTests
{
    [Fact]
    public void AStableAbstractPackageSitsOnTheMainSequence()
    {
        var document = new DiagramDocument
        {
            Nodes =
            [
                Type("ns:core", "type:core.IGate", "IGate", abstractType: true),
                Type("ns:app", "type:app.App", "App", abstractType: false)
            ],
            Edges = [new DiagramEdge { From = "type:app.App", To = "type:core.IGate" }]
        };
        var scores = MartinDistance.Measure(document).ToDictionary(score => score.Id);
        var core = scores["ns:core"];
        Assert.Equal(1, core.Abstractness);
        Assert.Equal(0, core.Instability);
        Assert.Equal(0, core.Distance);
        Assert.Contains("On the line", core.Meaning());
        Assert.Equal(1, core.Afferent);
        Assert.Equal(0, core.Efferent);
        var app = scores["ns:app"];
        Assert.Equal(0, app.Abstractness);
        Assert.Equal(1, app.Instability);
        Assert.Equal(0, app.Distance);
        Assert.Equal(1, TypeCoupling.Count(document, "type:app.App"));
        Assert.Equal(1, TypeCoupling.Count(document, "type:core.IGate"));
    }

    [Fact]
    public void AStableConcretePackageIsFarFromTheLine()
    {
        var document = new DiagramDocument
        {
            Nodes =
            [
                Type("ns:core", "type:core.Clock", "Clock", abstractType: false),
                Type("ns:app", "type:app.App", "App", abstractType: false)
            ],
            Edges = [new DiagramEdge { From = "type:app.App", To = "type:core.Clock" }]
        };
        var core = MartinDistance.Measure(document).Single(score => score.Id == "ns:core");
        Assert.Equal(0, core.Abstractness);
        Assert.Equal(0, core.Instability);
        Assert.Equal(1, core.Distance);
        Assert.Contains("Stable concrete", core.Meaning());
        Assert.Equal("#FF5C7A", PackageScore.Color(core.Distance));
        Assert.Equal("#3DDC97", PackageScore.Color(0));
    }

    [Fact]
    public void TestTypesAreLeftOut()
    {
        var document = new DiagramDocument
        {
            Nodes =
            [
                Type("ns:app", "type:app.App", "App", abstractType: false),
                Type("ns:app.Tests", "type:app.Tests.AppTests", "AppTests", abstractType: false, file: "tests/AppTests.cs")
            ],
            Edges = [new DiagramEdge { From = "type:app.Tests.AppTests", To = "type:app.App" }]
        };
        var app = MartinDistance.Measure(document).Single();
        Assert.Equal("ns:app", app.Id);
        Assert.Equal(0, app.Afferent);
        Assert.Equal(0, app.Efferent);
    }

    [Fact]
    public void CSharpMarksAnInterfaceAndAnAbstractClass()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pious-martin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(dir, "Gate.cs"), """
                namespace Demo;
                public interface IGate {}
                public abstract class Base {}
                public class App : Base {}
                """);
            var document = new CSharpScanner().Scan(dir);
            Assert.True(document.Nodes.Single(node => node.Name == "IGate").Abstract);
            Assert.True(document.Nodes.Single(node => node.Name == "Base").Abstract);
            Assert.False(document.Nodes.Single(node => node.Name == "App").Abstract);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    static DiagramNode Type(string package, string id, string name, bool abstractType, string file = "App.cs") => new()
    {
        Id = id,
        Name = name,
        Parent = package,
        Kind = "package",
        File = file,
        Abstract = abstractType
    };
}
