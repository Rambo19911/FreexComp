using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subject of the quick-action glyph (F13-003, I-352).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The main window at 800 × 500, light and dark, at scale 1 and 1.5, with the caret resting on the undeclared
    /// variable of line 7: <c>editor-quick-action-glyph</c>. Rules (UI-ED-150): the glyph slot of that line draws the bulb
    /// in <c>status.hint</c> and nothing of the error marker it hides (<c>editor.squiggle.error</c>); a bookmark (line 3)
    /// and a breakpoint (line 5) are drawn beside it for comparison; the slots of the other lines stay empty.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-150")]
    public async Task QuickActionGlyph_DrawsTheBulbInTheHintColour()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string subject = "editor-quick-action-glyph";
        const int line = 7;
        if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
            Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        var size = new Size(800, 500);
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Theme, double Scale, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            var (h, editor, doc, probe, asked) = await QuickActionTests.StartWithMarkerAsync(o => o.Settings["appearance.theme"] = theme);
            await using var _ = h;
            await h.ExecuteAsync("search.bookmark.toggle", null); // the caret is on line 2 …
            QuickActionTests.PlaceCaret(editor, doc, editor.Document.GetLineByNumber(3).Offset);
            await h.ExecuteAsync("search.bookmark.toggle", null);
            QuickActionTests.PlaceCaret(editor, doc, editor.Document.GetLineByNumber(2).Offset);
            await h.ExecuteAsync("search.bookmark.toggle", null); // … and its bookmark is taken away again
            h.Services.Breakpoints!.Toggle(BuildScenario.UnitPath(h), 5);
            QuickActionTests.PlaceCaret(editor, doc, QuickActionTests.Unit.IndexOf("Count", StringComparison.Ordinal) + 2);
            await QuickActionTests.RestAsync(h, asked);
            Assert.Equal([null, null, "Bookmark", null, "Breakpoint", null, "QuickAction", null, null, null],
                Enumerable.Range(1, 10).Select(probe.GlyphAt));

            var hint = Token(h.Window, "status.hint");
            var error = Token(h.Window, "editor.squiggle.error");
            foreach (var scale in (double[])[1.0, 1.5])
            {
                VisualSnapshot.Arrange(h.Window, size, scale);
                var variant = $"{subject} {VisualSnapshot.FileName(theme, scale, size)}";
                var gutter = editor.TextArea.LeftMargins.OfType<GlyphMargin>().Single();
                if (VisualSnapshot.HiddenPart(gutter, h.Window) is { } hidden)
                    defects.Add((variant, "cut: the glyph margin — " + hidden));
                int[] Slot(int row) => VisualSnapshot.PixelsOf(h.Window, new Rect(
                    gutter.TranslatePoint(new Point(0, (row - 1) * editor.TextArea.TextView.DefaultLineHeight), h.Window)!.Value,
                    new Size(gutter.Bounds.Width, editor.TextArea.TextView.DefaultLineHeight)));

                var bulb = Slot(line);
                if (bulb.Count(p => Near(p, hint)) < 8 * scale)
                    defects.Add((variant, $"the bulb is not drawn in status.hint ({bulb.Count(p => Near(p, hint))} pixels)"));
                if (bulb.Any(p => Near(p, error)))
                    defects.Add((variant, "the error marker is drawn under the bulb"));
                if (Slot(3).Distinct().Count() < 2 || Slot(5).Distinct().Count() < 2)
                    defects.Add((variant, "the bookmark or the breakpoint is not drawn"));
                foreach (var empty in (int[])[1, 2, 4, 6, 8])
                {
                    if (Slot(empty).Any(p => Near(p, hint)))
                        defects.Add((variant, $"line {empty} draws the hint colour in its glyph slot"));
                }

                files.Add((theme, scale, VisualSnapshot.Capture(h.Window, subject, theme)));
            }
        }

        Assert.Equal(4, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length); // 2 themes × 2 scales
        foreach (var (_, scale, file) in files)
        {
            Assert.Equal(((int)(size.Width * scale), (int)(size.Height * scale)), VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Scale == light.Scale);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>The colour of a design token in the window's theme.</summary>
    private static Color Token(Window window, string token) =>
        window.TryFindResource(IconCatalog.BrushKey(token), window.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException("No brush for " + token);

    /// <summary>The pixel has the colour, within the tolerance of text and edge smoothing; either byte order of the frame.</summary>
    private static bool Near(int pixel, Color color)
    {
        const int tolerance = 12;
        int a = pixel & 0xFF, g = (pixel >> 8) & 0xFF, b = (pixel >> 16) & 0xFF;
        return Math.Abs(g - color.G) <= tolerance
            && ((Math.Abs(a - color.B) <= tolerance && Math.Abs(b - color.R) <= tolerance)
                || (Math.Abs(a - color.R) <= tolerance && Math.Abs(b - color.B) <= tolerance));
    }
}
