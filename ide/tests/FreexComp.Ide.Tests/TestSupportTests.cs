using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests;

[Trait("Phase", "1")]
public sealed class TestSupportTests
{
    [Fact(Timeout = 30000)]
    public async Task FakesUsableFromIdeTests()
    {
        IFileSystem fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\p");
        fs.WriteAllBytes(@"C:\p\a.pas", [1]);

        var clock = new ManualClock();
        var fired = false;
        clock.Schedule(TimeSpan.FromSeconds(1), () => fired = true);
        clock.Advance(TimeSpan.FromSeconds(1));

        IClipboardService clipboard = new FakeClipboard();
        await clipboard.SetTextAsync("x");

        IShellService shell = new FakeShellService(fs);
        shell.MoveToRecycleBin(@"C:\p\a.pas");

        IUiDispatcher dispatcher = new ImmediateDispatcher();
        var posted = false;
        dispatcher.Post(() => posted = true);

        IFileWatcherFactory watchers = new ManualFileWatcherFactory();
        using var watcher = watchers.Watch(@"C:\p");

        Assert.True(fired);
        Assert.Equal("x", await clipboard.GetTextAsync());
        Assert.False(fs.FileExists(@"C:\p\a.pas"));
        Assert.True(posted);
        Assert.Equal(@"C:\p", watcher.Directory);
    }
}
