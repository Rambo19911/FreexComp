namespace FreexComp.Debugger.Session;

/// <summary>The task-returning methods of the public session interface (names equal the method names).</summary>
internal enum SessionMethod
{
    LaunchAsync, TerminateAsync,
    ContinueAsync, PauseAsync, StepOverAsync, StepIntoAsync, StepOutAsync, RunToLineAsync,
    SetBreakpointsAsync, SetExceptionSettingsAsync,
    GetThreadsAsync, GetStackTraceAsync, GetScopesAsync, GetChildrenAsync, EvaluateAsync, SetValueAsync, ReadMemoryAsync,
}

/// <summary>
/// The public state of a session (architecture §5): which calls each state allows and which transitions exist.
/// Written only by the session loop; <see cref="State"/> may be read from any thread.
/// </summary>
internal sealed class SessionStateMachine
{
    private volatile int _state;

    public SessionStateMachine(DebugState initial = DebugState.Idle) => _state = (int)initial;

    public DebugState State => (DebugState)_state;

    /// <summary>The "Allowed public calls" column of the state table.</summary>
    public static bool IsAllowed(DebugState state, SessionMethod method) => state switch
    {
        DebugState.Idle => method is SessionMethod.LaunchAsync or SessionMethod.SetBreakpointsAsync
            or SessionMethod.SetExceptionSettingsAsync or SessionMethod.TerminateAsync,
        DebugState.Starting => method is SessionMethod.SetBreakpointsAsync or SessionMethod.SetExceptionSettingsAsync
            or SessionMethod.TerminateAsync,
        DebugState.Running => method is SessionMethod.PauseAsync or SessionMethod.SetBreakpointsAsync
            or SessionMethod.SetExceptionSettingsAsync or SessionMethod.TerminateAsync,
        DebugState.Stopped => method is not SessionMethod.LaunchAsync,
        DebugState.Terminating or DebugState.Terminated => method is SessionMethod.TerminateAsync,
        _ => false,
    };

    /// <summary>Throws <see cref="DebuggerException"/> (<c>InvalidState</c>) when the current state does not allow the call.</summary>
    public void Require(SessionMethod method)
    {
        var state = State;
        if (!IsAllowed(state, method))
            throw new DebuggerException(DebugErrorCode.InvalidState, Messages.InvalidState(method.ToString(), state));
    }

    /// <summary>Moves to <paramref name="to"/> and returns the previous state; only the arrows of the state diagram exist.</summary>
    public DebugState Transition(DebugState to)
    {
        var from = State;
        var legal = from switch
        {
            DebugState.Idle => to is DebugState.Starting or DebugState.Terminated,
            DebugState.Starting => to is DebugState.Running or DebugState.Stopped or DebugState.Terminating or DebugState.Terminated,
            DebugState.Running => to is DebugState.Stopped or DebugState.Terminating or DebugState.Terminated,
            DebugState.Stopped => to is DebugState.Running or DebugState.Terminating or DebugState.Terminated,
            DebugState.Terminating => to is DebugState.Terminated,
            _ => false,
        };
        if (!legal)
            throw new InvalidOperationException($"Illegal session state transition {from} -> {to}.");
        _state = (int)to;
        return from;
    }
}
