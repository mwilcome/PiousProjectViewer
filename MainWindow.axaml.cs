using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using pious_project_viewer.Diagram;

namespace pious_project_viewer;

public partial class MainWindow : Window
{
    Session _session = new();

    public MainWindow()
    {
        InitializeComponent();
        Diagram.ViewChanged += (_, _) => RefreshInspector();
        _session = SessionStore.Load();
        Diagram.Mode = _session.Mode == "crap" ? PaintMode.Crap : PaintMode.Complexity;
        if (Diagram.Mode == PaintMode.Crap)
            CrapMode.IsChecked = true;
        if (_session.Folder is not null && Directory.Exists(_session.Folder))
            OpenFolder(_session.Folder);
        else
            ShowEmpty();
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

    void OpenFolder(string folder)
    {
        var scanner = Scanners.For(folder);
        Diagram.Document = scanner is null
            ? new DiagramDocument
            {
                Title = Path.GetFileName(folder),
                Note = "No supported scanner for this folder yet. C# projects are the first scanner."
            }
            : scanner.Scan(folder);
        var root = Diagram.Document.Nodes.SingleOrDefault(node => node.Parent is null && node.Kind != "foreign");
        if (root is not null && Diagram.Document.Nodes.Any(node => node.Parent == root.Id))
            Diagram.Open(root.Id);
        else
            RefreshInspector();
        _session.Folder = folder;
        SessionStore.Save(_session);
        CrapMode.IsEnabled = Diagram.Document.SupportsCrap;
        if (!Diagram.Document.SupportsCrap && Diagram.Mode == PaintMode.Crap)
        {
            ComplexityMode.IsChecked = true;
            Diagram.Mode = PaintMode.Complexity;
        }
    }

    void ShowEmpty()
    {
        Diagram.Document = new DiagramDocument
        {
            Title = "No project",
            Note = "Open a project folder to scan it."
        };
        RefreshInspector();
    }

    void RefreshInspector()
    {
        ProjectText.Text = string.IsNullOrWhiteSpace(_session.Folder) ? "No folder open." : _session.Folder;
        ModeNote.Text = Diagram.Mode switch
        {
            PaintMode.Crap when !Diagram.Document?.SupportsCrap == true => "This scanner does not compute CRAP.",
            PaintMode.Crap when Diagram.Document?.CoverageReady != true => "No coverage report found. Tests were not run. Boxes stay neutral.",
            PaintMode.Crap => "CRAP uses the newest coverage.cobertura.xml in the folder.",
            _ => "Complexity ignores tests. Switch to CRAP when you want coverage in the color."
        };
        if (Diagram.Document?.SupportsCrap == true && Diagram.Document.CoverageReady != true && Diagram.Mode == PaintMode.Complexity)
            ModeNote.Text += " A coverage report is not loaded.";
        PathText.Text = Diagram.PathText;
        DetailText.Text = Diagram.DetailText;
        BackButton.IsEnabled = Diagram.CanGoBack;
    }
}
