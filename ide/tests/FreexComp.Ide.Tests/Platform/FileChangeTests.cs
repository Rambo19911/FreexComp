using Avalonia.Headless.XUnit;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Platform;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>External change detection with the real <c>FileSystemWatcher</c> (IDE-P1-110).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class FileChangeTests
{
    // IDE-TEST-006: 30 s per test; IDE-TEST-013: the watcher event is awaited with the harness's 5 s wait.
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-110")]
    [Trait("Req", "IDE-P1-113")]
    public async Task RealWatcherDetectsChange()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealFileWatchers = true;
            o.InitialFiles["Watched.pas"] = "unit Watched;\r\n";
        });
        Assert.IsType<FileSystemWatcherFactory>(h.Services.FileWatchers);
        var path = Path.Combine(h.TempDir, "Watched.pas");
        var document = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(path)).Value);

        // Another program rewrites the file: the watcher event (debounced on the harness clock) reloads it.
        h.WriteFileExternally("Watched.pas", "unit Watched;\r\n\r\n// changed outside\r\n");
        await h.WaitForAsync(() => document.Buffer.Text.Contains("changed outside", StringComparison.Ordinal),
            "the real watcher reported the change");
        await h.Services.FileChanges.WhenIdle();
        Assert.False(document.IsModified);
        Assert.Empty(document.InfoBars);

        Assert.True(h.Services.Documents.Close(document)); // disposes the watcher before the temp folder goes
    }
}
