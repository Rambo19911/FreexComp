namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// Reads the redirected output of a child process. Such a pipe is a synchronous handle: an asynchronous read of it
/// parks a thread-pool thread until the child writes, and tests running in parallel then leave the pool without a
/// thread for the work that would let them finish. Each pipe is therefore read by blocking on a thread of its own.
/// </summary>
internal static class ChildPipe
{
    private const int ReadSize = 4096;

    /// <summary>
    /// Starts a background thread named <paramref name="threadName"/> that reads <paramref name="source"/> to its end;
    /// the task yields every byte read. A source that breaks or is disposed (the child was killed, the process object
    /// released) ends the capture with what was read so far; any other failure faults the task. The read cannot be
    /// cancelled: a caller that stops waiting ends the thread by killing the child.
    /// </summary>
    public static Task<byte[]> ReadToEnd(Stream source, string threadName)
    {
        var done = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Read(source));
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = threadName,
        };
        thread.Start();
        return done.Task;
    }

    private static byte[] Read(Stream source)
    {
        var captured = new MemoryStream();
        var buffer = new byte[ReadSize];
        try
        {
            while (true)
            {
                var read = source.Read(buffer);
                if (read == 0)
                {
                    break;
                }

                captured.Write(buffer, 0, read);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // the pipe broke or the process object was disposed: end of stream
        }

        return captured.ToArray();
    }
}
