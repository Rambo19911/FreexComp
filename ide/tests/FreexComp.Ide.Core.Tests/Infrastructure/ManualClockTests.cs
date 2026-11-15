using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>The fake clock under the threading Core code uses in tests (I-173, I-197).</summary>
[Trait("Phase", "1")]
public sealed class ManualClockTests
{
    /// <summary>
    /// Core code schedules from the thread pool (e.g. <c>SharingRetry</c> after an awaited save) while the test thread
    /// advances: no timer is lost, none runs twice, a disposed one never runs, and <see cref="ManualClock.Advance"/>
    /// never sees a half-updated timer set (the I-173 flake: a NullReferenceException inside <c>Advance</c>).
    /// <para>
    /// I-197: the scheduler is a dedicated thread (not a thread-pool task the spinning test thread could starve under
    /// full-solution load) and the advancing loop is bounded: every advance is O(log n), and a disposed timer leaves
    /// the queue at once, so the queue never holds more than the not-yet-fired timers.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30000)]
    public Task ScheduleFromAnotherThreadWhileAdvancing()
    {
        const int Count = 20000;
        var clock = new ManualClock();
        int fired = 0, cancelledFired = 0, scheduled = 0;
        var scheduler = new Thread(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                clock.Schedule(TimeSpan.FromMilliseconds(1), () => Interlocked.Increment(ref fired));
                clock.Schedule(TimeSpan.FromDays(3650), () => Interlocked.Increment(ref cancelledFired)).Dispose();
                Volatile.Write(ref scheduled, i + 1);
            }
        })
        {
            IsBackground = true,
        };
        scheduler.Start();

        while (Volatile.Read(ref scheduled) < Count)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(clock.PendingCount <= Count, "disposed timers must not pile up in the queue");
        }

        scheduler.Join();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(Count, fired);
        Assert.Equal(0, cancelledFired);
        Assert.Equal(0, clock.PendingCount);
        return Task.CompletedTask;
    }

    /// <summary>
    /// I-197: when scheduling outruns advancing (the full-solution case), one advance drains a large backlog; taking
    /// the next due timer must not cost a sort of the whole queue (the old list made this O(n² log n)).
    /// </summary>
    [Fact(Timeout = 10000)]
    public Task AdvanceDrainsALargeBacklogQuickly()
    {
        const int Count = 50000;
        var clock = new ManualClock();
        int fired = 0;
        for (int i = 0; i < Count; i++)
        {
            clock.Schedule(TimeSpan.FromMilliseconds(1 + (i % 7)), () => fired++);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(Count, fired);
        Assert.Equal(0, clock.PendingCount);
        return Task.CompletedTask;
    }

    /// <summary>I-197: a disposed timer leaves the queue immediately; fired timers leave it too.</summary>
    [Fact]
    public void DisposedTimersLeaveTheQueue()
    {
        var clock = new ManualClock();
        var order = new List<int>();
        var far = clock.Schedule(TimeSpan.FromDays(3650), () => order.Add(0));
        clock.Schedule(TimeSpan.FromSeconds(2), () => order.Add(2));
        clock.Schedule(TimeSpan.FromSeconds(1), () => order.Add(1));
        clock.Schedule(TimeSpan.FromSeconds(1), () => order.Add(11));
        Assert.Equal(4, clock.PendingCount);

        far.Dispose();
        Assert.Equal(3, clock.PendingCount);
        far.Dispose();
        Assert.Equal(3, clock.PendingCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([1, 11], order);
        Assert.Equal(1, clock.PendingCount);

        clock.Advance(TimeSpan.FromDays(4000));
        Assert.Equal([1, 11, 2], order);
        Assert.Equal(0, clock.PendingCount);
    }
}
