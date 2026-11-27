namespace FreexComp.Debugger;

/// <summary>Creates debug sessions.</summary>
public interface IDebugSessionFactory
{
    /// <summary>Creates an idle session. Never throws for a missing adapter; that is reported by the launch.</summary>
    IDebugSession Create(DebugSessionOptions options);
}

/// <summary>One debuggee process under one adapter process. Single-use: a terminated session cannot be relaunched.</summary>
public interface IDebugSession : IAsyncDisposable
{
    // ---- state (thread-safe snapshots) ----
    DebugState State { get; }
    StopInfo? CurrentStop { get; }
    int? ProcessId { get; }
    int? ExitCode { get; }
    bool HasTypeInfo { get; }

    // ---- lifetime ----
    Task LaunchAsync(LaunchRequest request, CancellationToken ct = default);
    Task TerminateAsync(CancellationToken ct = default);

    // ---- execution control ----
    Task ContinueAsync(CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task StepOverAsync(int threadId, CancellationToken ct = default);
    Task StepIntoAsync(int threadId, CancellationToken ct = default);
    Task StepOutAsync(int threadId, CancellationToken ct = default);
    Task RunToLineAsync(string path, int line, CancellationToken ct = default);

    // ---- breakpoints ----
    /// <summary>Replaces the complete breakpoint set of one source file.</summary>
    Task<IReadOnlyList<BreakpointStatus>> SetBreakpointsAsync(
        string path, IReadOnlyList<BreakpointSpec> breakpoints, CancellationToken ct = default);
    Task SetExceptionSettingsAsync(ExceptionSettings settings, CancellationToken ct = default);

    // ---- inspection ----
    Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken ct = default);
    Task<StackTraceResult> GetStackTraceAsync(int threadId, int startFrame = 0, int maxFrames = 200,
                                              CancellationToken ct = default);
    Task<IReadOnlyList<VariableScope>> GetScopesAsync(int frameId, CancellationToken ct = default);
    Task<IReadOnlyList<VariableInfo>> GetChildrenAsync(VariableHandle handle, int start = 0,
                                                       int count = 0, CancellationToken ct = default);
    Task<EvaluateResult> EvaluateAsync(string expression, int? frameId, EvaluateContext context,
                                       CancellationToken ct = default);
    /// <summary>Assigns the value of a Pascal expression to the node identified by <paramref name="evaluateName"/>.</summary>
    Task<VariableInfo> SetValueAsync(string evaluateName, string valueExpression, int? frameId,
                                     CancellationToken ct = default);
    Task<byte[]> ReadMemoryAsync(ulong address, int count, CancellationToken ct = default);

    /// <summary>Kind, length, reference count, code page, encoding and layout of the text behind <see cref="VariableInfo.Text"/>.</summary>
    Task<TextDetails> GetTextDetailsAsync(VariableHandle text, CancellationToken ct = default);

    /// <summary>One chunk of at most 65 536 elements from <paramref name="start"/>; continue at <see cref="TextChunk.Next"/> until <see cref="TextChunk.AtEnd"/>.</summary>
    Task<TextChunk> ReadTextAsync(VariableHandle text, long start, int count, CancellationToken ct = default);

    // ---- events (raised serially on the session's dispatcher thread) ----
    event EventHandler<StateChangedEventArgs>? StateChanged;
    event EventHandler<StopInfo>? Stopped;
    event EventHandler? Continued;
    event EventHandler<OutputEventArgs>? Output;
    event EventHandler<BreakpointChangedEventArgs>? BreakpointChanged;
    event EventHandler<ThreadChangedEventArgs>? ThreadChanged;
    event EventHandler<ModuleEventArgs>? ModuleLoaded;
    event EventHandler<TerminatedEventArgs>? Terminated;
}
