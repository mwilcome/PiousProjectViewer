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
    static readonly FontFamily UiFont = new("fonts:Inter#Inter");
    static readonly IBrush CanvasBrush = Brush("#0E1014");
    static readonly IBrush Ink = Brush("#F4F6F8");
    static readonly IBrush Quiet = Brush("#8B93A1");
    static readonly IBrush FrameStroke = Brush("#2A3140");
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
            if (_mode == PaintMode.Distance && _document is not null)
            {
                var score = _packages.FirstOrDefault(item => item.Id == node.Id);
                if (score.Types == 0 && node.Parent is not null)
                    score = _packages.FirstOrDefault(item => item.Id == node.Parent);
                if (score.Types > 0 && string.IsNullOrWhiteSpace(node.File))
                    return score.Name + ". " + score.Meaning();
                if (!string.IsNullOrWhiteSpace(node.File))
                    return Display(node.Id, node.Name) + ". " + TypeCoupling.Word(TypeCoupling.Count(_document, node.Id)) + ".";
            }
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
            var title = _document?.Title ?? "";
            var note = string.IsNullOrWhiteSpace(_document?.Note) ? "Open a project folder to scan it." : _document.Note;
            var y = 24d;
            var width = Math.Max(40, Bounds.Width - 48);
            if (!string.IsNullOrWhiteSpace(title))
                y += DrawWrapped(context, title, 18, Ink, new Point(24, y), width, FontWeight.SemiBold) + 12;
            DrawWrapped(context, note, 15, Quiet, new Point(24, y), width);
            return;
        }

        using (context.PushTransform(Matrix.CreateScale(_scale, _scale)))
        using (context.PushTransform(Matrix.CreateTranslation(_pan.X / _scale, _pan.Y / _scale)))
        {
            DrawFrame(context);
            if (!_declutter)
            {
                foreach (var edge in _scene.Edges)
                    DrawEdge(context, edge);
            }
            foreach (var (id, box) in _scene.Boxes)
                DrawNode(context, Node(id)!, box, id == _selectedId);
        }
        DrawHover(context);
        DrawProposalBanner(context);
    }

    void DrawProposalBanner(DrawingContext context)
    {
        if (_document?.Title != "Proposal" || string.IsNullOrWhiteSpace(_document.Note) || _scene.Boxes.Count == 0)
            return;
        var width = Math.Max(80, Bounds.Width - 24);
        var formatted = Format(_document.Note, 14, Brush("#F0C14A"), FontWeight.SemiBold);
        formatted.MaxTextWidth = Math.Max(40, width - 20);
        var bar = new Rect(12, 12, width, formatted.Height + 16);
        context.FillRectangle(Brush("#3A2E12"), bar);
        context.DrawRectangle(null, new Pen(Brush("#F0C14A"), 1), bar);
        context.DrawText(formatted, new Point(22, 20));
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
        _selectedMember = _hitMember?.Name;
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
        if (e.ClickCount == 2 && hit is not null && _hitMember is DiagramMember member)
        {
            var file = member.File ?? hit.File;
            if (file is not null)
                SourceEditor.Open(file, member.Line);
            _dragging = false;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (e.ClickCount == 2 && hit is not null && HasChildren(hit.Id))
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
        _packages = _document is null ? [] : MartinDistance.Measure(_document);
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
        var frameColor = FrameColor();
        context.DrawRectangle(null, new Pen(Brush(frameColor), 1.6), new RoundedRect(frame, 18));
        DrawText(context, _scene.Title, 15, Ink, new Point(frame.X + 18, frame.Y + 14));
        var frameWord = FrameWord();
        if (frameWord.Length > 0)
            DrawText(context, frameWord, 12, Brush(frameColor), new Point(frame.X + 18, frame.Y + 32));
    }

    string FrameColor()
    {
        var score = CurrentPackage();
        return score.Types > 0 ? PackageScore.Color(score.Distance) : "#2A3140";
    }

    string FrameWord()
    {
        var score = CurrentPackage();
        return score.Types > 0 ? FolderWord(score) : "";
    }

    PackageScore CurrentPackage()
    {
        var id = CurrentParentId;
        if (_mode != PaintMode.Distance || id is null)
            return default;
        return _packages.FirstOrDefault(item => item.Id == id);
    }

    static string FolderWord(PackageScore score)
    {
        if (score.Distance <= 0.1)
            return score.Instability < 0.5 ? "stable, mostly abstract" : "depends outward";
        if (score.Instability < 0.5 && score.Abstractness < 0.5)
            return "others depend on this";
        if (score.Instability >= 0.5 && score.Abstractness >= 0.5)
            return "abstract, but depends outward";
        return "off the line";
    }

    void DrawNode(DrawingContext context, DiagramNode node, Box box, bool selected)
    {
        var rect = ToRect(box);
        var accent = ClassAccent(node);
        var hot = node.Id == _hotId;
        var stroke = selected ? Ink : Brush(accent);
        var pen = new Pen(stroke, selected ? 2.2 : hot ? 1.8 : 1.4);
        var fill = node.Kind == "foreign" ? "#1A2330" : "#16181D";
        if (node.Kind == "foreign")
            context.DrawEllipse(Brush(fill), pen, rect);
        else
            context.DrawRectangle(Brush(fill), pen, new RoundedRect(rect, 12));
        if (selected && node.Kind != "foreign")
            context.DrawRectangle(null, new Pen(Brush("#F4F6F8"), 1.2), new RoundedRect(rect.Inflate(4), 15));

        if (node.Kind == "foreign")
        {
            DrawCentered(context, node.Name, rect, 15, Ink, -8);
            DrawCentered(context, Caption(node), rect, 11, Quiet, 10);
            return;
        }
        DrawClassHeader(context, node, rect, accent);
        DrawMemberLines(context, node, rect, selected);
    }

    void DrawClassHeader(DrawingContext context, DiagramNode node, Rect rect, string accent)
    {
        var level = _document?.Nodes.Any(item => item.Rank != 0) == true;
        var nameX = rect.X + (level ? 28 : 16);
        if (level)
            DrawText(context, node.Rank.ToString(), 12, Quiet, new Point(rect.X + 10, rect.Y + 10));
        DrawText(context, node.Name, 15, Ink, new Point(nameX, rect.Y + 8), FontWeight.SemiBold);
        var marks = Marks(node);
        if (marks.Length > 0)
            DrawText(context, marks, 12, Brush(accent), new Point(rect.Right - 16 - marks.Length * 9, rect.Y + 10));
        DrawText(context, Caption(node), 12, Brush(accent), new Point(nameX, rect.Y + 30));
    }

    void DrawMemberLines(DrawingContext context, DiagramNode node, Rect rect, bool selected)
    {
        var listed = DiagramScene.Listed(node);
        var lineY = rect.Y + 52;
        foreach (var member in listed)
        {
            DrawMemberLine(context, node, member, rect, lineY, selected);
            lineY += 18;
        }
        var shown = listed.Count(member => member.Kind is not ("field" or "html" or "scss"));
        var total = (node.Members ?? []).Count(member => member.Kind is not ("field" or "html" or "scss"));
        if (total > shown)
            DrawText(context, "+ " + (total - shown) + " more", 12, Quiet, new Point(rect.X + 16, lineY));
    }

    void DrawMemberLine(DrawingContext context, DiagramNode node, DiagramMember member, Rect rect, double lineY, bool selected)
    {
        var picked = selected && member.Name == _selectedMember;
        var hovered = node.Id == _hotId && member.Name == _hotMember;
        if (picked || hovered)
            context.DrawRectangle(Brush(picked ? "#243044" : "#1C2330"), null, new RoundedRect(new Rect(rect.X + 8, lineY - 2, rect.Width - 16, 18), 5));
        var color = LineColor(member);
        DrawText(context, member.IsPublic ? "+" : "−", 12, Brush(color), new Point(rect.X + 14, lineY));
        var label = member.Kind is "html" or "scss"
            ? member.Kind + "  " + member.Name
            : DiagramScene.DisplayName(member.Name);
        DrawText(context, label, 13, member.Kind is "html" or "scss" or "field" ? Quiet : Ink, new Point(rect.X + 30, lineY));
        var score = LineScore(member);
        if (score.Length == 0)
            return;
        var formatted = Format(score, 12, Brush(color));
        context.DrawText(formatted, new Point(rect.Right - 14 - formatted.Width, lineY));
    }

    string ClassAccent(DiagramNode node)
    {
        if (node.Kind == "foreign")
            return "#6E8CA8";
        if (_mode == PaintMode.Distance && _document is not null)
        {
            var folder = _packages.FirstOrDefault(item => item.Id == node.Id);
            if (folder.Types > 0 && string.IsNullOrWhiteSpace(node.File))
                return PackageScore.Color(folder.Distance);
            if (!string.IsNullOrWhiteSpace(node.File))
                return TypeCoupling.Color(TypeCoupling.Count(_document, node.Id));
        }
        if (_mode == PaintMode.Crap && _document?.CoverageReady == true && node.CrapMu is double mu)
        {
            var band = new CrapRollup(mu, node.CrapMax ?? mu, node.CrapSigma ?? 0).Band;
            return band switch
            {
                "calm" => "#3DDC97",
                "warning" => "#F0C14A",
                _ => "#FF5C7A"
            };
        }
        if (node.WorstCc is int cc)
            return LineColor(new DiagramMember { Cc = cc, Kind = "method" });
        return "#5C6B78";
    }

    string LineColor(DiagramMember member)
    {
        if (member.Kind is "html" or "scss" or "field")
            return "#8B93A1";
        if (LinesUseCrap && member.Crap is double crap)
            return crap <= 8 ? "#3DDC97" : crap <= 30 ? "#F0C14A" : "#FF5C7A";
        return member.Cc switch
        {
            <= 8 => "#3DDC97",
            <= 30 => "#F0C14A",
            _ => "#FF5C7A"
        };
    }

    string LineScore(DiagramMember member)
    {
        if (member.Kind is "html" or "scss" or "field")
            return "";
        if (LinesUseCrap && member.Crap is double crap)
            return crap.ToString("0.0");
        return member.Cc.ToString();
    }

    static string Marks(DiagramNode node)
    {
        var marks = "";
        if (node.WorstCc is int cc && cc >= 11)
            marks += "C";
        if ((node.Members ?? []).Any(member => member.Survived is > 0))
            marks += marks.Length == 0 ? "M" : " M";
        return marks;
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
            var first = new Point(edge.Points[0].X, edge.Points[0].Y);
            figure.BeginFigure(first, false);
            tip = new Point(edge.Points[^1].X, edge.Points[^1].Y);
            if (edge.Points.Count == 2)
            {
                var (c1, c2) = Bow(edge.Points[0], edge.Points[1]);
                figure.CubicBezierTo(c1, c2, tip);
                before = c2;
            }
            else
            {
                var bend = new Point(edge.Points[1].X, edge.Points[1].Y);
                figure.CubicBezierTo(bend, bend, tip);
                before = bend;
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

    static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point origin, FontWeight weight = FontWeight.Normal)
    {
        context.DrawText(Format(text, size, brush, weight), origin);
    }

    static double DrawWrapped(DrawingContext context, string text, double size, IBrush brush, Point origin, double width, FontWeight weight = FontWeight.Normal)
    {
        var formatted = Format(text, size, brush, weight);
        formatted.MaxTextWidth = width;
        context.DrawText(formatted, origin);
        return formatted.Height;
    }

    static FormattedText Format(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal) => new(
        text,
        System.Globalization.CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(UiFont, FontStyle.Normal, weight),
        size,
        brush);

    DiagramMember? _hitMember;
    string? _selectedMember;
    string? _hotMember;

    DiagramNode? Hit(Point screen)
    {
        _hitMember = null;
        var diagram = ScreenToDiagram(screen);
        foreach (var (id, box) in _scene.Boxes.Reverse())
        {
            if (DiagramScene.IsMemberKey(id) || !ToRect(box).Contains(diagram))
                continue;
            var node = Node(id);
            if (node is null)
                continue;
            _hitMember = MemberAt(node, box, diagram);
            return node;
        }
        return null;
    }

    static DiagramMember? MemberAt(DiagramNode node, Box box, Point diagram)
    {
        var listed = DiagramScene.Listed(node);
        var index = (int)((diagram.Y - box.Y - 46) / 18);
        return index >= 0 && index < listed.Count ? listed[index] : null;
    }

    Point ScreenToDiagram(Point screen) => (screen - _pan) / _scale;

    static Rect ToRect(Box box) => new(box.X, box.Y, box.Width, box.Height);

    public DiagramNode? SelectedNode => _selectedId is null ? null : Node(_selectedId);

    public string? CurrentParentId => _depth.Count == 0 ? null : _depth.Peek();

    public bool Declutter
    {
        get => _declutter;
        set
        {
            _declutter = value;
            InvalidateVisual();
        }
    }

    bool _declutter;
    IReadOnlyList<PackageScore> _packages = [];

    public IReadOnlyList<DiagramNode> VisibleNodes() =>
        _scene.Boxes.Keys
            .Select(id => DiagramScene.IsMemberKey(id) ? null : Node(id))
            .Where(node => node is not null && node.Kind != "foreign")
            .Cast<DiagramNode>()
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ToList();

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
        if (LinesUseCrap && node.CrapMu is not null && node.CrapSigma is not null)
        {
            var rollup = new CrapRollup(node.CrapMu.Value, node.CrapMax ?? node.CrapMu.Value, node.CrapSigma.Value);
            var band = rollup.Band.Length == 0 ? rollup.Band : char.ToUpper(rollup.Band[0]) + rollup.Band[1..];
            var headline = $"Average {rollup.Mu:0.0}, worst {rollup.Max:0.0}, spread {rollup.Sigma:0.0}. {band}.";
            if (worst is null)
                return headline + " " + DataLine(node);
            return headline + " " + $"{DiagramScene.DisplayName(worst.Name)} is the highest: CRAP {worst.Crap:0.0}, complexity {worst.Cc}, covered {CoverageText(worst)}.";
        }
        if (worst is null)
            return DataLine(node);
        var plain = DiagramScene.DisplayName(worst.Name);
        if (_mode == PaintMode.Crap || _mode == PaintMode.Distance)
            return $"{plain} is the highest. Complexity {worst.Cc}. CRAP waits for a coverage report.";
        return $"{plain} is the highest. Complexity {worst.Cc}, {Heat.Word(worst.Cc)}.";
    }

    public string Describe(DiagramNode node) => NodeLine(node);

    DiagramMember? Worst(DiagramNode node)
    {
        var methods = MethodsOf(node);
        if (methods.Count == 0)
            return null;
        if (LinesUseCrap)
            return methods.OrderByDescending(member => member.Crap ?? -1).ThenByDescending(member => member.Cc).First();
        return methods.OrderByDescending(member => member.Cc).ThenBy(member => member.Name, StringComparer.Ordinal).First();
    }

    public string MemberLabel(DiagramMember member)
    {
        var name = DiagramScene.DisplayName(member.Name);
        var mark = member.IsPublic ? "+" : "−";
        var mutants = MutationText(member);
        if (member.Kind == "field")
            return "field  " + name;
        if (LinesUseCrap)
            return $"{member.Crap,6:0.0}   {member.Cc,2}   {CoverageText(member),4}   {mutants}{mark} {name}";
        return $"{member.Cc,2}   {mutants}{mark} {name}";
    }

    public string ColumnHeader =>
        (LinesUseCrap
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

    bool LinesUseCrap =>
        (_mode == PaintMode.Crap || _mode == PaintMode.Distance) && _document?.CoverageReady == true;

    string Caption(DiagramNode node)
    {
        if (node.Kind == "foreign")
            return "library";
        if (_mode == PaintMode.Distance && _document is not null)
        {
            var folder = _packages.FirstOrDefault(item => item.Id == node.Id);
            if (folder.Types > 0 && string.IsNullOrWhiteSpace(node.File))
                return FolderWord(folder);
            if (!string.IsNullOrWhiteSpace(node.File))
            {
                var touched = TypeCoupling.Word(TypeCoupling.Count(_document, node.Id));
                var role = RoleWord(node.Role);
                return role.Length == 0 ? touched : role + "  " + touched;
            }
        }
        var body = _mode == PaintMode.Crap && _document?.CoverageReady == true && node.CrapMu is not null
            ? "μ " + node.CrapMu.Value.ToString("0.0")
            : node.WorstCc is int cc
                ? WithKinds("cc " + cc, node)
                : WithKinds(FieldCaption(node), node);
        var kindWord = RoleWord(node.Role);
        return kindWord.Length == 0 ? body : kindWord + "  " + body;
    }

    static string RoleWord(string? role) => role switch
    {
        "component" => "component",
        "injectable" => "service",
        "directive" => "directive",
        "pipe" => "pipe",
        "ngmodule" => "module",
        _ => ""
    };

    static string FieldCaption(DiagramNode node)
    {
        var fields = FieldNames(node);
        return fields.Count == 0 ? "no methods" : fields.Count == 1 ? "1 field" : fields.Count + " fields";
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
        var hotMember = _hitMember?.Name;
        string? label = null;
        if (hit is null && !_declutter)
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
        _hoverAt = new Point(screen.X + 16, screen.Y + 16);
        if (label == _hover && hot == _hotId && hotMember == _hotMember)
            return;
        _hotId = hot;
        _hotMember = hotMember;
        _hover = label;
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
        var bow = Math.Min(18, length * 0.08);
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
