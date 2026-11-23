using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>D-088: the Design view and the component metadata loaded at start-up (tool-palette.md DSG-TP-003, DSG-TP-004).</summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class DesignerMetadataViewTests
{
    private static async Task<FormDocumentViewModel> OpenAsync(HeadlessHarness h)
    {
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "MainForm.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        return document;
    }

    private static string Tab(string view) => AutomationIds.DocumentViewTab("MainForm.pas", view);

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-003")]
    public async Task DesignViewShowsProgressRingUntilMetadataLoaded()
    {
        using var gate = new ManualResetEventSlim();
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            DesignerViewTests.Files(o);
            o.MetadataGate = gate;
        });
        var metadata = h.Services.Metadata!;
        Assert.Equal(MetadataLoadState.Loading, metadata.State); // the main window is up, the load still runs

        // Code view works; the Design view shows the centered progress ring instead of the surface.
        var document = await OpenAsync(h);
        Assert.Equal(DocumentView.Code, document.ActiveView);
        h.Click(Tab("Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var ring = h.Find<ProgressRing>(AutomationIds.DesignerLoading("MainForm"));
        Assert.True(ring.IsEffectivelyVisible);
        Assert.False(h.TryFind<DesignSurfaceControl>(AutomationIds.Designer("MainForm"))?.IsEffectivelyVisible == true);

        gate.Set();
        await h.WaitForAsync(() => metadata.Loading.IsCompleted, "metadata loaded");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(MetadataLoadState.Loaded, metadata.State);
        Assert.False(ring.IsEffectivelyVisible);
        var surface = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        Assert.True(surface.IsEffectivelyVisible);
        Assert.False(document.Designer.Surface!.IsReadOnly);
        Assert.NotNull(document.Designer.Model!.FindByName("GreetButton")!.Info); // bound to the loaded TButton
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-004")]
    public async Task MissingMetadataKeepsDesignViewClosed()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            DesignerViewTests.Files(o);
            o.ComponentMetadata = false;
            o.MessageAnswers["Msg.Designer.MetadataMissing"] = "Cancel";
        });
        Assert.Equal(MetadataLoadState.Missing, h.Services.Metadata!.State);
        var document = await OpenAsync(h);

        // The Design tab does not switch; the message names the expected index, once per session.
        h.Click(Tab("Design"));
        await h.WaitForAsync(() => h.Messages.Requests.Count == 1, "Msg.Designer.MetadataMissing");
        var request = Assert.Single(h.Messages.Requests);
        Assert.Equal("Msg.Designer.MetadataMissing", request.MessageId);
        Assert.Equal([Path.Combine(h.UserDir, "ide", "ui-framework", "design", "index.json")], request.Args);
        Assert.Equal(DocumentView.Code, document.ActiveView);
        Assert.True(h.Find<ToggleButton>(Tab("Code")).IsChecked);
        Assert.False(h.Find<ToggleButton>(Tab("Design")).IsChecked);

        h.Press("F7");
        h.Click(Tab("Design"));
        Assert.Equal(DocumentView.Code, document.ActiveView);
        Assert.Single(h.Messages.Requests);
        Assert.Null(h.TryFind<Control>(AutomationIds.Designer("MainForm")));
    }
}
