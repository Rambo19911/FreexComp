using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Tests.CodeModel;

/// <summary>Code completion logic (I-114, editor-features.md §8) over the project <c>Compilation</c>.</summary>
[Trait("Phase", "3")]
public sealed class CompletionTests
{
    private const string Unit =
        "unit Unit1;\ninterface\nimplementation\ntype\n  TPoint = record\n    X, Y: Integer;\n  end;\nvar\n  StringList: Integer;\n  StrCount: Integer;\n" +
        "procedure Run;\nvar\n  P: TPoint;\nbegin\n  // comment\n  P := P;\nend;\nend.\n";

    private static async Task<(BuildTestBed Bed, TextDocumentViewModel Doc)> StartAsync()
    {
        var bed = await SemanticHighlightTests.StartAsync("");
        bed.File(BuildTestBed.UnitPath, Unit);
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        bed.Services.Documents.Activate(doc);
        return (bed, doc);
    }

    /// <summary>Puts the caret right after the first <paramref name="marker"/> (minus <paramref name="back"/> characters).</summary>
    private static void CaretAfter(TextDocumentViewModel doc, string marker, int back = 0) =>
        doc.Selection = TextSelection.At(doc.Buffer.Text.IndexOf(marker, StringComparison.Ordinal) + marker.Length - back);

    private static void Type(TextDocumentViewModel doc, string text)
    {
        foreach (var c in text)
            Assert.True(doc.TypeText(c.ToString()));
    }

    private static CompletionItem Item(string label, string sort, CompletionItemKind kind = CompletionItemKind.Variable) =>
        new(label, kind, label, sort, null, false, null);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-080")]
    public async Task TriggersAndSuppression()
    {
        // The pure rule (b)–(d).
        static string? NoClass(int _) => null;
        Assert.Equal(CompletionAutoTrigger.Immediate, CompletionRules.AutoTrigger("P.", 2, '.', NoClass));
        Assert.Equal(CompletionAutoTrigger.Immediate, CompletionRules.AutoTrigger("F().", 4, '.', NoClass));
        Assert.Equal(CompletionAutoTrigger.Immediate, CompletionRules.AutoTrigger("A[1].", 5, '.', NoClass));
        Assert.Equal(CompletionAutoTrigger.None, CompletionRules.AutoTrigger("1 .", 3, '.', NoClass));
        Assert.Equal(CompletionAutoTrigger.Delayed, CompletionRules.AutoTrigger("x := S", 6, 'S', NoClass));
        Assert.Equal(CompletionAutoTrigger.Delayed, CompletionRules.AutoTrigger("_", 1, '_', NoClass));
        Assert.Equal(CompletionAutoTrigger.None, CompletionRules.AutoTrigger("St", 2, 't', NoClass)); // not the first character
        Assert.Equal(CompletionAutoTrigger.None, CompletionRules.AutoTrigger("x1", 2, '1', NoClass));
        foreach (var typed in ":(,= ")
            Assert.Equal(CompletionAutoTrigger.None, CompletionRules.AutoTrigger("a" + typed, 2, typed, NoClass)); // (d)
        foreach (var inside in (string[])["syntax.comment", "syntax.string", "syntax.number"])
            Assert.Equal(CompletionAutoTrigger.None, CompletionRules.AutoTrigger("x S", 3, 'S', _ => inside));

        var (bed, doc) = await StartAsync();
        var session = Assert.IsType<CompletionSession>(doc.Completion);
        var delay = TimeSpan.FromMilliseconds(bed.Services.Settings.Get(SettingsCatalog.EditorCompletionDelayMs));

        // (b) '.' after an identifier: at once, the record's fields.
        CaretAfter(doc, "  P := P");
        Type(doc, ".");
        await session.Pending;
        Assert.True(session.IsOpen);
        Assert.Equal(["X", "Y"], session.Items.Select(i => i.Label));
        Assert.True(session.HandleKey("Escape"));
        Assert.False(session.IsOpen);

        // (c) the first identifier character: only after the delay.
        CaretAfter(doc, "  P := P.");
        Type(doc, " + S");
        Assert.False(session.IsOpen);
        bed.Clock.Advance(delay - TimeSpan.FromMilliseconds(1));
        await session.Pending;
        Assert.False(session.IsOpen);
        bed.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await session.Pending;
        Assert.True(session.IsOpen);
        Assert.Contains(session.Items, i => i.Label == "StringList");
        session.Close();

        // A non-identifier character before the delay ends the pending trigger.
        Type(doc, " + T ");
        bed.Clock.Advance(delay * 2);
        await session.Pending;
        Assert.False(session.IsOpen);

        // Never inside a comment, nor in overwrite mode, nor with the setting off.
        CaretAfter(doc, "// comment");
        Type(doc, " S");
        bed.Clock.Advance(delay * 2);
        await session.Pending;
        Assert.False(session.IsOpen);
        CaretAfter(doc, "  P := P");
        doc.ViewState.Overwrite = true;
        Type(doc, ".");
        await session.Pending;
        Assert.False(session.IsOpen);
        doc.ViewState.Overwrite = false;
        bed.Services.Settings.Set(SettingsCatalog.EditorCompletionAutoTrigger, false);
        CaretAfter(doc, "begin\n");
        Type(doc, "S");
        bed.Clock.Advance(delay * 2);
        await session.Pending;
        Assert.False(session.IsOpen);

        // (a) Ctrl+Space works with the setting off; inside a comment it does nothing.
        Assert.True(bed.Services.Commands.CanExecute(CompletionController.CommandId));
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Assert.True(session.IsOpen);
        session.Close();
        CaretAfter(doc, "// comm");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Assert.False(session.IsOpen);
    }

    /// <summary>UI-ED-115 (I-198): the characters of a label that the typed text matched, for the highlight.</summary>
    [Fact]
    [Trait("Req", "UI-ED-115")]
    public void MatchHighlightIndices()
    {
        Assert.Equal([0, 1], CompletionRules.MatchedIndices("Wr", "WriteLn"));
        Assert.Equal([0, 1, 2], CompletionRules.MatchedIndices("wri", "WriteLn")); // case-insensitive prefix
        Assert.Equal([0, 5], CompletionRules.MatchedIndices("WL", "WriteLn")); // word starts
        Assert.Equal([2, 4], CompletionRules.MatchedIndices("ab", "x_a_b")); // after "_"
        Assert.Empty(CompletionRules.MatchedIndices("", "WriteLn"));
        Assert.Empty(CompletionRules.MatchedIndices("xq", "WriteLn"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-081")]
    [Trait("Req", "IDE-ED-082")]
    [Trait("Req", "IDE-ED-083")]
    [Trait("Req", "IDE-ED-084")]
    [Trait("Req", "IDE-ED-085")]
    public async Task FilterRankCommit()
    {
        // IDE-ED-082 matching and IDE-ED-083 ranking.
        Assert.Equal(CompletionMatch.ExactCasePrefix, CompletionRules.Match("Str", "StringList"));
        Assert.Equal(CompletionMatch.Prefix, CompletionRules.Match("str", "StringList"));
        Assert.Equal(CompletionMatch.WordStarts, CompletionRules.Match("sl", "StringList"));
        Assert.Equal(CompletionMatch.WordStarts, CompletionRules.Match("fb", "foo_bar"));
        Assert.Equal(CompletionMatch.None, CompletionRules.Match("sr", "StringList"));
        Assert.Equal(CompletionMatch.None, CompletionRules.Match("x", "StringList"));
        var ranked = CompletionRules.FilterAndRank(
            [Item("strong", "0strong"), Item("StringList", "2stringlist"), Item("Str", "3str"), Item("SomeLocal", "0somelocal"),
             Item("string", "9", CompletionItemKind.Keyword), Item("Size", "0size")], "St");
        Assert.Equal(["StringList", "Str", "strong", "string"], ranked.Select(i => i.Label));
        Assert.Equal(["SomeLocal", "Size"], CompletionRules.FilterAndRank([Item("Size", "3size"), Item("SomeLocal", "0somelocal")], "s").Select(i => i.Label));

        var (bed, doc) = await StartAsync();
        var session = doc.Completion!;
        var statuses = new List<string?>();
        bed.Services.Status.Changed += (_, _) => statuses.Add(bed.Services.Status.Message);

        // Ctrl+Space after "Str": the popup filters as the user types; the best item is pre-selected.
        CaretAfter(doc, "  P := P;");
        Assert.True(doc.HandleKey("Enter"));
        Type(doc, "Str");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Assert.True(session.IsOpen);
        Assert.Equal(["StrCount", "StringList"], session.Items.Take(2).Select(i => i.Label));
        Assert.Equal("StrCount", session.SelectedItem!.Label);
        Type(doc, "i");
        Assert.Equal("StringList", session.SelectedItem!.Label);
        session.HandleKey("Down");
        Assert.True(session.SelectedIndex <= session.Items.Count - 1);

        // Backspace re-filters; deleting past the start closes.
        session.HandleKey("Up");
        Assert.True(doc.HandleKey("Backspace"));
        Assert.True(session.IsOpen);
        Assert.Equal("StrCount", session.SelectedItem!.Label);
        foreach (var _ in "Str")
            doc.HandleKey("Backspace");
        Assert.True(session.IsOpen); // at the start: the whole list
        doc.HandleKey("Backspace");
        Assert.False(session.IsOpen);

        // Enter commits: the identifier from its start to the caret is replaced; the text right of the caret stays.
        Type(doc, "  sl");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Assert.Equal("Slice", session.SelectedItem!.Label); // a prefix match ranks before the word-start match
        session.Select(session.Items.ToList().FindIndex(i => i.Label == "StringList"));
        Assert.True(doc.HandleKey("Enter"));
        Assert.False(session.IsOpen);
        Assert.Contains("\n  StringList\n", doc.Buffer.Text, StringComparison.Ordinal);

        // A commit character commits a prefix match, then is inserted.
        Type(doc, " := Str");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Type(doc, ";");
        Assert.False(session.IsOpen);
        Assert.Contains("  StringList := StrCount;", doc.Buffer.Text, StringComparison.Ordinal);

        // A typed character that empties the list closes the popup and is inserted.
        Type(doc, " Str");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Type(doc, "q");
        Assert.False(session.IsOpen);
        Assert.Contains("StrCount; Strq", doc.Buffer.Text, StringComparison.Ordinal);

        // Ctrl+Enter replaces the whole identifier; Escape inserts nothing.
        var at = doc.Buffer.Text.IndexOf("Strq", StringComparison.Ordinal);
        doc.Selection = TextSelection.At(at + 2);
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Assert.True(session.IsOpen);
        var before = doc.Buffer.Text;
        session.HandleKey("Escape");
        Assert.Equal(before, doc.Buffer.Text);
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        session.Select(session.Items.ToList().FindIndex(i => i.Label == "StrCount"));
        Assert.True(doc.HandleKey("Ctrl+Enter"));
        Assert.Contains("StrCount; StrCount\n", doc.Buffer.Text, StringComparison.Ordinal);

        // IDE-ED-081: a single candidate is inserted without the popup; an empty result shows the message.
        Type(doc, ";\n  StringL");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Assert.False(session.IsOpen);
        Assert.Contains("\n  StringList", doc.Buffer.Text[at..], StringComparison.Ordinal);
        Type(doc, ";\n  Qzx");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Assert.False(session.IsOpen);
        Assert.Equal(Strings.Msg_Editor_NoCompletions, statuses[^1]);

        // IDE-ED-085: a routine item inserts only its name.
        Type(doc, ";\n  Ru");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        if (session.IsOpen)
        {
            session.Select(session.Items.ToList().FindIndex(i => i.Label == "Run"));
            session.Commit(wholeIdentifier: false);
        }

        Assert.EndsWith("\n  Run\nend;\nend.\n", doc.Buffer.Text, StringComparison.Ordinal);

        // IDE-ED-063: at a statement position the code templates are listed (after the exact-prefix symbols); committing expands.
        Type(doc, ";\n  wh");
        await bed.Services.Commands.ExecuteAsync(CompletionController.CommandId);
        Assert.True(session.IsOpen);
        var template = Assert.Single(session.Items, i => i.Kind == CompletionItemKind.Snippet);
        Assert.Equal(("while", "while loop"), (template.Label, template.Detail));
        session.Select(session.Items.ToList().IndexOf(template));
        session.Commit(wholeIdentifier: false);
        Assert.EndsWith("\n  Run;\n  while  do\n  begin\n    \n  end;\nend;\nend.\n", doc.Buffer.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.True(doc.InTemplateFieldMode);
    }

}
