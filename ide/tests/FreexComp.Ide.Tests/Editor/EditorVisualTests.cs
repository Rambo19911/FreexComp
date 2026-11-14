using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using LineNumberMargin = FreexComp.Ide.Editor.LineNumberMargin;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Gutter, current line, ruler and whitespace of the code editor (I-045, editor.md §2–§6, §13.3/§13.4).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class EditorVisualTests
{
    private const double GlyphWidth = 18, FoldWidth = 14, ChangeBarWidth = 3;

    private static string Lines(int count) => string.Join("\r\n", Enumerable.Range(1, count).Select(i => "x" + i));

    private static async Task<(TextDocumentViewModel Document, TextEditor Editor, IEditorTestProbe Probe)> OpenAsync(HeadlessHarness h, string fileName)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, fileName));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor." + fileName);
        var view = editor.FindAncestorOfType<CodeEditorView>();
        Assert.NotNull(view);
        return (Assert.IsType<TextDocumentViewModel>(result.Value), editor, view);
    }

    private static void Set<T>(HeadlessHarness h, SettingKey<T> key, T value) where T : notnull
    {
        h.Services.Settings.Set(key, value);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>What <paramref name="visual"/> draws, flattened: (fill colour or null, rectangle bounds) per geometry, and glyph-run colours.</summary>
    private static List<(Color? Fill, Rect Bounds)> Shapes(Visual visual) =>
        [.. Recorded(visual).OfType<GeometryDrawing>().Select(d => ((d.Brush as ISolidColorBrush)?.Color, d.Geometry?.Bounds ?? default))];

    private static List<Color> TextColors(Visual visual) =>
        [.. Recorded(visual).OfType<GlyphRunDrawing>().Select(d => Assert.IsAssignableFrom<ISolidColorBrush>(d.Foreground).Color)];

    private static IEnumerable<Drawing> Recorded(Visual visual)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
            visual.Render(context);
        return Flatten(group);

        static IEnumerable<Drawing> Flatten(Drawing drawing) =>
            drawing is DrawingGroup g ? g.Children.SelectMany(Flatten) : [drawing];
    }

    private static T Margin<T>(TextEditor editor) where T : AbstractMargin => editor.TextArea.LeftMargins.OfType<T>().Single();

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-010")]
    [Trait("Req", "UI-ED-004")]
    public async Task GutterWidthFollowsLineCount()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = Lines(9));
        var (doc, editor, probe) = await OpenAsync(h, "A.pas");

        // §2: our margins in order, AvaloniaEdit's own line-number and folding margins are not used.
        Assert.Equal([typeof(GlyphMargin), typeof(LineNumberMargin), typeof(FoldMargin), typeof(ChangeBarMargin), typeof(TextPaddingMargin)],
            editor.TextArea.LeftMargins.Select(m => m.GetType()));
        Assert.False(editor.ShowLineNumbers);

        var nine = probe.GutterWidth;
        Set(h, SettingsCatalog.EditorShowLineNumbers, false);
        Assert.Equal(GlyphWidth + FoldWidth + ChangeBarWidth, probe.GutterWidth);
        Set(h, SettingsCatalog.EditorShowFoldMargin, false);
        Assert.Equal(GlyphWidth + ChangeBarWidth, probe.GutterWidth);
        Set(h, SettingsCatalog.EditorShowChangeBar, false);
        Assert.Equal(GlyphWidth, probe.GutterWidth); // UI-ED-004: the glyph margin is always there
        Set(h, SettingsCatalog.EditorShowLineNumbers, true);
        Set(h, SettingsCatalog.EditorShowFoldMargin, true);
        Set(h, SettingsCatalog.EditorShowChangeBar, true);
        Assert.Equal(nine, probe.GutterWidth);

        // (max(3, digits) + 1) × charWidth: 9 lines → 4 digits wide.
        var charWidth = (nine - GlyphWidth - FoldWidth - ChangeBarWidth) / 4;
        Assert.True(charWidth > 0);
        Assert.Equal(4 * charWidth, Margin<LineNumberMargin>(editor).Bounds.Width, 3);

        // Up to 999 lines nothing changes; the 1000th line adds one charWidth; removing it shrinks back.
        doc.Buffer.ApplyEdits([new Core.Text.TextEdit(doc.Buffer.Length, 0, "\r\n" + Lines(990))]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(999, doc.Buffer.LineCount);
        Assert.Equal(nine, probe.GutterWidth, 3);
        doc.Buffer.ApplyEdits([new Core.Text.TextEdit(doc.Buffer.Length, 0, "\r\n")]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(nine + charWidth, probe.GutterWidth, 3);
        Assert.Equal(5 * charWidth, Margin<LineNumberMargin>(editor).Bounds.Width, 3);
        doc.Buffer.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(nine, probe.GutterWidth, 3);
    }

    private sealed class FakeDecorations : IEditorDecorationProvider
    {
        public Dictionary<int, LineGlyph[]> Glyphs { get; } = [];

        public event EventHandler? Changed;

        public List<TextMarker> TextMarkers { get; } = [];

        public IReadOnlyList<LineGlyph> GetLineGlyphs(int line) => Glyphs.GetValueOrDefault(line, []);

        public IReadOnlyList<TextMarker> GetMarkers() => TextMarkers;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-062")]
    [Trait("Req", "UI-ED-063")]
    [Trait("Req", "UI-ED-064")]
    [Trait("Req", "UI-ED-065")]
    [Trait("Req", "UI-ED-066")]
    [Trait("Req", "UI-ED-067")]
    [Trait("Req", "UI-ED-068")]
    public async Task SquigglePerSeverityAndOverlap()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = "X := 'abc;\r\nY := 1;\r\n  Z;\r\n  W;\r\n  V;";
        });
        var (doc, editor, probe) = await OpenAsync(h, "A.pas");
        var textView = editor.TextArea.TextView;
        var renderer = textView.BackgroundRenderers.OfType<TextMarkerRenderer>().Single();
        Color error = Color.Parse("#B3261E"), warning = Color.Parse("#8A5A00"), hint = Color.Parse("#6B4FA0");
        List<(Color? Pen, Color? Fill, Rect Bounds)> Drawn()
        {
            textView.EnsureVisualLines();
            var group = new DrawingGroup();
            using (var context = group.Open())
                renderer.Draw(textView, context);
            return [.. group.Children.OfType<GeometryDrawing>().Select(d => ((d.Pen?.Brush as ISolidColorBrush)?.Color, (d.Brush as ISolidColorBrush)?.Color, d.Geometry!.Bounds))];
        }

        // UI-ED-068: the unterminated string (a lexer error token) has an error marker.
        Assert.Equal([(5, 5, "Error")], probe.Markers);
        Assert.Equal([error], Drawn().Select(d => d.Pen));

        // UI-ED-067: overlapping markers — each character shows only the most severe one; a zero-length marker at the
        // end of line 2 is one character wide (UI-ED-066).
        var other = new FakeDecorations();
        other.TextMarkers.AddRange([new TextMarker(0, 8, MarkerSeverity.Warning), new TextMarker(12, 4, MarkerSeverity.Hint), new TextMarker(19, 0, MarkerSeverity.Warning)]);
        doc.Decorations.Add(other);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([(0, 5, "Warning"), (5, 5, "Error"), (12, 4, "Hint"), (19, 1, "Warning")], probe.Markers);
        var drawn = Drawn();
        Assert.Equal([warning, error, warning], drawn.Where(d => d.Pen is not null).Select(d => d.Pen));
        Assert.Equal(3, drawn.Count(d => d.Fill == hint)); // UI-ED-065: three dots
        var squiggles = drawn.Where(d => d.Pen is not null).ToList();
        Assert.Equal(3, squiggles[1].Bounds.Height, 3);     // error: peak-to-peak 3
        Assert.Equal(2, squiggles[0].Bounds.Height, 3);     // warning: peak-to-peak 2
        Assert.True(squiggles[2].Bounds.Width >= textView.WideSpaceWidth - 4, "zero-length marker drawn one character wide");

        // Fixing the string removes the lexer marker; the other markers stay.
        doc.Buffer.ApplyEdits([new Core.Text.TextEdit(9, 0, "'")]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([(0, 8, "Warning"), (12, 4, "Hint"), (19, 1, "Warning")], probe.Markers);

        // UI-ED-066: a marker over five lines underlines only the first three, later lines from their first non-blank.
        other.TextMarkers.Clear();
        other.TextMarkers.Add(new TextMarker(0, doc.Buffer.Length, MarkerSeverity.Warning));
        other.Raise();
        Dispatcher.UIThread.RunJobs();
        var lines = Drawn();
        Assert.Equal(3, lines.Count);
        Assert.True(lines[2].Bounds.Left > lines[0].Bounds.Left + textView.WideSpaceWidth, "line 3 starts at its first non-blank");
        Assert.True(lines.Select(l => l.Bounds.Top).Distinct().Count() == 3);

        // UI-ED-063…065 geometry: vertices every 2 px from the left edge snapped to 4 px; hint dots at a 3 px pitch.
        Assert.Equal([new Point(4, 10), new Point(6, 13), new Point(8, 10), new Point(10, 13), new Point(12, 10), new Point(14, 13)],
            MarkerGeometry.Squiggle(5, 13, 10, MarkerGeometry.ErrorPeakToPeak));
        Assert.Equal([new Point(0, 0), new Point(2, 2), new Point(4, 0)], MarkerGeometry.Squiggle(0, 4, 0, MarkerGeometry.WarningPeakToPeak));
        Assert.Equal([new Point(10.75, 5.75), new Point(13.75, 5.75), new Point(16.75, 5.75)], MarkerGeometry.HintDots(10, 5));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-131")]
    public async Task ErrorMarkerToolTip()
    {
        const string text = "X := 'abc;\r\nY := 1;\r\n";
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = text);
        var (doc, editor, _) = await OpenAsync(h, "A.pas");
        var textView = editor.TextArea.TextView;
        Control? TipAt(int line, int column)
        {
            textView.EnsureVisualLines();
            var position = textView.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.LineMiddle) - textView.ScrollOffset;
            h.Window.MouseMove(textView.TranslatePoint(position + new Point(textView.WideSpaceWidth / 2, 0), h.Window)!.Value);
            Dispatcher.UIThread.RunJobs();
            return ToolTip.GetTip(textView) as Control;
        }

        static List<(string? Icon, string? Code, string? Message)> Rows(Control tip)
        {
            Assert.Equal("Editor.ErrorTip", AutomationIds.Get(tip));
            return [.. Assert.IsType<StackPanel>(tip).Children.Cast<StackPanel>().Select(row => (
                row.Children.OfType<Icons.IdeIcon>().Single().IconName,
                row.Children.OfType<TextBlock>().SingleOrDefault(t => t.Classes.Contains("codeSmall"))?.Text,
                row.Children.OfType<TextBlock>().SingleOrDefault(t => t.Classes.Contains("body"))?.Text))];
        }

        // The lexer-error marker of the unterminated string carries the lexer's code and message.
        var lexical = Assert.Single(FreexComp.Compiler.Syntax.Lexer.GetDiagnostics(FreexComp.Compiler.SourceText.From(@"C:\A.pas", text)),
            d => d.Severity == FreexComp.Compiler.DiagnosticSeverity.Error);
        var tip = Assert.IsAssignableFrom<Control>(TipAt(1, 8)); // the "b" of 'abc
        Assert.Equal([("error", lexical.Code, lexical.Message)], Rows(tip));
        Assert.Null(TipAt(2, 1)); // no marker on "Y"

        // Several markers at the position: one row each, most severe first, at most 5 (the hint is the sixth).
        var other = new FakeDecorations();
        other.TextMarkers.AddRange([
            new TextMarker(0, 10, MarkerSeverity.Hint, "H1", "hint one"),
            new TextMarker(5, 5, MarkerSeverity.Warning, "W1", "warning one"),
            new TextMarker(7, 0, MarkerSeverity.Warning, "W2", "warning two"), // zero length: covers one character
            new TextMarker(2, 6, MarkerSeverity.Warning, "W3", "warning three"),
            new TextMarker(7, 2, MarkerSeverity.Warning, "W4", "warning four"),
        ]);
        doc.Decorations.Add(other);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([("error", lexical.Code, lexical.Message), ("warning", "W1", "warning one"), ("warning", "W2", "warning two"),
            ("warning", "W3", "warning three"), ("warning", "W4", "warning four")], Rows(Assert.IsAssignableFrom<Control>(TipAt(1, 8))));
        Assert.Equal([("hint", "H1", "hint one")], Rows(Assert.IsAssignableFrom<Control>(TipAt(1, 1))));

        // Fixing the string removes the lexer marker and its row.
        doc.Buffer.ApplyEdits([new Core.Text.TextEdit(9, 0, "'")]);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Rows(Assert.IsAssignableFrom<Control>(TipAt(1, 8))), r => r.Icon == "error");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-020")]
    [Trait("Req", "UI-ED-004")]
    public async Task GlyphPriority()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = Lines(8);
        });
        var (doc, editor, probe) = await OpenAsync(h, "A.pas");
        var glyphMargin = Margin<GlyphMargin>(editor);
        Assert.Null(probe.GlyphAt(1));
        Assert.DoesNotContain(Shapes(glyphMargin), s => s.Fill != Color.Parse("#F6F7F5")); // only the gutter background

        var bookmarks = new FakeDecorations();
        var debugger = new FakeDecorations();
        doc.Decorations.Add(bookmarks);
        doc.Decorations.Add(debugger);
        bookmarks.Glyphs[2] = [new LineGlyph(LineGlyphKind.Bookmark, 3)];
        bookmarks.Glyphs[3] = [new LineGlyph(LineGlyphKind.Bookmark)];
        bookmarks.Glyphs[4] = [new LineGlyph(LineGlyphKind.Error)];
        debugger.Glyphs[3] = [new LineGlyph(LineGlyphKind.Breakpoint)];
        debugger.Glyphs[4] = [new LineGlyph(LineGlyphKind.Frame), new LineGlyph(LineGlyphKind.Execution)];
        debugger.Glyphs[5] = [new LineGlyph(LineGlyphKind.BreakpointDisabled)];
        debugger.Glyphs[6] = [new LineGlyph(LineGlyphKind.Frame), new LineGlyph(LineGlyphKind.Breakpoint)];
        bookmarks.Raise();
        Dispatcher.UIThread.RunJobs();

        // §3: only the highest-priority glyph per line.
        Assert.Equal("Bookmark3", probe.GlyphAt(2));
        Assert.Equal("Breakpoint", probe.GlyphAt(3));
        Assert.Equal("Execution", probe.GlyphAt(4));
        Assert.Equal("BreakpointDisabled", probe.GlyphAt(5));
        Assert.Equal("Frame", probe.GlyphAt(6));
        Assert.Null(probe.GlyphAt(7));

        // UI-ED-020: line backgrounds, execution > call frame > breakpoint; disabled breakpoints have none.
        Assert.Null(probe.LineBackgroundAt(2));
        Assert.Equal("editor.breakpoint.line.bg", probe.LineBackgroundAt(3));
        Assert.Equal("editor.execution.line.bg", probe.LineBackgroundAt(4));
        Assert.Null(probe.LineBackgroundAt(5));
        Assert.Equal("editor.callFrame.line.bg", probe.LineBackgroundAt(6));

        // The margin draws them: bookmark, breakpoint disc, execution arrow (frame hidden), ring, frame outline.
        var fills = Shapes(glyphMargin).Select(s => s.Fill).ToList();
        Assert.Contains(Color.Parse("#1E5FA8"), fills); // editor.bookmark
        Assert.Contains(Color.Parse("#C23A31"), fills); // editor.breakpoint
        Assert.Contains(Color.Parse("#C28A00"), fills); // editor.execution

        // A provider change redraws.
        debugger.Glyphs.Remove(4);
        debugger.Raise();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Error", probe.GlyphAt(4));
        Assert.Null(probe.LineBackgroundAt(4));
        Assert.DoesNotContain(Color.Parse("#C28A00"), Shapes(glyphMargin).Select(s => s.Fill));

        // The caret line's highlight gives way to a line-state background (UI-ED-020).
        editor.TextArea.Caret.Line = 3;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("editor.breakpoint.line.bg", probe.LineBackgroundAt(3));
        doc.Decorations.Remove(debugger);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Bookmark", probe.GlyphAt(3));
        Assert.Equal("editor.currentLine.bg", probe.LineBackgroundAt(3));
    }

    private sealed class FakeLineChanges : ILineChangeSource
    {
        public Dictionary<int, LineChangeState> States { get; } = [];

        public event EventHandler? Changed;

        public LineChangeState StateOf(int line) => States.GetValueOrDefault(line);

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-040")]
    public async Task ChangeBarStates()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = Lines(5);
        });
        var (doc, editor, probe) = await OpenAsync(h, "A.pas");
        var bar = Margin<ChangeBarMargin>(editor);
        Color modified = Color.Parse("#C28A00"), saved = Color.Parse("#2E7D32");

        // Until the line-change tracker (I-046) is plugged in, no line has a mark.
        Assert.All(Enumerable.Range(1, 5), line => Assert.Equal("None", probe.ChangeBarAt(line)));
        Assert.DoesNotContain(Shapes(bar), s => s.Fill == modified || s.Fill == saved);

        var changes = new FakeLineChanges();
        changes.States[2] = LineChangeState.Modified;
        changes.States[3] = LineChangeState.Modified;
        changes.States[5] = LineChangeState.Saved;
        doc.LineChanges = changes;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["None", "Modified", "Modified", "None", "Saved"], Enumerable.Range(1, 5).Select(probe.ChangeBarAt));
        // UI-ED-040: unsaved = 3 px wide, saved = 2 px left-aligned; full line height, no gaps between adjacent lines.
        var marks = Shapes(bar).Where(s => s.Fill == modified || s.Fill == saved).OrderBy(s => s.Bounds.Y).ToList();
        Assert.Equal(3, marks.Count);
        Assert.All(marks.Take(2), m => Assert.Equal((modified, 0.0, 3.0), (m.Fill!.Value, m.Bounds.X, m.Bounds.Width)));
        Assert.Equal(marks[0].Bounds.Bottom, marks[1].Bounds.Top, 3);
        Assert.Equal(editor.TextArea.TextView.DefaultLineHeight, marks[0].Bounds.Height, 3);
        Assert.Equal((saved, 0.0, 2.0), (marks[2].Fill!.Value, marks[2].Bounds.X, marks[2].Bounds.Width));

        // A source change redraws.
        changes.States[5] = LineChangeState.None;
        changes.Raise();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("None", probe.ChangeBarAt(5));
        Assert.DoesNotContain(Shapes(bar), s => s.Fill == saved);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-050")]
    public async Task CurrentLineHiddenWithMultiLineSelection()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = Lines(4));
        var (doc, editor, probe) = await OpenAsync(h, "A.pas");
        var area = editor.TextArea;
        Assert.False(editor.Options.HighlightCurrentLine); // ours replaces the built-in one
        Assert.Contains(area.TextView.BackgroundRenderers, r => r is CurrentLineRenderer);

        area.Caret.Line = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([null, "editor.currentLine.bg", null, null], Enumerable.Range(1, 4).Select(probe.LineBackgroundAt));

        // A selection inside the line keeps the highlight; one spanning two lines hides it.
        area.Selection = Selection.Create(area, doc.Buffer.GetOffset(2, 1), doc.Buffer.GetOffset(2, 3));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("editor.currentLine.bg", probe.LineBackgroundAt(2));
        area.Selection = Selection.Create(area, doc.Buffer.GetOffset(2, 1), doc.Buffer.GetOffset(3, 2));
        area.Caret.Offset = doc.Buffer.GetOffset(3, 2);
        Dispatcher.UIThread.RunJobs();
        Assert.All(Enumerable.Range(1, 4), line => Assert.Null(probe.LineBackgroundAt(line)));
        area.ClearSelection();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("editor.currentLine.bg", probe.LineBackgroundAt(3));

        // The focus border is drawn only while the editor has keyboard focus.
        var renderer = area.TextView.BackgroundRenderers.OfType<CurrentLineRenderer>().Single();
        Assert.True(renderer.DrawsBorder); // opening the document focused its editor
        Assert.True(h.Find<Avalonia.Controls.Control>("DocumentArea").Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.False(renderer.DrawsBorder);
        Assert.Equal("editor.currentLine.bg", probe.LineBackgroundAt(3)); // the background stays without focus
        Assert.True(area.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.True(renderer.DrawsBorder);

        Set(h, SettingsCatalog.EditorHighlightCurrentLine, false);
        Assert.Null(probe.LineBackgroundAt(3));
    }

    /// <summary>What a background renderer draws: (fill colour, outline colour, bounds) per geometry.</summary>
    private static List<(Color? Fill, Color? Pen, Rect Bounds)> Drawn(TextView textView, IBackgroundRenderer renderer)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
            renderer.Draw(textView, context);
        return [.. group.Children.OfType<GeometryDrawing>()
            .Select(d => ((d.Brush as ISolidColorBrush)?.Color, (d.Pen?.Brush as ISolidColorBrush)?.Color, d.Geometry?.Bounds ?? default))];
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-053")]
    [Trait("Req", "IDE-ED-070")]
    public async Task BracketMatchPrefersBracketBeforeCaret()
    {
        const string text = "F(a)(b);\r\nbegin\r\n  X := '(' + a);\r\nend;\r\n";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = text;
        });
        var (_, editor, probe) = await OpenAsync(h, "A.pas");
        var area = editor.TextArea;
        var renderer = area.TextView.BackgroundRenderers.OfType<BracketMatchRenderer>().Single();
        Assert.Equal(KnownLayer.Selection, renderer.Layer);
        void CaretAt(int offset)
        {
            area.Caret.Offset = offset;
            Dispatcher.UIThread.RunJobs();
        }

        // Between ")" and "(": the bracket before the caret wins.
        CaretAt(4);
        Assert.Equal([(1, 1), (3, 1)], probe.BracketHighlights);
        var bg = Color.Parse("#E3F2EF");
        var border = Color.Parse("#0F7B6C");
        var drawn = Drawn(area.TextView, renderer);
        Assert.Equal(2, drawn.Count(d => d.Fill == bg));
        Assert.Equal(2, drawn.Count(d => d.Pen == border));
        CaretAt(5);
        Assert.Equal([(4, 1), (6, 1)], probe.BracketHighlights);

        // Inside a string nothing pairs; a keyword pairs with its end; a bracket without partner is outlined in red.
        var quote = text.IndexOf("'('", StringComparison.Ordinal);
        CaretAt(quote + 1);
        Assert.Empty(probe.BracketHighlights);
        CaretAt(text.IndexOf("begin", StringComparison.Ordinal) + 2);
        Assert.Equal([(10, 5), (text.IndexOf("end;", StringComparison.Ordinal), 3)], probe.BracketHighlights);
        CaretAt(text.IndexOf("a);", StringComparison.Ordinal) + 2);
        Assert.Equal([(text.IndexOf("a);", StringComparison.Ordinal) + 1, 1)], probe.BracketHighlights);
        drawn = Drawn(area.TextView, renderer);
        Assert.DoesNotContain(drawn, d => d.Fill == bg);
        Assert.Contains(drawn, d => d.Pen == Color.Parse("#B3261E")); // editor.squiggle.error

        // editor.highlightMatchingBrackets turns it off.
        CaretAt(4);
        Assert.NotEmpty(probe.BracketHighlights);
        Set(h, SettingsCatalog.EditorHighlightMatchingBrackets, false);
        Assert.Empty(probe.BracketHighlights);
        Assert.Empty(Drawn(area.TextView, renderer));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-056")]
    [Trait("Req", "IDE-ED-073")]
    public async Task IndentGuidesFollowCaretBlock()
    {
        const string text = "begin\r\n  begin\r\n    A;\r\n  end;\r\nend;\r\n";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light"; // editor.tabWidth = 2 (default)
            o.InitialFiles["A.pas"] = text;
        });
        var (doc, editor, _) = await OpenAsync(h, "A.pas");
        var area = editor.TextArea;
        var renderer = area.TextView.BackgroundRenderers.OfType<IndentGuideRenderer>().Single();
        Assert.Equal(KnownLayer.Background, renderer.Layer);
        var guide = Color.Parse("#E4E8E3");
        var active = Color.Parse("#B4BAB3");

        // Lines 2–4 have a guide at column 0, line 3 one at column 2 too; the caret in "A;" makes column 2 active.
        area.Caret.Offset = doc.Buffer.GetOffset(3, 6);
        Dispatcher.UIThread.RunJobs();
        var drawn = Drawn(area.TextView, renderer);
        Assert.Equal(3, drawn.Count(d => d.Pen == guide));
        Assert.Equal(1, drawn.Count(d => d.Pen == active));
        var x = area.TextView.GetVisualPosition(new TextViewPosition(3, 3), VisualYPosition.LineTop).X - area.TextView.HorizontalOffset;
        Assert.Equal(Math.Round(x) + 0.5, drawn.Single(d => d.Pen == active).Bounds.X); // left edge of the column, pixel-snapped

        // On the outer "begin" the outer block is active (column 0 over lines 2–4).
        area.Caret.Offset = doc.Buffer.GetOffset(1, 3);
        Dispatcher.UIThread.RunJobs();
        drawn = Drawn(area.TextView, renderer);
        Assert.Equal(3, drawn.Count(d => d.Pen == active));

        Set(h, SettingsCatalog.EditorShowIndentGuides, false);
        Assert.Empty(Drawn(area.TextView, renderer));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-003")]
    [Trait("Req", "UI-ED-011")]
    public async Task ThemeSwitchRecolors()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.Settings["editor.showWhitespace"] = true;
            o.InitialFiles["A.pas"] = Lines(3);
        });
        var (_, editor, _) = await OpenAsync(h, "A.pas");
        var numbers = Margin<LineNumberMargin>(editor);
        var textView = editor.TextArea.TextView;
        editor.TextArea.Caret.Line = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Color.Parse("#F6F7F5"), Shapes(numbers)[0].Fill); // bg.gutter
        Assert.Equal([Color.Parse("#737D76"), Color.Parse("#1C211E"), Color.Parse("#737D76")], TextColors(numbers)); // caret line: .current
        Assert.Equal(Color.Parse("#ECEEEB"), Assert.IsAssignableFrom<ISolidColorBrush>(textView.ColumnRulerPen.Brush).Color);
        Assert.Equal(Color.Parse("#C9CEC7"), Assert.IsAssignableFrom<ISolidColorBrush>(textView.NonPrintableCharacterBrush).Color);

        await h.ExecuteAsync("view.theme.dark");

        Assert.Same(numbers, Margin<LineNumberMargin>(editor));
        Assert.Equal(Color.Parse("#1B1F1D"), Shapes(numbers)[0].Fill);
        Assert.Equal([Color.Parse("#838E87"), Color.Parse("#E4E8E5"), Color.Parse("#838E87")], TextColors(numbers));
        Assert.Equal(Color.Parse("#262B29"), Assert.IsAssignableFrom<ISolidColorBrush>(textView.ColumnRulerPen.Brush).Color);
        Assert.Equal(Color.Parse("#3A423E"), Assert.IsAssignableFrom<ISolidColorBrush>(textView.NonPrintableCharacterBrush).Color);
        Assert.Equal(Color.Parse("#1B1F1D"), Shapes(Margin<ChangeBarMargin>(editor))[0].Fill);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-057")]
    [Trait("Req", "UI-ED-058")]
    public async Task RulerAndWhitespaceFollowSettings()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "a b\tc\r\n");
        var (_, editor, _) = await OpenAsync(h, "A.pas");
        var options = editor.Options;

        Assert.True(options.ShowColumnRulers);
        Assert.Equal([120], options.ColumnRulerPositions);
        Assert.False(options.ShowSpaces);
        Assert.False(options.ShowTabs);
        Assert.False(options.ShowEndOfLine);

        Set(h, SettingsCatalog.EditorRightMargin, 80);
        Assert.Equal([80], options.ColumnRulerPositions);
        Set(h, SettingsCatalog.EditorRightMargin, 0);
        Assert.False(options.ShowColumnRulers); // 0 hides the ruler

        Set(h, SettingsCatalog.EditorShowWhitespace, true);
        Assert.True(options.ShowSpaces);
        Assert.True(options.ShowTabs);
        Assert.Equal("·", options.ShowSpacesGlyph);
        Assert.Equal("→", options.ShowTabsGlyph);
        Assert.False(options.ShowEndOfLine);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-012")]
    public async Task LineNumberClickSelectsLines()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = Lines(4));
        var (doc, editor, _) = await OpenAsync(h, "A.pas");
        var numbers = Margin<LineNumberMargin>(editor);
        var lineHeight = editor.TextArea.TextView.DefaultLineHeight;
        Point LineCenter(int line) => numbers.TranslatePoint(new Point(numbers.Bounds.Width / 2, (line - 0.5) * lineHeight), h.Window)!.Value;

        // Click selects the whole line including its break.
        h.Window.MouseDown(LineCenter(2), Avalonia.Input.MouseButton.Left);
        h.Window.MouseUp(LineCenter(2), Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("x2\r\n", editor.TextArea.Selection.GetText());

        // Shift+click extends to the clicked line.
        h.Window.MouseDown(LineCenter(3), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.Shift);
        h.Window.MouseUp(LineCenter(3), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("x2\r\nx3\r\n", editor.TextArea.Selection.GetText());

        // Dragging selects a range of lines.
        h.Window.MouseDown(LineCenter(1), Avalonia.Input.MouseButton.Left);
        h.Window.MouseMove(LineCenter(3), Avalonia.Input.RawInputModifiers.LeftMouseButton);
        h.Window.MouseUp(LineCenter(3), Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("x1\r\nx2\r\nx3\r\n", editor.TextArea.Selection.GetText());
        Assert.Equal(new Core.Text.TextPosition(4, 1), doc.ViewState.Caret);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-021")]
    [Trait("Req", "UI-ED-022")]
    public async Task GlyphContextMenuAndTooltip()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = Lines(8));
        var (doc, editor, _) = await OpenAsync(h, "A.pas");
        var gutter = Margin<GlyphMargin>(editor);
        var lineHeight = editor.TextArea.TextView.DefaultLineHeight;
        Point LineCenter(Control margin, int line) => margin.TranslatePoint(new Point(margin.Bounds.Width / 2, (line - 0.5) * lineHeight), h.Window)!.Value;
        var glyphs = new FakeDecorations();
        doc.Decorations.Add(glyphs);
        glyphs.Glyphs[2] = [new LineGlyph(LineGlyphKind.BreakpointConditionalDisabled, Condition: "I > 3", PassCount: 2)];
        glyphs.Glyphs[3] = [new LineGlyph(LineGlyphKind.Bookmark, 4), new LineGlyph(LineGlyphKind.Execution), new LineGlyph(LineGlyphKind.Breakpoint)];
        glyphs.Glyphs[4] = [new LineGlyph(LineGlyphKind.BreakpointUnverified), new LineGlyph(LineGlyphKind.Bookmark)];
        glyphs.Glyphs[5] = [new LineGlyph(LineGlyphKind.Error, ToolTip: "E2003 Undeclared identifier: 'Foo'")];
        glyphs.Raise();
        Dispatcher.UIThread.RunJobs();

        // UI-ED-021: tooltips from the Msg.Editor.Tip texts, one line per glyph, highest priority first.
        string? TipAt(int line)
        {
            h.Window.MouseMove(LineCenter(gutter, line));
            Dispatcher.UIThread.RunJobs();
            return ToolTip.GetTip(gutter) as string;
        }

        var nl = Environment.NewLine;
        Assert.Equal("Breakpoint at line 2, condition: I > 3, pass count: 2, disabled", TipAt(2));
        Assert.Equal("Execution point" + nl + "Breakpoint at line 3" + nl + "Bookmark 4", TipAt(3));
        Assert.Equal("The breakpoint is not on a line that produces code." + nl + "Bookmark", TipAt(4));
        Assert.Equal("E2003 Undeclared identifier: 'Foo'", TipAt(5));
        Assert.Null(TipAt(6));

        ContextMenu RightClick(Control margin, int line)
        {
            h.Window.MouseDown(LineCenter(margin, line), Avalonia.Input.MouseButton.Right);
            h.Window.MouseUp(LineCenter(margin, line), Avalonia.Input.MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            var menu = Assert.IsType<ContextMenu>(margin.ContextMenu);
            Assert.True(menu.IsOpen);
            return menu;
        }

        static List<string?> Ids(ContextMenu menu) => [.. menu.Items.Cast<Control>().Select(c => c is Separator ? null : AutomationIds.Get(c))];
        static string Header(ContextMenu menu, int index) => (string)((MenuItem)menu.Items[index]!).Header!;

        // UI-ED-022: on a breakpoint glyph — enable/disable, properties, —, Delete Breakpoint (run.toggleBreakpoint);
        // the caret moves to the clicked line, so the commands act on it.
        var menu = RightClick(gutter, 3);
        Assert.Equal(["Context.run.toggleBreakpointEnabled", "Context.run.breakpointProperties", null, "Context.run.toggleBreakpoint"], Ids(menu));
        Assert.Equal("_Delete Breakpoint", Header(menu, 3));
        Assert.Equal(3, doc.ViewState.Caret.Line);
        menu.Close();
        Dispatcher.UIThread.RunJobs();

        // Elsewhere in the gutter (also over the line numbers): toggle breakpoint, toggle bookmark, —, three setting toggles.
        menu = RightClick(Margin<LineNumberMargin>(editor), 6);
        Assert.Equal(["Context.run.toggleBreakpoint", "Context.search.bookmark.toggle", null,
            "Context." + Core.Menus.GutterMenu.LineNumbersAction, "Context." + Core.Menus.GutterMenu.FoldingMarkersAction,
            "Context." + Core.Menus.GutterMenu.ChangeBarAction], Ids(menu));
        Assert.Equal(6, doc.ViewState.Caret.Line);
        var lineNumbers = (MenuItem)menu.Items[3]!;
        Assert.True(lineNumbers.IsChecked);
        Assert.Equal(MenuItemToggleType.CheckBox, lineNumbers.ToggleType);
        lineNumbers.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Services.Settings.Get(SettingsCatalog.EditorShowLineNumbers));
        Assert.False(Margin<LineNumberMargin>(editor).IsVisible);
        menu.Close();
        Dispatcher.UIThread.RunJobs();

        menu = RightClick(gutter, 7);
        Assert.False(((MenuItem)menu.Items[3]!).IsChecked);
        Assert.True(((MenuItem)menu.Items[4]!).IsChecked);
        menu.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-052")]
    public async Task OverwriteCaretHalfOpaque()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = Lines(3);
        });
        var (doc, editor, _) = await OpenAsync(h, "A.pas");
        var area = editor.TextArea;
        area.Focus();
        Dispatcher.UIThread.RunJobs();
        Color light = Color.Parse("#1C211E"), dark = Color.Parse("#E4E8E5"); // editor.caret
        ISolidColorBrush Caret() => Assert.IsAssignableFrom<ISolidColorBrush>(area.CaretBrush);

        // Insert mode: the opaque editor.caret bar.
        Assert.Equal((light, 1.0), (Caret().Color, Caret().Opacity));

        // Overwrite mode: a block of editor.caret at 50 % opacity; also after a theme switch.
        h.Press("Insert");
        Assert.True(area.OverstrikeMode);
        Assert.Equal((light, 0.5), (Caret().Color, Caret().Opacity));
        h.Services.Settings.Set(SettingsCatalog.AppearanceTheme, "dark");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((dark, 0.5), (Caret().Color, Caret().Opacity));

        // Back to insert mode: opaque again.
        h.Press("Insert");
        Assert.False(area.OverstrikeMode);
        Assert.Equal((dark, 1.0), (Caret().Color, Caret().Opacity));
        Assert.False(doc.ViewState.Overwrite);
    }
}