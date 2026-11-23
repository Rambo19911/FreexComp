namespace FreexComp.Debugger;

/// <summary>Error categories of the debugger library (append-only).</summary>
public enum DebugErrorCode
{
    AdapterNotFound, AdapterStartFailed, AdapterCrashed, ProtocolError, MissingCapability,
    RequestTimeout, RequestFailed, InvalidState, StaleReference, ProgramNotFound, LaunchFailed,
    TypeInfoMissing, TypeInfoInvalid, ExpressionSyntax, ExpressionEvaluation, MemoryUnreadable,
    NotModifiable, Cancelled
}

/// <summary>Every failure the library reports to its caller.</summary>
public sealed class DebuggerException : Exception
{
    /// <summary>Error category.</summary>
    public DebugErrorCode Code { get; }

    /// <summary>The adapter request that failed, if any.</summary>
    public string? DapCommand { get; }

    public DebuggerException(DebugErrorCode code, string message, string? dapCommand = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        DapCommand = dapCommand;
    }
}
