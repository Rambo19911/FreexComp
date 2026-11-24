using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary><c>IProjectService</c>: open, model, close, title, <c>project.viewSource</c> (project-manager.md §1–§2).</summary>
[Trait("Phase", "1")]
public sealed class ProjectServiceTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";

    private readonly InMemoryFileSystem _fs = new();
    private readonly RecordingLog _log = new();
    private readonly IdeServices _services;

    public ProjectServiceTests()
    {
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Log = _log;
        });
    }

    private IProjectService Projects => _services.Projects;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private FakeFileDialogService Pickers => (FakeFileDialogService)_services.FileDialogs;

    private string File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    /// <summary>Hello.pproj: main source Hello.pas, members Unit1.pas and sub/Unit2.pas.</summary>
    private void WriteHello(string name = "Hello")
    {
        File(ProjectPath, $$"""
            { "formatVersion": 1, "name": "{{name}}", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "sub/Unit2.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        File(Dir + @"\Unit1.pas", "unit Unit1;\ninterface\nimplementation\nend.\n");
        File(Dir + @"\sub\Unit2.pas", "unit Unit2;\ninterface\nimplementation\nend.\n");
    }

    private async Task<TextDocumentViewModel> OpenDocumentAsync(string path)
    {
        var result = await _services.Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsAssignableFrom<TextDocumentViewModel>(result.Value); // a unit with a companion form opens as its subclass (I-129)
    }

    private static void Type(TextDocumentViewModel document, string text) =>
        document.Buffer.ApplyEdits([new TextEdit(0, 0, text)]);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-011")]
    [Trait("Req", "IDE-PM-012")]
    public Task OpenValidatesAndPopulatesModel() => TestUiThread.RunAsync(() => OpenValidatesAndPopulatesModelOnUiThread()); // I-209

    private async Task OpenValidatesAndPopulatesModelOnUiThread()
    {
        // Main source not listed, a duplicate entry (case and separator differ), unknown active configuration.
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "activeConfiguration": "Profile",
              "files": [ { "path": "Unit1.pas" }, { "path": "Main.pas", "form": "Main.form" }, { "path": "notes.md" },
                         { "path": "UNIT1.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        var changes = 0;
        Projects.ProjectChanged += (_, _) => changes++;

        Assert.True(await Projects.OpenAsync(ProjectPath));

        var project = Assert.IsType<ProjectModel>(Projects.Project);
        Assert.Equal(ProjectPath, project.FilePath);
        Assert.Equal(Dir, project.Directory);
        Assert.Equal("Hello", project.Name);
        Assert.Equal("pascal", project.Language);
        Assert.Equal("console", project.AppType);
        Assert.Equal("Hello.pas", project.MainSource);
        Assert.Equal(
            [
                new ProjectFileItem("Hello.pas", null, "unit"),
                new ProjectFileItem("Unit1.pas", null, "unit"),
                new ProjectFileItem("Main.pas", "Main.form", "unit"),
                new ProjectFileItem("notes.md", null, "other"),
            ],
            project.Files);
        Assert.Equal(["Debug", "Release"], project.Configurations);
        Assert.Equal("Debug", project.ActiveConfiguration);
        Assert.Equal(["win64"], project.Platforms);
        Assert.False(project.IsModified);
        Assert.Equal(1, changes);
        Assert.Contains(_log.Lines, l => l.Contains("WARN", StringComparison.Ordinal) && l.Contains("UNIT1.pas", StringComparison.Ordinal));

        // Step 4 without a session: the main source is opened and active; members are known by path.
        Assert.Equal(Dir + @"\Hello.pas", _services.Documents.ActiveDocument!.FilePath);
        Assert.True(Projects.IsMember(Dir + @"\unit1.PAS"));
        Assert.True(Projects.IsMember(ProjectPath));
        Assert.False(Projects.IsMember(Dir + @"\Other.pas"));
        Assert.False(Projects.IsMember(null));
        Assert.True(_services.Commands.CanExecute("file.closeProject"));
        Assert.True(_services.Commands.CanExecute("project.viewSource"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-011")]
    [Trait("Req", "IDE-PM-014")]
    public Task OpenRestoresSession() => TestUiThread.RunAsync(() => OpenRestoresSessionOnUiThread()); // I-209

    private async Task OpenRestoresSessionOnUiThread()
    {
        WriteHello();
        File(Dir + @"\.freexcomp\session.json", """
            { "formatVersion": 1, "project": "Hello.pproj",
              "documents": [ { "path": "sub/Unit2.pas", "caret": { "line": 2, "column": 3 } }, { "path": "Unit1.pas" } ],
              "activeDocument": "sub/Unit2.pas" }
            """);

        // Step 4: the session's documents instead of the main source.
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.Equal([Dir + @"\sub\Unit2.pas", Dir + @"\Unit1.pas"], _services.Documents.Documents.Select(d => d.FilePath));
        var unit2 = Assert.IsType<TextDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(Dir + @"\sub\Unit2.pas", unit2.FilePath);
        Assert.Equal(new TextPosition(2, 3), unit2.ViewState.Caret);

        // Close writes the session before the members close (IDE-PM-014 step 3).
        unit2.ViewState.Caret = new TextPosition(3, 1);
        var paths = new List<string?>();
        _services.Documents.DocumentClosed += (_, _) =>
        {
            var text = Encoding.UTF8.GetString(_fs.ReadAllBytes(Dir + @"\.freexcomp\session.json"));
            paths.Add(text.Contains("\"line\": 3", StringComparison.Ordinal) ? "written" : "stale");
        };
        Assert.True(await Projects.CloseAsync());
        Assert.Equal(["written", "written"], paths);

        // environment.restoreOpenFiles off: only the main source opens.
        _services.Settings.Set(SettingsCatalog.EnvironmentRestoreOpenFiles, false);
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.Equal(Dir + @"\Hello.pas", Assert.Single(_services.Documents.Documents).FilePath);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-012")]
    public Task EmptyFilePathIsIgnoredAndKept() => TestUiThread.RunAsync(() => EmptyFilePathIsIgnoredAndKeptOnUiThread()); // I-209

    private async Task EmptyFilePathIsIgnoredAndKeptOnUiThread()
    {
        var text = """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "" }, { "path": "Unit1.pas" } ] }
            """;
        File(ProjectPath, text);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");

        Assert.True(await Projects.OpenAsync(ProjectPath));

        Assert.Equal(["Hello.pas", "Unit1.pas"], Projects.Project!.Files.Select(f => f.Path));
        Assert.False(Projects.IsMember(Dir));
        Assert.Contains(_log.Lines, l => l.Contains("WARN", StringComparison.Ordinal) && l.Contains("empty", StringComparison.Ordinal));
        Assert.Equal(text, Encoding.UTF8.GetString(_fs.ReadAllBytes(ProjectPath))); // the entry stays in the file
    }

    [Theory(Timeout = 30000)]
    [InlineData("""{ "formatVersion": 1, "name": "1Bad", "mainSource": "Hello.pas" }""", "Msg.Project.OpenFailed", "NameInvalid")]
    [InlineData("""{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "configurations": {} }""", "Msg.Project.OpenFailed", "NoConfigurations")]
    [InlineData("""{ "formatVersion": 3, "name": "Hello", "mainSource": "Hello.pas" }""", "Msg.Project.NewerFormat", null)]
    [InlineData("{ \"formatVersion\": 1,\n  \"name\": 5, \"mainSource\": \"Hello.pas\" }", "Msg.Project.OpenFailed", "Position")]
    [Trait("Req", "IDE-PM-005")]
    [Trait("Req", "IDE-PM-011")]
    [Trait("Req", "IDE-PM-012")]
    public Task OpenRejectsInvalidProjects(string text, string messageId, string? reason) => TestUiThread.RunAsync(() => OpenRejectsInvalidProjectsOnUiThread(text, messageId, reason)); // I-209

    private async Task OpenRejectsInvalidProjectsOnUiThread(string text, string messageId, string? reason)
    {
        File(ProjectPath, text);
        Dialogs.Answer(messageId, "OK");

        Assert.False(await Projects.OpenAsync(ProjectPath));

        Assert.Null(Projects.Project);
        Assert.Empty(_services.Documents.Documents);
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal(messageId, request.MessageId);
        if (reason is not null)
        {
            Assert.Equal(ProjectPath, request.Args[0]);
            var expected = reason switch
            {
                "NameInvalid" => Strings.Msg_Project_NameInvalid,
                "NoConfigurations" => Strings.Reason_NoConfigurations,
                _ => "Hello.pproj(2,11): ",
            };
            Assert.StartsWith(expected, request.Args[1], StringComparison.Ordinal);
        }

        Assert.Equal(text, Encoding.UTF8.GetString(_fs.ReadAllBytes(ProjectPath))); // never modified
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-011")]
    public Task OpenFailedOffersOpenAsText() => TestUiThread.RunAsync(() => OpenFailedOffersOpenAsTextOnUiThread()); // I-209

    private async Task OpenFailedOffersOpenAsTextOnUiThread()
    {
        File(ProjectPath, "{ // comments are not JSON\n}");
        Dialogs.Answer("Msg.Project.OpenFailed", "OpenAsText");

        Assert.False(await Projects.OpenAsync(ProjectPath));

        Assert.Null(Projects.Project);
        Assert.Equal(ProjectPath, _services.Documents.ActiveDocument!.FilePath);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-013")]
    [Trait("Req", "IDE-PM-014")]
    [Trait("Req", "IDE-PM-080")]
    [Trait("Req", "UI-DLG-063")]
    [Trait("Req", "UI-DLG-055")]
    public Task CloseKeepsNonMemberDocuments() => TestUiThread.RunAsync(() => CloseKeepsNonMemberDocumentsOnUiThread()); // I-209

    private async Task CloseKeepsNonMemberDocumentsOnUiThread()
    {
        WriteHello();
        var loose = await OpenDocumentAsync(File(@"C:\other\Loose.pas", "unit Loose;\n"));
        Assert.True(await Projects.OpenAsync(ProjectPath));
        var main = (TextDocumentViewModel)_services.Documents.ActiveDocument!;
        var unit1 = await OpenDocumentAsync(Dir + @"\Unit1.pas");
        var unit2 = await OpenDocumentAsync(Dir + @"\sub\Unit2.pas");
        var projectText = await OpenDocumentAsync(ProjectPath);

        // Save As of a member is "Save Copy As": the copy becomes a non-member document (IDE-PM-080).
        Pickers.NextSave(@"C:\other\Copy.pas");
        Assert.True(await _services.Documents.PickAndSaveAsAsync(unit2));
        Assert.Equal(Strings.Picker_SaveCopyAs, Pickers.Requests[^1].Title);
        Assert.Equal(@"C:\other\Copy.pas", unit2.FilePath);
        Assert.False(Projects.IsMember(unit2.FilePath));
        Assert.Contains(Projects.Project!.Files, f => f.Path == "sub/Unit2.pas"); // the project still lists the old file
        Assert.False(Projects.IsMember(loose.FilePath));

        // One prompt over the modified members and the project file, which is listed first; non-members are not asked.
        Type(loose, "x");
        Type(unit1, "x");
        Type(projectText, " ");
        Type(main, "x");
        UnsavedChangesViewModel? shown = null;
        Dialogs.Script(UnsavedChangesViewModel.DialogName, vm =>
        {
            shown = (UnsavedChangesViewModel)vm;
            return vm.PressCancelAsync();
        });
        Assert.False(await Projects.CloseAsync());
        Assert.Equal([projectText, main, unit1], shown!.Files.Select(f => f.Document));
        Assert.NotNull(Projects.Project); // canceled: nothing changed
        Assert.Equal(5, _services.Documents.Documents.Count);

        Dialogs.Script(UnsavedChangesViewModel.DialogName, vm => vm.Buttons.Single(b => b.Id == "DontSave").PressAsync());
        var changes = 0;
        Projects.ProjectChanged += (_, _) => changes++;
        await _services.Commands.ExecuteAsync("file.closeProject");

        Assert.Null(Projects.Project);
        Assert.Equal(1, changes);
        Assert.Equal([loose, unit2], _services.Documents.Documents);
        Assert.True(loose.IsModified); // untouched by the project's prompt
        Assert.False(_services.Commands.CanExecute("file.closeProject"));
        Assert.False(_services.Commands.CanExecute("project.viewSource"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-010")]
    [Trait("Req", "UI-DLG-063")]
    public Task OpeningAnotherProjectClosesTheCurrentOneFirst() => TestUiThread.RunAsync(() => OpeningAnotherProjectClosesTheCurrentOneFirstOnUiThread()); // I-209

    private async Task OpeningAnotherProjectClosesTheCurrentOneFirstOnUiThread()
    {
        WriteHello();
        File(@"C:\q\Two\Two.pproj", """{ "formatVersion": 1, "name": "Two", "mainSource": "Two.pas" }""");
        File(@"C:\q\Two\Two.pas", "program Two;\nbegin\nend.\n");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        var main = (TextDocumentViewModel)_services.Documents.ActiveDocument!;
        Type(main, "x");

        // Cancel in the unsaved prompt: the current project stays open.
        Dialogs.Enqueue("Msg.File.SaveChangesOne", "Cancel", "DontSave");
        Pickers.NextOpen(@"C:\q\Two\Two.pproj");
        await _services.Commands.ExecuteAsync("file.openProject");
        Assert.Equal("Hello", Projects.Project!.Name);
        Assert.Equal(Dir + @"\Hello.pas", Assert.Single(_services.Documents.Documents).FilePath);

        Assert.True(await Projects.OpenAsync(@"C:\q\Two\Two.pproj"));
        Assert.Equal("Two", Projects.Project!.Name);
        Assert.Equal(@"C:\q\Two\Two.pas", Assert.Single(_services.Documents.Documents).FilePath);
        Assert.Equal(["Msg.File.SaveChangesOne", "Msg.File.SaveChangesOne"], Dialogs.Requests.Select(r => r.MessageId));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-015")]
    public Task ViewSourceOpensMainSource() => TestUiThread.RunAsync(() => ViewSourceOpensMainSourceOnUiThread()); // I-209

    private async Task ViewSourceOpensMainSourceOnUiThread()
    {
        WriteHello();
        Assert.False(_services.Commands.CanExecute("project.viewSource"));
        Assert.True(await Projects.OpenAsync(ProjectPath));
        await OpenDocumentAsync(Dir + @"\Unit1.pas");

        await _services.Commands.ExecuteAsync("project.viewSource");

        Assert.Equal(Dir + @"\Hello.pas", _services.Documents.ActiveDocument!.FilePath);
        Assert.Equal(2, _services.Documents.Documents.Count);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-053")]
    public Task FileOpenWithProjectFileOpensTheProject() => TestUiThread.RunAsync(() => FileOpenWithProjectFileOpensTheProjectOnUiThread()); // I-209

    private async Task FileOpenWithProjectFileOpensTheProjectOnUiThread()
    {
        WriteHello();
        var notes = File(@"C:\other\notes.txt", "n");
        Pickers.NextOpen(ProjectPath, notes);

        await _services.Commands.ExecuteAsync("file.open");

        Assert.Equal("Hello", Projects.Project?.Name);
        Assert.Equal([Dir + @"\Hello.pas", notes], _services.Documents.Documents.Select(d => d.FilePath));
        Assert.Equal(notes, _services.Documents.ActiveDocument!.FilePath);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-051")]
    public Task ProjectDirectoryIsTheThirdInitialFolder() => TestUiThread.RunAsync(() => ProjectDirectoryIsTheThirdInitialFolderOnUiThread()); // I-209

    private async Task ProjectDirectoryIsTheThirdInitialFolderOnUiThread()
    {
        WriteHello();
        _fs.CreateDirectory(Path.Combine(_services.UserPaths.DocumentsDirectory, "FreexComp Projects"));
        Assert.NotEqual(Dir, _services.FilePrompts.InitialFolder("addExisting", stated: null));

        Assert.True(await Projects.OpenAsync(ProjectPath));

        Assert.Equal(Dir, _services.FilePrompts.InitialFolder("addExisting", stated: null));
        Assert.Equal(@"C:\p", _services.FilePrompts.InitialFolder("addExisting", stated: @"C:\p"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-030")]
    [Trait("Req", "UI-WIN-031")]
    public Task WindowTitleSubjectIsTheProject() => TestUiThread.RunAsync(() => WindowTitleSubjectIsTheProjectOnUiThread()); // I-209

    private async Task WindowTitleSubjectIsTheProjectOnUiThread()
    {
        WriteHello();
        var window = new MainWindowViewModel(_services);
        var loose = await OpenDocumentAsync(File(@"C:\other\Loose.pas", "unit Loose;\n"));
        Assert.Equal("Loose.pas – FreexComp", window.Title);

        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.Equal("Hello – FreexComp", window.Title);
        _services.Documents.Activate(loose);
        Assert.Equal("Hello – FreexComp", window.Title); // the active file is not part of the title

        _services.Settings.Set(SettingsCatalog.AppearanceShowFullPathInTitle, true);
        Assert.Equal(ProjectPath + " – FreexComp", window.Title);

        Assert.True(await Projects.CloseAsync());
        Assert.Equal(@"C:\other\Loose.pas – FreexComp", window.Title);
    }

    private ProjectManagerViewModel ProjectManager => (ProjectManagerViewModel)_services.ToolWindows.Get("ProjectManager")!;

    private string Saved() => Encoding.UTF8.GetString(_fs.ReadAllBytes(ProjectPath));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-040")]
    [Trait("Req", "IDE-PM-041")]
    [Trait("Req", "IDE-PM-042")]
    [Trait("Req", "IDE-PM-004")]
    public Task AddNewUnitNamesAndSkeleton() => TestUiThread.RunAsync(() => AddNewUnitNamesAndSkeletonOnUiThread()); // I-209

    private async Task AddNewUnitNamesAndSkeletonOnUiThread()
    {
        WriteHello();
        File(Dir + @"\Unit3.pas", "unit Unit3;\n"); // on disk in the target folder, not in the project
        Assert.False(_services.Commands.CanExecute("project.addNewUnit"));
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.True(_services.Commands.CanExecute("project.addNewUnit"));
        var mainText = _fs.ReadAllBytes(Dir + @"\Hello.pas");

        // Unit1 and Unit2 are project units (sub/Unit2.pas counts), Unit3.pas exists in the main source's folder.
        await _services.Commands.ExecuteAsync("project.addNewUnit");

        var path = Dir + @"\Unit4.pas";
        Assert.Equal("unit Unit4;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n", Encoding.UTF8.GetString(_fs.ReadAllBytes(path)));
        Assert.Equal(["Hello.pas", "Unit1.pas", "sub/Unit2.pas", "Unit4.pas"], Projects.Project!.Files.Select(f => f.Path));
        Assert.Contains("\"path\": \"Unit4.pas\"", Saved(), StringComparison.Ordinal); // saved at once (IDE-PM-004)
        var document = Assert.IsType<TextDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(path, document.FilePath);
        Assert.False(document.IsModified);
        Assert.Equal(new TextPosition(6, 1), document.ViewState.Caret); // the empty line after implementation
        Assert.Equal(mainText, _fs.ReadAllBytes(Dir + @"\Hello.pas")); // uses clause untouched (IDE-PM-042)

        // A selected folder row is the target folder; settings decide line ending and encoding.
        _services.Settings.Set(SettingsCatalog.FilesDefaultLineEnding, "lf");
        ProjectManager.Select(ProjectManager.Find("sub/")!);
        await _services.Commands.ExecuteAsync("project.addNewUnit");
        Assert.Equal("unit Unit3;\n\ninterface\n\nimplementation\n\nend.\n", Encoding.UTF8.GetString(_fs.ReadAllBytes(Dir + @"\sub\Unit3.pas")));
        Assert.Equal("sub/Unit3.pas", Projects.Project!.Files[^1].Path);
        Assert.Equal(["sub/Unit3.pas"], ProjectManager.Selection.Select(n => n.Key)); // the affected row is selected (IDE-PM-033)

        // file.newUnit adds a project unit while a project is open (a file row selected: the main source's folder).
        ProjectManager.Select(ProjectManager.Find("Unit1.pas")!);
        await _services.Commands.ExecuteAsync("file.newUnit");
        Assert.Equal("Unit5.pas", Projects.Project!.Files[^1].Path);
        Assert.Equal(Dir + @"\Unit5.pas", _services.Documents.ActiveDocument!.FilePath);

        // Without a project file.newUnit makes an untitled unit again.
        Assert.True(await Projects.CloseAsync());
        await _services.Commands.ExecuteAsync("file.newUnit");
        Assert.Null(_services.Documents.ActiveDocument!.FilePath);
    }

    /// <summary>
    /// <c>project.addNewFile</c> (phase8-project-files.md §3): <c>File&lt;N&gt;.txt</c> with zero bytes in the folder of
    /// IDE-PM-040, listed and saved, its row selected and being named, not opened; a name that would make it a unit or
    /// a form is refused inline.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-PM-118")]
    [Trait("Req", "IDE-PM-119")]
    [Trait("Req", "UI-TW-117")]
    public Task AddNewFile_CreatesListsAndRefusesUnitExtension() => TestUiThread.RunAsync(() => AddNewFile_CreatesListsAndRefusesUnitExtensionOnUiThread());

    private async Task AddNewFile_CreatesListsAndRefusesUnitExtensionOnUiThread()
    {
        WriteHello();
        File(Dir + @"\file1.TXT", "taken, in another letter case");
        _fs.CreateDirectory(Dir + @"\File2.txt"); // a directory of that name counts too (IDE-PM-111)
        var projectManagerFocus = new FocusState(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);
        _services.FocusTracker.Update(projectManagerFocus);
        Assert.False(_services.Commands.CanExecute("project.addNewFile")); // enabled as project.addNewUnit: a project is open
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.True(_services.Commands.CanExecute("project.addNewFile"));
        var documents = _services.Documents.Documents.Count;

        // A file row selected: the main source's folder (IDE-PM-040).
        ProjectManager.Select(ProjectManager.Find("Unit1.pas")!);
        await _services.Commands.ExecuteAsync("project.addNewFile");

        var path = Dir + @"\File3.txt";
        Assert.Empty(_fs.ReadAllBytes(path)); // zero bytes
        Assert.Equal(new ProjectFileItem("File3.txt", null, "other"), Projects.Project!.Files[^1]); // kind from the extension
        Assert.Contains("\"path\": \"File3.txt\"", Saved(), StringComparison.Ordinal); // saved at once (IDE-PM-004)
        Assert.Equal(documents, _services.Documents.Documents.Count); // not opened
        var row = Assert.Single(ProjectManager.Selection);
        Assert.Equal("File3.txt", row.Key);
        var naming = Assert.IsType<RenameFileViewModel>(row.Rename);
        Assert.Equal("File3".Length, naming.BaseNameLength); // UI-TW-117: the base name selected

        // IDE-PM-119: a unit needs its skeleton, a form its unit; the editor stays open with the message.
        naming.NewName = "Helper.PAS";
        Assert.Equal(Strings.Msg_Project_NewFileIsUnit, naming.Error);
        Assert.False(await naming.CommitAsync());
        naming.NewName = "Main.form";
        Assert.Equal(Strings.Status_AddFormViaUnit, naming.Error);
        Assert.False(await naming.CommitAsync());
        naming.NewName = "a?.txt"; // the validation of IDE-PM-070 for non-unit files
        Assert.Equal(Strings.Msg_NewFile_NameInvalid, naming.Error);
        Assert.Same(naming, row.Rename);
        Assert.True(_fs.FileExists(path));

        // A valid name renames file and entry.
        naming.NewName = "notes.md";
        Assert.Null(naming.Error);
        Assert.True(await naming.CommitAsync());
        Assert.False(_fs.FileExists(path));
        Assert.Empty(_fs.ReadAllBytes(Dir + @"\notes.md"));
        Assert.Equal("notes.md", Projects.Project!.Files[^1].Path);
        Assert.Equal(["notes.md"], ProjectManager.Selection.Select(n => n.Key));

        // A selected folder row is the target folder; a later rename of that file is an ordinary rename again.
        ProjectManager.Select(ProjectManager.Find("sub/")!);
        Assert.True(await Projects.AddNewFileAsync());
        Assert.Equal("sub/File1.txt", Projects.Project!.Files[^1].Path);
        Assert.Empty(_fs.ReadAllBytes(Dir + @"\sub\File1.txt"));
        Assert.Equal(["sub/File1.txt"], ProjectManager.Selection.Select(n => n.Key));
        ProjectManager.Selection[0].Rename!.Cancel(); // Escape keeps the provisional name
        Assert.True(_fs.FileExists(Dir + @"\sub\File1.txt"));
        Assert.True(Projects.ValidateRename("sub/File1.txt", "Later.pas").Success);
        Assert.Equal("Msg.Project.NewFileIsUnit", Projects.ValidateRename("sub/File1.txt", "Later.pas", newFile: true).MessageId);
        Assert.Empty(Dialogs.Requests);
    }

    /// <summary>
    /// A unit that cannot be written: <c>Msg.File.SaveFailed</c> with Retry and Cancel only (there is no document to
    /// "Save As" yet, as in the New File dialog, UI-DLG-040); Retry writes again, Cancel creates nothing.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-041")]
    public Task AddNewUnitWriteFailureOffersRetry() => TestUiThread.RunAsync(() => AddNewUnitWriteFailureOffersRetryOnUiThread()); // I-209

    private async Task AddNewUnitWriteFailureOffersRetryOnUiThread()
    {
        var fs = new RecordingFileSystem(_fs);
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Log = _log;
        });
        var dialogs = (FakeDialogService)services.Dialogs;
        WriteHello();
        Assert.True(await services.Projects.OpenAsync(ProjectPath));
        var unit3 = Dir + @"\Unit3.pas";
        var failures = 2;
        fs.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + unit3, StringComparison.Ordinal) && failures-- > 0;
        dialogs.Enqueue("Msg.File.SaveFailed", "Retry", "Retry");

        // Two failed writes, two answers Retry: the third write succeeds and the unit is added and opened.
        Assert.True(await services.Projects.AddNewUnitAsync());
        var asked = dialogs.Requests.Where(r => r.MessageId == "Msg.File.SaveFailed").ToList();
        Assert.Equal(2, asked.Count);
        Assert.All(asked, r => Assert.Equal(["Retry", "Cancel"], r.Buttons!));
        Assert.Equal(unit3, asked[0].Args[0]);
        Assert.True(_fs.FileExists(unit3));
        Assert.Equal("Unit3.pas", services.Projects.Project!.Files[^1].Path);
        Assert.Equal(unit3, services.Documents.ActiveDocument?.FilePath);

        // Cancel: nothing is created, the project is unchanged.
        var before = Saved();
        var unit4 = Dir + @"\Unit4.pas";
        fs.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + unit4, StringComparison.Ordinal);
        dialogs.Enqueue("Msg.File.SaveFailed", "Cancel");
        Assert.False(await services.Projects.AddNewUnitAsync());
        fs.FailWhen = null;
        Assert.Equal(3, dialogs.Requests.Count(r => r.MessageId == "Msg.File.SaveFailed"));
        Assert.Equal(["Retry", "Cancel"], dialogs.Requests[^1].Buttons!);
        Assert.False(_fs.FileExists(unit4));
        Assert.Equal(before, Saved());
        Assert.DoesNotContain(services.Projects.Project!.Files, f => f.Path == "Unit4.pas");
    }
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-050")]
    [Trait("Req", "IDE-PM-051")]
    [Trait("Req", "IDE-PM-052")]
    [Trait("Req", "IDE-PM-030")]
    public Task AddExistingDetectsFormCompanion() => TestUiThread.RunAsync(() => AddExistingDetectsFormCompanionOnUiThread()); // I-209

    private async Task AddExistingDetectsFormCompanionOnUiThread()
    {
        WriteHello();
        var main = File(Dir + @"\forms\Main.pas", "unit Main;\n\ninterface\n\n{$FORM Main.form}\n\nimplementation\n\nend.\n");
        var mainForm = File(Dir + @"\forms\Main.form", "object Main: TForm\nend\n");
        var other = File(Dir + @"\Other.pas", "unit Other;\n// {$FORM Other.form} is only a comment\ninterface\nimplementation\nend.\n");
        File(Dir + @"\Other.form", "object Other: TForm\nend\n");
        var near = File(@"C:\p\Shared\Near.pas", "unit Near;\ninterface\nimplementation\nend.\n");
        var far = File(@"D:\libs\Far.pas", "unit Far;\ninterface\nimplementation\nend.\n");
        var notes = File(Dir + @"\docs\readme.md", "# readme\n");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        var opened = _services.Documents.Documents.Count;

        // A .form file is refused with a status message; a file already in the project is skipped with one.
        Pickers.NextOpen(mainForm);
        await _services.Commands.ExecuteAsync("project.addExistingFile");
        Assert.Equal(Strings.Status_AddFormViaUnit, _services.Status.Message);
        Pickers.NextOpen(Dir + @"\UNIT1.pas");
        await _services.Commands.ExecuteAsync("project.addExistingFile");
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_FileAlreadyInProject, "UNIT1.pas"), _services.Status.Message);
        Assert.Equal(["Hello.pas", "Unit1.pas", "sub/Unit2.pas"], Projects.Project!.Files.Select(f => f.Path));

        Pickers.NextOpen(main, other, near, far, notes);
        await _services.Commands.ExecuteAsync("project.addExistingFile");

        Assert.Equal(
            [
                new ProjectFileItem("Hello.pas", null, "unit"),
                new ProjectFileItem("Unit1.pas", null, "unit"),
                new ProjectFileItem("sub/Unit2.pas", null, "unit"),
                new ProjectFileItem("forms/Main.pas", "forms/Main.form", "unit"),
                new ProjectFileItem("Other.pas", null, "unit"),
                new ProjectFileItem("../Shared/Near.pas", null, "unit"),
                new ProjectFileItem("D:/libs/Far.pas", null, "unit"),
                new ProjectFileItem("docs/readme.md", null, "other"),
            ],
            Projects.Project!.Files);
        var saved = Saved();
        Assert.Contains("\"form\": \"forms/Main.form\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\"", saved, StringComparison.Ordinal); // derived from the extension, not written
        Assert.True(Projects.IsMember(mainForm));
        Assert.Equal(opened, _services.Documents.Documents.Count); // added files are not opened
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-031")]
    public Task DuplicateUnitNameIsRefused() => TestUiThread.RunAsync(() => DuplicateUnitNameIsRefusedOnUiThread()); // I-209

    private async Task DuplicateUnitNameIsRefusedOnUiThread()
    {
        WriteHello();
        var duplicate = File(@"C:\other\unit1.pas", "unit unit1;\ninterface\nimplementation\nend.\n");
        var fresh = File(@"C:\other\Fresh.pas", "unit Fresh;\ninterface\nimplementation\nend.\n");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        var before = Saved();
        Dialogs.Answer("Msg.Project.DuplicateUnitName", "OK");

        Pickers.NextOpen(duplicate);
        await _services.Commands.ExecuteAsync("project.addExistingFile");

        var message = Assert.Single(Dialogs.Requests, r => r.MessageId == "Msg.Project.DuplicateUnitName");
        Assert.Equal(["unit1"], message.Args);
        Assert.Equal(before, Saved());
        Assert.Equal(3, Projects.Project!.Files.Count);

        // The other chosen files are still added.
        Pickers.NextOpen(duplicate, fresh);
        await _services.Commands.ExecuteAsync("project.addExistingFile");
        Assert.Equal("../../other/Fresh.pas", Projects.Project!.Files[^1].Path);
        Assert.Equal(4, Projects.Project!.Files.Count);
    }

    private FakeShellService Shell => (FakeShellService)_services.Shell;

    /// <summary>Focus in the Project Manager tree: file commands act on its selection (IDE-PM-060).</summary>
    private void FocusProjectManager() =>
        _services.FocusTracker.Update(new FocusState(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-060")]
    [Trait("Req", "IDE-PM-061")]
    public Task RemoveAndDeleteRules() => TestUiThread.RunAsync(() => RemoveAndDeleteRulesOnUiThread()); // I-209

    private async Task RemoveAndDeleteRulesOnUiThread()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" }, { "path": "notes.md" },
                         { "path": "Main.pas", "form": "Main.form" }, { "path": "Locked.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        foreach (var unit in new[] { "Unit1", "Unit2", "Main", "Locked" })
            File(Dir + $@"\{unit}.pas", $"unit {unit};\ninterface\nimplementation\nend.\n");
        File(Dir + @"\notes.md", "notes\n");
        File(Dir + @"\Main.form", "object Main: TForm\nend\n");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        IEnumerable<string> Paths() => Projects.Project!.Files.Select(f => f.Path);

        // The active document is the main source: nothing to remove or delete (menus.md §6).
        Assert.False(_services.Commands.CanExecute("project.removeFile"));
        Assert.False(_services.Commands.CanExecute("project.deleteFile"));

        // A selection with the main source is refused as a whole.
        Dialogs.Answer("Msg.Project.RemoveMainSource", "OK");
        Assert.False(await Projects.RemoveFilesAsync(["Hello.pas", "Unit1.pas"]));
        Assert.False(await Projects.DeleteFilesAsync(["Hello.pas"]));
        Assert.All(Dialogs.Requests, r => Assert.Equal(["Hello.pas"], r.Args));
        Assert.Equal(2, Dialogs.Requests.Count);
        Assert.Equal(["Hello.pas", "Unit1.pas", "Unit2.pas", "notes.md", "Main.pas", "Locked.pas"], Paths());

        // From the main menu with focus elsewhere the active document is the target; Cancel keeps it.
        var unit1 = await OpenDocumentAsync(Dir + @"\Unit1.pas");
        Assert.True(_services.Commands.CanExecute("project.removeFile"));
        Dialogs.Enqueue("Msg.Project.RemoveFileConfirm", "Cancel", "Remove");
        await _services.Commands.ExecuteAsync("project.removeFile");
        Assert.Contains("Unit1.pas", Paths());
        await _services.Commands.ExecuteAsync("project.removeFile");
        Assert.Equal(["Unit1.pas"], Dialogs.Requests[^1].Args);
        Assert.DoesNotContain("Unit1.pas", Paths());
        Assert.True(_fs.FileExists(Dir + @"\Unit1.pas")); // stays on disk
        Assert.Contains(unit1, _services.Documents.Documents); // stays open, now a non-member
        Assert.False(Projects.IsMember(unit1.FilePath));

        // With focus in the Project Manager its selection is the target; several files ask once with the count.
        FocusProjectManager();
        ProjectManager.Select(ProjectManager.Find("Unit2.pas")!);
        ProjectManager.Select(ProjectManager.Find("notes.md")!, ProjectSelectMode.Toggle);
        Dialogs.Answer("Msg.Project.RemoveFilesConfirm", "Remove");
        await _services.Commands.ExecuteAsync("project.removeFile");
        Assert.Equal(["2"], Dialogs.Requests[^1].Args);
        Assert.Equal(["Hello.pas", "Main.pas", "Locked.pas"], Paths());

        // Delete: a unit with a form lists both files; Cancel deletes nothing.
        var main = await OpenDocumentAsync(Dir + @"\Main.pas");
        Type(main, "x");
        FocusProjectManager();
        ProjectManager.Select(ProjectManager.Find("Main.pas")!);
        Dialogs.Enqueue("Msg.Project.DeleteFileConfirm", "Cancel", "Delete");
        await _services.Commands.ExecuteAsync("project.deleteFile");
        Assert.Equal(["Main.pas, Main.form"], Dialogs.Requests[^1].Args);
        Assert.Empty(Shell.Calls);
        Assert.Contains("Main.pas", Paths());

        // Delete moves both to the Recycle Bin, closes the modified document without a prompt, removes the entry.
        await _services.Commands.ExecuteAsync("project.deleteFile");
        Assert.Equal([$@"MoveToRecycleBin {Dir}\Main.pas", $@"MoveToRecycleBin {Dir}\Main.form"], Shell.Calls);
        Assert.False(_fs.FileExists(Dir + @"\Main.pas"));
        Assert.False(_fs.FileExists(Dir + @"\Main.form"));
        Assert.DoesNotContain(main, _services.Documents.Documents);
        Assert.DoesNotContain(Dialogs.Requests, r => r.MessageId == "Msg.File.SaveChangesOne");
        Assert.Equal(["Hello.pas", "Locked.pas"], Paths());

        // A file that cannot be recycled: Msg.Project.DeleteFailed, the entry and the file are kept.
        Shell.RecycleFailsFor = p => p.EndsWith("Locked.pas", StringComparison.Ordinal);
        Dialogs.Answer("Msg.Project.DeleteFileConfirm", "Delete");
        Dialogs.Answer("Msg.Project.DeleteFailed", "OK");
        Assert.False(await Projects.DeleteFilesAsync(["Locked.pas"]));
        Assert.Equal([Dir + @"\Locked.pas", FakeShellService.RecycleFailure], Dialogs.Requests[^1].Args);
        Assert.Equal(["Hello.pas", "Locked.pas"], Paths());
        Assert.True(_fs.FileExists(Dir + @"\Locked.pas"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-004")]
    [Trait("Req", "IDE-PM-033")]
    public Task FileOperationsSaveProjectImmediately() => TestUiThread.RunAsync(() => FileOperationsSaveProjectImmediatelyOnUiThread()); // I-209

    private async Task FileOperationsSaveProjectImmediatelyOnUiThread()
    {
        WriteHello();
        var extra = File(@"C:\p\Hello\Extra.pas", "unit Extra;\ninterface\nimplementation\nend.\n");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Dialogs.Answer("Msg.Project.RemoveFileConfirm", "Remove");
        Dialogs.Answer("Msg.Project.DeleteFileConfirm", "Delete");

        // Every successful file-list operation is on disk at once; the project is never left modified.
        Assert.True(await Projects.AddNewUnitAsync());
        Assert.Contains("\"path\": \"Unit3.pas\"", Saved(), StringComparison.Ordinal);
        Assert.Equal(1, await Projects.AddExistingFilesAsync([extra]));
        Assert.Contains("\"path\": \"Extra.pas\"", Saved(), StringComparison.Ordinal);
        Assert.True(await Projects.RemoveFilesAsync(["Unit1.pas"]));
        Assert.DoesNotContain("Unit1.pas", Saved(), StringComparison.Ordinal);
        Assert.True(await Projects.DeleteFilesAsync(["sub/Unit2.pas"]));
        Assert.DoesNotContain("Unit2.pas", Saved(), StringComparison.Ordinal);
        Assert.True(await Projects.RenameFileAsync("Unit3.pas", "Renamed.pas"));
        Assert.Contains("\"path\": \"Renamed.pas\"", Saved(), StringComparison.Ordinal);
        Assert.False(Projects.Project!.IsModified);

        // A project file that cannot be written: the failure is shown and the project stays as it was, in memory and on disk.
        var before = Saved();
        _fs.SetReadOnly(ProjectPath, true);
        Dialogs.Answer("Msg.File.SaveFailed", "OK");
        Assert.False(await Projects.RemoveFilesAsync(["Extra.pas"]));
        Assert.Equal(ProjectPath, Assert.Single(Dialogs.Requests, r => r.MessageId == "Msg.File.SaveFailed").Args[0]);
        Assert.Contains("Extra.pas", Projects.Project!.Files.Select(f => f.Path));
        Assert.Equal(before, Saved());

        // A failed project save after deleting: the file is in the Recycle Bin, the entry stays (shown as missing).
        Assert.False(await Projects.DeleteFilesAsync(["Extra.pas"]));
        Assert.False(_fs.FileExists(extra));
        Assert.Contains("Extra.pas", Projects.Project!.Files.Select(f => f.Path));
        Assert.Equal(before, Saved());
    }

    private const string MainUnit = "{ Main unit }\nunit Main;\n\ninterface\n\n{$FORM Main.form}\n\nimplementation\n// unit Main; stays\nend.\n";

    /// <summary>Hello.pproj: main source Hello.pas, forms/Main.pas with forms/Main.form, Unit1.pas, notes.md.</summary>
    private void WriteRenameProject()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "forms/Main.pas", "form": "forms/Main.form" }, { "path": "Unit1.pas" },
                         { "path": "notes.md" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\n\nuses Main;\n\nbegin\nend.\n");
        File(Dir + @"\forms\Main.pas", MainUnit);
        File(Dir + @"\forms\Main.form", "object Main: TForm\nend\n");
        File(Dir + @"\Unit1.pas", "unit Unit1;\ninterface\nimplementation\nend.\n");
        File(Dir + @"\notes.md", "notes\n");
    }

    private string Text(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-070")]
    [Trait("Req", "IDE-PM-071")]
    [Trait("Req", "IDE-PM-072")]
    [Trait("Req", "IDE-PM-073")]
    [Trait("Req", "UI-DLG-070")]
    [Trait("Req", "UI-DLG-071")]
    [Trait("Req", "UI-DLG-073")]
    public Task RenameUpdatesHeadingFormAndProject() => TestUiThread.RunAsync(() => RenameUpdatesHeadingFormAndProjectOnUiThread()); // I-209

    private async Task RenameUpdatesHeadingFormAndProjectOnUiThread()
    {
        WriteRenameProject();
        File(Dir + @"\forms\Taken.pas", "unit Taken;\n");
        Assert.True(await Projects.OpenAsync(ProjectPath));

        // Validation shared by the dialog and the in-place editor (IDE-PM-070, UI-DLG-071).
        OperationResult Check(string path, string name) => Projects.ValidateRename(path, name);
        Assert.True(Check("forms/Main.pas", "Main.pas").Success); // unchanged: a no-op
        Assert.True(Check("forms/Main.pas", "MAIN.pas").Success); // only the case changes
        Assert.Equal("Msg.Project.RenameInvalid", Check("forms/Main.pas", "Main.txt").MessageId);
        Assert.Equal("Msg.Project.RenameInvalid", Check("forms/Main.pas", "1Main.pas").MessageId);
        Assert.Equal("Msg.Project.RenameInvalid", Check("forms/Main.pas", "begin.pas").MessageId);
        Assert.Equal(("Msg.Project.RenameExists", "taken.pas"), (Check("forms/Main.pas", "taken.pas").MessageId, Check("forms/Main.pas", "taken.pas").Args[0]));
        Assert.Equal(("Msg.Project.DuplicateUnitName", "unit1"), (Check("forms/Main.pas", "unit1.pas").MessageId, Check("forms/Main.pas", "unit1.pas").Args[0]));
        Assert.Equal("Msg.NewFile.NameInvalid", Check("notes.md", "a:b.md").MessageId);
        Assert.Equal("Msg.NewFile.NameInvalid", Check("notes.md", "").MessageId);
        Assert.True(Check("notes.md", "readme.txt").Success);

        // An open, modified unit with a form: saved first, both files renamed, heading and {$FORM} updated by the lexer.
        var main = await OpenDocumentAsync(Dir + @"\forms\Main.pas");
        main.Buffer.ApplyEdits([new TextEdit(main.Buffer.Text.Length, 0, "// edited\n")]);
        var hello = Text(Dir + @"\Hello.pas");
        Assert.True(await Projects.RenameFileAsync("forms/Main.pas", "Login.pas"));

        Assert.False(_fs.FileExists(Dir + @"\forms\Main.pas"));
        Assert.False(_fs.FileExists(Dir + @"\forms\Main.form"));
        Assert.Equal("{ Main unit }\nunit Login;\n\ninterface\n\n{$FORM Login.form}\n\nimplementation\n// unit Main; stays\nend.\n// edited\n",
            Text(Dir + @"\forms\Login.pas"));
        Assert.Equal("object Main: TForm\nend\n", Text(Dir + @"\forms\Login.form"));
        Assert.Contains(new ProjectFileItem("forms/Login.pas", "forms/Login.form", "unit"), Projects.Project!.Files);
        Assert.DoesNotContain("forms/Main", Saved(), StringComparison.Ordinal);
        Assert.Equal(Dir + @"\forms\Login.pas", main.FilePath);
        Assert.Equal("Login.pas", main.Title);
        Assert.False(main.IsModified);
        Assert.Equal(hello, Text(Dir + @"\Hello.pas")); // uses clauses elsewhere are not updated (IDE-PM-072)
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_RenameUnitNote, "Main.pas", "Login.pas"), _services.Status.Message);
        Assert.Equal(["forms/Login.pas"], ProjectManager.Selection.Select(n => n.Key)); // IDE-PM-033

        // A closed unit, only the case changes; the heading follows the new name.
        Assert.True(await Projects.RenameFileAsync("Unit1.pas", "UNIT1.pas"));
        Assert.Equal("unit UNIT1;\ninterface\nimplementation\nend.\n", Text(Dir + @"\UNIT1.pas"));
        Assert.Contains(_fs.EnumerateFiles(Dir, "*.pas"), f => Path.GetFileName(f) == "UNIT1.pas");
        Assert.Contains("\"path\": \"UNIT1.pas\"", Saved(), StringComparison.Ordinal);

        // From the main menu with focus elsewhere: the RenameFile dialog for the active document. The main source is
        // renamed with its program heading; the .pproj and the project name stay (IDE-PM-073).
        RenameFileViewModel? dialog = null;
        Dialogs.Script(RenameFileViewModel.DialogName, async vm =>
        {
            dialog = (RenameFileViewModel)vm;
            Assert.Equal("Hello.pas", dialog.NewName);
            Assert.True(dialog.IsUnit);
            Assert.False(dialog.Rename.IsEnabled); // unchanged name
            dialog.NewName = "Login.pas";
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_DuplicateUnitName, "Login"), dialog.Error);
            Assert.False(dialog.Rename.IsEnabled);
            dialog.NewName = "Start.pas";
            Assert.Null(dialog.Error);
            await dialog.PressDefaultAsync();
        });
        _services.Documents.Activate(_services.Documents.Documents.Single(d => d.FilePath == Dir + @"\Hello.pas"));
        Assert.True(_services.Commands.CanExecute("project.renameFile"));
        await _services.Commands.ExecuteAsync("project.renameFile");
        Assert.True(dialog!.IsClosed);
        Assert.Equal("program Start;\n\nuses Main;\n\nbegin\nend.\n", Text(Dir + @"\Start.pas"));
        Assert.Equal("Start.pas", Projects.Project!.MainSource);
        Assert.Equal(("Hello", ProjectPath), (Projects.Project.Name, Projects.Project.FilePath));
        Assert.Equal(Dir + @"\Start.pas", _services.Documents.ActiveDocument!.FilePath);

        // Other files: any valid file name, the text is untouched; an unchanged name does nothing.
        Assert.True(await Projects.RenameFileAsync("notes.md", "notes.md"));
        Assert.True(await Projects.RenameFileAsync("notes.md", "readme.txt"));
        Assert.Equal("notes\n", Text(Dir + @"\readme.txt"));
        Assert.Contains("\"path\": \"readme.txt\"", Saved(), StringComparison.Ordinal);
        Assert.False(Projects.Project!.IsModified);
    }

    /// <summary>
    /// UI-DLG-072: the Rename File dialog offers <c>UpdateReferences</c> only for Pascal units, checked by default; with it
    /// cleared the unit keeps its heading while the file, its form companion, the <c>{$FORM}</c> directive and the
    /// <c>.pproj</c> still follow the new name.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-072")]
    public Task UpdateReferencesIsOfferedForUnitsOnly() => TestUiThread.RunAsync(() => UpdateReferencesIsOfferedForUnitsOnlyOnUiThread()); // I-209

    private async Task UpdateReferencesIsOfferedForUnitsOnlyOnUiThread()
    {
        WriteRenameProject();
        Assert.True(await Projects.OpenAsync(ProjectPath));

        RenameFileViewModel? dialog = null;
        Dialogs.Script(RenameFileViewModel.DialogName, async vm =>
        {
            dialog = (RenameFileViewModel)vm;
            await dialog.PressCancelAsync();
        });
        await OpenDocumentAsync(Dir + @"\notes.md");
        await _services.Commands.ExecuteAsync("project.renameFile");
        Assert.False(dialog!.IsUnit); // a text file: no check box

        Dialogs.Script(RenameFileViewModel.DialogName, async vm =>
        {
            dialog = (RenameFileViewModel)vm;
            Assert.True(dialog.IsUnit);
            Assert.True(dialog.UpdateReferences); // checked by default
            dialog.UpdateReferences = false;
            dialog.NewName = "Login.pas";
            await dialog.PressDefaultAsync();
        });
        await OpenDocumentAsync(Dir + @"\forms\Main.pas");
        await _services.Commands.ExecuteAsync("project.renameFile");

        var text = Text(Dir + @"\forms\Login.pas");
        Assert.Contains("unit Main;", text, StringComparison.Ordinal); // the heading stays
        Assert.Contains("{$FORM Login.form}", text, StringComparison.Ordinal);
        Assert.True(_fs.FileExists(Dir + @"\forms\Login.form"));
        Assert.Contains(new ProjectFileItem("forms/Login.pas", "forms/Login.form", "unit"), Projects.Project!.Files);
        Assert.Contains("forms/Login.pas", Saved(), StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-071")]
    public Task RenameRollsBackOnFailure() => TestUiThread.RunAsync(() => RenameRollsBackOnFailureOnUiThread()); // I-209

    private async Task RenameRollsBackOnFailureOnUiThread()
    {
        var fs = new RecordingFileSystem(_fs);
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Log = _log;
        });
        var dialogs = (FakeDialogService)services.Dialogs;
        var projects = services.Projects;
        WriteRenameProject();
        Assert.True(await projects.OpenAsync(ProjectPath));
        var project = Saved();
        var files = _fs.EnumerateFiles(Dir + @"\forms", "*").Order().ToList();
        dialogs.Answer("Msg.Project.RenameFailed", "OK");

        async Task FailsAsync(Func<string, bool> failWhen)
        {
            fs.FailWhen = failWhen;
            var before = dialogs.Requests.Count;
            Assert.False(await projects.RenameFileAsync("forms/Main.pas", "Login.pas"));
            fs.FailWhen = null;
            var request = Assert.Single(dialogs.Requests.Skip(before));
            Assert.Equal("Msg.Project.RenameFailed", request.MessageId);
            Assert.Equal(Dir + @"\forms\Main.pas", request.Args[0]);
            Assert.StartsWith("Injected failure", request.Args[1], StringComparison.Ordinal);
            Assert.Equal(files, _fs.EnumerateFiles(Dir + @"\forms", "*").Order().ToList());
            Assert.Equal(MainUnit, Text(Dir + @"\forms\Main.pas"));
            Assert.Equal(project, Saved());
            Assert.Contains(new ProjectFileItem("forms/Main.pas", "forms/Main.form", "unit"), projects.Project!.Files);
        }

        // Each step that fails undoes the ones before it: the form rename, the project save, the text update.
        await FailsAsync(call => call.StartsWith("MoveFile", StringComparison.Ordinal) && call.Contains(@"Main.form -> ", StringComparison.Ordinal));
        await FailsAsync(call => call.StartsWith("WriteAllBytesDurable " + ProjectPath, StringComparison.Ordinal));
        await FailsAsync(call => call.StartsWith("WriteAllBytesDurable " + Dir + @"\forms\Login.pas", StringComparison.Ordinal));

        // An open document: its text, path and save point are restored too.
        var main = (TextDocumentViewModel)(await services.Documents.OpenAsync(Dir + @"\forms\Main.pas")).Value!;
        await FailsAsync(call => call.StartsWith("WriteAllBytesDurable " + Dir + @"\forms\Login.pas", StringComparison.Ordinal));
        Assert.Equal((MainUnit, Dir + @"\forms\Main.pas", false), (main.Buffer.Text, main.FilePath, main.IsModified));

        // A modified document whose save fails (step 1) aborts before anything is renamed.
        main.Buffer.ApplyEdits([new TextEdit(0, 0, "// x\n")]);
        dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        fs.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + Dir + @"\forms\Main.pas", StringComparison.Ordinal);
        Assert.False(await projects.RenameFileAsync("forms/Main.pas", "Login.pas"));
        fs.FailWhen = null;
        Assert.Equal("Msg.File.SaveFailed", dialogs.Requests[^1].MessageId);
        Assert.Equal(files, _fs.EnumerateFiles(Dir + @"\forms", "*").Order().ToList());
        Assert.Equal(project, Saved());
        Assert.True(main.IsModified);
    }
}
