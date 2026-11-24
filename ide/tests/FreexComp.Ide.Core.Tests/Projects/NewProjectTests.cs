using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>Project templates and project creation (project-manager.md §3, IDE-PM-020…024; dialogs.md §3).</summary>
[Trait("Phase", "1")]
public sealed class NewProjectTests
{
    private const string Location = @"C:\work\Projects";

    /// <summary>project-format.md §1 without comments, name Hello, files = [Hello.pas], searchPaths = [] (IDE-PM-024).</summary>
    private const string HelloPproj = """
        {
          "formatVersion": 1,
          "name": "Hello",
          "language": "pascal",
          "appType": "console",
          "mainSource": "Hello.pas",
          "files": [
            {
              "path": "Hello.pas"
            }
          ],
          "searchPaths": [],
          "unitScopeNames": [],
          "rtti": "broad",
          "manifest": "default",
          "defines": [],
          "platforms": [
            "win64"
          ],
          "configurations": {
            "Debug": {
              "optimization": 0,
              "debugInfo": true,
              "defines": [
                "DEBUG"
              ],
              "outputDir": "bin/Debug"
            },
            "Release": {
              "optimization": 2,
              "debugInfo": false,
              "defines": [
                "RELEASE"
              ],
              "outputDir": "bin/Release"
            }
          },
          "activeConfiguration": "Debug",
          "run": {
            "arguments": "",
            "workingDir": ""
          },
          "events": {
            "preBuild": [],
            "postBuild": []
          },
          "dependencies": []
        }

        """;

    private readonly InMemoryFileSystem _memory = new();
    private readonly RecordingFileSystem _fs;
    private readonly RecordingLog _log = new();
    private readonly IdeServices _services;

    public NewProjectTests()
    {
        _fs = new RecordingFileSystem(_memory);
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Log = _log;
        });
    }

    private IProjectService Projects => _services.Projects;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private ProjectTemplate Console => Projects.Templates.Single(t => t.Id == "console-pascal");

    private static string Program(string name, string newLine) =>
        string.Join(newLine, $"program {name};", "", "uses", "  SysUtils;", "", "begin", "  WriteLn('Hello, world');", "end.", "");

    private string Text(string path) => Encoding.UTF8.GetString(_memory.ReadAllBytes(path));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-020")]
    [Trait("Req", "IDE-PM-023")]
    [Trait("Req", "IDE-PM-024")]
    public Task CreatesConsoleTemplateFiles() => TestUiThread.RunAsync(() => CreatesConsoleTemplateFilesOnUiThread()); // I-209

    private async Task CreatesConsoleTemplateFilesOnUiThread()
    {
        // The embedded template (IDE-PM-020/024): listed first, with its template.json metadata.
        var template = Projects.Templates[0];
        Assert.Equal("console-pascal", template.Id);
        Assert.Equal("Console Application", template.Name);
        Assert.Equal("A program that reads and writes text in a console window.", template.Description);
        Assert.Equal(("pascal", "console"), (template.Language, template.AppType));
        Assert.Equal([("Program.pas.txt", "$(ProjectName).pas"), ("Project.pproj.txt", "$(ProjectName).pproj")],
            template.Files.Select(f => (f.Source, f.Target)));

        // Defaults: CRLF + UTF-8 without BOM for sources; the .pproj is canonical (UTF-8, LF).
        var outcome = await Projects.CreateProjectAsync(new NewProjectRequest("console-pascal", "Hello", Location));

        Assert.Equal(NewProjectOutcome.Created, outcome);
        var dir = Location + @"\Hello";
        Assert.Equal([dir + @"\Hello.pas", dir + @"\Hello.pproj"], _memory.EnumerateFiles(dir, "*"));
        Assert.Equal(Program("Hello", "\r\n"), Text(dir + @"\Hello.pas"));
        Assert.Equal(HelloPproj, Text(dir + @"\Hello.pproj"));
        Assert.Empty(Dialogs.Requests);

        // Opened (IDE-PM-011) with the main source active and the caret on the first line after `begin`.
        Assert.Equal("Hello", Projects.Project?.Name);
        Assert.Equal(dir + @"\Hello.pproj", Projects.Project!.FilePath);
        var main = Assert.IsType<TextDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(dir + @"\Hello.pas", main.FilePath);
        Assert.False(main.IsModified);
        Assert.Equal(new TextPosition(7, 3), main.ViewState.Caret);

        // Settings apply to sources only: LF + UTF-8 with BOM; the .pproj stays UTF-8 without BOM + LF.
        _services.Settings.Set(SettingsCatalog.FilesDefaultLineEnding, "lf");
        _services.Settings.Set(SettingsCatalog.FilesDefaultEncoding, "utf-8-bom");
        Assert.Equal(NewProjectOutcome.Created, await Projects.CreateProjectAsync(new NewProjectRequest("console-pascal", "Two", Location)));
        var pas = _memory.ReadAllBytes(Location + @"\Two\Two.pas");
        Assert.Equal([0xEF, 0xBB, 0xBF], pas[..3]);
        Assert.Equal(Program("Two", "\n"), Encoding.UTF8.GetString(pas[3..]));
        Assert.Equal(HelloPproj.Replace("Hello", "Two", StringComparison.Ordinal), Text(Location + @"\Two\Two.pproj"));
        Assert.Equal("Two", Projects.Project?.Name);
        Assert.Equal([Location + @"\Two\Two.pas"], _services.Documents.Documents.Select(d => d.FilePath)); // Hello was closed
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-021")]
    [Trait("Req", "IDE-PM-022")]
    public Task ValidationOrderAndMessages() => TestUiThread.RunAsync(() => ValidationOrderAndMessagesOnUiThread()); // I-209

    private async Task ValidationOrderAndMessagesOnUiThread()
    {
        static (string?, string) Name(string name) =>
            NewProjectValidation.ValidateName(name) is var r ? (r.MessageId, string.Join("|", r.Args)) : default;

        // Name, first failure wins: empty → invalid → too long → reserved word (also `System`, dialogs.md §3).
        Assert.Equal(("Msg.Project.NameEmpty", ""), Name(""));
        Assert.Equal(("Msg.Project.NameInvalid", ""), Name("1Hello"));
        Assert.Equal(("Msg.Project.NameInvalid", ""), Name("Hello World"));
        Assert.Equal(("Msg.Project.NameInvalid", ""), Name("Héllo"));
        Assert.Equal(("Msg.Project.NameInvalid", ""), Name(new string('a', 65) + "-"));
        Assert.Equal(("Msg.Project.NameTooLong", "64"), Name(new string('a', 65)));
        Assert.Equal(("Msg.Project.NameReserved", "Begin"), Name("Begin"));
        Assert.Equal(("Msg.Project.NameReserved", "program"), Name("program"));
        Assert.Equal(("Msg.Project.NameReserved", "SYSTEM"), Name("SYSTEM"));
        Assert.Equal((null, ""), Name(new string('a', 64)));
        Assert.Equal((null, ""), Name("_Hello1"));

        (string?, string) Where(string location, string name = "Hello") =>
            NewProjectValidation.ValidateLocation(location, name, Console, _fs) is var r ? (r.MessageId, string.Join("|", r.Args)) : default;

        // Location: empty → invalid → target not empty → path too long. The folder itself need not exist.
        Assert.Equal(("Msg.Project.LocationEmpty", ""), Where(""));
        Assert.Equal(("Msg.Project.LocationEmpty", ""), Where("   "));
        Assert.Equal(("Msg.Project.LocationInvalid", ""), Where(@"work\Projects"));
        Assert.Equal(("Msg.Project.LocationInvalid", ""), Where(@"\work"));
        Assert.Equal(("Msg.Project.LocationInvalid", ""), Where(@"C:\work\a|b"));
        Assert.Equal(("Msg.Project.LocationInvalid", ""), Where(@"\\server"));
        Assert.Equal((null, ""), Where(Location));
        Assert.Equal((null, ""), Where("C:/work/Projects")); // either separator
        Assert.Equal((null, ""), Where(@"\\server\share\Projects"));

        _memory.CreateDirectory(Location + @"\Empty");
        Assert.Equal((null, ""), Where(Location, "Empty"));
        _memory.CreateDirectory(Location + @"\Hello\sub");
        Assert.Equal(("Msg.Project.LocationNotEmpty", Location + @"\Hello"), Where(Location));
        _memory.CreateDirectory(Location + @"\Other");
        _memory.WriteAllBytes(Location + @"\Other\x.txt", [1]);
        Assert.Equal(("Msg.Project.LocationNotEmpty", Location + @"\Other"), Where(Location, "Other"));

        // The longest file to be created is <Location>\<Name>\<Name>.pproj: 240 characters pass, 241 fail.
        var name = new string('n', 60);
        var fits = @"C:\" + new string('d', 240 - 3 - 1 - 60 - 1 - 66);
        Assert.Equal(240, Path.Combine(fits, name, name + ".pproj").Length);
        Assert.Equal((null, ""), Where(fits, name));
        Assert.Equal(("Msg.Project.PathTooLong", "240"), Where(fits + "d", name));

        // Creation re-checks: a non-empty target is refused and left untouched.
        Dialogs.Answer("Msg.Project.CreateFailed", "OK");
        Assert.Equal(NewProjectOutcome.Failed, await Projects.CreateProjectAsync(new NewProjectRequest("console-pascal", "Other", Location)));
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Project.CreateFailed", request.MessageId);
        Assert.Equal(Location + @"\Other", request.Args[0]);
        Assert.Equal(["x.txt"], _memory.EnumerateFiles(Location + @"\Other", "*").Select(Path.GetFileName));
        Assert.Null(Projects.Project);
    }

    [Theory(Timeout = 30000)]
    [InlineData(".pproj")]
    [InlineData(".pas")]
    [Trait("Req", "IDE-PM-023")]
    public Task FailureRollsBackCreatedFiles(string failingFile) => TestUiThread.RunAsync(() => FailureRollsBackCreatedFilesOnUiThread(failingFile)); // I-209

    private async Task FailureRollsBackCreatedFilesOnUiThread(string failingFile)
    {
        // C:\work exists with a file of its own; New\Deep\Hello is created by the operation.
        _memory.CreateDirectory(@"C:\work");
        _memory.WriteAllBytes(@"C:\work\keep.txt", [1]);
        _fs.FailWhen = call => call.StartsWith("WriteAllBytesDurable", StringComparison.Ordinal) && call.Contains(failingFile + ".~tmp", StringComparison.Ordinal);
        Dialogs.Answer("Msg.Project.CreateFailed", "OK");

        var outcome = await Projects.CreateProjectAsync(new NewProjectRequest("console-pascal", "Hello", @"C:\work\New\Deep"));

        Assert.Equal(NewProjectOutcome.Failed, outcome);
        Assert.False(_memory.DirectoryExists(@"C:\work\New"));
        Assert.Equal([@"C:\work\keep.txt"], _memory.EnumerateFiles(@"C:\work", "*"));
        Assert.Empty(_memory.EnumerateDirectories(@"C:\work"));
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Project.CreateFailed", request.MessageId);
        Assert.Equal(@"C:\work\New\Deep\Hello", request.Args[0]);
        Assert.StartsWith("Injected failure: ", request.Args[1], StringComparison.Ordinal);
        Assert.Null(Projects.Project);
        Assert.Empty(_services.Documents.Documents);

        // An existing location is kept; only the project folder goes.
        Dialogs.Requests.Clear();
        _memory.CreateDirectory(@"C:\work\Existing");
        Assert.Equal(NewProjectOutcome.Failed, await Projects.CreateProjectAsync(new NewProjectRequest("console-pascal", "Hello", @"C:\work\Existing")));
        Assert.True(_memory.DirectoryExists(@"C:\work\Existing"));
        Assert.Empty(_memory.EnumerateDirectories(@"C:\work\Existing"));
        Assert.Empty(_memory.EnumerateFiles(@"C:\work\Existing", "*"));

        // An existing empty project folder is kept too.
        Dialogs.Requests.Clear();
        _memory.CreateDirectory(@"C:\work\Existing\Hello");
        Assert.Equal(NewProjectOutcome.Failed, await Projects.CreateProjectAsync(new NewProjectRequest("console-pascal", "Hello", @"C:\work\Existing")));
        Assert.True(_memory.DirectoryExists(@"C:\work\Existing\Hello"));
        Assert.Empty(_memory.EnumerateFiles(@"C:\work\Existing\Hello", "*"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-020")]
    [Trait("Req", "IDE-PM-023")]
    [Trait("Req", "DSG-CS-110")]
    [Trait("Phase", "5")]
    public Task GuiTemplateCreatesUnitAndForm() => TestUiThread.RunAsync(() => GuiTemplateCreatesUnitAndFormOnUiThread()); // I-209

    private async Task GuiTemplateCreatesUnitAndFormOnUiThread()
    {
        // Listed from Phase 5 on (the designer phase; phase gating: not at Phase 4), after console-pascal.
        Assert.DoesNotContain(TestComposition.Build(o => o.ImplementedPhase = 4).Projects.Templates, t => t.Id == "gui-pascal");
        var services = _services;
        var gui = services.Projects.Templates.Single(t => t.Id == "gui-pascal");
        Assert.Equal("console-pascal", services.Projects.Templates[0].Id);
        Assert.Equal(("pascal", "gui"), (gui.Language, gui.AppType));

        Assert.Equal(NewProjectOutcome.Created, await services.Projects.CreateProjectAsync(new NewProjectRequest("gui-pascal", "Hello", Location)));

        // The program, the first form (unit and form named MainForm, DSG-CS-110) and the project with its form entry.
        var dir = Location + @"\Hello";
        Assert.Equal([dir + @"\Hello.pas", dir + @"\Hello.pproj", dir + @"\MainForm.form", dir + @"\MainForm.pas"],
            _memory.EnumerateFiles(dir, "*").Order(StringComparer.Ordinal));
        Assert.Equal(string.Join("\r\n", "program Hello;", "", "uses", "  Forms, MainForm;", "", "begin", "  Application.Initialize;",
            "  Application.CreateForm(TMainForm, MainForm.MainForm);", "  Application.Run;", "end.", ""), Text(dir + @"\Hello.pas"));
        Assert.Equal(Designer.NewFormTemplateTests.Unit("MainForm", "\r\n"), Text(dir + @"\MainForm.pas"));
        Assert.Equal(Designer.NewFormTemplateTests.Form("MainForm"), Text(dir + @"\MainForm.form"));
        var project = services.Projects.Project!;
        Assert.Equal("gui", project.AppType);
        Assert.Equal([("Hello.pas", null), ("MainForm.pas", "MainForm.form")], project.Files.Select(f => (f.Path, f.Form)));
        const string consoleFiles = "      \"path\": \"Hello.pas\"\n    }\n";
        const string guiFiles = "      \"path\": \"Hello.pas\"\n    },\n    {\n      \"path\": \"MainForm.pas\",\n      \"form\": \"MainForm.form\"\n    }\n";
        Assert.Equal(HelloPproj.Replace("\"console\"", "\"gui\"", StringComparison.Ordinal).Replace(consoleFiles, guiFiles, StringComparison.Ordinal),
            Text(dir + @"\Hello.pproj"));

        // Opened with the program active and the caret on the first line after `begin`.
        var main = Assert.IsType<TextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal(dir + @"\Hello.pas", main.FilePath);
        Assert.Equal(new TextPosition(7, 3), main.ViewState.Caret);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-020")]
    public void InvalidTemplateIsSkippedWithLogError()
    {
        var log = new RecordingLog();
        var files = new Dictionary<string, string>
        {
            ["Templates/zeta/template.json"] = """{ "id": "zeta", "name": "Zeta", "description": "z", "language": "pascal", "appType": "console", "files": [ { "source": "a.txt", "target": "a.pas" } ] }""",
            ["Templates/zeta/a.txt"] = "x",
            ["Templates/broken/template.json"] = "{ not json",
            ["Templates/console-pascal/template.json"] = """{ "id": "console-pascal", "name": "Z last by name", "description": "c", "language": "pascal", "appType": "console", "files": [] }""",
            ["Templates/alpha/template.json"] = """{ "id": "alpha", "name": "Alpha", "description": "a", "language": "pascal", "appType": "console", "files": [] }""",
            ["Templates/nosource/template.json"] = """{ "id": "nosource", "name": "No source", "description": "n", "language": "pascal", "appType": "console", "files": [ { "source": "missing.txt", "target": "m.pas" } ] }""",
        };

        var templates = ProjectTemplates.Load(files.Keys, key => files.GetValueOrDefault(key), log);

        Assert.Equal(["console-pascal", "alpha", "zeta"], templates.Select(t => t.Id));
        Assert.Equal("x", templates[2].ReadSource("a.txt"));
        Assert.Equal(2, log.Lines.Count(l => l.StartsWith("ERROR ", StringComparison.Ordinal)));
        Assert.Contains(log.Lines, l => l.Contains("broken", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("nosource", StringComparison.Ordinal));
    }

    /// <summary>The template source of IDE-TGT-016 (phase8-build-options.md §2.1) for a project of that name.</summary>
    private static string Library(string name, string newLine) => string.Join(newLine,
        $"library {name};",
        "",
        "{ Routines listed under \"exports\" can be called from other programs.",
        "  Pass only simple types, records of simple types and pointers across the",
        "  library boundary: the caller and the library do not share a memory manager. }",
        "",
        "function Add(A, B: Integer): Integer; stdcall;",
        "begin",
        "  Result := A + B;",
        "end;",
        "",
        "exports",
        "  Add;",
        "",
        "begin",
        "end.",
        "");

    /// <summary>
    /// IDE-TGT-015, 016: the template <c>library-pascal</c> ("Dynamic-link library", <c>pascal</c>, <c>library</c>) is
    /// listed after <c>gui-pascal</c> and creates <c>&lt;name&gt;.pproj</c> and <c>&lt;name&gt;.pas</c> with exactly the
    /// specified source; the project is a library without a host application.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-TGT-015")]
    [Trait("Req", "IDE-TGT-016")]
    public Task LibraryTemplateCreatesFiles() => TestUiThread.RunAsync(async () =>
    {
        Assert.Equal(["console-pascal", "gui-pascal", "library-pascal"], Projects.Templates.Select(t => t.Id));
        var template = Projects.Templates[2];
        Assert.Equal(("Dynamic-link library", "pascal", "library"), (template.Name, template.Language, template.AppType));
        Assert.Equal(["$(ProjectName).pas", "$(ProjectName).pproj"], template.Files.Select(f => f.Target));
        // A composition of an earlier phase does not list it.
        Assert.DoesNotContain(TestComposition.Build(o => o.ImplementedPhase = 6).Projects.Templates, t => t.Id == "library-pascal");

        Assert.Equal(NewProjectOutcome.Created, await Projects.CreateProjectAsync(new NewProjectRequest("library-pascal", "Mathlib", Location)));

        var dir = Location + @"\Mathlib";
        Assert.Equal([dir + @"\Mathlib.pas", dir + @"\Mathlib.pproj"], _memory.EnumerateFiles(dir, "*").Order(StringComparer.Ordinal));
        Assert.Equal(Library("Mathlib", "\r\n"), Text(dir + @"\Mathlib.pas"));
        Assert.DoesNotContain("\t", Text(dir + @"\Mathlib.pas"), StringComparison.Ordinal);
        Assert.Equal(HelloPproj.Replace("\"console\"", "\"library\"", StringComparison.Ordinal).Replace("Hello", "Mathlib", StringComparison.Ordinal),
            Text(dir + @"\Mathlib.pproj"));
        var project = Projects.Project!;
        Assert.Equal(("Mathlib", "library", "Mathlib.pas"), (project.Name, project.AppType, project.MainSource));
        Assert.Equal(["Debug", "Release"], Projects.Definition!.ConfigurationNames);
        Assert.Equal("", Projects.Definition!.Run.HostApplication);
        Assert.DoesNotContain("hostApplication", Text(dir + @"\Mathlib.pproj"), StringComparison.Ordinal);
        Assert.Equal(dir + @"\Mathlib.pas", _services.Documents.ActiveDocument?.FilePath);
        Assert.Empty(Dialogs.Requests);

        // LF when that is the default line ending of new files.
        _services.Settings.Set(FreexComp.Ide.Core.Settings.SettingsCatalog.FilesDefaultLineEnding, "lf");
        Assert.Equal(NewProjectOutcome.Created, await Projects.CreateProjectAsync(new NewProjectRequest("library-pascal", "Other", Location)));
        Assert.Equal(Library("Other", "\n"), Text(Location + @"\Other\Other.pas"));
    });
}
