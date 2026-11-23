namespace FreexComp.Debugger;

/// <summary>Settings of one debug session.</summary>
public sealed record DebugSessionOptions
{
    /// <summary>Absolute path of the debug adapter executable. The library performs no search.</summary>
    public required string AdapterPath { get; init; }

    /// <summary>
    /// Extra command-line arguments for the adapter process. Empty for the real adapter; tests pass a scenario file
    /// to the scripted test adapter. Sessions never set process-wide environment variables.
    /// </summary>
    public IReadOnlyList<string> AdapterArguments { get; init; } = [];

    /// <summary>Directories whose sources count as "user code" (project directory and search paths). Absolute.</summary>
    public IReadOnlyList<string> UserSourceRoots { get; init; } = [];

    /// <summary>Timeout for ordinary requests.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Timeout from the start of the launch until the debuggee runs or stops for the first time.</summary>
    public TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional protocol trace sink (every message, both directions). Called on arbitrary threads.</summary>
    public Action<DapTraceEntry>? Trace { get; init; }

    /// <summary>Value display options.</summary>
    public ValueFormatOptions Format { get; init; } = new();
}

/// <summary>One traced protocol message or adapter stderr line.</summary>
public sealed record DapTraceEntry(DateTimeOffset Time, DapDirection Direction, string Json);

/// <summary>Direction of a traced entry.</summary>
public enum DapDirection { ToAdapter, FromAdapter, AdapterStderr }

/// <summary>How values are rendered.</summary>
public sealed record ValueFormatOptions
{
    /// <summary>Render integers in hexadecimal.</summary>
    public bool HexIntegers { get; init; } = false;

    /// <summary>Characters of a string shown before truncation.</summary>
    public int MaxStringChars { get; init; } = 1024;

    /// <summary>Array or record elements in a one-line summary.</summary>
    public int MaxSummaryElements { get; init; } = 8;

    /// <summary>Children fetched per page.</summary>
    public int ChildPageSize { get; init; } = 100;
}

/// <summary>What to launch and how.</summary>
public sealed record LaunchRequest
{
    /// <summary>Absolute path of the executable.</summary>
    public required string Program { get; init; }

    /// <summary>Program arguments, one element per argument.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Working directory; null means the directory of <see cref="Program"/>.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Complete environment of the debuggee; null means inherit.</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>Stop at the program entry.</summary>
    public bool StopOnEntry { get; init; } = false;

    /// <summary>Initial breakpoints, one entry per source file.</summary>
    public IReadOnlyList<SourceBreakpoints> Breakpoints { get; init; } = [];

    /// <summary>Exception notification settings.</summary>
    public ExceptionSettings Exceptions { get; init; } = new();

    /// <summary>Type-information sidecar; null means the program path with the extension replaced by ".pdi".</summary>
    public string? TypeInfoPath { get; init; }
}
