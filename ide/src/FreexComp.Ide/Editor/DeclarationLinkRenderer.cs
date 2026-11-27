using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace FreexComp.Ide.Editor;

/// <summary>
/// IDE-ED-149 (I-322): the Ctrl+hover link, a 1 px underline in <see cref="Token"/> under the identifier, on its text
/// baseline row (the bottom of each line rectangle the identifier covers). The span is asked for on every draw.
/// </summary>
public sealed class DeclarationLinkRenderer(Control themeSource, Func<(int Offset, int Length)?> link) : IBackgroundRenderer
{
    /// <summary>The colour token of the underline (design-tokens.md <c>text.link</c>).</summary>
    public const string Token = "text.link";

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (link() is not { } span || !textView.VisualLinesValid || textView.VisualLines.Count == 0
            || ThemeBrushes.Find(themeSource, Token) is not { } brush)
            return;
        int first = textView.VisualLines[0].FirstDocumentLine.Offset, last = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        if (span.Offset + span.Length < first || span.Offset > last)
            return; // GetRectsForSegment would build lines outside the view
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = span.Offset, Length = span.Length }))
            drawingContext.FillRectangle(brush, new Rect(rect.Left, Math.Floor(rect.Bottom) - 1, rect.Width, 1));
    }
}
