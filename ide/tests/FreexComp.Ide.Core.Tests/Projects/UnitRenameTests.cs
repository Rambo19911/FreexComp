using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>
/// Unit rename through the language service (phase8-project-files.md §5, I-236): the heading, every <c>uses</c> entry and
/// the qualified names change in one all-or-nothing operation; conflicts and edits outside the project are refused; a
/// unit that cannot be analysed is renamed as in Phases 1–7.
/// </summary>
[Trait("Phase", "8")]
public sealed class UnitRenameTests
{
    private const string Dir = @"C:\p\App";
    private const string ProjectPath = Dir + @"\App.pproj";

    private static readonly FocusState ProjectManagerFocus = new(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);

    private readonly InMemoryFileSystem _fs = new();
    private readonly RecordingFileSystem _recording;
    private readonly ManualClock _clock = new();
    private readonly IdeServices _services;
    private int _busyChanges;

    public UnitRenameTests()
    {
        _recording = new RecordingFileSystem(_fs);
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _recording;
            o.Clock = _clock;
        });
        _services.Projects.PendingRenameChanged += (_, _) => _busyChanges++;
    }

    private ProjectManagerViewModel Pm => _services.ProjectManager;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private string Text(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    /// <summary>The toolchain under <c>G:\</c> with the real system unit, so that the project binds (the code model is available).</summary>
    private void WriteToolchain()
    {
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(System.IO.File.Exists))
            File(@"G:\rtl\" + Path.GetFileName(file), System.IO.File.ReadAllText(file));
        foreach (var tool in (string[])[@"G:\llvm\clang.exe", @"G:\llvm\lld-link.exe", @"G:\llvm\llvm-dlltool.exe"])
            File(tool, "");
        File(@"G:\fw\src\Placeholder.pas", "unit Placeholder;\ninterface\nimplementation\nend.\n");
        _services.Settings.Set(SettingsCatalog.ToolchainLlvmBinDir, @"G:\llvm");
        _services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\rtl");
        _services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, @"G:\fw");
    }

    /// <summary>The <see cref="RenameFixture"/> project at <see cref="Dir"/>, opened with the Project Manager focused.</summary>
    private async Task OpenFixtureAsync(string? projectFile = null, bool toolchain = true)
    {
        if (toolchain)
            WriteToolchain();
        File(ProjectPath, projectFile ?? RenameFixture.ProjectFile);
        File(Dir + @"\App.pas", RenameFixture.App);
        File(Dir + @"\Unit1.pas", RenameFixture.Unit1);
        File(Dir + @"\Unit2.pas", RenameFixture.Unit2);
        File(Dir + @"\Unit3.pas", RenameFixture.Unit3);
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync()); // the main source opened with the project
        _services.FocusTracker.Update(ProjectManagerFocus);
        await Pm.MissingFileChecks;
        _busyChanges = 0;
    }

    private async Task<TextDocumentViewModel> OpenAsync(string name) =>
        Assert.IsType<TextDocumentViewModel>((await _services.Documents.OpenAsync(Dir + @"\" + name)).Value);

    private static string Status(string resource, params object[] args) => string.Format(CultureInfo.CurrentCulture, resource, args);

    private void AssertFixtureUnchanged()
    {
        Assert.Equal(RenameFixture.App, Text(Dir + @"\App.pas"));
        Assert.Equal(RenameFixture.Unit1, Text(Dir + @"\Unit1.pas"));
        Assert.Equal(RenameFixture.Unit2, Text(Dir + @"\Unit2.pas"));
        Assert.Equal(RenameFixture.Unit3, Text(Dir + @"\Unit3.pas"));
        Assert.False(_fs.FileExists(Dir + @"\Customers.pas"));
        Assert.Equal(["App.pas", "Unit1.pas", "Unit2.pas", "Unit3.pas"], _services.Projects.Project!.Files.Select(f => f.Path).Take(4));
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-130")]
    [Trait("Req", "IDE-PM-131")]
    [Trait("Req", "IDE-PM-136")]
    [Trait("Req", "UI-TW-118")]
    public Task UpdatesHeadingUsesAndQualifiedNames() => TestUiThread.RunAsync(() => UpdatesHeadingUsesAndQualifiedNamesOnUiThread());

    private async Task UpdatesHeadingUsesAndQualifiedNamesOnUiThread()
    {
        // Unit1 has a companion form: the {$FORM} directive stays the IDE's edit (IDE-PM-131). Unit4 is used by nobody.
        const string FormUnit = "unit Unit1;\n\ninterface\n\n{$FORM Unit1.form}\n\nprocedure Hello;\n\nimplementation\n\nprocedure Hello;\nbegin\nend;\n\nend.\n";
        await OpenFixtureAsync("""
            { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
              "files": [ { "path": "App.pas" }, { "path": "Unit1.pas", "form": "Unit1.form" }, { "path": "Unit2.pas" }, { "path": "Unit3.pas" },
                         { "path": "Unit4.pas" } ] }
            """);
        File(Dir + @"\Unit1.pas", FormUnit);
        File(Dir + @"\Unit1.form", "object Unit1: TForm\nend\n");
        File(Dir + @"\Unit4.pas", "unit Unit4;\ninterface\nimplementation\nend.\n");

        // UI-TW-118: while the language service works the row is busy — typed name, the gestures off, and after 400 ms
        // the status bar says so.
        var busy = new List<(string? Name, bool CanRename, bool CanDrag, string? Status)>();
        var row = Pm.Find("Unit1.pas")!;
        Pm.Select(row);
        Assert.True(_services.Commands.CanExecute("project.renameFile"));
        _services.Projects.PendingRenameChanged += (_, _) =>
        {
            if (_services.Projects.PendingRename is null)
                return;
            _clock.Advance(TimeSpan.FromMilliseconds(400));
            busy.Add((row.BusyName, _services.Commands.CanExecute("project.renameFile") || _services.Commands.CanExecute("project.deleteFile")
                || _services.Commands.CanExecute("project.removeFile"), Pm.CanStartDrag(row), _services.Status.Message));
        };

        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));

        Assert.Equal([("Customers.pas", false, false, Strings.Status_UpdatingReferences)], busy);
        Assert.Null(_services.Projects.PendingRename);
        Assert.Null(Pm.Find("Customers.pas")!.BusyName);
        Assert.Equal(RenameFixture.Renamed(RenameFixture.App), Text(Dir + @"\App.pas"));
        Assert.Equal(RenameFixture.Renamed(FormUnit), Text(Dir + @"\Customers.pas"));
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit2), Text(Dir + @"\Unit2.pas"));
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit3), Text(Dir + @"\Unit3.pas"));
        Assert.False(_fs.FileExists(Dir + @"\Unit1.pas"));
        Assert.True(_fs.FileExists(Dir + @"\Customers.form"));
        Assert.Equal(new Models.ProjectFileItem("Customers.pas", "Customers.form", "unit"), _services.Projects.Project!.Files[1]);
        Assert.Equal(Status(Strings.Msg_Project_RenameUnitRefsNote, "Unit1.pas", "Customers.pas", 4, 3), _services.Status.Message);
        Assert.Empty(Dialogs.Requests);

        // IDE-PM-136: no reference outside the heading — the Phase 1 status.
        Assert.True(await _services.Projects.RenameFileAsync("Unit4.pas", "Lonely.pas"));
        Assert.StartsWith("unit Lonely;", Text(Dir + @"\Lonely.pas"), StringComparison.Ordinal);
        Assert.Equal(Status(Strings.Msg_Project_RenameUnitNote, "Unit4.pas", "Lonely.pas"), _services.Status.Message);

        // The renamed project is consistent for the code model: a second rename finds every reference again.
        Assert.True(await _services.Projects.RenameFileAsync("Customers.pas", "Unit1.pas"));
        Assert.Equal(RenameFixture.App, Text(Dir + @"\App.pas"));
        Assert.Equal(FormUnit, Text(Dir + @"\Unit1.pas"));
        Assert.Equal(RenameFixture.Unit3, Text(Dir + @"\Unit3.pas"));
    }

    /// <summary>
    /// I-256 (c), IDE-PM-136: the status names the counts grammatically — "1 reference in 1 file was updated",
    /// "{n} references in 1 file were updated", "{n} references in {m} files were updated".
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-136")]
    public Task StatusUsesSingularAndPlural() => TestUiThread.RunAsync(() => StatusUsesSingularAndPluralOnUiThread());

    private async Task StatusUsesSingularAndPluralOnUiThread()
    {
        await OpenFixtureAsync();
        // Unit3 is named twice in the program (uses + qualifier), Unit2 once, Unit1 in three files.
        File(Dir + @"\App.pas", RenameFixture.App.Replace("  Three;", "  Unit3.Three;", StringComparison.Ordinal));

        Assert.True(await _services.Projects.RenameFileAsync("Unit2.pas", "Second.pas"));
        Assert.Equal("Renamed Unit2.pas to Second.pas; 1 reference in 1 file was updated", _services.Status.Message);
        Assert.Equal(Status(Strings.Msg_Project_RenameUnitRefsNoteOne, "Unit2.pas", "Second.pas"), _services.Status.Message);

        Assert.True(await _services.Projects.RenameFileAsync("Unit3.pas", "Third.pas"));
        Assert.Equal("Renamed Unit3.pas to Third.pas; 2 references in 1 file were updated", _services.Status.Message);
        Assert.Equal(Status(Strings.Msg_Project_RenameUnitRefsNoteOneFile, "Unit3.pas", "Third.pas", 2), _services.Status.Message);

        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));
        Assert.Equal("Renamed Unit1.pas to Customers.pas; 4 references in 3 files were updated", _services.Status.Message);
        Assert.Equal(Status(Strings.Msg_Project_RenameUnitRefsNote, "Unit1.pas", "Customers.pas", 4, 3), _services.Status.Message);
        Assert.Empty(Dialogs.Requests);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-132")]
    public Task OpenBuffersGetOneUndoGroupAndAreSaved() => TestUiThread.RunAsync(() => OpenBuffersGetOneUndoGroupAndAreSavedOnUiThread());

    private async Task OpenBuffersGetOneUndoGroupAndAreSavedOnUiThread()
    {
        await OpenFixtureAsync();
        var app = await OpenAsync("App.pas");
        var unit1 = await OpenAsync("Unit1.pas");
        // An unsaved change is part of the compilation the edits are computed on: a second qualified call.
        var at = app.Buffer.Text.IndexOf("  Two;", StringComparison.Ordinal);
        app.Buffer.ApplyEdits([new TextEdit(at, 0, "  Unit1.Hello;\n")]);
        var typed = app.Buffer.Text;
        Assert.True(app.IsModified);

        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));

        // The open buffers hold the new text and are saved; the unit's document follows its file.
        Assert.Equal(RenameFixture.Renamed(typed), app.Buffer.Text);
        Assert.False(app.IsModified);
        Assert.Equal(app.Buffer.Text, Text(Dir + @"\App.pas"));
        Assert.Equal(Dir + @"\Customers.pas", unit1.FilePath);
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit1), unit1.Buffer.Text);
        Assert.False(unit1.IsModified);
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit2), Text(Dir + @"\Unit2.pas")); // not open: written directly
        Assert.Equal(Status(Strings.Msg_Project_RenameUnitRefsNote, "Unit1.pas", "Customers.pas", 5, 3), _services.Status.Message);

        // One undo group per buffer: a single undo gives the text before the rename.
        app.Buffer.Undo();
        Assert.Equal(typed, app.Buffer.Text);
        unit1.Buffer.Undo();
        Assert.Equal(RenameFixture.Unit1, unit1.Buffer.Text);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-132")]
    public Task FailureRestoresEveryEditedFile() => TestUiThread.RunAsync(() => FailureRestoresEveryEditedFileOnUiThread());

    private async Task FailureRestoresEveryEditedFileOnUiThread()
    {
        await OpenFixtureAsync();
        Dialogs.Answer("Msg.Project.RenameFailed", "OK");
        var project = Text(ProjectPath);
        var files = _fs.AllFiles;
        var app = await OpenAsync("App.pas");

        async Task FailsAsync(Func<string, bool> failWhen)
        {
            var calls = 0;
            _recording.FailWhen = call => failWhen(call) && calls++ == 0; // the write fails once: the restore can write
            var before = Dialogs.Requests.Count;
            Assert.False(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));
            _recording.FailWhen = null;
            Assert.True(calls >= 1, "the injected failure was hit");
            var request = Assert.Single(Dialogs.Requests.Skip(before));
            Assert.Equal("Msg.Project.RenameFailed", request.MessageId);
            Assert.Equal(Dir + @"\Unit1.pas", request.Args[0]);

            Assert.Equal(files, _fs.AllFiles);
            Assert.Equal(project, Text(ProjectPath));
            AssertFixtureUnchanged();
            Assert.Equal(RenameFixture.App, app.Buffer.Text);
            Assert.False(app.IsModified);
            Assert.Equal(Dir + @"\App.pas", app.FilePath);
            Assert.Null(_services.Projects.PendingRename);
        }

        // The last reference file (not open) cannot be written: the buffer of App.pas and the file Unit2.pas, both
        // already written, get their text back.
        await FailsAsync(call => call.StartsWith("WriteAllBytesDurable " + Dir + @"\Unit3.pas", StringComparison.Ordinal));

        // The unit's own text (the last step) fails: every reference file is restored.
        await FailsAsync(call => call.StartsWith("WriteAllBytesDurable " + Dir + @"\Customers.pas", StringComparison.Ordinal));

        // The open buffer cannot be saved: nothing else was edited yet; the buffer is as before.
        await FailsAsync(call => call.StartsWith("WriteAllBytesDurable " + Dir + @"\App.pas", StringComparison.Ordinal));

        // And without a failure the same rename goes through.
        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));
        Assert.Equal(RenameFixture.Renamed(RenameFixture.App), app.Buffer.Text);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-134")]
    public Task ConflictChangesNothing() => TestUiThread.RunAsync(() => ConflictChangesNothingOnUiThread());

    private async Task ConflictChangesNothingOnUiThread()
    {
        // A unit named Customers exists on the search path (not a project member, another folder): the validation of
        // IDE-PM-070 lets the name pass, the conflict check of the language service refuses it.
        await OpenFixtureAsync("""
            { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas", "searchPaths": ["lib"],
              "files": [ { "path": "App.pas" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" }, { "path": "Unit3.pas" } ] }
            """);
        File(Dir + @"\lib\Customers.pas", "unit Customers;\ninterface\nimplementation\nend.\n");
        Dialogs.Answer("Msg.Project.RenameConflict", "OK");
        var project = Text(ProjectPath);
        var files = _fs.AllFiles;
        var callsBefore = _recording.Calls.Count;

        // Started in the tree: the editor stays open with the typed name.
        Pm.Select(Pm.Find("Unit1.pas")!);
        Assert.True(Pm.BeginRename("Unit1.pas"));
        var row = Pm.Find("Unit1.pas")!;
        var editor = Assert.IsType<RenameFileViewModel>(row.Rename);
        editor.NewName = "Customers.pas";
        Assert.Null(editor.Error);
        Assert.False(await editor.CommitAsync());

        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Project.RenameConflict", request.MessageId);
        Assert.Equal(["Customers", "A unit named 'Customers' already exists."], request.Args);
        Assert.Same(editor, row.Rename);
        Assert.Equal("Customers.pas", editor.NewName);
        Assert.Null(row.BusyName);
        Assert.Equal(2, _busyChanges); // busy while the service worked, then not
        Assert.Equal(files, _fs.AllFiles);
        Assert.Equal(project, Text(ProjectPath));
        AssertFixtureUnchanged();
        Assert.DoesNotContain(_recording.Calls.Skip(callsBefore), c => c.StartsWith("MoveFile ", StringComparison.Ordinal) || c.StartsWith("WriteAllBytes", StringComparison.Ordinal));
        editor.Cancel();
        Assert.Null(row.Rename);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-133")]
    [Trait("Req", "IDE-LIB-008")]
    public Task EditOutsideProjectIsRefused() => TestUiThread.RunAsync(() => EditOutsideProjectIsRefusedOnUiThread());

    private async Task EditOutsideProjectIsRefusedOnUiThread()
    {
        await OpenFixtureAsync();
        Dialogs.Answer("Msg.Project.RenameConflict", "OK");
        var project = Text(ProjectPath);

        async Task RefusedAsync()
        {
            var before = Dialogs.Requests.Count;
            Assert.False(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));
            var request = Assert.Single(Dialogs.Requests.Skip(before));
            Assert.Equal("Msg.Project.RenameConflict", request.MessageId);
            Assert.Equal(["Customers", Strings.Reason_RenameOutsideProject], request.Args);
            Assert.Equal("The unit is used by files outside the project.", Strings.Reason_RenameOutsideProject);
            Assert.Equal(project, Text(ProjectPath));
            Assert.Equal(RenameFixture.Unit1, Text(Dir + @"\Unit1.pas"));
            Assert.Equal(RenameFixture.Unit2, Text(Dir + @"\Unit2.pas"));
            Assert.False(_fs.FileExists(Dir + @"\Customers.pas"));
        }

        // A unit outside the project directory and its search paths (reached through an "in" path) uses Unit1.
        const string Shared = "unit Shared;\ninterface\nuses Unit1;\nimplementation\nend.\n";
        var withShared = RenameFixture.App.Replace("uses Unit1,", @"uses Shared in '..\Shared\Shared.pas', Unit1,", StringComparison.Ordinal);
        File(@"C:\p\Shared\Shared.pas", Shared);
        File(Dir + @"\App.pas", withShared);
        await RefusedAsync();
        Assert.Equal(Shared, Text(@"C:\p\Shared\Shared.pas"));
        Assert.Equal(withShared, Text(Dir + @"\App.pas"));

        // IDE-LIB-008: a library source that is not a project member uses Unit1.
        const string Library = "unit LibUser;\ninterface\nuses Unit1;\nimplementation\nend.\n";
        var withLibrary = RenameFixture.App.Replace("uses Unit1,", "uses LibUser, Unit1,", StringComparison.Ordinal);
        File(@"G:\fw\src\LibUser.pas", Library);
        File(Dir + @"\App.pas", withLibrary);
        await RefusedAsync();
        Assert.Equal(Library, Text(@"G:\fw\src\LibUser.pas"));

        // The same file as a project member may be edited.
        File(ProjectPath, project.Replace("{ \"path\": \"Unit3.pas\" }", "{ \"path\": \"Unit3.pas\" }, { \"path\": \"G:/fw/src/LibUser.pas\" }", StringComparison.Ordinal));
        Assert.True(await _services.Projects.CloseAsync());
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));
        Assert.Equal(RenameFixture.Renamed(Library), Text(@"G:\fw\src\LibUser.pas"));
        Assert.Equal(RenameFixture.Renamed(withLibrary), Text(Dir + @"\App.pas"));
    }

    /// <summary>I-252: the search paths of IDE-PM-133 are the effective ones of the active configuration (CMP-1160).</summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-133")]
    [Trait("Req", "IDE-CFG-001")]
    public Task EditInSearchPathOfActiveConfiguration() => TestUiThread.RunAsync(() => EditInSearchPathOfActiveConfigurationOnUiThread());

    private async Task EditInSearchPathOfActiveConfigurationOnUiThread()
    {
        // Shared lies outside the project directory, on a search path that only configuration Wide (and Wider) has.
        const string Shared = "unit Shared;\ninterface\nuses Unit1;\nimplementation\nend.\n";
        var withShared = RenameFixture.App.Replace("uses Unit1,", "uses Shared, Unit1,", StringComparison.Ordinal);
        File(@"C:\p\Shared\Shared.pas", Shared);
        await OpenFixtureAsync("""
            { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                  "Wide": { "optimization": 0, "debugInfo": true, "defines": [], "searchPaths": ["../Shared"], "outputDir": "bin/Wide" },
                                  "Wider": { "inherits": "Wide" } },
              "activeConfiguration": "Wider",
              "files": [ { "path": "App.pas" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" }, { "path": "Unit3.pas" } ] }
            """);
        File(Dir + @"\App.pas", withShared);

        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));
        Assert.Empty(Dialogs.Requests);
        Assert.Equal(RenameFixture.Renamed(Shared), Text(@"C:\p\Shared\Shared.pas"));
        Assert.Equal(RenameFixture.Renamed(withShared), Text(Dir + @"\App.pas"));
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-135")]
    public Task UnparsableUnitFallsBackToHeadingOnly() => TestUiThread.RunAsync(() => UnparsableUnitFallsBackToHeadingOnlyOnUiThread());

    private async Task UnparsableUnitFallsBackToHeadingOnlyOnUiThread()
    {
        // No heading at all: the file, the project entry and nothing else change — exactly Phases 1–7.
        const string Broken = "{ work in progress }\nprocedure Hello;\n";
        await OpenFixtureAsync();
        File(Dir + @"\Unit1.pas", Broken);
        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));
        Assert.Equal(Broken, Text(Dir + @"\Customers.pas"));
        Assert.Equal(RenameFixture.App, Text(Dir + @"\App.pas"));
        Assert.Equal(RenameFixture.Unit2, Text(Dir + @"\Unit2.pas"));
        Assert.Equal("Customers.pas", _services.Projects.Project!.Files[1].Path);
        Assert.Empty(Dialogs.Requests);

        // A heading that names another unit than the file is left alone as before.
        File(Dir + @"\Unit2.pas", "unit Other;\ninterface\nimplementation\nend.\n");
        Assert.True(await _services.Projects.RenameFileAsync("Unit2.pas", "Second.pas"));
        Assert.StartsWith("unit Other;", Text(Dir + @"\Second.pas"), StringComparison.Ordinal);
        Assert.Equal(RenameFixture.App, Text(Dir + @"\App.pas"));
        Assert.Empty(Dialogs.Requests);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-135")]
    public Task UnparsableUnit_CodeModelUnavailable_HeadingByLexer() => TestUiThread.RunAsync(async () =>
    {
        // No toolchain paths: the heading is found with the lexer, references stay, the Phase 1 status is shown.
        await OpenFixtureAsync(toolchain: false);
        Assert.Null(_services.Toolchain.Resolve().RtlDir);
        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "Customers.pas"));
        Assert.Equal(RenameFixture.Renamed(RenameFixture.Unit1), Text(Dir + @"\Customers.pas"));
        Assert.Equal(RenameFixture.App, Text(Dir + @"\App.pas"));
        Assert.Equal(Status(Strings.Msg_Project_RenameUnitNote, "Unit1.pas", "Customers.pas"), _services.Status.Message);
        Assert.Empty(Dialogs.Requests);
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-PM-137")]
    public Task CaseOnlyAndOtherFilesSkipLanguageService() => TestUiThread.RunAsync(() => CaseOnlyAndOtherFilesSkipLanguageServiceOnUiThread());

    private async Task CaseOnlyAndOtherFilesSkipLanguageServiceOnUiThread()
    {
        await OpenFixtureAsync("""
            { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
              "files": [ { "path": "App.pas" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" }, { "path": "Unit3.pas" },
                         { "path": "notes.md" } ] }
            """);
        File(Dir + @"\notes.md", "Unit1 is described here\n");

        // A change of letter case only: the heading follows (lexer), no other file is touched, the service is not asked.
        Assert.True(await _services.Projects.RenameFileAsync("Unit1.pas", "UNIT1.pas"));
        Assert.StartsWith("unit UNIT1;", Text(Dir + @"\UNIT1.pas"), StringComparison.Ordinal);
        Assert.Equal(RenameFixture.App, Text(Dir + @"\App.pas"));
        Assert.Equal(0, _busyChanges);
        Assert.True(_services.CodeModel.Worker.IsIdle);

        // A file of another kind; a unit renamed without "update the heading".
        Assert.True(await _services.Projects.RenameFileAsync("notes.md", "readme.md"));
        Assert.Equal("Unit1 is described here\n", Text(Dir + @"\readme.md"));
        Assert.True(await _services.Projects.RenameFileAsync("Unit3.pas", "Third.pas", updateHeading: false));
        Assert.Equal(RenameFixture.Unit3, Text(Dir + @"\Third.pas"));
        Assert.Equal(0, _busyChanges);
        File(Dir + @"\Unit3.pas", RenameFixture.Unit3); // the program still uses Unit3: put it back for what follows
        _fs.DeleteFile(Dir + @"\Third.pas");
        File(ProjectPath, Text(ProjectPath).Replace("Third.pas", "Unit3.pas", StringComparison.Ordinal));
        Assert.True(await _services.Projects.CloseAsync());
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        _busyChanges = 0;

        // The main source: "program <Name>" through the same edit set; the project keeps its name and file (IDE-PM-073).
        Assert.True(await _services.Projects.RenameFileAsync("App.pas", "Start.pas"));
        Assert.Equal(RenameFixture.App.Replace("program App;", "program Start;", StringComparison.Ordinal), Text(Dir + @"\Start.pas"));
        Assert.Equal(2, _busyChanges);
        Assert.Equal("App", _services.Projects.Project!.Name);
        Assert.Equal("Start.pas", _services.Projects.Project.MainSource);
        Assert.True(_fs.FileExists(ProjectPath));
    }
}
