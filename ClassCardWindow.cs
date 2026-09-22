using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PiousProjectViewer.Diagram;

namespace PiousProjectViewer;

public sealed class ClassCardWindow : Window
{
    DiagramNode? _node;

    public ClassCardWindow()
    {
        Width = 680;
        Height = 760;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#161513");
        Foreground = Brush("#F3EFE8");
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close();
            e.Handled = true;
        };
    }

    public void ShowNode(Window owner, DiagramView diagram, DiagramNode node)
    {
        _node = node;
        Title = node.Name;
        var root = new DockPanel { Margin = new Avalonia.Thickness(22) };
        var header = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(header, Dock.Top);
        var parent = diagram.Document?.Nodes.FirstOrDefault(item => item.Id == node.Parent);
        header.Children.Add(Text(node.Name, 22, "#F3EFE8", FontWeight.SemiBold));
        header.Children.Add(Text((parent?.Name ?? diagram.Document?.Title ?? "") + "    class", 13, "#9A948A"));
        header.Children.Add(Text(diagram.Describe(node), 13, "#F3EFE8"));
        header.Children.Add(Text(diagram.ColumnHeader, 12, "#8E887F", family: "Cascadia Mono,Consolas,monospace"));
        root.Children.Add(header);

        var body = new StackPanel { Spacing = 2 };
        foreach (var member in Ordered(diagram, node))
        {
            var line = member.Line;
            var button = new Button
            {
                Content = diagram.MemberLabel(member),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
                FontSize = 13,
                Background = Brushes.Transparent,
                Foreground = Brush("#F3EFE8"),
                Padding = new Avalonia.Thickness(2, 2)
            };
            button.Click += (_, _) =>
            {
                if (_node?.File is string file)
                    SourceEditor.Open(file, line);
            };
            body.Children.Add(button);
        }
        body.Children.Add(Text("Relationships", 13, "#C4BEB4", FontWeight.SemiBold, new Avalonia.Thickness(0, 16, 0, 4)));
        foreach (var line in Relationships(diagram, node))
            body.Children.Add(Text(line, 13, "#9A948A"));
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
        if (IsVisible)
            Activate();
        else
            Show(owner);
    }

    static IEnumerable<DiagramMember> Ordered(DiagramView diagram, DiagramNode node)
    {
        var members = node.Members ?? [];
        return diagram.Mode == PaintMode.Crap && diagram.Document?.CoverageReady == true
            ? members.OrderBy(member => member.Kind == "field").ThenByDescending(member => member.Crap ?? -1)
            : members.OrderBy(member => member.Kind == "field").ThenByDescending(member => member.Cc);
    }

    static IEnumerable<string> Relationships(DiagramView diagram, DiagramNode node)
    {
        var document = diagram.Document;
        if (document is null)
            yield break;
        string NameOf(string id) => document.Nodes.FirstOrDefault(item => item.Id == id)?.Name ?? id;
        var depends = document.Edges.Where(edge => edge.From == node.Id).Select(edge => NameOf(edge.To)).Distinct().OrderBy(name => name).ToList();
        var usedBy = document.Edges.Where(edge => edge.To == node.Id).Select(edge => NameOf(edge.From)).Distinct().OrderBy(name => name).ToList();
        if (depends.Count == 0 && usedBy.Count == 0)
        {
            yield return "No direct relationships.";
            yield break;
        }
        foreach (var name in depends)
            yield return "depends on  " + name;
        foreach (var name in usedBy)
            yield return "used by  " + name;
    }

    static TextBlock Text(string value, double size, string color, FontWeight weight = FontWeight.Normal, Avalonia.Thickness? margin = null, string? family = null) => new()
    {
        Text = value,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brush(color),
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Avalonia.Thickness(0),
        FontFamily = family is null ? FontFamily.Default : new FontFamily(family)
    };

    static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
