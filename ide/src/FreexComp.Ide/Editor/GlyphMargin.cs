using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Editor;

/// <summary>
/// Glyph margin (editor.md §3): 2 px left padding + one 16 × 16 slot per line with the highest-priority glyph of the
/// document's <see cref="EditorDecorations"/>; an execution arrow on a breakpoint line is drawn over the disc. Vector
/// geometry per §3.1; the quick-action glyph is the icon <c>hint</c> (UI-ED-150) — an indicator, a click on it is a click
/// on the margin. The tooltip has one line per glyph on the line (UI-ED-021, <see cref="EditorDecorations.ToolTipAt"/>); a
/// right click asks for the gutter menu (UI-ED-022, <see cref="GutterMargin.ContextMenuRequested"/>); a left click is
/// reported through <see cref="LineClicked"/> (UI-ED-012).
/// </summary>
public sealed class GlyphMargin(TextArea area) : GutterMargin(area)
{
    private const double SlotLeft = 2;
    private EditorDecorations? _decorations;
    private int? _pointerLine; // the 1-based line under the pointer; null while it is elsewhere

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
                DrawGlyph(context, breakpoint, Brush);
                context.DrawGeometry(Brush("editor.execution"), new Pen(Brush("bg.gutter"), 1), Arrow());
            }
            else
            {
                DrawGlyph(context, glyph, Brush);
            }
        }
    }

    /// <summary>UI-ED-012 (P6): a left click on the margin; the 1-based line is passed on (it toggles a breakpoint there).</summary>
    public Action<int>? LineClicked { get; set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        TrackPointer(e); // a pointer that arrives with the click (no move before it)
        if (e.Handled || LineClicked is not { } clicked || TextView is not { VisualLinesValid: true } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Handled = true; // the text area must not move the caret or start a selection
        clicked(LineAt(e.GetPosition(this).Y));
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        TrackPointer(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        TrackPointer(e);
    }

    /// <summary>The pointer is over a line of the margin: its tooltip is that line's (UI-ED-021).</summary>
    private void TrackPointer(PointerEventArgs e)
    {
        _pointerLine = TextView is null ? null : LineAt(e.GetPosition(this).Y);
        UpdateToolTip();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerLine = null;
    }

    /// <summary>UI-ED-021: the tooltip of the line under the pointer — also when its glyphs change while the pointer rests there.</summary>
    private void UpdateToolTip() => ToolTip.SetTip(this, _pointerLine is { } line ? _decorations?.ToolTipAt(line) : null);

    /// <summary>
    /// Draws <paramref name="glyph"/> in a 16 × 16 slot at the origin (§3.1); <paramref name="Brush"/> gives the brush of a
    /// design token. Shared with the debug tool windows, whose State and Marker columns show the same glyphs.
    /// </summary>
    internal static void DrawGlyph(DrawingContext context, LineGlyph glyph, Func<string, IBrush?> Brush)
    {
        var center = new Point(8, 8);
        switch (glyph.Kind)
        {
            case LineGlyphKind.Breakpoint:
                context.DrawEllipse(Brush("editor.breakpoint"), null, center, 6, 6);
                break;
            case LineGlyphKind.BreakpointConditional:
                context.DrawEllipse(Brush("editor.breakpoint"), null, center, 6, 6);
                DrawEqualsBars(context, Brush("editor.glyph.onFill"));
                break;
            case LineGlyphKind.BreakpointDisabled:
                DrawRing(context, Brush);
                break;
            case LineGlyphKind.BreakpointConditionalDisabled:
                DrawRing(context, Brush);
                DrawEqualsBars(context, Brush("editor.breakpoint.disabled"));
                break;
            case LineGlyphKind.BreakpointUnverified:
                DrawRing(context, Brush);
                DrawCenteredText(context, "?", 8, Brush("editor.breakpoint.disabled"), center);
                break;
            case LineGlyphKind.Execution:
                context.DrawGeometry(Brush("editor.execution"), null, Arrow());
                break;
            case LineGlyphKind.Frame:
                context.DrawGeometry(null, new Pen(Brush("status.success"), 1.25), Arrow());
                break;
            case LineGlyphKind.Bookmark:
                DrawBookmark(context, glyph.Number, Brush);
                break;
            case LineGlyphKind.QuickAction:
                DrawIcon(context, "hint", Brush);
                break;
            case LineGlyphKind.Error:
                DrawErrorMarker(context, Brush);
                break;
        }
    }

    /// <summary>An icon of toolbars.md §4 on its 16 × 16 grid, every layer in the colour of its design token.</summary>
    private static void DrawIcon(DrawingContext context, string name, Func<string, IBrush?> Brush)
    {
        foreach (var layer in IconCatalog.Get(name).Layers)
        {
            var brush = Brush(layer.ColorToken);
            if (layer.IsFilled)
                context.DrawGeometry(brush, null, layer.Geometry);
            else
                context.DrawGeometry(null, new Pen(brush, IconCatalog.StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), layer.Geometry);
        }
    }

    private static void DrawRing(DrawingContext context, Func<string, IBrush?> Brush) =>
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
    private static void DrawBookmark(DrawingContext context, int number, Func<string, IBrush?> Brush)
    {
        var brush = Brush("editor.bookmark");
        context.FillRectangle(brush ?? Brushes.Transparent, new Rect(3, 2, 1.25, 12));
        context.DrawGeometry(brush, null, Polygon((4.25, 2), (13.25, 2), (11.25, 5.5), (13.25, 9), (4.25, 9)));
        if (number > 0)
            DrawCenteredText(context, number.ToString(CultureInfo.InvariantCulture), 7, Brush("text.onAccent"), new Point(8, 5.5));
    }

    /// <summary>Filled octagon, diameter 8, with a 1 px ✕ in <c>editor.glyph.onFill</c>.</summary>
    private static void DrawErrorMarker(DrawingContext context, Func<string, IBrush?> Brush)
    {
        const double r = 4, k = 1.657; // half side of a regular octagon of circumradius ≈ 4
        context.DrawGeometry(Brush("editor.squiggle.error"), null,
            Polygon((8 - k, 8 - r), (8 + k, 8 - r), (8 + r, 8 - k), (8 + r, 8 + k), (8 + k, 8 + r), (8 - k, 8 + r), (8 - r, 8 + k), (8 - r, 8 - k)));
        var pen = new Pen(Brush("editor.glyph.onFill"), 1);
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

    private void OnDecorationsChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
        if (_pointerLine is not null)
            UpdateToolTip();
    }
}
