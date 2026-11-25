namespace FreexComp.Compiler.Toolchain;

/// <summary>
/// Reads a redirected pipe of a child process on a dedicated background thread (CMP-602, C-319). Such a pipe is a synchronous
/// handle: an asynchronous read of it, or the event-based line reader of the process class, parks a thread-pool thread until
/// the child writes, and a parallel build (two pipes per tool process) then starves the pool. A dedicated thread blocks
/// nothing but itself.
/// </summary>
internal static class PipeThread
{
    /// <summary>The prefix of the threads' names (<c>FreexComp pipe &lt;what&gt;</c>).</summary>
    public const string NamePrefix = "FreexComp pipe ";

    /// <summary>
    /// Starts a thread that hands every line of <paramref name="reader"/> to <paramref name="onLine"/>, in order, until the pipe
    /// ends (the child and everything that inherited the handle closed it, or the reader was disposed). The task completes
    /// when the thread is done; it never fails for the pipe — an exception of <paramref name="onLine"/> faults it.
    /// </summary>
    public static Task ReadLines(TextReader reader, Action<string> onLine, string what)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                while (ReadLine(reader) is { } line)
                {
                    onLine(line);
                }

                done.SetResult();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = NamePrefix + what,
        };
        thread.Start();
        return done.Task;
    }

    /// <summary>The next line; null at the end of the pipe, also when it breaks or is closed under the read.</summary>
    private static string? ReadLine(TextReader reader)
    {
        try
        {
            return reader.ReadLine();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return null;
        }
    }
}
