using FreexComp.Designer.Catalog;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>D-088: the component metadata loaded at IDE start (tool-palette.md DSG-TP-003, DSG-TP-004).</summary>
[Trait("Phase", "5")]
public sealed class ComponentMetadataServiceTests
{
    private const string Meta = @"C:\meta";

    private static void CopyTestMetadata(DesignerFixture f)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "metadata"), "*.json"))
            f.File(Path.Combine(Meta, Path.GetFileName(file)), File.ReadAllText(file));
        f.Services.Settings.Set(SettingsCatalog.ToolchainFrameworkDesignDir, Meta);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-003")]
    public Task StartupLoadsInBackgroundAndWaitingFormsBindToIt() => TestUiThread.RunAsync(StartupLoadsInBackgroundOnUiThread);

    private static async Task StartupLoadsInBackgroundOnUiThread()
    {
        var deadline = CurrentTest.Deadline;
        using var gate = new ManualResetEventSlim();
        RecordingFileSystem? recording = null;
        RecordingLog? log = null;
        var f = new DesignerFixture(o =>
        {
            o.FileSystem = recording = new RecordingFileSystem(o.FileSystem);
            log = (RecordingLog)o.Log;
        });
        recording!.BeforeRead = path =>
        {
            if (path.StartsWith(Meta, StringComparison.OrdinalIgnoreCase))
                gate.Wait(deadline.Remaining); // the metadata read is held on its background thread
        };
        CopyTestMetadata(f);
        var metadata = f.Services.Metadata!;
        Assert.Equal(MetadataLoadState.NotLoaded, metadata.State);

        // IDE-ARCH-015 step 9: the start-up sequence starts the load and does not wait for it.
        await StartupSequence.RunAsync(f.Services);
        Assert.Equal(MetadataLoadState.Loading, metadata.State);
        Assert.False(metadata.Loading.IsCompleted);

        // A form opened meanwhile: the Code view works, the Design view waits (progress ring) and is not active yet.
        var document = await f.OpenAsync(design: false);
        var designer = document.Designer;
        Assert.True(designer.IsMetadataLoading);
        var waits = 0;
        designer.MetadataLoadingChanged += (_, _) => waits++;
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.False(designer.IsDesignActive);

        gate.Set();
        await metadata.Loading;
        Assert.Equal(MetadataLoadState.Loaded, metadata.State);
        Assert.NotSame(ComponentCatalog.Empty, metadata.Catalog);
        Assert.NotNull(metadata.Catalog.Find("TButton"));
        var palette = metadata.Catalog.All.Count(c => c.Palette is not null && !c.IsAbstract);
        Assert.True(palette > 0);
        Assert.Contains($"INFO Designer: component metadata loaded: {metadata.Catalog.All.Count} classes, {palette} on the palette", log!.Lines);
        Assert.Same(metadata.Catalog, ((DocumentService)f.Services.Documents).DesignCatalog);

        // The waiting form is read against the loaded catalog: it is designable now.
        Assert.False(designer.IsMetadataLoading);
        Assert.Equal(1, waits);
        Assert.Same(metadata.Catalog, designer.Catalog);
        Assert.True(designer.IsDesignActive);
        Assert.False(DesignerFixture.Object(designer, "GreetButton").IsPreserved);
        Assert.False(designer.Surface!.IsReadOnly);
        Assert.False(document.IsModified);

        // A form opened afterwards does not wait.
        f.File(@"C:\p\Second.pas", DesignerFixture.Unit.Replace("unit MainForm", "unit Second", StringComparison.Ordinal)
            .Replace("MainForm.form", "Second.form", StringComparison.Ordinal));
        f.File(@"C:\p\Second.form", DesignerFixture.Form);
        var second = Assert.IsType<FormDocumentViewModel>((await f.Services.Documents.OpenAsync(@"C:\p\Second.pas")).Value);
        Assert.False(second.Designer.IsMetadataLoading);
        Assert.Same(metadata.Catalog, second.Designer.Catalog);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-004")]
    public Task MissingIndexUsesEmptyCatalogAndRefusesTheDesignView() => TestUiThread.RunAsync(MissingIndexOnUiThread);

    private static async Task MissingIndexOnUiThread()
    {
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        f.Services.Settings.Set(SettingsCatalog.ToolchainFrameworkDesignDir, @"C:\nowhere");
        var metadata = f.Services.Metadata!;
        await metadata.LoadAsync();
        Assert.Equal(MetadataLoadState.Missing, metadata.State);
        Assert.Same(ComponentCatalog.Empty, metadata.Catalog);
        Assert.Same(ComponentCatalog.Empty, ((DocumentService)f.Services.Documents).DesignCatalog);

        // The first Design view activation is refused with Msg.Designer.MetadataMissing; Open Options opens Toolchain Paths.
        dialogs.Answer("Msg.Designer.MetadataMissing", "OpenOptions");
        string? page = null;
        dialogs.Script(OptionsViewModel.DialogName, dialog =>
        {
            page = ((OptionsViewModel)dialog).SelectedPage?.Id;
            return dialog.PressCancelAsync();
        });
        var document = await f.OpenAsync(design: false);
        Assert.False(document.ShowView(DocumentView.Design));
        Assert.Equal(DocumentView.Code, document.ActiveView);
        await metadata.MissingMessage;
        var request = Assert.Single(dialogs.Requests);
        Assert.Equal("Msg.Designer.MetadataMissing", request.MessageId);
        Assert.Equal([Path.Combine(@"C:\nowhere", "index.json")], request.Args);
        Assert.Equal(OptionsViewModel.ToolchainPathsPage, page);

        // Once per IDE session: later activations are refused without a message.
        Assert.False(document.ShowView(DocumentView.Design));
        document.ToggleFormCode();
        Assert.Equal(DocumentView.Code, document.ActiveView);
        Assert.Single(dialogs.Requests);
        Assert.True(document.ShowView(DocumentView.Text)); // the form text stays available

        // A form with a catalog of its own (not the empty one) is not refused.
        ((DocumentService)f.Services.Documents).DesignCatalog = CodeSyncTests.Catalog;
        f.File(@"C:\p\Second.pas", DesignerFixture.Unit.Replace("unit MainForm", "unit Second", StringComparison.Ordinal)
            .Replace("MainForm.form", "Second.form", StringComparison.Ordinal));
        f.File(@"C:\p\Second.form", DesignerFixture.Form);
        var second = Assert.IsType<FormDocumentViewModel>((await f.Services.Documents.OpenAsync(@"C:\p\Second.pas")).Value);
        Assert.True(second.ShowView(DocumentView.Design));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-003")]
    [Trait("Req", "DSG-TP-004")]
    public Task LoaderFailureEndsInErrorState() => TestUiThread.RunAsync(LoaderFailureEndsInErrorStateOnUiThread);

    private static async Task LoaderFailureEndsInErrorStateOnUiThread()
    {
        var deadline = CurrentTest.Deadline;
        using var gate = new ManualResetEventSlim();
        var dialogs = new FakeDialogService();
        RecordingFileSystem? recording = null;
        RecordingLog? log = null;
        var f = new DesignerFixture(o =>
        {
            o.Dialogs = dialogs;
            o.FileSystem = recording = new RecordingFileSystem(o.FileSystem);
            log = (RecordingLog)o.Log;
        });
        CopyTestMetadata(f);
        recording!.BeforeRead = path =>
        {
            if (path.StartsWith(Meta, StringComparison.OrdinalIgnoreCase))
            {
                gate.Wait(deadline.Remaining);
                throw new InvalidOperationException("metadata reader broke"); // not an I/O error: the loader itself fails
            }
        };
        var metadata = f.Services.Metadata!;
        await StartupSequence.RunAsync(f.Services);
        var document = await f.OpenAsync(design: false);
        var designer = document.Designer;
        Assert.True(designer.IsMetadataLoading);

        // The load fails: a terminal error state, the waiting form stops waiting (no endless progress ring).
        gate.Set();
        await metadata.Loading;
        Assert.Equal(MetadataLoadState.Failed, metadata.State);
        Assert.Same(ComponentCatalog.Empty, metadata.Catalog);
        Assert.False(designer.IsMetadataLoading);
        Assert.Contains(log!.Lines, l => l.StartsWith("ERROR Designer: ", StringComparison.Ordinal) && l.Contains("metadata reader broke", StringComparison.Ordinal));

        // Like a missing index: the Design view is refused with Msg.Designer.MetadataMissing, naming the reason.
        dialogs.Answer("Msg.Designer.MetadataMissing", "Cancel");
        Assert.False(document.ShowView(DocumentView.Design));
        await metadata.MissingMessage;
        var request = Assert.Single(dialogs.Requests);
        Assert.Equal("Msg.Designer.MetadataMissing", request.MessageId);
        Assert.Contains("metadata reader broke", Assert.Single(request.Args), StringComparison.Ordinal);

        // The reload command is available again (not stuck in Loading).
        recording.BeforeRead = null;
        Assert.True(f.Services.Commands.CanExecute("component.reloadMetadata"));
    }
}
