using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary><c>ISessionService</c>: <c>session.json</c> and <c>session-noproject.json</c> (architecture.md §8.2).</summary>
[Trait("Phase", "1")]
public sealed class SessionTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";
    private const string StateDir = Dir + @"\.freexcomp";
    private const string SessionPath = StateDir + @"\session.json";

    private const string Unit1Text = "unit Unit1;\ninterface\nimplementation\nprocedure A;\nbegin\nend;\nend.\n";

    private readonly InMemoryFileSystem _fs = new();
    private readonly RecordingFileSystem _recording;
    private readonly RecordingLog _log = new();
    private readonly ManualClock _clock = new();
    private readonly IdeServices _services;

    public SessionTests()
    {
        _recording = new RecordingFileSystem(_fs);
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _recording;
            o.Log = _log;
            o.Clock = _clock;
        });
    }

    private IProjectService Projects => _services.Projects;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private ProjectManagerViewModel ProjectManager => (ProjectManagerViewModel)_services.ToolWindows.Get("ProjectManager")!;

    private string File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    private string Read(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    /// <summary>Hello.pproj: main source Hello.pas, members Unit1.pas (one foldable routine at line 4) and sub/Unit2.pas.</summary>
    private void WriteHello()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "sub/Unit2.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        File(Dir + @"\Unit1.pas", Unit1Text);
        File(Dir + @"\sub\Unit2.pas", "unit Unit2;\ninterface\nimplementation\nend.\n");
    }

    private async Task<TextDocumentViewModel> OpenDocumentAsync(string path)
    {
        var result = await _services.Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    private TextDocumentViewModel Document(string path) =>
        Assert.IsType<TextDocumentViewModel>(_services.Documents.Documents.Single(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-070")]
    [Trait("Req", "IDE-ARCH-071")]
    [Trait("Req", "IDE-ARCH-076")]
    [Trait("Req", "UI-TW-012")]
    public async Task RoundTripDocumentsCaretsFolds()
    {
        WriteHello();
        // Members of later phases are preserved (IDE-ARCH-076); an empty document list opens the main source.
        File(SessionPath, """{ "formatVersion": 1, "documents": [], "bookmarks": [ { "path": "Hello.pas", "line": 2, "number": 1 } ], "custom": 5 }""");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.Equal(Dir + @"\Hello.pas", Assert.Single(_services.Documents.Documents).FilePath);

        var unit1 = await OpenDocumentAsync(Dir + @"\Unit1.pas");
        var unit2 = await OpenDocumentAsync(Dir + @"\sub\Unit2.pas");
        unit1.ViewState.FoldedLines = [4];
        Assert.True(unit1.Folding.Regions.Single(r => r.StartLine == 4).IsFolded);
        unit1.ViewState.Caret = new TextPosition(2, 4);
        unit1.ViewState.SelectionAnchor = new TextPosition(1, 3);
        unit1.ViewState.HorizontalOffset = 12;
        unit1.ViewState.TopLine = 3;
        unit2.ViewState.Caret = new TextPosition(3, 2);
        _services.Documents.Activate(unit1);
        ProjectManager.Find("sub/")!.IsExpanded = false;
        ProjectManager.Select(ProjectManager.Find("Unit1.pas")!);

        Assert.True(await Projects.CloseAsync());

        var session = JsonNode.Parse(Read(SessionPath))!.AsObject();
        Assert.Equal(1, (int)session["formatVersion"]!);
        Assert.Equal("Hello.pproj", (string)session["project"]!);
        Assert.Equal("Edit", (string)session["activeProfile"]!);
        Assert.Equal("Unit1.pas", (string)session["activeDocument"]!);
        var documents = session["documents"]!.AsArray();
        Assert.Equal(["Hello.pas", "Unit1.pas", "sub/Unit2.pas"], documents.Select(d => (string)d!["path"]!));
        var first = documents[1]!;
        Assert.Equal("code", (string)first["view"]!);
        Assert.Equal(2, (int)first["caret"]!["line"]!);
        Assert.Equal(4, (int)first["caret"]!["column"]!);
        Assert.Equal(3, (int)first["selection"]!["anchor"]!["column"]!);
        Assert.Equal(4, (int)first["selection"]!["active"]!["column"]!);
        Assert.Equal(3, (int)first["scroll"]!["line"]!);
        Assert.Equal(12, (double)first["scroll"]!["x"]!);
        Assert.Equal([4], first["folds"]!.AsArray().Select(f => (int)f!));
        Assert.Null(documents[2]!["selection"]);
        Assert.Equal(2, (int)session["bookmarks"]![0]!["line"]!);
        Assert.Equal(5, (int)session["custom"]!);
        Assert.DoesNotContain("sub/", session["projectManager"]!["expanded"]!.AsArray().Select(k => (string)k!));
        Assert.Contains(ProjectManagerViewModel.RootKey, session["projectManager"]!["expanded"]!.AsArray().Select(k => (string)k!));
        Assert.Equal("Unit1.pas", (string)session["projectManager"]!["selected"]!);
        Assert.DoesNotContain('\r', Read(SessionPath));
        Assert.Empty(_services.Documents.Documents);

        // Reopening restores tabs, carets, selection, scroll line, folds, the active document and the tree.
        Assert.True(await Projects.OpenAsync(ProjectPath));

        Assert.Equal([Dir + @"\Hello.pas", Dir + @"\Unit1.pas", Dir + @"\sub\Unit2.pas"], _services.Documents.Documents.Select(d => d.FilePath));
        unit1 = Document(Dir + @"\Unit1.pas");
        Assert.Same(unit1, _services.Documents.ActiveDocument);
        Assert.Equal(new TextPosition(2, 4), unit1.ViewState.Caret);
        Assert.Equal(new TextPosition(1, 3), unit1.ViewState.SelectionAnchor);
        Assert.Equal(3, unit1.ViewState.TopLine);
        Assert.Equal(12, unit1.ViewState.HorizontalOffset);
        Assert.Equal([4], unit1.ViewState.FoldedLines);
        Assert.True(unit1.Folding.Regions.Single(r => r.StartLine == 4).IsFolded);
        Assert.Equal(new TextPosition(3, 2), Document(Dir + @"\sub\Unit2.pas").ViewState.Caret);
        Assert.False(ProjectManager.Find("sub/")!.IsExpanded);
        Assert.True(ProjectManager.Find(ProjectManagerViewModel.RootKey)!.IsExpanded);
        Assert.Equal(["Unit1.pas"], ProjectManager.Selection.Select(n => n.Key));
        Assert.Empty(Dialogs.Requests);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-071")]
    public async Task ChangesAreWrittenFiveSecondsAfterTheLastOne()
    {
        WriteHello();
        Assert.True(await Projects.OpenAsync(ProjectPath));
        _clock.Advance(TimeSpan.FromSeconds(10));
        _fs.DeleteFile(SessionPath);

        var main = Document(Dir + @"\Hello.pas");
        main.ViewState.Caret = new TextPosition(2, 1);
        _clock.Advance(TimeSpan.FromSeconds(4));
        main.ViewState.Caret = new TextPosition(3, 1);
        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(_fs.FileExists(SessionPath));
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(3, (int)JsonNode.Parse(Read(SessionPath))!["documents"]![0]!["caret"]!["line"]!);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-072")]
    public async Task GarbageSessionIsIgnored()
    {
        WriteHello();
        File(SessionPath, "{ \"formatVersion\": 1, \"documents\": [ { \"path\": ");

        Assert.True(await Projects.OpenAsync(ProjectPath));

        Assert.Equal(Dir + @"\Hello.pas", Assert.Single(_services.Documents.Documents).FilePath); // the main source instead
        Assert.Contains(_log.Lines, l => l.Contains("WARN", StringComparison.Ordinal) && l.Contains("session.json", StringComparison.Ordinal));
        Assert.Empty(Dialogs.Requests);

        // A session whose shape is wrong is garbage too.
        Assert.True(await Projects.CloseAsync());
        File(SessionPath, """{ "formatVersion": 1, "documents": { "path": "Unit1.pas" }, "activeDocument": 7 }""");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.Equal(Dir + @"\Hello.pas", Assert.Single(_services.Documents.Documents).FilePath);

        // A write failure is logged once per project open, never shown.
        _recording.FailWhen = call => call.Contains(@"\.freexcomp\", StringComparison.OrdinalIgnoreCase) && call.StartsWith("Write", StringComparison.Ordinal);
        var warnings = _log.Lines.Count(l => l.Contains("WARN", StringComparison.Ordinal));
        Document(Dir + @"\Hello.pas").ViewState.Caret = new TextPosition(2, 1);
        _clock.Advance(TimeSpan.FromSeconds(5));
        Document(Dir + @"\Hello.pas").ViewState.Caret = new TextPosition(3, 1);
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await Projects.CloseAsync());
        Assert.Equal(warnings + 1, _log.Lines.Count(l => l.Contains("WARN", StringComparison.Ordinal)));
        Assert.Empty(Dialogs.Requests);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-074")]
    public async Task CreatesGitIgnoreInStateFolder()
    {
        WriteHello();
        Assert.False(_fs.DirectoryExists(StateDir));
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.True(await Projects.CloseAsync());

        Assert.Equal("*\n", Read(StateDir + @"\.gitignore"));
        Assert.True(_fs.FileExists(SessionPath));

        // Only when the folder is created: a changed or deleted .gitignore is left alone afterwards.
        File(StateDir + @"\.gitignore", "custom\n");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.True(await Projects.CloseAsync());
        Assert.Equal("custom\n", Read(StateDir + @"\.gitignore"));
        _fs.DeleteFile(StateDir + @"\.gitignore");
        Assert.True(await Projects.OpenAsync(ProjectPath));
        Assert.True(await Projects.CloseAsync());
        Assert.False(_fs.FileExists(StateDir + @"\.gitignore"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-073")]
    public async Task MissingFilesAndOutOfRangeCaretsAreTolerated()
    {
        WriteHello();
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "sub/Unit2.pas" }, { "path": "C:/other/Notes.pas" } ] }
            """);
        var outside = File(@"C:\other\Notes.pas", "unit Notes;\nend.\n"); // a member outside the project folder
        File(SessionPath, $$"""
            { "formatVersion": 1, "project": "Hello.pproj",
              "documents": [
                { "path": "Gone.pas", "caret": { "line": 1, "column": 1 } },
                { "path": "Unit1.pas", "caret": { "line": 99, "column": 50 },
                  "selection": { "anchor": { "line": 2, "column": 80 }, "active": { "line": 99, "column": 50 } },
                  "scroll": { "line": 500, "x": 0 }, "folds": [ 2, 4 ] },
                { "path": "{{outside.Replace(@"\", @"\\", StringComparison.Ordinal)}}", "caret": { "line": 0, "column": -3 }, "folds": "x" },
                { "path": "sub/Gone2.pas" }
              ],
              "activeDocument": "Gone.pas",
              "projectManager": { "expanded": [ "$project", "nope/" ], "selected": "Gone.pas" } }
            """);

        Assert.True(await Projects.OpenAsync(ProjectPath));

        Assert.Equal([Dir + @"\Unit1.pas", outside], _services.Documents.Documents.Select(d => d.FilePath));
        var unit1 = Document(Dir + @"\Unit1.pas");
        Assert.Equal(new TextPosition(8, 1), unit1.ViewState.Caret); // clamped to the end of the file
        Assert.Equal(new TextPosition(2, 10), unit1.ViewState.SelectionAnchor);
        Assert.Equal([4], unit1.ViewState.FoldedLines); // line 2 starts no region
        Assert.Equal(new TextPosition(1, 1), Document(outside).ViewState.Caret);
        Assert.Equal(outside, _services.Documents.ActiveDocument!.FilePath); // the last restored one
        Assert.Empty(ProjectManager.Selection);
        Assert.Empty(Dialogs.Requests);
        Assert.DoesNotContain(_log.Lines, l => l.Contains("ERROR", StringComparison.Ordinal));

        // The rewritten session stores the outside member with its absolute path; only members are stored.
        await OpenDocumentAsync(File(@"C:\loose\Loose.pas", "unit Loose;\n"));
        Assert.True(await Projects.CloseAsync());
        var paths = JsonNode.Parse(Read(SessionPath))!["documents"]!.AsArray().Select(d => (string)d!["path"]!);
        Assert.Equal(["Unit1.pas", outside], paths);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-075")]
    public async Task LooseFilesUseTheNoProjectSession()
    {
        var notes = File(@"C:\other\Notes.pas", "unit Notes;\ninterface\nimplementation\nend.\n");
        var readme = File(@"C:\other\Readme.txt", "r\n");
        var document = await OpenDocumentAsync(notes);
        await OpenDocumentAsync(readme);
        _services.Documents.NewUntitledUnit(); // untitled documents are not part of a session
        document.ViewState.Caret = new TextPosition(3, 2);
        _services.Documents.Activate(document);

        _services.Session.Save();

        var path = Path.Combine(_services.UserPaths.LocalDirectory, "session-noproject.json");
        var session = JsonNode.Parse(Read(path))!.AsObject();
        Assert.Null(session["project"]);
        Assert.True(session.ContainsKey("project"));
        Assert.Equal([notes, readme], session["documents"]!.AsArray().Select(d => (string)d!["path"]!));
        Assert.Equal(notes, (string)session["activeDocument"]!);

        foreach (var open in _services.Documents.Documents.ToList())
            _services.Documents.Close(open);
        Assert.Equal(2, await _services.Session.RestoreAsync());
        Assert.Equal([notes, readme], _services.Documents.Documents.Select(d => d.FilePath));
        Assert.Equal(notes, _services.Documents.ActiveDocument!.FilePath);
        Assert.Equal(new TextPosition(3, 2), Document(notes).ViewState.Caret);
    }
}
