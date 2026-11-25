using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>
/// Three projects below <c>C:\g</c> (App, Lib, Tools; each with its program file and one unit, Lib also with a form
/// unit) and the group file <c>C:\g\All.pgroup</c> for the project-group tests (project-groups.md §7).
/// </summary>
internal sealed class ProjectGroupFixture
{
    public const string Root = @"C:\g";
    public const string GroupPath = Root + @"\All.pgroup";
    public static readonly FocusState ProjectManagerFocus = new(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);
    public static readonly FocusState EditorFocus = new(CommandScope.Editor, null, null, false);

    public ProjectGroupFixture()
    {
        Recording = new RecordingFileSystem(Files);
        Services = TestComposition.Build(o =>
        {
            o.FileSystem = Recording;
            o.Log = Log;
        });
        AddProject("App");
        AddProject("Lib", """, { "path": "LibForm.pas", "form": "LibForm.form" }""");
        File(Dir("Lib") + @"\LibForm.pas", "unit LibForm;\ninterface\nimplementation\n{$FORM LibForm.form}\nend.\n");
        File(Dir("Lib") + @"\LibForm.form", "object LibForm: TForm\nend\n");
        AddProject("Tools");
    }

    public InMemoryFileSystem Files { get; } = new();

    public RecordingFileSystem Recording { get; }

    public RecordingLog Log { get; } = new();

    public IdeServices Services { get; }

    public IProjectService Projects => Services.Projects;

    public IProjectGroupService Groups => Services.Groups;

    public ProjectManagerViewModel Pm => Services.ProjectManager;

    public FakeDialogService Dialogs => (FakeDialogService)Services.Dialogs;

    public FakeFileDialogService Pickers => (FakeFileDialogService)Services.FileDialogs;

    public static string Dir(string name) => Root + @"\" + name;

    public static string Proj(string name) => Dir(name) + @"\" + name + ".pproj";

    public static string Unit(string name) => Dir(name) + @"\" + name + "Unit.pas";

    public string File(string path, string text)
    {
        Files.CreateDirectory(Path.GetDirectoryName(path)!);
        Files.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    public string Text(string path) => Encoding.UTF8.GetString(Files.ReadAllBytes(path));

    /// <summary><c>&lt;dir&gt;\&lt;name&gt;.pproj</c> with <c>&lt;name&gt;.pas</c> and <c>&lt;name&gt;Unit.pas</c>.</summary>
    public string AddProject(string name, string extraFiles = "", string? directory = null)
    {
        var dir = directory ?? Dir(name);
        File(dir + @"\" + name + ".pproj", $$"""
            { "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas",
              "files": [ { "path": "{{name}}.pas" }, { "path": "{{name}}Unit.pas" }{{extraFiles}} ] }
            """);
        File(dir + @"\" + name + ".pas", $"program {name};\nbegin\nend.\n");
        File(dir + @"\" + name + "Unit.pas", $"unit {name}Unit;\ninterface\nimplementation\nend.\n");
        return dir + @"\" + name + ".pproj";
    }

    /// <summary>Writes <c>All.pgroup</c> with an unknown key that every later write must keep.</summary>
    public void WriteGroup(string active = "App/App.pproj", params string[] projects)
    {
        if (projects.Length == 0)
            projects = ["App/App.pproj", "Lib/Lib.pproj", "Tools/Tools.pproj"];
        File(GroupPath, $$"""
            {
              "formatVersion": 1,
              "projects": [{{string.Join(", ", projects.Select(p => $"\"{p}\""))}}],
              "activeProject": "{{active}}",
              "x-note": "kept"
            }
            """);
    }

    public async Task OpenGroupAsync(string active = "App/App.pproj", params string[] projects)
    {
        WriteGroup(active, projects);
        Assert.True(await Projects.OpenAsync(GroupPath));
        await Pm.MissingFileChecks;
    }

    public async Task<TextDocumentViewModel> OpenDocumentAsync(string path)
    {
        var result = await Services.Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsAssignableFrom<TextDocumentViewModel>(result.Value);
    }

    public static void Type(TextDocumentViewModel document, string text) => document.Buffer.ApplyEdits([new TextEdit(0, 0, text)]);

    public IReadOnlyList<string> OpenPaths() => [.. Services.Documents.Documents.Select(d => d.FilePath).OfType<string>()];

    public IReadOnlyList<string> MemberNames() => [.. Groups.Members.Select(m => m.Name)];

    public IReadOnlyList<string> RootKeys() => [.. Pm.Roots.Select(r => r.Key)];
}
