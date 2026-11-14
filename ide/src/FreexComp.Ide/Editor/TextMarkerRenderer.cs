using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Editing;

namespace FreexComp.Ide.Editor;

/// <summary>
/// Squiggles and hint dots of the document's text markers (editor.md §6 row 12, §7, UI-ED-062…067). The drawn pieces
/// (<see cref="EditorDecorations.DrawnMarkers"/>: the most severe marker per character) are asked for on every draw,
/// so they follow edits as their providers do (the lexer markers are recomputed per buffer version).
/// </summary>
public sealed class TextMarkerRenderer(Control themeSource) : IBackgroundRenderer
{
    /// <summary>UI-ED-066: a marker over several lines underlines at most this many.</summary>
    public const int MaxLines = 3;

    public EditorDecorations? Decorations { get; set; }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Decorations is not { } decorations || !textView.VisualLinesValid || textView.VisualLines.Count == 0 || textView.Document is not { } document)
            return;
        var markers = decorations.DrawnMarkers;
        if (markers.Count == 0)
            return;
        int first = textView.VisualLines[0].FirstDocumentLine.Offset, last = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var marker in markers)
        {
            if (marker.Offset + marker.Length < first || marker.Offset > last)
                continue;
            var brush = ThemeBrushes.Find(themeSource, marker.Severity switch
            {
                MarkerSeverity.Error => "editor.squiggle.error",
                MarkerSeverity.Warning => "editor.squiggle.warning",
                _ => "editor.squiggle.hint",
            });
            if (brush is null)
                continue;
            foreach (var (left, right, y0) in Spans(textView, document, marker))
                DrawMarker(drawingContext, brush, marker.Severity, left, right, y0);
        }
    }

    /// <summary>
    /// UI-ED-066: per line of the marker (at most <see cref="MaxLines"/>; later lines from their first non-blank
    /// character) the x-range and <c>y0</c> = baseline + 1 of every visual row; an empty range at a line end is one
    /// character wide.
    /// </summary>
    private static IEnumerable<(double Left, double Right, double Y0)> Spans(TextView textView, TextDocument document, TextMarker marker)
    {
        int end = Math.Min(document.TextLength, marker.Offset + marker.Length);
        int start = Math.Min(marker.Offset, document.TextLength);
        var line = document.GetLineByOffset(start);
        for (int n = 0; n < MaxLines && line is not null && (n == 0 || line.Offset < end); n++, line = line.NextLine)
        {
            int segStart = n == 0 ? start : FirstNonBlank(document, line);
            int segEnd = Math.Min(end, line.EndOffset);
            var position = document.GetLocation(segStart);
            var lineTop = textView.GetVisualPosition(new TextViewPosition(position), VisualYPosition.LineTop);
            var baseline = textView.GetVisualPosition(new TextViewPosition(position), VisualYPosition.Baseline);
            double baselineInRow = baseline.Y - lineTop.Y;
            if (segEnd <= segStart)
            {
                if (n > 0 && segEnd < segStart)
                    continue; // a blank continuation line
                var x = lineTop.X - textView.ScrollOffset.X;
                yield return (x, x + textView.WideSpaceWidth, baseline.Y - textView.ScrollOffset.Y + 1);
                continue;
            }

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = segStart, EndOffset = segEnd }))
                yield return (rect.Left, rect.Right, rect.Top + baselineInRow + 1);
        }
    }

    private static int FirstNonBlank(TextDocument document, DocumentLine line)
    {
        int offset = line.Offset;
        while (offset < line.EndOffset && document.GetCharAt(offset) is ' ' or '\t')
            offset++;
        return offset;
    }

    private static void DrawMarker(DrawingContext context, IBrush brush, MarkerSeverity severity, double left, double right, double y0)
    {
        if (severity == MarkerSeverity.Hint)
        {
            foreach (var center in MarkerGeometry.HintDots(left, y0))
                context.DrawEllipse(brush, null, center, MarkerGeometry.HintDotDiameter / 2, MarkerGeometry.HintDotDiameter / 2);
            return;
        }

        var points = MarkerGeometry.Squiggle(left, right, y0, severity == MarkerSeverity.Error ? MarkerGeometry.ErrorPeakToPeak : MarkerGeometry.WarningPeakToPeak);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false);
            foreach (var p in points.Skip(1))
                g.LineTo(p);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(brush, 1), geometry);
    }
}

/// <summary>The marker shapes of UI-ED-063…065 (pure geometry, for the renderer and its tests).</summary>
public static class MarkerGeometry
{
    public const double WavePeriod = 4, ErrorPeakToPeak = 3, WarningPeakToPeak = 2;
    public const double HintDotDiameter = 1.5, HintDotPitch = 3;
    public const int HintDotCount = 3;

    /// <summary>
    /// UI-ED-063/064: vertices every 2 px alternating between <paramref name="y0"/> and <paramref name="y0"/> +
    /// <paramref name="peakToPeak"/>; the wave starts at <paramref name="left"/> snapped down to a multiple of 4 px (so
    /// adjacent markers align) and runs until it reaches <paramref name="right"/>.
    /// </summary>
    public static IReadOnlyList<Point> Squiggle(double left, double right, double y0, double peakToPeak)
    {
        var points = new List<Point>();
        double x = Math.Floor(left / WavePeriod) * WavePeriod;
        for (int i = 0; ; i++, x += WavePeriod / 2)
        {
            points.Add(new Point(x, i % 2 == 0 ? y0 : y0 + peakToPeak));
            if (x >= right)
                break;
        }

        if (points.Count == 1)
            points.Add(new Point(x + WavePeriod / 2, y0 + peakToPeak));
        return points;
    }

    /// <summary>UI-ED-065: the centres of three 1.5 px dots at a 3 px pitch, from <paramref name="left"/>, under the baseline.</summary>
    public static IReadOnlyList<Point> HintDots(double left, double y0) =>
        [.. Enumerable.Range(0, HintDotCount).Select(i => new Point(left + HintDotDiameter / 2 + i * HintDotPitch, y0 + HintDotDiameter / 2))];
}
