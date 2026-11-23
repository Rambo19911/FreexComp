using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests;

/// <summary>IDE-P3-094: the scripted launcher that every run test uses instead of real processes.</summary>
[Trait("Phase", "3")]
public sealed class FakeProcessLauncherTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-094")]
    public Task SimulatesStartOutputExitAndKill() => TestUiThread.RunAsync(() => SimulatesStartOutputExitAndKillOnUiThread()); // I-209

    private async Task SimulatesStartOutputExitAndKillOnUiThread()
    {
        var launcher = new FakeProcessLauncher();
        var lines = new List<ProcessOutputLine>();
        var request = new ProcessStartRequest(@"C:\p\Hello.exe", "a b", @"C:\p");
        using var first = launcher.Start(request, lines.Add);
        Assert.Same(request, launcher.Last.Request);

        launcher.Last.WriteStdOut("Hello");
        launcher.Last.WriteStdErr("oops");
        Assert.Equal([new ProcessOutputLine("Hello", false), new ProcessOutputLine("oops", true)], lines);
        Assert.False(first.Exited.IsCompleted);
        launcher.Last.Exit(0);
        Assert.Equal(0, await first.Exited);

        using var second = launcher.Start(request, lines.Add);
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        Assert.True(await second.KillTreeAsync());
        Assert.Equal(1, await second.Exited);
        Assert.Equal(1, launcher.Last.KillCount);

        using var third = launcher.Start(request with { Capture = false }, lines.Add);
        launcher.Last.KillSucceeds = false;
        Assert.False(await third.KillTreeAsync());
        Assert.Throws<InvalidOperationException>(() => launcher.Last.WriteStdOut("not captured"));

        launcher.NextStartFails = 2;
        Assert.Throws<System.ComponentModel.Win32Exception>(() => launcher.Start(request, lines.Add));
        Assert.Equal(3, launcher.Started.Count);
    }
}
