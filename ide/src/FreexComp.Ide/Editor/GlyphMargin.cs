using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Editing;

namespace FreexComp.Ide.Editor;

/// <summary>
/// Glyph margin (editor.md §3): 2 px left padding + one 16 × 16 slot per line with the highest-priority glyph of the
/// document's <see cref="EditorDecorations"/>; an execution arrow on a breakpoint line is drawn over the disc. Vector
/// geometry per §3.1. The tooltip has one line per glyph on the line (UI-ED-021, <see cref="EditorDecorations.ToolTipAt"/>); a
/// right click asks for the gutter menu (UI-ED-022, <see cref="GutterMargin.ContextMenuRequested"/>); a left click is
/// reported through <see cref="LineClicked"/> (UI-ED-012).
/// </summary>
public sealed class GlyphMargin(TextArea area) : GutterMargin(area)
{
    private const double SlotLeft = 2;
    private EditorDecorations? _decorations;

    public override double MarginWidth => 18;

    /// <summary>The document's decorations; set by the editor view (null while unbound).</summary>
    public EditorDecorations? Decorations
    {
        get => _decorations;
        set
        {
            if (_decorations is not null)
                _decorations.Changed -= OnDecorationsChanged;
            _decorations = value;
            if (_decorations is not null)
                _decorations.Changed += OnDecorationsChanged;
            InvalidateVisual();
        }
    }

    protected override void RenderLine(DrawingContext context, VisualLine line, int lineNumber, double top, double rowHeight, double height)
    {
        if (_decorations?.GlyphsAt(lineNumber) is not [var glyph, ..] glyphs)
            return;
        var origin = new Point(SlotLeft, Math.Round(top + (rowHeight - 16) / 2));
        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            if (glyph.Kind == LineGlyphKind.Execution && glyphs.FirstOrDefault(g => g.IsBreakpoint) is { } breakpoint)
            {
                // §3: both stay recognizable — the breakpoint below, the arrow with a 1 px gutter outline on top.
                DrawGlyph(context, breakpoint);
                context.DrawGeometry(Brush("editor.execution"), new Pen(Brush("bg.gutter"), 1), Arrow());
            }
            else
            {
                DrawGlyph(context, glyph);
            }
        }
    }

    /// <summary>UI-ED-012 (P6): a left click on the margin; the 1-based line is passed on (it toggles a breakpoint there).</summary>
    public Action<int>? LineClicked { get; set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || LineClicked is not { } clicked || TextView is not { VisualLinesValid: true } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Handled = true; // the text area must not move the caret or start a selection
        clicked(LineAt(e.GetPosition(this).Y));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ToolTip.SetTip(this, TextView is null ? null : _decorations?.ToolTipAt(LineAt(e.GetPosition(this).Y))); // UI-ED-021
    }

    private void DrawGlyph(DrawingContext context, LineGlyph glyph)
    {
        var center = new Point(8, 8);
        switch (glyph.Kind)
        {
            case LineGlyphKind.Breakpoint:
                context.DrawEllipse(Brush("editor.breakpoint"), null, center, 6, 6);
                break;
            case LineGlyphKind.BreakpointConditional:
                context.DrawEllipse(Brush("editor.breakpoint"), null, center, 6, 6);
                DrawEqualsBars(context, Brushes.White);
                break;
            case LineGlyphKind.BreakpointDisabled:
                DrawRing(context);
                break;
            case LineGlyphKind.BreakpointConditionalDisabled:
                DrawRing(context);
                DrawEqualsBars(context, Brush("editor.breakpoint.disabled"));
                break;
            case LineGlyphKind.BreakpointUnverified:
                DrawRing(context);
                DrawCenteredText(context, "?", 8, Brush("editor.breakpoint.disabled"), center);
                break;
            case LineGlyphKind.Execution:
                context.DrawGeometry(Brush("editor.execution"), null, Arrow());
                break;
            case LineGlyphKind.Frame:
                context.DrawGeometry(null, new Pen(Brush("status.success"), 1.25), Arrow());
                break;
            case LineGlyphKind.Bookmark:
                DrawBookmark(context, glyph.Number);
                break;
            case LineGlyphKind.Error:
                DrawErrorMarker(context);
                break;
        }
    }

    private void DrawRing(DrawingContext context) =>
        context.DrawEllipse(null, new Pen(Brush("editor.breakpoint.disabled"), 1.5), new Point(8, 8), 5.25, 5.25);

    /// <summary>Two bars 6 × 1.5 with a 1.5 gap forming <c>=</c>, centred.</summary>
    private static void DrawEqualsBars(DrawingContext context, IBrush? brush)
    {
        if (brush is null)
            return;
        context.FillRectangle(brush, new Rect(5, 5.75, 6, 1.5));
        context.FillRectangle(brush, new Rect(5, 8.75, 6, 1.5));
    }

    /// <summary>Right-pointing block arrow: shaft 6 × 4 from x = 2, head from x = 8 to 14, height 10.</summary>
    private static StreamGeometry Arrow() => Polygon((2, 6), (8, 6), (8, 3), (14, 8), (8, 13), (8, 10), (2, 10));

    /// <summary>Flag 9 × 7 with a V-notch of depth 2 on a 1.25 px pole 12 high at x = 3; a numbered bookmark shows its digit.</summary>
    private void DrawBookmark(DrawingContext context, int number)
    {
        var brush = Brush("editor.bookmark");
        context.FillRectangle(brush ?? Brushes.Transparent, new Rect(3, 2, 1.25, 12));
        context.DrawGeometry(brush, null, Polygon((4.25, 2), (13.25, 2), (11.25, 5.5), (13.25, 9), (4.25, 9)));
        if (number > 0)
            DrawCenteredText(context, number.ToString(CultureInfo.InvariantCulture), 7, Brush("text.onAccent"), new Point(8, 5.5));
    }

    /// <summary>Filled octagon, diameter 8, with a 1 px white ✕.</summary>
    private void DrawErrorMarker(DrawingContext context)
    {
        const double r = 4, k = 1.657; // half side of a regular octagon of circumradius ≈ 4
        context.DrawGeometry(Brush("editor.squiggle.error"), null,
            Polygon((8 - k, 8 - r), (8 + k, 8 - r), (8 + r, 8 - k), (8 + r, 8 + k), (8 + k, 8 + r), (8 - k, 8 + r), (8 - r, 8 + k), (8 - r, 8 - k)));
        var pen = new Pen(Brushes.White, 1);
        context.DrawLine(pen, new Point(6.5, 6.5), new Point(9.5, 9.5));
        context.DrawLine(pen, new Point(9.5, 6.5), new Point(6.5, 9.5));
    }

    private static void DrawCenteredText(DrawingContext context, string text, double size, IBrush? brush, Point center)
    {
        if (brush is null)
            return;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), size, brush);
        context.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }

    private static StreamGeometry Polygon(params (double X, double Y)[] points)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(new Point(points[0].X, points[0].Y), isFilled: true);
        foreach (var (x, y) in points.Skip(1))
            g.LineTo(new Point(x, y));
        g.EndFigure(isClosed: true);
        return geometry;
    }

    private void OnDecorationsChanged(object? sender, EventArgs e) => InvalidateVisual();
}
