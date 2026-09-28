using PiousProjectViewer.Diagram;

namespace PiousProjectViewer.Tests;

public class ScannerGapTests
{
    [Fact]
    public void JavaScanReadsClassesAndSkipsTests()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "pom.xml"), "<project></project>");
            var main = Path.Combine(dir, "src", "main", "java", "com", "acme", "demo");
            var other = Path.Combine(dir, "src", "main", "java", "com", "acme", "other");
            var tests = Path.Combine(dir, "src", "test", "java", "com", "acme", "demo");
            var generated = Path.Combine(dir, "target", "gen");
            Directory.CreateDirectory(main);
            Directory.CreateDirectory(other);
            Directory.CreateDirectory(tests);
            Directory.CreateDirectory(generated);
            File.WriteAllText(Path.Combine(main, "Alpha.java"), """
                package com.acme.demo;
                import com.acme.other.Beta;
                public class Alpha {
                    public Alpha() {}
                    public int run(int value) {
                        if (value > 0 && value < 3) return value;
                        return 0;
                    }
                }
                """);
            File.WriteAllText(Path.Combine(other, "Beta.java"), """
                package com.acme.other;
                public class Beta {
                    public void help() {}
                }
                """);
            File.WriteAllText(Path.Combine(tests, "AlphaTest.java"), "package com.acme.demo; public class AlphaTest { public void nope() {} }");
            File.WriteAllText(Path.Combine(generated, "Skip.java"), "package gen; public class Skip { public void nope() {} }");
            File.WriteAllText(Path.Combine(dir, "mutation-report.json"), """
                {
                  "files": {
                    "src/main/java/com/acme/demo/Alpha.java": {
                      "mutants": [
                        { "status": "Killed", "location": { "start": { "line": 6 } } },
                        { "status": "Timeout", "location": { "start": { "line": 6 } } },
                        { "status": "Survived", "location": { "start": { "line": 6 } } },
                        { "status": "NoCoverage", "location": { "start": { "line": 6 } } },
                        { "status": "Ignored", "location": { "start": { "line": 6 } } },
                        { "status": "Killed" },
                        { "status": "Killed", "location": { "start": { "line": 90 } } }
                      ]
                    }
                  }
                }
                """);

            var scanner = new JavaScanner();
            Assert.True(scanner.CanScan(dir));
            Assert.True(scanner.SupportsComplexity);
            Assert.True(scanner.SupportsCrap);
            Assert.Contains("jacoco-maven-plugin", scanner.TestCommand(dir));
            Assert.Equal("", scanner.MutateCommand(dir));
            Assert.Equal("jacoco.xml", scanner.CoverageFile(dir));
            var document = scanner.Scan(dir);
            Assert.Contains(document.Nodes, node => node.Name == "Alpha");
            Assert.Contains(document.Nodes, node => node.Name == "Beta");
            Assert.DoesNotContain(document.Nodes, node => node.Name is "AlphaTest" or "Skip");
            var alpha = document.Nodes.Single(node => node.Name == "Alpha");
            Assert.Contains(alpha.Members!, member => member.Name == "Alpha()");
            var run = alpha.Members!.Single(member => member.Name == "run()");
            Assert.True(run.Cc >= 3);
            Assert.Equal(2, run.Killed);
            Assert.Equal(1, run.Survived);
            Assert.Equal(1, run.Uncovered);
            Assert.Contains(document.Edges, edge => edge.From == alpha.Id && edge.To.EndsWith("Beta"));
            Assert.Contains(document.Nodes, node => node.Id == "ns:com");

            var gradle = NewDir();
            try
            {
                File.WriteAllText(Path.Combine(gradle, "build.gradle"), "");
                Assert.Equal("gradle test jacocoTestReport", scanner.TestCommand(gradle));
                File.WriteAllText(Path.Combine(gradle, "gradlew"), "");
                Assert.Equal("./gradlew test jacocoTestReport", scanner.TestCommand(gradle));
                File.WriteAllText(Path.Combine(gradle, "gradlew.bat"), "");
                var both = OperatingSystem.IsWindows()
                    ? "gradlew.bat test jacocoTestReport"
                    : "./gradlew test jacocoTestReport";
                Assert.Equal(both, scanner.TestCommand(gradle));
            }
            finally
            {
                Directory.Delete(gradle, true);
            }
            var empty = NewDir();
            try
            {
                Assert.False(scanner.CanScan(empty));
            }
            finally
            {
                Directory.Delete(empty, true);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AngularTextScanAndTypeScriptScanBothFindClasses()
    {
        var dir = NewDir();
        try
        {
            var scanner = new AngularScanner();
            Assert.False(scanner.CanScan(dir));
            File.WriteAllText(Path.Combine(dir, "package.json"), """{ "dependencies": { "left-pad": "1.0.0" } }""");
            Assert.False(scanner.CanScan(dir));
            File.WriteAllText(Path.Combine(dir, "package.json"), """{ "dependencies": { "@angular/core": "21.0.0" } }""");
            Assert.True(scanner.CanScan(dir));
            Assert.True(scanner.SupportsComplexity);
            Assert.True(scanner.SupportsCrap);
            Assert.Contains("ng test", scanner.TestCommand(dir));
            Assert.Equal("", scanner.MutateCommand(dir));
            Assert.Equal("lcov.info", scanner.CoverageFile(dir));

            var src = Path.Combine(dir, "src", "app");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(Path.Combine(dir, "node_modules", "hidden"));
            File.WriteAllText(Path.Combine(src, "demo.ts"), """
                import { Component } from '@angular/core';
                import { Other } from './other';
                export class Demo {
                  private title = 'x';
                  run() {
                    if (ready && !ready) return;
                  }
                }
                """);
            File.WriteAllText(Path.Combine(src, "other.ts"), "export class Other { help() {} }\n");
            File.WriteAllText(Path.Combine(dir, "lcov.info"), "SF:src/app/demo.ts\nDA:5,1\nDA:6,0\n");
            File.WriteAllText(Path.Combine(src, "demo.spec.ts"), "export class Spec { keep() {} }\n");
            File.WriteAllText(Path.Combine(dir, "node_modules", "hidden", "skip.ts"), "export class Hidden { keep() {} }\n");
            var text = scanner.ScanText(dir);
            var demo = text.Nodes.Single(node => node.Name == "Demo");
            Assert.Contains(demo.Members!, member => member.Name == "title" && member.Kind == "field");
            Assert.True(demo.Members!.Single(member => member.Name == "run()").Cc >= 3);
            Assert.Contains(text.Edges, edge => edge.From == demo.Id && edge.To.EndsWith("Other"));
            Assert.DoesNotContain(text.Nodes, node => node.Id == "foreign:Angular");
            Assert.DoesNotContain(text.Nodes, node => node.Name is "Spec" or "Hidden");

            File.WriteAllText(Path.Combine(dir, "angular.json"), "{}");
            File.WriteAllText(Path.Combine(src, "demo.html"), "<p>Hi</p>");
            File.WriteAllText(Path.Combine(src, "demo.scss"), "p { color: black; }");
            var parsed = scanner.Scan(dir);
            demo = parsed.Nodes.Single(node => node.Name == "Demo");
            Assert.Contains(demo.Members!, member => member.Kind == "html" && member.Name == "template");
            Assert.Contains(demo.Members!, member => member.Kind == "scss");
            Assert.DoesNotContain(parsed.Nodes, node => node.Id == "foreign:Angular");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AutoPicksTheLanguageWithTheMostSourceFiles()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(dir, "pom.xml"), "<project></project>");
            File.WriteAllText(Path.Combine(dir, "angular.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "One.cs"), "class One {}");
            File.WriteAllText(Path.Combine(dir, "Two.cs"), "class Two {}");
            File.WriteAllText(Path.Combine(dir, "Only.java"), "class Only {}");
            File.WriteAllText(Path.Combine(dir, "Only.ts"), "export class Only {}");
            Directory.CreateDirectory(Path.Combine(dir, "node_modules"));
            File.WriteAllText(Path.Combine(dir, "node_modules", "Skip.cs"), "class Skip {}");
            Directory.CreateDirectory(Path.Combine(dir, "target"));
            File.WriteAllText(Path.Combine(dir, "target", "Gen.java"), "class Gen {}");
            Assert.Equal("C#", Scanners.For(dir)!.Name);
            Assert.Equal("Java", Scanners.Resolve(dir, "Java")!.Name);
            Assert.Null(Scanners.Resolve(dir, "Nope"));
            var blank = Path.Combine(dir, "blank");
            Directory.CreateDirectory(blank);
            Assert.Null(Scanners.For(blank));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MutationReportCountsOnlyMutantsInsideTheMethod()
    {
        Assert.Null(MutationReport.Find(Path.Combine(Path.GetTempPath(), "pious-missing-" + Guid.NewGuid().ToString("N"))));
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "mutation-report.json"), """{ "other": true }""");
            var report = MutationReport.Load(Path.Combine(dir, "mutation-report.json"));
            Assert.Equal((0, 0, 0), report.Counts("Alpha.java", 1, 20));
            Assert.Null(CoverageReport.Find(Path.Combine(Path.GetTempPath(), "pious-missing-" + Guid.NewGuid().ToString("N"))));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CoverageIgnoresAFileTheReportNeverMeasured()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "coverage.cobertura.xml"), """
                <coverage><packages><package><classes>
                  <class filename="Measured.cs"><lines>
                    <line number="4" hits="0"/>
                    <line hits="3"/>
                  </lines></class>
                </classes></package></packages></coverage>
                """);
            File.WriteAllText(Path.Combine(dir, "lcov.info"), "SF:src/Alpha.java\nDA:4,1\nDA:5,0\n");
            File.WriteAllText(Path.Combine(dir, "jacoco.xml"), """
                <report><package name="com/acme"><sourcefile name="Alpha.java">
                  <line nr="8" ci="1" mi="0"/>
                  <line nr="9" ci="0" mi="1"/>
                  <line nr="10" ci="0" mi="0"/>
                </sourcefile></package></report>
                """);
            var cobertura = CoverageReport.Load(Path.Combine(dir, "coverage.cobertura.xml"));
            Assert.Null(cobertura.Percent("Other.cs", 1, 10));
            Assert.Null(cobertura.Percent("Measured.cs", 20, 30));
            Assert.Equal(0, cobertura.Percent("Measured.cs", 4, 4));
            Assert.Equal(50, CoverageReport.LoadLcov(Path.Combine(dir, "lcov.info")).Percent("src/Alpha.java", 4, 5));
            Assert.Equal(50, CoverageReport.LoadJacoco(Path.Combine(dir, "jacoco.xml")).Percent("com/acme/Alpha.java", 8, 9));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BodilessMembersAreNotScoredAsMethods()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(dir, "Types.cs"), """
                namespace Demo;
                public interface Gate
                {
                    string Name { get; }
                    void Run();
                }
                public class Alpha
                {
                    public int Count { get; set; }
                    public int Size => 1;
                    public void Run() { if (true) {} }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "mutation-report.json"), """
                { "files": { "Types.cs": { "mutants": [
                  { "status": "Killed", "location": { "start": { "line": 11 } } }
                ] } } }
                """);
            var document = new CSharpScanner().Scan(dir);
            var gate = document.Nodes.Single(node => node.Name == "Gate");
            Assert.DoesNotContain(gate.Members!, member => member.Name.StartsWith("Run"));
            Assert.Contains(gate.Members!, member => member.Name == "Name" && member.Kind == "field");
            var alpha = document.Nodes.Single(node => node.Name == "Alpha");
            Assert.Contains(alpha.Members!, member => member.Name == "Count" && member.Kind == "field");
            Assert.Contains(alpha.Members!, member => member.Name == "Size" && member.Kind == "method");
            var run = alpha.Members!.Single(member => member.Name.StartsWith("Run"));
            Assert.Equal(1, run.Killed);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AFolderThatHoldsOneClassDrawsThatClass()
    {
        var document = new DiagramDocument
        {
            Title = "Demo",
            Nodes =
            [
                new DiagramNode { Id = "ns:pages", Name = "pages", Kind = "package" },
                new DiagramNode { Id = "ns:pages.home", Name = "home", Parent = "ns:pages", Kind = "package" },
                new DiagramNode
                {
                    Id = "type:Home",
                    Name = "Home",
                    Parent = "ns:pages.home",
                    Kind = "package",
                    File = "home.ts",
                    Members =
                    [
                        new DiagramMember { Name = "run()", Kind = "method", Cc = 2, IsPublic = true },
                        new DiagramMember { Name = "template", Kind = "html", IsPublic = true }
                    ]
                },
                new DiagramNode { Id = "ns:many", Name = "many", Kind = "package" },
                new DiagramNode { Id = "type:A", Name = "A", Parent = "ns:many", Kind = "package", File = "a.cs" },
                new DiagramNode { Id = "type:B", Name = "B", Parent = "ns:many", Kind = "package", File = "b.cs" }
            ]
        };
        var pages = DiagramScene.Build(document, "ns:pages");
        Assert.Contains("type:Home", pages.Boxes.Keys);
        Assert.DoesNotContain("ns:pages.home", pages.Boxes.Keys);
        Assert.DoesNotContain(pages.Boxes.Keys, key => key.StartsWith("m:", StringComparison.Ordinal));
        Assert.True(pages.Boxes["type:Home"].Height > DiagramScene.BoxHeight);
        var many = DiagramScene.Build(document, "ns:many");
        Assert.Contains("type:A", many.Boxes.Keys);
        Assert.Contains("type:B", many.Boxes.Keys);
        var blocked = DiagramScene.Points(new Box(0, 0, 100, 40), new Box(300, 0, 100, 40), false, [new Box(140, 0, 80, 40)]);
        Assert.True(blocked.Count > 2);
    }

    [Fact]
    public void RecipeOmitsEchoAndMailNamesTheBox()
    {
        var dir = NewDir();
        try
        {
            DiagramPublisher.WriteRecipe(dir, "echo skip", "scan-me", "echo no", "coverage.cobertura.xml");
            var recipe = File.ReadAllText(Path.Combine(dir, ".pious", "project.json"));
            Assert.DoesNotContain("echo", recipe);
            Assert.Contains("scan-me", recipe);
            Assert.DoesNotContain("coverageFile", recipe);
            File.WriteAllText(Path.Combine(dir, ".gitignore"), "bin/");
            DiagramPublisher.WriteRecipe(dir, "dotnet test", "scan-me", "", "coverage.cobertura.xml");
            recipe = File.ReadAllText(Path.Combine(dir, ".pious", "project.json"));
            Assert.Contains("coverageFile", recipe);
            var ignore = File.ReadAllText(Path.Combine(dir, ".gitignore"));
            Assert.Contains(".pious/", ignore);
            DiagramPublisher.WriteRecipe(dir, "dotnet test", "scan-me", "", "coverage.cobertura.xml");
            Assert.Equal(1, File.ReadAllText(Path.Combine(dir, ".gitignore")).Split(".pious/").Length - 1);
            DiagramPublisher.WriteRecipe(dir, "", "scan-me", "", null);
            Assert.Contains("dotnet test", File.ReadAllText(Path.Combine(dir, ".pious", "project.json")));
            Assert.Equal("dotnet test", DiagramPublisher.SavedTest(dir));
            DiagramPublisher.WriteRecipe(dir, "", "other-scan", "", null);
            Assert.DoesNotContain("dotnet test", File.ReadAllText(Path.Combine(dir, ".pious", "project.json")));
            var node = new DiagramNode { Id = "type:Demo.Alpha", Name = "Alpha", Kind = "package", File = "Types.cs", Line = 4 };
            DiagramPublisher.PostRefreshNode(dir, node, "dotnet test", "scan-me");
            DiagramPublisher.PostProposal(dir);
            var mail = File.ReadAllText(Path.Combine(dir, ".pious", "to-agent.json"));
            Assert.Contains("refresh-node", mail);
            Assert.Contains("Alpha", mail);
            Assert.Contains("proposal", mail);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BrokenLevelsAndAnEmptyDiagramDoNotThrow()
    {
        var dir = NewDir();
        try
        {
            var pious = Path.Combine(dir, ".pious");
            Directory.CreateDirectory(pious);
            var document = new DiagramDocument
            {
                Nodes =
                [
                    new DiagramNode { Id = "ns:inner.A", Name = "A", Kind = "package", Rank = 4 },
                    new DiagramNode { Id = "ns:outer.B", Name = "B", Kind = "package", Rank = 4 },
                    new DiagramNode { Id = "foreign:Lib", Name = "Lib", Kind = "foreign", Rank = 9 }
                ],
                Edges =
                [
                    new DiagramEdge { From = "ns:inner.A", To = "ns:outer.B" },
                    new DiagramEdge { From = "ns:inner.A", To = "foreign:Lib" }
                ]
            };
            File.WriteAllText(Path.Combine(pious, "levels.json"), "{");
            LevelRank.Apply(document, dir);
            Assert.Equal(4, document.Nodes[0].Rank);
            File.WriteAllText(Path.Combine(pious, "levels.json"), "{}");
            LevelRank.Apply(document, dir);
            Assert.Equal(4, document.Nodes[0].Rank);
            File.WriteAllText(Path.Combine(pious, "levels.json"), """{ "innerFirst": [["inner"], ["outer"]] }""");
            LevelRank.Apply(document, dir);
            Assert.True(document.Edges[0].Violating);
            Assert.False(document.Edges[1].Violating);
            var empty = Path.Combine(dir, "none.json");
            File.WriteAllText(empty, "null");
            Assert.Empty(DiagramLoader.Load(empty).Nodes);
            File.WriteAllText(empty, """{ "nodes": [ { "id": "a", "name": "A", "members": null } ] }""");
            Assert.NotNull(DiagramLoader.Load(empty).Nodes[0].Members);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MissingFolderShowsTheEmptyPicture() => HeadlessApp.OnUi(() =>
    {
        var dir = NewDir();
        var previous = SessionStore.FilePathOverride;
        SessionStore.FilePathOverride = Path.Combine(dir, "session.json");
        try
        {
            SessionStore.Save(new Session { Folder = Path.Combine(dir, "gone"), Mode = "complexity", Language = "auto", Agent = "Grok" });
            var window = new MainWindow();
            window.Show();
            Assert.Equal("No project", window.Diagram.Document!.Title);
            window.Close();
        }
        finally
        {
            SessionStore.FilePathOverride = previous;
            Directory.Delete(dir, true);
        }
    });

    static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pious-gap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
