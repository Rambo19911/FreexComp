namespace FreexComp.Compiler;

/// <summary>
/// CMP-156 parallel loops that never wait for a free thread-pool worker (C-269): the calling thread works through the items
/// itself and up to <c>maxParallelism − 1</c> helpers queued to the pool take items as they start. A helper that starts after
/// every item is taken does nothing, so the caller only ever waits for items already in progress on another thread — with every
/// pool worker busy, the caller simply does all the work. Body exceptions are rethrown as an <see cref="AggregateException"/>
/// once every started item has finished; cancellation stops taking items and throws <see cref="OperationCanceledException"/>.
/// </summary>
internal static class ParallelWork
{
    /// <summary>Synchronous form, for callers that are synchronous by design (binding).</summary>
    public static void For(int count, int maxParallelism, Action<int> body, CancellationToken ct = default)
    {
        var run = Run.Start(count, maxParallelism, body, ct);
        run.Work();
        run.Done.Wait(CancellationToken.None);
        run.ThrowIfFailed();
    }

    /// <summary>Completes synchronously when the caller took every item; otherwise resumes on the thread finishing the last one.</summary>
    public static async Task ForAsync(int count, int maxParallelism, Action<int> body, CancellationToken ct)
    {
        var run = Run.Start(count, maxParallelism, body, ct);
        run.Work();
        await run.Done.ConfigureAwait(false);
        run.ThrowIfFailed();
    }

    private sealed class Run
    {
        private readonly int _count;
        private readonly Action<int> _body;
        private readonly CancellationToken _ct;
        private readonly TaskCompletionSource _done = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _errors = new();
        private int _next = -1;
        private int _unfinished;

        private Run(int count, Action<int> body, CancellationToken ct)
        {
            _count = count;
            _body = body;
            _ct = ct;
            _unfinished = count;
            if (count == 0)
            {
                _done.SetResult();
            }
        }

        public Task Done => _done.Task;

        public static Run Start(int count, int maxParallelism, Action<int> body, CancellationToken ct)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentNullException.ThrowIfNull(body);
            ct.ThrowIfCancellationRequested();
            var run = new Run(count, body, ct);
            for (var helper = 1; helper < Math.Min(Math.Max(maxParallelism, 1), count); helper++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(static r => r.Work(), run, preferLocal: false);
            }

            return run;
        }

        /// <summary>Takes and runs items until none is left; never throws (failures are collected).</summary>
        public void Work()
        {
            int i;
            while ((i = Interlocked.Increment(ref _next)) < _count)
            {
                try
                {
                    if (!_ct.IsCancellationRequested && _errors.IsEmpty)
                    {
                        _body(i);
                    }
                }
#pragma warning disable CA1031 // every failure is rethrown by the caller once all started items have finished
                catch (Exception e)
#pragma warning restore CA1031
                {
                    _errors.Enqueue(e);
                }
                finally
                {
                    if (Interlocked.Decrement(ref _unfinished) == 0)
                    {
                        _done.SetResult();
                    }
                }
            }
        }

        public void ThrowIfFailed()
        {
            if (!_errors.IsEmpty)
            {
                throw new AggregateException(_errors);
            }

            _ct.ThrowIfCancellationRequested();
        }
    }
}
