using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace PiousProjectViewer.Diagram;

public sealed class DiagramView : Control
{
    static readonly IBrush CanvasBrush = Brush("#141311");
    static readonly IBrush Ink = Brush("#F3EFE8");
    static readonly IBrush Quiet = Brush("#9A948A");
    static readonly IBrush FrameStroke = Brush("#5C574E");
    static readonly IBrush Violation = Brush(Heat.Violation);

    readonly Stack<string> _depth = new();

    DiagramDocument? _document;
    Scene _scene = Scene.Empty;
    PaintMode _mode = PaintMode.Complexity;
    string? _selectedId;
    Vector _pan = new(48, 36);
    double _scale = 1;
    bool _userMoved;
    Point _dragStart;
    Vector _panAtDrag;
    bool _dragging;
    IPointer? _captured;
    string? _hover;
    string? _hotId;
    Point _hoverAt;

    public DiagramView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public event EventHandler? ViewChanged;
    public event EventHandler? DepthChanged;
    public event EventHandler<DiagramNode>? OpenCard;
    public event EventHandler<DiagramNode>? RefreshNode;

    public PaintMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DiagramDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            _depth.Clear();
            _selectedId = null;
            _userMoved = false;
            Rebuild();
            DepthChanged?.Invoke(this, EventArgs.Empty);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool CanGoBack => _depth.Count > 0;

    public string PathText
    {
        get
        {
            if (_document is null)
                return "";
            var names = new List<string> { _document.Title };
            foreach (var id in _depth.Reverse())
                names.Add(Display(id, Node(id)?.Name));
            return string.Join("  /  ", names);
        }
    }

    public string DetailText
    {
        get
        {
            var node = Selected();
            if (node is null)
                return "";
            var inside = HasChildren(node.Id)
                ? " Double-click to open it."
                : node.Kind == "foreign"
                    ? ""
                    : FieldNames(node).Count > 0 && MethodsOf(node).Count == 0
                        ? " Double-click to see its fields."
                        : " Double-click for the method list.";
            return $"{Display(node.Id, node.Name)}. {NodeLine(node)}{inside}";
        }
    }

    public void ReplaceDocument(DiagramDocument document)
    {
        var parent = _depth.Count == 0 ? null : _depth.Peek();
        _document = document;
        if (parent is not null && document.Nodes.All(node => node.Id != parent))
            _depth.Clear();
        _selectedId = _selectedId is not null && document.Nodes.Any(node => node.Id == _selectedId) ? _selectedId : null;
        _userMoved = true;
        if (_depth.Count == 0 && OpenAloneRoot())
            _userMoved = false;
        Rebuild();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    bool OpenAloneRoot()
    {
        if (_document is null || _depth.Count > 0)
            return false;
        var root = _document.Nodes.SingleOrDefault(node => node.Parent is null && node.Kind != "foreign");
        if (root is null || _document.Nodes.All(node => node.Parent != root.Id))
            return false;
        _depth.Push(root.Id);
        _selectedId = null;
        return true;
    }

    public void Open(string id)
    {
        _depth.Clear();
        _depth.Push(id);
        _selectedId = null;
        _userMoved = false;
        Rebuild();
        DepthChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void GoBack()
    {
        if (_depth.Count == 0)
            return;
        _depth.Pop();
        _selectedId = null;
        _userMoved = false;
        Rebuild();
        DepthChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(CanvasBrush, new Rect(Bounds.Size));
        if (_document is null || _scene.Boxes.Count == 0)
        {
            var note = string.IsNullOrWhiteSpace(_document?.Note) ? "Open a project folder to scan it." : _document.Note;
            DrawText(context, note, 16, Ink, new Point(24, 24));
            return;
        }

        using (context.PushTransform(Matrix.CreateScale(_scale, _scale)))
        using (context.PushTransform(Matrix.CreateTranslation(_pan.X / _scale, _pan.Y / _scale)))
        {
            DrawFrame(context);
            foreach (var edge in _scene.Edges)
                DrawEdge(context, edge);
            foreach (var (id, box) in _scene.Boxes)
                DrawNode(context, Node(id)!, box, id == _selectedId);
        }
        DrawHover(context);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        CenterIfNeeded();
        return size;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var hit = Hit(e.GetPosition(this));
        _selectedId = hit?.Id;
        var right = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
        if (right && hit is not null)
        {
            _selectedId = hit.Id;
            _dragging = false;
            ShowRefreshMenu(hit);
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        _dragging = e.ClickCount < 2;
        _dragStart = e.GetPosition(this);
        _panAtDrag = _pan;
        _captured = e.Pointer;
        e.Pointer.Capture(this);
        if (e.ClickCount == 2 && hit is not null && HasChildren(hit.Id))
        {
            _depth.Push(hit.Id);
            _selectedId = null;
            _userMoved = false;
            _dragging = false;
            Rebuild();
            DepthChanged?.Invoke(this, EventArgs.Empty);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (e.ClickCount == 2 && hit is not null && hit.Kind != "foreign")
        {
            OpenCard?.Invoke(this, hit);
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _captured is null)
        {
            UpdateHover(e.GetPosition(this));
            return;
        }
        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 4)
            return;
        _userMoved = true;
        _pan = _panAtDrag + delta;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        _captured = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is null && _hotId is null)
            return;
        _hover = null;
        _hotId = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        var screen = e.GetPosition(this);
        var diagramPoint = ScreenToDiagram(screen);
        _scale = Math.Clamp(_scale * factor, 0.4, 2.8);
        _pan = screen - diagramPoint * _scale;
        _userMoved = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        CenterIfNeeded();
    }

    void Rebuild()
    {
        _scene = _document is null
            ? Scene.Empty
            : DiagramScene.Build(_document, _depth.Count == 0 ? null : _depth.Peek());
        CenterIfNeeded();
        InvalidateVisual();
    }

    void CenterIfNeeded()
    {
        if (_userMoved || Bounds.Width < 1 || _scene.Boxes.Count == 0)
            return;
        var bounds = _scene.Bounds;
        const double margin = 48;
        var contentW = bounds.Width * _scale;
        var contentH = bounds.Height * _scale;
        var x = contentW + margin * 2 <= Bounds.Width
            ? (Bounds.Width - contentW) / 2 - bounds.X * _scale
            : margin - bounds.X * _scale;
        var y = margin - bounds.Y * _scale;
        _pan = new Vector(x, y);
    }

    void DrawFrame(DrawingContext context)
    {
        var frame = ToRect(_scene.Frame);
        if (frame.Width < 1)
            return;
        context.DrawRectangle(null, new Pen(FrameStroke, 1.4), new RoundedRect(frame, 18));
        DrawText(context, _scene.Title, 15, Ink, new Point(frame.X + 18, frame.Y + 14));
    }

    void DrawNode(DrawingContext context, DiagramNode node, Box box, bool selected)
    {
        var rect = ToRect(box);
        var fillHex = BoxPaint.Fill(node, _mode, _document?.CoverageReady == true);
        var unscored = fillHex == BoxPaint.CrapNeutral || fillHex == Heat.Color(null);
        var hot = node.Id == _hotId;
        var stroke = selected ? Ink : hot ? Brush("#E4D7B8") : unscored ? Brush("#9BB0BA") : Quiet;
        var pen = new Pen(stroke, selected ? 2.6 : 1.4);
        if (node.Kind == "foreign")
            context.DrawEllipse(Brush(fillHex), pen, rect);
        else
            context.DrawRectangle(Brush(fillHex), pen, new RoundedRect(rect, 14));
        if (selected)
        {
            var ring = rect.Inflate(5);
            var ringPen = new Pen(Brush("#E6C98A"), 1.6);
            if (node.Kind == "foreign")
                context.DrawEllipse(null, ringPen, ring);
            else
                context.DrawRectangle(null, ringPen, new RoundedRect(ring, 16));
        }

        if (node.Kind == "foreign")
        {
            DrawCentered(context, node.Name, rect, 15, Ink, -8);
            DrawCentered(context, Caption(node), rect, 11, Quiet, 10);
            return;
        }
        DrawText(context, node.Name, 15, Ink, new Point(rect.X + 12, rect.Y + 8));
        DrawText(context, Caption(node), 11, selected || hot ? Ink : Quiet, new Point(rect.X + 12, rect.Y + 28));
        var lineY = rect.Y + 48;
        foreach (var line in DiagramScene.LinesIn(_document ?? new DiagramDocument(), node))
        {
            DrawText(context, line, 12, Quiet, new Point(rect.X + 12, lineY));
            lineY += 15;
        }
    }

    void ShowRefreshMenu(DiagramNode node)
    {
        var item = new MenuItem { Header = "Refresh this box" };
        item.Click += (_, _) => RefreshNode?.Invoke(this, node);
        var menu = new ContextMenu { Items = { item } };
        menu.Open(this);
    }

    void DrawEdge(DrawingContext context, RoutedEdge edge)
    {
        if (edge.Points.Count < 2)
            return;
        var geometry = new StreamGeometry();
        Point tip;
        Point before;
        using (var figure = geometry.Open())
        {
            var first = edge.Points[0];
            figure.BeginFigure(new Point(first.X, first.Y), false);
            if (edge.Points.Count == 2)
            {
                var (c1, c2) = Bow(edge.Points[0], edge.Points[1]);
                var end = new Point(edge.Points[1].X, edge.Points[1].Y);
                figure.CubicBezierTo(c1, c2, end);
                tip = end;
                before = c2;
            }
            else
            {
                var end = new Point(edge.Points[^1].X, edge.Points[^1].Y);
                figure.CubicBezierTo(
                    new Point(edge.Points[1].X, edge.Points[1].Y),
                    new Point(edge.Points[^2].X, edge.Points[^2].Y),
                    end);
                tip = end;
                before = new Point(edge.Points[^2].X, edge.Points[^2].Y);
            }
            figure.EndFigure(false);
        }
        var brush = edge.Violating ? Violation : Quiet;
        var pen = new Pen(brush, edge.Violating ? 2 : 1.5)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        context.DrawGeometry(null, pen, geometry);
        DrawArrowHead(context, before, tip, brush);
    }

    static void DrawArrowHead(DrawingContext context, Point from, Point to, IBrush brush)
    {
        var vector = to - from;
        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        if (length < 1)
            return;
        var unit = vector / length;
        var normal = new Vector(-unit.Y, unit.X);
        var tip = to;
        var back = tip - unit * 10;
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(tip, true);
            figure.LineTo(back + normal * 4.5);
            figure.LineTo(back - normal * 4.5);
            figure.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    static void DrawCentered(DrawingContext context, string text, Rect rect, double size, IBrush brush, double yOffset)
    {
        var formatted = Format(text, size, brush);
        context.DrawText(formatted, new Point(rect.Center.X - formatted.Width / 2, rect.Center.Y - formatted.Height / 2 + yOffset));
    }

    static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point origin)
    {
        context.DrawText(Format(text, size, brush), origin);
    }

    static FormattedText Format(string text, double size, IBrush brush) => new(
        text,
        System.Globalization.CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default),
        size,
        brush);

    DiagramNode? Hit(Point screen)
    {
        var diagram = ScreenToDiagram(screen);
        foreach (var (id, box) in _scene.Boxes.Reverse())
        {
            if (ToRect(box).Contains(diagram))
                return Node(id);
        }
        return null;
    }

    Point ScreenToDiagram(Point screen) => (screen - _pan) / _scale;

    static Rect ToRect(Box box) => new(box.X, box.Y, box.Width, box.Height);

    public DiagramNode? SelectedNode => _selectedId is null ? null : Node(_selectedId);

    public string? CurrentParentId => _depth.Count == 0 ? null : _depth.Peek();

    public void Select(string id)
    {
        _selectedId = id;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    DiagramNode? Selected() => SelectedNode;

    DiagramNode? Node(string id) => _document?.Nodes.FirstOrDefault(node => node.Id == id);

    string NodeLine(DiagramNode node)
    {
        var worst = Worst(node);
        if (_mode == PaintMode.Crap && _document?.CoverageReady == true && node.CrapMu is not null && node.CrapSigma is not null)
        {
            var rollup = new CrapRollup(node.CrapMu.Value, node.CrapMax ?? node.CrapMu.Value, node.CrapSigma.Value);
            var method = worst is null
                ? DataLine(node)
                : $"Worst method {worst.Name}, CRAP {worst.Crap:0.0}, complexity {worst.Cc}, coverage {CoverageText(worst)}.";
            return $"CRAP μ {rollup.Mu:0.0}, max {rollup.Max:0.0}, σ {rollup.Sigma:0.0}, {rollup.Band}. {method}";
        }
        if (worst is null)
            return DataLine(node);
        if (_mode == PaintMode.Crap)
            return $"Worst method {worst.Name}, complexity {worst.Cc}. CRAP waits for a coverage report.";
        return $"Worst method {worst.Name}, complexity {worst.Cc}, {Heat.Word(worst.Cc)}.";
    }

    public string Describe(DiagramNode node) => NodeLine(node);

    DiagramMember? Worst(DiagramNode node)
    {
        var methods = MethodsOf(node);
        if (methods.Count == 0)
            return null;
        if (_mode == PaintMode.Crap && _document?.CoverageReady == true)
            return methods.OrderByDescending(member => member.Crap ?? -1).ThenByDescending(member => member.Cc).First();
        return methods.OrderByDescending(member => member.Cc).ThenBy(member => member.Name, StringComparer.Ordinal).First();
    }

    public string MemberLabel(DiagramMember member)
    {
        var mark = member.IsPublic ? "+" : "-";
        var mutants = MutationText(member);
        if (member.Kind == "field")
            return $"       field          {mark} {member.Name}";
        if (_mode == PaintMode.Crap && _document?.CoverageReady == true)
            return $"{member.Crap,6:0.0}   {member.Cc,2}   {CoverageText(member),4}   {mutants}{mark} {member.Name}";
        return $"{member.Cc,2}   {mutants}{mark} {member.Name}";
    }

    public string ColumnHeader =>
        (_mode == PaintMode.Crap && _document?.CoverageReady == true
            ? "  CRAP  CC  Cov  "
            : "CC  ")
        + (_document?.MutationReady == true ? "k  s  u  " : "")
        + "method";

    static string MutationText(DiagramMember member) =>
        member.Killed is int killed
            ? $"{killed,2} {member.Survived ?? 0,2} {member.Uncovered ?? 0,2}   "
            : "";

    static string CoverageText(DiagramMember member) =>
        member.Coverage is double coverage ? coverage.ToString("0.#") + "%" : "—";

    string Caption(DiagramNode node)
    {
        if (node.Kind == "foreign")
            return "library";
        if (_mode == PaintMode.Crap && _document?.CoverageReady == true && node.CrapMu is not null)
            return "μ " + node.CrapMu.Value.ToString("0.0");
        if (node.WorstCc is int cc)
            return WithKinds("cc " + cc, node);
        var fields = FieldNames(node);
        var plain = fields.Count == 0 ? "no methods" : fields.Count == 1 ? "1 field" : fields.Count + " fields";
        return WithKinds(plain, node);
    }

    string WithKinds(string caption, DiagramNode node)
    {
        if (_document is null)
            return caption;
        var kinds = DiagramScene.FileKinds(_document, node);
        if (!kinds.Contains("html") && !kinds.Contains("scss"))
            return caption;
        var label = string.Join(" · ", kinds);
        return caption is "no methods" ? label : caption + "   " + label;
    }

    static List<DiagramMember> MethodsOf(DiagramNode node) =>
        (node.Members ?? []).Where(member => member.Kind != "field").ToList();

    static List<string> FieldNames(DiagramNode node) =>
        (node.Members ?? []).Where(member => member.Kind == "field").Select(member => member.Name).ToList();

    static string DataLine(DiagramNode node)
    {
        var fields = FieldNames(node);
        if (fields.Count == 0)
            return "No methods or fields. Double-click opens the file.";
        return "Data type. Fields: " + string.Join(", ", fields) + ".";
    }

    static string Display(string id, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("ns:", StringComparison.Ordinal) && !name.StartsWith("type:", StringComparison.Ordinal))
            return name;
        var bare = id;
        var colon = bare.IndexOf(':');
        if (colon >= 0)
            bare = bare[(colon + 1)..];
        var slash = bare.LastIndexOf('/');
        if (slash >= 0)
            bare = bare[(slash + 1)..];
        var dot = bare.LastIndexOf('.');
        return dot < 0 ? bare : bare[(dot + 1)..];
    }

    bool HasChildren(string id) => _document?.Nodes.Any(node => node.Parent == id) == true;

    void UpdateHover(Point screen)
    {
        var diagram = ScreenToDiagram(screen);
        var hit = Hit(screen);
        var hot = hit?.Id;
        string? label = null;
        if (hit is null)
        {
            var best = 12d;
            foreach (var edge in _scene.Edges)
            {
                var distance = DistanceToCurve(diagram, edge.Points);
                if (distance < best && !string.IsNullOrWhiteSpace(edge.Label))
                {
                    best = distance;
                    label = edge.Label;
                }
            }
        }
        if (label == _hover && hot == _hotId)
            return;
        _hotId = hot;
        _hover = label;
        _hoverAt = new Point(screen.X + 16, screen.Y + 16);
        InvalidateVisual();
    }

    void DrawHover(DrawingContext context)
    {
        if (string.IsNullOrWhiteSpace(_hover))
            return;
        var lines = _hover.Split('\n');
        var formatted = lines.Select(line => Format(line, 13, Ink)).ToList();
        var width = formatted.Max(line => line.Width) + 16;
        var height = formatted.Sum(line => line.Height) + 12;
        var x = Math.Min(_hoverAt.X, Math.Max(8, Bounds.Width - width - 8));
        var y = Math.Min(_hoverAt.Y, Math.Max(8, Bounds.Height - height - 8));
        context.FillRectangle(Brush("#1C1B19"), new Rect(x, y, width, height));
        context.DrawRectangle(null, new Pen(Quiet, 1), new Rect(x, y, width, height));
        var lineY = y + 6;
        foreach (var line in formatted)
        {
            context.DrawText(line, new Point(x + 8, lineY));
            lineY += line.Height;
        }
    }

    static double DistanceToCurve(Point point, IReadOnlyList<Point2> points)
    {
        var samples = CurveSamples(points).ToList();
        var best = double.MaxValue;
        var here = new Point2(point.X, point.Y);
        for (var i = 1; i < samples.Count; i++)
            best = Math.Min(best, DistToSegment(here, samples[i - 1], samples[i]));
        return best;
    }

    static IEnumerable<Point2> CurveSamples(IReadOnlyList<Point2> points)
    {
        if (points.Count == 2)
        {
            var (c1, c2) = Bow(points[0], points[1]);
            var start = new Point(points[0].X, points[0].Y);
            var end = new Point(points[1].X, points[1].Y);
            for (var i = 0; i <= 16; i++)
            {
                var at = Cubic(start, c1, c2, end, i / 16d);
                yield return new Point2(at.X, at.Y);
            }
            yield break;
        }
        foreach (var point in points)
            yield return point;
    }

    static (Point C1, Point C2) Bow(Point2 start, Point2 end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1)
            return (new Point(start.X, start.Y), new Point(end.X, end.Y));
        var ux = dx / length;
        var uy = dy / length;
        var bow = Math.Min(28, length * 0.2);
        return (
            new Point(start.X + ux * length * 0.35 - uy * bow, start.Y + uy * length * 0.35 + ux * bow),
            new Point(end.X - ux * length * 0.35 - uy * bow, end.Y - uy * length * 0.35 + ux * bow));
    }

    static Point Cubic(Point start, Point c1, Point c2, Point end, double t)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;
        return new Point(
            a * start.X + b * c1.X + c * c2.X + d * end.X,
            a * start.Y + b * c1.Y + c * c2.Y + d * end.Y);
    }

    static double DistToSegment(Point2 point, Point2 a, Point2 b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var length = abx * abx + aby * aby;
        if (length < 1)
            return Math.Sqrt((point.X - a.X) * (point.X - a.X) + (point.Y - a.Y) * (point.Y - a.Y));
        var t = Math.Clamp(((point.X - a.X) * abx + (point.Y - a.Y) * aby) / length, 0, 1);
        var dx = point.X - (a.X + abx * t);
        var dy = point.Y - (a.Y + aby * t);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
