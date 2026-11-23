using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Multi-view documents (designer.md §1, main-window.md UI-WIN-020; I-129): one tab, views Code / Design / Text.</summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class DocumentViewSwitchTests
{
    private const string Unit = "unit MainForm;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\n{$FORM MainForm.form}\r\n\r\nend.\r\n";
    private const string FormText = "object MainForm: TMainForm\r\n  Caption = 'MainForm'\r\nend\r\n";

    private static void Files(HarnessOptions o)
    {
        o.InitialFiles["MainForm.pas"] = Unit;
        o.InitialFiles["MainForm.form"] = FormText;
        o.InitialFiles["Plain.pas"] = "unit Plain;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n";
    }

    private static async Task<DocumentViewModel> OpenAsync(HeadlessHarness h, string name)
    {
        var document = (await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, name))).Value!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return document;
    }

    private static string Tab(string view) => AutomationIds.DocumentViewTab("MainForm.pas", view);

    /// <summary>The editor shown in the form document's view (null in the Design view).</summary>
    private static CodeEditorView? VisibleEditor(HeadlessHarness h) =>
        h.Window.GetVisualDescendants().OfType<CodeEditorView>().SingleOrDefault(e => e.IsEffectivelyVisible);

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-020")]
    [Trait("Req", "UI-DSG-001")]
    [Trait("Req", "UI-DSG-002")]
    public async Task SwitcherShowsThreeViewsForFormUnits()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var document = Assert.IsType<FormDocumentViewModel>(await OpenAsync(h, "MainForm.pas"));

        // One tab for the unit and its form; the switcher strip (26 high) with Code active.
        Assert.Single(h.Services.Documents.Documents);
        Assert.Equal(Path.Combine(h.TempDir, "MainForm.form"), document.FormText.FilePath);
        Assert.Equal(FormText, document.FormBuffer.Text);
        var code = h.Find<ToggleButton>(Tab("Code"));
        var design = h.Find<ToggleButton>(Tab("Design"));
        var text = h.Find<ToggleButton>(Tab("Text"));
        Assert.Equal(26, Assert.IsAssignableFrom<Control>(code.GetVisualParent()!.GetVisualParent()).Bounds.Height);
        Assert.Equal(DocumentView.Code, document.ActiveView);
        Assert.True(code.IsChecked);
        Assert.False(design.IsChecked);
        Assert.Equal(Unit, VisibleEditor(h)!.Editor.Text);

        h.Click(Tab("Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.True(design.IsChecked);
        Assert.False(code.IsChecked);
        Assert.Null(VisibleEditor(h));

        h.Click(Tab("Text"));
        Assert.Equal(DocumentView.Text, document.ActiveView);
        Assert.True(text.IsChecked);
        Assert.Equal(FormText, VisibleEditor(h)!.Editor.Text);
        Assert.Same(document.FormText, document.CommandTarget);

        h.Click(Tab("Code"));
        Assert.Equal(Unit, VisibleEditor(h)!.Editor.Text);
        Assert.Same(document, document.CommandTarget);
        h.Click(Tab("Code")); // the active tab stays active
        Assert.True(code.IsChecked);
        Assert.Equal(DocumentView.Code, document.ActiveView);

        // A unit without a companion form: a plain document, no switcher.
        Assert.IsNotType<FormDocumentViewModel>(await OpenAsync(h, "Plain.pas"));
        Assert.Empty(h.FindAll("Document.Plain.pas.ViewTabs"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-001")]
    public async Task F7TogglesCodeAndDesign()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var document = Assert.IsType<FormDocumentViewModel>(await OpenAsync(h, "MainForm.pas"));

        h.Press("F7");
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.True(h.Find<ToggleButton>(Tab("Design")).IsChecked);
        h.Press("F7");
        Assert.Equal(DocumentView.Code, document.ActiveView);

        // View Form as Text, then F7 goes to the Design view (designer README §3).
        await h.ExecuteAsync("view.formAsText");
        Assert.Equal(DocumentView.Text, document.ActiveView);
        Assert.True(h.Find<ToggleButton>(Tab("Text")).IsChecked);
        h.Press("F7");
        Assert.Equal(DocumentView.Design, document.ActiveView);

        // Both commands need a form document.
        Assert.True(h.Services.Commands.CanExecute("view.toggleFormCode"));
        await OpenAsync(h, "Plain.pas");
        Assert.False(h.Services.Commands.CanExecute("view.toggleFormCode"));
        Assert.False(h.Services.Commands.CanExecute("view.formAsText"));
        h.Press("F7");
        Assert.Equal(DocumentView.Design, document.ActiveView);
    }

    /// <summary>The Phase-5 commands at the product's implemented phase, in a form document's Code and Design views.</summary>
    private static readonly string[] Phase5ViewCommands =
        ["view.toggleFormCode", "view.formAsText", "view.objectInspector", "view.toolPalette", "view.toolbar.designer",
         "window.layout.design", "component.reloadMetadata"];

    private static readonly string[] Phase5DesignerCommands =
        ["designer.toggleGrid", "designer.toggleSnapToGrid", "designer.toggleGuidelines", "designer.lockControls", "designer.tabOrder",
         "designer.selectNext", "designer.sendToBack", "designer.selectParent"];

    /// <summary>
    /// I-207: <c>ServiceGraph.ImplementedPhase</c> is 5, so the designer and form-view commands are real (not later-phase stubs)
    /// and enabled in their context without a test override; below Phase 5 the same commands are disabled stubs.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-001")]
    public async Task CommandsEnabledInPhase5()
    {
        Assert.Equal(5, Core.Composition.ServiceGraph.ImplementedPhase);
        await using (var h = await HeadlessHarness.StartAsync(DesignerViewTests.Files))
        {
            Assert.Equal(5, h.Services.ImplementedPhase);
            var document = await DesignerViewTests.OpenFormAsync(h);
            Assert.All(Phase5ViewCommands, id => Assert.True(h.Services.Commands.CanExecute(id), id));
            h.Click(Tab("Design"));
            Assert.Equal(DocumentView.Design, document.ActiveView);
            document.Designer.Surface!.Selection.Select(document.Designer.Model!.FindByName("GreetButton")!);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.All(Phase5DesignerCommands, id => Assert.True(h.Services.Commands.CanExecute(id), id));
            await h.ExecuteAsync("view.toggleFormCode");
            Assert.Equal(DocumentView.Code, document.ActiveView);
        }

        // The gating itself: at Phase 4 they are later-phase stubs, never enabled. (The two form-view commands are registered by
        // the document service at every phase and stay usable on a form document: they are not part of the phase switch.)
        await using var early = await HeadlessHarness.StartAsync(o =>
        {
            Files(o);
            o.ImplementedPhase = 4;
        });
        await OpenAsync(early, "MainForm.pas");
        Assert.All(Phase5ViewCommands.Skip(2).Concat(Phase5DesignerCommands), id => Assert.False(early.Services.Commands.CanExecute(id), id));
    }
}
