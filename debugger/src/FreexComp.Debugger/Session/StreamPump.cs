using System.IO.Pipelines;

namespace FreexComp.Debugger.Session;

/// <summary>Runs blocking work on a thread of its own instead of a thread-pool thread.</summary>
internal static class DedicatedThread
{
    /// <summary>Starts a background thread for <paramref name="work"/>; the task ends with it (faulted when it throws).</summary>
    public static Task Run(string name, Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work();
                done.SetResult();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = name,
        };
        thread.Start();
        return done.Task;
    }
}

/// <summary>
/// Makes a stream that can only be read by blocking (the pipe of a child process is a synchronous handle) readable
/// without blocking anything: a thread of its own reads the source and hands the bytes on. An asynchronous read of
/// the pipe itself would park a thread-pool thread for as long as the adapter is silent; with several sessions (or a
/// busy host) the pool then has no thread left for the work that would make the adapter speak.
/// </summary>
internal static class StreamPump
{
    private const int ReadSize = 4096;

    /// <summary>
    /// Starts pumping <paramref name="source"/>. The returned stream yields the same bytes in the same order and ends
    /// when the source ends or breaks. The pump never waits for its reader: its thread ends with the source.
    /// </summary>
    public static Stream Start(Stream source, string threadName)
    {
        // No back-pressure: a paused pump would outlive a reader that stopped reading.
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0, useSynchronizationContext: false));
        _ = DedicatedThread.Run(threadName, () => Pump(source, pipe.Writer));
        return pipe.Reader.AsStream();
    }

    private static void Pump(Stream source, PipeWriter writer)
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                var read = source.Read(writer.GetSpan(ReadSize));
                if (read == 0)
                    break;
                writer.Advance(read);
                // Without a pause threshold the flush only publishes the bytes; it completes at once.
                var flush = writer.FlushAsync();
                if ((flush.IsCompleted ? flush.Result : flush.AsTask().GetAwaiter().GetResult()).IsCompleted)
                    break;   // the reader is gone
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The pipe broke or the process object was disposed: end of stream.
        }
        catch (Exception e)
        {
            failure = e;
        }

        writer.Complete(failure);
    }
}
