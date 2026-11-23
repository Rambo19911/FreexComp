namespace FreexComp.Debugger;

/// <summary>Public state of a session.</summary>
public enum DebugState { Idle, Starting, Running, Stopped, Terminating, Terminated }

/// <summary>Why the debuggee stopped.</summary>
public enum StopReason { Entry, Breakpoint, Step, Pause, Exception, RunToLine }

/// <summary>One public stop. <paramref name="Epoch"/> increases by 1 at every stop; handles are valid for one epoch.</summary>
public sealed record StopInfo(
    int Epoch,
    StopReason Reason,
    int ThreadId,
    SourceLocation? Location,
    IReadOnlyList<int> HitBreakpointIds,
    ExceptionInfo? Exception,
    string Description);

/// <summary>A source position: absolute path, 1-based line and column.</summary>
public sealed record SourceLocation(string Path, int Line, int Column);

/// <summary>Origin of an exception stop.</summary>
public enum ExceptionKind { Language, Os }

/// <summary>Details of an exception stop.</summary>
public sealed record ExceptionInfo(
    ExceptionKind Kind,
    string ClassName,
    string Message,
    uint OsCode,
    ulong ObjectAddress);

/// <summary>Which exceptions stop the debuggee.</summary>
public sealed record ExceptionSettings
{
    /// <summary>Stop when the program raises a language exception.</summary>
    public bool BreakOnLanguageExceptions { get; init; } = true;

    /// <summary>Stop on operating-system exceptions.</summary>
    public bool BreakOnOsExceptions { get; init; } = true;

    /// <summary>Class names (case-insensitive, exact match, no inheritance) that never stop.</summary>
    public IReadOnlyList<string> IgnoredClasses { get; init; } = ["EAbort"];
}
