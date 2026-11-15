using System.Globalization;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

[Trait("Phase", "3")]
public sealed partial class BuildServiceTests
{
    private const string ProjectPath = @"C:\p\Hello.pproj";

    /// <summary>IDE-P3-045: 10 000 diagnostics from an engine thread reach the UI in order, at most one delivery per 50 ms.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-045")]
    [Trait("Req", "IDE-ARCH-086")]
    public async Task LargeDiagnosticBurstIsBatched()
    {
        var clock = new ManualClock();
        var deliveries = new List<(DateTimeOffset At, int Count)>();
        var received = new List<BuildEvent>();
        var pump = new BuildEventPump(clock, new ImmediateDispatcher(), batch =>
        {
            deliveries.Add((clock.Now, batch.Count));
            received.AddRange(batch);
        });

        // The scripted engine reports Started + 10 000 diagnostics + Finished on a background (engine) thread, in bursts.
        var diagnostics = Enumerable.Range(0, 10_000)
            .Select(i => FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'x" + i.ToString(CultureInfo.InvariantCulture) + "'",
                @"C:\p\Unit1.pas", line: i + 1, column: 1))
            .Select(d => (BuildEvent)new BuildEvent.Diagnostic(DateTime.UtcNow, ProjectPath, d))
            .ToList();
        var start = clock.Now;
        for (var burst = 0; burst < 100; burst++)
        {
            var part = diagnostics.GetRange(burst * 100, 100);
            await Task.Run(() => part.ForEach(pump.Report), TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromMilliseconds(5));
        }

        Assert.All(deliveries.Zip(deliveries.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= BuildEventPump.Interval,
            $"two deliveries {pair.Second.At - pair.First.At} apart"));
        Assert.InRange(deliveries.Count, 1, (int)((clock.Now - start) / BuildEventPump.Interval));

        // Finished flushes at once, without waiting for the next interval.
        var before = deliveries.Count;
        pump.Report(new BuildEvent.Finished(DateTime.UtcNow, ProjectPath, false, false, 10_000, 0, 0, TimeSpan.FromSeconds(1)));
        Assert.Equal(before + 1, deliveries.Count);
        Assert.Equal([.. diagnostics, received[^1]], received);
        Assert.Equal(pump.Deliveries, deliveries.Count);

        // Drain delivers what is queued; an abandoned pump discards everything after.
        pump.Report(diagnostics[0]);
        pump.Drain();
        Assert.Equal(before + 2, deliveries.Count);
        pump.Abandon();
        pump.Report(diagnostics[1]);
        clock.Advance(BuildEventPump.Interval);
        pump.Drain();
        Assert.Equal(before + 2, deliveries.Count);
    }

    /// <summary>IDE-P3-031: the fake engine is scripted with the contract's own event records.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-031")]
    public async Task FakeEngineReportsScriptedEventsAndResult()
    {
        var engine = new FakeBuildEngine();
        var error = FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", @"C:\p\Unit1.pas", 12, 5, 3);
        engine.Enqueue(FakeBuildEngine.Failure(ProjectPath, error));
        var events = new List<BuildEvent>();
        var result = await engine.RunAsync(new BuildRequest { ProjectPath = ProjectPath }, new SyncProgress(events.Add), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(1, result.Errors);
        Assert.Collection(events,
            e => Assert.IsType<BuildEvent.Started>(e),
            e => Assert.Same(error, Assert.IsType<BuildEvent.Diagnostic>(e).Value),
            e => Assert.False(Assert.IsType<BuildEvent.Finished>(e).Success));
        Assert.Equal(ProjectPath, Assert.Single(engine.Requests).ProjectPath);

        // Nothing queued: a successful build.
        events.Clear();
        var ok = await engine.RunAsync(new BuildRequest { ProjectPath = ProjectPath }, new SyncProgress(events.Add), TestContext.Current.CancellationToken);
        Assert.True(ok.Success);
        Assert.Equal(@"C:\p\Hello.exe", ok.OutputPath);
        Assert.Equal(2, events.Count);
    }

    private sealed class SyncProgress(Action<BuildEvent> report) : IProgress<BuildEvent>
    {
        public void Report(BuildEvent value) => report(value);
    }
}
