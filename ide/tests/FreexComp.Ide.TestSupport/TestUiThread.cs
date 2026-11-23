namespace FreexComp.Ide.TestSupport;

/// <summary>
/// I-208: a UI thread for Core tests. The test body runs on a dedicated thread with a single-thread synchronization
/// context, like the IDE's UI thread: awaits resume there, and an <see cref="ImmediateDispatcher"/> created on it runs work
/// posted from other threads (results of the code-model worker, applied through <c>IUiDispatcher.Post</c>) there too —
/// between the test's own steps, never at the same time. Without it such work ran on the posting pool thread, in
/// parallel with the test thread (a structure-view rebuild while a clock callback enumerated its rows).
/// </summary>
public static class TestUiThread
{
    /// <summary>The context of the test UI thread the caller runs on, or null on any other thread.</summary>
    public static SynchronizationContext? Current => SynchronizationContext.Current as UiContext;

    /// <summary>Runs <paramref name="body"/> on a new test UI thread; completes as the body does (its exception, if any).</summary>
    public static Task RunAsync(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var context = new UiContext();
            SynchronizationContext.SetSynchronizationContext(context);
            Task task;
            try
            {
                task = body();
            }
            catch (Exception e)
            {
                task = Task.FromException(e);
            }

            task.ContinueWith(_ => context.Complete(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            context.RunUntilComplete();
            if (task.IsFaulted)
                done.SetException(task.Exception!.InnerExceptions);
            else if (task.IsCanceled)
                done.SetCanceled();
            else
                done.SetResult();
        })
        {
            IsBackground = true,
            Name = "Test UI thread",
        };
        thread.Start();
        return done.Task;
    }

    /// <summary>Posted callbacks queue until the owning thread runs them; after the test they go to the thread pool.</summary>
    private sealed class UiContext : SynchronizationContext
    {
        private readonly object _gate = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private bool _completed;

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_gate)
            {
                if (!_completed)
                {
                    _queue.Enqueue((d, state));
                    Monitor.PulseAll(_gate);
                    return;
                }
            }

            ThreadPool.QueueUserWorkItem(_ => d(state)); // a late result after the test ended
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("Send would block the owning thread.");

        public override SynchronizationContext CreateCopy() => this;

        public void Complete()
        {
            lock (_gate)
            {
                _completed = true;
                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>Runs posted callbacks on the calling thread until <see cref="Complete"/> and the queue is empty.</summary>
        public void RunUntilComplete()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_gate)
                {
                    while (_queue.Count == 0 && !_completed)
                        Monitor.Wait(_gate);
                    if (_queue.Count == 0)
                        return;
                    item = _queue.Dequeue();
                }

                item.Callback(item.State);
            }
        }
    }
}
