using System.Collections.Concurrent;
using FreexComp.Debugger.Session;

namespace FreexComp.Debugger.Tests;

/// <summary>State machine and session-loop skeleton (architecture §5, §6; debug-session DBG-401, DBG-406, DBG-421, DBG-422).</summary>
public sealed class SessionStateTests
{
    /// <summary>Safety net only: every wait normally completes in milliseconds.</summary>
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(60);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DebugSessionOptions Options = new() { AdapterPath = @"C:\nowhere\lldb-dap.exe" };

    /// <summary>
    /// The table of architecture §5 ("Allowed public calls"), written out per state; every method not listed is not
    /// allowed in that state.
    /// </summary>
    private static readonly Dictionary<DebugState, string[]> Allowed = new()
    {
        [DebugState.Idle] = ["LaunchAsync", "SetBreakpointsAsync", "SetExceptionSettingsAsync", "TerminateAsync"],
        [DebugState.Starting] = ["SetBreakpointsAsync", "SetExceptionSettingsAsync", "TerminateAsync"],
        [DebugState.Running] = ["PauseAsync", "SetBreakpointsAsync", "SetExceptionSettingsAsync", "TerminateAsync"],
        // "everything except LaunchAsync"
        [DebugState.Stopped] =
        [
            "TerminateAsync", "ContinueAsync", "PauseAsync", "StepOverAsync", "StepIntoAsync", "StepOutAsync",
            "RunToLineAsync", "SetBreakpointsAsync", "SetExceptionSettingsAsync", "GetThreadsAsync",
            "GetStackTraceAsync", "GetScopesAsync", "GetChildrenAsync", "EvaluateAsync", "SetValueAsync", "ReadMemoryAsync",
        ],
        [DebugState.Terminating] = ["TerminateAsync"],
        [DebugState.Terminated] = ["TerminateAsync"],
    };

    /// <summary>Every task-returning method of the public interface, by name.</summary>
    private static readonly Dictionary<string, Func<IDebugSession, Task>> Calls = new()
    {
        ["LaunchAsync"] = s => s.LaunchAsync(new LaunchRequest { Program = @"C:\nowhere\p.exe" }, Ct),
        ["TerminateAsync"] = s => s.TerminateAsync(Ct),
        ["ContinueAsync"] = s => s.ContinueAsync(Ct),
        ["PauseAsync"] = s => s.PauseAsync(Ct),
        ["StepOverAsync"] = s => s.StepOverAsync(1, Ct),
        ["StepIntoAsync"] = s => s.StepIntoAsync(1, Ct),
        ["StepOutAsync"] = s => s.StepOutAsync(1, Ct),
        ["RunToLineAsync"] = s => s.RunToLineAsync(@"C:\nowhere\p.pas", 3, Ct),
        ["SetBreakpointsAsync"] = s => s.SetBreakpointsAsync(@"C:\nowhere\p.pas", [], Ct),
        ["SetExceptionSettingsAsync"] = s => s.SetExceptionSettingsAsync(new ExceptionSettings(), Ct),
        ["GetThreadsAsync"] = s => s.GetThreadsAsync(Ct),
        ["GetStackTraceAsync"] = s => s.GetStackTraceAsync(1, ct: Ct),
        ["GetScopesAsync"] = s => s.GetScopesAsync(1, Ct),
        ["GetChildrenAsync"] = s => s.GetChildrenAsync(new VariableHandle(1, 1), ct: Ct),
        ["EvaluateAsync"] = s => s.EvaluateAsync("1", null, EvaluateContext.Watch, Ct),
        ["SetValueAsync"] = s => s.SetValueAsync("X", "1", null, Ct),
        ["ReadMemoryAsync"] = s => s.ReadMemoryAsync(0x10000, 4, Ct),
    };

    public static TheoryData<DebugState, string, bool> StateTable()
    {
        var data = new TheoryData<DebugState, string, bool>();
        foreach (var state in Enum.GetValues<DebugState>())
        {
            foreach (var method in Calls.Keys)
                data.Add(state, method, Allowed[state].Contains(method));
        }

        return data;
    }

    [Fact]
    public void StateTable_CoversEveryInterfaceMethod()
    {
        var interfaceMethods = typeof(IDebugSession).GetMethods()
            .Where(m => !m.IsSpecialName && typeof(Task).IsAssignableFrom(m.ReturnType))
            .Select(m => m.Name);

        Assert.Equal(interfaceMethods.Order(), Calls.Keys.Order());
        Assert.Equal(Calls.Keys.Order(), Enum.GetNames<SessionMethod>().Order());
        Assert.Equal(6 * 17, StateTable().Count);
    }

    [Theory]
    [MemberData(nameof(StateTable))]
    [Trait("Req", "DBG-401")]
    public async Task Method_InWrongState_Throws(DebugState state, string method, bool allowed)
    {
        Assert.Equal(allowed, SessionStateMachine.IsAllowed(state, Enum.Parse<SessionMethod>(method)));
        if (allowed)
            return;   // what an allowed call does is covered by the tests of its own task

        var events = new ConcurrentQueue<string>();
        await using var session = new DebugSession(Options, state);
        session.StateChanged += (_, e) => events.Enqueue("state " + e.NewState);

        var task = Calls[method](session);   // returns a task; never throws synchronously

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => task.WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.InvalidState, exception.Code);
        Assert.Equal($"{method} is not allowed in state {state}.", exception.Message);
        Assert.Equal(state, session.State);
        // Nothing was started and nothing was sent: there is no adapter process at all.
        Assert.Null(((IDebugSessionDiagnostics)session).AdapterProcessId);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(DebugState.Idle, new[] { DebugState.Starting, DebugState.Terminated })]
    [InlineData(DebugState.Starting, new[] { DebugState.Running, DebugState.Stopped, DebugState.Terminating, DebugState.Terminated })]
    [InlineData(DebugState.Running, new[] { DebugState.Stopped, DebugState.Terminating, DebugState.Terminated })]
    [InlineData(DebugState.Stopped, new[] { DebugState.Running, DebugState.Terminating, DebugState.Terminated })]
    [InlineData(DebugState.Terminating, new[] { DebugState.Terminated })]
    [InlineData(DebugState.Terminated, new DebugState[0])]
    public void Transition_OnlyAlongTheDiagram(DebugState from, DebugState[] legal)
    {
        foreach (var to in Enum.GetValues<DebugState>())
        {
            var machine = new SessionStateMachine(from);
            if (legal.Contains(to))
            {
                Assert.Equal(from, machine.Transition(to));
                Assert.Equal(to, machine.State);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => machine.Transition(to));
                Assert.Equal(from, machine.State);
            }
        }
    }

    [Fact]
    public async Task Snapshot_InitialValues()
    {
        await using var session = new DebugSessionFactory().Create(Options);

        Assert.IsType<DebugSession>(session);
        Assert.Equal(DebugState.Idle, session.State);
        Assert.Null(session.CurrentStop);
        Assert.Null(session.ProcessId);
        Assert.Null(session.ExitCode);
        Assert.False(session.HasTypeInfo);
        var diagnostics = (IDebugSessionDiagnostics)session;
        Assert.Null(diagnostics.AdapterProcessId);
        Assert.Equal(0, diagnostics.InternalStopCount);
    }

    [Fact]
    [Trait("Req", "DBG-406")]
    [Trait("Req", "DBG-421")]
    [Trait("Req", "DBG-422")]
    public async Task Terminate_InIdle_MovesToTerminated_EventsInOrder()
    {
        await using var session = new DebugSessionFactory().Create(Options);
        var log = new ConcurrentQueue<string>();
        var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The snapshot already holds the new state when the handlers run.
        session.StateChanged += (sender, e) => log.Enqueue($"state {e.OldState}->{e.NewState} (State={((IDebugSession)sender!).State}) on {Thread.CurrentThread.Name}");
        session.Terminated += (sender, e) =>
        {
            log.Enqueue($"terminated {e.Cause} {e.ExitCode?.ToString() ?? "null"} {e.Message ?? "null"} (State={((IDebugSession)sender!).State})");
            terminated.SetResult();
        };

        await session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await terminated.Task.WaitAsync(Safety, Ct);

        Assert.Equal(DebugState.Terminated, session.State);
        Assert.Equal(
        [
            "state Idle->Terminated (State=Terminated) on FreexComp.Debugger.Events",
            "terminated TerminatedByUser null null (State=Terminated)",
        ], log);

        // A second call is a no-op: no further events, and the session stays terminated.
        await session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        var exception = await Assert.ThrowsAsync<DebuggerException>(() => session.LaunchAsync(new LaunchRequest { Program = @"C:\p.exe" }, Ct));
        Assert.Equal(DebugErrorCode.InvalidState, exception.Code);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    [Trait("Req", "DBG-407")]
    public async Task Dispose_InIdle_TerminatesWithCauseDisposed_ThenCallsThrow()
    {
        var session = new DebugSessionFactory().Create(Options);
        var log = new ConcurrentQueue<string>();
        session.StateChanged += (_, e) => log.Enqueue($"state {e.OldState}->{e.NewState}");
        session.Terminated += (_, e) => log.Enqueue($"terminated {e.Cause}");

        await session.DisposeAsync();

        // Both events were delivered before DisposeAsync returned; nothing is raised afterwards.
        Assert.Equal(["state Idle->Terminated", "terminated Disposed"], log);
        Assert.Equal(DebugState.Terminated, session.State);
        foreach (var call in Calls.Values)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => call(session));
        await session.DisposeAsync();   // idempotent
        Assert.Equal(2, log.Count);
    }

    [Fact]
    [Trait("Req", "DBG-414")]
    public async Task Call_WithCancelledToken_FaultsOperationCanceled()
    {
        await using var session = new DebugSessionFactory().Create(Options);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.TerminateAsync(new CancellationToken(canceled: true)));

        Assert.Equal(DebugState.Idle, session.State);
    }
}
