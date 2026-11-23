using System.Collections.Concurrent;
using FreexComp.Debugger.Session;

namespace FreexComp.Debugger.Tests;

/// <summary>Event delivery on the session's dispatcher thread (architecture §6; debug-session DBG-420, DBG-423, DBG-407).</summary>
public sealed class EventDispatcherTests
{
    /// <summary>Safety net only: every wait normally completes in milliseconds.</summary>
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(60);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Source
    {
        public event EventHandler<StateChangedEventArgs>? StateChanged;
        public event EventHandler<OutputEventArgs>? Output;
        public event EventHandler? Continued;

        public EventHandler<StateChangedEventArgs>? StateChangedHandlers => StateChanged;
        public EventHandler<OutputEventArgs>? OutputHandlers => Output;
        public EventHandler? ContinuedHandlers => Continued;
    }

    private static EventDispatcher Create(Source source) => new(source, () => source.OutputHandlers);

    [Fact]
    [Trait("Req", "DBG-420")]
    public async Task Serial_Order()
    {
        var source = new Source();
        using var dispatcher = Create(source);
        var received = new List<string>();
        var threads = new HashSet<Thread>();
        var inHandler = 0;
        var overlaps = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Output += (sender, e) =>
        {
            if (Interlocked.Increment(ref inHandler) != 1)
                Interlocked.Increment(ref overlaps);
            Assert.Same(source, sender);
            received.Add(e.Text);
            threads.Add(Thread.CurrentThread);
            Interlocked.Decrement(ref inHandler);
        };
        source.Continued += (_, _) => done.SetResult();

        // Four producers at once: every producer's events arrive in its own order, never two handlers at a time.
        const int perProducer = 500;
        using var start = new ManualResetEventSlim();
        var producers = Enumerable.Range(0, 4).Select(p => Task.Run(() =>
        {
            start.Wait(Ct);
            for (var i = 0; i < perProducer; i++)
                dispatcher.Raise(() => source.OutputHandlers, new OutputEventArgs(OutputCategory.Stdout, $"{p}:{i}"));
        }, Ct)).ToArray();
        start.Set();
        await Task.WhenAll(producers).WaitAsync(Safety, Ct);
        dispatcher.Raise(() => source.ContinuedHandlers);
        await done.Task.WaitAsync(Safety, Ct);

        Assert.Equal(4 * perProducer, received.Count);
        for (var p = 0; p < 4; p++)
        {
            var prefix = p + ":";
            Assert.Equal(
                Enumerable.Range(0, perProducer).Select(i => prefix + i),
                received.Where(text => text.StartsWith(prefix, StringComparison.Ordinal)));
        }

        Assert.Equal(0, overlaps);
        var thread = Assert.Single(threads);
        Assert.Equal("FreexComp.Debugger.Events", thread.Name);
        Assert.True(thread.IsBackground);
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread.ManagedThreadId);
    }

    [Fact]
    [Trait("Req", "DBG-422")]
    public async Task Post_RunsActionsInOrder_BetweenEvents()
    {
        var source = new Source();
        using var dispatcher = Create(source);
        var log = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Continued += (_, _) => log.Add("event");

        dispatcher.Post(() => log.Add("first"));
        dispatcher.Raise(() => source.ContinuedHandlers);
        dispatcher.Post(() => log.Add("last"));
        dispatcher.Post(done.SetResult);
        await done.Task.WaitAsync(Safety, Ct);

        Assert.Equal(["first", "event", "last"], log);
    }

    [Fact]
    [Trait("Req", "DBG-423")]
    public async Task HandlerThrows_Continues_ReportsOutput()
    {
        var source = new Source();
        using var dispatcher = Create(source);
        var log = new ConcurrentQueue<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StateChanged += (_, _) => throw new InvalidOperationException("boom");
        source.StateChanged += (_, e) => log.Enqueue("second handler: " + e.NewState);
        source.Output += (_, e) => log.Enqueue($"output {e.Category}: {e.Text}");
        source.Continued += (_, _) => done.SetResult();

        dispatcher.Raise(() => source.StateChangedHandlers, new StateChangedEventArgs(DebugState.Idle, DebugState.Starting));
        dispatcher.Raise(() => source.StateChangedHandlers, new StateChangedEventArgs(DebugState.Starting, DebugState.Running));
        dispatcher.Raise(() => source.ContinuedHandlers);
        await done.Task.WaitAsync(Safety, Ct);

        // Each failure is reported once, the remaining handlers of the same event still run, and dispatching continues.
        Assert.Equal(
        [
            "output Debugger: Event handler failed: InvalidOperationException: boom",
            "second handler: Starting",
            "output Debugger: Event handler failed: InvalidOperationException: boom",
            "second handler: Running",
        ], log);
    }

    [Fact]
    [Trait("Req", "DBG-423")]
    public async Task OutputHandlerThrows_NotReportedRecursively()
    {
        var source = new Source();
        using var dispatcher = Create(source);
        var calls = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Output += (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("output handler");
        };
        source.Continued += (_, _) => done.SetResult();

        dispatcher.Raise(() => source.OutputHandlers, new OutputEventArgs(OutputCategory.Stdout, "x"));
        dispatcher.Raise(() => source.ContinuedHandlers);
        await done.Task.WaitAsync(Safety, Ct);

        // Once for the event itself and once for the failure report; the failing report is not reported again.
        Assert.Equal(2, calls);
    }

    [Fact]
    [Trait("Req", "DBG-407")]
    public async Task NoEventsAfterDispose()
    {
        var source = new Source();
        var dispatcher = Create(source);
        var log = new ConcurrentQueue<string>();
        var entered = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        source.Output += (_, e) =>
        {
            log.Enqueue("handler start " + e.Text);
            entered.TrySetResult(Thread.CurrentThread);
            release.Wait(Ct);
            log.Enqueue("handler end " + e.Text);
        };

        for (var i = 0; i < 10; i++)
            dispatcher.Raise(() => source.OutputHandlers, new OutputEventArgs(OutputCategory.Stdout, i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var thread = await entered.Task.WaitAsync(Safety, Ct);

        // Dispose is called while a handler is still running: it returns only after every started handler has
        // finished. (How many of the queued events were delivered before Dispose took effect depends on scheduling;
        // that queued events are dropped is shown deterministically by Dispose_InsideHandler below.)
        var disposeCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispose = Task.Run(() =>
        {
            disposeCalled.SetResult();
            dispatcher.Dispose();
            log.Enqueue("dispose returned");
        }, Ct);
        await disposeCalled.Task.WaitAsync(Safety, Ct);
        release.Set();
        await dispose.WaitAsync(Safety, Ct);

        var entries = log.ToArray();
        Assert.Equal("dispose returned", entries[^1]);
        Assert.Equal("handler start 0", entries[0]);
        Assert.Equal(
            entries.Count(e => e.StartsWith("handler start", StringComparison.Ordinal)),
            entries.Count(e => e.StartsWith("handler end", StringComparison.Ordinal)));
        Assert.False(thread.IsAlive);

        // Nothing runs after Dispose returned.
        dispatcher.Raise(() => source.OutputHandlers, new OutputEventArgs(OutputCategory.Stdout, "late"));
        dispatcher.Post(() => log.Enqueue("late action"));
        dispatcher.Dispose();   // idempotent
        Assert.Equal(entries, log.ToArray());
    }

    [Fact]
    public async Task Dispose_InsideHandler_DoesNotDeadlock_AndStopsDelivery()
    {
        var source = new Source();
        var dispatcher = Create(source);
        var calls = 0;
        var disposed = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Continued += (_, _) =>
        {
            calls++;
            dispatcher.Dispose();
            disposed.SetResult(Thread.CurrentThread);
        };

        dispatcher.Raise(() => source.ContinuedHandlers);
        dispatcher.Raise(() => source.ContinuedHandlers);
        var thread = await disposed.Task.WaitAsync(Safety, Ct);
        dispatcher.Dispose();   // from another thread: waits for the dispatcher thread to end

        Assert.False(thread.IsAlive);
        Assert.Equal(1, calls);
    }
}
