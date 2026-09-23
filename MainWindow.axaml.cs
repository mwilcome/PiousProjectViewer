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
    string _language = "auto";
    bool _companionRunning;
    bool _showingProposal;
    bool _proposalAnnounced;
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
        _session.Language = "auto";
        _language = "auto";
        LanguageBox.SelectedItem = "Auto";
        AgentBox.SelectedItem = Agents.Names.Contains(_session.Agent) ? _session.Agent : Agents.Names[0];
        AgentName.Text = AgentBox.SelectedItem as string ?? Agents.Names[0];
        AgentBox.IsVisible = Agents.Names.Count > 1;
        AgentName.IsVisible = Agents.Names.Count < 2;
        Diagram.Mode = _session.Mode == "crap" ? PaintMode.Crap : PaintMode.Complexity;
        if (Diagram.Mode == PaintMode.Crap)
            CrapMode.IsChecked = true;
        _session.Folder = null;
        ShowEmpty();
        _pulse.Due += (_, _) => Dispatcher.UIThread.Post(OnPiousFile);
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

    void OnDeclutter(object? sender, RoutedEventArgs e)
    {
        Diagram.Declutter = !Diagram.Declutter;
        DeclutterButton.Content = Diagram.Declutter ? "Show wires" : "Hide wires";
        WireHint.Text = Diagram.Declutter ? "Shows the lines again." : "Hides the lines. The uses stay.";
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
        _language = selected == "Auto" ? "auto" : selected;
        RefreshInspector();
    }

    void OnAgent(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || AgentBox.SelectedItem is not string selected)
            return;
        _session.Agent = selected;
        SessionStore.Save(_session);
    }

    void WriteProjectRecipe(string folder)
    {
        DiagramPublisher.WriteRecipe(folder, "", ScanCommand.CommandLine(folder, _language), ScanCommand.MutateCommandFor(folder), null);
        CompanionStatus.Text = "Wrote .pious in this folder.";
    }

    internal void OpenFolder(string folder)
    {
        var diagramPath = DiagramPublisher.DiagramPath(folder);
        var previous = _session.Folder;
        var switched = !string.IsNullOrWhiteSpace(previous) && !SameFolder(previous, folder);
        var stoppedCompanion = switched && Companion.IsLive;
        if (stoppedCompanion)
            Companion.Kill();
        _session.Folder = folder;
        _showingProposal = false;
        _proposalAnnounced = File.Exists(DiagramPublisher.ProposalPath(folder));
        _companionRunning = false;
        var recognized = Scanners.Resolve(folder, _language) is not null;
        if (File.Exists(diagramPath))
            Diagram.Document = DiagramLoader.Load(diagramPath);
        else if (!recognized)
        {
            Diagram.Document = new DiagramDocument
            {
                Title = "No diagram available.",
                Note = "Scanner could not detect supported language specific files. Ensure you are in a real project folder with supported languages."
            };
        }
        else if (!File.Exists(DiagramPublisher.RecipePath(folder)))
        {
            Diagram.Document = new DiagramDocument
            {
                Title = Path.GetFileName(folder),
                Note = "Generate project and start agent."
            };
        }
        else
        {
            Diagram.Document = new DiagramDocument
            {
                Title = Path.GetFileName(folder),
                Note = "Waiting for the companion to write the diagram. Start it, then refresh."
            };
        }
        var root = Diagram.Document.Nodes.SingleOrDefault(node => node.Parent is null && node.Kind != "foreign");
        if (root is not null && Diagram.Document.Nodes.Any(node => node.Parent == root.Id))
            Diagram.Open(root.Id);
        else
            RefreshInspector();
        SessionStore.Save(_session);
        if (Directory.Exists(Path.GetDirectoryName(diagramPath)))
            _pulse.WatchFile(diagramPath);
        else
            _pulse.Stop();
        CrapMode.IsEnabled = Diagram.Document.SupportsCrap;
        if (!Diagram.Document.SupportsCrap && Diagram.Mode == PaintMode.Crap)
        {
            ComplexityMode.IsChecked = true;
            Diagram.Mode = PaintMode.Complexity;
        }
        if (stoppedCompanion)
            CompanionStatus.Text = "Stopped the companion. It was still in the other folder.";
    }

    static bool SameFolder(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    void ShowEmpty()
    {
        _showingProposal = false;
        _proposalAnnounced = false;
        _pulse.Stop();
        Diagram.Document = new DiagramDocument
        {
            Title = "No project",
            Note = "Open a folder."
        };
        RefreshInspector();
    }

    void RefreshInspector()
    {
        var folderOpen = !string.IsNullOrWhiteSpace(_session.Folder);
        if (!folderOpen)
        {
            ProjectText.Text = "No project";
            ProjectPath.Text = "Open a folder to begin.";
        }
        else
        {
            var name = Path.GetFileName(_session.Folder!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            ProjectText.Text = string.IsNullOrWhiteSpace(name) ? _session.Folder : name;
            ProjectPath.Text = _session.Folder;
        }
        var detected = folderOpen ? Scanners.Resolve(_session.Folder!, _language) : null;
        LanguageNote.Text = !folderOpen
            ? "Used when a folder is opened."
            : _language == "auto"
                ? detected is null ? "This folder is not recognized." : "Detected " + detected.Name + "."
                : "Using " + _language + ".";
        FillLegend();
        PathText.Text = Diagram.PathText;
        DetailText.Text = Diagram.DetailText;
        FillList();
        BackButton.IsVisible = Diagram.CanGoBack;
        var proposal = folderOpen && File.Exists(DiagramPublisher.ProposalPath(_session.Folder!));
        ViewText.Text = !proposal
            ? "Writes a second picture. The scan stays."
            : _showingProposal ? "Showing the proposal." : "Showing the scanned diagram.";
        var live = _companionRunning && Companion.IsLive;
        SetPrimary(OpenButton, !folderOpen);
        RefreshButton.IsEnabled = live && folderOpen;
        ProposalButton.IsEnabled = live && folderOpen;
        ToolTip.SetTip(ProposalButton, live
            ? "Asks the companion to write a proposal. The picture on screen stays."
            : "Start the companion first.");
        SwitchProposalButton.IsVisible = proposal;
        SwitchProposalButton.Content = _showingProposal ? "Switch to scanned diagram" : "Switch to proposal";
        SetPrimary(SwitchProposalButton, proposal && !_showingProposal);
        ToolTip.SetTip(SwitchProposalButton, _showingProposal ? "Shows the scanned diagram." : "Shows the proposal.");
        StartCompanionButton.Content = live ? "Restart companion" : "Start companion";
        var recognized = !string.IsNullOrWhiteSpace(_session.Folder) && Scanners.Resolve(_session.Folder, _language) is not null;
        var hasRecipe = recognized && File.Exists(DiagramPublisher.RecipePath(_session.Folder!));
        GenerateButton.IsVisible = recognized && !hasRecipe;
        StartCompanionButton.IsVisible = folderOpen && !GenerateButton.IsVisible;
        ToolTip.SetTip(RefreshButton, live
            ? "Runs the tests, then redraws the diagram."
            : "Start the companion first.");
    }

    void FillLegend()
    {
        LegendRow.Children.Clear();
        if (Diagram.Mode == PaintMode.Crap && Diagram.Document?.CoverageReady != true)
        {
            LegendRow.Children.Add(Swatch(BoxPaint.CrapNeutral, "No score yet"));
            return;
        }
        if (Diagram.Mode == PaintMode.Crap)
        {
            LegendRow.Children.Add(Swatch(BoxPaint.CrapCalm, "≤ 8"));
            LegendRow.Children.Add(Swatch(BoxPaint.CrapWarning, "≤ 20"));
            LegendRow.Children.Add(Swatch(BoxPaint.CrapHot, "Above"));
            LegendRow.Children.Add(Swatch(BoxPaint.CrapNeutral, "None"));
            return;
        }
        LegendRow.Children.Add(Swatch(Heat.Color(1), "1–4"));
        LegendRow.Children.Add(Swatch(Heat.Color(5), "5–7"));
        LegendRow.Children.Add(Swatch(Heat.Color(8), "8–10"));
        LegendRow.Children.Add(Swatch(Heat.Color(11), "11–20"));
        LegendRow.Children.Add(Swatch(Heat.Color(21), "21+"));
        LegendRow.Children.Add(Swatch(Heat.Color(null), "None"));
    }

    static StackPanel Swatch(string hex, string label) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 5,
        Margin = new Avalonia.Thickness(0, 0, 10, 4),
        Children =
        {
            new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new Avalonia.CornerRadius(4),
                Background = new SolidColorBrush(Color.Parse(hex)),
                VerticalAlignment = VerticalAlignment.Center
            },
            new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#8B93A1")),
                VerticalAlignment = VerticalAlignment.Center
            }
        }
    };

    static void SetPrimary(Button button, bool on)
    {
        if (on)
        {
            if (!button.Classes.Contains("primary"))
                button.Classes.Add("primary");
        }
        else
            button.Classes.Remove("primary");
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
            DiagramPublisher.SavedTest(_session.Folder),
            ScanCommand.CommandLine(_session.Folder, _language));
        _ = Companion.SendInputAsync(GrokLaunch.WakeLine);
        CompanionStatus.Text = "Asked the companion to run the tests, then the scanner.";
    }

    void OnPiousFile()
    {
        var path = _pulse.LastPath;
        var diagram = !string.IsNullOrWhiteSpace(path)
            && Path.GetFileName(path).Equals("diagram.json", StringComparison.OrdinalIgnoreCase);
        if (diagram)
            ReloadDiagram();
        else if (_showingProposal && !string.IsNullOrWhiteSpace(path)
            && Path.GetFileName(path).Equals("proposal.json", StringComparison.OrdinalIgnoreCase))
            ShowCurrentPicture();
        else
            RefreshInspector();
        if (_showingProposal || string.IsNullOrWhiteSpace(_session.Folder))
            return;
        if (!File.Exists(DiagramPublisher.ProposalPath(_session.Folder)))
        {
            _proposalAnnounced = false;
            return;
        }
        if (_proposalAnnounced)
            return;
        _proposalAnnounced = true;
        CompanionStatus.Text = "Proposal is ready. Switch to proposal to see it.";
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

    internal void RefreshOne(DiagramNode node)
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
            DiagramPublisher.SavedTest(_session.Folder),
            ScanCommand.CommandLine(_session.Folder, _language));
        _ = Companion.SendInputAsync(GrokLaunch.WakeLine);
        CompanionStatus.Text = "Asked the companion to refresh " + node.Name + ".";
    }

    void OnGenerateProposal(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
            return;
        if (!_companionRunning || !Companion.IsLive)
        {
            CompanionStatus.Text = "Start the companion first. Nothing was sent.";
            return;
        }
        DiagramPublisher.PostProposal(_session.Folder);
        _ = Companion.SendInputAsync(GrokLaunch.WakeLine);
        _proposalAnnounced = false;
        CompanionStatus.Text = "Asked the companion for a proposal.";
    }

    void OnSwitchProposal(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
            return;
        if (!File.Exists(DiagramPublisher.ProposalPath(_session.Folder)))
            return;
        _showingProposal = !_showingProposal;
        ShowCurrentPicture();
    }

    void ShowCurrentPicture()
    {
        var folder = _session.Folder;
        if (string.IsNullOrWhiteSpace(folder))
            return;
        var path = _showingProposal
            ? DiagramPublisher.ProposalPath(folder)
            : DiagramPublisher.DiagramPath(folder);
        if (!File.Exists(path))
            return;
        var document = DiagramLoader.Load(path);
        if (_showingProposal)
        {
            document.Title = "Proposal";
            document.Note = "This is the proposal. The scanned diagram is unchanged.";
        }
        Diagram.Document = document;
        RefreshInspector();
    }

    void FillList()
    {
        MemberRows.Children.Clear();
        var document = Diagram.Document;
        if (document is null)
            return;
        var selected = Diagram.SelectedNode;
        ListHeading.IsVisible = selected is not null;
        ListHeading.Text = selected?.Name ?? "";
        var children = selected is null
            ? Diagram.VisibleNodes().ToList()
            : DiagramScene.Shown(document, selected.Id);
        if (selected is null)
        {
            foreach (var child in children)
                AddChildRow(child);
            return;
        }
        if (children.Count > 0)
        {
            foreach (var child in children)
                AddChildRow(child);
        }
        var members = (selected.Members ?? []).Where(member => member.Kind != "field").ToList();
        var fields = (selected.Members ?? []).Where(member => member.Kind == "field").ToList();
        if (members.Count == 0 && fields.Count == 0)
            return;
        foreach (var member in members.OrderByDescending(member => member.Crap ?? member.Cc).ThenBy(member => member.Name, StringComparer.Ordinal))
            AddMemberRow(selected, member);
        foreach (var field in fields.OrderBy(field => field.Line))
            AddMemberRow(selected, field);
    }

    void AddChildRow(DiagramNode child)
    {
        var score = Diagram.Mode == PaintMode.Crap && child.CrapMu is double mu
            ? mu.ToString("0.0").PadLeft(6) + "  "
            : child.WorstCc is int cc ? "cc " + cc.ToString().PadLeft(2) + "  " : "         ";
        var kindList = Diagram.Document is null ? [] : DiagramScene.FileKinds(Diagram.Document, child);
        var kinds = kindList.Contains("html") || kindList.Contains("scss") ? string.Join(" · ", kindList) : "";
        var button = RowButton(score + child.Name + (kinds.Length == 0 ? "" : "   " + kinds));
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
        FontSize = 13,
        Classes = { "row" },
        Background = Brushes.Transparent,
        Foreground = new SolidColorBrush(Color.Parse("#F4F6F8")),
        Padding = new Avalonia.Thickness(2, 4),
        MinHeight = 0
    };

    async void OnGenerate(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
            return;
        if (Scanners.Resolve(_session.Folder, _language) is null)
            return;
        WriteProjectRecipe(_session.Folder);
        _pulse.WatchFile(DiagramPublisher.DiagramPath(_session.Folder));
        RefreshInspector();
        await StartCompanionAsync();
        if (_companionRunning)
            CompanionStatus.Text = "Wrote .pious in this folder. Agent is running.";
    }

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
        if (Scanners.Resolve(_session.Folder, _language) is null)
        {
            CompanionStatus.Text = "Open a project folder before starting the companion.";
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
                GrokLaunch.Opening(_session.Folder));
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
