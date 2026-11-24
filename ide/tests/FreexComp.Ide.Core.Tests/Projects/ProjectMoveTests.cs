using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Popups;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>Moving project files between folders (phase8-project-files.md §4): the operation behind drag and drop and <c>project.moveFile</c>.</summary>
[Trait("Phase", "8")]
public sealed class ProjectMoveTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";
    private const string MainUnit = "unit Main;\n\ninterface\n\n{$FORM Main.form}\n\nimplementation\nend.\n";

    private static readonly FocusState ProjectManagerFocus = new(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);

    private readonly InMemoryFileSystem _fs = new();
    private readonly RecordingFileSystem _recording;
    private readonly IdeServices _services;

    public ProjectMoveTests()
    {
        _recording = new RecordingFileSystem(_fs);
        _services = TestComposition.Build(o => o.FileSystem = _recording);
    }

    private ProjectManagerViewModel Pm => _services.ProjectManager;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private string Text(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    private string Saved() => Text(ProjectPath);

    /// <summary>
    /// Hello.pproj: Hello.pas (main source), Unit1.pas, Main.pas with Main.form, notes.md, lib/Unit2.pas, src/Unit3.pas,
    /// src/notes.md and ../Shared/Common.pas; opened, the tree focused.
    /// </summary>
    private async Task OpenHelloAsync()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "searchPaths": ["vendor"],
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "Main.pas", "form": "Main.form" }, { "path": "notes.md" },
                         { "path": "lib/Unit2.pas" }, { "path": "src/Unit3.pas" }, { "path": "src/notes.md" }, { "path": "../Shared/Common.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Unit1;\nbegin\nend.\n");
        File(Dir + @"\Unit1.pas", "unit Unit1;\ninterface\nimplementation\nend.\n");
        File(Dir + @"\Main.pas", MainUnit);
        File(Dir + @"\Main.form", "object Main: TForm\nend\n");
        File(Dir + @"\notes.md", "notes\n");
        File(Dir + @"\lib\Unit2.pas", "unit Unit2;\ninterface\nimplementation\nend.\n");
        File(Dir + @"\src\Unit3.pas", "unit Unit3;\ninterface\nimplementation\nend.\n");
        File(Dir + @"\src\notes.md", "other notes\n");
        File(@"C:\p\Shared\Common.pas", "unit Common;\ninterface\nimplementation\nend.\n");
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        _services.FocusTracker.Update(ProjectManagerFocus);
        await Pm.MissingFileChecks;
    }

    private ProjectNodeViewModel Row(string key) => Pm.Find(key) ?? throw new InvalidOperationException("No row " + key);

    private IReadOnlyList<string> Paths() => [.. _services.Projects.Project!.Files.Select(f => f.Path)];

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-121")]
    [Trait("Req", "IDE-PM-123")]
    [Trait("Req", "IDE-PM-125")]
    public Task MoveIntoSubfolder_UpdatesPathsFormAndOpenDocument() => TestUiThread.RunAsync(() => MoveIntoSubfolder_UpdatesPathsFormAndOpenDocumentOnUiThread());

    private async Task MoveIntoSubfolder_UpdatesPathsFormAndOpenDocumentOnUiThread()
    {
        await OpenHelloAsync();
        var unit1 = (TextDocumentViewModel)(await _services.Documents.OpenAsync(Dir + @"\Unit1.pas")).Value!;
        unit1.Buffer.ApplyEdits([new TextEdit(unit1.Buffer.Text.Length, 0, "// edited\n")]);
        var renamed = new List<string>();
        _services.Projects.FileRenamed += (_, path) => renamed.Add(path);

        // IDE-PM-121: the root and folder rows are targets, a file row stands for its folder, External is none; a drop
        // into the directory every dragged file is already in is a no-op.
        var lib = Row("lib/");
        Assert.Same(lib, Pm.DropTargetFor(lib));
        Assert.Same(lib, Pm.DropTargetFor(Row("lib/Unit2.pas")));
        Assert.Same(Pm.Roots[0], Pm.DropTargetFor(Row("Unit1.pas")));
        Assert.Same(Pm.Roots[0], Pm.DropTargetFor(Row("Main.form")));
        Assert.Same(Pm.Roots[0], Pm.DropTargetFor(Pm.Roots[0]));
        Assert.Null(Pm.DropTargetFor(Row(ProjectManagerViewModel.ExternalKey)));
        Assert.Null(Pm.DropTargetFor(Row("../Shared/Common.pas")));
        Assert.Null(Pm.DropTargetFor(null));
        _recording.FailWhen = call => call.StartsWith("MoveFile", StringComparison.Ordinal);
        Assert.False(await Pm.MoveAsync([Row("Unit1.pas"), Row("notes.md")], Pm.Roots[0]));
        _recording.FailWhen = null;
        Assert.Empty(Dialogs.Requests);
        Assert.True(unit1.IsModified); // nothing happened, not even the save

        // Two rows into lib/: a modified open unit and a unit with its form.
        lib.IsExpanded = false;
        var project = Saved();
        var calls = _recording.Calls.Count;
        Assert.True(await Pm.MoveAsync([Row("Unit1.pas"), Row("Main.pas")], lib));

        // (1) the modified document was saved before the move; (2) files and the companion form moved on disk.
        var disk = _recording.Calls.Skip(calls).Where(c => c.StartsWith("MoveFile", StringComparison.Ordinal) || c.Contains(@"Unit1.pas", StringComparison.Ordinal)).ToList();
        Assert.True(disk.FindIndex(c => c.StartsWith("WriteAllBytesDurable " + Dir + @"\Unit1.pas", StringComparison.Ordinal))
            < disk.FindIndex(c => c == $@"MoveFile {Dir}\Unit1.pas -> {Dir}\lib\Unit1.pas"));
        Assert.Contains($@"MoveFile {Dir}\Main.pas -> {Dir}\lib\Main.pas", disk);
        Assert.Contains($@"MoveFile {Dir}\Main.form -> {Dir}\lib\Main.form", disk);
        Assert.False(_fs.FileExists(Dir + @"\Unit1.pas"));
        Assert.False(_fs.FileExists(Dir + @"\Main.pas"));
        Assert.False(_fs.FileExists(Dir + @"\Main.form"));
        Assert.Equal("unit Unit1;\ninterface\nimplementation\nend.\n// edited\n", Text(Dir + @"\lib\Unit1.pas"));
        Assert.Equal(MainUnit, Text(Dir + @"\lib\Main.pas")); // IDE-PM-125: source text is not edited, {$FORM} stays valid
        Assert.Equal("object Main: TForm\nend\n", Text(Dir + @"\lib\Main.form"));

        // (3) paths rewritten in place, entry order unchanged; (5) the project saved; searchPaths never edited.
        Assert.Equal(["Hello.pas", "lib/Unit1.pas", "lib/Main.pas", "notes.md", "lib/Unit2.pas", "src/Unit3.pas", "src/notes.md", "../Shared/Common.pas"], Paths());
        Assert.Equal(new ProjectFileItem("lib/Main.pas", "lib/Main.form", "unit"), _services.Projects.Project!.Files[2]);
        Assert.Equal("Hello.pas", _services.Projects.Project.MainSource);
        Assert.NotEqual(project, Saved());
        Assert.Contains("\"path\": \"lib/Unit1.pas\"", Saved(), StringComparison.Ordinal);
        Assert.Contains("\"form\": \"lib/Main.form\"", Saved(), StringComparison.Ordinal);
        Assert.Equal(["vendor"], _services.Projects.Definition!.SearchPaths);

        // (4) the open document follows its file.
        Assert.Equal(Dir + @"\lib\Unit1.pas", unit1.FilePath);
        Assert.False(unit1.IsModified);
        Assert.True(_services.Projects.IsMember(unit1.FilePath));
        Assert.Equal(["lib/Unit1.pas", "lib/Main.pas"], renamed);

        // (6) the tree: target expanded, moved rows selected; the status names file count and folder.
        Assert.True(Row("lib/").IsExpanded);
        Assert.Equal(["lib/Unit1.pas", "lib/Main.pas"], Pm.Selection.Select(n => n.Key));
        Assert.Equal("lib/Main.form", Row("lib/Main.pas").Children.Single().Key);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_MovedMany, 2, "lib"), _services.Status.Message);

        // One file, dropped on a file row of the root: back into the project directory.
        Assert.True(await Pm.MoveAsync([Row("lib/Unit1.pas")], Pm.DropTargetFor(Row("notes.md"))!));
        Assert.Equal("Unit1.pas", Paths()[1]);
        Assert.Equal(Dir + @"\Unit1.pas", unit1.FilePath);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_MovedOne, "Unit1.pas", "Hello"), _services.Status.Message);

        // Rows already in the target stay; the others move (IDE-PM-121).
        Assert.True(await Pm.MoveAsync([Row("lib/Unit2.pas"), Row("src/Unit3.pas")], Row("lib/")));
        Assert.Equal("lib/Unit3.pas", Paths()[5]);
        Assert.Equal(["lib/Unit3.pas"], Pm.Selection.Select(n => n.Key));
        Assert.Empty(Dialogs.Requests);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-123")]
    public Task MoveMainSource_UpdatesMainSource() => TestUiThread.RunAsync(() => MoveMainSource_UpdatesMainSourceOnUiThread());

    private async Task MoveMainSource_UpdatesMainSourceOnUiThread()
    {
        await OpenHelloAsync();
        var main = Assert.IsType<TextDocumentViewModel>(_services.Documents.ActiveDocument, exactMatch: false);
        Assert.Equal(Dir + @"\Hello.pas", main.FilePath);

        Assert.True(await Pm.MoveAsync([Row("Hello.pas")], Row("src/")));

        Assert.Equal("src/Hello.pas", _services.Projects.Project!.MainSource);
        Assert.Equal("src/Hello.pas", Paths()[0]);
        Assert.Contains("\"mainSource\": \"src/Hello.pas\"", Saved(), StringComparison.Ordinal);
        Assert.Equal(Dir + @"\src\Hello.pas", main.FilePath);
        Assert.True(Row("src/Hello.pas").IsMainSource);
        Assert.Equal("program Hello;\nuses Unit1;\nbegin\nend.\n", Text(Dir + @"\src\Hello.pas"));
        Assert.Equal("Hello", _services.Projects.Project.Name); // IDE-PM-073: the project is not renamed
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-122")]
    public Task ExistingTargetRefusesWholeMove() => TestUiThread.RunAsync(() => ExistingTargetRefusesWholeMoveOnUiThread());

    private async Task ExistingTargetRefusesWholeMoveOnUiThread()
    {
        await OpenHelloAsync();
        File(Dir + @"\lib\MAIN.FORM", "another form, not in the project");
        Dialogs.Answer("Msg.Project.MoveExists", "OK");
        Dialogs.Answer("Msg.Project.MoveFailed", "OK");
        var project = Saved();
        var files = _fs.AllFiles;

        async Task RefusedAsync(string[] rows, string target, string messageId, string firstArg)
        {
            var before = Dialogs.Requests.Count;
            Assert.False(await Pm.MoveAsync([.. rows.Select(Row)], Row(target)));
            var request = Assert.Single(Dialogs.Requests.Skip(before));
            Assert.Equal(messageId, request.MessageId);
            Assert.Equal(firstArg, request.Args[0]);
            Assert.Equal(files, _fs.AllFiles); // nothing was moved
            Assert.Equal(project, Saved());
            Assert.Equal("Unit1.pas", Paths()[1]);
        }

        // The companion form collides (ignoring case): the unit dragged first does not move either.
        await RefusedAsync(["Unit1.pas", "Main.pas"], "lib/", "Msg.Project.MoveExists", "Main.form");
        Assert.Equal("lib", Dialogs.Requests[^1].Args[1]);

        // A file of the same name in the target, also when it is another dragged file; into the root the folder is the project.
        await RefusedAsync(["notes.md"], "src/", "Msg.Project.MoveExists", "notes.md");
        await RefusedAsync(["src/notes.md"], ProjectManagerViewModel.RootKey, "Msg.Project.MoveExists", "notes.md");
        Assert.Equal("Hello", Dialogs.Requests[^1].Args[1]);
        _fs.CreateDirectory(Dir + @"\src\Unit1.pas"); // a directory of that name
        await RefusedAsync(["Unit1.pas"], "src/", "Msg.Project.MoveExists", "Unit1.pas");
        _fs.DeleteDirectory(Dir + @"\src\Unit1.pas");

        // The longest resulting path over 240 characters: the existing path-too-long text, nothing moved.
        var deep = string.Join('/', Enumerable.Repeat("abcdefghijklmnopqrstuvwxyz0123", 8)) + "/";
        _fs.CreateDirectory(Path.Combine(Dir, deep.Replace('/', '\\')));
        Pm.RestoreFolders([deep], []);
        files = _fs.AllFiles;
        await RefusedAsync(["Unit1.pas"], deep, "Msg.Project.MoveFailed", Path.Combine(Dir, deep.Replace('/', '\\'), "Unit1.pas"));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_PathTooLong, 240), Dialogs.Requests[^1].Args[1]);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-123")]
    [Trait("Req", "IDE-PM-124")]
    public Task FailureRollsBackDiskAndProject() => TestUiThread.RunAsync(() => FailureRollsBackDiskAndProjectOnUiThread());

    private async Task FailureRollsBackDiskAndProjectOnUiThread()
    {
        await OpenHelloAsync();
        Dialogs.Answer("Msg.Project.MoveFailed", "OK");
        var project = Saved();
        var files = _fs.AllFiles;
        var unit1 = (TextDocumentViewModel)(await _services.Documents.OpenAsync(Dir + @"\Unit1.pas")).Value!;

        async Task<FreexComp.Ide.Core.Services.MessageRequest> FailsAsync(Func<string, bool> failWhen)
        {
            _recording.FailWhen = failWhen;
            var before = Dialogs.Requests.Count;
            Assert.False(await Pm.MoveAsync([Row("Unit1.pas"), Row("Main.pas")], Row("lib/")));
            _recording.FailWhen = null;
            var request = Assert.Single(Dialogs.Requests.Skip(before));
            Assert.Equal("Msg.Project.MoveFailed", request.MessageId);
            return request;
        }

        void Unchanged()
        {
            Assert.Equal(files, _fs.AllFiles);
            Assert.Equal(project, Saved());
            Assert.Equal(["Hello.pas", "Unit1.pas", "Main.pas"], Paths().Take(3));
            Assert.Equal(new ProjectFileItem("Main.pas", "Main.form", "unit"), _services.Projects.Project!.Files[2]);
            Assert.Equal(Dir + @"\Unit1.pas", unit1.FilePath);
            Assert.NotNull(Pm.Find("Unit1.pas"));
        }

        // The second file's move fails: the first is moved back, the project is as before; path and OS reason shown.
        var request = await FailsAsync(call => call == $@"MoveFile {Dir}\Main.pas -> {Dir}\lib\Main.pas");
        Assert.Equal([Dir + @"\Main.pas", "Injected failure: " + $@"MoveFile {Dir}\Main.pas -> {Dir}\lib\Main.pas"], request.Args);
        Unchanged();

        // The companion form, the project save and a document that cannot follow: each undoes the steps before it.
        request = await FailsAsync(call => call.StartsWith($@"MoveFile {Dir}\Main.form -> ", StringComparison.Ordinal));
        Assert.Equal(Dir + @"\Main.form", request.Args[0]);
        Unchanged();
        await FailsAsync(call => call.StartsWith("WriteAllBytesDurable " + ProjectPath, StringComparison.Ordinal));
        Unchanged();
        await FailsAsync(call => call.StartsWith("WriteAllBytesDurable " + Dir + @"\lib\Unit1.pas", StringComparison.Ordinal));
        Unchanged();

        // A rollback that fails too: the path that could not be restored is added on a new line; the project file is
        // left unchanged, so the entry shows as missing (UI-TW-107 offers the repair).
        request = await FailsAsync(call => call == $@"MoveFile {Dir}\Main.pas -> {Dir}\lib\Main.pas" || call == $@"MoveFile {Dir}\lib\Unit1.pas -> {Dir}\Unit1.pas");
        Assert.Equal(Dir + @"\Main.pas", request.Args[0]);
        Assert.Equal(["Injected failure: " + $@"MoveFile {Dir}\Main.pas -> {Dir}\lib\Main.pas", Dir + @"\lib\Unit1.pas"], request.Args[1].Split('\n'));
        Assert.Equal(project, Saved());
        Assert.Equal("Unit1.pas", Paths()[1]);
        Assert.True(_fs.FileExists(Dir + @"\lib\Unit1.pas"));
        Pm.OnWindowActivated();
        await Pm.MissingFileChecks;
        Assert.True(Row("Unit1.pas").IsMissing);
        _fs.MoveFile(Dir + @"\lib\Unit1.pas", Dir + @"\Unit1.pas", overwrite: false);
        Pm.OnWindowActivated();
        await Pm.MissingFileChecks;
        Assert.False(Row("Unit1.pas").IsMissing);

        // A modified document whose save fails (step 1) aborts before any move.
        unit1.Buffer.ApplyEdits([new TextEdit(0, 0, "// x\n")]);
        Dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        _recording.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + Dir + @"\Unit1.pas", StringComparison.Ordinal);
        var calls = _recording.Calls.Count;
        var moved = await Pm.MoveAsync([Row("Unit1.pas"), Row("Main.pas")], Row("lib/"));
        _recording.FailWhen = null;
        Assert.False(moved, string.Join(" | ", _recording.Calls.Skip(calls)));
        Assert.Equal("Msg.File.SaveFailed", Dialogs.Requests[^1].MessageId);
        Assert.DoesNotContain(_recording.Calls.Skip(calls), c => c.StartsWith("MoveFile " + Dir + @"\Main", StringComparison.Ordinal));
        Assert.True(unit1.IsModified);
        Assert.Equal(project, Saved());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-126")]
    public Task EmptiedSourceFolderStaysAsEmptyFolder() => TestUiThread.RunAsync(() => EmptiedSourceFolderStaysAsEmptyFolderOnUiThread());

    private async Task EmptiedSourceFolderStaysAsEmptyFolderOnUiThread()
    {
        await OpenHelloAsync();
        Assert.Empty(Pm.EmptyFolders);

        // One of two files leaves src/: the folder still has a project file, no entry is needed.
        Assert.True(await Pm.MoveAsync([Row("src/Unit3.pas")], Row("lib/")));
        Assert.Empty(Pm.EmptyFolders);

        // The last one leaves: the folder stays in the tree as an empty folder, in the session too.
        Assert.True(await Pm.MoveAsync([Row("src/notes.md")], Row("lib/")));
        Assert.Equal(["src/"], Pm.EmptyFolders);
        var src = Row("src/");
        Assert.Empty(src.Children);
        Assert.True(_fs.DirectoryExists(Dir + @"\src"));
        Assert.Equal(["lib/notes.md"], Pm.Selection.Select(n => n.Key));
        _services.Session.Save();
        Assert.Contains("\"src/\"", Text(Dir + @"\.freexcomp\session.json"), StringComparison.Ordinal);

        // It is a drop target again, and the user removes it with Delete (IDE-PM-115).
        Assert.True(await Pm.MoveAsync([Row("lib/Unit3.pas")], src));
        Assert.Equal("src/Unit3.pas", Paths()[5]);
        Assert.True(await Pm.MoveAsync([Row("src/Unit3.pas")], Pm.Roots[0]));
        Pm.Select(Row("src/"));
        Assert.True(_services.Commands.CanExecute("project.removeFile"));
        await _services.Commands.ExecuteAsync("project.removeFile");
        Assert.Null(Pm.Find("src/"));
        Assert.False(_fs.DirectoryExists(Dir + @"\src"));
        Assert.Empty(Pm.EmptyFolders);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-120")]
    [Trait("Req", "IDE-PM-127")]
    [Trait("Req", "UI-TW-124")]
    public Task MoveCommandListsFoldersAndMoves() => TestUiThread.RunAsync(() => MoveCommandListsFoldersAndMovesOnUiThread());

    private async Task MoveCommandListsFoldersAndMovesOnUiThread()
    {
        var commands = _services.Commands;
        Assert.False(commands.CanExecute("project.moveFile")); // no project
        await OpenHelloAsync();
        _fs.CreateDirectory(Dir + @"\lib\data");
        Pm.RestoreFolders(["lib/data/"], []);
        MoveToFolderViewModel Popup() => Assert.IsType<MoveToFolderViewModel>(_services.QuickPick.Current);

        // Enabled as a drag could start (IDE-PM-120): file rows inside the project that exist; not the root, folders,
        // form child rows alone, External files, missing files; not while the filter box is non-empty.
        foreach (var (key, enabled) in new[]
                 {
                     ("Unit1.pas", true), ("Hello.pas", true), ("lib/Unit2.pas", true), ("notes.md", true), (ProjectManagerViewModel.RootKey, false),
                     ("lib/", false), ("Main.form", false), ("../Shared/Common.pas", false), (ProjectManagerViewModel.ExternalKey, false),
                 })
        {
            Pm.Select(Row(key));
            Assert.True(commands.CanExecute("project.moveFile") == enabled, key);
            Assert.True(Pm.CanStartDrag(Row(key)) == enabled, key);
        }

        _fs.DeleteFile(Dir + @"\notes.md");
        Pm.OnWindowActivated();
        await Pm.MissingFileChecks;
        Pm.Select(Row("notes.md"));
        Assert.False(commands.CanExecute("project.moveFile")); // missing
        Pm.Select(Row("Unit1.pas"));
        Pm.FilterText = "unit";
        Assert.False(commands.CanExecute("project.moveFile"));
        Assert.False(Pm.CanStartDrag(Row("Unit1.pas")));
        Pm.FilterText = "";
        ((ManualClock)_services.Clock).Advance(ToolWindowViewModel.FilterDelay);
        Assert.True(commands.CanExecute("project.moveFile"));

        // One file of the root: the project (labelled with its name) is omitted, every folder key follows in tree order.
        await commands.ExecuteAsync("project.moveFile");
        var popup = Popup();
        Assert.Equal("QuickPick", popup.Name);
        Assert.Equal(Strings.QuickPick_MoveToFolder_Title, popup.AccessibleName);
        Assert.Equal("Move to Folder", popup.Title);
        Assert.Equal([("lib/", "lib"), ("lib/data/", "lib/data"), ("src/", "src")], popup.Items.Select(i => (i.Id, i.Text)));

        // Typing filters by substring; Escape closes without moving.
        popup.Input = "DAT";
        Assert.Equal(["lib/data/"], popup.Items.Select(i => i.Id));
        Assert.Equal([4, 5, 6], popup.Items[0].Highlights);
        popup.Input = "zzz";
        Assert.Empty(popup.Items);
        Assert.Equal(Strings.Msg_Find_NoMatches, popup.NoMatchText);
        await popup.CommitAsync(); // nothing selected: nothing happens
        popup.Close();
        Assert.Null(_services.QuickPick.Current);
        Assert.Equal("Unit1.pas", Paths()[1]);

        // Files of different folders: every target is listed, the root first; Enter moves the selection.
        Pm.Select(Row("Unit1.pas"));
        Pm.Select(Row("lib/Unit2.pas"), ProjectSelectMode.Toggle);
        Pm.Select(Row("Main.form"), ProjectSelectMode.Toggle); // a form child row in the selection moves with its unit only
        await commands.ExecuteAsync("project.moveFile");
        popup = Popup();
        Assert.Equal([("$project", "Hello"), ("lib/", "lib"), ("lib/data/", "lib/data"), ("src/", "src")], popup.Items.Select(i => (i.Id, i.Text)));
        popup.Input = "src";
        await popup.CommitAsync();
        Assert.Null(_services.QuickPick.Current);
        Assert.Equal(["Hello.pas", "src/Unit1.pas", "Main.pas", "notes.md", "src/Unit2.pas"], Paths().Take(5));
        Assert.Equal(["src/Unit1.pas", "src/Unit2.pas"], Pm.Selection.Select(n => n.Key));
        Assert.Equal(["lib/data/"], Pm.EmptyFolders); // lib/ stays as the ancestor of a remembered folder (IDE-PM-114)

        // All selected files share a folder: that folder is omitted; the root is the first row.
        await commands.ExecuteAsync("project.moveFile");
        popup = Popup();
        Assert.Equal(["$project", "lib/", "lib/data/"], popup.Items.Select(i => i.Id));
        await popup.CommitAsync();
        Assert.Equal(["Unit1.pas", "Unit2.pas"], [Paths()[1], Paths()[4]]);
        Assert.Empty(Dialogs.Requests);
    }
}
