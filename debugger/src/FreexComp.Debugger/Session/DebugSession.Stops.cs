using System.Globalization;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

// Stop pipeline (architecture §7), execution control (DAP usage §6), threads and stack trace (DAP usage §5.2, §5.3).
internal sealed partial class DebugSession
{
    private const int StopFrameCount = 20;
    private static readonly TimeSpan PauseWindow = TimeSpan.FromSeconds(5);
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
    private bool _entryStopSeen;              // the entry stop of a launch that goes on to the main block (DBG-225)
    private volatile int _internalStops;
    private volatile bool _pauseRequested;    // the user asked to pause and no stop has been published since (DBG-029)
    private DateTimeOffset? _pauseAcknowledged;   // when the adapter acknowledged our pause request (DBG-091)

    /// <summary>
    /// A run to a line: the file of the temporary breakpoint, or the id of the user breakpoint of that file that
    /// stands in for it - the id, not the table entry: the IDE may send the file's set again during the run (DBG-065).
    /// <paramref name="Reason"/> and <paramref name="Description"/> are those of the stop at the line: a run to the
    /// cursor, or the first step of a program (DBG-225).
    /// </summary>
    private sealed record RunToLineState(BreakpointManager.Source Source, int? BypassId, StopReason Reason, string Description);

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
        _pauseRequested = false;
        _pauseAcknowledged = null;
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
        _stopTopFrame = response.StackFrames.Count > 0 ? EntryOf(response.StackFrames, 0) : null;
        // Top user frame; when there is none, the first frame that has source.
        var location = (frames.FirstOrDefault(f => f.IsUserCode) ?? frames.FirstOrDefault(f => f.Location is not null))?.Location;
        var depth = response.TotalFrames ?? frames.Count;

        StopInfo Public(StopReason reason, IReadOnlyList<int> hit, string description, ExceptionInfo? exception = null, SourceLocation? at = null)
        {
            _stopDepth = depth;
            return new StopInfo(_epoch + 1, reason, threadId, exception is null ? location : at, hit, exception, description);
        }

        // Resumes after a stop the session resolves itself - unless the user asked to pause meanwhile: then the
        // debuggee stays stopped and this stop is published as the pause (DBG-029).
        async Task<StopInfo?> ResumeOrPauseAsync(string command, int resumeThread)
        {
            if (_pauseRequested)
                return Public(StopReason.Pause, [], Messages.Paused);
            await ResumeInternallyAsync(command, resumeThread).ConfigureAwait(false);
            return null;
        }

        StopInfo RaiseStop(SourceLocation? at, RaisedException raised) => Public(
            StopReason.Exception, [], Messages.ExceptionStop(raised.ClassName, raised.Message),
            new ExceptionInfo(ExceptionKind.Language, raised.ClassName, raised.Message, 0, raised.Address), at);

        // What the previous stop left behind for this one.
        var raiseReportedHere = _raiseReportedThreads.Remove(threadId);
        _fatalCode = null;

        // The raise hook (DBG-242 ... DBG-245): the exception object, then the ignore list.
        async Task<StopInfo?> RaiseHookStopAsync()
        {
            var raised = await ReadExceptionAsync(response.StackFrames[0].Id, frames[0].Location, registers: true).ConfigureAwait(false);
            _raiseReportedThreads.Add(threadId);
            if (ExceptionMonitor.IsIgnored(_exceptionSettings, raised.Classes))
            {
                // Resolved internally: continue, or go on with the user's step (DBG-244, DBG-030).
                var (command, resumeThread) = StopResolver.ResumeAfterSkip(_step, threadId, depth);
                return await ResumeOrPauseAsync(command, resumeThread).ConfigureAwait(false);
            }

            return RaiseStop(LocationBelow(frames, 0), raised);
        }

        // DBG-070: the adapter names the hook's breakpoint.
        if (body.Reason is "breakpoint" or "function breakpoint" && frames.Count > 0 && IsRaiseHookBreakpoint(body))
            return await RaiseHookStopAsync().ConfigureAwait(false);

        switch (body.Reason)
        {
            case "exception" when !isInterrupt:
            {
                // An exception that no handler takes: the adapter reports no others (DBG-247).
                var code = ExceptionMonitor.ParseCode(body.Description, body.Text);
                if (code == ExceptionMonitor.ThreadNameCode)
                {
                    var (command, resumeThread) = StopResolver.ResumeAfterSkip(_step, threadId, depth);
                    return await ResumeOrPauseAsync(command, resumeThread).ConfigureAwait(false);
                }

                if (code == ExceptionMonitor.BreakpointCode)
                    return Public(StopReason.Pause, [], Messages.HardCodedBreakpoint);

                if (code != 0 && code == _typeInfo?.Abi.PascalExceptionCode)
                {
                    if (raiseReportedHere)
                    {
                        // The hook already reported this raise (DBG-248): no second stop. Nothing handles it, and a
                        // resume would mask it, so the debuggee ends here with the code as its exit code.
                        _internalStops++;
                        await EndAfterFatalExceptionAsync(code).ConfigureAwait(false);
                        return null;
                    }

                    // DBG-246: not seen through the hook. The object is read from the frame of the raise routine when
                    // the type information describes it.
                    var hookIndex = Enumerable.Range(0, frames.Count).FirstOrDefault(i => IsHookFrame(response.StackFrames[i], frames[i]), -1);
                    var raised = hookIndex >= 0
                        ? await ReadExceptionAsync(response.StackFrames[hookIndex].Id, frames[hookIndex].Location, registers: false).ConfigureAwait(false)
                        : RaisedException.Unknown(0);
                    _fatalCode = code;
                    return RaiseStop(hookIndex >= 0 ? LocationBelow(frames, hookIndex) : location, raised);
                }

                var address = frames.Count > 0 ? frames[0].InstructionPointer : 0;
                var text = ExceptionMonitor.OsText(code);
                _fatalCode = code;
                return Public(StopReason.Exception, [], Messages.OsExceptionAt(text, address),
                    new ExceptionInfo(ExceptionKind.Os, "", text, code, address), location);
            }

            case "entry" when _launchRequest is { StopAtMainBody: true } && !_entryStopSeen:
                // DBG-225: on to the first statement of the main block; where that is not known, the entry stop is
                // the public stop.
                _entryStopSeen = true;
                if (await RunToMainBodyAsync(threadId).ConfigureAwait(false))
                    return null;
                return Public(StopReason.Entry, [], Messages.StoppedAtEntry);

            case "entry" when _launchRequest is { StopOnEntry: true } or { StopAtMainBody: true }:
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

                if (_runToLine is { BypassId: { } bypassId } run
                    && hit.FirstOrDefault(e => e.Spec.Id == bypassId && e.SourcePath.Equals(run.Source.Path, StringComparison.OrdinalIgnoreCase)) is { } bypass)
                {
                    // The user's breakpoint on the target line of a run to that line: the arrival is counted, its
                    // condition and hit count are bypassed for this one run (DBG-065).
                    bypass.HitCount++;
                    _events.Raise(() => BreakpointChanged, new BreakpointChangedEventArgs(bypass.Status));
                    return Public(run.Reason, [], run.Description);
                }

                if (hit.Count > 0)
                {
                    // Conditions, hit counts and log messages are ours (features §2.2), evaluated in the top frame.
                    var frame = _stopTopFrame is { } topFrame ? ContextOf(topFrame) : null;
                    var countsBefore = hit.Select(e => e.HitCount).ToList();
                    BreakpointManager.ArrivalOutcome outcome;
                    try
                    {
                        outcome = await BreakpointManager.EvaluateArrivalAsync(
                            hit,
                            condition => frame is null
                                ? throw new DebuggerException(DebugErrorCode.ExpressionEvaluation, Messages.NotAvailable)
                                : Values.EvaluateConditionAsync(condition, frame, CancellationToken.None),
                            expression => frame is null
                                ? throw new DebuggerException(DebugErrorCode.ExpressionEvaluation, Messages.NotAvailable)
                                : Values.EvaluateLogTextAsync(expression, frame, CancellationToken.None)).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is DebuggerException or OperationCanceledException)
                    {
                        // The adapter failed, not the expression (a timeout, say). The arrivals counted so far are
                        // reported all the same (DBG-410); the stop itself is the caller's to publish (DBG-028).
                        foreach (var counted in hit.Where((entry, index) => entry.HitCount != countsBefore[index]).OrderBy(entry => entry.Spec.Id))
                            _events.Raise(() => BreakpointChanged, new BreakpointChangedEventArgs(counted.Status));

                        throw;
                    }

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
                    return Public(_runToLine?.Reason ?? StopReason.RunToLine, [], _runToLine?.Description ?? Messages.RunToLineCompleted);
                // DBG-070: ... or the top frame is the hook routine (and the stop is at no line breakpoint of ours).
                if (hit.Count == 0 && _hookArmed && frames.Count > 0 && IsHookFrame(response.StackFrames[0], frames[0]))
                    return await RaiseHookStopAsync().ConfigureAwait(false);
                if (hit.Count == 0)
                    break;   // none of ours: a plain pause

                // Every breakpoint skipped: continue, or go on with the user's step (DBG-030).
                var (command, resumeThread) = StopResolver.ResumeAfterSkip(_step, threadId, depth);
                return await ResumeOrPauseAsync(command, resumeThread).ConfigureAwait(false);
            }

            case "step":
                // With user source roots, a step does not end in code that is not the user's (DBG-222). The same holds
                // when a library is debugged through its host: the host's frames without source are not the user's.
                if (_step is { } step && (_options.UserSourceRoots.Count > 0 || _launchRequest is { Libraries.Count: > 0 })
                    && UserCodeFilter.Decide(step, frames) is { } internalCommand)
                {
                    if (internalCommand == StopResolver.Continue)
                        _step = null;
                    return await ResumeOrPauseAsync(internalCommand, step.ThreadId).ConfigureAwait(false);
                }

                return Public(StopReason.Step, [], Messages.StepCompleted);
        }

        // DBG-091: any other stop within 5 s of our pause request, while no step is in progress, is the pause stop.
        var answersPause = _pauseAcknowledged is { } asked && _step is null && _time.GetUtcNow() - asked <= PauseWindow;
        var description = body.Reason is "pause" or "breakpoint" or "function breakpoint" || isInterrupt || answersPause
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
        if (_fatalCode is { } fatal)
        {
            await EndAfterFatalExceptionAsync(fatal).ConfigureAwait(false);
            return true;
        }

        // A step at the entry stop is the first step of the program (DBG-225): at its entry the program has no
        // statement to step from - the step would end in code that is not the user's and the program would run on to
        // its end (DBG-222) - so it runs to the first statement of the main block instead, where that is known.
        RunToLineState? toMainBody = null;
        if (command != StopResolver.Continue && _currentStop is { Reason: StopReason.Entry })
            toMainBody = await PlaceMainBodyTargetAsync(ct).ConfigureAwait(false);
        if (toMainBody is not null)
        {
            try
            {
                await RequestAsync(StopResolver.Continue, new ThreadArguments(threadId), ct).ConfigureAwait(false);
            }
            catch (DebuggerException e) when (e.Code != DebugErrorCode.AdapterCrashed)
            {
                // Not resumed: the session stays stopped, without the temporary breakpoint.
                await RemoveTemporaryBreakpointAsync(toMainBody.Source, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            _runToLine = toMainBody;
            _step = null;
            LeaveStop();
            SetState(DebugState.Running);
            _events.Raise(() => Continued);
            return true;
        }

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
        if (_fatalCode is { } fatal)
        {
            await EndAfterFatalExceptionAsync(fatal).ConfigureAwait(false);
            return true;
        }

        var (source, existing) = await PlaceRunTargetAsync(path, line, ct).ConfigureAwait(false);

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

        _runToLine = new RunToLineState(source, existing?.Spec.Id, StopReason.RunToLine, Messages.RunToLineCompleted);
        _step = null;
        LeaveStop();
        SetState(DebugState.Running);
        _events.Raise(() => Continued);
        return true;
    }

    /// <summary>
    /// The breakpoint a run to a line stops at: the enabled user breakpoint that already is on the line (returned), or
    /// else a temporary breakpoint, sent with the file's set (DBG-065). Faults when the line has no code; then no
    /// temporary breakpoint is left behind.
    /// </summary>
    private async Task<(BreakpointManager.Source Source, BreakpointManager.Entry? Existing)> PlaceRunTargetAsync(string path, int line, CancellationToken ct)
    {
        var source = _breakpoints.GetOrAdd(PathText.Normalize(path));
        var noCode = new DebuggerException(DebugErrorCode.RequestFailed, Messages.NoCodeAt(Path.GetFileName(source.Path), line), "setBreakpoints");
        var existing = source.Entries.FirstOrDefault(e => e.IsSent && (e.Spec.Line == line || e.ActualLine == line));
        if (existing is not null)
        {
            if (existing.State != BreakpointState.Verified)
                throw noCode;
            return (source, existing);
        }

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

        return (source, null);
    }

    /// <summary>
    /// DBG-225: the target of the first step of a program that stands at its entry - the first statement of the main
    /// block (the routine of the type information whose display name is the program's name). The stop there is
    /// published as a step stop; any other public stop that comes first ends the run like a run to a line (DBG-231).
    /// Null when the main block is not known or its line has no code: nothing is left behind.
    /// </summary>
    private async Task<RunToLineState?> PlaceMainBodyTargetAsync(CancellationToken ct)
    {
        if (_programInfo is not { } typeInfo
            || typeInfo.Units.SelectMany(unit => unit.Routines)
                .FirstOrDefault(routine => string.Equals(routine.DisplayName, typeInfo.Program, StringComparison.OrdinalIgnoreCase)) is not { } main
            || !Path.IsPathFullyQualified(main.File))
            return null;

        try
        {
            var (source, existing) = await PlaceRunTargetAsync(main.File, main.BodyLine, ct).ConfigureAwait(false);
            return new RunToLineState(source, existing?.Spec.Id, StopReason.Step, Messages.StepCompleted);
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.RequestFailed)
        {
            return null;   // no code on the line
        }
    }

    /// <summary>
    /// At the entry stop of a launch with <c>StopAtMainBody</c>: on to the main block, resumed internally - no public
    /// event, the state stays <c>Starting</c> (DBG-425). False when the main block cannot be run to: nothing was resumed.
    /// </summary>
    private async Task<bool> RunToMainBodyAsync(int threadId)
    {
        if (await PlaceMainBodyTargetAsync(CancellationToken.None).ConfigureAwait(false) is not { } run)
            return false;
        _runToLine = run;
        try
        {
            await ResumeInternallyAsync(StopResolver.Continue, threadId).ConfigureAwait(false);
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.RequestFailed)
        {
            // Not resumed: the debuggee is still at its entry.
            _internalStops--;
            await EndRunToLineAsync().ConfigureAwait(false);
            return false;
        }

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
        {
            _pauseRequested = false;   // also when the stop being resolved while the call waited was published for it (DBG-029)
            return true;
        }

        try
        {
            if (_threads.Count == 0)
                _threads = DapJson.Deserialize<ThreadsResponseBody>(await RequestAsync("threads", null, ct).ConfigureAwait(false)).Threads;
            await RequestAsync("pause", new ThreadArguments(_threads.FirstOrDefault()?.Id ?? 0), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is DebuggerException or OperationCanceledException)
        {
            _pauseRequested = false;
            throw;
        }

        _pauseAcknowledged = _time.GetUtcNow();
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
        for (var index = 0; index < frames.Count; index++)
            _frames.Add(frames[index].Id, EntryOf(response.StackFrames, index));
        // DBG-076: without totalFrames, exactly "levels" frames signal "maybe more".
        var total = response.TotalFrames ?? startFrame + frames.Count + (frames.Count == levels ? 1 : 0);
        return new StackTraceResult(frames, total);
    }

    /// <summary>Maps an adapter frame (DBG-074 … DBG-077). With <paramref name="register"/> the frame gets an id of the current stop.</summary>
    private StackFrameInfo MapFrame(DapStackFrame frame, int index, bool register)
    {
        var location = LocationOf(frame);
        var instructionPointer = ParseAddress(frame.InstructionPointerReference);
        // DBG-075: the routine of the type information that contains the frame's line, else the adapter's name.
        var name = RoutineOf(frame, location) is { } routine
            ? routine.DisplayName
            : string.IsNullOrEmpty(frame.Name)
                ? "$" + instructionPointer.ToString("X16", CultureInfo.InvariantCulture)
                : frame.Name;
        var roots = _options.UserSourceRoots;
        var isUserCode = location is not null && (roots.Count == 0 || roots.Any(root => PathText.IsUnder(location.Path, root)));
        var module = DapJson.IdText(frame.ModuleId) is { } moduleId && _modules.TryGetValue(moduleId, out var fileName) ? fileName : "";

        var id = 0;
        if (register)
            id = ++_lastFrameId;   // the entry is added by the caller, which has the whole stack
        return new StackFrameInfo(id, index, name, location, isUserCode, instructionPointer, module);
    }

    /// <summary>The source position of an adapter frame; null when it has no source file (DBG-074).</summary>
    private static SourceLocation? LocationOf(DapStackFrame frame) =>
        frame.Source is { Path: { Length: > 0 } path } source && (source.SourceReference ?? 0) == 0
        && Path.IsPathFullyQualified(path) && !path.Contains('`') && frame.Line > 0
            ? new SourceLocation(PathText.Normalize(path), frame.Line, Math.Max(frame.Column, 1))
            : null;

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
