using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary><see cref="IUiDispatcher"/> that queues posted work until the test calls <see cref="RunAll"/>.</summary>
public sealed class QueuedDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();

    public int Pending => _queue.Count;

    public bool CheckAccess() => true;

    public void Post(Action action, UiPriority priority = UiPriority.Normal) => _queue.Enqueue(action);

    /// <summary>Runs queued work, including work queued while running, until the queue is empty.</summary>
    public void RunAll()
    {
        while (_queue.TryDequeue(out var action))
        {
            action();
        }
    }
}
