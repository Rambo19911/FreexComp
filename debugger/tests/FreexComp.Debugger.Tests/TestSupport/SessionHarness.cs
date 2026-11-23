using System.Diagnostics;
using System.Text.Json;
using FreexComp.Debugger.Session;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>An append-only list on which tests wait for an item (no polling: waiters are completed by <see cref="Add"/>).</summary>
internal sealed class Watch<T>
{
    private readonly Lock _gate = new();
    private readonly List<T> _items = [];
    private readonly List<(Func<T, bool> Match, TaskCompletionSource<T> Found)> _waiters = [];

    public void Add(T item)
    {
        List<TaskCompletionSource<T>> found = [];
        lock (_gate)
        {
            _items.Add(item);
            foreach (var waiter in _waiters.Where(w => w.Match(item)).ToList())
            {
                _waiters.Remove(waiter);
                found.Add(waiter.Found);
            }
        }

        foreach (var waiter in found)
            waiter.TrySetResult(item);
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
            return [.. _items];
    }

    /// <summary>The first item (already present or still to come) that matches.</summary>
    public async Task<T> WaitAsync(Func<T, bool> match)
    {
        var found = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            foreach (var item in _items)
            {
                if (match(item))
                    return item;
            }

            _waiters.Add((match, found));
        }

        return await found.Task.WaitAsync(FakeAdapterRunner.Safety, TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// A real <see cref="DebugSession"/> over the FakeAdapter helper exe with a scenario. Records every public event as
/// one text line and every traced protocol message. On dispose the session is disposed, the adapter process must be
/// gone (process-leak check) and the scenario directory is deleted.
/// </summary>
internal sealed class SessionHarness : IAsyncDisposable
{
    public static readonly TimeSpan Safety = FakeAdapterRunner.Safety;

    private readonly string _scenarioPath;

    private SessionHarness(string scenarioPath, DebugSession session, ManualTimeProvider? clock, Watch<DapTraceEntry> trace)
    {
        _scenarioPath = scenarioPath;
        Session = session;
        Clock = clock;
        Trace = trace;

        session.StateChanged += (_, e) => Events.Add($"state {e.OldState}->{e.NewState}");
        session.Stopped += (_, e) => Events.Add(
            $"stopped {e.Reason} epoch={e.Epoch} thread={e.ThreadId} loc={Location(e.Location)} hits=[{string.Join(",", e.HitBreakpointIds)}] desc={e.Description}");
        session.Continued += (_, _) => Events.Add("continued");
        session.Output += (_, e) => Events.Add($"output {e.Category} {JsonSerializer.Serialize(e.Text)}");
        session.BreakpointChanged += (_, e) => Events.Add("breakpoint " + Status(e.Status));
        session.ThreadChanged += (_, e) => Events.Add($"thread {e.ThreadId} {e.Change}");
        session.ModuleLoaded += (_, e) => Events.Add($"module {e.Name}|{e.Path}|{e.HasSymbols}");
        session.Terminated += (_, e) => Events.Add($"terminated {e.Cause} {e.ExitCode?.ToString() ?? "null"} {e.Message ?? "null"}");
    }

    public DebugSession Session { get; }

    /// <summary>Non-null when the session runs on a manual clock.</summary>
    public ManualTimeProvider? Clock { get; }

    /// <summary>Every public event, in delivery order, one line each.</summary>
    public Watch<string> Events { get; } = new();

    /// <summary>Every traced protocol message and adapter stderr line.</summary>
    public Watch<DapTraceEntry> Trace { get; }

    /// <summary>An existing file that serves as the debuggee path (the fake adapter never starts it).</summary>
    public static string Program => FakeAdapterRunner.ExePath;

    public static string Location(SourceLocation? location) =>
        location is null ? "null" : $"{location.Path}:{location.Line}:{location.Column}";

    public static string Status(BreakpointStatus status) =>
        $"{status.Id} {status.State} line={status.ActualLine} hits={status.HitCount} msg={status.Message ?? "null"}";

    /// <param name="scenarioJson">FakeAdapter scenario.</param>
    /// <param name="manualClock">Run the session on a clock that only the test advances.</param>
    /// <param name="configure">Changes to the session options.</param>
    public static SessionHarness Create(
        string scenarioJson, bool manualClock = false, Func<DebugSessionOptions, DebugSessionOptions>? configure = null)
    {
        var scenarioPath = FakeAdapterRunner.WriteScenario(scenarioJson);
        var trace = new Watch<DapTraceEntry>();
        var options = new DebugSessionOptions
        {
            AdapterPath = FakeAdapterRunner.ExePath,
            AdapterArguments = [scenarioPath],
            Trace = trace.Add,
        };
        options = configure?.Invoke(options) ?? options;
        var clock = manualClock ? new ManualTimeProvider() : null;
        var session = clock is null ? new DebugSession(options) : new DebugSession(options, clock);
        return new SessionHarness(scenarioPath, session, clock, trace);
    }

    /// <summary>
    /// The default request launches <see cref="Program"/> with the demo program's type information; a test about a
    /// program without type information passes its own request.
    /// </summary>
    public Task LaunchAsync(LaunchRequest? request = null) =>
        Session.LaunchAsync(request ?? new LaunchRequest { Program = Program, TypeInfoPath = PdiFixture.DbgDemoPath },
            TestContext.Current.CancellationToken).WaitAsync(Safety);

    public Task<string> WaitForEventAsync(string prefix) => Events.WaitAsync(e => e.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Waits for a request sent to the adapter and returns its JSON text.</summary>
    public async Task<string> WaitForRequestAsync(string command) =>
        (await Trace.WaitAsync(e => e.Direction == DapDirection.ToAdapter && e.Json.Contains($"\"type\":\"request\",\"command\":\"{command}\"", StringComparison.Ordinal))).Json;

    /// <summary>Waits for a response received from the adapter.</summary>
    public async Task<string> WaitForResponseAsync(string command) =>
        (await Trace.WaitAsync(e => e.Direction == DapDirection.FromAdapter && e.Json.Contains("\"type\":\"response\"", StringComparison.Ordinal)
            && e.Json.Contains($"\"command\":\"{command}\"", StringComparison.Ordinal))).Json;

    /// <summary>Waits until the adapter has answered <paramref name="count"/> requests of this command.</summary>
    public async Task WaitForResponsesAsync(string command, int count)
    {
        var seen = 0;
        await Trace.WaitAsync(e => e.Direction == DapDirection.FromAdapter && e.Json.Contains("\"type\":\"response\"", StringComparison.Ordinal)
            && e.Json.Contains($"\"command\":\"{command}\"", StringComparison.Ordinal) && ++seen == count);
    }

    /// <summary>The commands of all requests sent so far, in order.</summary>
    public IReadOnlyList<string> SentCommands() =>
        [.. SentRequests().Select(json => JsonDocument.Parse(json).RootElement.GetProperty("command").GetString()!)];

    /// <summary>The JSON text of all requests sent so far (optionally of one command), in order.</summary>
    public IReadOnlyList<string> SentRequests(string? command = null) =>
        [.. Trace.Snapshot()
            .Where(e => e.Direction == DapDirection.ToAdapter && e.Json.Contains("\"type\":\"request\"", StringComparison.Ordinal))
            .Select(e => e.Json)
            .Where(json => command is null || json.Contains($"\"command\":\"{command}\"", StringComparison.Ordinal))];

    /// <summary>The <c>arguments</c> object text of the only request of this command.</summary>
    public string SentArguments(string command)
    {
        var json = Assert.Single(SentRequests(command));
        return JsonDocument.Parse(json).RootElement.GetProperty("arguments").GetRawText();
    }

    public IReadOnlyList<string> StderrLines() =>
        [.. Trace.Snapshot().Where(e => e.Direction == DapDirection.AdapterStderr).Select(e => e.Json)];

    /// <summary>The adapter consumed every scenario step and met no unexpected request (valid once the session ended).</summary>
    public void AssertScenarioComplete() => Assert.Contains("SCENARIO-COMPLETE", StderrLines());

    public int? AdapterProcessId => ((IDebugSessionDiagnostics)Session).AdapterProcessId;

    public static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var adapter = AdapterProcessId;
        try
        {
            await Session.DisposeAsync().AsTask().WaitAsync(Safety);
        }
        finally
        {
            var leaked = adapter is { } processId && IsAlive(processId);
            if (leaked)
            {
                using var process = Process.GetProcessById(adapter!.Value);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(Safety);
            }

            Directory.Delete(Path.GetDirectoryName(_scenarioPath)!, recursive: true);
            Assert.False(leaked, $"The session left its adapter process {adapter} running.");
        }
    }
}
