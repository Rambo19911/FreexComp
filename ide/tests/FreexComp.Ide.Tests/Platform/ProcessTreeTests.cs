using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Platform.Windows;
using FreexComp.Ide.Tests.Fixtures;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>The Windows process launcher with its Job Object (I-104, IDE-P3-073/083/094), against real processes.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ProcessTreeTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private static ProcessStartRequest Request(string mode)
    {
        var (file, arguments) = ProcessHelper.Command(mode);
        return new ProcessStartRequest(file, arguments, Path.GetTempPath());
    }

    private static bool IsGone(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true; // no such process
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-083")]
    [Trait("Req", "IDE-P3-090")]
    [Trait("Req", "IDE-P3-094")]
    public async Task KillTreeTerminatesGrandchildren()
    {
        var childLine = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new WindowsProcessLauncher(new ManualClock());
        using var process = launcher.Start(Request(ProcessHelper.SpawnChild), line =>
        {
            if (line.Text.StartsWith("child ", StringComparison.Ordinal))
                childLine.TrySetResult(line.Text);
        });
        var grandchild = int.Parse((await childLine.Task.WaitAsync(Limit, TestContext.Current.CancellationToken))["child ".Length..], CultureInfo.InvariantCulture);
        Assert.False(IsGone(process.ProcessId));
        using var grandchildProcess = Process.GetProcessById(grandchild);
        Assert.False(grandchildProcess.HasExited);

        var watch = Stopwatch.StartNew();
        Assert.True(await process.KillTreeAsync());
        await grandchildProcess.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"tree gone after {watch.Elapsed}");
        Assert.True(IsGone(process.ProcessId));
        Assert.Equal(1, await process.Exited.WaitAsync(Limit, TestContext.Current.CancellationToken)); // TerminateJobObject exit code
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-073")]
    [Trait("Req", "IDE-P3-082")]
    [Trait("Req", "IDE-P3-084")]
    public async Task CapturesStdoutAndStderr()
    {
        var lines = new ConcurrentQueue<ProcessOutputLine>();
        var launcher = new WindowsProcessLauncher(new ManualClock());
        using var process = launcher.Start(Request(ProcessHelper.WriteBoth), lines.Enqueue);

        Assert.Equal(3, await process.Exited.WaitAsync(Limit, TestContext.Current.CancellationToken));

        // Each pipe keeps its own order; the interleaving of the two is not asserted (IDE-P3-074).
        Assert.Equal(["out 1", "grün", "cr", "Name: "], lines.Where(l => !l.IsError).Select(l => l.Text));
        Assert.Equal(["err 1", "stdin 0"], lines.Where(l => l.IsError).Select(l => l.Text));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-081")]
    public void MissingExecutableThrows()
    {
        var launcher = new WindowsProcessLauncher(new ManualClock());
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            launcher.Start(new ProcessStartRequest(Path.Combine(Path.GetTempPath(), "no-such-program-" + Guid.NewGuid().ToString("N") + ".exe"), "", Path.GetTempPath()), _ => { }));
    }
}
