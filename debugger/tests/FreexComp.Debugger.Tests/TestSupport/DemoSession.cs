namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// A session over the FakeAdapter that stands in the demo program's <c>Run</c> on line 71 with the memory of the
/// line-71 fixture: the adapter answers "address of" for the frame's variables and serves (and accepts) memory itself.
/// </summary>
internal static class DemoSession
{
    public const string Source = @"C:\work\DbgDemo\DbgDemo.pas";
    public const int ThreadId = 1;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public const string Threads = """{ "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } }""";

    /// <summary>A <c>stackTrace</c> step: the given routine on top of the main block.</summary>
    public static string Stack(int line, string routine = "Run", string? path = Source) => $$"""
        { "on": "stackTrace", "respond": { "totalFrames": 2, "stackFrames": [
            { "id": 524288, "name": "{{routine}}", "line": {{line}}, "column": 3{{SourceOf(path)}} },
            { "id": 524289, "name": "DbgDemo", "line": 77, "column": 3{{SourceOf(Source)}} } ] } }
        """;

    private static string SourceOf(string? path) =>
        path is null ? "" : $$""", "source": { "name": "{{Path.GetFileName(path)}}", "path": "{{path.Replace(@"\", @"\\")}}" }""";

    public static string StoppedEvent(string reason, string more = "") =>
        $$"""{ "event": "stopped", "body": { "reason": "{{reason}}", "threadId": 1{{more}} } }""";

    /// <summary>The scenario: the first stop (a step stop on line 71 unless <paramref name="firstStop"/> says otherwise), then <paramref name="steps"/>.</summary>
    public static string Scenario(string steps = "", string? capabilities = null, string? firstStop = null, string setup = "") => $$"""
        { {{(capabilities is null ? "" : "\"capabilities\": " + capabilities + ",")}}
          "variables": {{DemoMemory.VariablesJson}},
          "memory": {{DemoMemory.MemoryJson}},
          "steps": [
            {{setup}}
            { "on": "configurationDone", "respond": {}, "thenEvents": [ {{firstStop ?? StoppedEvent("step")}} ] },
            {{Threads}},
            {{Stack(71)}}{{(steps.Trim().Length > 0 ? ", " + steps : "")}} ] }
        """;

    public static async Task<SessionHarness> StoppedInRunAsync(
        string steps = "", string? capabilities = null, Func<DebugSessionOptions, DebugSessionOptions>? configure = null)
    {
        var harness = SessionHarness.Create(Scenario(steps, capabilities), configure: configure);
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");
        return harness;
    }

    public static async Task EndAsync(SessionHarness harness)
    {
        await harness.Session.TerminateAsync(Ct).WaitAsync(SessionHarness.Safety, Ct);
        await harness.WaitForEventAsync("terminated");   // the last event: everything before it has been delivered
        harness.AssertScenarioComplete();
    }

    public static async Task<VariableInfo> EvaluateAsync(SessionHarness harness, string expression, int? frameId = null) =>
        (await harness.Session.EvaluateAsync(expression, frameId, EvaluateContext.Watch, Ct).WaitAsync(SessionHarness.Safety, Ct)).Value;
}
