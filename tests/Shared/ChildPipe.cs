namespace FreexComp.Tests.Shared;

/// <summary>
/// Reads a redirected pipe of a child process to its end on a dedicated background thread. The pipes of a child
/// process are synchronous handles: an asynchronous read would park a thread-pool thread until the child writes, and
/// tests running in parallel would starve the pool (L-022). Linked into the lead's test projects.
/// </summary>
internal static class ChildPipe
{
    public static Task<string> ReadToEndOnThread(StreamReader reader, string name)
    {
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(reader.ReadToEnd());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = "FreexComp.Tests.ChildPipe." + name,
        };
        thread.Start();
        return done.Task;
    }
}
