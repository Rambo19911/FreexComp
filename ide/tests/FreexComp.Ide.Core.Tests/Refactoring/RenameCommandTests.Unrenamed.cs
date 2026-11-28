using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// IDE-REFAC-061 (I-385): after an applied rename the occurrences the language service did not rename — inactive
/// conditional code, names that do not resolve — are warning rows in Messages at their place in the renamed text; the
/// status says so; Messages is neither opened nor focused; a canceled or blocked rename adds nothing.
/// </summary>
public sealed partial class RenameCommandTests
{
    /// <summary>C with an unresolved <c>Old</c> behind a renamed one on the same line and an <c>Old</c> in inactive code.</summary>
    private const string UnitCUnrenamed = "unit C;\n\ninterface\n\nuses A, Lib;\n\nprocedure Use(F: TMainForm);\n\nimplementation\n\n"
        + "procedure Use(F: TMainForm);\nvar\n  Count: Integer;\nbegin\n  Count := 1;\n  F.Old(nil); Old;\n"
        + "  {$IFDEF NEVER_DEFINED} F.Old(nil); {$ENDIF}\n  LibProc;\nend;\n\nend.\n";

    /// <summary>The tool rows of Messages (every row that is not the summary).</summary>
    private List<MessageItem> ToolRows() => [.. _services.Messages.Items.Where(i => !i.IsSummary)];

    /// <summary>Which tool windows are shown and active, group by group (Messages is neither opened nor activated).</summary>
    private string LayoutState() => string.Join(" | ", _services.Layout.Layout.Groups.Select(g => string.Join(",", g.Tools) + ">" + g.ActiveTool));

    /// <summary>The 1-based line and column of <paramref name="needle"/>'s character <paramref name="at"/> in <paramref name="text"/>.</summary>
    internal static (int Line, int Column) LineColumn(string text, string needle, int at = 0)
    {
        var offset = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(offset >= 0, needle);
        offset += at;
        var lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        return (text[..offset].Count(c => c == '\n') + 1, offset - lineStart + 1);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-061")]
    public Task UnrenamedOccurrencesBecomeMessagesRows() => TestUiThread.RunAsync(async () =>
    {
        await OpenRenameProjectAsync(UnitCUnrenamed);
        PreviewApplies();
        var b = await OpenAsync(B);
        Caret(b, "Old");
        var layout = LayoutState();
        var focused = 0;
        _services.Layout.Layout.PaneFocusRequested += (_, _) => focused++;

        var result = await Engine.RunAsync(Renamer());

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        var renamed = Text(C);
        Assert.Contains("  F.Renamed(nil); Old;\n", renamed, StringComparison.Ordinal);
        var rows = ToolRows();
        Assert.Equal(2, rows.Count);
        // In the service's order: the unresolved name (its column shifted by the rename before it on its line), then the
        // inactive one; warnings with the names, at the place that spells the old name in the renamed text.
        var unresolved = LineColumn(renamed, "; Old;", 2);
        var inactive = LineColumn(renamed, "F.Old(nil); {$ENDIF}", 2);
        Assert.Equal(
            [
                (MessageSeverity.Warning, "Old could not be resolved here and was not renamed to Renamed", unresolved.Line, unresolved.Column, unresolved.Column + 3),
                (MessageSeverity.Warning, "Old in inactive code was not renamed to Renamed", inactive.Line, inactive.Column, inactive.Column + 3),
            ],
            rows.Select(r => (r.Severity, r.Text, r.Location!.Line, r.Location.Column, r.Location.EndColumn)));
        Assert.All(rows, r => Assert.Equal(C, r.Location!.FilePath, StringComparer.OrdinalIgnoreCase));
        Assert.All(rows, r => Assert.True(r.IsNavigable));
        Assert.Equal("Rename 'Old' to 'Renamed': occurrences that were not changed are listed in Messages (2)", _services.Status.Message);
        // Messages is neither opened nor focused.
        Assert.Equal(layout, LayoutState());
        Assert.Equal(0, focused);
        await SettleAsync();
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-061")]
    public Task CanceledPreviewAddsNoRow() => TestUiThread.RunAsync(async () =>
    {
        await OpenRenameProjectAsync(UnitCUnrenamed);
        Dialogs.Script(RefactoringPreviewViewModel.DialogName, dialog =>
        {
            _previews.Add(Assert.IsType<RefactoringPreviewViewModel>(dialog));
            return dialog.PressCancelAsync();
        });
        var b = await OpenAsync(B);
        Caret(b, "Old");

        Assert.Equal(RefactoringOutcome.Canceled, (await Engine.RunAsync(Renamer())).Outcome);
        Assert.Single(_previews);
        Assert.Empty(ToolRows());

        // Blocked (a read-only file the rename edits): no row either.
        _fs.SetReadOnly(C, true);
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        Assert.Equal(RefactoringOutcome.Blocked, (await Engine.RunAsync(Renamer())).Outcome);
        Assert.Empty(ToolRows());
        Assert.Equal(UnitCUnrenamed, Text(C));
        await SettleAsync();
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-061")]
    public Task RowsAreCappedWithAMoreRow() => TestUiThread.RunAsync(async () =>
    {
        // 201 occurrences in inactive code: 200 rows, then one row that says more were not listed (no location).
        var inactive = string.Concat(Enumerable.Repeat("  F.Old(nil);\n", RenameServiceLimit + 1));
        await OpenRenameProjectAsync(UnitC.Replace("  LibProc;\n", "{$IFDEF NEVER_DEFINED}\n" + inactive + "{$ENDIF}\n  LibProc;\n", StringComparison.Ordinal));
        PreviewApplies();
        var b = await OpenAsync(B);
        Caret(b, "Old");

        Assert.Equal(RefactoringOutcome.Applied, (await Engine.RunAsync(Renamer())).Outcome);

        var rows = ToolRows();
        Assert.Equal(RenameServiceLimit + 1, rows.Count);
        Assert.All(rows.Take(RenameServiceLimit), r => Assert.Equal("Old in inactive code was not renamed to Renamed", r.Text));
        Assert.Equal((MessageSeverity.Warning, "More occurrences were not renamed; only the first 200 are listed", (MessageLocation?)null),
            (rows[^1].Severity, rows[^1].Text, rows[^1].Location));
        Assert.Equal($"Rename 'Old' to 'Renamed': occurrences that were not changed are listed in Messages ({RenameServiceLimit})", _services.Status.Message);
        await SettleAsync();
    });

    /// <summary>The language service's limit of unrenamed occurrences (CMP-1510).</summary>
    private const int RenameServiceLimit = FreexComp.LanguageService.RenameService.MaxUnrenamed;
}
