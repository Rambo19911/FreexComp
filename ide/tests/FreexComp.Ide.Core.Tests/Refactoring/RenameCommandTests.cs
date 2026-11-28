using System.Text;
using FreexComp.Compiler.Text;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// Rename Symbol, the core (I-379, F13-001; IDE-REFAC-040…046): the refactoring finds the target with the language
/// service, refuses what cannot be renamed before a name is asked, routes, and the plan's workspace edit is applied by
/// the engine as one operation — open and closed files, unsaved buffers, forms open in the designer. The command
/// <c>refactor.rename</c> is still a stub (I-380): the tests run the refactoring through <see cref="IRefactoringService.RunAsync"/>
/// with a name entry that plans one name and answers it.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class RenameCommandTests : RefactoringTestBase
{
    private const string AForm = Dir + @"\A.form";
    private const string Lib = @"G:\ui\src\Lib.pas";

    private const string RenameProject = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "A.pas", "form": "A.form" }, { "path": "B.pas" }, { "path": "C.pas" }, { "path": "R.pas" } ] }
        """;

    /// <summary>A unit with a class field and a routine with a local (the routing cases).</summary>
    private const string UnitR = "unit R;\n\ninterface\n\ntype\n  TR = class\n    FValue: Integer;\n  end;\n\nimplementation\n\n"
        + "procedure Go;\nvar\n  Value: Integer;\nbegin\n  Value := 1;\n  Value := Value + 1;\nend;\n\nend.\n";

    private const string Main = "program App;\n\nuses A, B, C;\n\nbegin\n  Run(nil);\n  Use(nil);\nend.\n";

    private const string UnitA = "unit A;\n\ninterface\n\ntype\n  TMainForm = class\n  published\n    procedure Old(Sender: TObject);\n"
        + "    procedure Other(Sender: TObject);\n  end;\n\nimplementation\n\n{$FORM A.form}\n\nprocedure TMainForm.Old(Sender: TObject);\nbegin\nend;\n\n"
        + "procedure TMainForm.Other(Sender: TObject);\nbegin\nend;\n\nend.\n";

    private const string UnitB = "unit B;\n\ninterface\n\nuses A;\n\nprocedure Run(F: TMainForm);\n\nimplementation\n\n"
        + "procedure Run(F: TMainForm);\nbegin\n  F.Old(nil);\nend;\n\nend.\n";

    private const string UnitC = "unit C;\n\ninterface\n\nuses A, Lib;\n\nprocedure Use(F: TMainForm);\n\nimplementation\n\n"
        + "procedure Use(F: TMainForm);\nvar\n  Count: Integer;\nbegin\n  Count := 1;\n  F.Old(nil);\n  LibProc;\nend;\n\nend.\n";

    private const string FormA = "object MainForm: TMainForm\n  OnShow = Old\n  object Button1: TButton\n    OnClick = Old\n  end\nend\n";

    private const string LibUnit = "unit Lib;\n\ninterface\n\nprocedure LibProc;\n\nimplementation\n\nprocedure LibProc;\nbegin\nend;\n\nend.\n";

    private readonly List<RenameInfo> _asked = [];
    private readonly List<RenamePlan> _plans = [];
    private readonly List<RefactoringPreviewViewModel> _previews = [];

    /// <summary>
    /// The refactoring with a name entry that plans <paramref name="newName"/> on the snapshot it is given and answers it
    /// (null: the entry is cancelled).
    /// </summary>
    private RenameRefactoring Renamer(string? newName = "Renamed") => new(Engine, _services.RenamePlanner, _services.CodeModel, _services.Projects,
        _services.LibrarySource, _services.Toolchain, async (info, source, position) =>
        {
            _asked.Add(info);
            if (newName is null)
                return null;
            var plan = await _services.RenamePlanner.PlanAsync(new RenameRequest(source, position, info.CurrentName, newName, false, true), CancellationToken.None);
            _plans.Add(plan);
            return new RenameDialogResult(newName, false, plan);
        });

    /// <summary>The project of the rename tests: A (with the closed form A.form), B, C and the library unit <c>Lib</c>; nothing open.</summary>
    private async Task OpenRenameProjectAsync(string unitC = UnitC)
    {
        WriteToolchain();
        File(Lib, LibUnit);
        _services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, @"G:\ui");
        File(ProjectPath, RenameProject);
        File(Dir + @"\App.pas", Main);
        File(A, UnitA);
        File(B, UnitB);
        File(C, unitC);
        File(Dir + @"\R.pas", UnitR);
        File(AForm, FormA);
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        await _services.ProjectManager.MissingFileChecks;
    }

    /// <summary>The Preview dialog applies what it shows; its diff work is awaited by <see cref="SettleAsync"/> (I-210).</summary>
    private void PreviewApplies() => Dialogs.Script(RefactoringPreviewViewModel.DialogName, dialog =>
    {
        _previews.Add(Assert.IsType<RefactoringPreviewViewModel>(dialog));
        return dialog.PressDefaultAsync();
    });

    /// <summary>The late work of every dialog and request the test started ends before the test does (I-210).</summary>
    private async Task SettleAsync()
    {
        foreach (var preview in _previews)
        {
            await preview.DiffUpdate;
            if (preview.Diff is { } diff)
                await diff.Recomputing;
        }
    }

    /// <summary>The caret on the <paramref name="occurrence"/>-th <paramref name="text"/> of <paramref name="document"/> (inside it, after one character).</summary>
    private static void Caret(TextDocumentViewModel document, string text, int occurrence = 1)
    {
        var at = -1;
        for (var i = 0; i < occurrence; i++)
            at = document.Buffer.Text.IndexOf(text, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, text);
        document.Selection = TextSelection.At(at + 1);
    }

    private static string Replaced(string text, string from, string to) => text.Replace(from, to, StringComparison.Ordinal);

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-043")]
    [Trait("Req", "IDE-REFAC-044")]
    [Trait("Req", "IDE-REFAC-045")]
    public Task RenamesOpenAndClosedFilesAndForm() => TestUiThread.RunAsync(() => RenamesOpenAndClosedFilesAndFormOnUiThread());

    private async Task RenamesOpenAndClosedFilesAndFormOnUiThread()
    {
        await OpenRenameProjectAsync();
        PreviewApplies();
        var b = await OpenAsync(B);
        Caret(b, "Old");

        var result = await Engine.RunAsync(Renamer());

        // One operation: B's buffer edited and not saved; A, C and the form rewritten; nothing else.
        Assert.Equal((RefactoringOutcome.Applied, 6, 4), (result.Outcome, result.EditCount, result.FileCount));
        Assert.Equal(Renamed(UnitB), b.Buffer.Text);
        Assert.True(b.IsModified);
        Assert.Equal(UnitB, Text(B));
        Assert.Equal((Renamed(UnitA), Renamed(UnitC), Renamed(FormA)), (Text(A), Text(C), Text(AForm)));
        Assert.Equal(Main, Text(Dir + @"\App.pas"));
        Assert.Equal(1, HistoryCount);
        Assert.Equal("Rename 'Old' to 'Renamed'", Engine.UndoTitle);
        var info = Assert.Single(_asked);
        Assert.Equal(("Old", RenameScope.Project), (info.CurrentName, info.Scope));
        var plan = Assert.Single(_plans);
        Assert.Equal((6, 4, "Rename 'Old' to 'Renamed'"), (plan.EditCount, plan.FileCount, plan.Edit!.Title));
        var preview = Assert.Single(_previews); // changes outside the active document: previewed (PreviewMode.Auto)
        Assert.Equal([B, AForm, A, C], preview.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase); // the active document first
        Assert.Empty(Dialogs.Requests);
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-045")]
    public Task ConflictChangesNothing() => TestUiThread.RunAsync(() => ConflictChangesNothingOnUiThread());

    private async Task ConflictChangesNothingOnUiThread()
    {
        await OpenRenameProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        var c = await OpenAsync(C);
        var b = await OpenAsync(B); // the active document
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "// unsaved\n")]);
        Caret(b, "Old");
        var before = Snapshot();
        var versions = (b.Buffer.Version, c.Buffer.Version);

        // The method renamed to the name of another method of its class: a conflict, found before anything is applied.
        var result = await Engine.RunAsync(Renamer("Other"));

        Assert.Equal(RefactoringOutcome.Failed, result.Outcome);
        var plan = Assert.Single(_plans);
        Assert.False(plan.Result.Success);
        Assert.Null(plan.Edit);
        Assert.NotEmpty(plan.Result.Conflicts);
        Assert.Equal(plan.Result.Error, result.Error);
        var refused = Assert.Single(Dialogs.Requests);
        Assert.Equal(("Msg.Refactoring.Refused", Strings.Refactoring_Rename, plan.Result.Error), (refused.MessageId, refused.Args[0], refused.Args[1]));
        // Card acceptance 3: every buffer version and every file byte unchanged, nothing on the history.
        Assert.Equal(before, Snapshot());
        Assert.Equal(versions, (b.Buffer.Version, c.Buffer.Version));
        Assert.Equal(0, HistoryCount);
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-044")]
    public Task UndoRestoresEveryFileByteIdentical() => TestUiThread.RunAsync(() => UndoRestoresEveryFileByteIdenticalOnUiThread());

    private async Task UndoRestoresEveryFileByteIdenticalOnUiThread()
    {
        await OpenRenameProjectAsync();
        Write(C, [.. Utf8Bom, .. Encoding.UTF8.GetBytes(UnitC.Replace("\n", "\r\n", StringComparison.Ordinal))]); // BOM and CRLF are kept
        PreviewApplies();
        var b = await OpenAsync(B);
        Caret(b, "Old");
        var before = Snapshot();
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.RunAsync(Renamer())).Outcome);
        Assert.Equal([.. Utf8Bom, .. Encoding.UTF8.GetBytes(Renamed(UnitC).Replace("\n", "\r\n", StringComparison.Ordinal))], Bytes(C));

        // refactor.undo takes back the whole operation: every file byte for byte, B's buffer to its text (still unsaved-free).
        var undone = await Engine.UndoAsync();

        Assert.Equal((RefactoringOutcome.Applied, 4), (undone.Outcome, undone.FileCount));
        Assert.Equal(UnitB, b.Buffer.Text);
        Assert.False(b.IsModified);
        Assert.Equal(before.Split("\n--\n")[0], Snapshot().Split("\n--\n")[0]);
        Assert.False(Engine.CanUndo);
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-044")]
    public Task EditorUndoRightAfterTakesBackEveryFile() => TestUiThread.RunAsync(() => EditorUndoRightAfterTakesBackEveryFileOnUiThread());

    private async Task EditorUndoRightAfterTakesBackEveryFileOnUiThread()
    {
        await OpenRenameProjectAsync();
        PreviewApplies();
        var b = await OpenAsync(B);
        var c = await OpenAsync(C);
        Caret(c, "Old");
        var files = FilesSnapshot();
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.RunAsync(Renamer())).Outcome);
        Assert.Equal((Renamed(UnitB), Renamed(UnitC)), (b.Buffer.Text, c.Buffer.Text));

        // Ctrl+Z right after, in B (an edited document that is not the active one): every file comes back.
        Assert.True(((ICommandTarget)b).CanExecute("edit.undo", null));
        Assert.True(await ((ICommandTarget)b).ExecuteAsync("edit.undo", null));

        Assert.Equal((UnitB, UnitC), (b.Buffer.Text, c.Buffer.Text));
        Assert.False(b.IsModified || c.IsModified);
        Assert.Equal(files, FilesSnapshot());
        Assert.False(Engine.CanUndo);
        Assert.Equal(Format(Strings.Msg_Refactoring_UndoneMany, "Rename 'Old' to 'Renamed'", 4), _services.Status.Message);
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-041")]
    [Trait("Req", "IDE-REFAC-045")]
    public Task UnsavedBufferOfAnotherUnitIsPartOfTheRename() => TestUiThread.RunAsync(() => UnsavedBufferOfAnotherUnitIsPartOfTheRenameOnUiThread());

    private async Task UnsavedBufferOfAnotherUnitIsPartOfTheRenameOnUiThread()
    {
        await OpenRenameProjectAsync();
        PreviewApplies();
        // C is open with an unsaved second call of the method; the rename starts in B.
        var c = await OpenAsync(C);
        c.Buffer.ApplyEdits([new TextEdit(c.Buffer.Text.IndexOf("  LibProc;", StringComparison.Ordinal), 0, "  F.Old(F);\n")]);
        var cText = c.Buffer.Text;
        var b = await OpenAsync(B);
        Caret(b, "Old");

        var result = await Engine.RunAsync(Renamer());

        Assert.Equal((RefactoringOutcome.Applied, 7, 4), (result.Outcome, result.EditCount, result.FileCount));
        Assert.Equal(Renamed(cText), c.Buffer.Text); // both calls, the unsaved one included
        Assert.Equal(UnitC, Text(C));              // and the file is not saved
        Assert.True(c.IsModified);
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-045")]
    public Task ReadOnlyReferencingFileBlocksEverything() => TestUiThread.RunAsync(() => ReadOnlyReferencingFileBlocksEverythingOnUiThread());

    private async Task ReadOnlyReferencingFileBlocksEverythingOnUiThread()
    {
        await OpenRenameProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        _fs.SetReadOnly(C, true);
        var b = await OpenAsync(B);
        Caret(b, "Old");
        var before = Snapshot();

        var result = await Engine.RunAsync(Renamer());

        Assert.Equal(RefactoringOutcome.Blocked, result.Outcome);
        Assert.Equal([new BlockedFile(C, BlockReason.NotWritable)], result.Blocked);
        var blocked = Assert.Single(Dialogs.Requests);
        Assert.Equal(("Msg.Refactoring.Blocked", "Rename 'Old' to 'Renamed'"), (blocked.MessageId, blocked.Args[0]));
        Assert.Equal(before, Snapshot()); // nothing anywhere: not B's buffer, not A, not the form
        Assert.Empty(Dialogs.Dialogs);   // blocked before a preview
        Assert.Equal(0, HistoryCount);
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-041")]
    public Task LibrarySymbolIsRefusedBeforeANameIsAsked() => TestUiThread.RunAsync(() => LibrarySymbolIsRefusedBeforeANameIsAskedOnUiThread());

    private async Task LibrarySymbolIsRefusedBeforeANameIsAskedOnUiThread()
    {
        await OpenRenameProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        Assert.True(_services.LibrarySource.IsLibrarySource(Lib));
        var c = await OpenAsync(C);
        Caret(c, "LibProc");
        var before = Snapshot();

        var result = await Engine.RunAsync(Renamer());

        var expected = Format(Strings.Reason_Rename_Library, "LibProc", "Lib.pas");
        Assert.Equal((RefactoringOutcome.Failed, expected), (result.Outcome, result.Error));
        Assert.Equal("LibProc is declared in the library source Lib.pas. Library sources cannot be changed.", expected);
        Assert.Empty(_asked); // no name was asked
        var refused = Assert.Single(Dialogs.Requests);
        Assert.Equal(("Msg.Refactoring.Refused", Strings.Refactoring_Rename, expected), (refused.MessageId, refused.Args[0], refused.Args[1]));
        Assert.Equal(before, Snapshot());
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-041")]
    public Task NoProjectOrNoLibraryRefuses() => TestUiThread.RunAsync(() => NoProjectOrNoLibraryRefusesOnUiThread());

    private async Task NoProjectOrNoLibraryRefusesOnUiThread()
    {
        await OpenRenameProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        var b = await OpenAsync(B);
        Caret(b, "Old");

        // The run-time library sources are not found: refused before the language service is asked.
        _services.Settings.Set(SettingsCatalog.ToolchainRtlDir, "");
        Assert.Null(_services.Toolchain.Resolve().RtlDir);
        var requests = _services.CodeModel.Worker.RequestCount;
        var noLibrary = await Engine.RunAsync(Renamer());
        Assert.Equal((RefactoringOutcome.Failed, Strings.Reason_Rename_NoLibrary), (noLibrary.Outcome, noLibrary.Error));
        Assert.Equal(requests, _services.CodeModel.Worker.RequestCount);
        _services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\rtl");

        // No project: a file outside a project has no language-service document.
        File(@"C:\loose\Loose.pas", "unit Loose;\n\ninterface\n\nprocedure Old;\n\nimplementation\n\nprocedure Old;\nbegin\nend;\n\nend.\n");
        Assert.True(await _services.Projects.CloseAsync());
        var loose = await OpenAsync(@"C:\loose\Loose.pas");
        Caret(loose, "Old");
        var noProject = await Engine.RunAsync(Renamer());
        Assert.Equal((RefactoringOutcome.Failed, Strings.Reason_Rename_NoProject), (noProject.Outcome, noProject.Error));

        Assert.Equal([Strings.Reason_Rename_NoLibrary, Strings.Reason_Rename_NoProject], Dialogs.Requests.Select(r => r.Args[1]));
        Assert.Empty(_asked);
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-041")]
    public Task NotRenamableShowsTheServicesReason() => TestUiThread.RunAsync(() => NotRenamableShowsTheServicesReasonOnUiThread());

    private async Task NotRenamableShowsTheServicesReasonOnUiThread()
    {
        await OpenRenameProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        var c = await OpenAsync(C);
        Caret(c, "Integer"); // a built-in type
        Assert.NotNull(_services.CodeModel.Synchronize());
        var document = _services.CodeModel.Workspace.GetDocument(C);
        var info = await _services.RenamePlanner.GetInfoAsync(document, c.Selection.Start, CancellationToken.None);
        Assert.False(info.CanRename);

        var result = await Engine.RunAsync(Renamer());

        Assert.Equal((RefactoringOutcome.Failed, info.Reason), (result.Outcome, result.Error));
        Assert.False(string.IsNullOrEmpty(info.Reason));
        Assert.Equal(info.Reason, Assert.Single(Dialogs.Requests).Args[1]);
        Assert.Empty(_asked);
        await SettleAsync();
    }

    [Theory(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-040")]
    [InlineData("na|me", true)]
    [InlineData("|&begin", true)]
    [InlineData("&be|gin", true)]
    [InlineData("name|", true)]
    [InlineData("name |", false)]
    [InlineData("be|gin", false)]
    [InlineData("{ na|me }", false)]
    [InlineData("// na|me", false)]
    [InlineData("'na|me'", false)]
    [InlineData("1|23", false)]
    [InlineData("[name]", true)]
    [InlineData("[na]me", true)]
    [InlineData("[name ]", false)]
    [InlineData("[name\n  other]", false)]
    public Task IsApplicableOnlyOnAName(string marked, bool applicable) => TestUiThread.RunAsync(() => IsApplicableOnlyOnANameOnUiThread(marked, applicable));

    /// <summary><paramref name="marked"/>: a caret <c>|</c> or a selection <c>[…]</c> in a statement of a procedure.</summary>
    private async Task IsApplicableOnlyOnANameOnUiThread(string marked, bool applicable)
    {
        await OpenRenameProjectAsync();
        var b = await OpenAsync(B);
        var line = marked.Replace("|", "", StringComparison.Ordinal).Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);
        var at = b.Buffer.Text.IndexOf("  F.Old(nil);", StringComparison.Ordinal);
        b.Buffer.ApplyEdits([new TextEdit(at, 0, "  " + line + "\n")]);
        var start = at + 2;
        b.Selection = marked.Contains('|', StringComparison.Ordinal)
            ? TextSelection.At(start + marked.IndexOf('|', StringComparison.Ordinal))
            : new TextSelection(start + marked.IndexOf('[', StringComparison.Ordinal), start + marked.IndexOf(']', StringComparison.Ordinal) - 1);
        var requests = _services.CodeModel.Worker.RequestCount;
        var reads = _recording.Calls.Count;

        Assert.Equal(applicable, Renamer().IsApplicable(new RefactoringContext(b, b.Selection, null)));
        Assert.Equal(requests, _services.CodeModel.Worker.RequestCount); // no language-service query
        Assert.Equal(reads, _recording.Calls.Count);                      // no I/O
        await SettleAsync();
    }

    [Theory(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-042")]
    [InlineData("unit", RenameRoute.Unit)]
    [InlineData("local", RenameRoute.Inline)]
    [InlineData("local&", RenameRoute.Dialog)]
    [InlineData("localBlock", RenameRoute.Dialog)]
    [InlineData("field", RenameRoute.Dialog)]
    public Task RoutingByTarget(string target, RenameRoute route) => TestUiThread.RunAsync(() => RoutingByTargetOnUiThread(target, route));

    private async Task RoutingByTargetOnUiThread(string target, RenameRoute route)
    {
        var text = target == "local&" ? UnitR.Replace("  Value := Value + 1;\n", "  &Value := Value + 1;\n", StringComparison.Ordinal) : UnitR;
        await OpenRenameProjectAsync();
        File(Dir + @"\R.pas", text);
        var r = await OpenAsync(Dir + @"\R.pas");
        var at = target switch
        {
            "unit" => text.IndexOf("R;", StringComparison.Ordinal),
            "field" => text.IndexOf("FValue", StringComparison.Ordinal) + 1,
            _ => text.IndexOf("Value := 1", StringComparison.Ordinal) + 1,
        };
        Assert.NotNull(_services.CodeModel.Synchronize());
        var info = await _services.RenamePlanner.GetInfoAsync(_services.CodeModel.Workspace.GetDocument(r.FilePath!), at, CancellationToken.None);
        Assert.True(info.CanRename, info.Reason);

        Assert.Equal(route, RenameRefactoring.Route(info, r.Buffer.Text, blockOrMultiCaret: target == "localBlock"));
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-046")]
    public Task FormThatCannotBeScannedFailsThePlan() => TestUiThread.RunAsync(() => FormThatCannotBeScannedFailsThePlanOnUiThread());

    private async Task FormThatCannotBeScannedFailsThePlanOnUiThread()
    {
        // A closed form that is not UTF-8: the plan fails with the service's sentence, nothing changes.
        await OpenRenameProjectAsync();
        Write(AForm, [.. Encoding.UTF8.GetBytes("object MainForm: TMainForm\n  Caption = '"), 0xFF, 0xFE, .. Encoding.UTF8.GetBytes("'\nend\n")]);
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        var b = await OpenAsync(B);
        Caret(b, "Old");
        var before = Snapshot();

        var result = await Engine.RunAsync(Renamer());

        var plan = Assert.Single(_plans);
        Assert.False(plan.Result.Success);
        Assert.Empty(plan.Result.Conflicts);
        Assert.Contains("A.form", plan.Result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((RefactoringOutcome.Failed, plan.Result.Error), (result.Outcome, result.Error));
        Assert.Equal(before, Snapshot());
        await SettleAsync();
    }

    // ---- forms open in the designer (the form unit of the designer tests, MainForm.pas + MainForm.form) ----

    private const string Caller = "unit B;\n\ninterface\n\nuses MainForm;\n\nprocedure Run(F: TMainForm);\n\nimplementation\n\n"
        + "procedure Run(F: TMainForm);\nbegin\n  F.GreetButtonClick(nil);\nend;\n\nend.\n";

    private const string DesignProject = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "B.pas" }, { "path": "MainForm.pas", "form": "MainForm.form" } ] }
        """;

    private static readonly (string Name, string Uses, string Types)[] Stubs =
    [
        ("Classes", "", "{$M+}\n  TPersistent = class end;\n  {$M-}\n  TComponent = class(TPersistent) end;"),
        ("Controls", "uses Classes;\n\n", "TControl = class(TComponent) end;"),
        ("Forms", "uses Classes, Controls;\n\n", "TForm = class(TControl) end;"),
        ("StdCtrls", "uses Classes, Controls;\n\n", "TEdit = class(TControl) end;\n  TButton = class(TControl) end;\n  TLabel = class(TControl) end;"),
        ("ExtCtrls", "uses Classes, Controls;\n\n", "TPanel = class(TControl) end;"),
    ];

    /// <summary>The designer's form unit and form, B calling its handler; the form open in the Design view.</summary>
    private async Task<FormDocumentViewModel> OpenDesignedFormAsync(string? formFile = null)
    {
        WriteToolchain();
        ((DocumentService)_services.Documents).DesignCatalog = CodeSyncTests.Catalog;
        File(ProjectPath, DesignProject);
        File(Dir + @"\App.pas", "program App;\n\nuses B, MainForm;\n\nbegin\n  Run(nil);\nend.\n");
        File(B, Caller);
        File(FormUnitPath, DesignerFixture.Unit);
        File(FormPath, formFile ?? Form);
        // The units the form unit uses, as small library units (the designer reads its catalog, the compiler these).
        _services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, @"G:\ui");
        foreach (var (name, uses, types) in Stubs)
            File(@"G:\ui\src\" + name + ".pas", $"unit {name};\n\ninterface\n\n{uses}type\n  {types}\n\nimplementation\n\nend.\n");
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        await _services.ProjectManager.MissingFileChecks;
        return await OpenFormAsync();
    }

    /// <summary>B open and active, the caret on its call of <c>GreetButtonClick</c>.</summary>
    private async Task OpenCallerAsync()
    {
        var b = await OpenAsync(B);
        Caret(b, "GreetButtonClick");
    }

    /// <summary>Renames <c>GreetButtonClick</c> to <c>GreetClick</c> at the caret of the active document.</summary>
    private Task<RefactoringResult> RenameHandlerAsync() => Engine.RunAsync(Renamer("GreetClick"));

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-046")]
    public Task OpenFormWithCrLfFileIsRenamedThroughItsFormText() => TestUiThread.RunAsync(() => OpenFormWithCrLfFileIsRenamedThroughItsFormTextOnUiThread());

    private async Task OpenFormWithCrLfFileIsRenamedThroughItsFormTextOnUiThread()
    {
        // The file has CRLF; the designer's form text (the writer's layout) has LF: the service is given the form text.
        var crlf = Form.Replace("\n", "\r\n", StringComparison.Ordinal);
        var document = await OpenDesignedFormAsync(crlf);
        Assert.NotEqual(crlf, document.Designer.CurrentFormText());
        PreviewApplies();
        await OpenCallerAsync();
        var files = FilesSnapshot();

        var result = await RenameHandlerAsync();

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Equal("GreetClick", OnClick(document));
        Assert.Equal(WithHandler(Form, "GreetClick"), document.Designer.CurrentFormText());
        Assert.Contains("procedure GreetClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(files, FilesSnapshot()); // open documents are edited, not saved: no file changes
        Assert.Equal(crlf, Text(FormPath));
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-046")]
    public Task UnsavedDesignerChangeIsRenamed() => TestUiThread.RunAsync(() => UnsavedDesignerChangeIsRenamedOnUiThread());

    private async Task UnsavedDesignerChangeIsRenamedOnUiThread()
    {
        var document = await OpenDesignedFormAsync();
        SetCaption(document, "Hello");
        Assert.True(document.IsModified);
        PreviewApplies();
        await OpenCallerAsync();

        var result = await RenameHandlerAsync();

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Equal("GreetClick", OnClick(document));
        var text = document.Designer.CurrentFormText();
        Assert.Contains("Caption = 'Hello'", text, StringComparison.Ordinal); // the unsaved change is kept
        Assert.Contains("OnClick = GreetClick", text, StringComparison.Ordinal);
        Assert.Equal(Form, Text(FormPath));
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-046")]
    public Task FormThatCannotBeScannedInTheTextViewFailsThePlan() => TestUiThread.RunAsync(() => FormThatCannotBeScannedInTheTextViewFailsThePlanOnUiThread());

    private async Task FormThatCannotBeScannedInTheTextViewFailsThePlanOnUiThread()
    {
        // The Text view of the open form holds a text the scanner rejects: the plan fails, nothing changes.
        var document = await OpenDesignedFormAsync();
        Assert.True(document.ShowView(DocumentView.Text));
        document.FormBuffer.ApplyEdits([new TextEdit(0, "object".Length, "objekt")]);
        Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        await OpenCallerAsync();
        var before = Snapshot();
        var formText = document.FormBuffer.Text;

        var result = await RenameHandlerAsync();

        var plan = Assert.Single(_plans);
        Assert.False(plan.Result.Success);
        Assert.Equal((RefactoringOutcome.Failed, plan.Result.Error), (result.Outcome, result.Error));
        Assert.Equal(before, Snapshot());
        Assert.Equal(formText, document.FormBuffer.Text);
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-046")]
    public Task MethodRenameWithOpenDesignerKeepsCodeSyncOk() => TestUiThread.RunAsync(() => MethodRenameWithOpenDesignerKeepsCodeSyncOkOnUiThread());

    private async Task MethodRenameWithOpenDesignerKeepsCodeSyncOkOnUiThread()
    {
        var document = await OpenDesignedFormAsync();
        var designer = document.Designer;
        Assert.Equal(CodeSyncState.Ok, designer.CodeSync.State);
        var unitBefore = document.Buffer.Text;

        // From the form's own Code view: the declaration, the body heading and the handler value in one operation.
        Assert.True(document.ShowView(DocumentView.Code));
        Caret(document, "GreetButtonClick");
        PreviewApplies();
        var result = await Engine.RunAsync(Renamer("GreetClick"));

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        // Code sync is Ok, the designer shows the new name, the unit has exactly the rename (no repair edit), and the form
        // text exactly the new handler value (no preserved block).
        Assert.Equal(CodeSyncState.Ok, designer.CodeSync.State);
        Assert.Equal(Replaced(unitBefore, "GreetButtonClick", "GreetClick"), document.Buffer.Text);
        Assert.Equal(WithHandler(Form, "GreetClick"), designer.CurrentFormText());
        Assert.Equal("GreetClick", OnClick(document));
        Assert.Empty(designer.CodeSync.RepairBeforeEdit());
        Assert.Equal(Replaced(Caller, "GreetButtonClick", "GreetClick"), Text(B)); // the closed caller is rewritten
        await SettleAsync();
    }

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-046")]
    [Trait("Req", "IDE-REFAC-063")]
    public Task FormClassRenameKeepsCodeSyncOk() => TestUiThread.RunAsync(async () =>
    {
        // The form class (I-386): the unit buffer, the form text of the open designer and the closed caller in one operation.
        var document = await OpenDesignedFormAsync();
        var designer = document.Designer;
        Assert.Equal(CodeSyncState.Ok, designer.CodeSync.State);
        var unitBefore = document.Buffer.Text;
        var formBefore = designer.CurrentFormText();
        Assert.True(document.ShowView(DocumentView.Code));
        Caret(document, "TMainForm = class");
        PreviewApplies();

        var result = await Engine.RunAsync(Renamer("TMainWindow"));

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        var preview = Assert.Single(_previews);
        Assert.Equal(new[] { FormUnitPath, B, FormPath }.Order(StringComparer.OrdinalIgnoreCase), preview.Files.Select(f => f.Path).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        // Code sync is Ok, the unit has exactly the rename (no repair edit), the form text exactly the new root class (no
        // preserved block), the designer's model names the new class; the caller is rewritten; one operation.
        Assert.Equal(CodeSyncState.Ok, designer.CodeSync.State);
        Assert.Equal(Replaced(unitBefore, "TMainForm", "TMainWindow"), document.Buffer.Text);
        Assert.Equal(Replaced(formBefore, "MainForm: TMainForm", "MainForm: TMainWindow"), designer.CurrentFormText());
        Assert.Equal("TMainWindow", designer.Model!.Root.TypeName);
        Assert.Empty(designer.CodeSync.RepairBeforeEdit());
        Assert.Equal(Replaced(Caller, "TMainForm", "TMainWindow"), Text(B));
        Assert.Equal(1, HistoryCount);
        await SettleAsync();
    });
}
