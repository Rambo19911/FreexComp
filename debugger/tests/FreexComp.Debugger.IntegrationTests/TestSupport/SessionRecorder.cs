using System.Text;
using System.Threading.Channels;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;
using Xunit.Sdk;

namespace FreexComp.Debugger.IntegrationTests.TestSupport;

/// <summary>
/// Records what a session against the real adapter does (scripted test DBG-303): every public event in order, every
/// protocol message, and the debuggee's standard output. Waits are on events with a time limit as safety net only.
/// When a test fails, <see cref="RunAsync"/> writes everything to a file and names it in the failure (DBG-304).
/// </summary>
internal sealed class SessionRecorder
{
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly string _name;
    private readonly List<string> _events = [];
    private readonly List<DapTraceEntry> _trace = [];
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly List<OutputEventArgs> _outputs = [];
    private readonly List<(string Expected, TaskCompletionSource Reached)> _stdoutWaiters = [];
    // Stops and the end of the session, in order: what the script waits for.
    private readonly Channel<object> _milestones = Channel.CreateUnbounded<object>();

    /// <param name="name">Name of the test: the name of the trace file written on failure.</param>
    public SessionRecorder(string name) => _name = name;

    /// <summary>The trace sink for <see cref="DebugSessionOptions.Trace"/>.</summary>
    public void Trace(DapTraceEntry entry)
    {
        lock (_gate)
            _trace.Add(entry);
    }

    public void Attach(IDebugSession session)
    {
        session.StateChanged += (_, e) => Add($"StateChanged({e.OldState}→{e.NewState})");
        session.Stopped += (_, e) =>
        {
            Add($"Stopped({e.Reason}, epoch {e.Epoch}, thread {e.ThreadId}, {(e.Location is null ? "no location" : e.Location.Path + ":" + e.Location.Line)}, "
                + $"hits [{string.Join(",", e.HitBreakpointIds)}], {e.Description})");
            _milestones.Writer.TryWrite(e);
        };
        session.Continued += (_, _) => Add("Continued");
        session.Output += (_, e) =>
        {
            Add($"Output({e.Category}, {System.Text.Json.JsonSerializer.Serialize(e.Text)})");
            lock (_gate)
                _outputs.Add(e);
            if (e.Category == OutputCategory.Stdout)
                OnStdout(e.Text);
            else if (e.Category == OutputCategory.Stderr)
                lock (_gate)
                    _stderr.Append(e.Text);
        };
        session.BreakpointChanged += (_, e) =>
        {
            Add($"BreakpointChanged({e.Status.Id}, {e.Status.State}, line {e.Status.ActualLine}, hits {e.Status.HitCount}, {e.Status.Message ?? "-"})");
            lock (_gate)
            {
                _breakpoints[e.Status.Id] = e.Status;
                _statusHistory.Add(e.Status);
            }
        };
        session.ThreadChanged += (_, e) => Add($"ThreadChanged({e.ThreadId}, {e.Change})");
        session.ModuleLoaded += (_, e) => Add($"ModuleLoaded({e.Name}, {e.Path}, symbols {e.HasSymbols})");
        session.Terminated += (_, e) =>
        {
            Add($"Terminated({e.Cause}, exit code {e.ExitCode?.ToString() ?? "none"}, {e.Message ?? "-"})");
            _milestones.Writer.TryWrite(e);
        };
    }

    private readonly Dictionary<int, BreakpointStatus> _breakpoints = [];
    private readonly List<BreakpointStatus> _statusHistory = [];

    /// <summary>Every event so far, one line each, in delivery order.</summary>
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_gate)
                return [.. _events];
        }
    }

    /// <summary>Forgets the events recorded so far: a script step that asserts exactly what follows starts here.</summary>
    public void ClearEvents()
    {
        lock (_gate)
            _events.Clear();
    }

    /// <summary>The texts of all output of one category so far, concatenated, line ends normalised to LF.</summary>
    public string OutputSoFar(OutputCategory category)
    {
        lock (_gate)
            return string.Concat(_outputs.Where(o => o.Category == category).Select(o => o.Text)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>Every status reported for a breakpoint through <c>BreakpointChanged</c>, in order.</summary>
    public IReadOnlyList<BreakpointStatus> StatusHistory(int breakpointId)
    {
        lock (_gate)
            return [.. _statusHistory.Where(s => s.Id == breakpointId)];
    }

    /// <summary>The last status reported for a breakpoint through <c>BreakpointChanged</c>; null when none was.</summary>
    public BreakpointStatus? LastStatus(int breakpointId)
    {
        lock (_gate)
            return _breakpoints.GetValueOrDefault(breakpointId);
    }

    /// <summary>All standard output of the debuggee so far, line ends normalised to LF.</summary>
    public string StdoutSoFar
    {
        get
        {
            lock (_gate)
                return _stdout.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }
    }

    public string StderrSoFar
    {
        get
        {
            lock (_gate)
                return _stderr.ToString();
        }
    }

    /// <summary>The next stop. Fails when the session ends instead, or when nothing happens within the limit.</summary>
    public async Task<StopInfo> WaitForStopAsync(TimeSpan? timeout = null) =>
        await NextMilestoneAsync("a stop", timeout).ConfigureAwait(false) as StopInfo
        ?? throw new XunitException("The session ended while the test waited for a stop.");

    /// <summary>The end of the session. Fails when the debuggee stops instead, or when nothing happens within the limit.</summary>
    public async Task<TerminatedEventArgs> WaitForTerminatedAsync(TimeSpan? timeout = null) =>
        await NextMilestoneAsync("the end of the session", timeout).ConfigureAwait(false) as TerminatedEventArgs
        ?? throw new XunitException("The debuggee stopped while the test waited for the end of the session.");

    /// <summary>The next stop (<see cref="StopInfo"/>) or the end of the session (<see cref="TerminatedEventArgs"/>), whichever comes.</summary>
    public Task<object> WaitForMilestoneAsync(TimeSpan? timeout = null) => NextMilestoneAsync("a stop or the end of the session", timeout);

    private async Task<object> NextMilestoneAsync(string what, TimeSpan? timeout)
    {
        try
        {
            return await _milestones.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(timeout ?? IntegrationFixture.Safety).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new XunitException($"Nothing happened while the test waited for {what} ({(timeout ?? IntegrationFixture.Safety).TotalSeconds} s).");
        }
    }

    /// <summary>Completes when the debuggee's standard output so far equals <paramref name="expected"/> (LF line ends).</summary>
    public async Task WaitForStdoutAsync(string expected, TimeSpan? timeout = null)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stdout.ToString().Replace("\r\n", "\n", StringComparison.Ordinal) == expected)
                return;
            _stdoutWaiters.Add((expected, reached));
        }

        try
        {
            await reached.Task.WaitAsync(timeout ?? IntegrationFixture.Safety, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Assert.Equal(expected, StdoutSoFar);
        }
    }

    private void OnStdout(string text)
    {
        List<TaskCompletionSource> reached = [];
        lock (_gate)
        {
            _stdout.Append(text);
            var soFar = _stdout.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach (var waiter in _stdoutWaiters.Where(w => w.Expected == soFar).ToList())
            {
                _stdoutWaiters.Remove(waiter);
                reached.Add(waiter.Reached);
            }
        }

        foreach (var waiter in reached)
            waiter.TrySetResult();
    }

    private void Add(string line)
    {
        lock (_gate)
            _events.Add(line);
    }

    /// <summary>
    /// Runs a scripted session on a fresh session object and always disposes it. Afterwards the adapter process and
    /// everything it started must be gone (DBG-305). A failure is reported together with the path of a file that
    /// holds the event list and the complete protocol trace.
    /// </summary>
    public async Task RunAsync(IntegrationFixture fixture, BuiltProgram program, Func<IDebugSession, Task> script)
    {
        var session = new DebugSessionFactory().Create(new DebugSessionOptions
        {
            AdapterPath = fixture.AdapterPath,
            UserSourceRoots = [program.Directory],
            Trace = Trace,
        });
        Attach(session);
        Exception? failure = null;
        try
        {
            await script(session).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            failure = e;
        }

        // Whatever the script did, nothing may survive the session: the adapter, what it started (its debug server,
        // the debuggee) and the debuggee by its process id are put on the lists of the leak checks before disposal.
        var started = new ProcessRegistry();
        if (((IDebugSessionDiagnostics)session).AdapterProcessId is { } adapterId)
        {
            started.TrackTree(adapterId, "debug adapter of " + _name);
            ProcessLeakFixture.Registry.TrackTree(adapterId, "debug adapter of " + _name);
        }

        if (session.ProcessId is { } debuggeeId)
        {
            started.Track(debuggeeId, "debuggee of " + _name);
            ProcessLeakFixture.Registry.Track(debuggeeId, "debuggee of " + _name);
        }

        try
        {
            await session.DisposeAsync().AsTask().WaitAsync(IntegrationFixture.Safety).ConfigureAwait(false);
        }
        catch (Exception e) when (failure is null)
        {
            failure = e;
        }

        // Disposal closes the job object that holds the adapter's process tree; the processes end right after. The
        // check waits for their exit (DBG-305), the limit being a safety net.
        var leaked = started.KillLeaked(ExitGrace).Concat(fixture.KillRunningBuiltPrograms(ExitGrace)).ToList();
        if (leaked.Count > 0 && failure is null)
            failure = new XunitException("Processes left running after the session was disposed (killed now): " + string.Join("; ", leaked));

        if (failure is not null)
            throw new XunitException($"{failure.Message}\nEvents and protocol trace: {Dump()}", failure);
    }

    /// <summary>Writes the event list and the protocol trace to <c>TestResults/&lt;test&gt;.trace.txt</c> and returns the path.</summary>
    public string Dump()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, _name + ".trace.txt");
        var text = new StringBuilder();
        lock (_gate)
        {
            text.Append("== events ==\n");
            foreach (var line in _events)
                text.Append(line).Append('\n');
            text.Append("\n== protocol trace ==\n");
            foreach (var entry in _trace)
            {
                var direction = entry.Direction switch
                {
                    DapDirection.ToAdapter => "-->",
                    DapDirection.FromAdapter => "<--",
                    _ => "err",
                };
                text.Append(entry.Time.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(' ').Append(direction).Append(' ').Append(entry.Json).Append('\n');
            }
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
