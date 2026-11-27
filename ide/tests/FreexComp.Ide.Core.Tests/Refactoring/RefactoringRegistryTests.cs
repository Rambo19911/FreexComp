using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// The refactoring registry (I-351, F13-003): a registered refactoring handles its catalogued command, the quick actions
/// of a position come from the providers and the registered refactorings — fixes first, no duplicates, at most twenty —
/// and the language-service provider offers the code actions of the diagnostics in the selected range.
/// </summary>
[Trait("Phase", "8")]
public sealed class RefactoringRegistryTests : RefactoringTestBase
{
    private const string DeclareCommand = "refactor.declareVariable";
    private const string ExtractCommand = "refactor.extractMethod";
    private const string Notes = Dir + @"\Notes.txt";

    /// <summary>A unit with one undeclared variable (E2003) in a routine body.</summary>
    private const string Undeclared = "unit A;\n\ninterface\n\nprocedure Run;\n\nimplementation\n\nprocedure Run;\nbegin\n  Count := 1;\nend;\n\nend.\n";

    private static WorkspaceEdit InsertAtStart(RefactoringContext context, string title) =>
        new(title, [new FileEdit(context.Document.FilePath!, context.Document.Buffer.Text, [new TextEdit(0, 0, "{x}")])]);

    private static QuickAction Fix(string id, string title) => new(id, title, QuickActionKind.Fix, _ => Task.FromResult<WorkspaceEdit?>(null));

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-030")]
    [Trait("Req", "IDE-REFAC-031")]
    [Trait("Req", "IDE-REFAC-012")]
    public Task RegisteredRefactoringHandlesItsCommand() => TestUiThread.RunAsync(() => RegisteredRefactoringHandlesItsCommandOnUiThread());

    private async Task RegisteredRefactoringHandlesItsCommandOnUiThread()
    {
        // Registered at composition time: one refactoring bound to a command that is delivered here, one bound to a
        // command that is not.
        var declare = new FakeRefactoring("declare-variable", "Declare Variable", DeclareCommand);
        var extract = new FakeRefactoring("extract-method", "Extract Method", ExtractCommand);
        using (Phase8Features.WithDelivered(DeclareCommand, "refactor.undo", "edit.quickActions"))
        {
            _services = Compose(o =>
            {
                o.Refactorings.Add(declare);
                o.Refactorings.Add(extract);
            });
        }

        var commands = _services.Commands;
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        Assert.False(commands.CanExecute(DeclareCommand)); // no text view

        await OpenProjectAsync();
        File(Notes, "notes\n");
        var a = await OpenAsync(A);
        a.Selection = new TextSelection(5, 6);

        // CanExecute follows IsApplicable, asked with the document and its selection and without a source (no query).
        Assert.True(commands.CanExecute(DeclareCommand));
        Assert.Equal((a, new TextSelection(5, 6)), (declare.AskedWith!.Document, declare.AskedWith.Selection));
        Assert.Null(declare.AskedWith.Source);
        declare.Applicable = _ => false;
        Assert.False(commands.CanExecute(DeclareCommand));
        declare.Applicable = _ => true;
        a.SetReadOnly(true); // … and only in a writable text view
        Assert.False(commands.CanExecute(DeclareCommand));
        a.SetReadOnly(false);
        Assert.True(commands.CanExecute(DeclareCommand));

        // A command that is not delivered stays on the later-phase stub, whatever its refactoring says.
        Assert.False(commands.CanExecute(ExtractCommand));
        await commands.ExecuteAsync(ExtractCommand);
        Assert.Empty(extract.Computed);

        // Executing computes with the source of the file and applies the edit: the active buffer, no preview.
        var text = a.Buffer.Text;
        declare.Compute = (context, _) => Task.FromResult<WorkspaceEdit?>(InsertAtStart(context, "Declare variable 'X'"));
        await commands.ExecuteAsync(DeclareCommand);
        var context = Assert.Single(declare.Computed);
        Assert.Equal((a, new TextSelection(5, 6)), (context.Document, context.Selection));
        Assert.Equal(A, context.Source!.Path, ignoreCase: true);
        Assert.Equal(text, context.Source.Text.Text);
        Assert.Equal("{x}" + text, a.Buffer.Text);
        Assert.Equal("Declare variable 'X': 1 change", _services.Status.Message);
        Assert.Equal((1, "Declare variable 'X'"), (HistoryCount, Engine.UndoTitle));
        Assert.Empty(Dialogs.Requests);
        Assert.Empty(Dialogs.Dialogs);

        // A null result (the refactoring's own dialog was cancelled): nothing changes, nothing is shown.
        var (version, status) = (a.Buffer.Version, _services.Status.Message);
        declare.Compute = (_, _) => Task.FromResult<WorkspaceEdit?>(null);
        await commands.ExecuteAsync(DeclareCommand);
        Assert.Equal(RefactoringOutcome.Canceled, (await Engine.RunAsync(declare)).Outcome);
        Assert.Equal((version, status, 1), (a.Buffer.Version, _services.Status.Message, HistoryCount));
        Assert.Empty(Dialogs.Requests);

        // A refusal is said with the refactoring's title and its reason.
        declare.Compute = (_, _) => throw new RefactoringException("The selection is not an expression.");
        await commands.ExecuteAsync(DeclareCommand);
        var refused = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.Refused", refused.MessageId);
        Assert.Equal(["Declare Variable", "The selection is not an expression."], refused.Args);
        var result = await Engine.RunAsync(declare);
        Assert.Equal((RefactoringOutcome.Failed, "The selection is not an expression."), (result.Outcome, result.Error));
        Assert.Equal((version, 1, 2), (a.Buffer.Version, HistoryCount, Dialogs.Requests.Count));
        Assert.False(Engine.IsBusy);

        // While the refactoring computes the engine is busy: its commands are disabled and a second run is refused.
        var computing = new TaskCompletionSource<WorkspaceEdit?>(TaskCreationOptions.RunContinuationsAsynchronously);
        declare.Compute = (_, _) => computing.Task;
        var running = commands.ExecuteAsync(DeclareCommand);
        Assert.True(Engine.IsBusy);
        Assert.False(commands.CanExecute(DeclareCommand));
        Assert.False(commands.CanExecute("edit.quickActions"));
        Assert.False(commands.CanExecute("refactor.undo"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine.RunAsync(declare));
        _clock.Advance(TimeSpan.FromMilliseconds(400)); // … and it says what it works on (IDE-REFAC-012)
        Assert.Equal(Format(Strings.Msg_Refactoring_Working, "Declare Variable"), _services.Status.Message);
        computing.SetResult(null);
        await running;
        Assert.False(Engine.IsBusy);
        Assert.NotEqual(Format(Strings.Msg_Refactoring_Working, "Declare Variable"), _services.Status.Message);
        Assert.True(commands.CanExecute(DeclareCommand));
        Assert.True(commands.CanExecute("edit.quickActions"));

        // A document that is not Object Pascal has no source.
        var notes = await OpenAsync(Notes);
        declare.Compute = (_, _) => Task.FromResult<WorkspaceEdit?>(null);
        await commands.ExecuteAsync(DeclareCommand);
        Assert.Same(notes, declare.Computed[^1].Document);
        Assert.Null(declare.Computed[^1].Source);

        // Ids are unique.
        Assert.Throws<InvalidOperationException>(() => Engine.Register(new FakeRefactoring("declare-variable", "Another")));
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-032")]
    public Task FixesFirstDuplicatesDroppedCappedAtTwenty() => TestUiThread.RunAsync(() => FixesFirstDuplicatesDroppedCappedAtTwentyOnUiThread());

    private async Task FixesFirstDuplicatesDroppedCappedAtTwentyOnUiThread()
    {
        await OpenProjectAsync();
        File(Notes, "notes\n");
        var notes = await OpenAsync(Notes);
        var selection = new TextSelection(1, 3);
        var offered = new List<QuickAction> { Fix("a", "Fix A"), new("x", "Extra X", QuickActionKind.Refactoring, _ => Task.FromResult<WorkspaceEdit?>(null)), Fix("b", "Fix B") };
        var first = new FakeQuickActionProvider((_, _) => Task.FromResult<IReadOnlyList<QuickAction>>(offered));
        var second = new FakeQuickActionProvider(Fix("a", "Fix A"), Fix("a", "Fix A (other)"), Fix("c", "Fix C"));
        var failing = new FakeQuickActionProvider((_, _) => throw new InvalidOperationException("boom"));
        var one = new FakeRefactoring("r1", "Refactor One") { Compute = (c, _) => Task.FromResult<WorkspaceEdit?>(InsertAtStart(c, "One")) };
        var two = new FakeRefactoring("r2", "Refactor Two") { Applicable = _ => false };
        Engine.Register(first);
        Engine.Register(failing);
        Engine.Register(second);
        Engine.Register(one);
        Engine.Register(two);
        static string Row(QuickAction action) => $"{action.Kind} {action.Id} {action.Title}";

        // All fixes, then all refactorings, each in the order received; the same id and title once; the provider that
        // throws is skipped and logged; a refactoring that is not applicable is left out.
        var actions = await Engine.GetQuickActionsAsync(notes, selection, CancellationToken.None);
        Assert.Equal(
            ["Fix a Fix A", "Fix b Fix B", "Fix a Fix A (other)", "Fix c Fix C", "Refactoring x Extra X", "Refactoring r1 Refactor One"],
            actions.Select(Row));
        Assert.Single(Log.Lines, l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains("Refactoring", StringComparison.Ordinal) && l.Contains("boom", StringComparison.Ordinal));
        Assert.False(Engine.IsBusy);

        // Every provider was asked with the document and the selection; the refactorings without a source.
        foreach (var provider in (FakeQuickActionProvider[])[first, second, failing])
            Assert.Equal((notes, selection), (Assert.Single(provider.Asked).Document, provider.Asked[0].Selection));
        Assert.Equal((notes, selection), (one.AskedWith!.Document, one.AskedWith.Selection));
        Assert.Null(one.AskedWith.Source);
        Assert.NotNull(two.AskedWith);

        // The action of a refactoring computes what the refactoring computes.
        var edit = await actions[^1].ComputeAsync(CancellationToken.None);
        Assert.Equal("One", edit!.Title);
        Assert.Equal((notes, selection), (Assert.Single(one.Computed).Document, one.Computed[0].Selection));

        // At most twenty: the fixes are kept first.
        two.Applicable = _ => true;
        offered.Clear();
        offered.AddRange(Enumerable.Range(1, 17).Select(i => Fix("f" + i, "Fix " + i)));
        actions = await Engine.GetQuickActionsAsync(notes, selection, CancellationToken.None);
        Assert.Equal(20, actions.Count);
        Assert.Equal(
            [.. Enumerable.Range(1, 17).Select(i => "f" + i), "a", "a", "c"],
            actions.Select(a => a.Id));
        Assert.All(actions, a => Assert.Equal(QuickActionKind.Fix, a.Kind));
        offered.RemoveRange(15, 2);
        actions = await Engine.GetQuickActionsAsync(notes, selection, CancellationToken.None);
        Assert.Equal(20, actions.Count);
        Assert.Equal(["Fix c Fix C", "Refactoring r1 Refactor One", "Refactoring r2 Refactor Two"], actions.Skip(17).Select(Row));

        // A newer request for the same document cancels the one that is still collecting; so does closing the document.
        var pending = new TaskCompletionSource<IReadOnlyList<QuickAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        Engine.Register(new FakeQuickActionProvider((_, ct) =>
        {
            tokens.Add(ct);
            return pending.Task.WaitAsync(ct);
        }));
        var older = Engine.GetQuickActionsAsync(notes, selection, CancellationToken.None);
        Assert.False(older.IsCompleted);
        var newer = Engine.GetQuickActionsAsync(notes, selection, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => older);
        Assert.Equal([true, false], tokens.Select(t => t.IsCancellationRequested));
        Assert.True(await _services.Documents.CloseAsync(notes));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => newer);
        Assert.Equal([true, true], tokens.Select(t => t.IsCancellationRequested));

        // The caller's own token cancels too.
        var other = await OpenAsync(A);
        using var cancel = new CancellationTokenSource();
        var cancelled = Engine.GetQuickActionsAsync(other, TextSelection.At(0), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        pending.SetResult([]);
        Assert.Equal(20, (await Engine.GetQuickActionsAsync(other, TextSelection.At(0), CancellationToken.None)).Count);
    }

    /// <summary>The project of the fixture with the real system unit and <see cref="Undeclared"/> as A.pas; no document open.</summary>
    private async Task OpenUndeclaredAsync()
    {
        WriteToolchain();
        File(ProjectPath, ProjectFile);
        File(Dir + @"\App.pas", "program App;\n\nuses A, B, C, D;\n\nbegin\nend.\n");
        File(A, Undeclared);
        foreach (var path in (string[])[B, C, D])
            File(path, Unit(Path.GetFileNameWithoutExtension(path)));
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        await _services.ProjectManager.MissingFileChecks;
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-033")]
    [Trait("Req", "IDE-REFAC-003")]
    public Task LanguageServiceProviderOffersCodeActionsOfTheRange() => TestUiThread.RunAsync(() => LanguageServiceProviderOffersCodeActionsOfTheRangeOnUiThread());

    private async Task LanguageServiceProviderOffersCodeActionsOfTheRangeOnUiThread()
    {
        await OpenUndeclaredAsync();
        var a = await OpenAsync(A);
        a.Buffer.ApplyEdits([new TextEdit(0, 0, "// unsaved\n")]); // the buffer differs from the file
        var text = a.Buffer.Text;
        var count = text.IndexOf("Count", StringComparison.Ordinal);

        // The caret in the undeclared identifier: the fix of the language service, with its id and title.
        var action = Assert.Single(await Engine.GetQuickActionsAsync(a, TextSelection.At(count + 2), CancellationToken.None));
        var source = _services.CodeModel.Workspace.GetDocument(A);
        var expected = Assert.Single(CodeActionService.GetCodeActions(source, new TextSpan(count + 2, 0), CancellationToken.None));
        Assert.Equal(("declare-variable", "Declare variable 'Count: Integer'", QuickActionKind.Fix), (action.Id, action.Title, action.Kind));
        Assert.Equal((expected.Id, expected.Title), (action.Id, action.Title));

        // Its workspace edit: the service's changes against the buffer text.
        var edit = await action.ComputeAsync(CancellationToken.None);
        Assert.Equal(action.Title, edit!.Title);
        var file = Assert.Single(edit.Files);
        Assert.Equal(A, file.Path, ignoreCase: true);
        Assert.Equal(text, file.BaseText);
        Assert.Equal(expected.Edits.Single().Changes.Select(c => (c.Span.Start, c.Span.Length, c.NewText)), file.Edits.Select(e => (e.Offset, e.Length, e.NewText)));

        // Applying it declares the variable — one undo step in the buffer, no preview, nothing written.
        var result = await Engine.ApplyAsync(edit);
        Assert.Equal((RefactoringOutcome.Applied, 1, 1), (result.Outcome, result.EditCount, result.FileCount));
        Assert.Equal(text.Replace("procedure Run;\nbegin\n", "procedure Run;\nvar\n  Count: Integer;\nbegin\n", StringComparison.Ordinal), a.Buffer.Text);
        Assert.Equal(Undeclared, Text(A));
        Assert.Empty(Dialogs.Dialogs);
        Assert.Equal("Declare variable 'Count: Integer': 1 change", _services.Status.Message);
        Assert.Empty(await Engine.GetQuickActionsAsync(a, TextSelection.At(a.Buffer.Text.IndexOf("Count :=", StringComparison.Ordinal)), CancellationToken.None)); // declared now
        a.Buffer.Undo();
        Assert.Equal(text, a.Buffer.Text);

        // A selection is the range: one that touches the diagnostic has the fix, one that does not has none.
        var line = text.IndexOf("  Count := 1;", StringComparison.Ordinal);
        Assert.Equal("declare-variable", Assert.Single(await Engine.GetQuickActionsAsync(a, new TextSelection(line + 13, line), CancellationToken.None)).Id);
        Assert.Empty(await Engine.GetQuickActionsAsync(a, new TextSelection(0, line - 1), CancellationToken.None));

        // A caret elsewhere gives none.
        Assert.Empty(await Engine.GetQuickActionsAsync(a, TextSelection.At(text.IndexOf("begin", StringComparison.Ordinal)), CancellationToken.None));
        Assert.Empty(await Engine.GetQuickActionsAsync(a, TextSelection.At(0), CancellationToken.None));

        // A file without a project gives none: there is no source to ask about.
        a.Buffer.Undo();
        Assert.False(a.IsModified);
        Assert.True(await _services.Documents.CloseAllAsync());
        Assert.True(await _services.Projects.CloseAsync());
        var loose = await OpenAsync(A);
        var requests = _services.CodeModel.Worker.RequestCount;
        Assert.Empty(await Engine.GetQuickActionsAsync(loose, TextSelection.At(Undeclared.IndexOf("Count", StringComparison.Ordinal) + 2), CancellationToken.None));
        Assert.Equal(requests, _services.CodeModel.Worker.RequestCount);
    }
}
