using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>The OS picker prompts of dialogs.md §5 (UI-DLG-050…054).</summary>
[Trait("Phase", "1")]
public sealed class FileDialogTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly IdeServices _services;

    public FileDialogTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
    }

    private FakeFileDialogService Pickers => (FakeFileDialogService)_services.FileDialogs;

    private FilePrompts Prompts => _services.FilePrompts;

    private string File(string path, string text = "")
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    private static string[] Labels(FilePickerRequest request) => [.. request.Filters.Select(f => f.Label)];

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-050")]
    [Trait("Req", "UI-DLG-051")]
    public Task FiltersAndInitialFolderRules() => TestUiThread.RunAsync(() => FiltersAndInitialFolderRulesOnUiThread()); // I-209

    private async Task FiltersAndInitialFolderRulesOnUiThread()
    {
        var documents = _services.UserPaths.DocumentsDirectory;
        var projects = Path.Combine(documents, "FreexComp Projects");

        // openFile: title, multi-select and the filters in order (first = default).
        Pickers.NextCancel();
        Assert.Empty(await Prompts.OpenFileAsync(activeDocumentPath: null));
        var open = Pickers.Requests[^1];
        Assert.Equal(("openFile", "Open File", true), (open.PromptId, open.Title, open.AllowMultiple));
        Assert.Equal(["Source files (*.pas;*.inc;*.form)", "Project files (*.pproj)", "Text files (*.txt;*.md;*.json)", "All files (*.*)"],
            Labels(open));
        Assert.Equal(["*.pas", "*.inc", "*.form"], open.Filters[0].Patterns);
        Assert.Null(open.InitialFolder);                                // no candidate exists

        // (5) the Documents folder, (4) the default projects folder (empty setting = Documents\FreexComp Projects).
        _fs.CreateDirectory(documents);
        Pickers.NextCancel();
        await Prompts.OpenFileAsync(null);
        Assert.Equal(documents, Pickers.Requests[^1].InitialFolder);
        _fs.CreateDirectory(projects);
        Pickers.NextCancel();
        await Prompts.OpenFileAsync(null);
        Assert.Equal(projects, Pickers.Requests[^1].InitialFolder);
        _services.Settings.Set(SettingsCatalog.EnvironmentDefaultProjectsDir, @"C:\Work");
        Pickers.NextCancel();
        await Prompts.OpenFileAsync(null);
        Assert.Equal(documents, Pickers.Requests[^1].InitialFolder);    // the configured C:\Work does not exist
        _fs.CreateDirectory(@"C:\Work");
        Pickers.NextCancel();
        await Prompts.OpenFileAsync(null);
        Assert.Equal(@"C:\Work", Pickers.Requests[^1].InitialFolder);

        // (2) the last folder of the same prompt, stored after a successful pick (not after a cancel).
        File(@"C:\p\A.pas");
        File(@"C:\p\B.pas");
        Pickers.NextOpen(@"C:\p\A.pas", @"C:\p\B.pas");
        Assert.Equal([@"C:\p\A.pas", @"C:\p\B.pas"], await Prompts.OpenFileAsync(null));
        Pickers.NextCancel();
        await Prompts.OpenFileAsync(null);
        Assert.Equal(@"C:\p", Pickers.Requests[^1].InitialFolder);

        // (1) the stated folder: the active document's folder when it is saved.
        File(@"C:\q\C.pas");
        Pickers.NextCancel();
        await Prompts.OpenFileAsync(@"C:\q\C.pas");
        Assert.Equal(@"C:\q", Pickers.Requests[^1].InitialFolder);
        Pickers.NextCancel();
        await Prompts.OpenFileAsync(@"C:\gone\D.pas");                  // its folder no longer exists
        Assert.Equal(@"C:\p", Pickers.Requests[^1].InitialFolder);

        // The last folder is per prompt and kept in uistate.json (dialogs.lastFolder.<promptId>).
        Pickers.NextCancel();
        Assert.Null(await Prompts.OpenProjectAsync());
        var project = Pickers.Requests[^1];
        Assert.Equal(("openProject", "Open Project", false), (project.PromptId, project.Title, project.AllowMultiple));
        Assert.Equal(["FreexComp projects and groups (*.pproj;*.pgroup)", "All files (*.*)"], Labels(project)); // IDE-PM-154
        Assert.Equal(@"C:\Work", project.InitialFolder);
        _services.Layout.Save();
        var uistate = JsonNode.Parse(_fs.ReadAllBytes(Path.Combine(_services.UserPaths.RoamingDirectory, "uistate.json")))!;
        Assert.Equal(@"C:\p", (string?)uistate["dialogs"]!["lastFolder"]!["openFile"]);

        // saveAs: filters by document type, default extension = first pattern, suggested name = title, the
        // document's folder.
        var doc = Assert.IsType<TextDocumentViewModel>((await _services.Documents.OpenAsync(@"C:\q\C.pas")).Value);
        Pickers.NextSave(@"C:\r\Renamed.pas");
        _fs.CreateDirectory(@"C:\r");
        Assert.Equal(@"C:\r\Renamed.pas", await Prompts.SaveAsAsync(doc));
        var save = Pickers.Requests[^1];
        Assert.Equal(("saveAs", "Save As", "C.pas", "pas", @"C:\q"), (save.PromptId, save.Title, save.SuggestedName, save.DefaultExtension, save.InitialFolder));
        Assert.Equal(["Pascal unit (*.pas)", "Include file (*.inc)", "All files (*.*)"], Labels(save));

        var json = Assert.IsType<TextDocumentViewModel>((await _services.Documents.OpenAsync(File(@"C:\q\data.json", "{}"))).Value);
        Pickers.NextCancel();
        Assert.Null(await Prompts.SaveAsAsync(json));
        Assert.Equal(["All files (*.*)"], Labels(Pickers.Requests[^1]));
        Assert.Null(Pickers.Requests[^1].DefaultExtension);

        // A .form without unit opens as a form document (DSG-CS-080).
        var form = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(File(@"C:\q\Main.form"))).Value);
        Pickers.NextCancel();
        await Prompts.SaveAsAsync(form);
        Assert.Equal(["Form file (*.form)"], Labels(Pickers.Requests[^1]));
        Assert.Equal("form", Pickers.Requests[^1].DefaultExtension);

        // An untitled document has no folder of its own (no project open): the last Save As folder.
        var unit = _services.Documents.NewUntitledUnit();
        Pickers.NextCancel();
        await Prompts.SaveAsAsync(unit);
        Assert.Equal(("Unit1.pas", @"C:\r"), (Pickers.Requests[^1].SuggestedName, Pickers.Requests[^1].InitialFolder));

        // addExisting and folder prompts.
        Pickers.NextCancel();
        Assert.Empty(await Prompts.AddExistingAsync());
        Assert.Equal(("addExisting", "Add to Project", true), (Pickers.Requests[^1].PromptId, Pickers.Requests[^1].Title, Pickers.Requests[^1].AllowMultiple));
        Assert.Equal(["Pascal units (*.pas)", "All files (*.*)"], Labels(Pickers.Requests[^1]));
        Pickers.NextFolder(@"C:\p");
        Assert.Equal(@"C:\p", await Prompts.PickFolderAsync("NewProject.Location", currentValue: @"C:\q"));
        var folder = Pickers.Requests[^1];
        Assert.Equal(("folder.NewProject.Location", "Select Folder", @"C:\q"), (folder.PromptId, folder.Title, folder.InitialFolder));
        Assert.Empty(folder.Filters);
        Pickers.NextCancel();
        await Prompts.PickFolderAsync("NewProject.Location", currentValue: "");
        Assert.Equal(@"C:\p", Pickers.Requests[^1].InitialFolder);     // its own last folder

        // An unscripted picker request fails the test (IDE-TEST-015).
        await Assert.ThrowsAsync<InvalidOperationException>(() => Prompts.OpenFileAsync(null));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-030")]
    [Trait("Req", "UI-DLG-054")]
    public Task FileOpenOpensEachPickedFile() => TestUiThread.RunAsync(() => FileOpenOpensEachPickedFileOnUiThread()); // I-209

    private async Task FileOpenOpensEachPickedFileOnUiThread()
    {
        var dialogs = (FakeDialogService)_services.Dialogs;
        var a = File(@"C:\p\A.pas", "a");
        var b = File(@"C:\p\B.pas", "b");
        Assert.True(_services.Commands.CanExecute("file.open"));

        // Each picked file opens; the last one becomes active.
        Pickers.NextOpen(a, b);
        await _services.Commands.ExecuteAsync("file.open");
        Assert.Equal([a, b], _services.Documents.Documents.Select(d => d.FilePath));
        Assert.Equal(b, _services.Documents.ActiveDocument!.FilePath);

        // A file already open is activated, not reloaded; a failing one is reported and the rest still open.
        var docA = _services.Documents.Documents[0];
        var c = File(@"C:\p\C.pas", "c");
        dialogs.Answer("Msg.File.NotFound", "OK");
        Pickers.NextOpen(@"C:\p\Gone.pas", a, c);
        await _services.Commands.ExecuteAsync("file.open");
        Assert.Equal("Msg.File.NotFound", Assert.Single(dialogs.Requests).MessageId);
        Assert.Equal([a, c, b], _services.Documents.Documents.Select(d => d.FilePath)); // C opens right of the re-activated A
        Assert.Same(docA, _services.Documents.Documents[0]);
        Assert.Equal(c, _services.Documents.ActiveDocument!.FilePath);

        // Canceled: nothing happens.
        Pickers.NextCancel();
        await _services.Commands.ExecuteAsync("file.open");
        Assert.Equal(3, _services.Documents.Documents.Count);
    }
}
