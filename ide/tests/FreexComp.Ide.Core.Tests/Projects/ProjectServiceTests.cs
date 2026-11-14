using System.Text;
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
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    private static void Type(TextDocumentViewModel document, string text) =>
        document.Buffer.ApplyEdits([new TextEdit(0, 0, text)]);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-011")]
    [Trait("Req", "IDE-PM-012")]
    public async Task OpenValidatesAndPopulatesModel()
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

    [Theory(Timeout = 30000)]
    [InlineData("""{ "formatVersion": 1, "name": "1Bad", "mainSource": "Hello.pas" }""", "Msg.Project.OpenFailed", "NameInvalid")]
    [InlineData("""{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "configurations": {} }""", "Msg.Project.OpenFailed", "NoConfigurations")]
    [InlineData("""{ "formatVersion": 3, "name": "Hello", "mainSource": "Hello.pas" }""", "Msg.Project.NewerFormat", null)]
    [InlineData("{ \"formatVersion\": 1,\n  \"name\": 5, \"mainSource\": \"Hello.pas\" }", "Msg.Project.OpenFailed", "Position")]
    [Trait("Req", "IDE-PM-005")]
    [Trait("Req", "IDE-PM-011")]
    [Trait("Req", "IDE-PM-012")]
    public async Task OpenRejectsInvalidProjects(string text, string messageId, string? reason)
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
    public async Task OpenFailedOffersOpenAsText()
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
    public async Task CloseKeepsNonMemberDocuments()
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
    public async Task OpeningAnotherProjectClosesTheCurrentOneFirst()
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
    public async Task ViewSourceOpensMainSource()
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
    public async Task FileOpenWithProjectFileOpensTheProject()
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
    public async Task ProjectDirectoryIsTheThirdInitialFolder()
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
    public async Task WindowTitleSubjectIsTheProject()
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
}
