using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;

namespace FreexComp.Ide.Editor;

/// <summary>
/// The <c>…</c> box of a collapsed region (UI-ED-034): text in <c>editor.foldMarker</c> on <c>editor.foldedText.bg</c>,
/// 1 px outline in <c>editor.foldMarker</c> at 50 % opacity, corner radius <c>radius.control</c> (2), horizontal
/// padding 4, height = line height − 2. AvaloniaEdit's own folding generator is sealed and draws a plain frame, so this
/// one wraps it: the wrapped generator keeps the folding manager connected to the text view and decides where a
/// collapsed region starts and how much text it hides; only the element it builds is replaced. A double-click on the box
/// asks <see cref="BoxDoubleClicked"/> (UI-ED-035, IDE-ED-058).
/// </summary>
public sealed class FoldBoxGenerator : VisualLineElementGenerator, ITextViewConnect
{
    private const double Padding = 4;
    private const double Radius = 2;

    private readonly FoldingElementGenerator _inner;
    private readonly Func<string, IBrush?> _brush;

    public FoldBoxGenerator(FoldingElementGenerator inner, Func<string, IBrush?> brush)
    {
        _inner = inner;
        _brush = brush;
    }

    public FoldingElementGenerator Inner => _inner;

    /// <summary>A double-click on the box of the folding.</summary>
    public Action<FoldingSection>? BoxDoubleClicked { get; set; }

    void ITextViewConnect.AddToTextView(TextView textView) => ((ITextViewConnect)_inner).AddToTextView(textView);

    void ITextViewConnect.RemoveFromTextView(TextView textView) => ((ITextViewConnect)_inner).RemoveFromTextView(textView);

    public override void StartGeneration(ITextRunConstructionContext context)
    {
        base.StartGeneration(context);
        _inner.StartGeneration(context);
    }

    public override void FinishGeneration()
    {
        _inner.FinishGeneration();
        base.FinishGeneration();
    }

    public override int GetFirstInterestedOffset(int startOffset) => _inner.GetFirstInterestedOffset(startOffset);

    public override VisualLineElement? ConstructElement(int offset)
    {
        if (_inner.ConstructElement(offset) is not { } builtIn || _inner.FoldingManager is not { } manager)
            return null;
        FoldingSection? section = null;
        foreach (var folding in manager.GetFoldingsAt(offset))
        {
            if (folding.IsFolded && (section is null || folding.EndOffset > section.EndOffset))
                section = folding;
        }

        if (section is null)
            return builtIn;
        var properties = new VisualLineElementTextRunProperties(CurrentContext.GlobalTextRunProperties);
        if (_brush("editor.foldMarker") is { } text)
            properties.SetForegroundBrush(text);
        var line = FormattedTextElement.PrepareText(TextFormatter.Current, section.Title, properties);
        return new FoldBoxElement(this, section, line, builtIn.DocumentLength);
    }

    private sealed class FoldBoxElement(FoldBoxGenerator owner, FoldingSection section, TextLine text, int documentLength)
        : FormattedTextElement(text, documentLength)
    {
        public FoldingSection Section => section;

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) =>
            new FoldBoxRun(this, TextRunProperties, owner._brush);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.ClickCount == 2 && owner.BoxDoubleClicked is { } handler)
            {
                handler(section);
                e.Handled = true;
                return;
            }

            base.OnPointerPressed(e);
        }
    }

    private sealed class FoldBoxRun(FormattedTextElement element, TextRunProperties properties, Func<string, IBrush?> brush)
        : FormattedTextRun(element, properties)
    {
        public override Size Size => base.Size + new Size(2 * Padding, 0);

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            var size = Size;
            var box = new Rect(origin.X + 0.5, origin.Y + 1.5, size.Width - 1, Math.Max(0, size.Height - 3));
            var marker = brush("editor.foldMarker");
            var outline = marker is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.5) : null;
            drawingContext.DrawRectangle(brush("editor.foldedText.bg"), outline is null ? null : new Pen(outline, 1), box, Radius, Radius);
            base.Draw(drawingContext, origin + new Point(Padding, 0));
        }
    }

    /// <summary>The folding whose box covers <paramref name="visualColumn"/> of <paramref name="line"/>, or null.</summary>
    public static FoldingSection? BoxAt(VisualLine line, int visualColumn) =>
        line.Elements.OfType<FoldBoxElement>().FirstOrDefault(e => e.VisualColumn <= visualColumn && visualColumn < e.VisualColumn + e.VisualLength)?.Section;
}
