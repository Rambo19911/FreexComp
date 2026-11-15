using FreexComp.Build;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Build;

/// <summary>
/// IDE-P3-045 / IDE-ARCH-086: receives <see cref="BuildEvent"/>s on engine threads and hands them to the UI thread in
/// order, in batches: at most one delivery per 50 ms of <see cref="IClock"/> time, and at once on
/// <see cref="BuildEvent.Finished"/> and on <see cref="Drain"/> (when <c>RunAsync</c> returned). After
/// <see cref="Abandon"/> every later event is discarded (IDE-P3-051).
/// </summary>
public sealed class BuildEventPump : IProgress<BuildEvent>
{
    /// <summary>The batching interval of IDE-P3-045.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<IReadOnlyList<BuildEvent>> _deliver;
    private readonly Lock _gate = new();
    private readonly Lock _flushGate = new();
    private List<BuildEvent> _pending = [];
    private IDisposable? _timer;
    private bool _abandoned;

    /// <param name="deliver">Called on the UI thread with the next events, in arrival order; never with an empty list.</param>
    public BuildEventPump(IClock clock, IUiDispatcher dispatcher, Action<IReadOnlyList<BuildEvent>> deliver)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(deliver);
        _clock = clock;
        _dispatcher = dispatcher;
        _deliver = deliver;
    }

    /// <summary>Number of deliveries so far (IDE-P3-045 budget check).</summary>
    public int Deliveries { get; private set; }

    /// <summary>Any thread.</summary>
    public void Report(BuildEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (_abandoned)
                return;
            _pending.Add(value);
            if (value is BuildEvent.Finished)
            {
                _timer?.Dispose();
                _timer = null;
                _dispatcher.Post(Flush);
            }
            else
            {
                _timer ??= _clock.Schedule(Interval, Flush);
            }
        }
    }

    /// <summary>UI thread: delivers everything still queued now (the engine returned).</summary>
    public void Drain() => Flush();

    /// <summary>Drops the queue and every later event (a build abandoned after the cancel timeout, IDE-P3-051).</summary>
    public void Abandon()
    {
        lock (_gate)
        {
            _abandoned = true;
            _pending.Clear();
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Flush()
    {
        lock (_flushGate)
        {
            List<BuildEvent> batch;
            lock (_gate)
            {
                _timer?.Dispose();
                _timer = null;
                if (_pending.Count == 0 || _abandoned)
                    return;
                batch = _pending;
                _pending = [];
            }

            Deliveries++;
            _deliver(batch);
        }
    }
}
