using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// Rename Symbol on a unit name (I-382, F13-001; IDE-REFAC-056…058): the preconditions are refusals; the Rename dialog in
/// unit mode validates the new name as the Project Manager validates a file name and checks it with the language service;
/// the Preview dialog in confirm mode shows every text change, all or nothing; then the Project Manager renames the file,
/// its project entry, the heading and every reference. The engine records nothing.
/// </summary>
[Trait("Phase", "8")]
public sealed class UnitRenameCommandTests : RefactoringTestBase
{
    private const string Unit1 = Dir + @"\Unit1.pas";
    private const string Unit2 = Dir + @"\Unit2.pas";
    private const string Unit3 = Dir + @"\Unit3.pas";
    private const string Customers = Dir + @"\Customers.pas";

    private readonly List<RenameSymbolViewModel> _dialogs = [];
    private readonly List<RefactoringPreviewViewModel> _previews = [];

    /// <summary>The <see cref="RenameFixture"/> project at <see cref="Dir"/>; <paramref name="app"/> replaces the program text.</summary>
    private async Task OpenFixtureAsync(string? app = null, string? projectFile = null)
    {
        WriteToolchain();
        File(ProjectPath, projectFile ?? RenameFixture.ProjectFile);
        File(Dir + @"\App.pas", app ?? RenameFixture.App);
        File(Unit1, RenameFixture.Unit1);
        File(Unit2, RenameFixture.Unit2);
        File(Unit3, RenameFixture.Unit3);
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        await _services.ProjectManager.MissingFileChecks;
    }

    /// <summary><paramref name="path"/> open and active, the editor focused, the caret inside the first <paramref name="name"/>.</summary>
    private async Task<TextDocumentViewModel> CaretOnAsync(string path, string name)
    {
        var document = await OpenAsync(path);
        _services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, document, IsTextInput: false));
        var at = document.Buffer.Text.IndexOf(name, StringComparison.Ordinal);
        Assert.True(at >= 0, name);
        document.Selection = TextSelection.At(at + 1);
        return document;
    }

    /// <summary>The Rename dialog gets <paramref name="name"/>, waits for its check and presses <c>Rename</c>.</summary>
    private void DialogRenames(string name) => Dialogs.Script(RenameSymbolViewModel.DialogName, async dialog =>
    {
        var model = Assert.IsType<RenameSymbolViewModel>(dialog);
        _dialogs.Add(model);
        model.NewName = name;
        _clock.Advance(RenameSymbolViewModel.CheckDelay);
        await model.Checking;
        await model.PressDefaultAsync();
    });

    /// <summary>The Preview dialog (confirm mode) presses <c>Apply</c> when <paramref name="apply"/>, else <c>Cancel</c>.</summary>
    private void PreviewAnswers(bool apply) => Dialogs.Script(RefactoringPreviewViewModel.DialogName, dialog =>
    {
        var preview = Assert.IsType<RefactoringPreviewViewModel>(dialog);
        _previews.Add(preview);
        return apply ? preview.PressDefaultAsync() : preview.PressCancelAsync();
    });

    private Task F2Async() => _services.Commands.ExecuteAsync(RenameRefactoring.RenameCommandId);

    /// <summary>The late work of every dialog the test opened ends before the test does (I-210).</summary>
    private async Task SettleAsync()
    {
        foreach (var dialog in _dialogs)
            await dialog.Checking;
        foreach (var preview in _previews)
        {
            await preview.DiffUpdate;
            if (preview.Diff is { } diff)
                await diff.Recomputing;
        }
    }

    private void AssertFixtureUnchanged(string app)
    {
        Assert.Equal(app, Text(Dir + @"\App.pas"));
        Assert.Equal(RenameFixture.Unit1, Text(Unit1));
        Assert.Equal(RenameFixture.Unit2, Text(Unit2));
        Assert.Equal(RenameFixture.Unit3, Text(Unit3));
        Assert.False(_fs.FileExists(Customers));
        Assert.Equal(RenameFixture.ProjectFile, Text(ProjectPath));
    }

    /// <summary>
    /// IDE-REFAC-057, 058: F2 on <c>Unit1</c> in Unit2's <c>uses</c>; the dialog in unit mode; the confirm-mode preview;
    /// <c>Apply</c>: the file is renamed, its heading and every reference updated, the open Unit2 edited and saved, the
    /// project entry follows; the engine's history is not involved.
    /// </summary>
    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-057")]
    [Trait("Req", "IDE-REFAC-058")]
    [Trait("Req", "IDE-REFAC-042")]
    public Task F2OnAUnitNameRenamesFileHeadingAndUses() => TestUiThread.RunAsync(() => F2OnAUnitNameRenamesFileHeadingAndUsesOnUiThread());

    private async Task F2OnAUnitNameRenamesFileHeadingAndUsesOnUiThread()
    {
        await OpenFixtureAsync();
        var unit2 = await CaretOnAsync(Unit2, "Unit1");
        DialogRenames("Customers");
        PreviewAnswers(apply: true);

        await F2Async();

        var dialog = Assert.Single(_dialogs);
        Assert.True(dialog.IsUnitMode);
        Assert.Equal(("Rename Unit", false), (dialog.Title, dialog.ShowOverloads));
        Assert.Equal("The file is renamed to Customers.pas.", dialog.FileNote);
        Assert.Equal("5 changes in 4 files", dialog.Summary);
        var preview = Assert.Single(_previews);
        Assert.True(preview.IsConfirm);
        Assert.Equal("The file Unit1.pas is renamed to Customers.pas. Open documents among the changed files are saved.", preview.Note);

        // The Project Manager's rename: file, heading, uses and qualifier; Unit2 edited in its buffer and saved.
        Assert.False(_fs.FileExists(Unit1));
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit1), Text(Customers));
        Assert.Equal(RenameFixture.Renamed(RenameFixture.App), Text(Dir + @"\App.pas"));
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit3), Text(Unit3));
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit2), unit2.Buffer.Text);
        Assert.False(unit2.IsModified);
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit2), Text(Unit2));
        Assert.Contains("Customers.pas", Text(ProjectPath), StringComparison.Ordinal);
        Assert.Equal(["App.pas", "Customers.pas", "Unit2.pas", "Unit3.pas"], _services.Projects.Project!.Files.Select(f => f.Path));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_RenameUnitRefsNote, "Unit1.pas", "Customers.pas", 4, 3), _services.Status.Message);
        // Nothing recorded by the engine; no other message box.
        Assert.Equal(0, HistoryCount);
        Assert.False(Engine.IsBusy);
        Assert.Empty(Dialogs.Requests);
        await SettleAsync();
    }

    /// <summary>
    /// IDE-REFAC-058 (1): the confirm-mode preview lists every text change of the unit rename — the active document first —
    /// without check boxes; <c>Cancel</c> changes nothing anywhere.
    /// </summary>
    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-058")]
    public Task PreviewListsEveryEditAndCancelChangesNothing() => TestUiThread.RunAsync(() => PreviewListsEveryEditAndCancelChangesNothingOnUiThread());

    private async Task PreviewListsEveryEditAndCancelChangesNothingOnUiThread()
    {
        await OpenFixtureAsync();
        var unit1 = await CaretOnAsync(Unit1, "Unit1");
        DialogRenames("Customers");
        PreviewAnswers(apply: false);
        var version = unit1.Buffer.Version;

        await F2Async();

        var preview = Assert.Single(_previews);
        Assert.True(preview.IsConfirm);
        Assert.Equal([Unit1, Dir + @"\App.pas", Unit2, Unit3], preview.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        Assert.Equal([1, 2, 1, 1], preview.Files.Select(f => f.Count));
        Assert.Equal("Changes: 5, files: 4", preview.Summary);
        // Canceled: nothing changed, nothing recorded, no status text of the Project Manager.
        AssertFixtureUnchanged(RenameFixture.App);
        Assert.Equal((RenameFixture.Unit1, version, false), (unit1.Buffer.Text, unit1.Buffer.Version, unit1.IsModified));
        Assert.Equal(0, HistoryCount);
        Assert.Null(_services.Projects.PendingRename);
        Assert.Empty(Dialogs.Requests);
        await SettleAsync();
    }

    /// <summary>IDE-REFAC-056: a unit that is not a member of the project (reached through an <c>in</c> path) is refused before a name is asked.</summary>
    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-056")]
    public Task UnitOutsideTheProjectRefuses() => TestUiThread.RunAsync(() => UnitOutsideTheProjectRefusesOnUiThread());

    private async Task UnitOutsideTheProjectRefusesOnUiThread()
    {
        const string Shared = "unit Shared;\ninterface\nprocedure Hi;\nimplementation\nprocedure Hi;\nbegin\nend;\nend.\n";
        var app = RenameFixture.App.Replace("uses Unit1,", @"uses Shared in '..\Shared\Shared.pas', Unit1,", StringComparison.Ordinal);
        File(@"C:\p\Shared\Shared.pas", Shared);
        await OpenFixtureAsync(app);
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        await CaretOnAsync(Dir + @"\App.pas", "Shared");

        await F2Async();

        var refused = Assert.Single(Dialogs.Requests);
        Assert.Equal(("Msg.Refactoring.Refused", Strings.Refactoring_Rename, "Only units of the open project can be renamed."),
            (refused.MessageId, refused.Args[0], refused.Args[1]));
        Assert.Empty(_dialogs);
        Assert.Equal(Shared, Text(@"C:\p\Shared\Shared.pas"));
        AssertFixtureUnchanged(app);
    }

    /// <summary>IDE-REFAC-056: a unit whose heading is not named after its file, and a project with a pending file rename, are refused.</summary>
    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-056")]
    public Task HeadingThatDiffersFromTheFileRefuses() => TestUiThread.RunAsync(() => HeadingThatDiffersFromTheFileRefusesOnUiThread());

    private async Task HeadingThatDiffersFromTheFileRefusesOnUiThread()
    {
        const string Gadget = "unit Gadget;\ninterface\nprocedure Spin;\nimplementation\nprocedure Spin;\nbegin\nend;\nend.\n";
        var app = RenameFixture.App.Replace("uses Unit1,", "uses Gadget in 'Gadget1.pas', Unit1,", StringComparison.Ordinal);
        File(Dir + @"\Gadget1.pas", Gadget);
        var project = RenameFixture.ProjectFile.Replace("{ \"path\": \"Unit3.pas\" }", "{ \"path\": \"Unit3.pas\" }, { \"path\": \"Gadget1.pas\" }", StringComparison.Ordinal);
        await OpenFixtureAsync(app, project);
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        await CaretOnAsync(Dir + @"\Gadget1.pas", "Gadget");

        await F2Async();

        var refused = Assert.Single(Dialogs.Requests);
        Assert.Equal(("Msg.Refactoring.Refused", "The unit Gadget is not named after its file Gadget1.pas. Rename the file in the Project Manager."),
            (refused.MessageId, refused.Args[1]));
        Assert.Empty(_dialogs);
        Assert.Equal(Gadget, Text(Dir + @"\Gadget1.pas"));
        Assert.Equal(app, Text(Dir + @"\App.pas"));
    }

    /// <summary>IDE-REFAC-056: while another file rename of the project has not finished, a unit rename is refused.</summary>
    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-056")]
    public Task PendingRenameRefuses() => TestUiThread.RunAsync(() => PendingRenameRefusesOnUiThread());

    private async Task PendingRenameRefusesOnUiThread()
    {
        await OpenFixtureAsync();
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        await CaretOnAsync(Unit1, "Unit1");
        // The worker is held: F2's query is queued first, then the analysis of the Project Manager's rename of Unit3, which
        // is pending from now on. The worker answers in that order, so F2 finds the rename pending.
        var gate = new ManualResetEventSlim();
        var held = _services.CodeModel.Worker.RunAsync("test-gate", _ =>
        {
            gate.Wait();
            return true;
        });
        Task f2;
        Task<bool> other;
        try
        {
            f2 = F2Async();
            other = _services.Projects.RenameFileAsync("Unit3.pas", "Third.pas");
            Assert.NotNull(_services.Projects.PendingRename);
        }
        finally
        {
            gate.Set();
        }

        await f2;
        var refused = Assert.Single(Dialogs.Requests);
        Assert.Equal(("Msg.Refactoring.Refused", "Another file rename of this project has not finished."), (refused.MessageId, refused.Args[1]));
        Assert.Empty(_dialogs);
        Assert.True(await other);
        await held;
        Assert.True(_fs.FileExists(Dir + @"\Third.pas"));
        Assert.True(_fs.FileExists(Unit1));
    }

    /// <summary>
    /// UI-DLG-382 (unit mode), IDE-REFAC-057: the name of an existing project unit shows the Project Manager's message; a unit
    /// name the project does not have but the library does shows the language service's sentence; <c>Rename</c> stays disabled.
    /// </summary>
    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-057")]
    [Trait("Req", "UI-DLG-382")]
    public Task ExistingUnitNameIsShownInTheDialog() => TestUiThread.RunAsync(() => ExistingUnitNameIsShownInTheDialogOnUiThread());

    private async Task ExistingUnitNameIsShownInTheDialogOnUiThread()
    {
        await OpenFixtureAsync();
        await CaretOnAsync(Unit1, "Unit1");
        Dialogs.Script(RenameSymbolViewModel.DialogName, async dialog =>
        {
            var model = Assert.IsType<RenameSymbolViewModel>(dialog);
            _dialogs.Add(model);

            model.NewName = "Unit3";
            _clock.Advance(RenameSymbolViewModel.CheckDelay);
            await model.Checking;
            Assert.Equal("A file named Unit3.pas already exists in this folder.", model.Error);
            Assert.Null(model.FileNote);
            Assert.False(model.Rename.IsEnabled);

            model.NewName = "System";
            _clock.Advance(RenameSymbolViewModel.CheckDelay);
            await model.Checking;
            Assert.Equal("A unit named 'System' already exists.", model.Error);
            Assert.Equal("The file is renamed to System.pas.", model.FileNote);
            Assert.False(model.Rename.IsEnabled);
            await model.PressCancelAsync();
        });

        await F2Async();

        Assert.Single(_dialogs);
        Assert.Empty(_previews);
        AssertFixtureUnchanged(RenameFixture.App);
        await SettleAsync();
    }
}
