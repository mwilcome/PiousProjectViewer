using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    string? _problem;
    StyleProposal? _proposal;
    Vector _pan;
    double _scale = 1;
    Point _dragStart;
    Vector _panAtDrag;
    bool _dragging;
    bool _moved;
    readonly List<(string Id, Rect Box)> _boxes = [];

    public event EventHandler? ViewChanged;

    public string? SelectedBox => _selectedBox;

    public string? Problem => _problem;

    public void SetProposal(StyleProposal? proposal)
    {
        _proposal = proposal;
        InvalidateVisual();
    }

    public void Show(StylePicture? picture)
    {
        var changed = !ReferenceEquals(_picture, picture);
        _picture = picture;
        if (changed)
        {
            _selected = null;
            _selectedBox = null;
            _problem = null;
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

    public void SelectProblem(string? id)
    {
        _problem = id;
        _selected = null;
        _selectedBox = null;
        _pan = default;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectBox(string? id)
    {
        _selectedBox = id;
        _selected = null;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearBox()
    {
        if (_selected is null && _selectedBox is null)
            return;
        _selected = null;
        _selectedBox = null;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        if (_selected is null && _selectedBox is null && _problem is null)
            return;
        _selected = null;
        _selectedBox = null;
        _problem = null;
        _pan = default;
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
            DrawText(context, "Refresh styles after opening a project.", 13, Brush("#9AA3B2"), new Point(24, 52), FontWeight.Normal);
            return;
        }
        var problems = StyleProblems.Build(_picture);
        var problem = problems.FirstOrDefault(item => item.Id == _problem);
        if (_proposal is not null)
        {
            DrawProposal(context, _proposal);
            return;
        }
        DrawHomes(context, problem, problems);
    }

    void DrawHomes(DrawingContext context, StyleProblem? focus, List<StyleProblem> problems)
    {
        var names = focus is null
            ? problems.SelectMany(item => item.Names).ToHashSet(StringComparer.Ordinal)
            : focus.Names.ToHashSet(StringComparer.Ordinal);
        var caption = focus is null
            ? problems.Count + (problems.Count == 1 ? " class to look at." : " classes to look at.") + " Border color is the file count: red is 6 or more, gold is 3 to 5, grey is 2."
            : focus.Brief + ". " + focus.Detail;
        DrawText(context, focus?.Title ?? "How it is", 16, Brush("#F4F6F8"), new Point(Inset, 16), FontWeight.SemiBold);
        DrawLine(context, caption, 13, Brush("#C5CAD3"), new Point(Inset, 40), 900, FontWeight.Normal);
        var gap = 10d;
        var width = Math.Max(108, (Bounds.Width / Math.Max(_scale, 0.01) - Inset * 2 - gap * 6) / StyleHomes.Order.Length);
        for (var i = 0; i < StyleHomes.Order.Length; i++)
        {
            var home = StyleHomes.Order[i];
            var x = Inset + i * (width + gap);
            DrawLine(context, StyleHomes.Label(home), 12, Brush("#9AA3B2"), new Point(x, 72), width, FontWeight.SemiBold);
            var mine = _picture!.Selectors.Where(selector => selector.Home == home && names.Contains(selector.Name))
                .GroupBy(selector => selector.Name + "|" + selector.SheetId, StringComparer.Ordinal)
                .Select(group => group.First())
                .Take(10)
                .ToList();
            var y = 96d;
            foreach (var selector in mine)
            {
                var box = new Rect(x, y, width, 48);
                var color = problems.FirstOrDefault(item => item.Names.Contains(selector.Name))?.Color ?? "#8B93A7";
                context.DrawRectangle(Brush("#16181D"), new Pen(Brush(color), 1.4), new RoundedRect(box, 8));
                using (context.PushClip(box))
                {
                    DrawLine(context, selector.Name, 12, Brush("#F4F6F8"), new Point(box.X + 8, box.Y + 6), box.Width - 16, FontWeight.SemiBold);
                    DrawLine(context, selector.FileName, 11, Brush("#9AA3B2"), new Point(box.X + 8, box.Y + 26), box.Width - 16, FontWeight.Normal);
                }
                var id = focus is null
                    ? problems.First(item => item.Names.Contains(selector.Name)).Id
                    : selector.SheetId;
                _boxes.Add((id, box));
                y += 56;
            }
        }
    }

    void DrawProposal(DrawingContext context, StyleProposal proposal)
    {
        DrawText(context, "Proposal", 16, Brush("#F4F6F8"), new Point(Inset, 16), FontWeight.SemiBold);
        DrawLine(context, string.IsNullOrWhiteSpace(proposal.Summary) ? proposal.Name : proposal.Summary, 13, Brush("#C5CAD3"), new Point(Inset, 40), 900, FontWeight.Normal);
        var drop = proposal.Drop.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var from = proposal.From.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = _picture!.Selectors.Where(selector =>
            !drop.Contains(selector.Name)
            && !from.Any(file => selector.FileName.Contains(file, StringComparison.OrdinalIgnoreCase) && !string.Equals(selector.Name, proposal.Name, StringComparison.OrdinalIgnoreCase)))
            .Where(selector => string.Equals(selector.Name, proposal.Name, StringComparison.OrdinalIgnoreCase) || drop.Count == 0 && string.Equals(selector.Name, proposal.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!kept.Any(selector => string.Equals(selector.Name, proposal.Name, StringComparison.OrdinalIgnoreCase)) && proposal.Name.Length > 0)
        {
            kept.Add(new StyleSelector
            {
                Name = proposal.Name,
                FileName = string.IsNullOrWhiteSpace(proposal.To) ? "shared" : Path.GetFileName(proposal.To),
                Home = "pieces",
                Color = "#3DDC97"
            });
        }
        var gap = 10d;
        var width = Math.Max(108, (Bounds.Width / Math.Max(_scale, 0.01) - Inset * 2 - gap * 6) / StyleHomes.Order.Length);
        for (var i = 0; i < StyleHomes.Order.Length; i++)
        {
            var home = StyleHomes.Order[i];
            var x = Inset + i * (width + gap);
            DrawLine(context, StyleHomes.Label(home), 12, Brush("#9AA3B2"), new Point(x, 72), width, FontWeight.SemiBold);
            var y = 96d;
            foreach (var selector in kept.Where(selector => selector.Home == home).Take(8))
            {
                var box = new Rect(x, y, width, 48);
                context.DrawRectangle(Brush("#16181D"), new Pen(Brush("#3DDC97"), 1.4), new RoundedRect(box, 8));
                using (context.PushClip(box))
                {
                    DrawLine(context, selector.Name, 12, Brush("#F4F6F8"), new Point(box.X + 8, box.Y + 6), box.Width - 16, FontWeight.SemiBold);
                    DrawLine(context, selector.FileName, 11, Brush("#9AA3B2"), new Point(box.X + 8, box.Y + 26), box.Width - 16, FontWeight.Normal);
                }
                y += 56;
            }
        }
    }

    void DrawFocus(DrawingContext context, StyleProblem problem)
    {
        DrawText(context, problem.Title, 16, Brush("#F4F6F8"), new Point(Inset, 16), FontWeight.SemiBold);
        DrawLine(context, problem.Brief + ". Click a file to see its lines.", 13, Brush("#C5CAD3"), new Point(Inset, 40), 760, FontWeight.Normal);
        if (problem.Id.StartsWith("problem:dup:", StringComparison.Ordinal))
            DrawNameFocus(context, problem.Id["problem:dup:".Length..]);
        else if (problem.Id == "problem:chunk")
            DrawChunkFocus(context);
        else if (problem.Id.StartsWith("problem:global:", StringComparison.Ordinal))
            DrawSheetFocus(context, problem.Id["problem:global:".Length..]);
        else if (problem.Id.StartsWith("problem:bare:", StringComparison.Ordinal))
            DrawBareFocus(context, problem.Id["problem:bare:".Length..]);
    }

    void DrawNameFocus(DrawingContext context, string name)
    {
        var rules = _picture!.Selectors.Where(selector => selector.Name == name).ToList();
        var sheets = rules.Select(rule => rule.SheetId).Distinct(StringComparer.Ordinal)
            .Select(id => _picture.Sheets.FirstOrDefault(sheet => sheet.Id == id))
            .Where(sheet => sheet is not null).Cast<StyleSheet>().ToList();
        var templateIds = rules.SelectMany(rule => rule.Hits).Distinct(StringComparer.Ordinal).ToList();
        var templates = templateIds.Select(id => _picture.Templates.FirstOrDefault(template => template.Id == id))
            .Where(template => template is not null).Cast<StyleTemplate>().ToList();
        DrawLinked(context, sheets, templates, rules);
    }

    void DrawChunkFocus(DrawingContext context)
    {
        var rules = _picture!.Selectors.Where(selector => selector.Mark.StartsWith("same chunk", StringComparison.Ordinal)).ToList();
        var sheets = rules.Select(rule => rule.SheetId).Distinct(StringComparer.Ordinal)
            .Select(id => _picture.Sheets.FirstOrDefault(sheet => sheet.Id == id))
            .Where(sheet => sheet is not null).Cast<StyleSheet>().ToList();
        var templates = rules.SelectMany(rule => rule.Hits).Distinct(StringComparer.Ordinal)
            .Select(id => _picture.Templates.FirstOrDefault(template => template.Id == id))
            .Where(template => template is not null).Cast<StyleTemplate>().ToList();
        DrawLinked(context, sheets, templates, rules);
    }

    void DrawSheetFocus(DrawingContext context, string sheetId)
    {
        var sheet = _picture!.Sheets.FirstOrDefault(item => item.Id == sheetId);
        if (sheet is null)
            return;
        var rules = _picture.Selectors.Where(selector => selector.SheetId == sheet.Id).ToList();
        var templates = rules.SelectMany(rule => rule.Hits).Distinct(StringComparer.Ordinal)
            .Select(id => _picture.Templates.FirstOrDefault(template => template.Id == id))
            .Where(template => template is not null).Cast<StyleTemplate>().ToList();
        DrawLinked(context, [sheet], templates, rules);
    }

    void DrawBareFocus(DrawingContext context, string templateId)
    {
        var template = _picture!.Templates.FirstOrDefault(item => item.Id == templateId);
        if (template is null)
            return;
        var local = _picture.Sheets.FirstOrDefault(sheet => sheet.TemplateId == template.Id);
        var sheets = local is null ? new List<StyleSheet>() : new List<StyleSheet> { local };
        var rules = _picture.Selectors.Where(selector => selector.Hits.Contains(template.Id)).ToList();
        foreach (var rule in rules)
        {
            var sheet = _picture.Sheets.FirstOrDefault(item => item.Id == rule.SheetId);
            if (sheet is not null && sheets.All(item => item.Id != sheet.Id))
                sheets.Add(sheet);
        }
        DrawLinked(context, sheets, [template], rules);
    }

    void DrawLinked(DrawingContext context, List<StyleSheet> sheets, List<StyleTemplate> templates, List<StyleSelector> rules)
    {
        const double column = 240;
        const double row = 56;
        const double step = 68;
        var right = Inset + column + 120;
        DrawText(context, "Stylesheets", 12, Brush("#7EB6D6"), new Point(Inset, 72), FontWeight.SemiBold);
        if (templates.Count > 0)
            DrawText(context, "HTML", 12, Brush("#E6C27A"), new Point(right, 72), FontWeight.SemiBold);
        var sheetBoxes = new Dictionary<string, Rect>();
        var templateBoxes = new Dictionary<string, Rect>();
        for (var i = 0; i < sheets.Count; i++)
            sheetBoxes[sheets[i].Id] = new Rect(Inset, 96 + i * step, column, row);
        for (var i = 0; i < templates.Count; i++)
            templateBoxes[templates[i].Id] = new Rect(right, 96 + i * step, column, row);
        if (_selectedBox is not null)
        {
            foreach (var rule in rules)
            {
                if (!sheetBoxes.TryGetValue(rule.SheetId, out var from))
                    continue;
                foreach (var hit in rule.Hits)
                {
                    if (_selectedBox != rule.SheetId && _selectedBox != hit)
                        continue;
                    if (!templateBoxes.TryGetValue(hit, out var to))
                        continue;
                    context.DrawLine(new Pen(Brush("#8B93A7"), 1.6), new Point(from.Right, from.Center.Y), new Point(to.Left, to.Center.Y));
                }
            }
        }
        foreach (var sheet in sheets)
            DrawSheet(context, sheet, sheetBoxes[sheet.Id]);
        foreach (var template in templates)
            DrawFocusTemplate(context, template, templateBoxes[template.Id]);
    }

    void DrawFocusTemplate(DrawingContext context, StyleTemplate template, Rect box)
    {
        var selected = _selectedBox == template.Id;
        context.DrawRectangle(Brush("#1A1E28"), new Pen(Brush(selected ? "#F4F6F8" : "#E6C27A"), selected ? 2.2 : 1.5), new RoundedRect(box, 18));
        using (context.PushClip(box))
        {
            DrawLine(context, "HTML", 11, Brush("#E6C27A"), new Point(box.X + 14, box.Y + 6), box.Width - 28, FontWeight.SemiBold);
            DrawLine(context, template.Name, 13, Brush("#F4F6F8"), new Point(box.X + 14, box.Y + 28), box.Width - 28, FontWeight.Normal);
        }
        _boxes.Add((template.Id, box));
    }

    void DrawSheet(DrawingContext context, StyleSheet sheet, Rect box)
    {
        var selected = _selectedBox == sheet.Id;
        var kind = sheet.Name.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ? "CSS" : "SCSS";
        if (sheet.Global)
            kind += " · global";
        context.DrawRectangle(Brush("#16181D"), new Pen(Brush(selected ? "#F4F6F8" : "#7EB6D6"), selected ? 2.2 : 1.5), new RoundedRect(box, 4));
        using (context.PushClip(box))
        {
            DrawLine(context, kind, 11, Brush("#7EB6D6"), new Point(box.X + 14, box.Y + 6), box.Width - 28, FontWeight.SemiBold);
            DrawLine(context, sheet.Name, 13, Brush("#F4F6F8"), new Point(box.X + 14, box.Y + 28), box.Width - 28, FontWeight.Normal);
        }
        _boxes.Add((sheet.Id, box));
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
        if (IsProblem(hit.Id))
        {
            _problem = hit.Id;
            _selected = null;
            _selectedBox = null;
            _pan = default;
        }
        else
        {
            _selected = null;
            _selectedBox = hit.Id;
            OpenHit(hit.Id);
        }
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    void OpenHit(string id)
    {
        var sheet = _picture?.Sheets.FirstOrDefault(item => item.Id == id);
        if (sheet is not null)
        {
            SourceEditor.Open(sheet.File, 1);
            return;
        }
        var template = _picture?.Templates.FirstOrDefault(item => item.Id == id);
        if (template is not null)
            SourceEditor.Open(template.File, 1);
    }

    static bool IsProblem(string id) => id.StartsWith("problem:", StringComparison.Ordinal);

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
        formatted.MaxTextHeight = size * 1.8;
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
