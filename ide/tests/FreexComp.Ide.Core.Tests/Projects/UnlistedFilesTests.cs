using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>Files added outside the IDE (phase8-project-files.md §7): detection, the info bar and its three actions.</summary>
[Trait("Phase", "8")]
public sealed class UnlistedFilesTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";
    private const string SessionPath = Dir + @"\.freexcomp\session.json";
    private const string Unit = "unit {0};\ninterface\nimplementation\nend.\n";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeShellService _shell = new();
    private readonly IdeServices _services;

    public UnlistedFilesTests()
    {
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Shell = _shell;
        });
    }

    private ProjectManagerViewModel Pm => _services.ProjectManager;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private void UnitFile(string relativePath) =>
        File(Path.Combine(Dir, relativePath), string.Format(CultureInfo.InvariantCulture, Unit, Path.GetFileNameWithoutExtension(relativePath)));

    /// <summary>Hello.pproj (output in <c>bin/Debug</c>) with Hello.pas, Unit1.pas, lib/Unit2.pas and <paramref name="extraFiles"/>.</summary>
    private void WriteHello(string extraFiles = "")
    {
        File(ProjectPath, $$"""
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "activeConfiguration": "Debug",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "lib/Unit2.pas" }{{extraFiles}} ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        UnitFile("Unit1.pas");
        UnitFile(@"lib\Unit2.pas");
    }

    private async Task OpenAsync()
    {
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        await Pm.MissingFileChecks;
    }

    /// <summary>Main-window activation: one detection, awaited (IDE-PM-146).</summary>
    private async Task DetectAsync()
    {
        Pm.OnWindowActivated();
        await Pm.MissingFileChecks;
    }

    private static string Many(int count) => string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_UnlistedFilesMany, count);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-145")]
    [Trait("Req", "IDE-PM-146")]
    [Trait("Req", "IDE-PM-150")]
    [Trait("Req", "UI-TW-123")]
    public Task DetectsOnlyDirectProjectFolders() => TestUiThread.RunAsync(() => DetectsOnlyDirectProjectFoldersOnUiThread());

    private async Task DetectsOnlyDirectProjectFoldersOnUiThread()
    {
        // Project files also in directories that never count: a dot directory, obj, the output directory's first segment.
        WriteHello(""", { "path": ".tools/Gen.pas" }, { "path": "obj/Cache.pas" }, { "path": "bin/Stub.pas" }, { "path": "notes.md" }, { "path": "../Shared/Common.pas" }""");
        UnitFile(@".tools\Gen.pas");
        UnitFile(@"obj\Cache.pas");
        UnitFile(@"bin\Stub.pas");
        File(Dir + @"\notes.md", "notes\n");
        File(@"C:\p\Shared\Common.pas", string.Format(CultureInfo.InvariantCulture, Unit, "Common"));
        _fs.CreateDirectory(Dir + @"\assets");
        File(SessionPath, """
            { "formatVersion": 1, "project": "Hello.pproj",
              "projectManager": { "expanded": [], "selected": null, "folders": ["assets/"], "ignoredFiles": [] } }
            """);

        await OpenAsync();
        Assert.Null(Pm.UnlistedFilesBar); // nothing unlisted: no bar
        Assert.Empty(Pm.UnlistedFiles);

        // Unlisted: directly in the project directory, in a directory with a project file, in a remembered empty folder.
        UnitFile("Extra.pas");
        UnitFile(@"lib\More.PAS"); // the extension ignoring case
        UnitFile(@"assets\Art.pas");
        // Not unlisted: other sub-directories, excluded directories, other extensions, files outside the project directory.
        UnitFile(@"other\Loose.pas");
        UnitFile(@"lib\deep\Deeper.pas");
        UnitFile(@".tools\Hidden.pas");
        UnitFile(@"obj\Temp.pas");
        UnitFile(@"bin\Out.pas");
        File(Dir + @"\Readme.txt", "text\n");
        File(Dir + @"\Unit9.pas.bak", "backup\n");
        File(@"C:\p\Shared\Other.pas", string.Format(CultureInfo.InvariantCulture, Unit, "Other"));
        var watchers = ((ManualFileWatcherFactory)_services.FileWatchers).ActiveDirectories.Count;

        // Nothing is watched for this (IDE-PM-150): the files show up at the next check, not before.
        Assert.Null(Pm.UnlistedFilesBar);
        await DetectAsync();

        Assert.Equal([Dir + @"\Extra.pas", Dir + @"\lib\More.PAS", Dir + @"\assets\Art.pas"], Pm.UnlistedFiles);
        var bar = Assert.IsType<InfoBarViewModel>(Pm.UnlistedFilesBar);
        Assert.Equal(ProjectManagerViewModel.UnlistedFilesKind, bar.Kind);
        Assert.Equal(InfoBarSeverity.Info, bar.Severity);
        Assert.Equal(Many(3), bar.Text);
        Assert.Equal(["AddToProject", "Show", "Ignore"], bar.Actions.Select(a => a.Name));
        Assert.Equal([Strings.InfoBar_Action_AddToProject, Strings.Button_Show, Strings.InfoBar_Action_Ignore], bar.Actions.Select(a => a.Label));
        Assert.False(bar.CanDismiss); // Ignore is the dismissal
        Assert.Null(Pm.MissingFilesBar);
        Assert.Equal(watchers, ((ManualFileWatcherFactory)_services.FileWatchers).ActiveDirectories.Count);

        // The same result keeps the same bar; one file: the singular text.
        await DetectAsync();
        Assert.Same(bar, Pm.UnlistedFilesBar);
        _fs.DeleteFile(Dir + @"\lib\More.PAS");
        _fs.DeleteFile(Dir + @"\assets\Art.pas");
        await DetectAsync();
        Assert.Equal(Strings.Msg_Project_UnlistedFilesOne, Pm.UnlistedFilesBar!.Text);

        // A file operation runs the detection too (IDE-PM-103): the new unit of the IDE itself is a project file.
        Assert.True(await _services.Projects.AddNewUnitAsync());
        await Pm.MissingFileChecks;
        Assert.Equal([Dir + @"\Extra.pas"], Pm.UnlistedFiles);

        // Gone from disk: the bar goes.
        _fs.DeleteFile(Dir + @"\Extra.pas");
        await DetectAsync();
        Assert.Null(Pm.UnlistedFilesBar);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-147")]
    [Trait("Req", "IDE-PM-148")]
    public Task AddShowIgnoreActions() => TestUiThread.RunAsync(() => AddShowIgnoreActionsOnUiThread());

    private async Task AddShowIgnoreActionsOnUiThread()
    {
        WriteHello();
        UnitFile("Scratch.pas");
        UnitFile(@"lib\Old.pas");
        File(SessionPath, """
            { "formatVersion": 1, "project": "Hello.pproj",
              "projectManager": { "expanded": [], "selected": null, "folders": [], "ignoredFiles": ["lib/Gone.pas", "Unit1.pas"] } }
            """);
        await OpenAsync();
        InfoBarAction Action(string name) => Pm.UnlistedFilesBar!.Actions.Single(a => a.Name == name);

        // IDE-PM-148: ignored entries whose file is gone or that are project members are dropped by the detection.
        Assert.Empty(Pm.IgnoredFiles);
        Assert.Equal(Many(2), Pm.UnlistedFilesBar!.Text);

        // Show: the OS file manager with the first unlisted file selected; the bar is gone until a new file is found.
        await Action("Show").ExecuteAsync();
        Assert.Equal(["RevealFile " + Dir + @"\Scratch.pas"], _shell.Calls);
        Assert.Null(Pm.UnlistedFilesBar);
        await DetectAsync();
        Assert.Null(Pm.UnlistedFilesBar);
        Assert.Equal(2, Pm.UnlistedFiles.Count);
        UnitFile("Another.pas");
        await DetectAsync();
        Assert.Equal(Many(3), Pm.UnlistedFilesBar!.Text);

        // Ignore: every currently unlisted file, relative with '/', ordinal order; stored in the session.
        await Action("Ignore").ExecuteAsync();
        Assert.Null(Pm.UnlistedFilesBar);
        Assert.Equal(["Another.pas", "Scratch.pas", "lib/Old.pas"], Pm.IgnoredFiles);
        _services.Session.Save();
        var session = Encoding.UTF8.GetString(_fs.ReadAllBytes(SessionPath)).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("\"ignoredFiles\": [\n      \"Another.pas\",\n      \"Scratch.pas\",\n      \"lib/Old.pas\"\n    ]", session, StringComparison.Ordinal);
        await DetectAsync();
        Assert.Null(Pm.UnlistedFilesBar);
        Assert.Empty(Pm.UnlistedFiles);

        // Restored with the project; an ignored file that no longer exists leaves the list at the next detection.
        Assert.True(await _services.Projects.CloseAsync());
        Assert.Empty(Pm.IgnoredFiles);
        _fs.DeleteFile(Dir + @"\Another.pas");
        await OpenAsync();
        Assert.Equal(["Scratch.pas", "lib/Old.pas"], Pm.IgnoredFiles);
        Assert.Null(Pm.UnlistedFilesBar);

        // Add to Project: as project.addExistingFile — the companion form is detected, a duplicate unit name is
        // reported once (for the first), the other files are still added.
        UnitFile(@"lib\Unit1.pas"); // duplicates the project's Unit1
        UnitFile(@"lib\HELLO.pas"); // duplicates the main source's unit name
        File(Dir + @"\Login.pas", "unit Login;\n{$FORM Login.form}\ninterface\nimplementation\nend.\n");
        File(Dir + @"\Login.form", "object Login: TForm\nend\n");
        UnitFile(@"lib\Zed.pas");
        await DetectAsync();
        Assert.Equal(Many(4), Pm.UnlistedFilesBar!.Text);
        Dialogs.Answer("Msg.Project.DuplicateUnitName", "OK");

        await Action("AddToProject").ExecuteAsync();
        await Pm.MissingFileChecks;

        var duplicate = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Project.DuplicateUnitName", duplicate.MessageId);
        Assert.Equal(["HELLO"], duplicate.Args);
        var files = _services.Projects.Project!.Files;
        Assert.Equal(["Hello.pas", "Unit1.pas", "lib/Unit2.pas", "Login.pas", "lib/Zed.pas"], files.Select(f => f.Path));
        Assert.Equal("Login.form", files[3].Form);
        Assert.Contains("\"path\": \"lib/Zed.pas\"", Encoding.UTF8.GetString(_fs.ReadAllBytes(ProjectPath)), StringComparison.Ordinal);
        Assert.Equal([Dir + @"\lib\HELLO.pas", Dir + @"\lib\Unit1.pas"], Pm.UnlistedFiles); // refused, still unlisted
        Assert.Null(Pm.UnlistedFilesBar); // answered: no bar until a new file is found
        Assert.Equal(["Scratch.pas", "lib/Old.pas"], Pm.IgnoredFiles);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-149")]
    public Task ExternalRenameShowsBothBars() => TestUiThread.RunAsync(() => ExternalRenameShowsBothBarsOnUiThread());

    private async Task ExternalRenameShowsBothBarsOnUiThread()
    {
        WriteHello();
        await OpenAsync();
        Assert.Null(Pm.MissingFilesBar);
        Assert.Null(Pm.UnlistedFilesBar);

        // Renamed outside the IDE: the old entry is missing, the new file unlisted; no pairing is attempted.
        _fs.MoveFile(Dir + @"\Unit1.pas", Dir + @"\Customers.pas", overwrite: false);
        await DetectAsync();

        Assert.Equal(Strings.Msg_Project_MissingFilesOne, Pm.MissingFilesBar!.Text);
        Assert.Equal(Strings.Msg_Project_UnlistedFilesOne, Pm.UnlistedFilesBar!.Text);
        Assert.True(Pm.Find("Unit1.pas")!.IsMissing);
        Assert.Equal(["Hello.pas", "Unit1.pas", "lib/Unit2.pas"], _services.Projects.Project!.Files.Select(f => f.Path));

        // The two existing actions repair it.
        await Pm.UnlistedFilesBar.Actions.Single(a => a.Name == "AddToProject").ExecuteAsync();
        await Pm.MissingFileChecks;
        Dialogs.Answer("Msg.Project.RemoveFileConfirm", "Remove");
        await Pm.MissingFilesBar!.Actions.Single(a => a.Name == "RemoveFromProject").ExecuteAsync();
        await Pm.MissingFileChecks;

        Assert.Equal(["Hello.pas", "lib/Unit2.pas", "Customers.pas"], _services.Projects.Project!.Files.Select(f => f.Path));
        Assert.Null(Pm.MissingFilesBar);
        Assert.Null(Pm.UnlistedFilesBar);
    }
}
