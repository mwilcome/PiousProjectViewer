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
        var structural = Shown(document, parent);
        var structuralIds = document.Nodes.Where(node => node.Kind != "foreign" && node.Parent == parent).Select(node => node.Id).ToHashSet();
        var foreignIds = document.Edges
            .Where(edge => document.Nodes.Any(node => node.Id == edge.To && node.Kind == "foreign"))
            .Where(edge => VisibleStructural(document, edge.From, structuralIds) is not null)
            .Select(edge => edge.To)
            .ToHashSet();
        var foreign = document.Nodes.Where(node => foreignIds.Contains(node.Id))
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ToList();
        var rows = structural
            .GroupBy(node => node.Rank)
            .OrderByDescending(group => group.Key)
            .Select(group => group.OrderBy(node => node.Name, StringComparer.Ordinal).ToList())
            .ToList();

        const double maxRow = 980;
        var placedRows = new List<List<(DiagramNode Node, double Width, double Height)>>();
        foreach (var row in rows)
        {
            var slice = new List<(DiagramNode Node, double Width, double Height)>();
            var used = 0d;
            foreach (var node in row)
            {
                var width = WidthFor(node);
                var height = HeightFor(node);
                if (slice.Count > 0 && used + GapX + width > maxRow)
                {
                    placedRows.Add(slice);
                    slice = [];
                    used = 0;
                }
                slice.Add((node, width, height));
                used += (slice.Count == 1 ? 0 : GapX) + width;
            }
            if (slice.Count > 0)
                placedRows.Add(slice);
        }
        var innerWidth = placedRows.Count == 0 ? BoxWidth : placedRows.Max(slice => slice.Sum(item => item.Width) + Math.Max(0, slice.Count - 1) * GapX);
        var boxes = new Dictionary<string, Box>();
        var y = PadTop;
        foreach (var slice in placedRows)
        {
            var rowHeight = slice.Max(item => item.Height);
            var rowWidth = slice.Sum(item => item.Width) + Math.Max(0, slice.Count - 1) * GapX;
            var x = PadX + Math.Max(0, (innerWidth - rowWidth) / 2);
            foreach (var item in slice)
            {
                boxes[item.Node.Id] = new Box(x, y, item.Width, item.Height);
                x += item.Width + GapX;
            }
            y += rowHeight + GapY;
        }

        var contentBottom = placedRows.Count == 0 ? PadTop : y - GapY;
        var frame = new Box(0, 0, innerWidth + PadX * 2, contentBottom + PadBottom);
        PlaceForeign(foreign, frame, boxes);

        var title = parent is null
            ? document.Title
            : document.Nodes.First(node => node.Id == parent).Name;
        return new Scene(frame, title, boxes, Routes(document, parent, boxes));
    }

    public static List<DiagramNode> Shown(DiagramDocument document, string? parent) =>
        document.Nodes.Where(node => node.Kind != "foreign" && node.Parent == parent)
            .Select(node => Hoist(document, node))
            .GroupBy(node => node.Id)
            .Select(group => group.First())
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ToList();

    static DiagramNode Hoist(DiagramDocument document, DiagramNode node)
    {
        var current = node;
        var seen = new HashSet<string>();
        while (!HasPicture(current) && seen.Add(current.Id))
        {
            var children = document.Nodes.Where(item => item.Parent == current.Id && item.Kind != "foreign").ToList();
            if (children.Count != 1)
                return current;
            current = children[0];
        }
        return current;
    }

    static bool HasPicture(DiagramNode node) =>
        !string.IsNullOrWhiteSpace(node.File) || (node.Members ?? []).Count > 0;

    public static double WidthFor(DiagramNode node)
    {
        if (node.Kind == "foreign")
            return 150;
        var longest = node.Name.Length;
        foreach (var line in LinesIn(node))
            longest = Math.Max(longest, line.Length);
        return Math.Clamp(32 + longest * 8.6, 148, 380);
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
            .Where(member => member.Kind is not ("field" or "html" or "scss"))
            .OrderByDescending(member => member.IsPublic)
            .ThenByDescending(member => member.Cc)
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .ToList();
        var lines = new List<string>();
        if (methods.Count > 0)
        {
            lines.AddRange(methods.Take(12).Select(ShortMember));
            if (methods.Count > 12)
                lines.Add("+ " + (methods.Count - 12) + " more");
        }
        else
        {
            lines.AddRange((node.Members ?? []).Where(member => member.Kind == "field").Take(8).Select(member => member.Name));
        }
        lines.AddRange((node.Members ?? []).Where(member => member.Kind is "html" or "scss").Select(ShortMember));
        return lines;
    }

    public static IReadOnlyList<string> FileKinds(DiagramDocument document, DiagramNode node)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        Collect(node);
        var order = new[] { "ts", "html", "scss", "cs", "java" };
        return order.Where(found.Contains).ToList();

        void Collect(DiagramNode current)
        {
            if (current.File is string file)
                Note(file);
            foreach (var member in current.Members ?? [])
            {
                if (member.Kind is "html" or "scss")
                    found.Add(member.Kind);
                else if (member.File is string memberFile)
                    Note(memberFile);
                else if (member.Kind is "method" or "field")
                    found.Add("ts");
            }
            foreach (var child in document.Nodes.Where(item => item.Parent == current.Id))
                Collect(child);
        }

        void Note(string file)
        {
            if (file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                found.Add("ts");
            else if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                found.Add("html");
            else if (file.EndsWith(".scss", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                found.Add("scss");
            else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                found.Add("cs");
            else if (file.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
                found.Add("java");
        }
    }

    public static string DisplayName(string name)
    {
        var paren = name.IndexOf('(');
        return paren > 0 ? name[..paren] : name;
    }

    public static string ShortMember(DiagramMember member)
    {
        if (member.Kind is "html" or "scss")
            return member.Kind + "  " + member.Name;
        var name = DisplayName(member.Name);
        if (name.Length > 24)
            name = name[..22] + "...";
        return (member.IsPublic ? "+ " : "− ") + name;
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
            boxes[node.Id] = new Box(x, y, WidthFor(node), HeightFor(node));
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
            if (boxes.ContainsKey(current.Id))
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
