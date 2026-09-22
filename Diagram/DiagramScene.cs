using System;
using System.Collections.Generic;
using System.Linq;

namespace pious_project_viewer.Diagram;

public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

public readonly record struct Point2(double X, double Y);

public sealed record RoutedEdge(string From, string To, bool Violating, IReadOnlyList<Point2> Points);

public sealed record Scene(
    Box Frame,
    string Title,
    IReadOnlyDictionary<string, Box> Boxes,
    IReadOnlyList<RoutedEdge> Edges)
{
    public static Scene Empty { get; } = new(new Box(0, 0, 0, 0), "", new Dictionary<string, Box>(), []);

    public Box Bounds
    {
        get
        {
            var bounds = Frame;
            foreach (var box in Boxes.Values)
                bounds = Union(bounds, box);
            return bounds;
        }
    }

    static Box Union(Box a, Box b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new Box(x, y, right - x, bottom - y);
    }
}

public static class DiagramScene
{
    public const double BoxWidth = 168;
    public const double BoxHeight = 64;

    const double GapX = 46;
    const double GapY = 52;
    const double PadX = 40;
    const double PadTop = 52;
    const double PadBottom = 32;

    public static Scene Build(DiagramDocument document, string? parent)
    {
        var structural = document.Nodes.Where(node => node.Kind != "foreign" && node.Parent == parent).ToList();
        var structuralIds = structural.Select(node => node.Id).ToHashSet();
        var foreignIds = document.Edges
            .Where(edge => document.Nodes.Any(node => node.Id == edge.To && node.Kind == "foreign"))
            .Where(edge => VisibleStructural(document, edge.From, structuralIds) is not null)
            .Select(edge => edge.To)
            .ToHashSet();
        var visible = structural.Concat(document.Nodes.Where(node => foreignIds.Contains(node.Id))).ToList();
        var rows = visible.Where(node => node.Kind != "foreign")
            .GroupBy(node => node.Rank)
            .OrderByDescending(group => group.Key)
            .Select(group => group.OrderBy(node => node.Name, StringComparer.Ordinal).ToList())
            .ToList();
        var foreign = visible.Where(node => node.Kind == "foreign")
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ToList();

        var columns = rows.Count == 0 ? 1 : Math.Min(4, rows.Max(row => row.Count));
        var innerWidth = columns * BoxWidth + Math.Max(0, columns - 1) * GapX;
        var boxes = new Dictionary<string, Box>();
        var y = PadTop;
        foreach (var row in rows)
        {
            for (var index = 0; index < row.Count; index += 4)
            {
                var slice = row.Skip(index).Take(4).ToList();
                var rowWidth = slice.Count * BoxWidth + Math.Max(0, slice.Count - 1) * GapX;
                var x = PadX + (innerWidth - rowWidth) / 2;
                foreach (var node in slice)
                {
                    boxes[node.Id] = new Box(x, y, BoxWidth, BoxHeight);
                    x += BoxWidth + GapX;
                }
                y += BoxHeight + GapY;
            }
        }

        var contentBottom = rows.Count == 0 ? PadTop : y - GapY;
        var frame = new Box(0, 0, innerWidth + PadX * 2, contentBottom + PadBottom);
        PlaceForeign(foreign, frame, boxes);

        var title = parent is null
            ? document.Title
            : document.Nodes.First(node => node.Id == parent).Name;
        return new Scene(frame, title, boxes, Routes(document, parent, boxes));
    }

    static void PlaceForeign(List<DiagramNode> foreign, Box frame, Dictionary<string, Box> boxes)
    {
        if (foreign.Count == 0)
            return;
        var stack = foreign.Count * BoxHeight + Math.Max(0, foreign.Count - 1) * 28;
        var y = frame.Y + Math.Max(PadTop, (frame.Height - stack) / 2);
        var x = frame.Right + 78;
        foreach (var node in foreign)
        {
            boxes[node.Id] = new Box(x, y, BoxWidth, BoxHeight);
            y += BoxHeight + 28;
        }
    }

    static List<RoutedEdge> Routes(DiagramDocument document, string? parent, Dictionary<string, Box> boxes)
    {
        var seen = new HashSet<string>();
        var routes = new List<RoutedEdge>();
        foreach (var edge in document.Edges)
        {
            var from = VisibleEnd(document, parent, edge.From, boxes);
            var to = VisibleEnd(document, parent, edge.To, boxes);
            if (from is null || to is null || from == to || !seen.Add(from + ">" + to))
                continue;
            var violating = document.Edges.Any(candidate =>
                candidate.Violating
                && VisibleEnd(document, parent, candidate.From, boxes) == from
                && VisibleEnd(document, parent, candidate.To, boxes) == to);
            routes.Add(new RoutedEdge(from, to, violating, Points(boxes[from], boxes[to], violating)));
        }
        return routes;
    }

    static string? VisibleStructural(DiagramDocument document, string id, HashSet<string> structuralIds)
    {
        var nodes = document.Nodes.ToDictionary(node => node.Id);
        if (!nodes.TryGetValue(id, out var current))
            return null;
        while (true)
        {
            if (structuralIds.Contains(current.Id))
                return current.Id;
            if (current.Parent is null || !nodes.TryGetValue(current.Parent, out current))
                return null;
        }
    }

    public static string? VisibleEnd(
        DiagramDocument document,
        string? parent,
        string id,
        IReadOnlyDictionary<string, Box> boxes)
    {
        var nodes = document.Nodes.ToDictionary(node => node.Id);
        if (!nodes.TryGetValue(id, out var current))
            return null;
        while (true)
        {
            if (current.Parent == parent && boxes.ContainsKey(current.Id))
                return current.Id;
            if (current.Parent is null || !nodes.TryGetValue(current.Parent, out current))
                return null;
        }
    }

    public static IReadOnlyList<Point2> Points(Box from, Box to, bool violating)
    {
        if (violating)
        {
            var channel = Math.Min(from.X, to.X) - 36;
            return
            [
                new Point2(from.X, from.CenterY),
                new Point2(channel, from.CenterY),
                new Point2(channel, to.CenterY),
                new Point2(to.X, to.CenterY)
            ];
        }

        var dx = to.CenterX - from.CenterX;
        var dy = to.CenterY - from.CenterY;
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            var start = dx >= 0 ? new Point2(from.Right, from.CenterY) : new Point2(from.X, from.CenterY);
            var end = dx >= 0 ? new Point2(to.X, to.CenterY) : new Point2(to.Right, to.CenterY);
            return [start, end];
        }

        var down = dy >= 0;
        var top = down ? new Point2(from.CenterX, from.Bottom) : new Point2(from.CenterX, from.Y);
        var bottom = down ? new Point2(to.CenterX, to.Y) : new Point2(to.CenterX, to.Bottom);
        return [top, bottom];
    }
}
