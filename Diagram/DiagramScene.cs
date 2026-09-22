using System;
using System.Collections.Generic;
using System.Linq;

namespace PiousProjectViewer.Diagram;

public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

public readonly record struct Point2(double X, double Y);

public sealed record RoutedEdge(string From, string To, bool Violating, IReadOnlyList<Point2> Points, string Label);

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
    public const double BoxWidth = 220;
    public const double BoxHeight = 64;
    const double LineHeight = 15;

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
                var heights = slice.Select(node => HeightFor(node)).ToList();
                var rowHeight = heights.Max();
                var rowWidth = slice.Count * BoxWidth + Math.Max(0, slice.Count - 1) * GapX;
                var x = PadX + (innerWidth - rowWidth) / 2;
                for (var item = 0; item < slice.Count; item++)
                {
                    boxes[slice[item].Id] = new Box(x, y, BoxWidth, heights[item]);
                    x += BoxWidth + GapX;
                }
                y += rowHeight + GapY;
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

    public static double HeightFor(DiagramNode node)
    {
        if (node.Kind == "foreign")
            return BoxHeight;
        var lines = LinesIn(node).Count;
        return lines == 0 ? BoxHeight : 50 + lines * LineHeight + 10;
    }

    public static IReadOnlyList<string> LinesIn(DiagramNode node)
    {
        var methods = (node.Members ?? [])
            .Where(member => member.Kind != "field")
            .OrderByDescending(member => member.IsPublic)
            .ThenByDescending(member => member.Cc)
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .ToList();
        var lines = methods.Take(12).Select(ShortMember).ToList();
        if (methods.Count > lines.Count)
            lines.Add("+ " + (methods.Count - lines.Count) + " more");
        return lines;
    }

    public static string ShortMember(DiagramMember member)
    {
        var name = member.Name;
        var paren = name.IndexOf('(');
        if (paren > 0)
            name = name[..paren];
        if (name.Length > 24)
            name = name[..22] + "...";
        return (member.IsPublic ? "+ " : "- ") + name;
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
        var groups = new Dictionary<(string From, string To), List<DiagramEdge>>();
        foreach (var edge in document.Edges)
        {
            var from = VisibleEnd(document, parent, edge.From, boxes);
            var to = VisibleEnd(document, parent, edge.To, boxes);
            if (from is null || to is null || from == to)
                continue;
            var key = (from, to);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }
            list.Add(edge);
        }

        var routes = new List<RoutedEdge>();
        foreach (var (key, list) in groups)
        {
            var obstacles = boxes.Where(pair => pair.Key != key.From && pair.Key != key.To).Select(pair => pair.Value).ToList();
            var violating = list.Any(edge => edge.Violating);
            var pairs = list
                .Select(edge => ShortName(document, edge.From) + " → " + ShortName(document, edge.To))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToList();
            var label = string.Join("\n", pairs.Take(12));
            if (pairs.Count > 12)
                label += "\n+" + (pairs.Count - 12) + " more";
            routes.Add(new RoutedEdge(key.From, key.To, violating, Points(boxes[key.From], boxes[key.To], violating, obstacles), label));
        }
        return routes;
    }

    static string ShortName(DiagramDocument document, string id) =>
        document.Nodes.FirstOrDefault(node => node.Id == id)?.Name ?? id;

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

    public static IReadOnlyList<Point2> Points(Box from, Box to, bool violating, IReadOnlyList<Box>? obstacles = null)
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

        var (start, end) = Ports(from, to);
        var blocks = obstacles ?? [];
        if (!blocks.Any(box => SegmentHits(start, end, box)))
            return [start, end];

        var hits = blocks.Where(box => SegmentHits(start, end, box)).ToList();
        var minX = hits.Min(box => box.X) - 28;
        var maxX = hits.Max(box => box.Right) + 28;
        var minY = hits.Min(box => box.Y) - 28;
        var maxY = hits.Max(box => box.Bottom) + 28;
        Point2[][] candidates =
        [
            [start, new Point2(start.X, minY), new Point2(end.X, minY), end],
            [start, new Point2(start.X, maxY), new Point2(end.X, maxY), end],
            [start, new Point2(minX, start.Y), new Point2(minX, end.Y), end],
            [start, new Point2(maxX, start.Y), new Point2(maxX, end.Y), end]
        ];
        return candidates
            .OrderBy(path => PathHits(path, blocks))
            .ThenBy(path => PathLength(path))
            .First();
    }

    static (Point2 Start, Point2 End) Ports(Box from, Box to)
    {
        var dx = to.CenterX - from.CenterX;
        var dy = to.CenterY - from.CenterY;
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            return dx >= 0
                ? (new Point2(from.Right, from.CenterY), new Point2(to.X, to.CenterY))
                : (new Point2(from.X, from.CenterY), new Point2(to.Right, to.CenterY));
        }
        return dy >= 0
            ? (new Point2(from.CenterX, from.Bottom), new Point2(to.CenterX, to.Y))
            : (new Point2(from.CenterX, from.Y), new Point2(to.CenterX, to.Bottom));
    }

    static int PathHits(IReadOnlyList<Point2> path, IReadOnlyList<Box> blocks)
    {
        var count = 0;
        for (var i = 1; i < path.Count; i++)
            count += blocks.Count(box => SegmentHits(path[i - 1], path[i], box));
        return count;
    }

    static double PathLength(IReadOnlyList<Point2> path)
    {
        var length = 0d;
        for (var i = 1; i < path.Count; i++)
        {
            var dx = path[i].X - path[i - 1].X;
            var dy = path[i].Y - path[i - 1].Y;
            length += Math.Sqrt(dx * dx + dy * dy);
        }
        return length;
    }

    static bool SegmentHits(Point2 a, Point2 b, Box box)
    {
        var inflated = new Box(box.X - 8, box.Y - 8, box.Width + 16, box.Height + 16);
        if (Inside(a, inflated) || Inside(b, inflated))
            return true;
        Point2[] corners =
        [
            new(inflated.X, inflated.Y),
            new(inflated.Right, inflated.Y),
            new(inflated.Right, inflated.Bottom),
            new(inflated.X, inflated.Bottom)
        ];
        for (var i = 0; i < 4; i++)
        {
            if (SegmentsCross(a, b, corners[i], corners[(i + 1) % 4]))
                return true;
        }
        return false;
    }

    static bool Inside(Point2 point, Box box) =>
        point.X >= box.X && point.X <= box.Right && point.Y >= box.Y && point.Y <= box.Bottom;

    static bool SegmentsCross(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    static double Cross(Point2 origin, Point2 end, Point2 point) =>
        (end.X - origin.X) * (point.Y - origin.Y) - (end.Y - origin.Y) * (point.X - origin.X);
}
