using System.Text;

namespace FreexComp.Data.Tests;

/// <summary>
/// Reads a redirected output pipe of a child process (task A-080). Such a pipe is a synchronous handle: an asynchronous
/// read of it parks a thread-pool thread for as long as the child is silent, and tests running in parallel then leave
/// the pool without a thread. Every pipe is read to its end by blocking reads on a background thread of its own.
/// </summary>
internal static class ChildPipe
{
    /// <summary>
    /// Starts a background thread named <paramref name="threadName"/> that reads <paramref name="reader"/> to its end.
    /// The task ends with the text read; a broken pipe or a reader disposed under the thread counts as the end.
    /// </summary>
    public static Task<string> ReadToEnd(TextReader reader, string threadName)
    {
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            try
            {
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    text.Append(buffer, 0, read);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // the pipe broke or the process object was disposed: end of the text
            }
            catch (Exception e)
            {
                done.SetException(e);
                return;
            }

            done.SetResult(text.ToString());
        })
        {
            IsBackground = true,
            Name = threadName,
        };
        thread.Start();
        return done.Task;
    }

    /// <summary>For a read nobody will await (its process was stopped): a late failure of it is not reported as unobserved.</summary>
    public static void Abandon(Task<string> read) =>
        _ = read.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
