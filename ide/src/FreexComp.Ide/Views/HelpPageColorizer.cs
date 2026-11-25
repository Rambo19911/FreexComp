using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Views;

/// <summary>
/// UI-MENU-032: the line styles of a help document over its body text (<c>type.body</c>). Heading lines are
/// <c>type.bodyStrong</c> in <c>text.primary</c>; the lines of an aligned table use <c>type.code</c> so that their
/// columns line up. The kind of every line comes from <see cref="HelpPageFormatter"/>.
/// <para>I-266: the header row of a table is bold (700), its other rows regular (400). The code font has no usable
/// semibold: its first family draws 600 hardly heavier than its strong regular face, so every row looked like the
/// header row, and the families after it have a regular and a bold face only, where 600 is the bold face emboldened
/// once more.</para>
/// </summary>
internal sealed class HelpPageColorizer(
    Func<IReadOnlyList<HelpLineKind>?> lines, Func<IBrush?> headingBrush, Func<FontFamily?> codeFont, Func<double> codeSize)
    : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0 || lines() is not { } kinds || line.LineNumber > kinds.Count)
            return;
        var kind = kinds[line.LineNumber - 1];
        if (kind is HelpLineKind.Text or HelpLineKind.ListItem)
            return;
        var table = kind is HelpLineKind.TableHeader or HelpLineKind.TableRow;
        var weight = kind switch
        {
            HelpLineKind.Heading => FontWeight.SemiBold,
            HelpLineKind.TableHeader => FontWeight.Bold,
            _ => FontWeight.Normal,
        };
        var foreground = kind == HelpLineKind.Heading ? headingBrush() : null;
        var family = table ? codeFont() : null;
        ChangeLinePart(line.Offset, line.EndOffset, element =>
        {
            var face = element.TextRunProperties.Typeface;
            element.TextRunProperties.SetTypeface(new Typeface(family ?? face.FontFamily, face.Style, weight, face.Stretch));
            if (table)
                element.TextRunProperties.SetFontRenderingEmSize(codeSize());
            if (foreground is not null)
                element.TextRunProperties.SetForegroundBrush(foreground);
        });
    }
}

/// <summary>
/// UI-MENU-032 (I-266): a wrapped list item of a help document continues under its text, not under its marker. The
/// editor cannot indent the wrapped lines of a line (its word-wrap indentation has no effect with this UI library
/// version), so the lines of a list item are broken here: the item's text (after its indent, marker and spaces, see
/// <see cref="HelpPageFormatter.ListItemTextStart"/>) is wrapped at the view's width less the marker's width, and at
/// every break the visual line gets a line end followed by an empty run as wide as the marker. Both exist in the
/// visual line only: the document text is unchanged and the caret does not stop at them. Two generators are added to
/// the view, one for the line ends (<paramref name="indent"/> false) and after it one for the indents: a generator
/// gives one element per offset. The view redraws when its width changes (the breaks depend on it).
/// </summary>
internal sealed class HelpListItemWrapGenerator(Func<IReadOnlyList<HelpLineKind>?> lines, bool indent) : VisualLineElementGenerator
{
    private readonly List<int> _breaks = [];
    private double _indent;

    public override void StartGeneration(ITextRunConstructionContext context)
    {
        base.StartGeneration(context);
        _breaks.Clear();
        var line = context.VisualLine.FirstDocumentLine;
        if (lines() is not { } kinds || line.LineNumber > kinds.Count || kinds[line.LineNumber - 1] != HelpLineKind.ListItem)
            return;
        var text = context.Document.GetText(line);
        var start = HelpPageFormatter.ListItemTextStart(text);
        if (start == 0)
            return;
        var run = context.GlobalTextRunProperties;
        _indent = new TextLayout(text[..start], run.Typeface, run.FontRenderingEmSize, null).WidthIncludingTrailingWhitespace;
        var width = Math.Floor(context.TextView.Bounds.Width - _indent) - 1;
        if (width < _indent) // like the editor: no indent of more than half the view
            return;
        var layout = new TextLayout(text[start..], run.Typeface, run.FontRenderingEmSize, null, textWrapping: TextWrapping.Wrap, maxWidth: width);
        var offset = line.Offset + start;
        foreach (var textLine in layout.TextLines.Take(layout.TextLines.Count - 1))
        {
            offset += textLine.Length;
            _breaks.Add(offset);
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        foreach (var offset in _breaks)
        {
            if (offset >= startOffset)
                return offset;
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset) =>
        !_breaks.Contains(offset) ? null : indent ? new Indent(_indent) : new LineEnd();

    /// <summary>Ends the text line; nothing of the document.</summary>
    private sealed class LineEnd() : VisualLineElement(1, 0)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new TextEndOfLine(1);

        public override int GetNextCaretPosition(int visualColumn, AvaloniaEdit.Document.LogicalDirection direction, CaretPositioningMode mode) => -1;
    }

    /// <summary>Empty space of the marker's width at the start of a wrapped line; nothing of the document.</summary>
    private sealed class Indent(double width) : VisualLineElement(1, 0)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new Space(width, TextRunProperties);

        public override int GetNextCaretPosition(int visualColumn, AvaloniaEdit.Document.LogicalDirection direction, CaretPositioningMode mode) => -1;
    }

    private sealed class Space(double width, TextRunProperties properties) : DrawableTextRun
    {
        public override Size Size => new(width, 0);

        public override double Baseline => 0;

        public override TextRunProperties Properties => properties;

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
        }
    }
}
