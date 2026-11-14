using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;

namespace FreexComp.Ide.Editor;

/// <summary>
/// Fold margin (UI-ED-030…032): 14 px, reads the foldings of the view's <see cref="FoldingManager"/> (AvaloniaEdit's
/// own folding margin is not used). Chevrons 12 × 12, stroke 1.25, <c>editor.foldMarker</c>: <c>⌄</c> expanded,
/// <c>›</c> collapsed. Expanded markers show only while the pointer is near the gutter or the caret is inside their
/// region; collapsed markers always. A click on a marker toggles its fold.
/// </summary>
public sealed class FoldMargin : GutterMargin
{
    private FoldingManager? _manager;
    private bool _pointerNear;

    public FoldMargin(TextArea area) : base(area) =>
        area.Caret.PositionChanged += (_, _) => InvalidateVisual();

    public override double MarginWidth => 14;

    /// <summary>The foldings drawn; set by the editor view (null while unbound).</summary>
    public FoldingManager? Manager
    {
        get => _manager;
        set
        {
            _manager = value;
            InvalidateVisual();
        }
    }

    /// <summary>UI-ED-032: the pointer is over the gutter or the text area's first 24 px; reported by the editor view.</summary>
    public bool IsPointerNear
    {
        get => _pointerNear;
        set
        {
            if (_pointerNear == value)
                return;
            _pointerNear = value;
            InvalidateVisual();
        }
    }

    protected override void RenderLine(DrawingContext context, VisualLine line, int lineNumber, double top, double rowHeight, double height)
    {
        if (FoldingStartingOn(line) is not { } folding)
            return;
        var caret = Area.Caret.Offset;
        if (!folding.IsFolded && !_pointerNear && (caret < folding.StartOffset || caret > folding.EndOffset))
            return;

        var pen = new Pen(Brush("editor.foldMarker"), 1.25, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        double x = 1, y = Math.Round(top + (rowHeight - 12) / 2); // the 12 × 12 icon centred in the 14 px margin
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            if (folding.IsFolded)
            {
                g.BeginFigure(new Point(x + 4.5, y + 3), isFilled: false);
                g.LineTo(new Point(x + 7.5, y + 6));
                g.LineTo(new Point(x + 4.5, y + 9));
            }
            else
            {
                g.BeginFigure(new Point(x + 3, y + 4.5), isFilled: false);
                g.LineTo(new Point(x + 6, y + 7.5));
                g.LineTo(new Point(x + 9, y + 4.5));
            }

            g.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (TextView is not { } textView || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var line = textView.GetVisualLineFromVisualTop(e.GetPosition(this).Y + textView.VerticalOffset);
        if (line is not null && FoldingStartingOn(line) is { } folding)
        {
            folding.IsFolded = !folding.IsFolded;
            e.Handled = true;
            InvalidateVisual();
        }
    }

    /// <summary>The outermost folding that starts on the visual line's first document line.</summary>
    private FoldingSection? FoldingStartingOn(VisualLine line)
    {
        if (_manager is null)
            return null;
        var first = line.FirstDocumentLine;
        var folding = _manager.GetNextFolding(first.Offset);
        return folding is not null && folding.StartOffset <= first.EndOffset ? folding : null;
    }
}
