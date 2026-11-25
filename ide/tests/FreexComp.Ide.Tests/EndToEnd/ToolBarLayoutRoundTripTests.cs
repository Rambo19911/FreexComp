using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Rendering;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// I-288 (found by D-117): a tool bar whose tool buttons have no stored <c>Width</c> — as a form written by hand or by the
/// run-time writer has them (a tool button stores its width only as a separator, UIF-800) — is laid out by the designer with
/// the class's default size; saving must not write a width the run-time layout then uses differently. Design → save →
/// compile → run with <c>--dump-layout</c>: the run-time layout equals the designer's layout of the form as it was shown
/// before the save, and the designer's layout of the saved file.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class ToolBarLayoutRoundTripTests
{
    private const string ToolBar = """
          object ToolBar1: TToolBar
            Left = 0
            Top = 0
            Width = 400
            Height = 29
            object ToolButton1: TToolButton
              Caption = 'One'
            end
            object ToolButton2: TToolButton
              Style = tbsSeparator
            end
            object ToolButton3: TToolButton
              Caption = 'Three'
            end
          end

        """;

    [AvaloniaFact(Timeout = 120000)]
    [Trait("Task", "I-288")]
    [Trait("Req", "DSG-FD-044")]
    [Trait("Req", "DSG-LAY-063")]
    public async Task Layout_ToolBarWithoutStoredWidths_DesignerEqualsRuntime()
    {
        await using var h = await Phase5Scenario.StartAsync();
        Assert.Equal(NewProjectOutcome.Created, await h.Services.Projects.CreateProjectAsync(
            new NewProjectRequest("gui-pascal", Phase5Scenario.ProjectName, h.TempDir)));

        // The form unit and file as written outside the designer: a tool bar with a button, a separator and a button.
        var unit = Phase5Scenario.UnitPath(h);
        var unitText = File.ReadAllText(unit, Encoding.UTF8);
        unitText = unitText.Replace("Classes, Controls, Forms;", "Classes, Controls, Forms, ComCtrls;", StringComparison.Ordinal)
            .Replace("class(TForm)", "class(TForm)\n    ToolBar1: TToolBar;\n    ToolButton1: TToolButton;\n    ToolButton2: TToolButton;\n    ToolButton3: TToolButton;", StringComparison.Ordinal);
        File.WriteAllText(unit, unitText.ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
        var form = Phase5Scenario.FormPath(h);
        var formText = File.ReadAllText(form, Encoding.UTF8).ReplaceLineEndings("\n");
        var end = formText.TrimEnd().LastIndexOf("end", StringComparison.Ordinal);
        File.WriteAllText(form, (formText[..end] + ToolBar + formText[end..]).ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
        await h.Services.FileChanges.WhenIdle();

        var s = await Phase5Scenario.OpenDesignAsync(h, unit);
        var model = s.Document.Designer.Model!;
        Assert.Null(s.Object("ToolButton2").FindProperty("Width"));

        // An edit, so that the save writes the form: the tool bar's button width in the Object Inspector.
        s.Surface.Selection.Select(s.Object("ToolBar1"));
        Dispatcher.UIThread.RunJobs();
        s.Inspect("ButtonWidth", "30");
        Assert.Equal(30, s.Prop("ToolBar1", "ButtonWidth"));

        // The layout the designer shows, in run-time mode (DSG-LAY-061), before the save.
        var engine = new LayoutEngine(s.Document.Designer.Catalog, RendererRegistry.CreateDefault());
        var shown = engine.ComputeWithSteps(model, "dump\n", LayoutMode.RunTime)[^1].ToDump();

        await Phase5Scenario.SaveAsync(s);
        var saved = File.ReadAllText(form, Encoding.UTF8);
        Assert.Contains("object ToolButton2: TToolButton", saved, StringComparison.Ordinal);

        var exe = await Phase5Scenario.BuildAsync(h);
        var runtime = await LayoutRoundTrip.RunDumpAsync(exe, h.TempDir);
        Assert.Equal([Phase5Scenario.FormName], runtime.Forms.Select(f => f.Name));
        LayoutRoundTrip.AssertEqual(new LayoutDumpDocument(LayoutDump.FormatVersion, LayoutDump.Dpi, [shown]), runtime, "the form as shown before the save:\n" + saved);
        LayoutRoundTrip.AssertEqual(LayoutRoundTrip.DesignerDump(Phase5Scenario.ProjectDir(h), runtime), runtime, "the saved form");
    }
}
