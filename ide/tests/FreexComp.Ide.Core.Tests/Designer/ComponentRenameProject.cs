using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.Tests.Refactoring;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using DesignCommandIds = FreexComp.Designer.Surface.Commands;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// The project of the component-rename tests (DSG-CS-130…134, I-383): the designer's form unit with references to its
/// components in method bodies, a second unit B referring to <c>OkButton</c>, the toolchain with the real system unit and
/// small library units, so that the code model binds. The form is opened in the Design view.
/// </summary>
public sealed class ComponentRenameProject : RefactoringTestBase
{
    public const string UnitPath = FormUnitPath;
    public const string FormFile = FormPath;
    public const string CallerPath = B;
    public const string AppPath = Dir + @"\App.pas";

    /// <summary>The designer's unit: <c>OkButton</c> once, <c>Panel1</c> twice, <c>NameEdit</c> once in the bodies (CRLF).</summary>
    public static readonly string UnitText = DesignerFixture.Unit
        .Replace("procedure TMainForm.GreetButtonClick(Sender: TObject);\r\nbegin\r\n\r\nend;",
            "procedure TMainForm.GreetButtonClick(Sender: TObject);\r\nbegin\r\n  OkButton := nil;\r\nend;", StringComparison.Ordinal)
        .Replace("procedure TMainForm.SharedClick(Sender: TObject);\r\nbegin\r\n\r\nend;",
            "procedure TMainForm.SharedClick(Sender: TObject);\r\nbegin\r\n  Panel1 := nil;\r\n  Panel1 := nil;\r\nend;", StringComparison.Ordinal)
        .Replace("procedure TMainForm.NameEditKeyPress(Sender: TObject; var Key: Char);\r\nbegin\r\n\r\nend;",
            "procedure TMainForm.NameEditKeyPress(Sender: TObject; var Key: Char);\r\nbegin\r\n  NameEdit := nil;\r\nend;", StringComparison.Ordinal);

    /// <summary>The second unit: one reference to <c>OkButton</c> (LF).</summary>
    public const string Caller = "unit B;\n\ninterface\n\nuses MainForm;\n\nprocedure Run(F: TMainForm);\n\nimplementation\n\n"
        + "procedure Run(F: TMainForm);\nbegin\n  F.OkButton := nil;\nend;\n\nend.\n";

    public const string Project = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "B.pas" }, { "path": "MainForm.pas", "form": "MainForm.form" } ] }
        """;

    private static readonly (string Name, string Uses, string Types)[] Stubs =
    [
        ("Classes", "", "{$M+}\n  TPersistent = class end;\n  {$M-}\n  TComponent = class(TPersistent) end;"),
        ("Controls", "uses Classes;\n\n", "TControl = class(TComponent) end;"),
        ("Forms", "uses Classes, Controls;\n\n", "TForm = class(TControl) end;"),
        ("StdCtrls", "uses Classes, Controls;\n\n", "TEdit = class(TControl) end;\n  TButton = class(TControl) end;\n  TLabel = class(TControl) end;"),
        ("ExtCtrls", "uses Classes, Controls;\n\n", "TPanel = class(TControl) end;"),
        ("Menus", "uses Classes;\n\n", "TMenuItem = class(TComponent) end;\n  TMainMenu = class(TComponent) end;"),
    ];

    public IdeServices Services => _services;

    public new FakeDialogService Dialogs => base.Dialogs;

    /// <summary>The status bar's current text.</summary>
    public string? Status => _services.Status.Message;

    /// <summary>
    /// Writes the toolchain, the library units and the project — <paramref name="files"/> (path, text) instead of or in
    /// addition to the standard ones — opens it and closes every document. <paramref name="catalog"/>: the designer's
    /// component metadata (default: the code-sync tests' catalog).
    /// </summary>
    public async Task OpenComponentProjectAsync(string project = Project, ComponentCatalog? catalog = null, params (string Path, string Text)[] files)
    {
        WriteToolchain();
        ((DocumentService)_services.Documents).DesignCatalog = catalog ?? CodeSyncTests.Catalog;
        _services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, @"G:\ui");
        foreach (var (name, uses, types) in Stubs)
            File(@"G:\ui\src\" + name + ".pas", $"unit {name};\n\ninterface\n\n{uses}type\n  {types}\n\nimplementation\n\nend.\n");
        File(ProjectPath, project);
        File(AppPath, "program App;\n\nuses B, MainForm;\n\nbegin\n  Run(nil);\nend.\n");
        File(CallerPath, Caller);
        File(UnitPath, UnitText);
        File(FormFile, Form);
        foreach (var (path, text) in files)
            File(path, text);
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        await _services.ProjectManager.MissingFileChecks;
    }

    /// <summary>The standard project with <paramref name="files"/>, and the form unit open in the Design view.</summary>
    public async Task<FormDocumentViewModel> OpenAsync(params (string Path, string Text)[] files)
    {
        await OpenComponentProjectAsync(Project, null, files);
        return await OpenFormAsync();
    }

    public new Task<FormDocumentViewModel> OpenFormAsync(string path = FormUnitPath) => base.OpenFormAsync(path);

    public new Task<TextDocumentViewModel> OpenAsync(string path) => base.OpenAsync(path);

    /// <summary>
    /// The Object Inspector's <c>Name</c> row of <paramref name="component"/> set to <paramref name="name"/> (DSG-OI-070):
    /// the edit is accepted at once; the rename itself completes with <see cref="FormDesignerViewModel.RenameWork"/>.
    /// </summary>
    public void RenameInInspector(FormDocumentViewModel document, string component, string name)
    {
        var inspector = new ObjectInspectorViewModel(_services.Status);
        inspector.Bind(document.Designer);
        DesignerFixture.Select(document.Designer, component);
        inspector.Page = InspectorPage.Properties;
        inspector.CurrentPath = "Name";
        Assert.True(inspector.BeginEdit());
        inspector.EditText = name;
        Assert.True(inspector.Commit());
    }

    /// <summary><see cref="RenameInInspector"/>, then the prepare and commit awaited; its result.</summary>
    public async Task<bool> RenameAsync(FormDocumentViewModel document, string component, string name)
    {
        var before = document.Designer.RenameWork;
        RenameInInspector(document, component, name);
        var work = document.Designer.RenameWork;
        Assert.NotNull(work);
        Assert.NotSame(before, work);
        return await work;
    }

    public static void Undo(FormDocumentViewModel document) => document.Designer.Surface!.Execute(DesignCommandIds.Undo);

    public static void Redo(FormDocumentViewModel document) => document.Designer.Surface!.Execute(DesignCommandIds.Redo);

    public string Disk(string path) => Text(path);

    public byte[] DiskBytes(string path) => Bytes(path);

    public void Put(string path, string text) => File(path, text);

    public void PutBytes(string path, byte[] bytes) => Write(path, bytes);

    public void SetReadOnly(string path) => _fs.SetReadOnly(path, true);

    public new string Snapshot() => base.Snapshot();

    public static string Renamed(string text, string from, string to) => text.Replace(from, to, StringComparison.Ordinal);

    public static byte[] Utf8WithBom(string text) => [.. Utf8Bom, .. Encoding.UTF8.GetBytes(text)];
}
