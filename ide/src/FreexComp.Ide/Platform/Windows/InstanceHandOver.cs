using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace FreexComp.Ide.Platform.Windows;

/// <summary>
/// I-213: a second IDE started with file arguments hands them to the running IDE of the same user directory and exits
/// (no window). The running instance owns a named mutex <c>Local\FreexComp.Instance.&lt;key&gt;</c> and serves a local
/// named pipe <c>FreexComp.Instance.&lt;key&gt;</c> (current user only, no socket, ARCHITECTURE §9); the key is a hash of
/// the user directory, so instances with different <c>--user-dir</c> never meet. Protocol: one full path per UTF-8 line,
/// an empty line, then the server answers <c>ok</c>. Without a running instance, or if it does not answer in time, the
/// second instance simply starts (IDE-P1-014: several instances may run).
/// </summary>
public sealed class InstanceHandOver : IDisposable
{
    private const string Ack = "ok";
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    private InstanceHandOver(Mutex mutex, string key)
    {
        _mutex = mutex;
        _pipeName = PipeName(key);
    }

    /// <summary>The instance key of a user directory (<c>--user-dir</c>; null = the default one).</summary>
    public static string Key(string? userDir)
    {
        var text = userDir is null ? "" : Path.GetFullPath(userDir).TrimEnd('\\', '/').ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private static string PipeName(string key) => "FreexComp.Instance." + key;

    private static string MutexName(string key) => @"Local\FreexComp.Instance." + key;

    /// <summary>Makes this process the running instance of <paramref name="key"/>; null when another process already is.</summary>
    public static InstanceHandOver? TryClaim(string key)
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName(key), out var createdNew);
        if (createdNew)
            return new InstanceHandOver(mutex, key);
        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Sends <paramref name="paths"/> to the running instance of <paramref name="key"/>; true when it acknowledged them.
    /// False at once when no instance runs; false after <paramref name="timeout"/> when it does not answer.
    /// </summary>
    public static bool TrySend(string key, IReadOnlyList<string> paths, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!Mutex.TryOpenExisting(MutexName(key), out var existing))
            return false;
        existing.Dispose();
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var pipe = new NamedPipeClientStream(".", PipeName(key), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.ConnectAsync(cts.Token).GetAwaiter().GetResult();
            var request = Encoding.UTF8.GetBytes(string.Concat(paths.Select(p => p + "\n")) + "\n");
            pipe.WriteAsync(request, cts.Token).AsTask().GetAwaiter().GetResult();
            pipe.FlushAsync(cts.Token).GetAwaiter().GetResult();
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            return reader.ReadLineAsync(cts.Token).AsTask().GetAwaiter().GetResult() == Ack;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Starts serving: each hand-over's paths go to <paramref name="received"/> on a background thread.</summary>
    public void Serve(Action<IReadOnlyList<string>> received)
    {
        ArgumentNullException.ThrowIfNull(received);
        if (_loop is not null)
            throw new InvalidOperationException("Already serving.");
        _loop = Task.Run(() => LoopAsync(received, _stop.Token));
    }

    private async Task LoopAsync(Action<IReadOnlyList<string>> received, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var paths = new List<string>();
                using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
                {
                    while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { Length: > 0 } line)
                        paths.Add(line);
                }

                if (paths.Count > 0)
                    received(paths);
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(Ack + "\n"), ct).ConfigureAwait(false);
                await pipe.FlushAsync(ct).ConfigureAwait(false);
                // The answer stays readable until the client has closed its end (end-of-file here), at most 5 s.
                using var drain = CancellationTokenSource.CreateLinkedTokenSource(ct);
                drain.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    _ = await pipe.ReadAsync(new byte[1], drain.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // a client that keeps its end open: go on
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that went away mid-request: serve the next one.
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // the loop ends with the cancellation
        }

        _stop.Dispose();
        _mutex.Dispose();
    }
}
