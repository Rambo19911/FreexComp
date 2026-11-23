namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>A clock that only moves when a test says so; timers fire synchronously inside <see cref="Advance"/>.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<(TimeSpan Due, TaskCompletionSource Created)> _watchers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    /// <summary>Number of timers that are created, not disposed and not yet fired.</summary>
    public int ActiveTimers
    {
        get
        {
            lock (_gate)
                return _timers.Count;
        }
    }

    /// <summary>
    /// Completes when a timer with exactly this due time is created after this call. Lets a test advance the clock
    /// only once the code under test is really waiting (no polling, no sleeping).
    /// </summary>
    public Task WhenTimerCreated(TimeSpan dueTime)
    {
        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
            _watchers.Add((dueTime, created));
        return created.Task;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _timers.Where(t => t.Due <= _now).OrderBy(t => t.Due).ToList();
            _timers.RemoveAll(due.Contains);
        }

        foreach (var timer in due)
            timer.Fire();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (period != Timeout.InfiniteTimeSpan)
            throw new NotSupportedException("periodic timers are not used by the debugger");
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                timer.Due = _now + dueTime;
                _timers.Add(timer);
            }

            foreach (var watcher in _watchers.Where(w => w.Due == dueTime).ToList())
            {
                _watchers.Remove(watcher);
                watcher.Created.TrySetResult();
            }
        }

        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();

        public void Dispose()
        {
            lock (owner._gate)
                owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
