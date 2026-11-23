using System.Text;
using System.Text.Json;
using FreexComp.Debugger;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// The debug session factory of tests (testing.md §5.4): real sessions of <c>FreexComp.Debugger</c> over the debugger
/// area's scripted adapter, <c>FakeAdapter.exe</c> next to the test assembly, which gets the scenario file as its
/// argument (<c>DebugSessionOptions.AdapterArguments</c>; no process-wide environment, so tests run in parallel).
/// Without a scenario the options pass through unchanged — the adapter path the IDE asked for, which does not exist in a
/// test. The options as the IDE gave them and every traced protocol message are recorded.
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

    /// <summary>The scenario file <c>TestData/debug-scenarios/&lt;name&gt;</c> next to the test assembly.</summary>
    public void UseScenarioFile(string name) => ScenarioPath = Path.Combine(AppContext.BaseDirectory, "TestData", "debug-scenarios", name);

    /// <summary>The sessions created from now on get the options unchanged.</summary>
    public void UseNoAdapter() => ScenarioPath = null;

    public IDebugSession Create(DebugSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Requested.Add(options);
        var actual = ScenarioPath is { } scenario
            ? options with { AdapterPath = AdapterExe, AdapterArguments = [scenario], Trace = Record }
            : options with { Trace = Record };
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

    /// <summary>What the adapter wrote to its error stream so far, one entry per line.</summary>
    public IReadOnlyList<string> AdapterStderr()
    {
        lock (_gate)
            return [.. _trace.Where(e => e.Direction == DapDirection.AdapterStderr).Select(e => e.Json)];
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
