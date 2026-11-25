using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>Folders in the Project Manager (phase8-project-files.md §2): New Folder, empty folders, removing one.</summary>
[Trait("Phase", "8")]
public sealed class ProjectFolderTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";
    private const string SessionPath = Dir + @"\.freexcomp\session.json";

    private static readonly FocusState ProjectManagerFocus = new(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);

    private readonly InMemoryFileSystem _fs = new();
    private readonly IdeServices _services;

    public ProjectFolderTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
    }

    private ProjectManagerViewModel Pm => _services.ProjectManager;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Hello.pproj: main source Hello.pas, members Unit1.pas and lib/Unit2.pas; opened, the tree focused.</summary>
    private async Task OpenHelloAsync()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "lib/Unit2.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        File(Dir + @"\Unit1.pas", "unit Unit1;\ninterface\nimplementation\nend.\n");
        File(Dir + @"\lib\Unit2.pas", "unit Unit2;\ninterface\nimplementation\nend.\n");
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        _services.FocusTracker.Update(ProjectManagerFocus);
        await Pm.MissingFileChecks;
    }

    private IReadOnlyList<string> Keys() => [.. Pm.Roots.SelectMany(r => r.SelfAndDescendants()).Select(n => n.Key)];

    private string Session() => Encoding.UTF8.GetString(_fs.ReadAllBytes(SessionPath));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-110")]
    [Trait("Req", "IDE-PM-111")]
    [Trait("Req", "UI-TW-116")]
    public Task NewFolder_CreatesDirectoryAndStartsRename() => TestUiThread.RunAsync(() => NewFolder_CreatesDirectoryAndStartsRenameOnUiThread());

    private async Task NewFolder_CreatesDirectoryAndStartsRenameOnUiThread()
    {
        var commands = _services.Commands;
        Assert.False(commands.CanExecute("project.newFolder")); // no project
        await OpenHelloAsync();
        File(Dir + @"\NewFolder1", "a file of that name"); // IDE-PM-111: files count too, ignoring case
        _fs.CreateDirectory(Dir + @"\newfolder2");

        // IDE-PM-110: the root or a folder row inside the project; not a file row, not a multi-selection.
        Pm.Select(Pm.Find("Unit1.pas")!);
        Assert.False(commands.CanExecute("project.newFolder"));
        Pm.Select(Pm.Find("Hello.pas")!, ProjectSelectMode.Toggle);
        Assert.False(commands.CanExecute("project.newFolder"));
        Pm.ClearSelection();
        Assert.False(commands.CanExecute("project.newFolder"));
        Pm.Select(Pm.Find(ProjectManagerViewModel.RootKey)!);
        Assert.True(commands.CanExecute("project.newFolder"));

        // Root: the smallest free NewFolder<N> in the project directory, remembered, selected, being named.
        await commands.ExecuteAsync("project.newFolder");
        Assert.True(_fs.DirectoryExists(Dir + @"\NewFolder3"));
        Assert.Equal(["NewFolder3/"], Pm.EmptyFolders);
        var row = Assert.Single(Pm.Selection);
        Assert.Equal("NewFolder3/", row.Key);
        Assert.Equal(ProjectNodeKind.Folder, row.Kind);
        Assert.Equal(Dir + @"\NewFolder3", row.FullPath); // UI-TW-115: the tooltip is the directory
        Assert.Empty(row.Children);
        var naming = Assert.IsType<FreexComp.Ide.Core.Dialogs.RenameFileViewModel>(row.Rename);
        Assert.Equal("NewFolder3", naming.NewName);
        Assert.Equal("NewFolder3".Length, naming.BaseNameLength); // UI-TW-116: all text selected
        Assert.True(naming.CancelInvalidOnFocusLoss);

        // Escape keeps the provisional name (IDE-PM-112).
        naming.Cancel();
        Assert.Null(row.Rename);
        Assert.True(_fs.DirectoryExists(Dir + @"\NewFolder3"));
        Assert.Contains("NewFolder3/", Keys());

        // A folder row: the new folder goes inside it, the parent is expanded; committing renames the directory.
        var lib = Pm.Find("lib/")!;
        lib.IsExpanded = false;
        Pm.Select(lib);
        await commands.ExecuteAsync("project.newFolder");
        Assert.True(Pm.Find("lib/")!.IsExpanded);
        var inner = Assert.Single(Pm.Selection);
        Assert.Equal("lib/NewFolder1/", inner.Key);
        inner.Rename!.NewName = "  data ";
        Assert.Null(inner.Rename.Error);
        Assert.True(await inner.Rename.CommitAsync());
        Assert.False(_fs.DirectoryExists(Dir + @"\lib\NewFolder1"));
        Assert.True(_fs.DirectoryExists(Dir + @"\lib\data"));
        Assert.Equal(["NewFolder3/", "lib/data/"], Pm.EmptyFolders);
        Assert.Equal(["lib/data/"], Pm.Selection.Select(n => n.Key));
        Assert.Null(Pm.Find("lib/NewFolder1/"));

        // The External group is no target; the IDE state must be Idle (IDE-PM-032).
        Assert.Empty(Dialogs.Requests);
    }

    [Theory(Timeout = 30000)]
    [InlineData("", "Msg.Project.FolderNameInvalid")]
    [InlineData(" .. ", "Msg.Project.FolderNameInvalid")]
    [InlineData("a/b", "Msg.Project.FolderNameInvalid")]
    [InlineData("a\tb", "Msg.Project.FolderNameInvalid")]
    [InlineData("con", "Msg.Project.FolderNameInvalid")]
    [InlineData("LPT1.txt", "Msg.Project.FolderNameInvalid")]
    [InlineData("data.", "Msg.Project.FolderNameInvalid")]
    [InlineData("LIB", "Msg.Project.FolderExists")]
    [InlineData("unit1.pas", "Msg.Project.FolderExists")]
    [InlineData("newfolder1", null)] // its own name in another letter case
    [InlineData("console", null)]
    [InlineData("  src v2  ", null)]
    [Trait("Req", "IDE-PM-112")]
    [Trait("Req", "UI-TW-116")]
    public Task NewFolder_NameValidation(string typed, string? messageId) =>
        TestUiThread.RunAsync(() => NewFolder_NameValidationOnUiThread(typed, messageId));

    private async Task NewFolder_NameValidationOnUiThread(string typed, string? messageId)
    {
        await OpenHelloAsync();
        Pm.Select(Pm.Find(ProjectManagerViewModel.RootKey)!);
        Assert.True(await Pm.NewFolderAsync());
        var row = Assert.Single(Pm.Selection);
        var naming = row.Rename!;

        naming.NewName = typed;

        var name = typed.Trim(' ');
        if (messageId is null)
        {
            Assert.Null(naming.Error);
            Assert.True(await naming.CommitAsync());
            Assert.True(_fs.DirectoryExists(Path.Combine(Dir, name)));
            Assert.Equal([name + "/"], Pm.EmptyFolders);
            Assert.Equal([name + "/"], Pm.Selection.Select(n => n.Key));
            Assert.Equal(name, Pm.Selection[0].Name); // the typed letter case
            return;
        }

        // The first failure is shown inline and the editor stays open; nothing is renamed.
        var expected = messageId == "Msg.Project.FolderExists"
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Project_FolderExists, name)
            : Strings.Msg_Project_FolderNameInvalid;
        Assert.Equal(expected, naming.Error);
        Assert.False(await naming.CommitAsync());
        Assert.Same(naming, row.Rename);
        Assert.True(_fs.DirectoryExists(Dir + @"\NewFolder1"));

        // UI-TW-116: an invalid name on focus loss cancels the edit and keeps the provisional name.
        await naming.FocusLostAsync();
        Assert.Null(row.Rename);
        Assert.Equal(["NewFolder1/"], Pm.EmptyFolders);
        Assert.Empty(Dialogs.Requests);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-111")]
    public Task NewFolder_FailureChangesNothing() => TestUiThread.RunAsync(() => NewFolder_FailureChangesNothingOnUiThread());

    private async Task NewFolder_FailureChangesNothingOnUiThread()
    {
        var recording = new RecordingFileSystem(_fs);
        var services = TestComposition.Build(o => o.FileSystem = recording);
        File(ProjectPath, """{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas" }""");
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        Assert.True(await services.Projects.OpenAsync(ProjectPath));
        var pm = services.ProjectManager;
        await pm.MissingFileChecks;
        pm.Select(pm.Find(ProjectManagerViewModel.RootKey)!);
        recording.FailWith = call => call.StartsWith("CreateDirectory " + Dir + @"\NewFolder1", StringComparison.Ordinal)
            ? new UnauthorizedAccessException("Injected denial")
            : null;
        var dialogs = (FakeDialogService)services.Dialogs;
        dialogs.Answer("Msg.Project.NewFolderFailed", "OK");

        Assert.False(await pm.NewFolderAsync());

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal("Msg.Project.NewFolderFailed", request.MessageId);
        Assert.Equal([Dir + @"\NewFolder1", "Injected denial"], request.Args);
        Assert.Empty(pm.EmptyFolders);
        Assert.Null(pm.Find("NewFolder1/"));
        Assert.False(_fs.DirectoryExists(Dir + @"\NewFolder1"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-113")]
    [Trait("Req", "IDE-PM-114")]
    [Trait("Req", "UI-TW-115")]
    public Task EmptyFoldersPersistInSessionAndDropWhenGone() => TestUiThread.RunAsync(() => EmptyFoldersPersistInSessionAndDropWhenGoneOnUiThread());

    private async Task EmptyFoldersPersistInSessionAndDropWhenGoneOnUiThread()
    {
        // The session lists folders in ordinal order: one that exists with a sub-folder, one whose directory is gone, one
        // outside the project directory, a duplicate in another letter case, and one that holds a project file.
        _fs.CreateDirectory(Dir + @"\assets\img");
        _fs.CreateDirectory(Dir + @"\Zeta");
        _fs.CreateDirectory(@"C:\p\Other");
        File(SessionPath, """
            { "formatVersion": 1, "project": "Hello.pproj",
              "projectManager": { "expanded": ["$project", "assets/"], "selected": "assets/img/",
                                  "folders": ["../Other/", "Zeta/", "assets/img/", "gone/", "lib/", "zeta/"], "ignoredFiles": [] } }
            """);
        await OpenHelloAsync();

        // IDE-PM-114: the union of derived and remembered folders with every ancestor; UI-TW-102 order.
        Assert.Equal(["Zeta/", "assets/img/", "lib/"], Pm.EmptyFolders);
        Assert.Equal(["$project", "assets/", "assets/img/", "lib/", "lib/Unit2.pas", "Zeta/", "Hello.pas", "Unit1.pas", "$platforms/", "$platforms/win64"], Keys());
        Assert.Equal(["assets/img/"], Pm.Selection.Select(n => n.Key)); // restored like any row
        var img = Pm.Find("assets/img/")!;
        Assert.Equal(ProjectNodeKind.Folder, img.Kind);
        Assert.Empty(img.Children); // UI-TW-115: no expander without children
        Assert.Equal(Dir + @"\assets\img", img.FullPath);

        // An empty folder is a target of New Unit and New File (IDE-PM-114).
        Pm.Select(img);
        Assert.Equal(Dir + @"\assets\img", Pm.SelectedFolderPath);
        Assert.True(await _services.Projects.AddNewUnitAsync());
        Assert.Equal("assets/img/Unit3.pas", _services.Projects.Project!.Files[^1].Path);
        Assert.Contains("assets/img/", Pm.EmptyFolders); // kept (harmless) while the folder holds a file

        // Written with the session: ordinal order, no duplicates.
        _services.Session.Save();
        Assert.Contains("\"folders\": [\n      \"Zeta/\",\n      \"assets/img/\",\n      \"lib/\"\n    ]", Session().Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

        // A directory removed outside the IDE: its entry is dropped at the next rebuild.
        _fs.DeleteDirectory(Dir + @"\Zeta");
        Pm.Select(Pm.Find(ProjectManagerViewModel.RootKey)!);
        Assert.True(await Pm.NewFolderAsync()); // rebuilds
        Assert.Equal(["NewFolder1/", "assets/img/", "lib/"], Pm.EmptyFolders);
        Assert.Null(Pm.Find("Zeta/"));

        // Another project starts without them.
        Assert.True(await _services.Projects.CloseAsync());
        Assert.Empty(Pm.EmptyFolders);
        Assert.Contains("\"NewFolder1/\"", Session(), StringComparison.Ordinal); // saved on close
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-115")]
    public Task RemoveEmptyFolder_DeletesOnlyEmptyDirectories() => TestUiThread.RunAsync(() => RemoveEmptyFolder_DeletesOnlyEmptyDirectoriesOnUiThread());

    private async Task RemoveEmptyFolder_DeletesOnlyEmptyDirectoriesOnUiThread()
    {
        _fs.CreateDirectory(Dir + @"\assets\img");
        _fs.CreateDirectory(Dir + @"\assets\raw");
        File(Dir + @"\assets\raw\photo.bin", "not a project file");
        _fs.CreateDirectory(Dir + @"\tmp");
        File(SessionPath, """
            { "formatVersion": 1, "project": "Hello.pproj",
              "projectManager": { "expanded": [], "selected": null, "folders": ["assets/img/", "assets/raw/", "tmp/"] } }
            """);
        await OpenHelloAsync();
        var commands = _services.Commands;

        // A folder with a project file in it or below it cannot be removed; rename and delete stay disabled on folders.
        Pm.Select(Pm.Find("lib/")!);
        Assert.False(Pm.CanRemoveSelectedFolder);
        Assert.False(commands.CanExecute("project.removeFile"));
        Assert.False(commands.CanExecute("project.deleteFile"));
        Assert.False(commands.CanExecute("project.renameFile"));

        // An empty one: no confirmation, the directory is deleted, the parent row selected.
        Pm.Select(Pm.Find("tmp/")!);
        Assert.True(commands.CanExecute("project.removeFile"));
        Assert.False(commands.CanExecute("project.deleteFile"));
        Assert.False(commands.CanExecute("project.renameFile"));
        await commands.ExecuteAsync("project.removeFile");
        Assert.False(_fs.DirectoryExists(Dir + @"\tmp"));
        Assert.Null(Pm.Find("tmp/"));
        Assert.Equal([ProjectManagerViewModel.RootKey], Pm.Selection.Select(n => n.Key));

        // With sub-folders, deepest first: the empty ones are deleted, a directory that holds another file stays on
        // disk (and so does its parent) and merely disappears from the tree.
        Pm.Select(Pm.Find("assets/")!);
        await commands.ExecuteAsync("project.removeFile");
        Assert.False(_fs.DirectoryExists(Dir + @"\assets\img"));
        Assert.True(_fs.FileExists(Dir + @"\assets\raw\photo.bin"));
        Assert.True(_fs.DirectoryExists(Dir + @"\assets"));
        Assert.Empty(Pm.EmptyFolders);
        Assert.Equal(["$project", "lib/", "lib/Unit2.pas", "Hello.pas", "Unit1.pas", "$platforms/", "$platforms/win64"], Keys());
        Assert.Empty(Dialogs.Requests); // nothing is lost, nothing is asked

        // Outside the Project Manager the command is about the active document again.
        Pm.Select(Pm.Find("lib/")!);
        _services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, null, false));
        Assert.Null(((ProjectService)_services.Projects).RemovableFolder!());
    }
}
