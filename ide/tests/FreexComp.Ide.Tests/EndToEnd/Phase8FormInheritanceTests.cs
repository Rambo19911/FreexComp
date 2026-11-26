using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Card F05-007 acceptance 2 with the real build engine, toolchain and framework metadata (form-inheritance-designer.md
/// §13, §14; D-132): a base dialog designed in the IDE, two levels of descendants made with New Inherited Form, each
/// changing inherited components and adding new ones, a hiding <c>inherited;</c> handler created by a double-click; saved,
/// built, run with <c>--dump-layout</c>, and the runtime layout of every level compared with the designer's.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase8FormInheritanceTests
{
    private const string Stub = "procedure TMainForm22.Button1Click(Sender: TObject);\r\nbegin\r\n  inherited;\r\n  \r\nend;\r\n";

    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-120")]
    [Trait("Req", "DSG-CS-121")]
    [Trait("Req", "DSG-CS-124")]
    [Trait("Req", "DSG-CS-125")]
    [Trait("Req", "DSG-CS-126")]
    [Trait("Req", "DSG-FD-123")]
    [Trait("Req", "DSG-FD-128")]
    [Trait("Req", "DSG-FD-130")]
    public async Task E2E_Phase8_FormInheritance_DesignSaveCompile_LayoutIdentical()
    {
        await using var h = await Phase5Scenario.StartAsync();
        var base1 = await Phase5Scenario.CreateProjectAndDesignAsync(h);
        var dir = Phase5Scenario.ProjectDir(h);
        await h.ExecuteAsync("view.toolPalette");

        // Level 1, the base dialog: a panel 300 wide holding two buttons; the first button's OnClick handler.
        base1.Place("TPanel", 8, 8);
        base1.Inspect("Width", "300");
        var panel = base1.Surface.Geometry.GetBounds(base1.Object("Panel1"));
        base1.PlaceAt("TButton", panel.Left + 12, panel.Top + 12);
        base1.PlaceAt("TButton", panel.Left + 100, panel.Top + 12);
        Assert.Equal(["Button1", "Button2"], base1.Object("Panel1").Children.Select(c => c.Name));
        var (bx, by) = base1.Center("Button1");
        base1.DoubleClick(bx, by);
        Assert.Equal(DocumentView.Code, base1.Document.ActiveView);
        h.Press("F7");
        await Phase5Scenario.SaveAsync(base1);
        Assert.Contains("      OnClick = Button1Click\n", File.ReadAllText(Phase5Scenario.FormPath(h), Encoding.UTF8), StringComparison.Ordinal);

        // Level 2 by New Inherited Form (the active form preselected): Button2's caption overridden, a button added into the panel.
        var level2 = await NewInheritedAsync(h, "TMainForm", Path.Combine(dir, "MainForm2.pas"));
        Assert.True(level2.Object("Button2").IsInherited);
        Select(level2, "Button2");
        level2.Inspect("Caption", "Close");
        panel = level2.Surface.Geometry.GetBounds(level2.Object("Panel1"));
        level2.PlaceAt("TButton", panel.Left + 200, panel.Top + 12);
        Assert.Equal(["Button1", "Button2", "Button3"], level2.Object("Panel1").Children.Select(c => c.Name));
        await Phase5Scenario.SaveAsync(level2);
        var form2 = File.ReadAllText(Path.Combine(dir, "MainForm2.form"), Encoding.UTF8);
        Assert.StartsWith("inherited MainForm2: TMainForm2\n  inherited Panel1: TPanel\n", form2, StringComparison.Ordinal); // no root lines
        Assert.Contains("    inherited Button2: TButton\n      Caption = 'Close'\n    end\n    object Button3: TButton\n", form2, StringComparison.Ordinal);
        Assert.Contains("  TMainForm2 = class(TMainForm)\r\n", level2.Document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("    Button3: TButton;\r\n", level2.Document.Buffer.Text, StringComparison.Ordinal);

        // Level 3: an inherited button moved, an edit added, the hiding stub of the inherited OnClick by a double-click.
        var level3 = await NewInheritedAsync(h, "TMainForm2", Path.Combine(dir, "MainForm22.pas"));
        Select(level3, "Button1");
        level3.Inspect("Left", "24");
        level3.Place("TEdit", 8, 80);
        (bx, by) = level3.Center("Button1");
        level3.DoubleClick(bx, by);
        Assert.Equal(DocumentView.Code, level3.Document.ActiveView);
        var buffer = level3.Document.Buffer;
        var stub = buffer.Text.IndexOf(Stub, StringComparison.Ordinal);
        Assert.True(stub >= 0, buffer.Text);
        buffer.ApplyEdits([new TextEdit(stub + Stub.IndexOf("  \r\nend;", StringComparison.Ordinal) + 2, 0, "Tag := 3;")]); // the stub is kept
        Dispatcher.UIThread.RunJobs();
        h.Press("F7");
        await Phase5Scenario.SaveAsync(level3);
        var form3 = File.ReadAllText(Path.Combine(dir, "MainForm22.form"), Encoding.UTF8);
        Assert.Contains("  inherited Panel1: TPanel\n    inherited Button1: TButton\n      Left = 24\n    end\n  end\n  object Edit1: TEdit\n", form3,
            StringComparison.Ordinal);
        Assert.DoesNotContain("OnClick", form3, StringComparison.Ordinal); // the ancestor's line binds the hiding method
        var unit3 = File.ReadAllText(Path.Combine(dir, "MainForm22.pas"), Encoding.UTF8);
        Assert.Contains("procedure TMainForm22.Button1Click(Sender: TObject);\r\nbegin\r\n  inherited;\r\n  Tag := 3;\r\nend;\r\n", unit3, StringComparison.Ordinal);

        // The overridden caption is in levels 2 and 3 only.
        static string Caption(Phase5Scenario.Session s) => ((StringValue)PropertyAccess.GetEffective(s.Object("Button2"), "Caption")).Value;
        Assert.Equal(("Button2", "Close", "Close"), (Caption(base1), Caption(level2), Caption(level3)));

        // The program creates the three forms; built (no diagnostic), run with --dump-layout: every level equals the designer's.
        var program = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(dir, Phase5Scenario.ProjectName + ".pas"))).Value);
        program.Buffer.ApplyEdits([new TextEdit(0, program.Buffer.Text.Length, string.Join("\r\n",
            $"program {Phase5Scenario.ProjectName};", "", "uses", "  Forms, MainForm, MainForm2, MainForm22;", "", "begin", "  Application.Initialize;",
            "  Application.CreateForm(TMainForm, MainForm.MainForm);", "  Application.CreateForm(TMainForm2, MainForm2.MainForm2);",
            "  Application.CreateForm(TMainForm22, MainForm22.MainForm22);", "  Application.Run;", "end.", ""))]);
        Assert.True(await h.Services.Documents.SaveAsync(program));
        var exe = await Phase5Scenario.BuildAsync(h);
        Assert.True(h.Services.Messages.Items.All(i => i.IsSummary), // zero diagnostics: only the build summary line
            string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}")));
        var runtime = await LayoutRoundTrip.RunDumpAsync(exe, h.TempDir);
        Assert.Equal(["MainForm", "MainForm2", "MainForm22"], runtime.Forms.Select(f => f.Name));
        var designer = LayoutRoundTrip.DesignerDump(dir, runtime);
        LayoutRoundTrip.AssertEqual(designer, runtime, "form inheritance");
        var leaf = runtime.Forms[2].Controls;
        Assert.Equal(["Panel1", "Edit1"], leaf.Select(c => c.Name));
        Assert.Equal(["Button1", "Button2", "Button3"], leaf[0].Controls.Select(c => c.Name));
        Assert.Equal(24, leaf[0].Controls[0].Left);
    }

    /// <summary>
    /// File › New › Inherited Form with <paramref name="ancestor"/> preselected (the active form), OK: the new unit opens in
    /// the Design view, resolved against its ancestor.
    /// </summary>
    private static async Task<Phase5Scenario.Session> NewInheritedAsync(HeadlessHarness h, string ancestor, string unitPath)
    {
        var running = h.Services.Commands.ExecuteAsync("file.newInheritedForm");
        var dlg = await h.WaitForDialogAsync("NewInheritedForm");
        Assert.True(dlg.Find<ListBoxItem>("Item." + ancestor).IsSelected);
        dlg.Click("Ok");
        await h.WaitForAsync(() => running.IsCompleted, "New Inherited Form closed");
        await running;
        Assert.Equal(unitPath, h.Services.Documents.ActiveDocument!.FilePath);
        var session = await Phase5Scenario.OpenDesignAsync(h, unitPath);
        Assert.True(session.Document.Designer.Model!.Root.IsInheritedRoot);
        Assert.Empty(session.Document.Designer.Model.Diagnostics);
        return session;
    }

    /// <summary>A click on the component (first elsewhere: a second press on the same spot would be a double-click).</summary>
    private static void Select(Phase5Scenario.Session s, string name)
    {
        var client = s.Surface.Geometry.FormClientRect;
        s.Click(client.Right - 4, client.Bottom - 4);
        var (x, y) = s.Center(name);
        s.Click(x, y);
        Assert.Equal([name], s.Selected);
    }
}
