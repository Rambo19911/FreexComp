using System.Globalization;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

// Stop pipeline (architecture §7), execution control (DAP usage §6), threads and stack trace (DAP usage §5.2, §5.3).
internal sealed partial class DebugSession
{
    private const int StopFrameCount = 20;
    private const int MaxStackLevels = 1000;

    // Session loop only.
    private int _epoch;
    private IReadOnlyList<DapThread> _threads = [];      // last known thread list (kept while running: needed for pause)
    private bool _threadsAreOfThisStop;
    // Our frame ids of the current stop -> the adapter's frame ids. Ids are never reused within a session, so an id
    // of an earlier stop is simply unknown (DBG-077, DBG-408).
    private readonly Dictionary<int, int> _frames = [];
    private int _lastFrameId;

    // ---- stop pipeline ----

    private async Task OnStoppedAsync(StoppedEventBody body)
    {
        if (State is not (DebugState.Starting or DebugState.Running))
            return;

        StopInfo stop;
        try
        {
            stop = await BuildStopAsync(body).ConfigureAwait(false);
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.AdapterCrashed)
        {
            return;   // the "adapter closed" work item follows
        }
        catch (DebuggerException e)
        {
            // A stop is never lost (DBG-028).
            stop = new StopInfo(_epoch + 1, StopReason.Pause, body.ThreadId ?? 0, null, [], null, Messages.StopDetailsUnavailable(e.Message));
        }
        catch (OperationCanceledException)
        {
            return;   // the session is ending
        }

        // Public stop: epoch++, snapshot, StateChanged immediately before Stopped (DBG-015, DBG-421, DBG-422).
        _epoch = stop.Epoch;
        _currentStop = stop;
        SetState(DebugState.Stopped);
        _events.Raise(() => Stopped, stop);
        CompleteLaunch();
    }

    /// <summary>Steps 1-3 of the pipeline: <c>threads</c>, <c>stackTrace</c> of the stopping thread, classification.</summary>
    private async Task<StopInfo> BuildStopAsync(StoppedEventBody body)
    {
        _threads = DapJson.Deserialize<ThreadsResponseBody>(await RequestAsync("threads", null, CancellationToken.None).ConfigureAwait(false)).Threads;
        _threadsAreOfThisStop = true;

        // The pinned adapter reports a pause as an "exception" stop described "interrupt" on an injected break-in
        // thread; the public thread is then the first real thread (DBG-091).
        var isInterrupt = body.Reason == "exception" && string.Equals(body.Description, "interrupt", StringComparison.OrdinalIgnoreCase);
        var threadId = isInterrupt
            ? _threads.FirstOrDefault(t => t.Id != body.ThreadId)?.Id ?? body.ThreadId ?? 0
            : body.ThreadId ?? _threads.FirstOrDefault()?.Id ?? 0;   // DBG-071

        var response = DapJson.Deserialize<StackTraceResponseBody>(await RequestAsync(
            "stackTrace", new StackTraceArguments(threadId, 0, StopFrameCount), CancellationToken.None).ConfigureAwait(false));
        var frames = response.StackFrames.Select((frame, index) => MapFrame(frame, index, register: false)).ToList();
        // Top user frame; when there is none, the first frame that has source.
        var location = (frames.FirstOrDefault(f => f.IsUserCode) ?? frames.FirstOrDefault(f => f.Location is not null))?.Location;
        var epoch = _epoch + 1;

        switch (body.Reason)
        {
            case "entry" when _launchRequest is { StopOnEntry: true }:
                return new StopInfo(epoch, StopReason.Entry, threadId, location, [], null, Messages.StoppedAtEntry);

            case "breakpoint" or "function breakpoint":
            {
                var hit = (body.HitBreakpointIds ?? [])
                    .Select(id => _breakpoints.FindByAdapterId(id)).OfType<BreakpointManager.Entry>().ToList();
                // No ids from the adapter: match the top frame against the bound lines (DBG-071).
                if (hit.Count == 0 && frames.FirstOrDefault()?.Location is { } top)
                    hit = [.. _breakpoints.FindByLocation(top.Path, top.Line)];
                if (hit.Count == 0)
                    break;   // none of ours: a plain pause
                return new StopInfo(epoch, StopReason.Breakpoint, threadId, location, [.. hit.Select(e => e.Spec.Id)], null,
                    location is null ? Messages.BreakpointHit : Messages.BreakpointAt(Path.GetFileName(location.Path), location.Line));
            }

            case "step":
                return new StopInfo(epoch, StopReason.Step, threadId, location, [], null, Messages.StepCompleted);
        }

        var description = body.Reason is "pause" or "breakpoint" or "function breakpoint" || isInterrupt
            ? Messages.Paused
            : FirstNonEmpty(body.Description, body.Text) ?? Messages.Paused;
        return new StopInfo(epoch, StopReason.Pause, threadId, location, [], null, description);
    }

    private static string? FirstNonEmpty(params string?[] texts) => texts.FirstOrDefault(t => !string.IsNullOrEmpty(t));

    /// <summary>The state leaves <c>Stopped</c>: the snapshot and everything valid for one stop only is dropped.</summary>
    private void LeaveStop()
    {
        _currentStop = null;
        _frames.Clear();
        _threadsAreOfThisStop = false;
        _memory?.Clear();
    }

    // ---- execution control ----

    /// <summary>
    /// <c>continue</c>, <c>next</c>, <c>stepIn</c> or <c>stepOut</c>: after the adapter's acknowledgement the session is
    /// <c>Running</c> (DBG-090, DBG-017); a failure leaves the state unchanged.
    /// </summary>
    private async Task<bool> ResumeAsync(string command, int threadId, CancellationToken ct)
    {
        await RequestAsync(command, new ThreadArguments(threadId), ct).ConfigureAwait(false);
        LeaveStop();
        SetState(DebugState.Running);
        _events.Raise(() => Continued);
        return true;
    }

    /// <summary>Completes on acknowledgement; a no-op while stopped (DBG-405). Uses any known thread (DBG-091).</summary>
    private async Task<bool> PauseCoreAsync(CancellationToken ct)
    {
        if (State == DebugState.Stopped)
            return true;
        if (_threads.Count == 0)
            _threads = DapJson.Deserialize<ThreadsResponseBody>(await RequestAsync("threads", null, ct).ConfigureAwait(false)).Threads;
        await RequestAsync("pause", new ThreadArguments(_threads.FirstOrDefault()?.Id ?? 0), ct).ConfigureAwait(false);
        return true;
    }

    // ---- inspection ----

    private async Task<IReadOnlyList<ThreadInfo>> GetThreadsCoreAsync(CancellationToken ct)
    {
        if (!_threadsAreOfThisStop)
        {
            _threads = DapJson.Deserialize<ThreadsResponseBody>(await RequestAsync("threads", null, ct).ConfigureAwait(false)).Threads;
            _threadsAreOfThisStop = true;
        }

        var current = _currentStop!.ThreadId;
        return [.. _threads.Select(t => new ThreadInfo(t.Id, string.IsNullOrEmpty(t.Name) ? Messages.ThreadName(t.Id) : t.Name, t.Id == current))];
    }

    private async Task<StackTraceResult> GetStackTraceCoreAsync(int threadId, int startFrame, int maxFrames, CancellationToken ct)
    {
        startFrame = Math.Max(startFrame, 0);
        // levels 0 ("all") is never sent; the cap is 1000 (DBG-073).
        var levels = maxFrames <= 0 ? MaxStackLevels : Math.Min(maxFrames, MaxStackLevels);
        var response = DapJson.Deserialize<StackTraceResponseBody>(await RequestAsync(
            "stackTrace", new StackTraceArguments(threadId, startFrame, levels), ct).ConfigureAwait(false));
        var frames = response.StackFrames.Select((frame, index) => MapFrame(frame, startFrame + index, register: true)).ToList();
        // DBG-076: without totalFrames, exactly "levels" frames signal "maybe more".
        var total = response.TotalFrames ?? startFrame + frames.Count + (frames.Count == levels ? 1 : 0);
        return new StackTraceResult(frames, total);
    }

    /// <summary>Maps an adapter frame (DBG-074 … DBG-077). With <paramref name="register"/> the frame gets an id of the current stop.</summary>
    private StackFrameInfo MapFrame(DapStackFrame frame, int index, bool register)
    {
        SourceLocation? location = null;
        if (frame.Source is { Path: { Length: > 0 } path } source && (source.SourceReference ?? 0) == 0
            && Path.IsPathFullyQualified(path) && !path.Contains('`') && frame.Line > 0)
            location = new SourceLocation(PathText.Normalize(path), frame.Line, Math.Max(frame.Column, 1));

        var instructionPointer = ParseAddress(frame.InstructionPointerReference);
        // DBG-075: the routine of the type information that contains the frame's line, else the adapter's name.
        var name = location is not null && _typeInfo?.FindRoutine(location.Path, location.Line) is { } routine
            ? routine.DisplayName
            : string.IsNullOrEmpty(frame.Name)
                ? "$" + instructionPointer.ToString("X16", CultureInfo.InvariantCulture)
                : frame.Name;
        var roots = _options.UserSourceRoots;
        var isUserCode = location is not null && (roots.Count == 0 || roots.Any(root => PathText.IsUnder(location.Path, root)));
        var module = DapJson.IdText(frame.ModuleId) is { } moduleId && _modules.TryGetValue(moduleId, out var fileName) ? fileName : "";

        var id = 0;
        if (register)
        {
            id = ++_lastFrameId;
            _frames.Add(id, frame.Id);
        }

        return new StackFrameInfo(id, index, name, location, isUserCode, instructionPointer, module);
    }

    private static ulong ParseAddress(string? text)
    {
        if (text is null)
            return 0;
        var digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text.AsSpan(2) : text.AsSpan();
        return ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    /// <summary>The adapter's frame id for one of our frame ids of the current stop (session loop, or a test while stopped).</summary>
    internal bool TryGetAdapterFrame(int frameId, out int adapterFrameId) => _frames.TryGetValue(frameId, out adapterFrameId);

    /// <summary>Faults with <c>StaleReference</c> for a frame id that does not belong to the current stop (DBG-408).</summary>
    private int RequireFrame(int frameId) =>
        TryGetAdapterFrame(frameId, out var adapterFrameId)
            ? adapterFrameId
            : throw new DebuggerException(DebugErrorCode.StaleReference, Messages.StaleFrame);
}
