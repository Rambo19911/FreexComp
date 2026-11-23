using System.Collections.Immutable;
using Avalonia.Headless.XUnit;
using FreexComp.Compiler;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Code written by the designer, checked by the real compiler with its form support (<c>{$FORM}</c>, E3101/E3102): the
/// whole program's diagnostics as <c>opc --check</c> reports them (<c>Compilation.GetDiagnostics</c>, api.md §1), through
/// the IDE's code model over the open project with the real RTL and framework sources (code-sync.md DSG-CS-092).
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class CodeSyncE2ETests
{
    /// <summary>A new <c>gui-pascal</c> project: a button placed, its default handler created by a double-click, saved; the unit compiles.</summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-092")]
    public async Task DesignerProducedUnitCompiles()
    {
        await using var h = await Phase5Scenario.StartAsync();
        var s = await Phase5Scenario.CreateProjectAndDesignAsync(h);
        await h.ExecuteAsync("view.toolPalette");
        s.Place("TButton", 40, 40);
        var (x, y) = s.Center("Button1");
        s.DoubleClick(x, y);
        Assert.Equal(DocumentView.Code, s.Document.ActiveView);
        var text = s.Document.Buffer.Text;
        Assert.Contains("    Button1: TButton;\r\n", text, StringComparison.Ordinal);
        Assert.Contains("    procedure Button1Click(Sender: TObject);\r\n", text, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.Button1Click(Sender: TObject);\r\nbegin\r\n", text, StringComparison.Ordinal);
        await Phase5Scenario.SaveAsync(s);
        Assert.Contains("OnClick = Button1Click", File.ReadAllText(Phase5Scenario.FormPath(h)), StringComparison.Ordinal);

        var errors = Errors(await DiagnosticsAsync(h));
        Assert.True(errors.Count == 0, "the designer-produced unit does not compile:\n" + string.Join("\n", errors));
    }

    /// <summary>A form whose components and handler the template unit does not declare.</summary>
    private const string BrokenForm = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'MainForm'
          ClientWidth = 400
          ClientHeight = 300
          PixelsPerInch = 96
          object NameEdit: TEdit
            Left = 16
            Top = 16
            Width = 121
            Height = 23
            TabOrder = 0
          end
          object GoButton: TButton
            Left = 16
            Top = 56
            Width = 75
            Height = 25
            Caption = 'Go'
            TabOrder = 1
            OnClick = GoButtonClick
          end
        end

        """;

    /// <summary>
    /// DSG-CS-092: a unit whose form class lacks the component fields and an event handler is reported with E3101 by the
    /// compiler; after the designer's <c>Repair</c> (before the first designer edit and at the save of the modified
    /// document, DSG-CS-091 a/b) the saved program has neither E3101 nor E3102, and no error at all.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-092")]
    public async Task RepairedUnitHasNoE3101()
    {
        await using var h = await Phase5Scenario.StartAsync();
        Assert.Equal(Core.Projects.NewProjectOutcome.Created, await h.Services.Projects.CreateProjectAsync(
            new Core.Projects.NewProjectRequest("gui-pascal", Phase5Scenario.ProjectName, h.TempDir)));
        await File.WriteAllTextAsync(Phase5Scenario.FormPath(h), BrokenForm.ReplaceLineEndings("\n"));

        var before = await DiagnosticsAsync(h);
        Assert.True(before.Count(d => d.Code == "E3101") == 3, "expected E3101 for NameEdit, GoButton and GoButtonClick:\n" + string.Join("\n", before));

        var s = await Phase5Scenario.OpenDesignAsync(h, Phase5Scenario.UnitPath(h));
        s.Surface.Selection.Set(["GoButton"]);
        h.Press("Ctrl+Right"); // a designer edit: the document is modified
        Assert.True(s.Document.IsModified);
        await Phase5Scenario.SaveAsync(s);
        var unit = File.ReadAllText(Phase5Scenario.UnitPath(h));
        Assert.Contains("    NameEdit: TEdit;\r\n", unit, StringComparison.Ordinal);
        Assert.Contains("procedure GoButtonClick(Sender: TObject);", unit, StringComparison.Ordinal);

        var after = await DiagnosticsAsync(h);
        Assert.DoesNotContain(after, d => d.Code is "E3101" or "E3102");
        var errors = Errors(after);
        Assert.True(errors.Count == 0, "the repaired unit does not compile:\n" + string.Join("\n", errors));
    }

    /// <summary>The program's diagnostics (parse + bind of every unit, as <c>opc --check</c>) with the open buffers.</summary>
    private static async Task<ImmutableArray<Diagnostic>> DiagnosticsAsync(HeadlessHarness h)
    {
        var task = h.Services.CodeModel.GetProjectDiagnosticsAsync(CancellationToken.None);
        await h.WaitForAsync(() => task.IsCompleted, "program diagnostics");
        return await task;
    }

    private static List<string> Errors(ImmutableArray<Diagnostic> diagnostics) =>
        [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Location.Path}({d.Location.LineSpan.Start.Line + 1}): {d.Code} {d.Message}")];
}
