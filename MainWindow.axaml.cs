using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PiousProjectViewer.Diagram;

namespace PiousProjectViewer;

public partial class MainWindow : Window
{
    readonly ProjectPulse _pulse = new();
    Session _session = new();
    bool _companionRunning;
    bool _showingProposal;
    ClassCardWindow? _cardWindow;
    bool _ready;

    internal Session SessionState => _session;

    internal Func<Task>? StartProcess { get; set; }


    public MainWindow()
    {
        InitializeComponent();
        Diagram.ViewChanged += (_, _) => RefreshInspector();
        Diagram.OpenCard += (_, node) => ShowCard(node);
        Diagram.RefreshNode += (_, node) => RefreshOne(node);
        LanguageBox.ItemsSource = new[] { "Auto" }.Concat(Scanners.All.Select(scanner => scanner.Name)).ToList();
        AgentBox.ItemsSource = Agents.Names.ToList();
        _session = SessionStore.Load();
        LanguageBox.SelectedItem = _session.Language == "auto" ? "Auto" : _session.Language;
        AgentBox.SelectedItem = Agents.Names.Contains(_session.Agent) ? _session.Agent : Agents.Names[0];
        Diagram.Mode = _session.Mode == "crap" ? PaintMode.Crap : PaintMode.Complexity;
        if (Diagram.Mode == PaintMode.Crap)
            CrapMode.IsChecked = true;
        if (_session.Folder is not null && Directory.Exists(_session.Folder))
            OpenFolder(_session.Folder);
        else
            ShowEmpty();
        _pulse.Due += (_, _) => Dispatcher.UIThread.Post(ReloadDiagram);
        _ready = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _pulse.Dispose();
        _cardWindow?.Close();
        Companion.Kill();
        base.OnClosed(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Diagram.Focus();
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Diagram.GoBack();
        e.Handled = true;
    }

    void OnBack(object? sender, RoutedEventArgs e) => Diagram.GoBack();

    async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a project folder",
            AllowMultiple = false
        });
        if (folders.Count == 0)
            return;
        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;
        OpenFolder(path);
    }

    void OnMode(object? sender, RoutedEventArgs e)
    {
        if (Diagram is null)
            return;
        Diagram.Mode = CrapMode.IsChecked == true ? PaintMode.Crap : PaintMode.Complexity;
        _session.Mode = Diagram.Mode == PaintMode.Crap ? "crap" : "complexity";
        SessionStore.Save(_session);
        RefreshInspector();
    }

    void OnLanguage(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LanguageBox.SelectedItem is not string selected)
            return;
        _session.Language = selected == "Auto" ? "auto" : selected;
        SessionStore.Save(_session);
        if (!string.IsNullOrWhiteSpace(_session.Folder))
            DiagramPublisher.WriteRecipe(_session.Folder, ScanCommand.TestCommandFor(_session.Folder), ScanCommand.CommandLine(_session.Folder, _session.Language), ScanCommand.MutateCommandFor(_session.Folder), ScanCommand.CoverageFileFor(_session.Folder));
        RefreshInspector();
    }

    void OnAgent(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || AgentBox.SelectedItem is not string selected)
            return;
        _session.Agent = selected;
        SessionStore.Save(_session);
    }

    internal void OpenFolder(string folder)
    {
        var diagramPath = DiagramPublisher.DiagramPath(folder);
        if (File.Exists(diagramPath))
            Diagram.Document = DiagramLoader.Load(diagramPath);
        else
        {
            var detected = Scanners.Resolve(folder, _session.Language);
            Diagram.Document = new DiagramDocument
            {
                Title = Path.GetFileName(folder),
                Note = detected is null
                    ? "No supported language was detected. The companion cannot scan this folder yet."
                    : "Waiting for the companion to write the diagram. Start it, then refresh."
            };
        }
        var root = Diagram.Document.Nodes.SingleOrDefault(node => node.Parent is null && node.Kind != "foreign");
        if (root is not null && Diagram.Document.Nodes.Any(node => node.Parent == root.Id))
            Diagram.Open(root.Id);
        else
            RefreshInspector();
        var previous = _session.Folder;
        var restartCompanion = Companion.IsLive
            && !string.Equals(previous, folder, StringComparison.OrdinalIgnoreCase);
        _session.Folder = folder;
        SessionStore.Save(_session);
        DiagramPublisher.WriteRecipe(folder, ScanCommand.TestCommandFor(folder), ScanCommand.CommandLine(folder, _session.Language), ScanCommand.MutateCommandFor(folder), ScanCommand.CoverageFileFor(folder));
        _pulse.WatchFile(diagramPath);
        if (restartCompanion)
            _ = StartCompanionAsync();
        CrapMode.IsEnabled = Diagram.Document.SupportsCrap;
        if (!Diagram.Document.SupportsCrap && Diagram.Mode == PaintMode.Crap)
        {
            ComplexityMode.IsChecked = true;
            Diagram.Mode = PaintMode.Complexity;
        }
    }

    void ShowEmpty()
    {
        _pulse.Stop();
        Diagram.Document = new DiagramDocument
        {
            Title = "No project",
            Note = "Open a project folder. The picture appears after the companion runs the scanner."
        };
        RefreshInspector();
    }

    void RefreshInspector()
    {
        ProjectText.Text = string.IsNullOrWhiteSpace(_session.Folder) ? "No folder open." : _session.Folder;
        var detected = string.IsNullOrWhiteSpace(_session.Folder) ? null : Scanners.For(_session.Folder);
        LanguageNote.Text = _session.Language == "auto"
            ? detected is null ? "Auto does not recognize this folder." : "Auto detects " + detected.Name + "."
            : "Using " + _session.Language + ".";
        LegendText.Text = Diagram.Mode == PaintMode.Crap
            ? "Calm ≤ 8. Warning through 20. Hot above 20. Slate has no score yet."
            : "1–4 very good. 5–7 good. 8–10 med. 11–20 bad. 21+ very bad. Slate is data or a library.";
        if (Diagram.Mode == PaintMode.Crap && Diagram.Document?.CoverageReady != true)
            LegendText.Text = "No coverage report yet. Slate means not scored, not a failure.";
        PathText.Text = Diagram.PathText;
        DetailText.Text = Diagram.DetailText;
        FillList();
        BackButton.IsVisible = Diagram.CanGoBack;
        var proposal = !string.IsNullOrWhiteSpace(_session.Folder) && File.Exists(DiagramPublisher.ProposalPath(_session.Folder));
        var live = _companionRunning && Companion.IsLive;
        var folderOpen = !string.IsNullOrWhiteSpace(_session.Folder);
        RefreshButton.IsEnabled = live && folderOpen;
        ProposalButton.IsEnabled = live && folderOpen;
        ProposalButton.Content = !proposal ? "Ask for a proposal" : _showingProposal ? "Show real diagram" : "Show proposal";
        ToolTip.SetTip(ProposalButton, live
            ? "Asks the companion for a what-if picture."
            : "Start the companion first.");
        StartCompanionButton.Content = live ? "Restart companion" : "Start companion";
        ToolTip.SetTip(RefreshButton, live
            ? "Runs the tests, then redraws the diagram."
            : "Start the companion first.");
    }

    void OnAskGrok(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
        {
            CompanionStatus.Text = "Open a project first.";
            return;
        }
        if (!Companion.IsLive)
        {
            CompanionStatus.Text = "Start the companion first. Nothing was sent.";
            return;
        }
        DiagramPublisher.PostRefresh(
            _session.Folder,
            ScanCommand.TestCommandFor(_session.Folder),
            ScanCommand.CommandLine(_session.Folder, _session.Language));
        _ = Companion.SendInputAsync(GrokLaunch.WakeLine);
        CompanionStatus.Text = "Asked the companion to run the tests, then the scanner.";
    }

    internal void ReloadDiagram()
    {
        var folder = _session.Folder;
        if (string.IsNullOrWhiteSpace(folder) || _showingProposal)
            return;
        var path = DiagramPublisher.DiagramPath(folder);
        if (!File.Exists(path))
            return;
        try
        {
            Diagram.ReplaceDocument(DiagramLoader.Load(path));
            RefreshInspector();
        }
        catch (IOException)
        {
        }
    }

    void ShowCard(DiagramNode node)
    {
        if (_cardWindow is null)
        {
            _cardWindow = new ClassCardWindow();
            _cardWindow.Closed += (_, _) => _cardWindow = null;
        }
        _cardWindow.ShowNode(this, Diagram, node);
    }

    void RefreshOne(DiagramNode node)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
            return;
        if (!_companionRunning || !Companion.IsLive)
        {
            CompanionStatus.Text = "Start the companion first. Nothing was sent.";
            return;
        }
        DiagramPublisher.PostRefreshNode(
            _session.Folder,
            node,
            ScanCommand.TestCommandFor(_session.Folder),
            ScanCommand.CommandLine(_session.Folder, _session.Language));
        _ = Companion.SendInputAsync(GrokLaunch.WakeLine);
        CompanionStatus.Text = "Asked the companion to refresh " + node.Name + ".";
    }

    void OnProposal(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
            return;
        var path = DiagramPublisher.ProposalPath(_session.Folder);
        if (!File.Exists(path))
        {
            if (!_companionRunning || !Companion.IsLive)
            {
                CompanionStatus.Text = "Start the companion first. Nothing was sent.";
                return;
            }
            DiagramPublisher.PostProposal(_session.Folder);
            _ = Companion.SendInputAsync(GrokLaunch.WakeLine);
            CompanionStatus.Text = "Asked the companion for a proposal.";
            return;
        }
        _showingProposal = !_showingProposal;
        var real = DiagramPublisher.DiagramPath(_session.Folder);
        Diagram.Document = DiagramLoader.Load(_showingProposal ? path : real);
        ProposalButton.Content = _showingProposal ? "Show real diagram" : "Show proposal";
        RefreshInspector();
    }

    void FillList()
    {
        MemberRows.Children.Clear();
        var document = Diagram.Document;
        if (document is null)
            return;
        var selected = Diagram.SelectedNode;
        var children = document.Nodes
            .Where(node => node.Kind != "foreign" && node.Parent == (selected?.Id ?? Diagram.CurrentParentId))
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ToList();
        if (selected is null)
        {
            ListHeading.Text = "On this level";
            foreach (var child in children)
                AddChildRow(child);
            return;
        }
        if (children.Count > 0)
        {
            ListHeading.Text = selected.Name;
            foreach (var child in children)
                AddChildRow(child);
        }
        var members = (selected.Members ?? []).Where(member => member.Kind != "field").ToList();
        var fields = (selected.Members ?? []).Where(member => member.Kind == "field").ToList();
        if (members.Count == 0 && fields.Count == 0)
        {
            if (children.Count == 0)
                ListHeading.Text = selected.Name;
            return;
        }
        if (children.Count == 0)
            ListHeading.Text = selected.Name;
        foreach (var member in members.OrderByDescending(member => member.Cc).ThenBy(member => member.Name, StringComparer.Ordinal))
            AddMemberRow(selected, member);
        foreach (var field in fields.OrderBy(field => field.Line))
            AddMemberRow(selected, field);
    }

    void AddChildRow(DiagramNode child)
    {
        var score = Diagram.Mode == PaintMode.Crap && child.CrapMu is double mu
            ? mu.ToString("0.0").PadLeft(6) + "  "
            : child.WorstCc is int cc ? "cc " + cc.ToString().PadLeft(2) + "  " : "         ";
        var button = RowButton(score + child.Name);
        var id = child.Id;
        button.Click += (_, _) => Diagram.Select(id);
        MemberRows.Children.Add(button);
    }

    void AddMemberRow(DiagramNode owner, DiagramMember member)
    {
        var button = RowButton(DiagramScene.ShortMember(member));
        var line = member.Line;
        button.Click += (_, _) =>
        {
            var file = member.File ?? owner.File;
            if (file is not null)
                SourceEditor.Open(file, line);
        };
        MemberRows.Children.Add(button);
    }

    static Button RowButton(string label) => new()
    {
        Content = label,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
        FontSize = 12,
        Background = Brushes.Transparent,
        Foreground = new SolidColorBrush(Color.Parse("#F3EFE8")),
        Padding = new Avalonia.Thickness(4, 3),
        MinHeight = 0
    };

    async void OnStartAgent(object? sender, RoutedEventArgs e) => await StartCompanionAsync();

    internal async Task StartCompanionAsync()
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
        {
            CompanionStatus.Text = "Open a project first.";
            return;
        }
        if (_session.Agent != "Grok")
        {
            CompanionStatus.Text = "This companion cannot be started yet.";
            return;
        }
        var grok = GrokLaunch.Find();
        var grokMissing = grok == "grok" ? !ExistsOnPath("grok") : !File.Exists(grok);
        if (grokMissing)
        {
            CompanionStatus.Text = "grok.exe was not found.";
            return;
        }
        if (Companion.IsLive)
            Companion.Kill();
        _companionRunning = false;
        if (StartProcess is not null)
            await StartProcess();
        else
            await Companion.LaunchProcess(
                _session.Folder,
                grok,
                "--always-approve",
                "--cwd", _session.Folder,
                "--rules", GrokLaunch.Rules,
                GrokLaunch.LaunchPrompt);
        _companionRunning = StartProcess is not null || Companion.IsLive;
        CompanionStatus.Text = _companionRunning ? "Running in this folder." : "The companion did not start.";
        RefreshInspector();
    }

    internal static bool ExistsOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;
        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (File.Exists(Path.Combine(directory, name)) || File.Exists(Path.Combine(directory, name + ".exe")))
                return true;
        }
        return false;
    }
}
