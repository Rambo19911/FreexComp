using System.Diagnostics;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The process-leak check (debugger testing DBG-334): the registry finds, kills and reports processes that outlive
/// their test, and the helpers that start processes feed the registry of the assembly-wide fixture.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Req", "DBG-334")]
public sealed class ProcessLeakFixtureTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Process StartWaiting(params string[] arguments)
    {
        var info = new ProcessStartInfo(FakeAdapterRunner.ExePath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    [Fact]
    public async Task Registry_LeakedProcess_KilledAndReported()
    {
        var registry = new ProcessRegistry();
        using var process = StartWaiting("--wait");   // never exits by itself
        try
        {
            registry.Track(process.Id, "left behind on purpose");
            Assert.True(registry.IsTracked(process.Id));

            var leaked = registry.KillLeaked();

            Assert.Equal([$"left behind on purpose (FakeAdapter), process id {process.Id}"], leaked);
            await process.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
            Assert.True(process.HasExited);
            Assert.Empty(registry.KillLeaked());   // reported once
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void Registry_TreeMemberAlreadyEnding_LeakStillReported_ProcessGoneOnReturn()
    {
        // G-083: killing a tree fails with "not all processes could be terminated" (access is denied) when a member
        // of the tree is already on its way out - the console host of a child that was just killed ends by itself.
        // That is no failure of the clean-up: the leak is reported as usual and the process is gone on return.
        var registry = new ProcessRegistry
        {
            KillTree = process =>
            {
                process.Kill(entireProcessTree: true);
                throw new AggregateException(
                    "Not all processes in process tree could be terminated.", new System.ComponentModel.Win32Exception(5));
            },
        };
        using var process = StartWaiting("--wait");   // never exits by itself
        try
        {
            registry.Track(process.Id, "left behind on purpose");

            var leaked = registry.KillLeaked();

            Assert.Equal([$"left behind on purpose (FakeAdapter), process id {process.Id}"], leaked);
            Assert.True(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task Registry_EndedProcess_NotReported()
    {
        var registry = new ProcessRegistry();
        using var process = StartWaiting("--stderr-lines", "1");
        registry.Track(process.Id, "ends by itself");
        await process.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);

        Assert.Empty(registry.KillLeaked());
        registry.Track(process.Id, "already gone");     // tracking an ended process records nothing
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task Registry_TrackTree_FindsChildProcesses()
    {
        // "--spawn-child" starts a child that never exits and reports its process id.
        var registry = new ProcessRegistry();
        using var parent = StartWaiting("--spawn-child");
        try
        {
            var line = await parent.StandardError.ReadLineAsync(Ct).AsTask().WaitAsync(Safety, Ct);
            Assert.NotNull(line);
            Assert.StartsWith("CHILD-PID ", line, StringComparison.Ordinal);
            var child = int.Parse(line["CHILD-PID ".Length..], System.Globalization.CultureInfo.InvariantCulture);

            registry.TrackTree(parent.Id, "parent");

            Assert.True(registry.IsTracked(parent.Id));
            Assert.True(registry.IsTracked(child));
            // Both are found alive, killed and reported (and with them whatever else the tree holds, such as the
            // console host of the child).
            var leaked = registry.KillLeaked();
            Assert.Contains(leaked, entry => entry.EndsWith($"process id {parent.Id}", StringComparison.Ordinal));
            Assert.Contains(leaked, entry => entry.EndsWith($"process id {child}", StringComparison.Ordinal));
            await parent.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
            using var childProcess = TryGet(child);
            if (childProcess is not null)
                await childProcess.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
        }
        finally
        {
            if (!parent.HasExited)
                parent.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task SessionHarness_AdapterIsOnTheFixtureList()
    {
        // The assembly fixture's Dispose fails the run for any process on its list that is still alive: every
        // session test puts its adapter there.
        await using var harness = SessionHarness.Create("{}");
        await harness.LaunchAsync();

        var adapter = Assert.NotNull(harness.AdapterProcessId);
        Assert.True(ProcessLeakFixture.Registry.IsTracked(adapter));
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
    }

    private static Process? TryGet(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
