using FreexComp.Debugger;
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

    /// <summary>
    /// I-258: a session over the scripted adapter has no wall-clock limit shorter than the test's own safety net. The
    /// product's limits (10 s per request, 30 s for the launch) are for a person at a debugger; on a machine that runs
    /// every test project at once the adapter process alone can take longer to start, and the launch would fail for a
    /// reason no test is about. What the IDE asked for is recorded unchanged.
    /// </summary>
    [Fact(Timeout = 30000)]
    public void ScriptedSessionsAreBoundedByTheTestsSafetyNetOnly()
    {
        using var sessions = new FakeAdapterSessions();
        var asked = new DebugSessionOptions { AdapterPath = @"C:	cdapter.exe" };
        var net = TimeSpan.FromMilliseconds(TestDeadline.DebugTestTimeout);

        sessions.UseScenario("""{ "steps": [] }""");
        sessions.Create(asked);
        Assert.Same(asked, Assert.Single(sessions.Requested));
        Assert.Equal((TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), (asked.RequestTimeout, asked.LaunchTimeout));
        Assert.Equal((FakeAdapterSessions.AdapterExe, net, net), (sessions.Created[0].AdapterPath, sessions.Created[0].RequestTimeout, sessions.Created[0].LaunchTimeout));

        // Without the scripted adapter the options stay the product's own.
        sessions.UseNoAdapter();
        sessions.Create(asked);
        Assert.Equal((asked.AdapterPath, asked.RequestTimeout, asked.LaunchTimeout),
            (sessions.Created[1].AdapterPath, sessions.Created[1].RequestTimeout, sessions.Created[1].LaunchTimeout));
    }
}
