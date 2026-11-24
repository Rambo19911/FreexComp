using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Debugging a library through its host (<see cref="LaunchRequest.Libraries"/>): which type-information files are
/// loaded, the raise routine of every module, and the host's frames without source in a step.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LibraryThroughHostTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Capabilities = """
        { "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true, "supportsFunctionBreakpoints": true,
          "supportTerminateDebuggee": true }
        """;

    /// <summary>A small type-information file: one unit with one routine, and the raise routine's symbol.</summary>
    private static string Pdi(string program, string unit, string routine, string hookSymbol) => $$"""
        { "formatVersion": 1, "program": "{{program}}", "container": "codeview",
          "abi": {{PdiFixture.Abi.Replace("System._Raise", hookSymbol, StringComparison.Ordinal)}},
          "types": [ { "id": 1, "name": "T{{unit}}", "unit": "{{unit}}", "kind": "class", "size": 8, "ancestor": null, "instanceSize": 8, "fields": [] } ],
          "units": [
            { "name": "{{unit}}", "file": "C:\\work\\{{unit}}.pas", "globals": [],
              "routines": [
                { "displayName": "{{routine}}", "linkageName": "{{unit}}.{{routine}}", "file": "C:\\work\\{{unit}}.pas",
                  "line": 10, "bodyLine": 11, "endLine": 20, "self": null, "vars": [] } ] } ] }
        """;

    /// <summary>
    /// A directory with a library file and (unless <paramref name="libraryPdi"/> is null) its type-information file,
    /// and optionally a type-information file for the host.
    /// </summary>
    private sealed class Files : IDisposable
    {
        private readonly string _anchor;

        public Files(string? libraryPdi, string? hostPdi = null)
        {
            _anchor = PdiFixture.WriteTemp("Lib.dll", "not a real library: the scripted adapter never loads it");
            var directory = Path.GetDirectoryName(_anchor)!;
            if (libraryPdi is not null)
                File.WriteAllText(Path.Combine(directory, "Lib.pdi"), libraryPdi);
            if (hostPdi is not null)
                File.WriteAllText(HostPdi = Path.Combine(directory, "Host.pdi"), hostPdi);
        }

        public string Library => _anchor;

        public string LibraryPdi => Path.ChangeExtension(_anchor, ".pdi");

        public string? HostPdi { get; }

        public LaunchRequest Request(ExceptionSettings? exceptions = null) => new()
        {
            Program = SessionHarness.Program,
            TypeInfoPath = HostPdi,
            Libraries = [Library],
            Exceptions = exceptions ?? SessionHarness.NoLanguageExceptions,
        };

        public void Dispose() => PdiFixture.DeleteDirectoryOf(_anchor);
    }

    private static string Scenario(params string[] steps) =>
        $$"""{ "capabilities": {{Capabilities}}, "steps": [ {{string.Join(", ", steps)}} ] }""";

    private static async Task EndAsync(SessionHarness harness)
    {
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");
        harness.AssertScenarioComplete();
    }

    // ---- the merged view ----

    [Fact]
    public void Merge_FindsRoutinesAndTypesOfEveryModule()
    {
        var library = PdiFixture.Parse(Pdi("Lib", "LibUnit", "Scale", "System._Raise"));
        var host = PdiFixture.Parse(Pdi("Host", "HostUnit", "Main", "System._Raise"));

        var merged = PdiFile.Merge([library, host]);

        Assert.NotNull(merged);
        Assert.Equal("Scale", merged.FindRoutine(@"C:\work\LibUnit.pas", 12)?.DisplayName);
        Assert.Equal("Main", merged.FindRoutine(@"C:\work\HostUnit.pas", 12)?.DisplayName);
        Assert.NotNull(merged.FindClass("TLibUnit", null));
        Assert.NotNull(merged.FindClass("THostUnit", null));
        Assert.Equal(["LibUnit", "HostUnit"], merged.Units.Select(unit => unit.Name));
        Assert.Equal("Lib", merged.Program);
        Assert.Same(library.Abi, merged.Abi);
    }

    [Fact]
    public void Merge_OfOneFileIsThatFile_OfNoneIsNull()
    {
        var only = PdiFixture.Parse(Pdi("Lib", "LibUnit", "Scale", "System._Raise"));

        Assert.Same(only, PdiFile.Merge([only]));
        Assert.Null(PdiFile.Merge([]));
    }

    // ---- launch ----

    [Fact]
    public async Task Launch_LoadsLibrarySidecar_HostSidecarIsOptional()
    {
        // The host has no type-information file: that is no message, and the session still has type information
        // (the library's). The raise routine of the library gets the function breakpoint.
        using var files = new Files(Pdi("Lib", "LibUnit", "Scale", "System._Raise"));
        await using var harness = SessionHarness.Create(Scenario(
            """
            { "on": "setFunctionBreakpoints", "expectArguments": { "breakpoints": [ { "name": "System._Raise" } ] },
              "respond": { "breakpoints": [ { "id": 7, "verified": false } ] } }
            """,
            """{ "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "breakpoint", "threadId": 1 } } ] }""",
            DemoSession.Threads,
            """
            { "on": "stackTrace", "respond": { "totalFrames": 2, "stackFrames": [
                { "id": 1, "name": "LibUnit.Scale", "line": 12, "column": 3, "source": { "path": "C:\\work\\LibUnit.pas" } },
                { "id": 2, "name": "Host.exe`main", "line": 5, "column": 1, "source": { "name": "Host.exe`main", "sourceReference": 1001 } } ] } }
            """,
            """
            { "on": "stackTrace", "respond": { "totalFrames": 2, "stackFrames": [
                { "id": 1, "name": "LibUnit.Scale", "line": 12, "column": 3, "source": { "path": "C:\\work\\LibUnit.pas" } },
                { "id": 2, "name": "Host.exe`main", "line": 5, "column": 1, "source": { "name": "Host.exe`main", "sourceReference": 1001 } } ] } }
            """));

        await harness.LaunchAsync(files.Request(new ExceptionSettings()));
        await harness.WaitForEventAsync("stopped");

        Assert.True(harness.Session.HasTypeInfo);
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.Contains("Type information file not found", StringComparison.Ordinal));
        // The frame in the library's unit is named by the library's type information; the host's frame has no source
        // and is not user code.
        var frames = (await harness.Session.GetStackTraceAsync(1, ct: Ct).WaitAsync(Safety, Ct)).Frames;
        Assert.Equal(["Scale", "Host.exe`main"], frames.Select(frame => frame.Name));
        Assert.Equal([true, false], frames.Select(frame => frame.IsUserCode));
        await EndAsync(harness);
    }

    [Fact]
    public async Task Launch_LibraryFileMissing_FailsBeforeTheAdapterStarts()
    {
        using var files = new Files(Pdi("Lib", "LibUnit", "Scale", "System._Raise"));
        var missing = Path.Combine(Path.GetDirectoryName(files.Library)!, "Other.dll");
        await using var harness = SessionHarness.Create(Scenario());

        var error = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync(files.Request() with { Libraries = [missing] }));

        Assert.Equal(DebugErrorCode.ProgramNotFound, error.Code);
        Assert.Equal("Library not found: " + missing, error.Message);
        Assert.Null(harness.AdapterProcessId);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
    }

    [Fact]
    public async Task Launch_LibrarySidecarMissing_SaysSoAndGoesOn()
    {
        using var files = new Files(libraryPdi: null);
        await using var harness = SessionHarness.Create(Scenario("""{ "on": "configurationDone", "respond": {} }"""));

        await harness.LaunchAsync(files.Request());

        Assert.False(harness.Session.HasTypeInfo);
        Assert.Contains(harness.Events.Snapshot(),
            e => e.StartsWith("output Debugger ", StringComparison.Ordinal) && e.Contains("Type information file not found", StringComparison.Ordinal)
                && e.Contains("Lib.pdi", StringComparison.Ordinal));
        await EndAsync(harness);
    }

    // ---- exceptions: the raise routine of every module ----

    [Fact]
    public async Task RaiseHooks_OneFunctionBreakpointPerDistinctSymbol_EitherIsARaise()
    {
        // The library and the host name different raise routines: both get a function breakpoint (the library's
        // first), and a stop at either is a raise.
        using var files = new Files(
            Pdi("Lib", "LibUnit", "Scale", "LibSystem._Raise"), hostPdi: Pdi("Host", "HostUnit", "Main", "System._Raise"));
        await using var harness = SessionHarness.Create(Scenario(
            """
            { "on": "setFunctionBreakpoints",
              "expectArguments": { "breakpoints": [ { "name": "LibSystem._Raise" }, { "name": "System._Raise" } ] },
              "respond": { "breakpoints": [ { "id": 7, "verified": false }, { "id": 8, "verified": true } ] } }
            """,
            """
            { "on": "configurationDone", "respond": {},
              "thenEvents": [ { "event": "stopped", "body": { "reason": "function breakpoint", "threadId": 1, "hitBreakpointIds": [7] } } ] }
            """,
            DemoSession.Threads,
            """
            { "on": "stackTrace", "respond": { "totalFrames": 2, "stackFrames": [
                { "id": 1, "name": "LibSystem._Raise", "line": 0, "column": 0 },
                { "id": 2, "name": "LibUnit.Scale", "line": 14, "column": 3, "source": { "path": "C:\\work\\LibUnit.pas" } } ] } }
            """,
            """{ "on": "scopes", "expectArguments": { "frameId": 1 }, "respond": { "scopes": [] } }"""));

        await harness.LaunchAsync(files.Request(new ExceptionSettings()));

        // Nothing describes the raise routine and the adapter has no registers: the class is not known, the stop is
        // an exception stop all the same, at the library routine below the raise.
        Assert.StartsWith(@"stopped Exception epoch=1 thread=1 loc=C:\work\LibUnit.pas:14:3 hits=[]", await harness.WaitForEventAsync("stopped"), StringComparison.Ordinal);
        Assert.Equal(ExceptionKind.Language, harness.Session.CurrentStop?.Exception?.Kind);
        await EndAsync(harness);
    }

    [Fact]
    public async Task RaiseHooks_SameSymbolInEveryModule_IsSentOnce()
    {
        using var files = new Files(
            Pdi("Lib", "LibUnit", "Scale", "System._Raise"), hostPdi: Pdi("Host", "HostUnit", "Main", "System._Raise"));
        await using var harness = SessionHarness.Create(Scenario(
            """
            { "on": "setFunctionBreakpoints", "expectArguments": { "breakpoints": [ { "name": "System._Raise" } ] },
              "respond": { "breakpoints": [ { "id": 7, "verified": true } ] } }
            """,
            """{ "on": "configurationDone", "respond": {} }"""));

        await harness.LaunchAsync(files.Request(new ExceptionSettings()));

        Assert.Equal("""{"breakpoints":[{"name":"System._Raise"}]}""", harness.SentArguments("setFunctionBreakpoints"));
        await EndAsync(harness);
    }

    // ---- stepping: the host's frames without source ----

    private const string StopInScale = """
        { "on": "stackTrace", "respond": { "totalFrames": 2, "stackFrames": [
            { "id": 1, "name": "LibUnit.Scale", "line": 12, "column": 3, "source": { "path": "C:\\work\\LibUnit.pas" } },
            { "id": 2, "name": "Host.exe`main", "line": 5, "column": 1, "source": { "name": "Host.exe`main", "sourceReference": 1001 } } ] } }
        """;

    private const string StopInHost = """
        { "on": "stackTrace", "respond": { "totalFrames": 1, "stackFrames": [
            { "id": 2, "name": "Host.exe`main", "line": 6, "column": 1, "source": { "name": "Host.exe`main", "sourceReference": 1001 } } ] } }
        """;

    private static string[] StepOutIntoHost(params string[] then) =>
    [
        """{ "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "breakpoint", "threadId": 1 } } ] }""",
        DemoSession.Threads, StopInScale,
        """{ "on": "stepOut", "expectArguments": { "threadId": 1 }, "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] }""",
        DemoSession.Threads, StopInHost,
        .. then,
    ];

    [Fact]
    public async Task StepOut_IntoHostWithoutSource_RunsOn_EvenWithoutUserSourceRoots()
    {
        // No user source roots are configured. A library is debugged through its host, so a Step Out of the library
        // routine that ends in the host - no source, no frame of user code left - does not stop there: the debuggee
        // runs on to the next stop in user code or to its end.
        using var files = new Files(Pdi("Lib", "LibUnit", "Scale", "System._Raise"));
        await using var harness = SessionHarness.Create(Scenario(StepOutIntoHost(
            """{ "on": "continue", "expectArguments": { "threadId": 1 }, "respond": { "allThreadsContinued": true } }""")));
        await harness.LaunchAsync(files.Request());
        await harness.WaitForEventAsync("stopped");
        var before = harness.SentCommands().Count;

        await harness.Session.StepOutAsync(1, Ct).WaitAsync(Safety, Ct);
        await harness.WaitForResponseAsync("continue");

        Assert.Equal(["stepOut", "threads", "stackTrace", "continue"], harness.SentCommands().Skip(before));
        Assert.Equal(DebugState.Running, harness.Session.State);
        await EndAsync(harness);
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.Contains("epoch=2", StringComparison.Ordinal));
        Assert.Equal(1, ((IDebugSessionDiagnostics)harness.Session).InternalStopCount);
    }

    [Fact]
    public async Task StepOut_WithoutLibraries_StopsWhereTheStepEnds_AsBefore()
    {
        // The same step in a session that debugs no library and has no user source roots: nothing is filtered.
        using var files = new Files(Pdi("Lib", "LibUnit", "Scale", "System._Raise"));
        await using var harness = SessionHarness.Create(Scenario(StepOutIntoHost()));
        await harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program, TypeInfoPath = files.LibraryPdi, Exceptions = SessionHarness.NoLanguageExceptions,
        });
        await harness.WaitForEventAsync("stopped");

        await harness.Session.StepOutAsync(1, Ct).WaitAsync(Safety, Ct);

        Assert.Equal("stopped Step epoch=2 thread=1 loc=null hits=[] desc=Step completed", await harness.WaitForEventAsync("stopped Step"));
        await EndAsync(harness);
    }
}
