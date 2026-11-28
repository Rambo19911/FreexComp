using System.IO.Pipes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Debugger.Session;

/// <summary>How the debuggee's standard output and standard error reach the session (DAP usage DBG-058, ADR 005).</summary>
internal enum OutputRelayMode
{
    /// <summary>
    /// The adapter relays them as <c>output</c> events. Nothing is redirected then, the standard input neither: the
    /// debuggee reads the adapter's, which carries the protocol (DBG-059) - not a mode for a program that reads input.
    /// </summary>
    Events,

    /// <summary>The session redirects them to two files of its own and reads them (<see cref="OutputRelay"/>).</summary>
    Files,
}

/// <summary>How much <see cref="OutputRelay.Pump"/> reads.</summary>
internal enum RelayRead
{
    /// <summary>The periodic read while the program runs: at most 1 MiB per file.</summary>
    Poll,

    /// <summary>Everything written so far: the program is stopped and writes nothing more until it runs again.</summary>
    ToEnd,

    /// <summary>Everything, and an unfinished UTF-8 sequence becomes U+FFFD: the program is gone.</summary>
    Final,
}

/// <summary>
/// The session's own relay of the debuggee's output (DBG-058, mode <see cref="OutputRelayMode.Files"/>): a fresh
/// directory <c>%TEMP%\FreexComp\dbg-&lt;pid&gt;-&lt;n&gt;</c> with <c>out.txt</c> and <c>err.txt</c>, which the
/// adapter is told to give the debuggee as stdout and stderr (launch <c>stdio</c>). The session reads what was
/// appended every <see cref="PollInterval"/>, to the end before it reports a stop, and once more, to the end, before it
/// reports the end of the session; the bytes are UTF-8, a sequence cut by the end of a read is completed by the next
/// one. Not thread-safe: the session loop is its only user.
/// <para>
/// The debuggee's standard input is the session's own as well (DBG-059): a pipe named after the directory, whose
/// writing end the session holds and never writes to. A debuggee that is given no standard input gets the adapter's -
/// the pipe the protocol's requests arrive on - and a program that reads a line then takes a request away from the
/// adapter: the request is never answered, and the program goes on with it as its input (G-103).
/// </para>
/// <para>
/// A launch whose input is at its end from the start (DBG-557) closes the writing end as soon as the program's side
/// has opened the pipe (<see cref="StdinClientConnected"/>, <see cref="CloseStdin"/>) - never before: a path that
/// cannot be opened makes the adapter give the program its own standard input again.
/// </para>
/// </summary>
internal sealed class OutputRelay : IDisposable
{
    /// <summary>The mode the session is built with (ADR 005, 2026-11-27: the adapter lost output under load).</summary>
    public const OutputRelayMode Mode = OutputRelayMode.Files;

    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    // What one periodic read takes from a file at most, so that a program that writes without pause cannot hold the
    // session loop; the final read takes everything.
    private const int MaxBytesPerPoll = 1 << 20;
    private const int ReadSize = 64 * 1024;

    private static int _counter;

    private readonly Tail[] _tails;
    private readonly byte[] _buffer = new byte[ReadSize];
    private NamedPipeServerStream? _stdin;
    private CancellationTokenSource? _stdinWatch;
    private bool _disposed;

    private OutputRelay(string directory)
    {
        Directory = directory;
        StdoutPath = Path.Combine(directory, "out.txt");
        StderrPath = Path.Combine(directory, "err.txt");
        StdinPath = StdinPipePrefix + StdinPipeName(directory);
        _tails = [new Tail(StdoutPath, OutputCategory.Stdout), new Tail(StderrPath, OutputCategory.Stderr)];
    }

    private const string StdinPipePrefix = @"\\.\pipe\";

    /// <summary>The name of the standard-input pipe that belongs to a directory: <c>FreexComp.dbg-&lt;pid&gt;-&lt;n&gt;.in</c>.</summary>
    private static string StdinPipeName(string directory) => "FreexComp." + Path.GetFileName(directory) + ".in";

    /// <summary>The directory that holds both files; deleted by <see cref="Dispose"/>.</summary>
    public string Directory { get; }

    public string StdoutPath { get; }

    public string StderrPath { get; }

    /// <summary>The path of the pipe the debuggee reads its standard input from; nothing is ever written to it.</summary>
    public string StdinPath { get; }

    /// <summary>The launch <c>stdio</c> value: stdin is the session's pipe, stdout and stderr go to the two files.</summary>
    public IReadOnlyList<string?> StdioArgument => [StdinPath, StdoutPath, StderrPath];

    /// <summary>
    /// Creates the directory and both (empty) files under <paramref name="root"/> (default <c>%TEMP%\FreexComp</c>),
    /// and the standard-input pipe. A directory of that name left behind by an earlier process with the same id is
    /// skipped, never reused.
    /// </summary>
    /// <exception cref="IOException">The directory, a file or the pipe could not be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, for lack of rights.</exception>
    public static OutputRelay Create(string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "FreexComp");
        string directory;
        do
            directory = Path.Combine(root, $"dbg-{Environment.ProcessId}-{Interlocked.Increment(ref _counter)}");
        while (System.IO.Directory.Exists(directory));

        System.IO.Directory.CreateDirectory(directory);
        var relay = new OutputRelay(directory);
        try
        {
            foreach (var tail in relay._tails)
                tail.Open();
            // One reader (whoever the adapter starts the debuggee through opens it once, for reading), the current
            // user only. The session keeps the writing end open and silent: a read of the debuggee waits. (The
            // session's handle is an asynchronous one only so that the wait for the reader can be given up.)
            relay._stdin = new NamedPipeServerStream(
                StdinPipeName(directory), PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
        }
        catch
        {
            relay.Dispose();
            throw;
        }

        return relay;
    }

    /// <summary>
    /// Reads what was appended to each file since the last call and reports it, stdout first, one call of
    /// <paramref name="report"/> per file that had text.
    /// </summary>
    public void Pump(RelayRead read, Action<OutputCategory, string> report)
    {
        if (_disposed)
            return;
        foreach (var tail in _tails)
        {
            if (tail.Read(_buffer, read == RelayRead.Poll ? MaxBytesPerPoll : long.MaxValue, read == RelayRead.Final) is { Length: > 0 } text)
                report(tail.Category, text);
        }
    }

    /// <summary>
    /// Completes when the program's side has opened the standard-input pipe (at once when it already has); cancelled
    /// or faulted when the relay is disposed, or the pipe closed, before that. Asked for once at most.
    /// </summary>
    public Task StdinClientConnected()
    {
        if (_stdin is not { } stdin)
            return Task.FromCanceled(new CancellationToken(canceled: true));
        _stdinWatch = new CancellationTokenSource();
        return stdin.WaitForConnectionAsync(_stdinWatch.Token);
    }

    /// <summary>
    /// Closes the session's end of the standard-input pipe: a read of the program reports the end of its input
    /// (DBG-557). Only for a pipe that has its reader - without one the path could no longer be opened.
    /// </summary>
    public void CloseStdin()
    {
        _stdinWatch?.Cancel();
        _stdinWatch?.Dispose();
        _stdinWatch = null;
        _stdin?.Dispose();
        _stdin = null;
    }

    /// <summary>Closes both files and the pipe and deletes the directory (once the debuggee has let go of the files).</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var tail in _tails)
            tail.Dispose();
        CloseStdin();
        if (TryDelete(Directory))
            return;

        // A process that is still ending may hold a file for a moment: retried in the background, never waited for.
        var directory = Directory;
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 40 && !TryDelete(directory); attempt++)
                await Task.Delay(PollInterval).ConfigureAwait(false);
        });
    }

    private static bool TryDelete(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory))
                System.IO.Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>One file: where reading goes on and the UTF-8 decoder that keeps an incomplete sequence.</summary>
    private sealed class Tail(string path, OutputCategory category) : IDisposable
    {
        private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();
        private SafeFileHandle? _handle;
        private long _offset;

        public OutputCategory Category { get; } = category;

        /// <summary>
        /// Creates the empty file and opens it for reading. Every kind of sharing is allowed: the debuggee's side opens
        /// it for writing (and truncates it) while this handle is open, and the directory is deleted with it open.
        /// </summary>
        public void Open()
        {
            File.WriteAllBytes(path, []);
            _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        public string Read(byte[] buffer, long limit, bool final)
        {
            if (_handle is null)
                return "";
            var text = new StringBuilder();
            long taken = 0;
            while (taken < limit)
            {
                int read;
                try
                {
                    read = RandomAccess.Read(_handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, limit - taken)), _offset);
                }
                catch (IOException)
                {
                    break;   // a file that cannot be read gives nothing more; the session goes on
                }

                if (read == 0)
                    break;
                _offset += read;
                taken += read;
                Decode(buffer.AsSpan(0, read), flush: false, text);
            }

            if (final)
                Decode([], flush: true, text);
            return text.ToString();
        }

        private void Decode(ReadOnlySpan<byte> bytes, bool flush, StringBuilder text)
        {
            Span<char> chars = new char[_decoder.GetCharCount(bytes, flush)];
            text.Append(chars[.._decoder.GetChars(bytes, chars, flush)]);
        }

        public void Dispose()
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}
