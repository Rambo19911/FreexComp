using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Word-occurrence highlight (I-053, IDE-ED-074, UI-ED-054).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class WordHighlightTests
{
    private static readonly TimeSpan Rest = TimeSpan.FromMilliseconds(250);

    private const string Code =
        "var\r\n" +
        "  Count: Integer;\r\n" +
        "begin\r\n" +
        "  count := 1; // Count\r\n" +
        "  S := 'Count';\r\n" +
        "  Inc(COUNT);\r\n" +
        "end;\r\n";

    private static int At(string text, string what, int skip = 0)
    {
        int offset = -1;
        for (int i = 0; i <= skip; i++)
            offset = text.IndexOf(what, offset + 1, StringComparison.Ordinal);
        return offset;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-074")]
    [Trait("Req", "UI-ED-054")]
    public async Task HighlightsIdentifierOccurrences()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = Code;
        });
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.A.pas");
        IEditorTestProbe probe = editor.FindAncestorOfType<CodeEditorView>()!;
        var area = editor.TextArea;
        var renderer = area.TextView.BackgroundRenderers.OfType<OccurrenceRenderer>().Single();
        Assert.Equal(KnownLayer.Selection, renderer.Layer);
        void CaretAt(int offset)
        {
            area.ClearSelection();
            area.Caret.Offset = offset;
            Dispatcher.UIThread.RunJobs();
        }

        (int, int)[] others = [(At(Code, "count"), 5), (At(Code, "COUNT"), 5)];

        // 250 ms after the caret rests on "Count": the other identifiers with that text, case-insensitively; not the one
        // at the caret, not the comment, not the string.
        CaretAt(At(Code, "Count") + 2);
        Assert.Empty(probe.OccurrenceHighlights);
        h.Clock.Advance(Rest - TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.OccurrenceHighlights);
        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(others, probe.OccurrenceHighlights);
        var group = new DrawingGroup();
        using (var context = group.Open())
            renderer.Draw(area.TextView, context);
        Assert.Equal(2, group.Children.OfType<GeometryDrawing>().Count(d => (d.Brush as ISolidColorBrush)?.Color == Color.Parse("#EDEFEA")));

        // Moving the caret clears them at once; at the end of a word it still counts as on the word.
        CaretAt(At(Code, "COUNT") + 5);
        Assert.Empty(probe.OccurrenceHighlights);
        h.Clock.Advance(Rest);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([(At(Code, "Count"), 5), (At(Code, "count"), 5)], probe.OccurrenceHighlights);

        // A keyword, a comment or a string at the caret: nothing.
        foreach (var offset in new[] { At(Code, "begin") + 1, At(Code, "Count", 1) + 1, At(Code, "Count", 2) + 1 })
        {
            CaretAt(offset);
            h.Clock.Advance(Rest);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(probe.OccurrenceHighlights);
        }

        // A selection equal to one identifier highlights; any other selection does not.
        area.Selection = Selection.Create(area, At(Code, "Count"), At(Code, "Count") + 5);
        area.Caret.Offset = At(Code, "Count") + 5;
        h.Clock.Advance(Rest);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(others, probe.OccurrenceHighlights);
        area.Selection = Selection.Create(area, At(Code, "Count") + 1, At(Code, "Count") + 5);
        h.Clock.Advance(Rest);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.OccurrenceHighlights);

        // Editing clears them until the caret rests again; the setting turns them off.
        CaretAt(At(Code, "Count") + 2);
        h.Clock.Advance(Rest);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(probe.OccurrenceHighlights);
        Assert.True(area.Focus());
        h.Type("x"); // "Coxunt"
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.OccurrenceHighlights);
        h.Clock.Advance(Rest);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.OccurrenceHighlights); // no other "Coxunt"
        CaretAt(At(editor.Text, "COUNT") + 1);
        h.Clock.Advance(Rest);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([(At(editor.Text, "count"), 5)], probe.OccurrenceHighlights);
        h.Services.Settings.Set(SettingsCatalog.EditorHighlightWordOccurrences, false);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.OccurrenceHighlights);
        h.Clock.Advance(Rest);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.OccurrenceHighlights);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-074")]
    public void KeywordsAndContextualKeywordsAreNotIdentifiers()
    {
        const string code = "property P: Integer read FRead write FRead;\r\nX := Read + read;";
        var tokens = new PascalLanguage().ScanCodeTokens(code);
        IEnumerable<int> Occurrences(int caret) => tokens.Occurrences(caret, TextSelection.At(caret)).Select(o => o.Offset);
        Assert.Equal([At(code, "FRead", 1)], Occurrences(At(code, "FRead") + 1));
        Assert.Empty(Occurrences(At(code, "read") + 1)); // the property specifier is a contextual keyword
        Assert.Equal([code.LastIndexOf("read", StringComparison.Ordinal)], Occurrences(At(code, "Read +") + 1)); // identifiers here
        Assert.Empty(Occurrences(1)); // "property"
    }
}
