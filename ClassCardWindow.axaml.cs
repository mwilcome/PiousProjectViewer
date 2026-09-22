using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using pious_project_viewer.Diagram;

namespace pious_project_viewer;

public partial class ClassCardWindow : Window
{
    DiagramView? _diagram;
    DiagramNode? _node;

    public ClassCardWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    public void ShowNode(Window owner, DiagramView diagram, DiagramNode node)
    {
        _diagram = diagram;
        _node = node;
        Title = node.Name;
        OpenClassButton.Content = node.Name;
        OpenClassButton.IsEnabled = !string.IsNullOrWhiteSpace(node.File);
        var parent = diagram.Document?.Nodes.FirstOrDefault(item => item.Id == node.Parent);
        CardMeta.Text = parent is null ? "class" : parent.Name + "    class";
        CardSummary.Text = diagram.Describe(node);
        CardColumns.Text = diagram.ColumnHeader;
        CardHint.Text = "Click a method. It opens in VS Code, and this window stays here.";
        MemberRows.Children.Clear();
        var members = node.Members ?? [];
        var ordered = diagram.Mode == PaintMode.Crap && diagram.Document?.CoverageReady == true
            ? members.OrderByDescending(member => member.Crap ?? -1).ThenByDescending(member => member.Cc)
            : members.OrderByDescending(member => member.Cc).ThenBy(member => member.Name, System.StringComparer.Ordinal);
        var any = false;
        foreach (var member in ordered)
        {
            any = true;
            var line = member.Line;
            var button = new Button
            {
                Content = diagram.MemberLabel(member),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
                FontSize = 13,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#F3EFE8")),
                Padding = new Avalonia.Thickness(8, 3)
            };
            button.Click += (_, _) => OpenAt(line);
            MemberRows.Children.Add(button);
        }
        if (!any)
        {
            MemberRows.Children.Add(new TextBlock
            {
                Text = "Nothing was found in this type. Open the file to see it.",
                Foreground = new SolidColorBrush(Color.Parse("#B7B1A8")),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(8, 4, 0, 0)
            });
        }
        if (IsVisible)
            Activate();
        else
            Show(owner);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Close();
        e.Handled = true;
    }

    void OnOpenClass(object? sender, RoutedEventArgs e) => OpenAt(_node?.Line ?? 1);

    void OpenAt(int line)
    {
        if (_node?.File is not string file)
            return;
        CardHint.Text = SourceEditor.Open(file, line);
    }
}
