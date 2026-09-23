using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using PiousProjectViewer.Diagram;

namespace PiousProjectViewer.Tests;

public class CoverageTests
{
    [Fact]
    public void PaintCoversEveryBand()
    {
        Assert.Equal(BoxPaint.CrapNeutral, BoxPaint.Fill(new DiagramNode { Kind = "foreign", WorstCc = 30 }, PaintMode.Crap, true));
        Assert.Equal(Heat.Color(3), BoxPaint.Fill(new DiagramNode { WorstCc = 3 }, PaintMode.Complexity, false));
        Assert.Equal(Heat.Color(22), BoxPaint.Fill(new DiagramNode { WorstCc = 22 }, PaintMode.Complexity, true));
        Assert.Equal(BoxPaint.CrapNeutral, BoxPaint.Fill(new DiagramNode { WorstCc = 9 }, PaintMode.Crap, false));
        Assert.Equal(BoxPaint.CrapNeutral, BoxPaint.Fill(new DiagramNode { CrapMu = 1 }, PaintMode.Crap, true));
        Assert.Equal(BoxPaint.CrapCalm, BoxPaint.Fill(new DiagramNode { CrapMu = 2, CrapMax = 3, CrapSigma = 1 }, PaintMode.Crap, true));
        Assert.Equal(BoxPaint.CrapWarning, BoxPaint.Fill(new DiagramNode { CrapMu = 10, CrapMax = 12, CrapSigma = 2 }, PaintMode.Crap, true));
        Assert.Equal(BoxPaint.CrapHot, BoxPaint.Fill(new DiagramNode { CrapMu = 30, CrapMax = 40, CrapSigma = 5 }, PaintMode.Crap, true));
        Assert.Equal("none", Heat.Word(null));
        Assert.Equal("#2E3C44", Heat.Color(null));
    }

    [Fact]
    public void SessionRoundTripUsesTheOverridePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "pious-session-" + Guid.NewGuid().ToString("N") + ".json");
        SessionStore.FilePathOverride = path;
        try
        {
            Assert.Equal("complexity", SessionStore.Load().Mode);
            SessionStore.Save(new Session { Folder = "C:\\demo", Mode = "crap", Language = "C#", Agent = "Grok" });
            var loaded = SessionStore.Load();
            Assert.Equal("C:\\demo", loaded.Folder);
            Assert.Equal("crap", loaded.Mode);
            Assert.Equal("C#", loaded.Language);
            File.WriteAllText(path, "not json");
            Assert.Equal("auto", SessionStore.Load().Language);
        }
        finally
        {
            SessionStore.FilePathOverride = null;
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ScanCommandCoversBothOutcomes()
    {
        var empty = Path.Combine(Path.GetTempPath(), "pious-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        var coded = Path.Combine(Path.GetTempPath(), "pious-coded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(coded);
        try
        {
            Assert.False(ScanCommand.TryRun(["--language", "C#"]));
            Assert.Contains("--scan", ScanCommand.CommandLine(coded, "auto"));
            Assert.Equal("echo No scanner for this folder.", ScanCommand.TestCommandFor(empty));
            Assert.True(ScanCommand.TryRun(["--scan", empty, "--language", "C#"]));
            Assert.Equal(1, Environment.ExitCode);
            Environment.ExitCode = 0;
            Assert.Null(Scanners.Resolve(empty, "C#"));
            Assert.Null(Scanners.Resolve(empty, "Cobol"));
            File.WriteAllText(Path.Combine(coded, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(coded, "Demo.cs"), """
                namespace Demo;
                public class Alpha
                {
                    public int Run(int value)
                    {
                        if (value > 0) return value;
                        return 0;
                    }
                    int Hidden() => 1;
                    public string? Name { get; set; }
                }
                """);
            File.WriteAllText(Path.Combine(coded, "Demo.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            Assert.NotNull(Scanners.Resolve(coded, "auto"));
            Assert.NotNull(Scanners.Resolve(coded, "C#"));
            Assert.Contains("Demo.Tests.csproj", new CSharpScanner().TestCommand(coded));
            Assert.True(ScanCommand.TryRun(["--scan", coded]));
            var document = DiagramLoader.Load(DiagramPublisher.DiagramPath(coded));
            var alpha = document.Nodes.Single(node => node.Name == "Alpha");
            Assert.Contains(alpha.Members, member => member.Name == "Run(int)" && member.IsPublic);
            Assert.Contains(alpha.Members, member => member.Name == "Hidden()" && !member.IsPublic);
            Assert.Contains(alpha.Members, member => member.Name == "Name" && member.IsPublic);
            DiagramPublisher.PostRefresh(coded, "dotnet test", "scan");
            Assert.Contains("refresh", File.ReadAllText(Path.Combine(coded, ".pious", "to-agent.json")));
        }
        finally
        {
            Environment.ExitCode = 0;
            Directory.Delete(empty, true);
            Directory.Delete(coded, true);
        }
    }

    [Fact]
    public void TwoProjectsAreGrafted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pious-graft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "One"));
        Directory.CreateDirectory(Path.Combine(dir, "Two"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "One", "One.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(dir, "Two", "Two.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(dir, "One", "A.cs"), "namespace One; public class A { public void Go() {} }");
            File.WriteAllText(Path.Combine(dir, "Two", "B.cs"), "namespace Two; public class B { public void Go() {} }");
            var document = new CSharpScanner().Scan(dir);
            Assert.Contains(document.Nodes, node => node.Name == "One");
            Assert.Contains(document.Nodes, node => node.Name == "Two");
            Assert.Contains(document.Nodes, node => node.Name == "A");
            var missing = Path.Combine(dir, "missing");
            Assert.Empty(CSharpScanner.FindProjects(missing));
            Assert.Empty(CSharpScanner.FindSolutions(missing));
            Assert.Empty(CSharpScanner.FindTestProjects(missing));
            Assert.Equal("", new CSharpScanner().TestCommand(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GrokLaunchFindsAnOverrideAndTheHomeCopy()
    {
        var previous = Environment.GetEnvironmentVariable("GROK_BIN");
        var fake = Path.Combine(Path.GetTempPath(), "pious-grok-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(fake, "");
        try
        {
            Environment.SetEnvironmentVariable("GROK_BIN", fake);
            Assert.Equal(fake, GrokLaunch.Find());
            Environment.SetEnvironmentVariable("GROK_BIN", Path.Combine(Path.GetTempPath(), "no-such-grok.exe"));
            var found = GrokLaunch.Find();
            Assert.False(string.IsNullOrWhiteSpace(found));
            Assert.Contains("mail", GrokLaunch.WakeLine);
            Assert.Contains("refresh", GrokLaunch.Rules);
            Assert.Contains("diagram.json", GrokLaunch.LaunchPrompt);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GROK_BIN", previous);
            File.Delete(fake);
        }
    }

    [Fact]
    public void PulseFiresAfterTheDiagramChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pious-pulse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "diagram.json");
        File.WriteAllText(file, "{}");
        using var pulse = new ProjectPulse();
        var fired = new ManualResetEventSlim(false);
        pulse.Due += (_, _) => fired.Set();
        pulse.WatchFile(file);
        pulse.WatchFile(file);
        File.WriteAllText(file, "{\"title\":\"x\"}");
        Assert.True(fired.Wait(TimeSpan.FromSeconds(3)));
        pulse.WatchFile(Path.GetPathRoot(dir)!);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void SourceOpenBuildsTheEditorCommandWithoutStartingIt()
    {
        ProcessStartInfo? captured = null;
        SourceEditor.Launcher = info => captured = info;
        SourceEditor.CodeFinder = () => "code.cmd";
        try
        {
            var message = SourceEditor.Open("C:\\demo\\Main.cs", 14);
            Assert.Contains("14", message);
            Assert.NotNull(captured);
            Assert.Equal("code.cmd", captured!.FileName);
            Assert.Contains("Main.cs:14:1", captured.Arguments);
            SourceEditor.CodeFinder = () => null;
            captured = null;
            var fallback = SourceEditor.Open("C:\\demo\\Main.cs", 0);
            Assert.Contains("default app", fallback);
            Assert.Equal("C:\\demo\\Main.cs", captured!.FileName);
            SourceEditor.CodeFinder = null;
            var real = SourceEditor.Open("C:\\demo\\Main.cs", 4);
            Assert.False(string.IsNullOrWhiteSpace(real));
        }
        finally
        {
            SourceEditor.Launcher = null;
            SourceEditor.CodeFinder = null;
        }
    }

    [Fact]
    public void SceneBoundsIncludeTheLibrary()
    {
        var scene = DiagramScene.Build(LoadSample(), null);
        Assert.True(scene.Bounds.Right >= scene.Boxes["avalonia"].Right);
        Assert.True(scene.Bounds.Bottom >= scene.Frame.Bottom);
    }

    [Fact]
    public void DiagramViewPaintsDrillsAndOpensACard() => HeadlessApp.OnUi(PaintDiagram);

    static void PaintDiagram()
    {
        var view = new DiagramView { Width = 900, Height = 600 };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        var opened = new List<string>();
        view.OpenCard += (_, node) => opened.Add(node.Name);
        view.Document = new DiagramDocument
        {
            Title = "Demo",
            CoverageReady = true,
            SupportsCrap = true,
            SupportsComplexity = true,
            Nodes =
            [
                new DiagramNode { Id = "ns:app", Name = "App", Kind = "package", WorstCc = 4, CrapMu = 2, CrapMax = 4, CrapSigma = 1 },
                new DiagramNode
                {
                    Id = "type:app.Main",
                    Name = "Main",
                    Parent = "ns:app",
                    Kind = "package",
                    File = "Main.cs",
                    Line = 3,
                    WorstCc = 8,
                    CrapMu = 4,
                    CrapMax = 8,
                    CrapSigma = 1,
                    Members = [new DiagramMember { Name = "Run()", Line = 5, Cc = 8, Coverage = 100, Crap = 8, IsPublic = true }]
                },
                new DiagramNode
                {
                    Id = "type:app.Other",
                    Name = "Other",
                    Parent = "ns:app",
                    Kind = "package",
                    File = "Other.cs",
                    Line = 1,
                    Members = [new DiagramMember { Name = "Go()", Line = 2, Cc = 1, IsPublic = true }]
                },
                new DiagramNode { Id = "foreign:Avalonia", Name = "Avalonia", Kind = "foreign" }
            ],
            Edges = [new DiagramEdge { From = "type:app.Main", To = "foreign:Avalonia" }]
        };
        view.Measure(new Size(900, 600));
        view.Arrange(new Rect(0, 0, 900, 600));
        using (var bitmap = new RenderTargetBitmap(new PixelSize(900, 600), new Vector(96, 96)))
            bitmap.Render(view);
        Assert.Contains("Demo", view.PathText);
        Assert.Equal("", view.DetailText);
        view.Mode = PaintMode.Crap;
        Assert.Contains("CRAP", view.ColumnHeader);
        var box = FindBox(view);
        Press(view, box, 1);
        Move(view, box + new Vector(12, 8));
        view.RaiseEvent(new PointerEventArgs(
            InputElement.PointerExitedEvent,
            view,
            new Pointer(1, PointerType.Mouse, true),
            view,
            box,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None));
        view.RaiseEvent(new PointerPressedEventArgs(
            view,
            new Pointer(1, PointerType.Mouse, true),
            view,
            box,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonPressed),
            KeyModifiers.None,
            1));
        Move(view, new Point(8, 8));
        Wheel(view, box, 1);
        Press(view, box, 2);
        Assert.Equal("ns:app", view.CurrentParentId);
        Assert.True(view.CanGoBack);
        view.GoBack();
        Assert.False(view.CanGoBack);
        view.Open("ns:app");
        Press(view, FindBox(view), 2);
        Assert.Contains("Main", opened);
        var main = view.Document.Nodes.Single(node => node.Name == "Main");
        Assert.Contains("Run", view.MemberLabel(main.Members[0]));
        Assert.DoesNotContain("()", view.MemberLabel(main.Members[0]));
        Assert.Contains("Run", view.Describe(main));
        view.Mode = PaintMode.Complexity;
        Assert.Contains("Complexity", view.Describe(main));
        view.ReplaceDocument(view.Document);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(900, 600), new Vector(96, 96)))
            bitmap.Render(view);
        window.Close();
    }

    [Fact]
    public void MainWindowKeepsTheDiagramAndPopsTheCard() => HeadlessApp.OnUi(() => OpenTheCard().GetAwaiter().GetResult());

    static async Task OpenTheCard()
    {
        var root = Path.Combine(Path.GetTempPath(), "pious-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var session = Path.Combine(root, "session.json");
        SessionStore.FilePathOverride = session;
        ProcessStartInfo? launched = null;
        SourceEditor.Launcher = info => launched = info;
        SourceEditor.CodeFinder = () => "code.cmd";
        try
        {
            File.WriteAllText(Path.Combine(root, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(root, "Demo.cs"), "namespace Demo; public class Alpha { public void Run() { if (true) {} } }");
            var scanner = new CSharpScanner();
            DiagramPublisher.Publish(root, scanner.Scan(root));
            SessionStore.Save(new Session { Folder = root, Mode = "crap", Language = "auto", Agent = "Grok" });
            var window = new MainWindow();
            window.Show();
            window.OpenFolder(root);
            var alpha = window.Diagram.Document!.Nodes.Single(node => node.Name == "Alpha");
            Assert.Equal("Alpha", alpha.Name);
            window.Diagram.Select(alpha.Id);
            var method = window.GetVisualDescendants().OfType<Button>().First(button => button.Content is string text && text.Contains("Run"));
            method.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(launched);
            Assert.Contains("Demo.cs", launched!.Arguments);
            launched = null;
            Press(window.Diagram, FindBox(window.Diagram), 2);
            var card = Assert.Single(window.OwnedWindows.OfType<ClassCardWindow>());
            Assert.Contains("Relationships", string.Join("\n", card.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)));
            var cardRun = card.GetVisualDescendants().OfType<Button>().First(button =>
                button.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text != null && block.Text.Contains("Run")));
            cardRun.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(launched);
            card.RaiseEvent(new KeyEventArgs { Key = Key.Escape, RoutedEvent = InputElement.KeyDownEvent });
            Assert.False(card.IsVisible);
            window.RefreshOne(alpha);
            Assert.Contains("Start the companion first", window.CompanionStatus.Text);
            var proposal = window.GetVisualDescendants().OfType<Button>().First(button => button.Content is string text && text.Contains("proposal"));
            proposal.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("Start the companion first", window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Name == "CompanionStatus").Text);
            var complexity = window.GetVisualDescendants().OfType<RadioButton>().First(button => Equals(button.Content, "Complexity"));
            complexity.IsChecked = true;
            var language = window.GetVisualDescendants().OfType<ComboBox>().First();
            language.SelectedItem = "C#";
            var ask = window.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, "Refresh diagram"));
            ask.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var back = window.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, "Back"));
            back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.RaiseEvent(new KeyEventArgs { Key = Key.Escape, RoutedEvent = InputElement.KeyDownEvent });
            var started = false;
            window.StartProcess = () =>
            {
                started = true;
                return Task.CompletedTask;
            };
            window.GetVisualDescendants().OfType<Button>().First(button => button.Content is string text && text.Contains("companion"))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(started);
            started = false;
            window.SessionState.Folder = null;
            await window.StartCompanionAsync();
            Assert.False(started);
            window.SessionState.Folder = root;
            window.SessionState.Agent = "Other";
            await window.StartCompanionAsync();
            Assert.False(started);
            window.SessionState.Agent = "Grok";
            GrokLaunch.FindOverride = () => "pious-missing-grok";
            await window.StartCompanionAsync();
            Assert.False(started);
            var fakeGrok = Path.Combine(root, "grok.exe");
            File.WriteAllText(fakeGrok, "");
            GrokLaunch.FindOverride = () => fakeGrok;
            await window.StartCompanionAsync();
            Assert.True(started);
            Assert.False(MainWindow.ExistsOnPath("pious-no-such-tool"));
            var bare = Path.Combine(root, "bare");
            Directory.CreateDirectory(bare);
            window.OpenFolder(bare);
            window.SessionState.Folder = " ";
            window.ReloadDiagram();
            window.SessionState.Folder = root;
            File.WriteAllText(DiagramPublisher.DiagramPath(root), File.ReadAllText(DiagramPublisher.DiagramPath(root)));
            window.ReloadDiagram();
            window.Close();
        }
        finally
        {
            SessionStore.FilePathOverride = null;
            SourceEditor.Launcher = null;
            SourceEditor.CodeFinder = null;
            GrokLaunch.FindOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    static Point FindBox(DiagramView view)
    {
        for (var y = 20; y < 580; y += 24)
        {
            for (var x = 20; x < 880; x += 24)
            {
                var point = new Point(x, y);
                Press(view, point, 1);
                if (view.DetailText.Length > 0)
                    return point;
            }
        }
        throw new InvalidOperationException(view.PathText);
    }

    static void Press(Control view, Point point, int clicks)
    {
        view.RaiseEvent(new PointerPressedEventArgs(
            view,
            new Pointer(1, PointerType.Mouse, true),
            view,
            point,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clicks));
        view.RaiseEvent(new PointerReleasedEventArgs(
            view,
            new Pointer(1, PointerType.Mouse, true),
            view,
            point,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));
    }

    static void Move(Control view, Point point) =>
        view.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent,
            view,
            new Pointer(1, PointerType.Mouse, true),
            view,
            point,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None));

    static void Wheel(Control view, Point point, double delta) =>
        view.RaiseEvent(new PointerWheelEventArgs(
            view,
            new Pointer(1, PointerType.Mouse, true),
            view,
            point,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            new Vector(0, delta)));

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
