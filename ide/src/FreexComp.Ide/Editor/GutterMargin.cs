using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Editor;

/// <summary>
/// One margin of the editor gutter (editor.md §2): fixed or computed width, <c>bg.gutter</c> background, arrow cursor
/// (UI-ED-013), redrawn when the visible lines or the theme change. Colours are looked up on every draw, so a theme
/// switch repaints without rebuilding (UI-ED-003).
/// </summary>
public abstract class GutterMargin : AbstractMargin
{
    protected GutterMargin(TextArea area)
    {
        Area = area;
        Cursor = new Cursor(StandardCursorType.Arrow);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right && TextView is { VisualLinesValid: true } && ContextMenuRequested is { } open)
            {
                e.Handled = true;
                open(this, LineAt(e.GetPosition(this).Y));
            }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected TextArea Area { get; }

    /// <summary>UI-ED-022: a right click on the margin asks for the gutter context menu of that 1-based line.</summary>
    public Action<GutterMargin, int>? ContextMenuRequested { get; set; }

    /// <summary>The width this margin takes while visible (DIP).</summary>
    public abstract double MarginWidth { get; }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brush("bg.gutter") ?? Brushes.Transparent, new Rect(Bounds.Size));
        if (TextView is not { VisualLinesValid: true } textView)
            return;
        foreach (var line in textView.VisualLines)
            RenderVisualLine(context, line);
    }

    private void RenderVisualLine(DrawingContext context, VisualLine line) =>
        RenderLine(context, line, line.FirstDocumentLine.LineNumber, line.VisualTop - TextView!.VerticalOffset, line.TextLines[0].Height, line.Height);

    /// <summary>
    /// Draws the margin's part of one visual line: <paramref name="rowHeight"/> is its first row (glyphs, numbers and
    /// fold markers go there, UI-ED-014), <paramref name="height"/> all its rows.
    /// </summary>
    protected abstract void RenderLine(DrawingContext context, VisualLine line, int lineNumber, double top, double rowHeight, double height);

    /// <summary>
    /// What <see cref="RenderLine"/> draws for one visual line, as in <see cref="Render"/> (for
    /// <see cref="IEditorTestProbe.VisualRows"/>): each shape's bounds in margin coordinates and whether it is stroked.
    /// </summary>
    internal IReadOnlyList<(Rect Bounds, bool Stroked)> DrawnShapes(VisualLine line)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
            RenderVisualLine(context, line);
        var shapes = new List<(Rect, bool)>();
        Collect(group, Matrix.Identity);
        return shapes;

        void Collect(Drawing drawing, Matrix transform)
        {
            if (drawing is DrawingGroup g)
            {
                var inner = g.Transform is { } t ? t.Value * transform : transform;
                foreach (var child in g.Children)
                    Collect(child, inner);
            }
            else if (drawing is GeometryDrawing { Geometry: { } geometry } shape)
            {
                shapes.Add((geometry.Bounds.TransformToAABB(transform), shape.Pen is not null));
            }
        }
    }

    /// <summary>The theme brush of a colour token (<c>editor.lineNumber</c> → <c>FxcEditorLineNumberBrush</c>).</summary>
    protected IBrush? Brush(string token) =>
        this.TryFindResource(IconCatalog.BrushKey(token), ActualThemeVariant, out var value) ? value as IBrush : null;

    /// <summary>The 1-based document line under a margin-relative y, clamped to the document.</summary>
    protected int LineAt(double y)
    {
        var textView = TextView!;
        var line = textView.GetDocumentLineByVisualTop(Math.Max(0, y + textView.VerticalOffset));
        return line?.LineNumber ?? textView.Document.LineCount;
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();
}

/// <summary>Change bar (UI-ED-040): unsaved lines 3 px in <c>editor.changeBar.modified</c>, saved ones 2 px in <c>editor.changeBar.saved</c>.</summary>
public sealed class ChangeBarMargin(TextArea area) : GutterMargin(area)
{
    private Core.Editing.ILineChangeSource _source = Core.Editing.NoLineChanges.Instance;

    public override double MarginWidth => 3;

    /// <summary>The document's change states; set by the editor view.</summary>
    public Core.Editing.ILineChangeSource Source
    {
        get => _source;
        set
        {
            _source.Changed -= OnSourceChanged;
            _source = value;
            _source.Changed += OnSourceChanged;
            InvalidateVisual();
        }
    }

    protected override void RenderLine(DrawingContext context, VisualLine line, int lineNumber, double top, double rowHeight, double height)
    {
        switch (_source.StateOf(lineNumber))
        {
            case Core.Editing.LineChangeState.Modified:
                context.FillRectangle(Brush("editor.changeBar.modified")!, new Rect(0, top, 3, height));
                break;
            case Core.Editing.LineChangeState.Saved:
                context.FillRectangle(Brush("editor.changeBar.saved")!, new Rect(0, top, 2, height));
                break;
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e) => InvalidateVisual();
}

/// <summary>
/// The 4 px text padding left of the text (editor.md §2); its left edge is the 1 px <c>border.subtle</c> line that
/// separates the gutter from the text area. Not part of the gutter width.
/// </summary>
public sealed class TextPaddingMargin : AbstractMargin
{
    public TextPaddingMargin() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) => new(4, 0);

    public override void Render(DrawingContext context)
    {
        if (this.TryFindResource(IconCatalog.BrushKey("border.subtle"), ActualThemeVariant, out var value) && value is IBrush brush)
            context.FillRectangle(brush, new Rect(0, 0, 1, Bounds.Height));
    }
}
