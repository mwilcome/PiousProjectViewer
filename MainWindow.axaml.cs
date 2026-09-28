using System;
using System.Collections.Generic;
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
    bool _stylesTab;
    string? _stylesFolder;
    StylePicture? _styles;
    bool _companionRunning;
    bool _showingProposal;
    bool _showingStyleProposal;
    bool _styleProposalAnnounced;
    bool _showCalmStyles;

    bool _proposalAnnounced;
    ClassCardWindow? _cardWindow;
    bool _ready;
    bool _sashDrag;
    bool _sashLeft;
    bool _sashFromSnap;
    bool _leftSnapped;
    bool _rightSnapped;
    double _sashDownX;
    double _sashDownWidth;
    const double LeftFloor = 180;
    const double RightFloor = 220;
    const double MiddleGap = 120;
    const double SnapPull = 48;
    const double SashSize = 6;

    internal Session SessionState => _session;

    internal Func<Task>? StartProcess { get; set; }


    public MainWindow()
    {
        InitializeComponent();
        Shell.SizeChanged += (_, _) => KeepMiddleGap();
        Diagram.ViewChanged += (_, _) => RefreshInspector();
        StylePictureView.ViewChanged += (_, _) => { if (_stylesTab) RefreshInspector(); };
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
        Diagram.Mode = _session.Mode switch
        {
            "crap" => PaintMode.Crap,
            "distance" => PaintMode.Distance,
            _ => PaintMode.Complexity
        };
        if (Diagram.Mode == PaintMode.Crap)
            CrapMode.IsChecked = true;
        else if (Diagram.Mode == PaintMode.Distance)
            DistanceMode.IsChecked = true;
        RememberProject.IsChecked = _session.RememberProject;
        var remembered = _session.RememberedFolder ?? _session.Folder;
        if (_session.RememberProject && !string.IsNullOrWhiteSpace(remembered) && Directory.Exists(remembered))
            OpenFolder(remembered);
        else
        {
            if (string.IsNullOrWhiteSpace(_session.RememberedFolder))
                _session.RememberedFolder = _session.Folder;
            _session.Folder = null;
            ShowEmpty();
        }
        _pulse.Due += (_, _) => Dispatcher.UIThread.Post(OnPiousFile);
        _ready = true;
    }

    void OnRememberProject(object? sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _session.RememberProject = RememberProject.IsChecked == true;
        SessionStore.Save(_session);
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
        if (_stylesTab && StylePictureView.SelectedBox is not null)
            StylePictureView.ClearBox();
        else if (_stylesTab && StylePictureView.Problem is not null)
            StylePictureView.ClearSelection();
        else
            Diagram.GoBack();
        e.Handled = true;
    }

    void OnBack(object? sender, RoutedEventArgs e)
    {
        if (_stylesTab && StylePictureView.SelectedBox is not null)
        {
            StylePictureView.ClearBox();
            return;
        }
        if (_stylesTab && StylePictureView.Problem is not null)
        {
            StylePictureView.ClearSelection();
            return;
        }
        Diagram.GoBack();
    }

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
        Diagram.Mode = DistanceMode.IsChecked == true
            ? PaintMode.Distance
            : CrapMode.IsChecked == true ? PaintMode.Crap : PaintMode.Complexity;
        _session.Mode = Diagram.Mode switch
        {
            PaintMode.Crap => "crap",
            PaintMode.Distance => "distance",
            _ => "complexity"
        };
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
        _session.RememberedFolder = folder;
        _styles = null;
        _stylesFolder = null;
        _stylesTab = false;
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
        var styles = detected?.ShowsStyles == true;
        StylesTab.IsVisible = styles;
        if (!styles && _stylesTab)
            _stylesTab = false;
        Diagram.IsVisible = !_stylesTab;
        StylePictureView.IsVisible = _stylesTab;
        ColorGroup.IsVisible = !_stylesTab;
        StyleLegend.IsVisible = _stylesTab;
        SetPrimary(ClassesTab, !_stylesTab);
        SetPrimary(StylesTab, _stylesTab);
        RefreshHint.IsVisible = false;
        RefreshButton.Content = _stylesTab ? "Refresh styles" : "Refresh diagram";
        if (_stylesTab)
            FillStyleLegend();
        else
            FillLegend();
        if (_stylesTab)
            ShowStyleOutline();
        else
        {
            PathText.Text = Diagram.PathText;
            DetailText.Text = Diagram.DetailText;
            FillList();
            BackButton.IsVisible = Diagram.CanGoBack;
        }
        var proposal = folderOpen && File.Exists(DiagramPublisher.ProposalPath(_session.Folder!));
        var styleProposal = folderOpen && File.Exists(StyleProposalPath(_session.Folder!));
        var live = _companionRunning && Companion.IsLive;
        var target = _stylesTab
            ? _styles is null ? null : StyleProblems.Build(_styles).FirstOrDefault(item => item.Id == StylePictureView.Problem)?.Title
            : Diagram.SelectedNode?.Name;
        var armed = folderOpen && live && !string.IsNullOrWhiteSpace(target);
        ProposalButton.Content = armed ? "Propose fix for " + target : "Generate proposal";
        ProposalButton.IsEnabled = armed;
        ViewText.Text = string.IsNullOrWhiteSpace(target) ? "Select a class." : "";
        ViewText.IsVisible = string.IsNullOrWhiteSpace(target);
        ToolTip.SetTip(ProposalButton, !folderOpen
            ? "Open a project first."
            : string.IsNullOrWhiteSpace(target)
                ? "Select a class."
                : !live
                    ? "Start the companion first."
                    : "Asks the companion for one fix. The scan stays.");
        SetPrimary(OpenButton, !folderOpen);
        RefreshButton.IsEnabled = _stylesTab ? folderOpen : live && folderOpen;
        SetPrimary(RefreshButton, RefreshButton.IsEnabled && !armed);
        SetPrimary(ProposalButton, armed);
        SetPrimary(StartCompanionButton, false);
        ToolTip.SetTip(RefreshButton, _stylesTab
            ? "Rescans the stylesheets. The companion is not required."
            : live ? "Runs the tests, then redraws the diagram." : "Start the companion first.");
        var showing = _stylesTab ? _showingStyleProposal : _showingProposal;
        SwitchProposalButton.IsVisible = _stylesTab ? styleProposal : proposal;
        SwitchProposalButton.Content = _stylesTab
            ? showing ? "How it is" : "Proposal"
            : showing ? "Switch to scanned diagram" : "Switch to proposal";
        SetPrimary(SwitchProposalButton, false);
        ToolTip.SetTip(SwitchProposalButton, showing ? "Shows how it is now." : "Shows the proposal.");
        StartCompanionButton.Content = live ? "Restart companion" : "Start companion";
        var recognized = !string.IsNullOrWhiteSpace(_session.Folder) && Scanners.Resolve(_session.Folder, _language) is not null;
        var hasRecipe = recognized && File.Exists(DiagramPublisher.RecipePath(_session.Folder!));
        GenerateButton.IsVisible = recognized && !hasRecipe;
        StartCompanionButton.IsVisible = folderOpen && !GenerateButton.IsVisible;

    }

    void FillLegend()
    {
        LegendRow.Children.Clear();
        if (Diagram.Mode == PaintMode.Distance)
        {
            ColorHint.Text = "A red folder is one other code depends on. A red box touches many types.";
            LegendRow.Children.Add(Swatch("#3DDC97", "Light"));
            LegendRow.Children.Add(Swatch("#F0C14A", "Some"));
            LegendRow.Children.Add(Swatch("#FF5C7A", "Heavy"));
            return;
        }
        if (Diagram.Mode == PaintMode.Crap && Diagram.Document?.CoverageReady != true)
        {
            ColorHint.Text = "CRAP needs a coverage report. Until then, nothing here is scored.";
            LegendRow.Children.Add(Swatch(BoxPaint.CrapNeutral, "No score yet"));
            return;
        }
        if (Diagram.Mode == PaintMode.Crap)
        {
            ColorHint.Text = "Complexity plus how much of the method is untested. Lower is safer to change.";
            LegendRow.Children.Add(Swatch(BoxPaint.CrapCalm, "≤ 8"));
            LegendRow.Children.Add(Swatch(BoxPaint.CrapWarning, "≤ 20"));
            LegendRow.Children.Add(Swatch(BoxPaint.CrapHot, "Above"));
            LegendRow.Children.Add(Swatch(BoxPaint.CrapNeutral, "None"));
            return;
        }
        ColorHint.Text = "How many paths a method has. Lower is simpler.";
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

    void OnToggleProject(object? sender, RoutedEventArgs e)
    {
        ProjectBody.IsVisible = !ProjectBody.IsVisible;
        ProjectToggle.Content = ProjectBody.IsVisible ? "Project  ▾" : "Project  ▸";
    }

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

    void OnClassesTab(object? sender, RoutedEventArgs e)
    {
        _stylesTab = false;
        RefreshInspector();
    }

    void OnStylesTab(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
            return;
        _stylesTab = true;
        if (_styles is null || _stylesFolder != _session.Folder)
            LoadStyles(write: true);
        else
        {
            StyleFiles.Write(_session.Folder, _styles);
            StylePictureView.Show(_styles);
        }
        RefreshInspector();
    }

    void LoadStyles(bool write)
    {
        _styles = StyleScanner.Scan(_session.Folder!);
        _stylesFolder = _session.Folder;
        if (write)
            StyleFiles.Write(_session.Folder!, _styles);
        StylePictureView.Show(_styles);
    }

    void KeepMiddleGap()
    {
        if (_sashDrag || Shell.Bounds.Width <= 1)
            return;
        var limit = Shell.Bounds.Width - MiddleGap;
        var left = _leftSnapped ? SashSize : Assigned(0);
        var right = _rightSnapped ? SashSize : Assigned(2);
        var overflow = left + right - limit;
        if (overflow <= 0.5)
            return;
        if (!_leftSnapped)
        {
            var give = Math.Min(overflow, Math.Max(0, left - LeftFloor));
            left -= give;
            overflow -= give;
        }
        if (overflow > 0.5 && !_rightSnapped)
        {
            var give = Math.Min(overflow, Math.Max(0, right - RightFloor));
            right -= give;
            overflow -= give;
        }
        if (overflow > 0.5 && !_rightSnapped)
        {
            overflow -= right - SashSize;
            right = SashSize;
            _rightSnapped = true;
        }
        if (overflow > 0.5 && !_leftSnapped)
        {
            left = SashSize;
            _leftSnapped = true;
        }
        OutlineBody.IsVisible = !_leftSnapped;
        ToolsBody.IsVisible = !_rightSnapped;
        WriteColumn(0, left);
        WriteColumn(2, right);
    }

    void OnSashPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _sashDrag = true;
        _sashLeft = sender == LeftSash || sender == LeftGrip;
        _sashFromSnap = _sashLeft ? _leftSnapped : _rightSnapped;
        _sashDownX = e.GetPosition(Shell).X;
        _sashDownWidth = Assigned(_sashLeft ? 0 : 2);
        e.Pointer.Capture((Control)sender!);
        e.Handled = true;
    }

    void OnSashMoved(object? sender, PointerEventArgs e)
    {
        if (!_sashDrag || e.Pointer.Captured != sender)
            return;
        var proposed = _sashDownWidth + (_sashLeft ? 1 : -1) * (e.GetPosition(Shell).X - _sashDownX);
        if (_sashLeft)
            DragSash(0, proposed, LeftFloor, ref _leftSnapped);
        else
            DragSash(2, proposed, RightFloor, ref _rightSnapped);
        e.Handled = true;
    }

    void OnSashReleased(object? sender, RoutedEventArgs e)
    {
        if (!_sashDrag)
            return;
        _sashDrag = false;
        if (e is PointerReleasedEventArgs released && released.Pointer.Captured == sender)
            released.Pointer.Capture(null);
        var index = _sashLeft ? 0 : 2;
        var floor = _sashLeft ? LeftFloor : RightFloor;
        var width = Assigned(index);
        var max = MaxFor(index);
        var opened = width >= floor - 0.5 || (max < floor && width >= max - 0.5 && width > SashSize);
        if (!opened)
        {
            if (_sashLeft)
                _leftSnapped = true;
            else
                _rightSnapped = true;
            ShowSection(_sashLeft, false);
            SetColumn(index, SashSize);
            return;
        }
        if (_sashLeft)
            _leftSnapped = false;
        else
            _rightSnapped = false;
        ShowSection(_sashLeft, true);
        SetColumn(index, width);
    }

    void DragSash(int index, double proposed, double floor, ref bool snapped)
    {
        var max = MaxFor(index);
        var left = index == 0;
        proposed = Math.Max(SashSize, Math.Min(proposed, max));
        if (_sashFromSnap)
        {
            snapped = false;
            ShowSection(left, true);
            SetColumn(index, proposed);
            return;
        }
        if (snapped)
        {
            if (proposed < floor)
            {
                ShowSection(left, false);
                SetColumn(index, SashSize);
                return;
            }
            snapped = false;
            ShowSection(left, true);
            SetColumn(index, proposed);
            return;
        }
        if (proposed >= floor)
        {
            ShowSection(left, true);
            SetColumn(index, proposed);
            return;
        }
        if (floor - proposed >= SnapPull)
        {
            snapped = true;
            ShowSection(left, false);
            SetColumn(index, SashSize);
            return;
        }
        ShowSection(left, true);
        SetColumn(index, Math.Min(floor, max));
    }

    double Assigned(int index)
    {
        var length = Shell.ColumnDefinitions[index].Width;
        return length.IsAbsolute ? length.Value : SashSize;
    }

    double MaxFor(int index)
    {
        if (Shell.Bounds.Width <= 1)
            return 800;
        var other = Assigned(index == 0 ? 2 : 0);
        return Math.Max(SashSize, Shell.Bounds.Width - other - MiddleGap);
    }

    void ShowSection(bool left, bool open)
    {
        if (left)
            OutlineBody.IsVisible = open;
        else
            ToolsBody.IsVisible = open;
    }

    void SetColumn(int index, double width) => WriteColumn(index, Math.Min(width, MaxFor(index)));

    void WriteColumn(int index, double width)
    {
        var column = Shell.ColumnDefinitions[index];
        column.MinWidth = SashSize;
        column.MaxWidth = double.PositiveInfinity;
        column.Width = new GridLength(Math.Max(SashSize, Math.Round(width)));
    }

    static string StyleProposalPath(string folder) => Path.Combine(folder, ".pious", "styles-proposal.json");

    void ShowStyleOutline()
    {
        if (_showingStyleProposal && !string.IsNullOrWhiteSpace(_session.Folder))
        {
            FillStyleProposal();
            return;
        }
        StylePictureView.SetProposal(null);
        if (_styles is null)
        {
            PathText.Text = "Styles";
            DetailText.Text = "Open the Styles tab to scan.";
            ListHeading.IsVisible = false;
            BackButton.IsVisible = false;
            MemberRows.Children.Clear();
            return;
        }
        var template = _styles.Templates.FirstOrDefault(item => item.Id == StylePictureView.SelectedBox);
        var problem = StyleProblems.Build(_styles).FirstOrDefault(item => item.Id == StylePictureView.Problem);
        if (problem is not null)
        {
            FillProblem(problem);
            return;
        }
        if (template is null)
        {
            var problems = StyleProblems.Build(_styles);
            PathText.Text = "Styles";
            DetailText.Text = problems.Count == 0
                ? "No style problems."
                : "Fold into the shared file: a shared home already exists. Create a shared home: the name is copied and nothing owns it.";
            ListHeading.IsVisible = false;
            BackButton.IsVisible = false;
            FillStyleList();
            return;
        }
        var hits = _styles.Selectors.Where(selector => selector.Hits.Contains(template.Id)).ToList();
        PathText.Text = template.Name;
        DetailText.Text = hits.Count == 0
            ? "No stylesheet hits this file."
            : hits.Count == 1
                ? "1 style hits this file. Click it to open the stylesheet."
                : hits.Count + " styles hit this file. Click one to open its stylesheet.";
        ListHeading.IsVisible = false;
        BackButton.IsVisible = true;
        FillTemplateStyles(template, hits);
        OutlineScroll.Offset = new Avalonia.Vector(0, 0);
    }

    void FillStyleLegend()
    {
        StyleSwatches.Children.Clear();
        StyleSwatches.Children.Add(Swatch("#FF5C7A", "6 or more files"));
        StyleSwatches.Children.Add(Swatch("#F0C14A", "3 to 5 files"));
        StyleSwatches.Children.Add(Swatch("#8B93A7", "2 files"));
    }

    void FillStyleList()
    {
        MemberRows.Children.Clear();
        if (_styles is null)
            return;
        var problems = StyleProblems.Build(_styles);
        AddProblemGroup("FOLD INTO THE SHARED FILE", problems.Where(problem => problem.Kind == "fold"));
        AddProblemGroup("CREATE A SHARED HOME", problems.Where(problem => problem.Kind == "promote"));
        var calm = StyleProblems.Calm(_styles);
        if (calm.Count == 0)
            return;
        var hidden = new Button
        {
            Content = (_showCalmStyles ? "Hide" : "Hidden") + " (" + calm.Count + ")",
            Classes = { "row" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Padding = new Avalonia.Thickness(4, 8, 4, 4)
        };
        hidden.Click += (_, _) =>
        {
            _showCalmStyles = !_showCalmStyles;
            FillStyleList();
        };
        MemberRows.Children.Add(hidden);
        if (!_showCalmStyles)
            return;
        foreach (var (name, file) in calm)
            MemberRows.Children.Add(StyleHitButton(name, "one home · " + file, "#8B93A7"));
    }

    void AddProblemGroup(string heading, IEnumerable<StyleProblem> problems)
    {
        var items = problems.ToList();
        if (items.Count == 0)
            return;
        MemberRows.Children.Add(new TextBlock
        {
            Text = heading,
            Classes = { "section" },
            Margin = new Avalonia.Thickness(4, 10, 0, 4)
        });
        foreach (var problem in items)
        {
            var button = StyleHitButton(problem.Title, problem.Brief, problem.Color);
            var id = problem.Id;
            button.Click += (_, _) => StylePictureView.SelectProblem(id);
            MemberRows.Children.Add(button);
        }
    }

    void FillProblem(StyleProblem problem)
    {
        PathText.Text = problem.Title;
        DetailText.Text = "Stylesheets are one group. HTML files are the other. Click a row to highlight it.";
        ListHeading.IsVisible = false;
        BackButton.IsVisible = true;
        MemberRows.Children.Clear();
        if (problem.Kind is "fold" or "promote")
        {
            var rules = _styles!.Selectors.Where(selector => problem.Names.Contains(selector.Name)).ToList();
            AddSheetRows(rules);
            AddTemplateRows(rules.SelectMany(rule => rule.Hits));
        }
        OutlineScroll.Offset = new Avalonia.Vector(0, 0);
    }

    void FillStyleProposal()
    {
        var proposal = StyleProposal.Load(StyleProposalPath(_session.Folder!));
        PathText.Text = "Proposal";
        DetailText.Text = proposal is null || string.IsNullOrWhiteSpace(proposal.Summary)
            ? "The proposal file is there, but it could not be read."
            : proposal.Summary;
        ListHeading.IsVisible = false;
        BackButton.IsVisible = false;
        MemberRows.Children.Clear();
        if (proposal is null)
            return;
        MemberRows.Children.Add(SectionLabel("THE CHANGE"));
        if (proposal.Name.Length > 0)
            MemberRows.Children.Add(StyleHitButton(proposal.Name, proposal.Kind == "fold" ? "Fold into the shared file" : "Create a shared home", "#3DDC97"));
        foreach (var file in proposal.From)
            MemberRows.Children.Add(StyleHitButton(Path.GetFileName(file), "remove the copy", "#FF5C7A"));
        if (proposal.To.Length > 0)
            MemberRows.Children.Add(StyleHitButton(Path.GetFileName(proposal.To), "the home", "#7EB6D6"));
        StylePictureView.SetProposal(proposal);
    }

    void FillNameProblem(string name)
    {
        var rules = _styles!.Selectors.Where(selector => selector.Name == name).ToList();
        AddSheetRows(rules);
        AddTemplateRows(rules.SelectMany(rule => rule.Hits));
    }

    void FillChunkProblem()
    {
        var rules = _styles!.Selectors.Where(selector => selector.Mark.StartsWith("same chunk", StringComparison.Ordinal)).ToList();
        if (rules.Count > 0)
            MemberRows.Children.Add(SectionLabel("RULES  " + rules.Count));
        foreach (var rule in rules)
        {
            var sheet = _styles.Sheets.FirstOrDefault(item => item.Id == rule.SheetId);
            var from = sheet is null ? rule.FileName : Relative(sheet.File);
            var button = StyleHitButton(rule.Name, from, rule.Color);
            var path = sheet?.File;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var file = path;
                button.Click += (_, _) => CompanionStatus.Text = SourceEditor.Open(file, 1);
            }
            MemberRows.Children.Add(button);
        }
        AddTemplateRows(rules.SelectMany(rule => rule.Hits));
    }

    void FillGlobalProblem(string sheetId)
    {
        var rules = _styles!.Selectors.Where(selector => selector.SheetId == sheetId).ToList();
        AddSheetRows(rules);
        AddTemplateRows(rules.SelectMany(rule => rule.Hits));
    }

    void AddSheetRows(List<StyleSelector> rules)
    {
        var sheets = rules.Select(rule => rule.SheetId).Distinct(StringComparer.Ordinal)
            .Select(id => _styles!.Sheets.FirstOrDefault(item => item.Id == id))
            .Where(sheet => sheet is not null)
            .Cast<StyleSheet>()
            .ToList();
        if (sheets.Count == 0)
            return;
        MemberRows.Children.Add(SectionLabel("STYLESHEETS  " + sheets.Count));
        foreach (var sheet in sheets)
        {
            var button = StyleHitButton(sheet.Name, FolderOf(Relative(sheet.File)), "#7EB6D6");
            var id = sheet.Id;
            var file = sheet.File;
            button.Click += (_, _) =>
            {
                StylePictureView.SelectBox(id);
                CompanionStatus.Text = SourceEditor.Open(file, 1);
            };
            MemberRows.Children.Add(button);
        }
    }

    void AddTemplateRows(IEnumerable<string> templateIds)
    {
        var templates = templateIds.Distinct(StringComparer.Ordinal)
            .Select(id => _styles!.Templates.FirstOrDefault(item => item.Id == id))
            .Where(template => template is not null)
            .Cast<StyleTemplate>()
            .ToList();
        if (templates.Count == 0)
            return;
        MemberRows.Children.Add(SectionLabel("HTML  " + templates.Count));
        foreach (var template in templates)
        {
            var button = StyleHitButton(template.Name, "HTML", "#E6C27A");
            var id = template.Id;
            var file = template.File;
            button.Click += (_, _) =>
            {
                StylePictureView.SelectBox(id);
                CompanionStatus.Text = SourceEditor.Open(file, 1);
            };
            MemberRows.Children.Add(button);
        }
    }

    static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        Classes = { "section" },
        Margin = new Avalonia.Thickness(4, 12, 0, 4)
    };

    static string FolderOf(string relative)
    {
        var slash = relative.LastIndexOf('/');
        return slash <= 0 ? relative : relative[..slash];
    }

    void FillTemplateStyles(StyleTemplate template, List<StyleSelector> hits)
    {
        MemberRows.Children.Clear();
        var htmlPath = template.File;
        var html = StyleHitButton(template.Name, "Open this HTML", "#9AA3B2");
        html.Click += (_, _) => CompanionStatus.Text = SourceEditor.Open(htmlPath, 1);
        MemberRows.Children.Add(html);
        foreach (var selector in hits)
        {
            var sheet = _styles!.Sheets.FirstOrDefault(item => item.Id == selector.SheetId);
            var from = sheet is null ? selector.FileName : Relative(sheet.File);
            var button = StyleHitButton(selector.Name, from, selector.Color);
            var path = sheet?.File;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var file = path;
                button.Click += (_, _) => CompanionStatus.Text = SourceEditor.Open(file, 1);
            }
            ToolTip.SetTip(button, selector.Mark);
            MemberRows.Children.Add(button);
        }
        if (template.Unstyled.Count == 0)
            return;
        MemberRows.Children.Add(new TextBlock
        {
            Text = "NO RULE",
            Classes = { "section" },
            Margin = new Avalonia.Thickness(4, 12, 0, 4)
        });
        foreach (var missing in template.Unstyled)
        {
            var button = StyleHitButton("." + missing, "In this HTML, nothing styles it", "#F0C14A");
            var file = htmlPath;
            button.Click += (_, _) => CompanionStatus.Text = SourceEditor.Open(file, 1);
            MemberRows.Children.Add(button);
        }
    }

    string Relative(string file)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder) || string.IsNullOrWhiteSpace(file))
            return file;
        return Path.GetRelativePath(_session.Folder, file).Replace('\\', '/');
    }

    static Button StyleHitButton(string name, string from, string accent)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3,*"),
            ColumnSpacing = 8,
            MinHeight = 28
        };
        row.Children.Add(new Border
        {
            Width = 3,
            CornerRadius = new Avalonia.CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse(accent)),
            VerticalAlignment = VerticalAlignment.Stretch
        });
        var stack = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(stack, 1);
        stack.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#F4F6F8")),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = from,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse(accent)),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        row.Children.Add(stack);
        return new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Classes = { "row" },
            Background = Brushes.Transparent,
            Padding = new Avalonia.Thickness(4, 3),
            MinHeight = 0
        };
    }

    void OnAskGrok(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
        {
            CompanionStatus.Text = "Open a project first.";
            return;
        }
        if (_stylesTab)
        {
            LoadStyles(write: true);
            RefreshInspector();
            CompanionStatus.Text = Directory.Exists(Path.Combine(_session.Folder, ".pious"))
                ? "Scanned the styles and wrote .pious/styles-diagram.json."
                : "Scanned the styles. No file was written, because this folder has no .pious yet.";
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
            _proposalAnnounced = false;
        else if (!_proposalAnnounced)
        {
            _proposalAnnounced = true;
            CompanionStatus.Text = "Proposal is ready. Switch to proposal to see it.";
        }
        var stylesReady = File.Exists(StyleProposalPath(_session.Folder));
        if (!stylesReady)
        {
            _styleProposalAnnounced = false;
            return;
        }
        if (_styleProposalAnnounced)
            return;
        _styleProposalAnnounced = true;
        CompanionStatus.Text = "Style proposal is ready. Switch to the proposal to see it.";
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
        if (_stylesTab)
        {
            if (_styles is null || _stylesFolder != _session.Folder)
                LoadStyles(write: true);
            else
                StyleFiles.Write(_session.Folder, _styles);
            if (!File.Exists(StyleFiles.DiagramPath(_session.Folder)))
            {
                CompanionStatus.Text = "The styles scan was not saved. Generate the project first so .pious exists.";
                return;
            }
            var chosen = StyleProblems.Build(_styles!).FirstOrDefault(item => item.Id == StylePictureView.Problem)?.Title;
            DiagramPublisher.PostStyleProposal(_session.Folder, chosen);
        }
        else
        {
            if (!File.Exists(DiagramPublisher.DiagramPath(_session.Folder)))
            {
                CompanionStatus.Text = "There is no diagram yet. Refresh the diagram first.";
                return;
            }
            DiagramPublisher.PostProposal(_session.Folder);
        }
        _ = Companion.SendInputAsync(GrokLaunch.WakeLine);
        _proposalAnnounced = false;
        CompanionStatus.Text = "Asked the companion for a proposal.";
    }

    void OnSwitchProposal(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_session.Folder))
            return;
        if (_stylesTab)
        {
            if (!File.Exists(StyleProposalPath(_session.Folder)))
                return;
            _showingStyleProposal = !_showingStyleProposal;
            RefreshInspector();
            return;
        }
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
        var picture = Diagram.Document;
        var distance = Diagram.Mode == PaintMode.Distance && picture is not null
            ? MartinDistance.Measure(picture).FirstOrDefault(item => item.Id == child.Id)
            : default;
        var meta = Diagram.Mode == PaintMode.Distance
            ? distance.Types > 0 ? "D " + distance.Distance.ToString("0.00") : child.Abstract ? "abstract" : ""
            : Diagram.Mode == PaintMode.Crap && child.CrapMu is double mu
            ? mu.ToString("0.0")
            : child.WorstCc is int cc ? "cc " + cc : "";
        var kindList = Diagram.Document is null ? [] : DiagramScene.FileKinds(Diagram.Document, child);
        var kinds = kindList.Count == 0 ? "" : string.Join(" · ", kindList);
        var button = OutlineButton(child.Name, meta, kinds, Diagram.NodeColor(child));
        var id = child.Id;
        button.Click += (_, _) => Diagram.Select(id);
        MemberRows.Children.Add(button);
    }

    void AddMemberRow(DiagramNode owner, DiagramMember member)
    {
        var button = OutlineButton(DiagramScene.ShortMember(member), "", "", Diagram.MemberColor(member));
        var line = member.Line;
        button.Click += (_, _) =>
        {
            var file = member.File ?? owner.File;
            if (file is not null)
                SourceEditor.Open(file, line);
        };
        MemberRows.Children.Add(button);
    }

    static Button OutlineButton(string name, string meta, string kinds, string accent)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3,*"),
            ColumnSpacing = 8,
            MinHeight = 28
        };
        row.Children.Add(new Border
        {
            Width = 3,
            CornerRadius = new Avalonia.CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse(accent)),
            VerticalAlignment = VerticalAlignment.Stretch
        });
        var stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(stack, 1);
        stack.Children.Add(Trimmed(name, 13, "#F4F6F8", 200));
        var detail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(meta))
            detail.Children.Add(Trimmed(meta, 11, accent, 72));
        if (!string.IsNullOrWhiteSpace(kinds))
            detail.Children.Add(Trimmed(kinds, 11, "#9AA3B2", 120));
        if (detail.Children.Count > 0)
            stack.Children.Add(detail);
        row.Children.Add(stack);
        return new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Classes = { "row" },
            Background = Brushes.Transparent,
            Padding = new Avalonia.Thickness(4, 3),
            MinHeight = 0
        };
    }

    static TextBlock Trimmed(string text, double size, string color, double width) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Cascadia Mono, Cascadia Code, Consolas, Menlo, SF Mono, monospace"),
        FontSize = size,
        Foreground = new SolidColorBrush(Color.Parse(color)),
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = width
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
            CompanionStatus.Text = "The grok command was not found.";
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
