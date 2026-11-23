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
    private readonly Dictionary<int, FrameEntry> _frames = [];
    private int _lastFrameId;

    // What the stop resolver remembers between stops (architecture §7).
    private StepState? _step;                 // the user's step in progress
    private int _stopDepth;                   // frames on the stopping thread's stack at the current public stop
    private RunToLineState? _runToLine;       // a run to a line in progress
    private volatile int _internalStops;

    /// <summary>A run to a line: the file of the temporary breakpoint, or the user breakpoint that stands in for it.</summary>
    private sealed record RunToLineState(BreakpointManager.Source Source, BreakpointManager.Entry? Bypass);

    // ---- stop pipeline ----

    private async Task OnStoppedAsync(StoppedEventBody body)
    {
        if (State is not (DebugState.Starting or DebugState.Running))
            return;

        StopInfo? stop;
        try
        {
            stop = await ResolveStopAsync(body).ConfigureAwait(false);
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.AdapterCrashed)
        {
            return;   // the "adapter closed" work item follows
        }
        catch (DebuggerException e)
        {
            // A stop is never lost (DBG-028).
            _stopDepth = 0;
            stop = new StopInfo(_epoch + 1, StopReason.Pause, body.ThreadId ?? 0, null, [], null, Messages.StopDetailsUnavailable(e.Message));
        }
        catch (OperationCanceledException)
        {
            return;   // the session is ending
        }

        if (stop is null)
            return;   // resolved internally: no public event, the state stays as it is (DBG-425)

        // A run to a line ends at the first public stop of any kind; its temporary breakpoint goes before the stop
        // is published (DBG-231).
        try
        {
            await EndRunToLineAsync().ConfigureAwait(false);
        }
        catch (DebuggerException e) when (e.Code != DebugErrorCode.AdapterCrashed)
        {
            // The breakpoint could not be removed; the stop is published all the same.
        }
        catch (Exception e) when (e is DebuggerException or OperationCanceledException)
        {
            return;
        }

        // Public stop: epoch++, snapshot, StateChanged immediately before Stopped (DBG-015, DBG-421, DBG-422).
        _step = null;
        _values = null;   // handles are numbered anew in the new epoch
        _epoch = stop.Epoch;
        _currentStop = stop;
        SetState(DebugState.Stopped);
        _events.Raise(() => Stopped, stop);
        CompleteLaunch();
    }

    /// <summary>
    /// Steps 1-3 of the pipeline: <c>threads</c>, <c>stackTrace</c> of the stopping thread, classification. Returns the
    /// public stop, or null when the stop was resolved internally (the debuggee has been resumed).
    /// </summary>
    private async Task<StopInfo?> ResolveStopAsync(StoppedEventBody body)
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
        _stopTopFrame = response.StackFrames.Count > 0 ? new FrameEntry(response.StackFrames[0].Id, frames[0].Location) : null;
        // Top user frame; when there is none, the first frame that has source.
        var location = (frames.FirstOrDefault(f => f.IsUserCode) ?? frames.FirstOrDefault(f => f.Location is not null))?.Location;
        var depth = response.TotalFrames ?? frames.Count;

        StopInfo Public(StopReason reason, IReadOnlyList<int> hit, string description)
        {
            _stopDepth = depth;
            return new StopInfo(_epoch + 1, reason, threadId, location, hit, null, description);
        }

        switch (body.Reason)
        {
            case "entry" when _launchRequest is { StopOnEntry: true }:
                return Public(StopReason.Entry, [], Messages.StoppedAtEntry);

            case "breakpoint" or "function breakpoint":
            {
                var ids = body.HitBreakpointIds ?? [];
                var hit = ids.Select(id => _breakpoints.FindByAdapterId(id)).OfType<BreakpointManager.Entry>().Distinct().ToList();
                var temporaryHit = ids.Any(id => _breakpoints.FindTemporary(id) is not null);
                // No ids from the adapter: match the top frame against the bound lines (DBG-071).
                if (hit.Count == 0 && !temporaryHit && frames.FirstOrDefault()?.Location is { } top)
                {
                    hit = [.. _breakpoints.FindByLocation(top.Path, top.Line)];
                    temporaryHit = _runToLine?.Source is { Temporary: { State: BreakpointState.Verified } temporary } source
                        && source.Path.Equals(top.Path, StringComparison.OrdinalIgnoreCase) && temporary.ActualLine == top.Line;
                }

                if (_runToLine?.Bypass is { } bypass && hit.Contains(bypass))
                {
                    // The user's breakpoint on the target line of a run to that line: the arrival is counted, its
                    // condition and hit count are bypassed for this one run (DBG-065).
                    bypass.HitCount++;
                    _events.Raise(() => BreakpointChanged, new BreakpointChangedEventArgs(bypass.Status));
                    return Public(StopReason.RunToLine, [], Messages.RunToLineCompleted);
                }

                if (hit.Count > 0)
                {
                    // Conditions, hit counts and log messages are ours (features §2.2), evaluated in the top frame.
                    var frame = _stopTopFrame is { } topFrame ? ContextOf(topFrame) : null;
                    var outcome = await BreakpointManager.EvaluateArrivalAsync(
                        hit,
                        condition => frame is null
                            ? throw new DebuggerException(DebugErrorCode.ExpressionEvaluation, Messages.NotAvailable)
                            : Values.EvaluateConditionAsync(condition, frame, CancellationToken.None),
                        expression => frame is null
                            ? throw new DebuggerException(DebugErrorCode.ExpressionEvaluation, Messages.NotAvailable)
                            : Values.EvaluateLogTextAsync(expression, frame, CancellationToken.None)).ConfigureAwait(false);
                    foreach (var output in outcome.Outputs)
                        _events.Raise(() => Output, output);
                    foreach (var entry in hit.OrderBy(e => e.Spec.Id))
                        _events.Raise(() => BreakpointChanged, new BreakpointChangedEventArgs(entry.Status));
                    if (outcome.Stoppers.Count > 0)
                    {
                        return Public(StopReason.Breakpoint, [.. outcome.Stoppers.Select(e => e.Spec.Id)],
                            outcome.Description
                            ?? (location is null ? Messages.BreakpointHit : Messages.BreakpointAt(Path.GetFileName(location.Path), location.Line)));
                    }
                }

                if (temporaryHit)
                    return Public(StopReason.RunToLine, [], Messages.RunToLineCompleted);
                if (hit.Count == 0)
                    break;   // none of ours: a plain pause

                // Every breakpoint skipped: continue, or go on with the user's step (DBG-030).
                var (command, resumeThread) = StopResolver.ResumeAfterSkip(_step, threadId, depth);
                await ResumeInternallyAsync(command, resumeThread).ConfigureAwait(false);
                return null;
            }

            case "step":
                // With user source roots, a step does not end in code that is not the user's (DBG-222).
                if (_step is { } step && _options.UserSourceRoots.Count > 0 && UserCodeFilter.Decide(step, frames) is { } internalCommand)
                {
                    if (internalCommand == StopResolver.Continue)
                        _step = null;
                    await ResumeInternallyAsync(internalCommand, step.ThreadId).ConfigureAwait(false);
                    return null;
                }

                return Public(StopReason.Step, [], Messages.StepCompleted);
        }

        var description = body.Reason is "pause" or "breakpoint" or "function breakpoint" || isInterrupt
            ? Messages.Paused
            : FirstNonEmpty(body.Description, body.Text) ?? Messages.Paused;
        return Public(StopReason.Pause, [], description);
    }

    /// <summary>
    /// Resumes the debuggee after a stop the session resolved itself. What was read during the stop is dropped; no
    /// public event is raised and the public state does not change (DBG-425).
    /// </summary>
    private async Task ResumeInternallyAsync(string command, int threadId)
    {
        _internalStops++;
        _values = null;
        _stopTopFrame = null;
        _threadsAreOfThisStop = false;
        DropDebuggeeCaches();
        await RequestAsync(command, new ThreadArguments(threadId), CancellationToken.None).ConfigureAwait(false);
    }

    private static string? FirstNonEmpty(params string?[] texts) => texts.FirstOrDefault(t => !string.IsNullOrEmpty(t));

    /// <summary>The state leaves <c>Stopped</c>: the snapshot and everything valid for one stop only is dropped.</summary>
    private void LeaveStop()
    {
        _currentStop = null;
        _frames.Clear();
        _threadsAreOfThisStop = false;
        _stopTopFrame = null;
        _values = null;
        DropDebuggeeCaches();
    }

    // ---- execution control ----

    /// <summary>
    /// <c>continue</c>, <c>next</c>, <c>stepIn</c> or <c>stepOut</c>: after the adapter's acknowledgement the session is
    /// <c>Running</c> (DBG-090, DBG-017); a failure leaves the state unchanged. A step is remembered until the next
    /// public stop (DBG-030, DBG-222).
    /// </summary>
    private async Task<bool> ResumeAsync(string command, int threadId, CancellationToken ct)
    {
        var depth = threadId == _currentStop?.ThreadId ? _stopDepth : 0;
        await RequestAsync(command, new ThreadArguments(threadId), ct).ConfigureAwait(false);
        _step = command == StopResolver.Continue ? null : new StepState(command, threadId, depth);
        LeaveStop();
        SetState(DebugState.Running);
        _events.Raise(() => Continued);
        return true;
    }

    /// <summary>
    /// DBG-230: a temporary breakpoint on the line (none when an enabled user breakpoint already is there, DBG-065),
    /// then <c>continue</c>. A line without code leaves the session stopped and faults.
    /// </summary>
    private async Task<bool> RunToLineCoreAsync(string path, int line, CancellationToken ct)
    {
        var source = _breakpoints.GetOrAdd(PathText.Normalize(path));
        var noCode = new DebuggerException(DebugErrorCode.RequestFailed, Messages.NoCodeAt(Path.GetFileName(source.Path), line), "setBreakpoints");
        var existing = source.Entries.FirstOrDefault(e => e.IsSent && (e.Spec.Line == line || e.ActualLine == line));
        if (existing is not null)
        {
            if (existing.State != BreakpointState.Verified)
                throw noCode;
        }
        else
        {
            source.Temporary = new BreakpointManager.Entry(new BreakpointSpec { Id = 0, Line = line }, source.Path) { State = BreakpointState.Pending };
            try
            {
                await SendBreakpointsAsync(source, ct, raiseChanges: true).ConfigureAwait(false);
            }
            catch (Exception e) when (e is DebuggerException or OperationCanceledException)
            {
                source.Temporary = null;
                _breakpoints.DropIfEmpty(source);
                throw;
            }

            if (source.Temporary.State != BreakpointState.Verified)
            {
                await RemoveTemporaryBreakpointAsync(source, ct).ConfigureAwait(false);
                throw noCode;
            }
        }

        try
        {
            await RequestAsync(StopResolver.Continue, new ThreadArguments(_currentStop!.ThreadId), ct).ConfigureAwait(false);
        }
        catch (DebuggerException e) when (e.Code != DebugErrorCode.AdapterCrashed)
        {
            // Not resumed: the session stays stopped, without the temporary breakpoint.
            await RemoveTemporaryBreakpointAsync(source, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        _runToLine = new RunToLineState(source, existing);
        _step = null;
        LeaveStop();
        SetState(DebugState.Running);
        _events.Raise(() => Continued);
        return true;
    }

    /// <summary>The run to a line is over: its temporary breakpoint is removed from the adapter.</summary>
    private async Task EndRunToLineAsync()
    {
        if (_runToLine is not { } run)
            return;
        _runToLine = null;
        await RemoveTemporaryBreakpointAsync(run.Source, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RemoveTemporaryBreakpointAsync(BreakpointManager.Source source, CancellationToken ct)
    {
        if (source.Temporary is null)
            return;
        source.Temporary = null;
        try
        {
            await SendBreakpointsAsync(source, ct, raiseChanges: true).ConfigureAwait(false);
        }
        finally
        {
            _breakpoints.DropIfEmpty(source);
        }
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
            _frames.Add(id, new FrameEntry(frame.Id, location));
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
    internal bool TryGetAdapterFrame(int frameId, out int adapterFrameId)
    {
        var known = _frames.TryGetValue(frameId, out var frame);
        adapterFrameId = frame?.AdapterId ?? 0;
        return known;
    }

    /// <summary>Faults with <c>StaleReference</c> for a frame id that does not belong to the current stop (DBG-408).</summary>
    private FrameEntry RequireFrame(int frameId) =>
        _frames.TryGetValue(frameId, out var frame)
            ? frame
            : throw new DebuggerException(DebugErrorCode.StaleReference, Messages.StaleFrame);
}
