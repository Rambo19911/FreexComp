using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IClock"/> whose time moves only through <see cref="Advance"/>; due callbacks run synchronously
/// inside <see cref="Advance"/>, in due-time order (ties: scheduling order), including callbacks scheduled
/// by callbacks that fall due within the same advance.
/// <para>
/// I-173: <see cref="Schedule"/>, <see cref="Now"/> and timer disposal may be called from any thread while the test
/// thread advances (Core code awaits off the UI thread in tests, e.g. <c>SharingRetry</c> on the thread pool); the
/// timer list is guarded, callbacks run outside the lock.
/// </para>
/// </summary>
public sealed class ManualClock : IClock
{
    private readonly Lock _gate = new();
    private readonly List<Timer> _timers = [];
    private long _sequence;
    private DateTimeOffset _now;

    public ManualClock(DateTimeOffset? start = null)
    {
        _now = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset Now
    {
        get
        {
            lock (_gate)
            {
                return _now;
            }
        }
    }

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        lock (_gate)
        {
            var timer = new Timer(this, _now + delay, _sequence++, callback);
            _timers.Add(timer);
            return timer;
        }
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset end;
        lock (_gate)
        {
            end = _now + by;
        }

        while (true)
        {
            Timer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Due <= end).OrderBy(t => t.Due).ThenBy(t => t.Sequence).FirstOrDefault();
                if (next is null)
                {
                    _now = end;
                    return;
                }

                _timers.Remove(next);
                _now = next.Due;
            }

            next.Callback();
        }
    }

    private void Remove(Timer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed record Timer(ManualClock Owner, DateTimeOffset Due, long Sequence, Action Callback) : IDisposable
    {
        public void Dispose() => Owner.Remove(this);
    }
}
