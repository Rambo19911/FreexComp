using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IClock"/> whose time moves only through <see cref="Advance"/>; due callbacks run synchronously
/// inside <see cref="Advance"/>, in due-time order (ties: scheduling order), including callbacks scheduled
/// by callbacks that fall due within the same advance.
/// </summary>
public sealed class ManualClock : IClock
{
    private readonly List<Timer> _timers = [];
    private long _sequence;

    public ManualClock(DateTimeOffset? start = null)
    {
        Now = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset Now { get; private set; }

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var timer = new Timer(this, Now + delay, _sequence++, callback);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var end = Now + by;
        while (true)
        {
            var next = _timers.Where(t => t.Due <= end).OrderBy(t => t.Due).ThenBy(t => t.Sequence).FirstOrDefault();
            if (next is null)
            {
                break;
            }

            _timers.Remove(next);
            Now = next.Due;
            next.Callback();
        }

        Now = end;
    }

    private sealed record Timer(ManualClock Owner, DateTimeOffset Due, long Sequence, Action Callback) : IDisposable
    {
        public void Dispose() => Owner._timers.Remove(this);
    }
}
