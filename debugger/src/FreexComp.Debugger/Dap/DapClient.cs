using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace FreexComp.Debugger.Dap;

/// <summary>
/// Protocol client over a <see cref="DapTransport"/> (architecture §4.2): owns the outgoing sequence counter,
/// correlates responses with requests, forwards events in arrival order, rejects reverse requests and traces every
/// message. One reader task and one writer task; requests go out strictly in call order (DBG-011).
/// </summary>
internal sealed partial class DapClient
{
    private readonly DapTransport _transport;
    private readonly Action<string, JsonElement> _onEvent;
    private readonly Action<DebuggerException?> _onClosed;
    private readonly Action<DapTraceEntry>? _trace;
    private readonly TimeProvider _time;

    private readonly Lock _gate = new();
    private readonly Dictionary<int, PendingRequest> _pending = [];
    private readonly Channel<byte[]> _outgoing =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _readerCancel = new();
    private int _nextSeq = 1;
    private long _responses;
    private bool _closed;
    private string _closedMessage = Messages.AdapterExited;

    /// <param name="onEvent">Called on the reader task for every event, in arrival order (event name, body).</param>
    /// <param name="onClosed">
    /// Called once when the connection ends by itself: null for end of stream (adapter exited), or the protocol
    /// error. Not called after <see cref="Close"/>.
    /// </param>
    public DapClient(
        DapTransport transport,
        Action<string, JsonElement> onEvent,
        Action<DebuggerException?> onClosed,
        Action<DapTraceEntry>? trace = null,
        TimeProvider? timeProvider = null)
    {
        _transport = transport;
        _onEvent = onEvent;
        _onClosed = onClosed;
        _trace = trace;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// How many responses the adapter has sent so far, whether or not anybody still waited for them: a response that
    /// comes after its request timed out is dropped, yet it shows that the adapter is at work.
    /// </summary>
    public long ResponseCount => Interlocked.Read(ref _responses);

    /// <summary>Completes when the reader and writer tasks have ended.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Start() => Completion = Task.WhenAll(Task.Run(ReadLoopAsync), Task.Run(WriteLoopAsync));

    /// <summary>
    /// Sends a request and awaits its response body (undefined element when the response has none). Faults with
    /// <see cref="DebuggerException"/>: <c>RequestFailed</c> for <c>success:false</c>, <c>RequestTimeout</c>,
    /// <c>AdapterCrashed</c> when the connection ends; or <see cref="OperationCanceledException"/> on cancellation.
    /// An abandoned request's late response is dropped.
    /// </summary>
    /// <param name="onCompleted">
    /// Called once with the completed task, synchronously on the thread that completes the request (for a response:
    /// the reader task, before the next message is read). Lets the owner keep a response in order with the events
    /// around it; continuations of the returned task give no such guarantee.
    /// </param>
    public Task<JsonElement> RequestAsync(
        string command, object? arguments, TimeSpan timeout, CancellationToken ct, Action<Task<JsonElement>>? onCompleted = null)
    {
        if (ct.IsCancellationRequested)
            return Completed(Task.FromCanceled<JsonElement>(ct), onCompleted);

        var pending = new PendingRequest(command) { OnCompleted = onCompleted };
        var accepted = false;
        lock (_gate)
        {
            if (!_closed)
            {
                accepted = true;
                var seq = _nextSeq++;
                var message = DapJson.SerializeRequest(seq, command, arguments);
                _pending.Add(seq, pending);
                pending.Timer = _time.CreateTimer(
                    _ => Complete(seq, p => p.Completion.TrySetException(
                        new DebuggerException(DebugErrorCode.RequestTimeout, Messages.RequestTimedOut(command), command))),
                    null, timeout, Timeout.InfiniteTimeSpan);
                pending.Cancellation = ct.UnsafeRegister(
                    _ => Complete(seq, p => p.Completion.TrySetCanceled(ct)), null);
                _outgoing.Writer.TryWrite(message);
            }
        }

        return accepted ? pending.Completion.Task : Completed(Task.FromException<JsonElement>(Crashed(command)), onCompleted);
    }

    private static Task<JsonElement> Completed(Task<JsonElement> task, Action<Task<JsonElement>>? onCompleted)
    {
        onCompleted?.Invoke(task);
        return task;
    }

    /// <summary>Owner-initiated end: pending requests fail with <c>AdapterCrashed</c>; no closed callback.</summary>
    public void Close()
    {
        Shutdown(null, notify: false);
        _readerCancel.Cancel();
    }

    private async Task ReadLoopAsync()
    {
        DebuggerException? error = null;
        try
        {
            while (await _transport.ReadAsync(_readerCancel.Token).ConfigureAwait(false) is { } body)
            {
                Trace(DapDirection.FromAdapter, body);
                Dispatch(body);
            }
        }
        catch (DebuggerException e)
        {
            error = e;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The stream broke or the owner closed us: same as end of stream.
        }

        Shutdown(error, notify: true);
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var message in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Trace(DapDirection.ToAdapter, message);
            try
            {
                await _transport.SendAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The adapter's input is gone: the adapter exited.
                Shutdown(null, notify: true);
            }
        }
    }

    private void Dispatch(byte[] body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException e)
        {
            throw ProtocolError(Messages.MalformedJson + e.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                throw ProtocolError(Messages.MessageWithoutType);

            switch (type.GetString())
            {
                case "response":
                    HandleResponse(root);
                    break;
                case "event":
                    if (StringProperty(root, "event") is { } name)
                        _onEvent(name, root.TryGetProperty("body", out var eventBody) ? eventBody.Clone() : default);
                    break;
                case "request":
                    RejectReverseRequest(root);
                    break;
                default:
                    throw ProtocolError(Messages.UnknownMessageType + type.GetString());
            }
        }
    }

    private void HandleResponse(JsonElement root)
    {
        Interlocked.Increment(ref _responses);
        if (!root.TryGetProperty("request_seq", out var requestSeq) || !requestSeq.TryGetInt32(out var seq))
            return;   // nobody can be waiting for it
        var success = root.TryGetProperty("success", out var flag) && flag.ValueKind == JsonValueKind.True;
        var hasBody = root.TryGetProperty("body", out var body);
        if (success)
        {
            var result = hasBody ? body.Clone() : default;
            Complete(seq, p => p.Completion.TrySetResult(result));
        }
        else
        {
            var message = ErrorText(root, hasBody ? body : default);
            Complete(seq, p => p.Completion.TrySetException(new DebuggerException(
                DebugErrorCode.RequestFailed, message ?? Messages.RequestFailed(p.Command), p.Command)));
        }
    }

    /// <summary><c>body.error.format</c> with its variables substituted, else <c>message</c>, else null.</summary>
    private static string? ErrorText(JsonElement root, JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            && StringProperty(error, "format") is { Length: > 0 } format)
        {
            var hasVariables = error.TryGetProperty("variables", out var variables) && variables.ValueKind == JsonValueKind.Object;
            return PlaceholderRegex().Replace(format, match =>
                hasVariables && StringProperty(variables, match.Groups[1].Value) is { } value ? value : match.Value);
        }

        return StringProperty(root, "message") is { Length: > 0 } message ? message : null;
    }

    private void RejectReverseRequest(JsonElement root)
    {
        var requestSeq = root.TryGetProperty("seq", out var seqElement) && seqElement.TryGetInt32(out var value) ? value : 0;
        var command = StringProperty(root, "command") ?? "";
        lock (_gate)
        {
            if (_closed)
                return;
            _outgoing.Writer.TryWrite(DapJson.SerializeResponse(
                _nextSeq++, requestSeq, command, success: false, Messages.ReverseRequestNotSupported));
        }
    }

    /// <summary>Removes the request from the pending table (if still there) and completes it once.</summary>
    private void Complete(int seq, Action<PendingRequest> completion)
    {
        PendingRequest? pending;
        lock (_gate)
        {
            if (!_pending.Remove(seq, out pending))
                return;
        }

        pending.Release();
        completion(pending);
        pending.OnCompleted?.Invoke(pending.Completion.Task);
    }

    private void Shutdown(DebuggerException? error, bool notify)
    {
        List<PendingRequest> pending;
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            if (error is not null)
                _closedMessage = error.Message;
            pending = [.. _pending.Values];
            _pending.Clear();
            _outgoing.Writer.TryComplete();
        }

        foreach (var request in pending)
        {
            request.Release();
            request.Completion.TrySetException(Crashed(request.Command));
            request.OnCompleted?.Invoke(request.Completion.Task);
        }

        if (notify)
            _onClosed(error);
    }

    private DebuggerException Crashed(string command) => new(DebugErrorCode.AdapterCrashed, _closedMessage, command);

    private static DebuggerException ProtocolError(string detail) =>
        new(DebugErrorCode.ProtocolError, Messages.ProtocolError(detail));

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private void Trace(DapDirection direction, byte[] body)
    {
        if (_trace is null)
            return;
        try
        {
            _trace(new DapTraceEntry(_time.GetUtcNow(), direction, Encoding.UTF8.GetString(body)));
        }
        catch (Exception)
        {
            // A failing trace sink must never break the session.
        }
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();

    private sealed class PendingRequest(string command)
    {
        public string Command { get; } = command;
        public TaskCompletionSource<JsonElement> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<Task<JsonElement>>? OnCompleted { get; init; }
        public ITimer? Timer { get; set; }
        public CancellationTokenRegistration Cancellation { get; set; }

        public void Release()
        {
            Timer?.Dispose();
            Cancellation.Dispose();
        }
    }
}
