using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using pious_project_viewer.Diagram;

namespace pious_project_viewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Diagram.ViewChanged += (_, _) => RefreshInspector();
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "diagram.json");
        Diagram.Document = File.Exists(path)
            ? DiagramLoader.Load(path)
            : new DiagramDocument { Title = "Diagram file not found" };
        RefreshInspector();
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

    void OnBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Diagram.GoBack();

    void RefreshInspector()
    {
        PathText.Text = Diagram.PathText;
        DetailText.Text = Diagram.DetailText;
        BackButton.IsEnabled = Diagram.CanGoBack;
    }
}
