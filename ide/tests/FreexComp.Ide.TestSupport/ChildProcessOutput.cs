using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// I-262: reads the redirected stdout and stderr of a child process to their ends, each on its own background thread
/// with blocking reads. A redirected pipe is a synchronous handle: an asynchronous read of it parks a thread-pool
/// thread until the child writes, and test projects running in parallel starve the pool that way. Reading both
/// streams at once also keeps a child that fills one pipe from blocking while the test waits on the other.
/// <code>var output = ChildProcessOutput.Read(process); await process.WaitForExitAsync(token); var text = await output.StandardOutput;</code>
/// A stream that is not redirected reads as the empty text. A pipe that breaks (the child was killed, the process
/// object disposed) ends its text with what arrived so far.
/// </summary>
public sealed class ChildProcessOutput
{
    private ChildProcessOutput(Process process, bool output, bool error)
    {
        var threads = new List<Thread>();
        var id = process.Id.ToString(CultureInfo.InvariantCulture);
        StandardOutput = output ? Start(process.StandardOutput, $"child-process {id} stdout", threads) : Task.FromResult("");
        StandardError = error ? Start(process.StandardError, $"child-process {id} stderr", threads) : Task.FromResult("");
        ReaderThreads = threads;
    }

    /// <summary>Everything the child wrote to stdout; completes at the end of the stream.</summary>
    public Task<string> StandardOutput { get; }

    /// <summary>Everything the child wrote to stderr; completes at the end of the stream.</summary>
    public Task<string> StandardError { get; }

    /// <summary>Both streams are at their ends.</summary>
    public Task Completed => Task.WhenAll(StandardOutput, StandardError);

    /// <summary>The threads that read (one per redirected stream): named background threads, none from the pool.</summary>
    public IReadOnlyList<Thread> ReaderThreads { get; }

    /// <summary>
    /// Starts reading every redirected output stream of a started process. Call it once, right after the start; a
    /// process whose output nobody needs is drained the same way, so that a full pipe never blocks it.
    /// </summary>
    public static ChildProcessOutput Read(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return new ChildProcessOutput(process, process.StartInfo.RedirectStandardOutput, process.StartInfo.RedirectStandardError);
    }

    private static Task<string> Start(StreamReader reader, string name, List<Thread> threads)
    {
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            try
            {
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                    text.Append(buffer, 0, count);
            }
            catch (IOException)
            {
                // The pipe broke: what arrived so far is kept.
            }
            catch (ObjectDisposedException)
            {
            }

            done.SetResult(text.ToString());
        })
        {
            IsBackground = true,
            Name = name,
        };
        threads.Add(thread);
        thread.Start();
        return done.Task;
    }
}
