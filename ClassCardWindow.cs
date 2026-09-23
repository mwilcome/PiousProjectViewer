using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PiousProjectViewer.Diagram;

namespace PiousProjectViewer;

public sealed class ClassCardWindow : Window
{
    const string Paper = "#F4F6F8";
    const string Muted = "#8B93A1";
    const string Calm = "#3DDC97";
    const string Warning = "#F0C14A";
    const string Hot = "#FF5C7A";

    DiagramNode? _node;

    public ClassCardWindow()
    {
        Width = 780;
        Height = 760;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#12141A");
        Foreground = Brush(Paper);
        FontFamily = new FontFamily("fonts:Inter#Inter");
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
        var header = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(header, Dock.Top);
        var parent = diagram.Document?.Nodes.FirstOrDefault(item => item.Id == node.Parent);
        var leveled = diagram.Document?.Nodes.Any(item => item.Rank != 0) == true;
        if (leveled)
            header.Children.Add(Text("Level " + node.Rank, 13, Muted));
        if (!string.IsNullOrWhiteSpace(parent?.Name))
            header.Children.Add(Text(parent.Name, 13, Muted));
        header.Children.Add(Text(node.Name, 22, Paper, FontWeight.SemiBold));
        header.Children.Add(Text(StatLine(diagram, node), 13, Paper));
        var crap = diagram.Mode == PaintMode.Crap && diagram.Document?.CoverageReady == true;
        var mutation = diagram.Document?.MutationReady == true;
        header.Children.Add(new Border
        {
            BorderBrush = Brush("#3A3834"),
            BorderThickness = new Avalonia.Thickness(0, 10, 0, 1),
            Padding = new Avalonia.Thickness(0, 0, 0, 6),
            Child = HeaderRow(crap, mutation)
        });
        root.Children.Add(header);

        var body = new StackPanel { Spacing = 0 };
        foreach (var member in Ordered(diagram, node))
        {
            var line = member.Line;
            var button = new Button
            {
                Content = MemberRow(member, crap, mutation),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                Foreground = Brush(Paper),
                Padding = new Avalonia.Thickness(0, 2)
            };
            button.Click += (_, _) =>
            {
                var file = member.File ?? _node?.File;
                if (file is not null)
                    SourceEditor.Open(file, line);
            };
            body.Children.Add(button);
        }
        body.Children.Add(Text("Relationships", 13, "#C4BEB4", FontWeight.SemiBold, new Avalonia.Thickness(0, 16, 0, 4)));
        foreach (var line in Relationships(diagram, node))
            body.Children.Add(Text(line, 13, Muted));
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
        if (IsVisible)
            Activate();
        else
            Show(owner);
    }

    static string StatLine(DiagramView diagram, DiagramNode node)
    {
        if (diagram.Mode == PaintMode.Crap && diagram.Document?.CoverageReady == true && node.CrapMu is double mu)
        {
            var rollup = new CrapRollup(mu, node.CrapMax ?? mu, node.CrapSigma ?? 0);
            var band = rollup.Band.Length == 0 ? rollup.Band : char.ToUpper(rollup.Band[0]) + rollup.Band[1..];
            return $"CRAP   {rollup.Mu:0.0} average    {rollup.Max:0.0} worst    {rollup.Sigma:0.0} spread    {band}";
        }
        if (node.WorstCc is int cc)
            return $"Complexity {cc}, {Heat.Word(cc)}.";
        return diagram.Describe(node);
    }

    static IEnumerable<DiagramMember> Ordered(DiagramView diagram, DiagramNode node)
    {
        var members = node.Members ?? [];
        return diagram.Mode == PaintMode.Crap && diagram.Document?.CoverageReady == true
            ? members.OrderBy(member => member.Kind is "field" or "html" or "scss").ThenByDescending(member => member.Crap ?? member.Cc)
            : members.OrderBy(member => member.Kind is "field" or "html" or "scss").ThenByDescending(member => member.Cc);
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

    static Grid HeaderRow(bool crap, bool mutation)
    {
        var grid = Table(crap, mutation);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Put(grid, 0, "Name", TextAlignment.Left, Muted, 11, false, 1);
        var col = 1;
        if (crap)
        {
            Put(grid, col, "CRAP", TextAlignment.Center, "#C4BEB4", 11, false, 0, 3);
            Put(grid, col, "Crap", TextAlignment.Right, Muted, 11, false, 1);
            Put(grid, col + 1, "CC", TextAlignment.Right, Muted, 11, false, 1);
            Put(grid, col + 2, "Cov", TextAlignment.Right, Muted, 11, false, 1);
            col += 3;
        }
        else
        {
            Put(grid, col, "CC", TextAlignment.Right, Muted, 11, false, 1);
            col += 1;
        }
        if (mutation)
        {
            Put(grid, col, "Mutation", TextAlignment.Center, "#C4BEB4", 11, false, 0, 3);
            Put(grid, col, "Killed", TextAlignment.Right, Muted, 11, false, 1);
            Put(grid, col + 1, "Survived", TextAlignment.Right, Muted, 11, false, 1);
            Put(grid, col + 2, "Uncovered", TextAlignment.Right, Muted, 11, false, 1);
        }
        return grid;
    }

    static Grid MemberRow(DiagramMember member, bool crap, bool mutation)
    {
        var grid = Table(crap, mutation);
        var scored = member.Kind is not ("field" or "html" or "scss");
        Put(grid, 0, RowName(member), TextAlignment.Left, Paper, 13, true);
        if (!scored)
            return grid;
        var col = 1;
        if (crap)
        {
            Put(grid, col, member.Crap is double score ? score.ToString("0.0") : "—", TextAlignment.Right, CrapText(member.Crap), 13, true);
            Put(grid, col + 1, member.Cc.ToString(), TextAlignment.Right, CcText(member.Cc), 13, true);
            Put(grid, col + 2, member.Coverage is double coverage ? coverage.ToString("0.#") + "%" : "—", TextAlignment.Right, CovText(member.Coverage), 13, true);
            col += 3;
        }
        else
        {
            Put(grid, col, member.Cc.ToString(), TextAlignment.Right, CcText(member.Cc), 13, true);
            col += 1;
        }
        if (!mutation)
            return grid;
        if (member.Killed is not int killed)
        {
            Put(grid, col, "no sites", TextAlignment.Left, Muted, 13, false, span: 3);
            return grid;
        }
        Put(grid, col, killed.ToString(), TextAlignment.Right, killed > 0 ? Calm : Muted, 13, true);
        Put(grid, col + 1, (member.Survived ?? 0).ToString(), TextAlignment.Right, (member.Survived ?? 0) > 0 ? Warning : Muted, 13, true);
        Put(grid, col + 2, (member.Uncovered ?? 0).ToString(), TextAlignment.Right, (member.Uncovered ?? 0) > 0 ? Hot : Muted, 13, true);
        return grid;
    }

    static string RowName(DiagramMember member) => member.Kind switch
    {
        "field" => "field   " + DiagramScene.DisplayName(member.Name),
        "html" => "html    " + member.Name,
        "scss" => "scss    " + member.Name,
        _ => (member.IsPublic ? "+  " : "−  ") + DiagramScene.DisplayName(member.Name)
    };

    static string CrapText(double? crap) => crap switch
    {
        null => Muted,
        <= 8 => Calm,
        <= 30 => Warning,
        _ => Hot
    };

    static string CcText(int cc) => cc switch
    {
        <= 4 => Calm,
        <= 7 => "#A8D5B5",
        <= 10 => Warning,
        <= 20 => "#E0A070",
        _ => Hot
    };

    static string CovText(double? coverage) => coverage switch
    {
        null => Muted,
        >= 90 => Calm,
        >= 70 => Warning,
        _ => Hot
    };

    static Grid Table(bool crap, bool mutation)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Column(double width) => grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        if (crap)
        {
            Column(56);
            Column(40);
            Column(56);
        }
        else
            Column(40);
        if (mutation)
        {
            Column(64);
            Column(76);
            Column(88);
        }
        return grid;
    }

    static void Put(Grid grid, int column, string text, TextAlignment align, string color, double size, bool mono, int row = 0, int span = 1)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = Brush(color),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = align,
            Margin = new Avalonia.Thickness(align == TextAlignment.Left ? 0 : 4, row == 0 ? 0 : 2, align == TextAlignment.Right ? 8 : 12, 0),
            FontFamily = mono
                ? new FontFamily("Cascadia Mono, Cascadia Code, Consolas, monospace")
                : new FontFamily("fonts:Inter#Inter")
        };
        Grid.SetColumn(block, column);
        Grid.SetRow(block, row);
        if (span > 1)
            Grid.SetColumnSpan(block, span);
        grid.Children.Add(block);
    }

    static TextBlock Text(string value, double size, string color, FontWeight weight = FontWeight.Normal, Avalonia.Thickness? margin = null) => new()
    {
        Text = value,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brush(color),
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Avalonia.Thickness(0)
    };

    static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
