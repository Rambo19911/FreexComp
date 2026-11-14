using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Editor;

/// <summary>
/// Bracket match (UI-ED-053): each highlighted bracket or keyword gets <c>editor.bracketMatch.bg</c> and a 1 px
/// <c>editor.bracketMatch.border</c> outline; a bracket without partner only a 1 px <c>editor.squiggle.error</c>
/// outline. The highlights are asked for on every draw.
/// </summary>
public sealed class BracketMatchRenderer(Control themeSource, Func<BracketMatch?> match) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (match() is not { } m || !textView.VisualLinesValid)
            return;
        var paired = m.Partner is not null;
        var background = paired ? ThemeBrushes.Find(themeSource, "editor.bracketMatch.bg") : null;
        var outline = ThemeBrushes.Find(themeSource, paired ? "editor.bracketMatch.border" : "editor.squiggle.error");
        var pen = outline is null ? null : new Pen(outline, 1);
        foreach (var token in m.Partner is { } partner ? new[] { m.Bracket, partner } : [m.Bracket])
        {
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = token.Offset, Length = token.Length }))
            {
                // Pixel-snapped so the 1 px outline covers whole pixels.
                var r = new Rect(Math.Round(rect.X) + 0.5, Math.Round(rect.Y) + 0.5, Math.Max(1, Math.Round(rect.Width) - 1), Math.Max(1, Math.Round(rect.Height) - 1));
                drawingContext.DrawRectangle(background, pen, r);
            }
        }
    }
}

/// <summary>Word-occurrence highlight (UI-ED-054): <c>editor.wordHighlight.bg</c> behind each occurrence in the visible lines.</summary>
public sealed class OccurrenceRenderer(Control themeSource, Func<IReadOnlyList<CodeToken>> occurrences) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var tokens = occurrences();
        if (tokens.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0
            || ThemeBrushes.Find(themeSource, "editor.wordHighlight.bg") is not { } brush)
            return;
        int first = textView.VisualLines[0].FirstDocumentLine.Offset, last = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var token in tokens)
        {
            if (token.End < first || token.Offset > last)
                continue; // GetRectsForSegment would build lines outside the view
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = token.Offset, Length = token.Length }))
                drawingContext.FillRectangle(brush, rect);
        }
    }
}

/// <summary>
/// Indentation guides (UI-ED-056, IDE-ED-073): 1 px dotted lines (1 on, 2 off) in <c>editor.indentGuide</c> at the guide
/// columns of each visible line; the guide of the innermost block containing the caret in
/// <c>editor.indentGuide.active</c>. X = left edge of the column, pixel-snapped.
/// </summary>
public sealed class IndentGuideRenderer(Control themeSource, Func<IndentGuideRenderer.State?> state) : IBackgroundRenderer
{
    /// <summary>What to draw: the buffer, the tab width and the active guide (null when guides are off).</summary>
    public readonly record struct State(ITextBuffer Buffer, int TabWidth, ActiveGuide? Active);

    private static readonly DashStyle Dotted = new([1, 2], 0);

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (state() is not { } s || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
            return;
        var lines = textView.VisualLines;
        int first = lines[0].FirstDocumentLine.LineNumber, last = lines[^1].LastDocumentLine.LineNumber;
        var guides = IndentGuides.Compute(s.Buffer, first, last, s.TabWidth);
        Pen? normal = null, active = null;
        foreach (var line in lines)
        {
            var number = line.FirstDocumentLine.LineNumber;
            double top = line.VisualTop - textView.VerticalOffset, bottom = top + line.Height;
            foreach (var column in guides[number - first])
            {
                var isActive = s.Active is { } a && a.Column == column && number >= a.FirstLine && number <= a.LastLine;
                var pen = isActive
                    ? active ??= PenFor("editor.indentGuide.active")
                    : normal ??= PenFor("editor.indentGuide");
                if (pen is null)
                    continue;
                var x = Math.Round(column * textView.WideSpaceWidth - textView.HorizontalOffset) + 0.5;
                drawingContext.DrawLine(pen, new Point(x, top), new Point(x, bottom));
            }
        }
    }

    private Pen? PenFor(string token) => ThemeBrushes.Find(themeSource, token) is { } brush ? new Pen(brush, 1, Dotted) : null;
}
