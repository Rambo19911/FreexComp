using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Threads and stack trace API, frame id table (DAP usage §5.2, §5.3: DBG-073 … DBG-077; debug-session DBG-408).</summary>
[Trait("Category", "Integration")]
public sealed class InspectionTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = @"C:\work\DbgDemo\DbgDemo.pas";

    private const string StopFrames = """
        { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Factorial", "line": 40, "column": 5,
            "source": { "name": "DbgDemo.pas", "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ], "totalFrames": 1 } }
        """;

    /// <summary>A module event, then a stop on thread 7412; <paramref name="threads"/> is the body of the threads response.</summary>
    private static string StopAtStart(string threads) => $$"""
        { "on": "configurationDone", "respond": {}, "thenEvents": [
            { "event": "module", "body": { "reason": "new", "module": { "id": "1A2B3C4D", "name": "DbgDemo.exe", "path": "C:\\work\\DbgDemo\\bin\\DbgDemo.exe", "symbolStatus": "Symbols loaded." } } },
            { "event": "module", "body": { "reason": "new", "module": { "id": 77, "name": "kernel32.dll" } } },
            { "event": "stopped", "body": { "reason": "step", "threadId": 7412 } } ] },
        { "on": "threads", "respond": { "threads": {{threads}} } },
        {{StopFrames}}
        """;

    private const string MainThread = """[ { "id": 7412, "name": "main thread" } ]""";

    private static async Task<SessionHarness> StoppedAsync(string threads, string moreSteps, params string[] userSourceRoots)
    {
        var harness = SessionHarness.Create(
            $$"""{ "steps": [ {{StopAtStart(threads)}}{{(moreSteps.Length > 0 ? ", " + moreSteps : "")}} ] }""",
            configure: o => o with { UserSourceRoots = userSourceRoots });
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");
        return harness;
    }

    private static string Frame(StackFrameInfo frame) =>
        $"#{frame.Index} {frame.Name} loc={SessionHarness.Location(frame.Location)} user={frame.IsUserCode} ip={frame.InstructionPointer:X} module={frame.Module}";

    private static async Task EndAsync(SessionHarness harness)
    {
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");   // the last event: everything before it has been delivered
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-400")]
    [Trait("Req", "DBG-254")]
    public async Task Threads_Mapped()
    {
        await using var harness = await StoppedAsync(
            """[ { "id": 8120, "name": "" }, { "id": 7412, "name": "main thread" }, { "id": 9000 } ]""", "");

        var threads = await harness.Session.GetThreadsAsync(Ct).WaitAsync(Safety, Ct);

        // Adapter order; an empty or missing name becomes "Thread <id>"; the current thread is the stop thread.
        Assert.Equal(
            [new ThreadInfo(8120, "Thread 8120", false), new ThreadInfo(7412, "main thread", true), new ThreadInfo(9000, "Thread 9000", false)],
            threads);
        // The list of the stop is reused: one threads request in the whole session.
        Assert.Single(harness.SentRequests("threads"));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-074")]
    [Trait("Req", "DBG-076")]
    [Trait("Req", "DBG-415")]
    [Trait("Req", "DBG-250")]
    [Trait("Req", "DBG-253")]
    public async Task StackTrace_Mapped_NoSource()
    {
        await using var harness = await StoppedAsync(MainThread, """
            { "on": "stackTrace", "expectArguments": { "threadId": 7412, "startFrame": 0, "levels": 200 }, "respond": { "totalFrames": 7, "stackFrames": [
                { "id": 524288, "name": "Factorial", "line": 40, "column": 5, "source": { "name": "DbgDemo.pas", "path": "c:\\work\\DbgDemo\\.\\DbgDemo.pas" },
                  "instructionPointerReference": "0x7FF6A1B21234", "moduleId": "1A2B3C4D" },
                { "id": 524289, "name": "System.SysInit", "line": 12, "column": 1, "source": { "name": "System.pas", "path": "C:\\rtl\\System.pas" },
                  "instructionPointerReference": "0x00007ff6a1b20000", "moduleId": "1A2B3C4D" },
                { "id": 524290, "name": "BaseThreadInitThunk", "line": 0, "column": 0, "instructionPointerReference": "0x7FFB12345678",
                  "presentationHint": "subtle", "moduleId": 77 },
                { "id": 524291, "name": "ntdll.dll`RtlUserThreadStart", "line": 9, "column": 1,
                  "source": { "name": "ntdll.dll`RtlUserThreadStart", "path": "ntdll.dll`RtlUserThreadStart", "sourceReference": 1002 },
                  "instructionPointerReference": "0x7FFB20000010", "moduleId": 4711 },
                { "id": 524292, "name": "", "line": 3, "column": 1, "source": { "path": "relative\\unit.pas" }, "instructionPointerReference": "0x7FFB12340000" },
                { "id": 524293, "name": "Tick", "line": 0, "column": 0, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } },
                { "id": 524294, "name": "Tock", "line": 7, "source": { "path": "C:\\work\\DbgDemo\\Lib\\Unit1.pas", "sourceReference": 0 } } ] } },
            { "on": "stackTrace", "expectArguments": { "threadId": 8120, "startFrame": 2, "levels": 2 }, "respond": { "stackFrames": [
                { "id": 1, "name": "A", "line": 0, "column": 0 }, { "id": 2, "name": "B", "line": 0, "column": 0 } ] } },
            { "on": "stackTrace", "expectArguments": { "threadId": 8120, "startFrame": 4, "levels": 1000 }, "respond": { "stackFrames": [
                { "id": 3, "name": "C", "line": 0, "column": 0 } ] } }
            """, @"C:\work\DbgDemo");

        var trace = await harness.Session.GetStackTraceAsync(7412, ct: Ct).WaitAsync(Safety, Ct);

        Assert.Equal(7, trace.TotalFrames);
        Assert.Equal(
        [
            // Normalised path (full path, upper-case drive letter); user code: under a user source root.
            $"#0 Factorial loc={Demo}:40:5 user=True ip=7FF6A1B21234 module=DbgDemo.exe",
            @"#1 System.SysInit loc=C:\rtl\System.pas:12:1 user=False ip=7FF6A1B20000 module=DbgDemo.exe",
            // No source at all.
            "#2 BaseThreadInitThunk loc=null user=False ip=7FFB12345678 module=kernel32.dll",
            // A frame without source arrives as module`function with a source reference: no location; unknown module id.
            "#3 ntdll.dll`RtlUserThreadStart loc=null user=False ip=7FFB20000010 module=",
            // No name: "$" and 16 hex digits of the instruction pointer; a relative path is no location.
            "#4 $00007FFB12340000 loc=null user=False ip=7FFB12340000 module=",
            // Line 0 is no location.
            "#5 Tick loc=null user=False ip=0 module=",
            // A missing column is reported as 1.
            @"#6 Tock loc=C:\work\DbgDemo\Lib\Unit1.pas:7:1 user=True ip=0 module=",
        ], trace.Frames.Select(Frame));
        // Our own ids: unique, and not the adapter's.
        Assert.Equal(7, trace.Frames.Select(f => f.Id).Distinct().Count());
        Assert.DoesNotContain(trace.Frames, f => f.Id >= 524288);

        // Without totalFrames: start + returned, plus one when exactly "levels" frames came back (maybe more).
        var page = await harness.Session.GetStackTraceAsync(8120, startFrame: 2, maxFrames: 2, ct: Ct).WaitAsync(Safety, Ct);
        Assert.Equal(5, page.TotalFrames);
        Assert.Equal([2, 3], page.Frames.Select(f => f.Index));
        // levels is capped at 1000 and never 0.
        var rest = await harness.Session.GetStackTraceAsync(8120, startFrame: 4, maxFrames: 5000, ct: Ct).WaitAsync(Safety, Ct);
        Assert.Equal(5, rest.TotalFrames);
        Assert.Equal("#4 C loc=null user=False ip=0 module=", Frame(Assert.Single(rest.Frames)));
        Assert.Equal("""{"threadId":8120,"startFrame":4,"levels":1000}""",
            System.Text.Json.JsonDocument.Parse(harness.SentRequests("stackTrace")[^1]).RootElement.GetProperty("arguments").GetRawText());
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-075")]
    [Trait("Req", "DBG-134")]
    [Trait("Req", "DBG-252")]
    public async Task FrameName_FromSidecar()
    {
        await using var harness = SessionHarness.Create($$"""
            { "steps": [ {{StopAtStart(MainThread)}},
            { "on": "stackTrace", "respond": { "stackFrames": [
                { "id": 1, "name": "DbgDemo.Factorial", "line": 40, "column": 5, "source": { "path": "c:\\work\\dbgdemo\\DBGDEMO.pas" } },
                { "id": 2, "name": "DbgDemo.Run", "line": 69, "column": 3, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } },
                { "id": 3, "name": "$main", "line": 77, "column": 3, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } },
                { "id": 4, "name": "DbgDemo.Beyond", "line": 90, "column": 1, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } },
                { "id": 5, "name": "Unit1.Helper", "line": 40, "column": 1, "source": { "path": "C:\\work\\DbgDemo\\Unit1.pas" } },
                { "id": 6, "name": "System._Raise", "line": 2990, "column": 1, "source": { "path": "C:\\rtl\\System.pas" } },
                { "id": 7, "name": "BaseThreadInitThunk", "line": 0, "column": 0, "instructionPointerReference": "0x7FFB12345678" },
                { "id": 8, "name": "", "line": 0, "column": 0, "instructionPointerReference": "0x7FFB12340000" } ] } } ] }
            """);
        await harness.LaunchAsync(new LaunchRequest { Program = SessionHarness.Program, TypeInfoPath = PdiFixture.DbgDemoPath });
        var stop = await harness.WaitForEventAsync("stopped");

        var trace = await harness.Session.GetStackTraceAsync(7412, ct: Ct).WaitAsync(Safety, Ct);

        Assert.True(harness.Session.HasTypeInfo);
        Assert.Equal(
        [
            // The routine of the type information that contains (path, line): its display name, not the adapter's.
            "Factorial", "Run",
            // The main block: the program's routine spans the whole file.
            "DbgDemo",
            // A line outside every routine, a file the type information does not know: the adapter's name.
            "DbgDemo.Beyond", "Unit1.Helper",
            "_Raise",
            // No source: the adapter's name, or "$" and the instruction pointer.
            "BaseThreadInitThunk", "$00007FFB12340000",
        ], trace.Frames.Select(f => f.Name));
        // With type information present nothing is reported as missing.
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.StartsWith("output", StringComparison.Ordinal));
        Assert.Contains("loc=" + Demo + ":40:5", stop);
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-074")]
    public async Task StackTrace_NoUserSourceRoots_EveryFrameWithSourceIsUserCode()
    {
        await using var harness = await StoppedAsync(MainThread, """
            { "on": "stackTrace", "respond": { "stackFrames": [
                { "id": 1, "name": "SysInit", "line": 12, "column": 1, "source": { "path": "C:\\rtl\\System.pas" } },
                { "id": 2, "name": "Thunk", "line": 0, "column": 0 } ] } }
            """);

        var trace = await harness.Session.GetStackTraceAsync(7412, maxFrames: 0, ct: Ct).WaitAsync(Safety, Ct);

        Assert.Equal([true, false], trace.Frames.Select(f => f.IsUserCode));
        Assert.Equal(2, trace.TotalFrames);
        // maxFrames 0 means "no limit of the caller": the cap is sent, never 0.
        Assert.EndsWith("\"levels\":1000}}", harness.SentRequests("stackTrace")[^1]);
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-408")]
    public async Task StaleFrameId_Throws()
    {
        await using var harness = await StoppedAsync(MainThread, $$"""
            { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Factorial", "line": 40, "column": 5, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } },
            { "on": "next", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 7412 } } ] },
            { "on": "threads", "respond": { "threads": {{MainThread}} } },
            {{StopFrames}},
            { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Factorial", "line": 41, "column": 5, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
            """);
        var session = harness.Session;
        var first = Assert.Single((await session.GetStackTraceAsync(7412, ct: Ct).WaitAsync(Safety, Ct)).Frames);
        Assert.True(session.TryGetAdapterFrame(first.Id, out var adapterFrame));
        Assert.Equal(524288, adapterFrame);

        await session.StepOverAsync(7412, Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("stopped Step epoch=2");
        var second = Assert.Single((await session.GetStackTraceAsync(7412, ct: Ct).WaitAsync(Safety, Ct)).Frames);
        var sent = harness.SentCommands().Count;

        // The adapter reused its frame id; ours differ per epoch, and the old one is refused without any request.
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(session.TryGetAdapterFrame(second.Id, out _));
        Assert.False(session.TryGetAdapterFrame(first.Id, out _));
        var exception = await Assert.ThrowsAsync<DebuggerException>(() => session.GetScopesAsync(first.Id, Ct).WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.StaleReference, exception.Code);
        Assert.Equal("The stack frame belongs to an earlier stop.", exception.Message);
        exception = await Assert.ThrowsAsync<DebuggerException>(() => session.GetScopesAsync(123456, Ct).WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.StaleReference, exception.Code);
        Assert.Equal(sent, harness.SentCommands().Count);
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-401")]
    public async Task Inspection_WhileRunning_InvalidState()
    {
        await using var harness = SessionHarness.Create("{}");
        await harness.LaunchAsync();

        var threads = await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.GetThreadsAsync(Ct).WaitAsync(Safety, Ct));
        var stack = await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.GetStackTraceAsync(1, ct: Ct).WaitAsync(Safety, Ct));

        Assert.Equal(DebugErrorCode.InvalidState, threads.Code);
        Assert.Equal(DebugErrorCode.InvalidState, stack.Code);
        await EndAsync(harness);
    }
}
