using System.Collections.Concurrent;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>
/// A single-thread synchronization context like the UI thread's: posted callbacks wait in a queue until the owning
/// thread runs them with <see cref="RunUntil"/>. Background work started under it (classifier, folding, code tokens)
/// applies its results on the test thread, so a test can drain that work before it ends (I-179).
/// </summary>
internal sealed class QueueSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Send would block the owning thread.");

    /// <summary>
    /// Runs posted callbacks on the calling thread, waiting for each, until <paramref name="done"/> holds. I-205: work
    /// that never posts again fails, naming <paramref name="step"/>, when <paramref name="deadline"/> runs out.
    /// </summary>
    public void RunUntil(Func<bool> done, TestDeadline deadline, string step)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentNullException.ThrowIfNull(deadline);
        while (!done())
        {
            if (!_queue.TryTake(out var item, deadline.Remaining))
                throw deadline.Expired(step);
            item.Callback(item.State);
        }
    }

    /// <summary>Runs <paramref name="action"/> with this context installed on the calling thread.</summary>
    public void Run(Action action)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            action();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}
