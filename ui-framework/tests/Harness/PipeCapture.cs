using System.Text;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// U-142: captures the redirected stdout or stderr of a child process on a background thread of its own. The pipe of a
/// child is a synchronous handle, so an asynchronous read of it parks a thread-pool thread until the child writes;
/// with many tests in parallel the pool then has no thread left for the work that would let the children finish.
/// The thread ends with the stream (the child exited, or its process object was disposed).
/// </summary>
internal sealed class PipeCapture
{
    private const int ReadSize = 4096;

    private readonly StringBuilder text = new();
    private readonly TaskCompletionSource<string> done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private PipeCapture(TextReader source, string threadName, bool lines)
    {
        new Thread(() => Pump(source, lines)) { IsBackground = true, Name = threadName }.Start();
    }

    /// <summary>
    /// What the reading thread saw of itself: (thread-pool thread, background thread). Valid once
    /// <see cref="Completion"/> ended; a capture always reads on a background thread outside the pool.
    /// </summary>
    public (bool Pool, bool Background) ReaderThread { get; private set; }

    /// <summary>Ends with the stream: everything that was read. Faulted only by an unexpected failure of the read.</summary>
    public Task<string> Completion => done.Task;

    /// <summary>What has been read so far.</summary>
    public string Text
    {
        get
        {
            lock (text)
            {
                return text.ToString();
            }
        }
    }

    /// <summary>
    /// Starts reading <paramref name="source"/> to its end. With <paramref name="lines"/> the text is captured line by
    /// line, each line ended by one <c>\n</c> whatever its line break was; without, exactly as it was written.
    /// </summary>
    public static PipeCapture Start(TextReader source, string threadName, bool lines = false) => new(source, threadName, lines);

    /// <summary>Blocks until the stream ended; everything that was read.</summary>
    public string ReadToEnd() => done.Task.GetAwaiter().GetResult();

    private void Pump(TextReader source, bool lines)
    {
        ReaderThread = (Thread.CurrentThread.IsThreadPoolThread, Thread.CurrentThread.IsBackground);
        try
        {
            if (lines)
            {
                while (source.ReadLine() is { } line)
                {
                    lock (text)
                    {
                        text.Append(line).Append('\n');
                    }
                }
            }
            else
            {
                var buffer = new char[ReadSize];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    lock (text)
                    {
                        text.Append(buffer, 0, read);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The pipe broke or the process object was disposed: end of stream.
        }
        catch (Exception e)
        {
            done.SetException(e);
            return;
        }

        done.SetResult(Text);
    }
}
