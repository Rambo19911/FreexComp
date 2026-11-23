using System.Collections.Concurrent;

namespace FreexComp.Debugger.Session;

/// <summary>
/// Raises all public events of one session serially on one dedicated background thread (architecture §6, debug-session
/// DBG-420). A handler that throws is reported once through the Output event and dispatching continues (DBG-423).
/// After <see cref="Dispose"/> returns no handler runs any more (DBG-407).
/// </summary>
internal sealed class EventDispatcher : IDisposable
{
    public const string ThreadName = "FreexComp.Debugger.Events";

    private readonly object _sender;
    private readonly Func<EventHandler<OutputEventArgs>?> _outputHandlers;
    private readonly BlockingCollection<Action> _queue = new(new ConcurrentQueue<Action>());
    private readonly Lock _gate = new();   // held while a queued action runs
    private readonly Thread _thread;
    private readonly List<TaskCompletionSource> _flushes = [];   // guarded by _flushes
    private bool _flushesClosed;
    private bool _disposed;

    /// <param name="sender">The <c>sender</c> argument of every event (the session).</param>
    /// <param name="outputHandlers">Current handlers of the Output event; receives the handler-failure reports.</param>
    public EventDispatcher(object sender, Func<EventHandler<OutputEventArgs>?> outputHandlers)
    {
        _sender = sender;
        _outputHandlers = outputHandlers;
        _thread = new Thread(Run) { IsBackground = true, Name = ThreadName };
        _thread.Start();
    }

    /// <summary>Queues an event. The handler list is read when the event is delivered.</summary>
    public void Raise<T>(Func<EventHandler<T>?> handlers, T args) =>
        Post(() =>
        {
            if (handlers() is not { } list)
                return;
            foreach (var handler in list.GetInvocationList().Cast<EventHandler<T>>())
                Invoke(() => handler(_sender, args), report: true);
        });

    /// <summary>Queues an event without arguments.</summary>
    public void Raise(Func<EventHandler?> handlers) =>
        Post(() =>
        {
            if (handlers() is not { } list)
                return;
            foreach (var handler in list.GetInvocationList().Cast<EventHandler>())
                Invoke(() => handler(_sender, EventArgs.Empty), report: true);
        });

    /// <summary>Queues an action to run on the dispatcher thread, in order with the events. Ignored after disposal.</summary>
    public void Post(Action action)
    {
        try
        {
            _queue.Add(action);
        }
        catch (InvalidOperationException)
        {
            // Disposed: nothing is delivered any more.
        }
    }

    /// <summary>
    /// Completes when everything queued before this call has been delivered, or when the dispatcher is disposed
    /// (whichever comes first): lets the session order a task completion after an event.
    /// </summary>
    public Task FlushAsync()
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_flushes)
        {
            if (_flushesClosed)
                return Task.CompletedTask;
            _flushes.Add(delivered);
        }

        Post(() =>
        {
            lock (_flushes)
                _flushes.Remove(delivered);
            delivered.TrySetResult();
        });
        return delivered.Task;
    }

    /// <summary>
    /// Stops delivery. Waits for a handler that is running (unless called from that handler); queued events are dropped.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        _queue.CompleteAdding();
        List<TaskCompletionSource> pending;
        lock (_flushes)
        {
            _flushesClosed = true;
            pending = [.. _flushes];
            _flushes.Clear();
        }

        foreach (var flush in pending)
            flush.TrySetResult();
        if (Thread.CurrentThread != _thread)
            _thread.Join();
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                Invoke(action, report: true);
            }
        }
    }

    private void Invoke(Action action, bool report)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            if (report)
                ReportFailure(e);
        }
    }

    /// <summary>One Output event (category Debugger) per failed handler; failures of the report itself are dropped.</summary>
    private void ReportFailure(Exception exception)
    {
        if (_outputHandlers() is not { } list)
            return;
        var args = new OutputEventArgs(OutputCategory.Debugger, Messages.HandlerFailed + exception.GetType().Name + ": " + exception.Message);
        foreach (var handler in list.GetInvocationList().Cast<EventHandler<OutputEventArgs>>())
            Invoke(() => handler(_sender, args), report: false);
    }
}
