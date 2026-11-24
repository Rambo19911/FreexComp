using FreexComp.Debugger.Dap;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Session;

// Exception notification (features §6): the function breakpoint on the runtime's raise routine (the "raise hook"),
// reading the exception object at a hook stop, the ignore list, and the stops the adapter reports for
// operating-system exceptions. The rules that need no debuggee are in ExceptionMonitor.
internal sealed partial class DebugSession
{
    private const int RegisterGroupLimit = 4;

    /// <summary>
    /// A raised exception object: its dynamic class, message and address, and the class followed by its known
    /// ancestors (what the ignore list is compared with, DBG-244).
    /// </summary>
    private readonly record struct RaisedException(string ClassName, string Message, ulong Address, IReadOnlyList<string> Classes)
    {
        public static RaisedException Unknown(ulong address) => new("", "", address, []);
    }

    // Session loop only.
    private ExceptionSettings _exceptionSettings = new();
    private bool _hookArmed;                    // the adapter holds the function breakpoints on the raise hooks
    private IReadOnlyList<int> _raiseHookBreakpointIds = [];
    private bool _hookUnavailableReported;
    // Threads whose last stop was a raise seen through the hook, published or ignored (DBG-248). A thread leaves the
    // set with its own next stop, or when it ends: the stop of another thread says nothing about it.
    private readonly HashSet<int> _raiseReportedThreads = [];
    private uint? _fatalCode;                   // the current public stop is an exception no handler takes (DBG-247)

    /// <summary>
    /// DBG-016: stored; from the configuration phase on, a change of what the adapter must hold is sent at once.
    /// </summary>
    private async Task<bool> SetExceptionSettingsCoreAsync(ExceptionSettings settings, CancellationToken ct)
    {
        _exceptionSettings = settings;
        if (State is DebugState.Running or DebugState.Stopped || (State == DebugState.Starting && _initializedProcessed))
            await SyncRaiseHookAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Makes the adapter's function breakpoints match the settings (DBG-241; DAP usage §4.2): the raise hook when
    /// language exceptions are to be reported, none otherwise. Nothing is sent when nothing changes. Without the
    /// adapter capability the notification is off and that is said once.
    /// </summary>
    /// <remarks>
    /// Every module links its own run-time library and so has its own raise routine. The breakpoint is set by name,
    /// once per distinct name among the modules with type information; the adapter places it in every loaded module
    /// that defines the name and in those loaded later, and removes it from one that is unloaded.
    /// </remarks>
    private async Task SyncRaiseHookAsync(CancellationToken ct)
    {
        var wanted = _exceptionSettings.BreakOnLanguageExceptions && _hookSymbols.Count > 0;
        if (wanted && !HasCapability("supportsFunctionBreakpoints"))
        {
            if (!_hookUnavailableReported)
                _events.Raise(() => Output, new OutputEventArgs(OutputCategory.Debugger, Messages.ExceptionNotificationUnavailable));
            _hookUnavailableReported = true;
            return;
        }

        if (wanted == _hookArmed)
            return;
        var arguments = new SetFunctionBreakpointsArguments(wanted ? [.. _hookSymbols.Select(symbol => new DapFunctionBreakpoint(symbol))] : []);
        var response = DapJson.Deserialize<SetBreakpointsResponseBody>(
            await RequestAsync("setFunctionBreakpoints", arguments, ct).ConfigureAwait(false));
        _hookArmed = wanted;
        _raiseHookBreakpointIds = wanted ? [.. response.Breakpoints.Select(breakpoint => breakpoint.Id).OfType<int>()] : [];
    }

    /// <summary>
    /// DBG-070: a breakpoint stop is the raise hook when the adapter names the hook's breakpoint, or (see
    /// <see cref="IsHookFrame"/>) when the top frame is the hook routine; either suffices.
    /// </summary>
    private bool IsRaiseHookBreakpoint(StoppedEventBody body) =>
        _hookArmed && body.HitBreakpointIds is { } hit && hit.Any(_raiseHookBreakpointIds.Contains);

    /// <summary>The frame is in the raise routine: by the adapter's name for it, or by its place in the type information.</summary>
    private bool IsHookFrame(DapStackFrame raw) =>
        _hookSymbols.Contains(raw.Name, StringComparer.OrdinalIgnoreCase) || HookRoutineOf(raw) is not null;

    /// <summary>
    /// The raise routine the frame runs, from the type information (DBG-134: on the lines of an outlined finally part
    /// the adapter's name of the frame decides between the part and its routine); null when it runs another routine.
    /// </summary>
    private PdiRoutine? HookRoutineOf(DapStackFrame raw) =>
        FrameRoutines.RoutineOf(_typeInfo, raw) is { } routine && _hookSymbols.Contains(routine.LinkageName, StringComparer.Ordinal)
            ? routine
            : null;

    /// <summary>
    /// DBG-242, DBG-243: the exception object of a frame of the raise routine, its dynamic class and its message.
    /// (1) Through the type information: the routine's first parameter in that frame. (2) With
    /// <paramref name="registers"/> (the frame is frame 0, stopped at the routine's start): the first argument
    /// register. Neither works: empty class name and message.
    /// </summary>
    private async Task<RaisedException> ReadExceptionAsync(DapStackFrame frame, bool registers)
    {
        var adapterFrameId = frame.Id;
        // The routine of the frame; for a frame the adapter gives no source for, any module's (they are alike).
        var hook = HookRoutineOf(frame)
            ?? _typeInfo?.Units.SelectMany(unit => unit.Routines).FirstOrDefault(routine => _hookSymbols.Contains(routine.LinkageName, StringComparer.Ordinal));
        ulong? instance = null;
        if (_typeInfo is { } pdi
            && hook?.Variables.FirstOrDefault(v => v.Kind is PdiVariableKind.Param or PdiVariableKind.VarParam) is { } parameter
            && await AddressOfCoreAsync(adapterFrameId, parameter.Name, CancellationToken.None).ConfigureAwait(false) is { } slot
            && await Memory.ReadAsync(slot, pdi.Abi.PointerSize, CancellationToken.None).ConfigureAwait(false) is { } pointer)
            instance = ValueFormatter.Unsigned(pointer);

        if (instance is null && registers)
            instance = await ReadRegisterAsync(adapterFrameId, "rcx").ConfigureAwait(false);
        if (instance is not { } address || address == 0 || _typeInfo is null)
            return RaisedException.Unknown(instance ?? 0);

        var (className, message, classes) = await new ValueDecoder(_typeInfo, Debuggee, _options.Format)
            .ReadExceptionAsync(address, CancellationToken.None).ConfigureAwait(false);
        return new RaisedException(className, message, address, classes);
    }

    /// <summary>
    /// A register of a frame from the adapter's Registers scope (name compared case-insensitively, value parsed as
    /// hex). The pinned adapter lists register groups under the scope: the first few groups are looked into as well.
    /// </summary>
    private async Task<ulong?> ReadRegisterAsync(int adapterFrameId, string name)
    {
        static ulong? Find(IReadOnlyList<RawVariable> variables, string name) =>
            variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } register
                ? FirstHexToken(register.Value)
                : null;

        try
        {
            var scopes = await GetAdapterScopesAsync(adapterFrameId, CancellationToken.None).ConfigureAwait(false);
            var scope = scopes.FirstOrDefault(s => string.Equals(s.PresentationHint, "registers", StringComparison.OrdinalIgnoreCase))
                ?? scopes.FirstOrDefault(s => string.IsNullOrEmpty(s.PresentationHint) && s.Name.Equals("registers", StringComparison.OrdinalIgnoreCase));
            if (scope is null)
                return null;
            var listed = await GetRawChildrenCoreAsync(scope.VariablesReference, CancellationToken.None).ConfigureAwait(false);
            if (Find(listed, name) is { } value)
                return value;
            foreach (var group in listed.Where(v => v.Reference > 0).Take(RegisterGroupLimit))
            {
                if (Find(await GetRawChildrenCoreAsync(group.Reference, CancellationToken.None).ConfigureAwait(false), name) is { } inGroup)
                    return inGroup;
            }
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.RequestFailed)
        {
            // The adapter has no registers for the frame.
        }

        return null;
    }

    /// <summary>DBG-245: the first user frame below the raise routine and the runtime's helpers; none: the first frame with source.</summary>
    private static SourceLocation? LocationBelow(IReadOnlyList<StackFrameInfo> frames, int hookIndex)
    {
        var below = frames.Skip(hookIndex + 1).ToList();
        return (below.FirstOrDefault(f => f.IsUserCode) ?? below.FirstOrDefault(f => f.Location is not null))?.Location;
    }

    /// <summary>
    /// The user resumes from an exception that no handler takes. The adapter would resume with the exception masked,
    /// so the debuggee is ended instead; its exit code is the exception code (DBG-247).
    /// </summary>
    private async Task EndAfterFatalExceptionAsync(uint code)
    {
        _exitCode = unchecked((int)code);
        await ShutdownAsync(TerminationCause.Exited, terminateDebuggee: true).ConfigureAwait(false);
    }
}
