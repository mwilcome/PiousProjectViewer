using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace pious_project_viewer.Diagram;

public sealed class DiagramView : Control
{
    static readonly IBrush CanvasBrush = Brush("#F4F1EC");
    static readonly IBrush Ink = Brush("#2C2A26");
    static readonly IBrush Quiet = Brush("#8A847C");
    static readonly IBrush FrameStroke = Brush("#C4BEB4");
    static readonly IBrush Violation = Brush(Heat.Violation);
    static readonly IBrush ForeignFill = Brush("#E7E2DA");

    readonly Stack<string> _depth = new();

    DiagramDocument? _document;
    Scene _scene = Scene.Empty;
    string? _selectedId;
    Vector _pan = new(48, 36);
    double _scale = 1;
    bool _userMoved;
    Point _dragStart;
    Vector _panAtDrag;
    bool _dragging;
    IPointer? _captured;

    public DiagramView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public event EventHandler? ViewChanged;

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
                names.Add(NameOf(id));
            return string.Join("  /  ", names);
        }
    }

    public string DetailText
    {
        get
        {
            var node = Selected();
            if (node is null)
                return "Fill is complexity, in sage and clay. Red is only an arrow that points from an inner part out to an outer one. The open circle means coverage has not been run.";
            var cc = node.WorstCc is int value ? value.ToString() : "none";
            var inside = HasChildren(node.Id) ? " Double-click to open it." : "";
            return $"{node.Name}. Worst method complexity {cc}, {Heat.Word(node.WorstCc)}. Coverage has not been run.{inside}";
        }
    }

    public void GoBack()
    {
        if (_depth.Count == 0)
            return;
        _depth.Pop();
        _selectedId = null;
        _userMoved = false;
        Rebuild();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(CanvasBrush, new Rect(Bounds.Size));
        if (_document is null || _scene.Boxes.Count == 0)
        {
            DrawText(context, "No diagram loaded.", 16, Ink, new Point(24, 24));
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
            return;
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

    void Rebuild()
    {
        _scene = _document is null
            ? Scene.Empty
            : DiagramScene.Build(_document, _depth.Count == 0 ? null : _depth.Peek());
        InvalidateVisual();
    }

    void CenterIfNeeded()
    {
        if (_userMoved || Bounds.Width < 1 || _scene.Boxes.Count == 0)
            return;
        var bounds = _scene.Bounds;
        _pan = new Vector(
            (Bounds.Width - bounds.Width * _scale) / 2 - bounds.X * _scale,
            (Bounds.Height - bounds.Height * _scale) / 2 - bounds.Y * _scale);
        InvalidateVisual();
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
        var fill = node.Kind == "foreign" ? ForeignFill : Brush(Heat.Color(node.WorstCc));
        var pen = new Pen(selected ? Ink : Quiet, selected ? 2 : 1.2);
        if (node.Kind == "foreign")
            context.DrawEllipse(fill, pen, rect);
        else
            context.DrawRectangle(fill, pen, new RoundedRect(rect, 14));

        var mark = node.Kind == "foreign"
            ? new Rect(rect.Center.X + rect.Width * 0.22, rect.Center.Y - rect.Height * 0.28, 7, 7)
            : new Rect(rect.Right - 20, rect.Y + 10, 7, 7);
        context.DrawEllipse(null, new Pen(Quiet, 1.2), mark);

        var caption = node.WorstCc is int cc ? "cc " + cc : "library";
        DrawCentered(context, node.Name, rect, 16, Ink, -9);
        DrawCentered(context, caption, rect, 12, Quiet, 11);
    }

    void DrawEdge(DrawingContext context, RoutedEdge edge)
    {
        if (edge.Points.Count < 2)
            return;
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            var first = edge.Points[0];
            figure.BeginFigure(new Point(first.X, first.Y), false);
            for (var i = 1; i < edge.Points.Count; i++)
                figure.LineTo(new Point(edge.Points[i].X, edge.Points[i].Y));
            figure.EndFigure(false);
        }
        var brush = edge.Violating ? Violation : Quiet;
        var pen = new Pen(brush, edge.Violating ? 2 : 1.5)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        context.DrawGeometry(null, pen, geometry);
        var end = edge.Points[^1];
        var before = edge.Points[^2];
        DrawArrowHead(context, new Point(before.X, before.Y), new Point(end.X, end.Y), brush);
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

    DiagramNode? Selected() => _selectedId is null ? null : Node(_selectedId);

    DiagramNode? Node(string id) => _document?.Nodes.FirstOrDefault(node => node.Id == id);

    string NameOf(string id) => Node(id)?.Name ?? id;

    bool HasChildren(string id) => _document?.Nodes.Any(node => node.Parent == id) == true;

    static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
