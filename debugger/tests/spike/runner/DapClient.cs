using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreexComp.Debugger.Spike;

/// <summary>
/// Minimal DAP client over the adapter's stdio (Content-Length framing, UTF-8 JSON). Every received message is
/// queued; waits remove the first matching message and are bounded by a timeout (no sleeps). Every message sent
/// or received is written to a transcript file. Dispose kills the whole adapter process tree (debuggee included).
/// </summary>
internal sealed class DapClient : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly object _gate = new();
    private readonly List<JsonElement> _inbox = [];
    private readonly StreamWriter _log;
    private readonly StringBuilder _stderr = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _eof;
    private int _seq;

    private List<JsonElement> Outputs { get; } = [];
    private List<JsonElement> Events { get; } = [];
    public int? DebuggeePid { get; private set; }
    public string LogPath { get; }

    public DapClient(string adapter, string pythonDir, bool nativePdbReader, string logPath)
    {
        LogPath = logPath;
        _log = new StreamWriter(logPath, false, Utf8) { AutoFlush = true };
        var psi = new ProcessStartInfo(adapter)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = Path.GetDirectoryName(adapter)!,
        };
        // Only the bundled Python runtime may satisfy the debugger library (ADR 005): drop every other Python from PATH.
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
            .Where(p => p.Length > 0 && !p.Contains("python", StringComparison.OrdinalIgnoreCase));
        psi.Environment["PATH"] = pythonDir + ";" + string.Join(';', path);
        if (nativePdbReader) psi.Environment["LLDB_USE_NATIVE_PDB_READER"] = "1";
        else psi.Environment.Remove("LLDB_USE_NATIVE_PDB_READER");
        _log.WriteLine($"# adapter {adapter}  LLDB_USE_NATIVE_PDB_READER={(nativePdbReader ? "1" : "(unset)")}");
        _process = Process.Start(psi) ?? throw new InvalidOperationException("adapter did not start");
        _stdin = _process.StandardInput.BaseStream;
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_stderr) _stderr.AppendLine(e.Data); };
        _process.BeginErrorReadLine();
        new Thread(ReadLoop) { IsBackground = true, Name = "dap-reader" }.Start();
    }

    public string Stderr { get { lock (_stderr) return _stderr.ToString(); } }

    public int Send(string command, JsonNode? arguments = null)
    {
        var msg = new JsonObject { ["seq"] = Interlocked.Increment(ref _seq), ["type"] = "request", ["command"] = command };
        if (arguments != null) msg["arguments"] = arguments;
        var body = Utf8.GetBytes(msg.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_log) _log.WriteLine($"[{_clock.ElapsedMilliseconds,6} ms] -> {msg.ToJsonString()}");
        try { _stdin.Write(header); _stdin.Write(body); _stdin.Flush(); }
        catch (IOException ex) { lock (_log) _log.WriteLine($"# send failed: {ex.Message}"); }
        return (int)msg["seq"]!;
    }

    /// <summary>Sends a request and waits (bounded) for its response; null on timeout or adapter exit.</summary>
    public JsonElement? Request(string command, JsonNode? arguments = null, int timeoutMs = 10_000)
    {
        int seq = Send(command, arguments);
        return WaitResponse(seq, timeoutMs);
    }

    public JsonElement? WaitResponse(int seq, int timeoutMs) =>
        Wait(m => Dap.Str(m, "type") == "response" && Dap.Int(m, "request_seq") == seq, timeoutMs);

    public JsonElement? WaitEvent(string name, int timeoutMs, Func<JsonElement, bool>? predicate = null) =>
        Wait(m => Dap.Str(m, "type") == "event" && Dap.Str(m, "event") == name && (predicate == null || predicate(m)), timeoutMs);

    /// <summary>First queued or arriving message matching any of the event names (removed from the queue).</summary>
    public JsonElement? WaitAnyEvent(string[] names, int timeoutMs) =>
        Wait(m => Dap.Str(m, "type") == "event" && names.Contains(Dap.Str(m, "event")), timeoutMs);

    public JsonElement? Wait(Func<JsonElement, bool> predicate, int timeoutMs)
    {
        var deadline = Stopwatch.StartNew();
        lock (_gate)
        {
            while (true)
            {
                for (int i = 0; i < _inbox.Count; i++)
                {
                    if (predicate(_inbox[i])) { var m = _inbox[i]; _inbox.RemoveAt(i); return m; }
                }
                long remaining = timeoutMs - deadline.ElapsedMilliseconds;
                if (_eof || remaining <= 0)
                {
                    lock (_log) _log.WriteLine($"[{_clock.ElapsedMilliseconds,6} ms] # wait gave up ({(_eof ? "adapter closed stdout" : $"timeout {timeoutMs} ms")})");
                    return null;
                }
                Monitor.Wait(_gate, TimeSpan.FromMilliseconds(remaining));
            }
        }
    }

    public JsonElement[] EventsSnapshot() { lock (_gate) return [.. Events]; }

    public string[] OutputCategories() { lock (_gate) return [.. Outputs.Select(o => Dap.Str(o, "body.category") ?? "(none)").Distinct()]; }

    public bool HasExited => _process.HasExited;

    private void ReadLoop()
    {
        var s = _process.StandardOutput.BaseStream;
        try
        {
            while (true)
            {
                int length = -1;
                while (true)
                {
                    string? line = ReadHeaderLine(s);
                    if (line == null) return;
                    if (line.Length == 0) break;
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..].Trim());
                }
                if (length < 0) continue;
                var buffer = new byte[length];
                s.ReadExactly(buffer);
                var text = Utf8.GetString(buffer);
                lock (_log) _log.WriteLine($"[{_clock.ElapsedMilliseconds,6} ms] <- {text}");
                JsonElement message;
                using (var doc = JsonDocument.Parse(buffer)) message = doc.RootElement.Clone();
                lock (_gate)
                {
                    _inbox.Add(message);
                    if (Dap.Str(message, "type") == "event")
                    {
                        Events.Add(message);
                        if (Dap.Str(message, "event") == "output") Outputs.Add(message);
                        if (Dap.Str(message, "event") == "process" && Dap.Int(message, "body.systemProcessId") is int pid) DebuggeePid = pid;
                    }
                    Monitor.PulseAll(_gate);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ObjectDisposedException)
        {
        }
        finally
        {
            lock (_gate) { _eof = true; Monitor.PulseAll(_gate); }
        }
    }

    private static string? ReadHeaderLine(Stream s)
    {
        var sb = new StringBuilder();
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) return null;
            if (b == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)b);
        }
    }

    public string OutputText(string category)
    {
        lock (_gate)
            return string.Concat(Outputs.Where(o => Dap.Str(o, "body.category") == category).Select(o => Dap.Str(o, "body.output")));
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited) _process.WaitForExit(3000);
            if (!_process.HasExited)
            {
                lock (_log) _log.WriteLine("# adapter still running: killing the process tree");
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { }
        if (DebuggeePid is int pid)
        {
            try
            {
                using var d = Process.GetProcessById(pid);
                lock (_log) _log.WriteLine($"# debuggee {pid} still running: killing it");
                d.Kill(entireProcessTree: true);
                d.WaitForExit(5000);
            }
            catch (ArgumentException) { } // already gone
            catch (InvalidOperationException) { }
        }
        var err = Stderr;
        lock (_log)
        {
            if (err.Length > 0) _log.WriteLine("# adapter stderr:\n" + err);
            _log.Dispose();
        }
        _process.Dispose();
    }
}

/// <summary>JSON helpers: dotted path access on DAP messages.</summary>
internal static class Dap
{
    public static JsonElement? Get(JsonElement? e, string path)
    {
        if (e is not JsonElement cur) return null;
        foreach (var part in path.Split('.'))
        {
            if (cur.ValueKind == JsonValueKind.Array && int.TryParse(part, out int idx))
            {
                if (idx >= cur.GetArrayLength()) return null;
                cur = cur[idx];
            }
            else if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(part, out cur)) return null;
        }
        return cur;
    }

    public static string? Str(JsonElement? e, string path) => Get(e, path) is JsonElement v
        ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Null => null, _ => v.GetRawText() }
        : null;

    public static long? Long(JsonElement? e, string path) =>
        Get(e, path) is JsonElement v && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;

    public static int? Int(JsonElement? e, string path) => Long(e, path) is long n && n >= int.MinValue && n <= int.MaxValue ? (int)n : null;

    public static bool Bool(JsonElement? e, string path) => Get(e, path) is JsonElement v && v.ValueKind == JsonValueKind.True;

    public static IEnumerable<JsonElement> Arr(JsonElement? e, string path) =>
        Get(e, path) is JsonElement v && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToArray() : [];

    public static string Raw(JsonElement? e, string path = "", int max = 300)
    {
        var v = path.Length == 0 ? e : Get(e, path);
        if (v is not JsonElement x) return "(none)";
        var t = x.GetRawText();
        return t.Length <= max ? t : t[..max] + "…";
    }
}
