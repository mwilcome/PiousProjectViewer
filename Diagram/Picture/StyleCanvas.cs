using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace PiousProjectViewer.Diagram;

public sealed class StyleCanvas : Control
{
    StylePicture? _picture;
    string? _selected;
    string? _selectedBox;
    Vector _pan;
    double _scale = 1;
    Point _dragStart;
    Vector _panAtDrag;
    bool _dragging;
    bool _moved;
    readonly List<(string Id, Rect Box)> _boxes = [];

    public event EventHandler? ViewChanged;

    public string? SelectedBox => _selectedBox;

    public void Show(StylePicture? picture)
    {
        var changed = !ReferenceEquals(_picture, picture);
        _picture = picture;
        if (changed)
        {
            _selected = null;
            _selectedBox = null;
        }
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Select(string? name)
    {
        _selected = name;
        var hadBox = _selectedBox is not null;
        _selectedBox = null;
        InvalidateVisual();
        if (hadBox)
            ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        if (_selected is null && _selectedBox is null)
            return;
        _selected = null;
        _selectedBox = null;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<StyleSelector> Rows => _picture?.Selectors ?? [];

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brush("#0E1014"), new Rect(Bounds.Size));
        using (context.PushTransform(Matrix.CreateTranslation(_pan.X, _pan.Y)))
        using (context.PushTransform(Matrix.CreateScale(_scale, _scale)))
            DrawPicture(context);
    }

    void DrawPicture(DrawingContext context)
    {
        _boxes.Clear();
        if (_picture is null || (_picture.Sheets.Count == 0 && _picture.Templates.Count == 0))
        {
            DrawText(context, "No styles found.", 16, Brush("#F4F6F8"), new Point(24, 24), FontWeight.SemiBold);
            DrawText(context, "Refresh styles after opening an Angular folder.", 13, Brush("#9AA3B2"), new Point(24, 52), FontWeight.Normal);
            return;
        }
        var globals = _picture.Sheets.Where(sheet => sheet.Global).ToList();
        var y = 28d;
        DrawText(context, "Global", 13, Brush("#9AA3B2"), new Point(Inset, y), FontWeight.SemiBold);
        y += 26;
        var x = Inset;
        var rowHeight = 0d;
        var globalBoxes = new Dictionary<string, Rect>();
        foreach (var sheet in globals)
        {
            var width = BoxWidth(sheet.Name, "global");
            globalBoxes[sheet.Id] = Place(width, 56, ref x, ref y, ref rowHeight);
        }
        var globalBottom = globals.Count == 0 ? y : globalBoxes.Values.Max(box => box.Bottom);
        y = globalBottom + 48;
        x = Inset;
        rowHeight = 0;
        var templateBoxes = new Dictionary<string, Rect>();
        foreach (var template in _picture.Templates)
        {
            var local = _picture.Sheets.FirstOrDefault(sheet => sheet.TemplateId == template.Id);
            var note = template.Unstyled.Count == 0 ? "classes are styled" : template.Unstyled.Count + " classes have no rule";
            var width = BoxWidth(template.Name, local is null ? "no local stylesheet" : local.Name, note);
            templateBoxes[template.Id] = Place(width, 78, ref x, ref y, ref rowHeight);
        }
        foreach (var sheet in globals)
        {
            var from = globalBoxes[sheet.Id];
            foreach (var template in _picture.Templates)
            {
                var linked = _picture.Selectors.Where(selector => selector.SheetId == sheet.Id && selector.Hits.Contains(template.Id)).ToList();
                if (linked.Count == 0 || !templateBoxes.TryGetValue(template.Id, out var to))
                    continue;
                var red = linked.Any(selector => selector.Color == "#FF5C7A");
                var gold = linked.Any(selector => selector.Color == "#F0C14A");
                context.DrawLine(new Pen(Brush(red ? "#FF5C7A" : gold ? "#F0C14A" : "#8B93A7"), 1.2), new Point(from.Center.X, from.Bottom), new Point(to.Center.X, to.Y));
            }
        }
        if (globals.Count == 0)
            DrawText(context, "No global stylesheet.", 13, Brush("#9AA3B2"), new Point(24, 50), FontWeight.Normal);
        foreach (var sheet in globals)
            DrawSheet(context, sheet, globalBoxes[sheet.Id]);
        foreach (var template in _picture.Templates)
        {
            var local = _picture.Sheets.FirstOrDefault(sheet => sheet.TemplateId == template.Id);
            DrawTemplate(context, template, local, templateBoxes[template.Id]);
        }
    }

    void DrawSheet(DrawingContext context, StyleSheet sheet, Rect box)
    {
        var selectors = _picture!.Selectors.Where(selector => selector.SheetId == sheet.Id).ToList();
        var color = selectors.Any(selector => selector.Color == "#FF5C7A")
            ? "#FF5C7A"
            : selectors.Any(selector => selector.Color == "#F0C14A") ? "#F0C14A" : "#3DDC97";
        var picked = _selectedBox == sheet.Id;
        context.DrawRectangle(Brush("#16181D"), new Pen(Brush(picked ? "#F4F6F8" : color), picked ? 2.2 : 1.5), new RoundedRect(box, 10));
        DrawLine(context, sheet.Name, 13, Brush("#F4F6F8"), new Point(box.X + 12, box.Y + 10), box.Width - 24, FontWeight.SemiBold);
        DrawLine(context, "global", 12, Brush(color), new Point(box.X + 12, box.Y + 30), box.Width - 24, FontWeight.Normal);
        _boxes.Add((sheet.Id, box));
    }

    void DrawTemplate(DrawingContext context, StyleTemplate template, StyleSheet? local, Rect box)
    {
        var related = _picture!.Selectors.Where(selector => selector.Hits.Contains(template.Id)).ToList();
        var color = related.Any(selector => selector.Color == "#FF5C7A") ? "#FF5C7A" : related.Any(selector => selector.Color == "#F0C14A") ? "#F0C14A" : "#3DDC97";
        var selected = _selectedBox == template.Id || (_selected is not null && related.Any(selector => selector.Name == _selected));
        context.DrawRectangle(Brush("#16181D"), new Pen(Brush(selected ? "#F4F6F8" : color), selected ? 2.2 : 1.5), new RoundedRect(box, 10));
        DrawLine(context, template.Name, 13, Brush("#F4F6F8"), new Point(box.X + 12, box.Y + 10), box.Width - 24, FontWeight.SemiBold);
        DrawLine(context, local is null ? "no local stylesheet" : local.Name, 12, Brush("#9AA3B2"), new Point(box.X + 12, box.Y + 30), box.Width - 24, FontWeight.Normal);
        var note = template.Unstyled.Count == 0 ? "classes are styled" : template.Unstyled.Count + " classes have no rule";
        DrawLine(context, note, 11, Brush("#9AA3B2"), new Point(box.X + 12, box.Y + 50), box.Width - 24, FontWeight.Normal);
        _boxes.Add((template.Id, box));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        _moved = false;
        _dragStart = e.GetPosition(this);
        _panAtDrag = _pan;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || e.Pointer.Captured != this)
            return;
        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 4)
            return;
        _moved = true;
        _pan = _panAtDrag + delta;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var click = _dragging && !_moved;
        var at = _dragStart;
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
        _dragging = false;
        if (click)
            Pick(at);
    }

    void Pick(Point screen)
    {
        if (_picture is null)
            return;
        var at = (screen - _pan) / _scale;
        var hit = _boxes.LastOrDefault(item => item.Box.Contains(at));
        if (hit.Id is null)
            return;
        _selected = null;
        _selectedBox = hit.Id;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _scale = Math.Clamp(_scale * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), 0.4, 2.5);
        InvalidateVisual();
        e.Handled = true;
    }

    const double Inset = 36;
    const double GapX = 22;
    const double GapY = 20;

    Rect Place(double width, double height, ref double x, ref double y, ref double rowHeight)
    {
        var limit = Math.Max(360, Bounds.Width / Math.Max(_scale, 0.01) - Inset);
        if (x > Inset && x + width > limit)
        {
            x = Inset;
            y += rowHeight + GapY;
            rowHeight = 0;
        }
        var box = new Rect(x, y, width, height);
        x += width + GapX;
        rowHeight = Math.Max(rowHeight, height);
        return box;
    }

    static double BoxWidth(params string[] lines)
    {
        var widest = 0d;
        foreach (var line in lines)
            widest = Math.Max(widest, Measure(line, 13, FontWeight.SemiBold));
        return Math.Clamp(widest + 28, 188, 340);
    }

    static double Measure(string text, double size, FontWeight weight) =>
        Line(text, size, Brushes.White, weight).Width;

    static void DrawLine(DrawingContext context, string text, double size, IBrush brush, Point at, double maxWidth, FontWeight weight)
    {
        var formatted = Line(text, size, brush, weight);
        formatted.MaxTextWidth = Math.Max(1, maxWidth);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        context.DrawText(formatted, at);
    }

    static FormattedText Line(string text, double size, IBrush brush, FontWeight weight) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default, FontStyle.Normal, weight),
        size,
        brush);

    static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point at, FontWeight weight)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
        context.DrawText(formatted, at);
    }

    static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
