namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// IDE-ARCH-087: the single background worker that runs every language-service request. Requests run one at a time in
/// arrival order; a new request of a kind supersedes a queued (not yet started) one of the same kind, which completes as
/// canceled. Results are immutable and may be read on any thread.
/// </summary>
public sealed class CodeModelWorker
{
    private readonly object _gate = new();
    private readonly LinkedList<Item> _queue = new();
    private bool _running;

    /// <summary>Queues <paramref name="work"/>; the task completes with its result, or canceled when superseded or canceled.</summary>
    public Task<T> RunAsync<T>(string kind, Func<CancellationToken, T> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(work);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new Item(kind, ct, token =>
        {
            try
            {
                completion.TrySetResult(work(token));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(token.IsCancellationRequested ? token : ct);
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        }, () => completion.TrySetCanceled());
        var start = false;
        lock (_gate)
        {
            for (var node = _queue.First; node is not null;)
            {
                var next = node.Next;
                if (node.Value.Kind == kind)
                {
                    _queue.Remove(node);
                    node.Value.Cancel();
                }

                node = next;
            }

            _queue.AddLast(item);
            if (!_running)
                _running = start = true;
        }

        if (start)
            _ = Task.Run(Pump, CancellationToken.None);
        return completion.Task;
    }

    private void Pump()
    {
        while (true)
        {
            Item item;
            lock (_gate)
            {
                if (_queue.First is not { } first)
                {
                    _running = false;
                    return;
                }

                item = first.Value;
                _queue.RemoveFirst();
            }

            if (item.Token.IsCancellationRequested)
                item.Cancel();
            else
                item.Run(item.Token);
        }
    }

    private sealed record Item(string Kind, CancellationToken Token, Action<CancellationToken> Run, Action Cancel);
}
