using System.Text;
using System.Text.Json;
using FreexComp.Debugger;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// The debug session factory of tests (testing.md §5.4): real sessions of <c>FreexComp.Debugger</c> over the debugger
/// area's scripted adapter, <c>FakeAdapter.exe</c> next to the test assembly, which gets the scenario file as its
/// argument (<c>DebugSessionOptions.AdapterArguments</c>; no process-wide environment, so tests run in parallel).
/// Without a scenario the options pass through unchanged — the adapter path the IDE asked for, which does not exist in a
/// test. The options as the IDE gave them and every traced protocol message are recorded. A scripted session's request
/// and launch limits are the test's safety net (<see cref="TestDeadline.DebugTestTimeout"/>, I-258).
/// </summary>
public sealed class FakeAdapterSessions : IDebugSessionFactory, IDisposable
{
    private readonly DebugSessionFactory _inner = new();
    private readonly Lock _gate = new();
    private readonly List<DapTraceEntry> _trace = [];
    private readonly List<(Func<DapTraceEntry, bool> Match, TaskCompletionSource<DapTraceEntry> Found)> _waiters = [];
    private readonly List<string> _directories = [];

    public static string AdapterExe => Path.Combine(AppContext.BaseDirectory, "FakeAdapter.exe");

    /// <summary>The scenario file of the next session; null = no fake adapter (see the class remarks).</summary>
    public string? ScenarioPath { get; private set; }

    /// <summary>The options of every session the IDE asked for, as it gave them.</summary>
    public List<DebugSessionOptions> Requested { get; } = [];

    /// <summary>The options each session was created with, in order (those of <see cref="Requested"/> as the factory changed them).</summary>
    public List<DebugSessionOptions> Created { get; } = [];

    /// <summary>The sessions created, in order.</summary>
    public List<IDebugSession> Sessions { get; } = [];

    public IDebugSession Last => Sessions[^1];

    /// <summary>Writes <paramref name="scenarioJson"/> to a temp file; the sessions created from now on run it.</summary>
    public void UseScenario(string scenarioJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        ScenarioPath = Path.Combine(directory, "scenario.json");
        File.WriteAllText(ScenarioPath, scenarioJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// The scenario file <c>TestData/debug-scenarios/&lt;name&gt;</c> next to the test assembly. With
    /// <paramref name="values"/> a copy runs in which every <c>${token}</c> is replaced by its value as JSON string content
    /// (the paths of the test's own files, which a file cannot know).
    /// </summary>
    public void UseScenarioFile(string name, params (string Token, string Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "debug-scenarios", name);
        if (values.Length == 0)
        {
            ScenarioPath = path;
            return;
        }

        var text = File.ReadAllText(path, Encoding.UTF8);
        foreach (var (token, value) in values)
            text = text.Replace("${" + token + "}", JsonSerializer.Serialize(value)[1..^1], StringComparison.Ordinal);
        UseScenario(text);
    }

    /// <summary>
    /// The disconnect wait of the sessions created from now on; null = the test's net. F14-047: after a refused reverse
    /// request the scripted adapter answers nothing, <c>disconnect</c> neither, as the pinned adapter; such a test sets
    /// <see cref="TimeSpan.Zero"/>, so the session closes the adapter's input at once and it exits.
    /// </summary>
    public TimeSpan? DisconnectWait { get; set; }

    /// <summary>The sessions created from now on get the options unchanged.</summary>
    public void UseNoAdapter() => ScenarioPath = null;

    public IDebugSession Create(DebugSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Requested.Add(options);
        // I-258: the product's wall-clock limits (10 s per request, 30 s for the launch) are not what a scripted session
        // is about, and a machine that runs every test project at once does not keep them (the adapter is a process to
        // start); the test's own timeout is the only bound. G-089 / I-303: so are the shutdown waits; a starved
        // scripted adapter would otherwise be killed before it writes its last error-stream line.
        var net = TimeSpan.FromMilliseconds(TestDeadline.DebugTestTimeout);
        var actual = ScenarioPath is { } scenario
            ? options with { AdapterPath = AdapterExe, AdapterArguments = [scenario], Trace = Record, RequestTimeout = net, LaunchTimeout = net,
                ShutdownWaits = new ShutdownWaits { Disconnect = DisconnectWait ?? net, AdapterExit = net, StderrEnd = net, TerminalClose = net } }
            : options with { Trace = Record };
        Created.Add(actual);
        var session = _inner.Create(actual);
        Sessions.Add(session);
        return session;
    }

    /// <summary>The JSON text of every request sent to the adapter so far (optionally of one command), in order.</summary>
    public IReadOnlyList<string> SentRequests(string? command = null)
    {
        lock (_gate)
        {
            return [.. _trace.Where(e => e.Direction == DapDirection.ToAdapter && IsRequest(e.Json, command)).Select(e => e.Json)];
        }
    }

    /// <summary>The <c>arguments</c> of every request of <paramref name="command"/> sent so far, as JSON text.</summary>
    public IReadOnlyList<string> SentArguments(string command) =>
        [.. SentRequests(command).Select(json => JsonDocument.Parse(json).RootElement.GetProperty("arguments").GetRawText())];

    /// <summary>The JSON text of every response the session sent to a reverse request of <paramref name="command"/> (F14-047), in order.</summary>
    public IReadOnlyList<string> SentResponses(string command)
    {
        lock (_gate)
        {
            return [.. _trace.Where(e => e.Direction == DapDirection.ToAdapter && e.Json.Contains("\"type\":\"response\"", StringComparison.Ordinal)
                && e.Json.Contains($"\"command\":\"{command}\"", StringComparison.Ordinal)).Select(e => e.Json)];
        }
    }

    /// <summary>What the adapter wrote to its error stream so far, one entry per line.</summary>
    public IReadOnlyList<string> AdapterStderr()
    {
        lock (_gate)
            return [.. _trace.Where(e => e.Direction == DapDirection.AdapterStderr).Select(e => e.Json)];
    }

    /// <summary>
    /// I-301: completes when the adapter has written <paramref name="line"/> to its error stream for the
    /// <paramref name="count"/>-th time (lines already written count; I-303: one per session of the test). No polling:
    /// completed by the trace callback. The session reads that stream on a thread of its own and stops waiting for its end
    /// after <see cref="ShutdownWaits.StderrEnd"/> (lifted to the test's net here, 2 s in the product), so a line (the
    /// scripted adapter's <c>SCENARIO-COMPLETE</c>) can arrive after the session has ended and the IDE is idle again.
    /// </summary>
    public Task WaitForStderrLineAsync(string line, int count = 1)
    {
        bool Match(DapTraceEntry e) => e.Direction == DapDirection.AdapterStderr && e.Json == line;
        var found = new TaskCompletionSource<DapTraceEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            var seen = _trace.Count(Match);
            if (seen >= count)
                return Task.CompletedTask;
            _waiters.Add((e => Match(e) && ++seen >= count, found));
        }

        return found.Task;
    }

    /// <summary>
    /// Completes when the adapter has received the <paramref name="count"/>-th request of <paramref name="command"/>
    /// (already sent ones count). No polling: completed by the trace callback.
    /// </summary>
    public Task WaitForRequestAsync(string command, int count = 1)
    {
        var found = new TaskCompletionSource<DapTraceEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            var seen = _trace.Count(e => e.Direction == DapDirection.ToAdapter && IsRequest(e.Json, command));
            if (seen >= count)
                return Task.CompletedTask;
            _waiters.Add((e => e.Direction == DapDirection.ToAdapter && IsRequest(e.Json, command) && ++seen >= count, found));
        }

        return found.Task;
    }

    public void Dispose()
    {
        foreach (var directory in _directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left for the OS temp cleanup; never hides the test's own result.
            }
        }
    }

    private static bool IsRequest(string json, string? command) =>
        json.Contains("\"type\":\"request\"", StringComparison.Ordinal)
        && (command is null || json.Contains($"\"command\":\"{command}\"", StringComparison.Ordinal));

    private void Record(DapTraceEntry entry)
    {
        List<TaskCompletionSource<DapTraceEntry>> found = [];
        lock (_gate)
        {
            _trace.Add(entry);
            foreach (var waiter in _waiters.Where(w => w.Match(entry)).ToList())
            {
                _waiters.Remove(waiter);
                found.Add(waiter.Found);
            }
        }

        foreach (var waiter in found)
            waiter.TrySetResult(entry);
    }
}
