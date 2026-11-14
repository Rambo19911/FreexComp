using System.Globalization;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace FreexComp.Ide.Editor;

/// <summary>
/// Line numbers (UI-ED-010…012): width <c>(max(3, digits) + 1) × charWidth</c>, right-aligned with one
/// <c>charWidth</c> padding, <c>editor.lineNumber</c> / <c>editor.lineNumber.current</c> on the caret line. Click
/// selects a whole line, drag a range of lines, Shift+click extends.
/// </summary>
public sealed class LineNumberMargin : GutterMargin
{
    private int _digits = 1;
    private int? _anchorLine;
    private Selection? _ownSelection;

    public LineNumberMargin(TextArea area) : base(area) =>
        area.Caret.PositionChanged += (_, _) => InvalidateVisual();

    /// <summary>Advance width of <c>0</c> in the editor font (UI-ED-010).</summary>
    public double CharWidth => Format("0", Brushes.Black).WidthIncludingTrailingWhitespace;

    public override double MarginWidth => (Math.Max(3, _digits) + 1) * CharWidth;

    protected override void OnDocumentChanged(TextDocument? oldDocument, TextDocument? newDocument)
    {
        if (oldDocument is not null)
            oldDocument.LineCountChanged -= OnLineCountChanged;
        base.OnDocumentChanged(oldDocument, newDocument);
        if (newDocument is not null)
            newDocument.LineCountChanged += OnLineCountChanged;
        OnLineCountChanged(null, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextElement.FontFamilyProperty || change.Property == TextElement.FontSizeProperty)
            InvalidateMeasure();
    }

    protected override void RenderLine(DrawingContext context, VisualLine line, int lineNumber, double top, double rowHeight, double height)
    {
        var brush = Brush(lineNumber == Area.Caret.Line ? "editor.lineNumber.current" : "editor.lineNumber") ?? Brushes.Gray;
        var text = Format(lineNumber.ToString(CultureInfo.InvariantCulture), brush);
        var x = MarginWidth - CharWidth - text.WidthIncludingTrailingWhitespace;
        context.DrawText(text, new Point(x, top + (rowHeight - text.Height) / 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (TextView is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Handled = true;
        var line = LineAt(e.GetPosition(this).Y);
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _anchorLine = extend
            ? (_ownSelection is not null && ReferenceEquals(_ownSelection, Area.Selection) ? _anchorLine : null) ?? Area.Caret.Line
            : line;
        SelectLines(_anchorLine.Value, line);
        e.Pointer.Capture(this);
        Area.Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_anchorLine is { } anchor && ReferenceEquals(e.Pointer.Captured, this))
        {
            SelectLines(anchor, LineAt(e.GetPosition(this).Y));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <summary>Lines <paramref name="anchor"/> to <paramref name="line"/> with their line breaks; the caret ends at the far edge.</summary>
    private void SelectLines(int anchor, int line)
    {
        var document = Area.Document;
        DocumentLine first = document.GetLineByNumber(Math.Min(anchor, line)), last = document.GetLineByNumber(Math.Max(anchor, line));
        int start = first.Offset, end = last.EndOffset + last.DelimiterLength;
        var (from, to) = line >= anchor ? (start, end) : (end, start);
        Area.Caret.Offset = to;
        _ownSelection = Selection.Create(Area, from, to);
        Area.Selection = _ownSelection;
    }

    private void OnLineCountChanged(object? sender, EventArgs e)
    {
        var digits = (Document?.LineCount ?? 1).ToString(CultureInfo.InvariantCulture).Length;
        if (digits == _digits)
            return;
        _digits = digits;
        InvalidateMeasure();
    }

    private FormattedText Format(string text, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(GetValue(TextElement.FontFamilyProperty)), GetValue(TextElement.FontSizeProperty), brush);
}
