using System.Collections.Concurrent;

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
    /// Runs posted callbacks on the calling thread, waiting for each, until <paramref name="done"/> holds; work that
    /// never posts again leaves the test to its <c>Timeout</c>.
    /// </summary>
    public void RunUntil(Func<bool> done)
    {
        while (!done())
        {
            var (callback, state) = _queue.Take();
            callback(state);
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
