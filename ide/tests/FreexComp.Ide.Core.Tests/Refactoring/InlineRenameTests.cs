using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// The inline rename of a local (I-381, F13-001; IDE-REFAC-050…055): F2 on a local links its occurrences in the editor;
/// typing renames all of them as one undo step, the name is checked 200 ms after the last change, Enter / Tab commit after
/// the check and every way out other than a commit takes the session's edits back.
/// </summary>
[Trait("Phase", "8")]
public sealed class InlineRenameTests : RefactoringTestBase
{
    private const string R = Dir + @"\R.pas";

    private const string Project = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "R.pas" } ] }
        """;

    private const string Main = "program App;\n\nuses R;\n\nbegin\n  Go;\nend.\n";

    /// <summary>A routine with the local <c>Value</c> (five occurrences) and the local <c>Count</c>.</summary>
    private const string UnitR = "unit R;\n\ninterface\n\nprocedure Go;\n\nimplementation\n\nprocedure Go;\nvar\n  Value: Integer;\n  Count: Integer;\n"
        + "begin\n  Value := 1;\n  Count := Value + 1;\n  Value := Value + Count;\nend;\n\nend.\n";

    private readonly List<InlineRenameSession> _sessions = [];

    private async Task<TextDocumentViewModel> OpenRAsync(string text = UnitR)
    {
        WriteToolchain();
        File(ProjectPath, Project);
        File(Dir + @"\App.pas", Main);
        File(R, text);
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        await _services.ProjectManager.MissingFileChecks;
        var r = await OpenAsync(R);
        _services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, r, IsTextInput: false)); // the editor has the focus
        r.RenameSessionChanged += (_, _) =>
        {
            if (r.RenameSession is { } session && !_sessions.Contains(session))
                _sessions.Add(session);
        };
        return r;
    }

    /// <summary>The offset of the <paramref name="occurrence"/>-th <paramref name="text"/>.</summary>
    private static int At(string inText, string text, int occurrence = 1)
    {
        var at = -1;
        for (var i = 0; i < occurrence; i++)
            at = inText.IndexOf(text, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, text);
        return at;
    }

    /// <summary>The caret inside the <paramref name="occurrence"/>-th <c>Value</c>, then F2 (<c>refactor.rename</c>).</summary>
    private async Task<InlineRenameSession> F2Async(TextDocumentViewModel r, int occurrence = 2)
    {
        r.Selection = TextSelection.At(At(r.Buffer.Text, "Value", occurrence) + 2);
        await _services.Commands.ExecuteAsync(RenameRefactoring.RenameCommandId);
        return Assert.IsType<InlineRenameSession>(r.RenameSession);
    }

    private static void Type(TextDocumentViewModel r, string text)
    {
        foreach (var ch in text)
            Assert.True(r.TypeText(ch.ToString()), $"'{ch}' typed");
    }

    /// <summary>200 ms after the last change: the check, awaited.</summary>
    private async Task CheckAsync(InlineRenameSession session)
    {
        _clock.Advance(InlineRenameSession.CheckDelay);
        await session.Checking;
    }

    private static string Renamed(string text, string name) => text.Replace("Value", name, StringComparison.Ordinal);

    /// <summary>The late work of every session and commit the test started ends before the test does (I-210).</summary>
    private async Task SettleAsync(TextDocumentViewModel r)
    {
        await r.RenameCommitting;
        foreach (var session in _sessions)
            await session.Checking;
    }

    /// <summary>Holds the code-model worker until the returned gate is set, so that a check cannot return early.</summary>
    private (ManualResetEventSlim Gate, Task Held) HoldWorker()
    {
        var gate = new ManualResetEventSlim();
        var held = _services.CodeModel.Worker.RunAsync("test-gate", _ =>
        {
            gate.Wait();
            return true;
        });
        return (gate, held);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-050")]
    [Trait("Req", "IDE-REFAC-042")]
    public Task F2OnALocalLinksEveryOccurrence() => TestUiThread.RunAsync(() => F2OnALocalLinksEveryOccurrenceOnUiThread());

    private async Task F2OnALocalLinksEveryOccurrenceOnUiThread()
    {
        var r = await OpenRAsync();
        var changes = 0;
        r.RenameSessionChanged += (_, _) => changes++;

        var session = await F2Async(r, occurrence: 3);

        // The five occurrences are one linked field; the one at the caret is the primary and is selected whole.
        var primary = At(UnitR, "Value", 3);
        Assert.Equal(primary, r.RenameFields[0].Start);
        Assert.Equal(Enumerable.Range(1, 5).Select(i => At(UnitR, "Value", i)), r.RenameFields.Select(f => f.Start).Order());
        Assert.All(r.RenameFields, f => Assert.Equal(5, f.End - f.Start));
        Assert.Equal(new TextSelection(primary, primary + 5), r.Selection);
        Assert.True(r.InTemplateFieldMode);
        Assert.Equal((RenameFieldState.Editing, "Value", R), (session.State, session.OldName, session.Source.Path), new TupleIgnoreCase());
        Assert.Equal(1, changes);
        // No dialog, the engine is not busy, nothing changed; the hint is sticky.
        Assert.Empty(Dialogs.Dialogs);
        Assert.False(Engine.IsBusy);
        Assert.Equal(UnitR, r.Buffer.Text);
        Assert.False(r.IsModified);
        Assert.Equal("Rename Value: type the new name. Enter applies, Esc cancels", _services.Status.Message);
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(Format(Strings.Msg_Rename_InlineHint, "Value"), _services.Status.Message);
        await SettleAsync(r);
    }

    /// <summary>Compares (state, name, path) with the path ignoring case.</summary>
    private sealed class TupleIgnoreCase : IEqualityComparer<(RenameFieldState, string, string)>
    {
        public bool Equals((RenameFieldState, string, string) x, (RenameFieldState, string, string) y) =>
            x.Item1 == y.Item1 && x.Item2 == y.Item2 && string.Equals(x.Item3, y.Item3, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((RenameFieldState, string, string) obj) => obj.Item1.GetHashCode();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-051")]
    [Trait("Req", "IDE-REFAC-053")]
    public Task TypingMirrorsAndEnterCommitsAsOneUndoStep() => TestUiThread.RunAsync(() => TypingMirrorsAndEnterCommitsAsOneUndoStepOnUiThread());

    private async Task TypingMirrorsAndEnterCommitsAsOneUndoStepOnUiThread()
    {
        var r = await OpenRAsync();
        var session = await F2Async(r);

        // Typing replaces the selected primary and every occurrence follows each keystroke.
        Type(r, "Tot");
        Assert.Equal(Renamed(UnitR, "Tot"), r.Buffer.Text);
        // The caret may go into another occurrence: typing there changes all of them, too.
        var last = r.RenameFields.Max(f => f.End);
        r.Selection = TextSelection.At(last);
        Type(r, "al");
        Assert.Equal(Renamed(UnitR, "Total"), r.Buffer.Text);
        Assert.Same(session, r.RenameSession);
        Assert.Equal("Total", session.TypedName);
        // A line break is not inserted, by typing or by pasting.
        Assert.False(r.TypeText("\n"));
        Assert.False(r.Paste("A\nB"));
        Assert.Equal(Renamed(UnitR, "Total"), r.Buffer.Text);

        await CheckAsync(session);
        Assert.Equal(RenameFieldState.Editing, session.State);
        var caret = r.CaretOffset;
        Assert.True(r.HandleKey("Enter"));
        await r.RenameCommitting;

        // Committed: the text as typed, the caret where it was, modified and not saved; no line break went in.
        Assert.Null(r.RenameSession);
        Assert.False(session.IsActive);
        Assert.Empty(r.RenameFields);
        Assert.Equal(Renamed(UnitR, "Total"), r.Buffer.Text);
        Assert.Equal(caret, r.CaretOffset);
        Assert.True(r.IsModified);
        Assert.Equal(UnitR, Text(R));
        Assert.Equal("Renamed Value to Total: 5 occurrences", _services.Status.Message);
        Assert.Equal(0, HistoryCount); // the engine's history is not involved

        // The whole rename is one undo step.
        await _services.Commands.ExecuteAsync("edit.undo");
        Assert.Equal(UnitR, r.Buffer.Text);
        Assert.False(r.IsModified);
        Assert.False(r.Buffer.CanUndo);
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-054")]
    public Task EscapeRestoresTextUndoHistoryAndSavePoint() => TestUiThread.RunAsync(() => EscapeRestoresTextUndoHistoryAndSavePointOnUiThread());

    private async Task EscapeRestoresTextUndoHistoryAndSavePointOnUiThread()
    {
        var r = await OpenRAsync();
        // An edit before the session: undo history, modified.
        r.Selection = TextSelection.At(0);
        Assert.True(r.Paste("// note\n"));
        var before = r.Buffer.Text;
        var version = r.Buffer.Version;

        foreach (var way in (string[])["Escape", "edit.undo"])
        {
            var session = await F2Async(r);
            Assert.Equal(At(before, "Value", 2) + 2, session.StartCaret);
            Type(r, "Total");
            Assert.Equal(Renamed(before, "Total"), r.Buffer.Text);
            if (way == "Escape")
                Assert.True(r.HandleKey("Escape"));
            else
                await _services.Commands.ExecuteAsync("edit.undo");

            // Text, undo history and save point as before the session; nothing to redo; the caret at its start.
            Assert.Null(r.RenameSession);
            Assert.Equal(before, r.Buffer.Text);
            Assert.True(r.IsModified);
            Assert.True(r.Buffer.CanUndo);
            Assert.False(r.Buffer.CanRedo);
            Assert.Equal(TextSelection.At(session.StartCaret), r.Selection);
            Assert.Equal("Rename canceled; Value is unchanged", _services.Status.Message);
            Assert.True(r.Buffer.Version > version);
        }

        // One more undo is the edit before the sessions: the file's text, at its save point.
        await _services.Commands.ExecuteAsync("edit.undo");
        Assert.Equal(UnitR, r.Buffer.Text);
        Assert.False(r.IsModified);

        // A session without an edit ends on Escape without a message.
        _services.Status.ShowMessage("before", TimeSpan.FromSeconds(1));
        await F2Async(r);
        Assert.True(r.HandleKey("Escape"));
        Assert.Null(r.RenameSession);
        Assert.Equal(UnitR, r.Buffer.Text);
        Assert.NotEqual(Format(Strings.Msg_Rename_InlineCanceled, "Value"), _services.Status.Message);
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-052")]
    [Trait("Req", "IDE-REFAC-053")]
    public Task ConflictKeepsTheSessionAndShowsTheReason() => TestUiThread.RunAsync(() => ConflictKeepsTheSessionAndShowsTheReasonOnUiThread());

    private async Task ConflictKeepsTheSessionAndShowsTheReasonOnUiThread()
    {
        var r = await OpenRAsync();
        var session = await F2Async(r);
        var states = new List<RenameFieldState>();
        r.RenameSessionChanged += (_, _) => states.Add(session.State);

        // The other local's name: the check of the planner finds the conflict.
        Type(r, "Count");
        Assert.Equal(RenameFieldState.Editing, session.State); // nothing known before the 200 ms
        await CheckAsync(session);
        Assert.Equal(RenameFieldState.Problem, session.State);
        Assert.False(string.IsNullOrEmpty(session.Problem));
        Assert.Equal(session.Problem, _services.Status.Message); // the reason in words (UI-A11Y-041)
        Assert.Equal([RenameFieldState.Problem], states);

        // Enter does nothing: the session and the problem stay.
        Assert.True(r.HandleKey("Enter"));
        await r.RenameCommitting;
        Assert.Same(session, r.RenameSession);
        Assert.Equal(RenameFieldState.Problem, session.State);
        Assert.Equal(Renamed(UnitR, "Count"), r.Buffer.Text);

        // A change drops the problem until the next check.
        Type(r, "s");
        Assert.Equal(RenameFieldState.Editing, session.State);
        Assert.Equal(Format(Strings.Msg_Rename_InlineHint, "Value"), _services.Status.Message);
        await CheckAsync(session);
        Assert.Equal(RenameFieldState.Editing, session.State);
        Assert.True(r.HandleKey("Enter"));
        await r.RenameCommitting;
        Assert.Null(r.RenameSession);
        Assert.Equal(Renamed(UnitR, "Counts"), r.Buffer.Text);
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-052")]
    public Task InvalidNameKeepsTheSession() => TestUiThread.RunAsync(() => InvalidNameKeepsTheSessionOnUiThread());

    private async Task InvalidNameKeepsTheSessionOnUiThread()
    {
        var r = await OpenRAsync();
        var session = await F2Async(r);
        var requests = _services.CodeModel.Worker.RequestCount;

        foreach (var (typed, problem) in ((string, string)[])[("begin", "begin is a reserved word."), ("9x", Strings.Msg_Rename_NameInvalid), ("", Strings.Msg_Rename_NameInvalid)])
        {
            // Replace the whole name: select the primary occurrence and type (or delete).
            var (start, end) = r.RenameFields[0];
            r.Selection = new TextSelection(start, end);
            if (typed.Length == 0)
                Assert.True(r.HandleKey("Backspace"));
            else
                Type(r, typed);
            Assert.All(r.RenameFields, f => Assert.Equal(typed, r.Buffer.GetText(f.Start, f.End - f.Start)));
            await CheckAsync(session);
            Assert.Equal((RenameFieldState.Problem, problem), (session.State, session.Problem));
            Assert.Equal(problem, _services.Status.Message);
            Assert.True(r.HandleKey("Enter"));
            await r.RenameCommitting;
            Assert.Same(session, r.RenameSession);
        }

        Assert.Equal(requests, _services.CodeModel.Worker.RequestCount); // no language-service query for these names
        Assert.True(r.HandleKey("Escape"));
        Assert.Equal(UnitR, r.Buffer.Text);
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-053")]
    public Task EnterBeforeTheCheckReturnsWaitsForIt() => TestUiThread.RunAsync(() => EnterBeforeTheCheckReturnsWaitsForItOnUiThread());

    private async Task EnterBeforeTheCheckReturnsWaitsForItOnUiThread()
    {
        var r = await OpenRAsync();
        var session = await F2Async(r);
        var (gate, held) = HoldWorker();
        try
        {
            // Enter before the 200 ms: the check is asked at once and the commit waits for it.
            Type(r, "Total");
            Assert.True(r.HandleKey("Enter"));
            Assert.False(r.RenameCommitting.IsCompleted);
            Assert.Same(session, r.RenameSession);

            // A change meanwhile drops that commit: Enter is pressed again.
            Type(r, "s");
            gate.Set();
            await r.RenameCommitting;
            Assert.Same(session, r.RenameSession);
            Assert.Equal(Renamed(UnitR, "Totals"), r.Buffer.Text);

            Assert.True(r.HandleKey("Enter"));
            await r.RenameCommitting;
            Assert.Null(r.RenameSession);
            Assert.Equal(Renamed(UnitR, "Totals"), r.Buffer.Text);
            Assert.Equal("Renamed Value to Totals: 5 occurrences", _services.Status.Message);
        }
        finally
        {
            gate.Set();
            await held;
            gate.Dispose();
        }

        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-052")]
    public Task ResultForAnOlderVersionIsDropped() => TestUiThread.RunAsync(() => ResultForAnOlderVersionIsDroppedOnUiThread());

    private async Task ResultForAnOlderVersionIsDroppedOnUiThread()
    {
        var r = await OpenRAsync();
        var session = await F2Async(r);
        var (gate, held) = HoldWorker();
        Task older;
        try
        {
            // The check of "Count" (a conflict) starts and is held; the name changes before its result arrives.
            Type(r, "Count");
            _clock.Advance(InlineRenameSession.CheckDelay);
            older = session.Checking;
            Assert.False(older.IsCompleted);
            Type(r, "er");
        }
        finally
        {
            gate.Set();
            await held;
            gate.Dispose();
        }

        await older;
        Assert.Equal((RenameFieldState.Editing, null), (session.State, session.Problem)); // the conflict of "Count" is not shown
        Assert.Equal(Format(Strings.Msg_Rename_InlineHint, "Value"), _services.Status.Message);
        await CheckAsync(session);
        Assert.Equal((RenameFieldState.Editing, null), (session.State, session.Problem));
        Assert.True(r.HandleKey("Escape"));
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-054")]
    public Task CaretLeavingCancelsAndRestores() => TestUiThread.RunAsync(() => CaretLeavingCancelsAndRestoresOnUiThread());

    private async Task CaretLeavingCancelsAndRestoresOnUiThread()
    {
        var r = await OpenRAsync();

        // The caret moves inside the occurrences: the session stays.
        var session = await F2Async(r);
        Type(r, "Total");
        r.Selection = TextSelection.At(r.RenameFields[^1].Start + 1);
        r.Selection = new TextSelection(r.RenameFields[1].Start, r.RenameFields[1].End);
        Assert.Same(session, r.RenameSession);

        // The caret leaves: the text comes back, the caret stays where it went (moved with the restored text).
        var end = At(UnitR, "end;");
        r.Selection = TextSelection.At(At(r.Buffer.Text, "end;"));
        Assert.Null(r.RenameSession);
        Assert.Equal(UnitR, r.Buffer.Text);
        Assert.False(r.IsModified);
        Assert.Equal(TextSelection.At(end), r.Selection);
        Assert.Equal("Rename canceled; Value is unchanged", _services.Status.Message);

        // A selection from an occurrence to outside it leaves, too.
        await F2Async(r);
        Type(r, "Total");
        var (start, stop) = r.RenameFields[0];
        r.Selection = new TextSelection(start, stop + 3);
        Assert.Null(r.RenameSession);
        Assert.Equal(UnitR, r.Buffer.Text);

        // A change from outside (the text replaced) ends the session without restoring anything.
        session = await F2Async(r);
        Type(r, "Total");
        var renamed = r.Buffer.Text;
        r.Buffer.ApplyEdits([new TextEdit(0, 0, "// outside\n")]);
        Assert.Null(r.RenameSession);
        Assert.False(session.IsActive);
        Assert.Equal("// outside\n" + renamed, r.Buffer.Text);
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-054")]
    public Task OtherCommandCancelsFirst() => TestUiThread.RunAsync(() => OtherCommandCancelsFirstOnUiThread());

    private async Task OtherCommandCancelsFirstOnUiThread()
    {
        var r = await OpenRAsync();
        var session = await F2Async(r);
        Type(r, "Total");

        // Copy and Delete inside an occurrence keep the session.
        var start = r.RenameFields[0].Start;
        r.Selection = new TextSelection(start, start + 2);
        await _services.Commands.ExecuteAsync("edit.copy");
        await _services.Commands.ExecuteAsync("edit.delete");
        Assert.Same(session, r.RenameSession);
        Assert.Equal(Renamed(UnitR, "tal"), r.Buffer.Text);

        // Another editor command: the session is cancelled first, then the command runs on the restored text.
        await _services.Commands.ExecuteAsync("edit.upperCase");
        Assert.Null(r.RenameSession);
        Assert.Equal(UnitR.Replace("  Value := 1;", "  VALUE := 1;", StringComparison.Ordinal), r.Buffer.Text);
        // Its undo is its own step; the session's step is gone.
        await _services.Commands.ExecuteAsync("edit.undo");
        Assert.Equal(UnitR, r.Buffer.Text);
        Assert.False(r.Buffer.CanUndo);
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-054")]
    public Task SaveCancelsFirstAndSavesTheOldName() => TestUiThread.RunAsync(() => SaveCancelsFirstAndSavesTheOldNameOnUiThread());

    private async Task SaveCancelsFirstAndSavesTheOldNameOnUiThread()
    {
        var r = await OpenRAsync();
        r.Selection = TextSelection.At(0);
        Assert.True(r.Paste("// note\n"));
        await F2Async(r);
        Type(r, "Total");

        // A save that is no command (as before a build): the cancel runs first, the saved text has the old name.
        Assert.True(await _services.Documents.SaveAsync(r));
        Assert.Null(r.RenameSession);
        Assert.Equal("// note\n" + UnitR, Text(R));
        Assert.Equal("// note\n" + UnitR, r.Buffer.Text);
        Assert.False(r.IsModified);

        // File > Save cancels it, too; read only and closing as well.
        await F2Async(r);
        Type(r, "Total");
        await _services.Commands.ExecuteAsync("file.save");
        Assert.Null(r.RenameSession);
        Assert.Equal("// note\n" + UnitR, Text(R));

        await F2Async(r);
        Type(r, "Total");
        Assert.True(await _services.Documents.CloseAsync(r)); // no save prompt: the session's edits are taken back first
        Assert.Empty(Dialogs.Requests);
        Assert.Null(r.RenameSession);
        Assert.Equal("// note\n" + UnitR, Text(R));
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-054")]
    public Task EngineOperationCancelsFirst() => TestUiThread.RunAsync(() => EngineOperationCancelsFirstOnUiThread());

    private async Task EngineOperationCancelsFirstOnUiThread()
    {
        var r = await OpenRAsync();
        var session = await F2Async(r);
        Type(r, "Total");

        // An operation of the engine on another file: the session is cancelled when the engine becomes busy.
        var app = Dir + @"\App.pas";
        var edit = new WorkspaceEdit("Insert", [new FileEdit(app, Main, [new TextEdit(0, 0, "{ x }\n")])]);
        var result = await Engine.ApplyAsync(edit, PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Null(r.RenameSession);
        Assert.False(session.IsActive);
        Assert.Equal(UnitR, r.Buffer.Text);
        Assert.Equal("{ x }\n" + Main, Text(app));

        // Read only: cancelled before the document stops being writable.
        await F2Async(r);
        Type(r, "Total");
        r.SetReadOnly(true);
        Assert.Null(r.RenameSession);
        Assert.Equal(UnitR, r.Buffer.Text);
        r.SetReadOnly(false);
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-054")]
    [Trait("Req", "IDE-REFAC-041")]
    public Task F2DuringASessionStartsAgain() => TestUiThread.RunAsync(() => F2DuringASessionStartsAgainOnUiThread());

    private async Task F2DuringASessionStartsAgainOnUiThread()
    {
        var r = await OpenRAsync();
        var first = await F2Async(r);
        Type(r, "Total");
        var primary = r.RenameFields[0].Start;

        await _services.Commands.ExecuteAsync(RenameRefactoring.RenameCommandId);

        // The first session is taken back; a new one starts on the restored text with the same target.
        var second = Assert.IsType<InlineRenameSession>(r.RenameSession);
        Assert.NotSame(first, second);
        Assert.False(first.IsActive);
        Assert.Equal(UnitR, r.Buffer.Text);
        Assert.Equal(("Value", primary), (second.OldName, r.RenameFields[0].Start));
        Assert.Equal(new TextSelection(primary, primary + 5), r.Selection);
        Assert.Empty(Dialogs.Dialogs);
        Assert.True(r.HandleKey("Escape"));
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-051")]
    public Task NoCompletionOrTemplateExpansionInASession() => TestUiThread.RunAsync(() => NoCompletionOrTemplateExpansionInASessionOnUiThread());

    private async Task NoCompletionOrTemplateExpansionInASessionOnUiThread()
    {
        var r = await OpenRAsync();
        var session = await F2Async(r);

        // Typing opens no completion; Ctrl+Space and parameter info are not available; no bracket is closed.
        Type(r, "T(");
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.NotEqual(true, r.Completion?.IsOpen);
        Assert.False(_services.Commands.CanExecute("edit.completeCode"));
        Assert.False(_services.Commands.CanExecute("edit.parameterInfo"));
        Assert.Equal(Renamed(UnitR, "T("), r.Buffer.Text);
        await session.Checking;

        // A template's abbreviation as the name: Tab commits it and expands nothing.
        var (start, end) = r.RenameFields[0];
        r.Selection = new TextSelection(start, end);
        Type(r, "ife");
        Assert.True(r.HandleKey("Tab"));
        await r.RenameCommitting;
        Assert.Null(r.RenameSession);
        Assert.Equal(Renamed(UnitR, "ife"), r.Buffer.Text);
        Assert.Equal("Renamed Value to ife: 5 occurrences", _services.Status.Message);
        await SettleAsync(r);
    }

    [Theory(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-042")]
    [Trait("Req", "IDE-REFAC-055")]
    [InlineData("ampersand")]
    [InlineData("block")]
    public Task AmpersandOccurrenceOrBlockSelectionUsesTheDialogPath(string way) =>
        TestUiThread.RunAsync(() => AmpersandOccurrenceOrBlockSelectionUsesTheDialogPathOnUiThread(way));

    private async Task AmpersandOccurrenceOrBlockSelectionUsesTheDialogPathOnUiThread(string way)
    {
        var text = way == "ampersand" ? UnitR.Replace("  Value := 1;", "  &Value := 1;", StringComparison.Ordinal) : UnitR;
        var r = await OpenRAsync(text);
        RenameSymbolViewModel? shown = null;
        Dialogs.Script(RenameSymbolViewModel.DialogName, async dialog =>
        {
            shown = Assert.IsType<RenameSymbolViewModel>(dialog);
            shown.NewName = "Total";
            _clock.Advance(RenameSymbolViewModel.CheckDelay);
            await shown.Checking;
            await shown.PressDefaultAsync();
        });
        var at = At(text, "Value", 3);
        if (way == "block")
        {
            // A column block on one line, inside the name.
            r.Selection = new TextSelection(at + 1, at + 3);
            r.ViewState.RectangularSelection = true;
            Assert.True(r.HasBlockSelection);
        }
        else
        {
            r.Selection = TextSelection.At(at + 2);
        }

        await _services.Commands.ExecuteAsync(RenameRefactoring.RenameCommandId);

        // The dialog renamed it; the edit is confined to the buffer, so the engine applied it without a preview.
        Assert.NotNull(shown);
        Assert.Null(r.RenameSession);
        Assert.Equal(UnitR.Replace("Value", "Total", StringComparison.Ordinal), r.Buffer.Text);
        Assert.DoesNotContain(Dialogs.Dialogs, d => d is RefactoringPreviewViewModel);
        Assert.Equal(1, HistoryCount);
        await shown!.Checking;
        await SettleAsync(r);
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-061")]
    [Trait("Req", "IDE-REFAC-062")]
    public Task UnrenamedInTheRoutineBecomeRows() => TestUiThread.RunAsync(() => UnrenamedInTheRoutineBecomeRowsOnUiThread());

    private async Task UnrenamedInTheRoutineBecomeRowsOnUiThread()
    {
        // A Value in inactive code of Go (behind two renamed ones on its line) and one in another routine.
        const string text = "unit R;\n\ninterface\n\nprocedure Go;\n\nimplementation\n\nprocedure Other;\nbegin\n"
            + "  {$IFDEF NEVER_DEFINED} Value := 0; {$ENDIF}\nend;\n\nprocedure Go;\nvar\n  Value: Integer;\n  Count: Integer;\n"
            + "begin\n  Value := 1;\n  Count := Value + 1;\n  Value := Value + Count; {$IFDEF NEVER_DEFINED} Value := 0; {$ENDIF}\nend;\n\nend.\n";
        var r = await OpenRAsync(text);
        var layout = Layout();
        var session = await F2Async(r, occurrence: 3);
        Assert.Equal(5, r.RenameFields.Count);
        Type(r, "Sum");
        await CheckAsync(session);
        // The session does not name them (IDE-REFAC-062): the hint only, no row yet.
        Assert.Equal(Format(Strings.Msg_Rename_InlineHint, "Value"), _services.Status.Message);
        Assert.DoesNotContain(_services.Messages.Items, i => !i.IsSummary);

        Assert.True(r.HandleKey("Enter"));
        await r.RenameCommitting;

        // Listed after the commit: only the one of Go (the scope of a local), at its place in the renamed text.
        Assert.Null(r.RenameSession);
        var renamed = r.Buffer.Text;
        Assert.Contains("  Sum := Sum + Count; {$IFDEF NEVER_DEFINED} Value := 0; {$ENDIF}\n", renamed, StringComparison.Ordinal);
        var (line, column) = RenameCommandTests.LineColumn(renamed, "Count; {$IFDEF NEVER_DEFINED} Value", "Count; {$IFDEF NEVER_DEFINED} ".Length);
        var row = Assert.Single(_services.Messages.Items, i => !i.IsSummary);
        Assert.Equal((Core.Build.MessageSeverity.Warning, "Value in inactive code was not renamed to Sum", line, column, column + 5),
            (row.Severity, row.Text, row.Location!.Line, row.Location.Column, row.Location.EndColumn));
        Assert.Equal(R, row.Location.FilePath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Rename 'Value' to 'Sum': occurrences that were not changed are listed in Messages (1)", _services.Status.Message);
        Assert.Equal(layout, Layout()); // Messages is neither opened nor activated
        await SettleAsync(r);
    }

    private string Layout() => string.Join(" | ", _services.Layout.Layout.Groups.Select(g => string.Join(",", g.Tools) + ">" + g.ActiveTool));
}
