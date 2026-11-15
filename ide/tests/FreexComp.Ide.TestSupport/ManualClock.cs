using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IClock"/> whose time moves only through <see cref="Advance"/>; due callbacks run synchronously
/// inside <see cref="Advance"/>, in due-time order (ties: scheduling order), including callbacks scheduled
/// by callbacks that fall due within the same advance.
/// <para>
/// I-173: <see cref="Schedule"/>, <see cref="Now"/> and timer disposal may be called from any thread while the test
/// thread advances (Core code awaits off the UI thread in tests, e.g. <c>SharingRetry</c> on the thread pool); the
/// timer set is guarded, callbacks run outside the lock.
/// </para>
/// <para>
/// I-197: pending timers are kept in a set ordered by (due, sequence), so scheduling, disposal and taking the next
/// due timer are O(log n), and a disposed timer leaves the set at once (<see cref="PendingCount"/>).
/// </para>
/// </summary>
public sealed class ManualClock : IClock
{
    private readonly Lock _gate = new();
    private readonly SortedSet<Timer> _timers = new(TimerOrder.Instance);
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

    /// <summary>Timers scheduled and neither fired nor disposed.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
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
            Timer next;
            lock (_gate)
            {
                if (_timers.Count == 0 || _timers.Min!.Due > end)
                {
                    _now = end;
                    return;
                }

                next = _timers.Min!;
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

    private sealed class Timer(ManualClock owner, DateTimeOffset due, long sequence, Action callback) : IDisposable
    {
        public DateTimeOffset Due { get; } = due;

        public long Sequence { get; } = sequence;

        public Action Callback { get; } = callback;

        public void Dispose() => owner.Remove(this);
    }

    private sealed class TimerOrder : IComparer<Timer>
    {
        public static readonly TimerOrder Instance = new();

        public int Compare(Timer? x, Timer? y)
        {
            int byDue = x!.Due.CompareTo(y!.Due);
            return byDue != 0 ? byDue : x.Sequence.CompareTo(y.Sequence);
        }
    }
}
