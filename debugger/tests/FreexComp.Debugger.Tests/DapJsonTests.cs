using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Every request we send equals the JSON shown in the DAP usage specification (names, values and property order);
/// the expected texts below are copied from that document. Responses and events deserialise leniently (DBG-010).
/// </summary>
[Trait("Req", "DBG-010")]
public sealed class DapJsonTests
{
    private static void AssertRequest(string expectedJson, int seq, string command, object? arguments)
    {
        var actual = Encoding.UTF8.GetString(DapJson.SerializeRequest(seq, command, arguments));

        Assert.Equal(Canonical(expectedJson), actual);
    }

    /// <summary>The same JSON without insignificant white space (property order and values unchanged).</summary>
    private static string Canonical(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            document.RootElement.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // ---- requests ----

    [Fact]
    [Trait("Req", "DBG-050")]
    public void Serialize_Initialize() => AssertRequest("""
        {"seq":1,"type":"request","command":"initialize","arguments":{
          "clientID":"freexcomp","clientName":"FreexComp","adapterID":"lldb-dap","locale":"en-US",
          "linesStartAt1":true,"columnsStartAt1":true,"pathFormat":"path",
          "supportsVariableType":true,"supportsVariablePaging":false,"supportsRunInTerminalRequest":false,
          "supportsMemoryReferences":true,"supportsProgressReporting":false,"supportsInvalidatedEvent":false,
          "supportsMemoryEvent":false,"supportsArgsCanBeInterpretedByShell":false,
          "supportsStartDebuggingRequest":false,"supportsANSIStyling":false}}
        """, 1, "initialize", new InitializeArguments());

    [Fact]
    [Trait("Req", "DBG-057")]
    public void Serialize_Launch() => AssertRequest("""
        {"seq":2,"type":"request","command":"launch","arguments":{
          "program":"C:\\work\\DbgDemo\\bin\\Debug\\DbgDemo.exe",
          "args":["--verbose","input file.txt"],
          "cwd":"C:\\work\\DbgDemo\\bin\\Debug",
          "env":{"MY_VAR":"1"},
          "stopOnEntry":false,
          "enableAutoVariableSummaries":false,
          "enableSyntheticChildDebugging":false,
          "displayExtendedBacktrace":false,
          "commandEscapePrefix":"`",
          "initCommands":["settings set target.process.thread.step-avoid-regexp \"\""],
          "sourceMap":[]}}
        """, 2, "launch", new LaunchArguments
        {
            Program = @"C:\work\DbgDemo\bin\Debug\DbgDemo.exe",
            Args = ["--verbose", "input file.txt"],
            Cwd = @"C:\work\DbgDemo\bin\Debug",
            Env = new Dictionary<string, string> { ["MY_VAR"] = "1" },
            StopOnEntry = false,
        });

    [Fact]
    public void Serialize_Launch_WithoutEnvironment_OmitsEnv()
    {
        var json = Encoding.UTF8.GetString(DapJson.SerializeRequest(2, "launch",
            new LaunchArguments { Program = @"C:\p.exe", Args = [], Cwd = @"C:\", StopOnEntry = true }));

        Assert.Equal("""{"seq":2,"type":"request","command":"launch","arguments":{"program":"C:\\p.exe","args":[],"cwd":"C:\\","stopOnEntry":true,"enableAutoVariableSummaries":false,"enableSyntheticChildDebugging":false,"displayExtendedBacktrace":false,"commandEscapePrefix":"`","initCommands":["settings set target.process.thread.step-avoid-regexp \"\""],"sourceMap":[]}}""", json);
    }

    [Fact]
    [Trait("Req", "DBG-058")]
    public void Serialize_Launch_WithStdio_KeepsTheNullOfStdin()
    {
        var json = Encoding.UTF8.GetString(DapJson.SerializeRequest(2, "launch",
            new LaunchArguments { Program = @"C:\p.exe", Args = [], Cwd = @"C:\", StopOnEntry = false, Stdio = [null, @"C:\t\out.txt", @"C:\t\err.txt"] }));

        Assert.EndsWith("""
            "sourceMap":[],"stdio":[null,"C:\\t\\out.txt","C:\\t\\err.txt"]}}
            """, json);
    }

    [Fact]
    [Trait("Req", "DBG-060")]
    public void Serialize_SetBreakpoints() => AssertRequest("""
        {"seq":3,"type":"request","command":"setBreakpoints","arguments":{
          "source":{"name":"DbgDemo.pas","path":"C:\\work\\DbgDemo\\DbgDemo.pas"},
          "breakpoints":[{"line":40},{"line":69}],
          "sourceModified":false}}
        """, 3, "setBreakpoints", new SetBreakpointsArguments(
            new DapSource { Name = "DbgDemo.pas", Path = @"C:\work\DbgDemo\DbgDemo.pas" },
            [new DapSourceBreakpoint(40), new DapSourceBreakpoint(69)]));

    [Fact]
    public void Serialize_SetFunctionBreakpoints() => AssertRequest("""
        {"seq":5,"type":"request","command":"setFunctionBreakpoints","arguments":{
          "breakpoints":[{"name":"System._Raise"}]}}
        """, 5, "setFunctionBreakpoints", new SetFunctionBreakpointsArguments([new DapFunctionBreakpoint("System._Raise")]));

    [Fact]
    public void Serialize_SetExceptionBreakpoints() => AssertRequest(
        """{"seq":6,"type":"request","command":"setExceptionBreakpoints","arguments":{"filters":[]}}""",
        6, "setExceptionBreakpoints", new SetExceptionBreakpointsArguments());

    [Fact]
    public void Serialize_ConfigurationDone() => AssertRequest(
        """{"seq":7,"type":"request","command":"configurationDone"}""", 7, "configurationDone", null);

    [Fact]
    public void Serialize_Threads() => AssertRequest(
        """{"seq":10,"type":"request","command":"threads"}""", 10, "threads", null);

    [Fact]
    [Trait("Req", "DBG-073")]
    public void Serialize_StackTrace() => AssertRequest(
        """{"seq":11,"type":"request","command":"stackTrace","arguments":{"threadId":7412,"startFrame":0,"levels":20}}""",
        11, "stackTrace", new StackTraceArguments(7412, 0, 20));

    [Fact]
    public void Serialize_Scopes() => AssertRequest(
        """{"seq":12,"type":"request","command":"scopes","arguments":{"frameId":524288}}""",
        12, "scopes", new ScopesArguments(524288));

    [Fact]
    [Trait("Req", "DBG-080")]
    public void Serialize_Variables() => AssertRequest(
        """{"seq":13,"type":"request","command":"variables","arguments":{"variablesReference":1}}""",
        13, "variables", new VariablesArguments(1));

    [Fact]
    [Trait("Req", "DBG-082")]
    public void Serialize_Evaluate() => AssertRequest("""
        {"seq":14,"type":"request","command":"evaluate","arguments":{
          "expression":"&Total","frameId":524290,"context":"watch"}}
        """, 14, "evaluate", new EvaluateArguments("&Total", 524290));

    [Fact]
    [Trait("Req", "DBG-086")]
    public void Serialize_ReadMemory() => AssertRequest("""
        {"seq":15,"type":"request","command":"readMemory","arguments":{
          "memoryReference":"0xA3C01FF7B4","offset":0,"count":256}}
        """, 15, "readMemory", new ReadMemoryArguments("0xA3C01FF7B4", 256));

    [Fact]
    public void Serialize_WriteMemory() => AssertRequest("""
        {"seq":16,"type":"request","command":"writeMemory","arguments":{
          "memoryReference":"0xA3C01FF7B4","offset":0,"allowPartial":false,"data":"YwAAAA=="}}
        """, 16, "writeMemory", new WriteMemoryArguments("0xA3C01FF7B4", "YwAAAA=="));

    [Fact]
    public void Serialize_SetVariable() => AssertRequest(
        """{"seq":16,"type":"request","command":"setVariable","arguments":{"variablesReference":1,"name":"Total","value":"99"}}""",
        16, "setVariable", new SetVariableArguments(1, "Total", "99"));

    [Fact]
    public void Serialize_Continue() => AssertRequest(
        """{"seq":20,"type":"request","command":"continue","arguments":{"threadId":7412}}""",
        20, "continue", new ThreadArguments(7412));

    [Fact]
    public void Serialize_Next() => AssertRequest(
        """{"seq":21,"type":"request","command":"next","arguments":{"threadId":7412}}""",
        21, "next", new ThreadArguments(7412));

    [Fact]
    public void Serialize_StepIn() => AssertRequest(
        """{"seq":22,"type":"request","command":"stepIn","arguments":{"threadId":7412}}""",
        22, "stepIn", new ThreadArguments(7412));

    [Fact]
    public void Serialize_StepOut() => AssertRequest(
        """{"seq":23,"type":"request","command":"stepOut","arguments":{"threadId":7412}}""",
        23, "stepOut", new ThreadArguments(7412));

    [Fact]
    public void Serialize_Pause() => AssertRequest(
        """{"seq":24,"type":"request","command":"pause","arguments":{"threadId":7412}}""",
        24, "pause", new ThreadArguments(7412));

    [Fact]
    [Trait("Req", "DBG-094")]
    public void Serialize_Disconnect() => AssertRequest(
        """{"seq":30,"type":"request","command":"disconnect","arguments":{"terminateDebuggee":true}}""",
        30, "disconnect", new DisconnectArguments(true));

    [Fact]
    [Trait("Req", "DBG-095")]
    public void Serialize_Disconnect_WithoutTerminateDebuggee_OmitsIt() => AssertRequest(
        """{"seq":31,"type":"request","command":"disconnect","arguments":{}}""",
        31, "disconnect", new DisconnectArguments(null));

    [Fact]
    public void Serialize_ReverseRequestRejection()
    {
        var json = Encoding.UTF8.GetString(DapJson.SerializeResponse(9, 4, "runInTerminal", success: false, "not supported"));

        Assert.Equal("""{"seq":9,"type":"response","request_seq":4,"success":false,"command":"runInTerminal","message":"not supported"}""", json);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-545")]
    public void Serialize_ReverseResponse_WithBody()
    {
        // The answer to the accepted runInTerminal request: the body follows the command; no message.
        var json = Encoding.UTF8.GetString(DapJson.SerializeResponse(
            3, 7, "runInTerminal", success: true, message: null, new RunInTerminalResponseBody(4242)));

        Assert.Equal("""{"seq":3,"type":"response","request_seq":7,"success":true,"command":"runInTerminal","body":{"shellProcessId":4242}}""", json);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-542")]
    public void Serialize_Launch_WithConsole()
    {
        // Terminal mode: the console option is the last property, and there is no stdio.
        var json = Encoding.UTF8.GetString(DapJson.SerializeRequest(2, "launch",
            new LaunchArguments { Program = @"C:\p.exe", Args = [], Cwd = @"C:\", StopOnEntry = false, Console = "externalTerminal" }));

        Assert.EndsWith("""
            "sourceMap":[],"console":"externalTerminal"}}
            """, json);
        Assert.DoesNotContain("stdio", json);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-541")]
    public void Serialize_Initialize_ForTerminalMode_AnnouncesRunInTerminal()
    {
        var captured = Encoding.UTF8.GetString(DapJson.SerializeRequest(1, "initialize", new InitializeArguments()));
        var terminal = Encoding.UTF8.GetString(DapJson.SerializeRequest(1, "initialize", new InitializeArguments { SupportsRunInTerminalRequest = true }));

        // The one value differs; every other property and the order are the same.
        Assert.Contains("\"supportsRunInTerminalRequest\":false", captured);
        Assert.Equal(captured.Replace("\"supportsRunInTerminalRequest\":false", "\"supportsRunInTerminalRequest\":true"), terminal);
    }

    [Fact]
    public void Serialize_NonAscii_WrittenAsUtf8NotEscaped()
    {
        var json = Encoding.UTF8.GetString(DapJson.SerializeRequest(1, "evaluate", new EvaluateArguments("&Ēka", 1)));

        Assert.Contains("\"&Ēka\"", json);
    }

    // ---- responses and events ----

    [Fact]
    public void Deserialize_IgnoresUnknownProperties()
    {
        var stopped = DapJson.Deserialize<StoppedEventBody>(Parse("""
            {"reason":"breakpoint","description":"breakpoint 2.1","threadId":7412,
             "allThreadsStopped":true,"hitBreakpointIds":[2],"preserveFocusHint":false,"futureProperty":{"x":[1,2]}}
            """));

        Assert.Equal("breakpoint", stopped.Reason);
        Assert.Equal("breakpoint 2.1", stopped.Description);
        Assert.Null(stopped.Text);
        Assert.Equal(7412, stopped.ThreadId);
        Assert.Equal([2], stopped.HitBreakpointIds!);
    }

    [Fact]
    public void Deserialize_MissingOptionalProperties_AreNull()
    {
        var stopped = DapJson.Deserialize<StoppedEventBody>(Parse("""{"reason":"pause"}"""));

        Assert.Equal("pause", stopped.Reason);
        Assert.Null(stopped.ThreadId);
        Assert.Null(stopped.HitBreakpointIds);
    }

    [Fact]
    public void Deserialize_SetBreakpointsResponse()
    {
        var body = DapJson.Deserialize<SetBreakpointsResponseBody>(Parse("""
            {"breakpoints":[
              {"id":1,"verified":true,"line":40,"source":{"name":"DbgDemo.pas","path":"C:\\work\\DbgDemo\\DbgDemo.pas"}},
              {"id":2,"verified":true,"line":69},
              {"id":3,"verified":false,"line":2,"message":"no locations found","reason":"pending"}]}
            """));

        Assert.Equal(3, body.Breakpoints.Count);
        Assert.Equal(new DapBreakpoint { Id = 2, Verified = true, Line = 69 }, body.Breakpoints[1]);
        Assert.False(body.Breakpoints[2].Verified);
        Assert.Equal("no locations found", body.Breakpoints[2].Message);
    }

    [Fact]
    public void Deserialize_ThreadsAndStackTrace()
    {
        var threads = DapJson.Deserialize<ThreadsResponseBody>(Parse(
            """{"threads":[{"id":7412,"name":"main thread"},{"id":8120,"name":"Thread 2"}]}"""));
        var stack = DapJson.Deserialize<StackTraceResponseBody>(Parse("""
            {"totalFrames":5,
             "stackFrames":[
               {"id":524288,"name":"Factorial","line":40,"column":5,
                "source":{"name":"DbgDemo.pas","path":"C:\\work\\DbgDemo\\DbgDemo.pas"},
                "instructionPointerReference":"0x7FF6A1B21234","moduleId":"1A2B3C4D"},
               {"id":524292,"name":"BaseThreadInitThunk","line":36,"column":0,
                "source":{"path":"C:\\Windows\\System32\\kernel32.dll`BaseThreadInitThunk","sourceReference":1},
                "instructionPointerReference":"0x7FFB12345678","presentationHint":"subtle","moduleId":7}]}
            """));

        Assert.Equal([new DapThread(7412, "main thread"), new DapThread(8120, "Thread 2")], threads.Threads);
        Assert.Equal(5, stack.TotalFrames);
        Assert.Equal(524288, stack.StackFrames[0].Id);
        Assert.Equal("Factorial", stack.StackFrames[0].Name);
        Assert.Equal(40, stack.StackFrames[0].Line);
        Assert.Equal(5, stack.StackFrames[0].Column);
        Assert.Equal(@"C:\work\DbgDemo\DbgDemo.pas", stack.StackFrames[0].Source!.Path);
        Assert.Null(stack.StackFrames[0].Source!.SourceReference);
        Assert.Equal("0x7FF6A1B21234", stack.StackFrames[0].InstructionPointerReference);
        Assert.Equal("1A2B3C4D", DapJson.IdText(stack.StackFrames[0].ModuleId));
        Assert.Equal(1, stack.StackFrames[1].Source!.SourceReference);
        Assert.Equal("7", DapJson.IdText(stack.StackFrames[1].ModuleId));
        Assert.Null(DapJson.IdText(null));
    }

    [Fact]
    public void Deserialize_ScopesVariablesEvaluate()
    {
        var scopes = DapJson.Deserialize<ScopesResponseBody>(Parse("""
            {"scopes":[
              {"name":"Locals","presentationHint":"locals","variablesReference":1,"expensive":false},
              {"name":"Globals","variablesReference":2,"expensive":false}]}
            """));
        var variables = DapJson.Deserialize<VariablesResponseBody>(Parse("""
            {"variables":[
              {"name":"N","value":"2","type":"Integer","variablesReference":0,"evaluateName":"N","memoryReference":"0x7F3A6FFB84"},
              {"name":"Result","value":"0","type":"Integer","variablesReference":0}]}
            """));
        var evaluate = DapJson.Deserialize<EvaluateResponseBody>(Parse(
            """{"result":"0x0000007f3a6ffb84","type":"int *","variablesReference":4}"""));

        Assert.Equal(new DapScope { Name = "Locals", PresentationHint = "locals", VariablesReference = 1 }, scopes.Scopes[0]);
        Assert.Null(scopes.Scopes[1].PresentationHint);
        Assert.Equal(new DapVariable { Name = "N", Value = "2", Type = "Integer", EvaluateName = "N", MemoryReference = "0x7F3A6FFB84" }, variables.Variables[0]);
        Assert.Null(variables.Variables[1].MemoryReference);
        Assert.Equal("0x0000007f3a6ffb84", evaluate.Result);
        Assert.Null(evaluate.MemoryReference);
    }

    [Fact]
    public void Deserialize_MemoryAndContinue()
    {
        var read = DapJson.Deserialize<ReadMemoryResponseBody>(Parse(
            """{"address":"0xA3C01FF7B4","data":"AAAAABgAAAA=","unreadableBytes":0}"""));
        var unreadable = DapJson.Deserialize<ReadMemoryResponseBody>(Parse("""{"address":"0x10","unreadableBytes":4}"""));
        var write = DapJson.Deserialize<WriteMemoryResponseBody>(Parse("""{"bytesWritten":4}"""));
        var resumed = DapJson.Deserialize<ContinueResponseBody>(Parse("""{"allThreadsContinued":true}"""));

        Assert.Equal("AAAAABgAAAA=", read.Data);
        Assert.Null(unreadable.Data);
        Assert.Equal(4, write.BytesWritten);
        Assert.Null(DapJson.Deserialize<WriteMemoryResponseBody>(Parse("{}")).BytesWritten);
        Assert.True(resumed.AllThreadsContinued);
    }

    [Fact]
    public void Deserialize_Events()
    {
        var exited = DapJson.Deserialize<ExitedEventBody>(Parse("""{"exitCode":3}"""));
        var thread = DapJson.Deserialize<ThreadEventBody>(Parse("""{"reason":"started","threadId":8120}"""));
        var output = DapJson.Deserialize<OutputEventBody>(Parse("""{"category":"stdout","output":"Dog Rex\r\n"}"""));
        var breakpoint = DapJson.Deserialize<BreakpointEventBody>(Parse(
            """{"reason":"changed","breakpoint":{"id":2,"verified":true,"line":70}}"""));
        var module = DapJson.Deserialize<ModuleEventBody>(Parse("""
            {"reason":"new","module":{"id":"1A2B","name":"DbgDemo.exe","path":"C:\\work\\DbgDemo.exe","symbolStatus":"Symbols loaded."}}
            """));
        var process = DapJson.Deserialize<ProcessEventBody>(Parse(
            """{"name":"C:\\work\\DbgDemo.exe","systemProcessId":4242,"isLocalProcess":true,"startMethod":"launch"}"""));

        Assert.Equal(3, exited.ExitCode);
        Assert.Equal(new ThreadEventBody { Reason = "started", ThreadId = 8120 }, thread);
        Assert.Equal(new OutputEventBody { Category = "stdout", Output = "Dog Rex\r\n" }, output);
        Assert.Equal("changed", breakpoint.Reason);
        Assert.Equal(70, breakpoint.Breakpoint.Line);
        Assert.Equal("new", module.Reason);
        Assert.Equal("1A2B", DapJson.IdText(module.Module.Id));
        Assert.Equal("DbgDemo.exe", module.Module.Name);
        Assert.Equal(@"C:\work\DbgDemo.exe", module.Module.Path);
        Assert.Equal("Symbols loaded.", module.Module.SymbolStatus);
        Assert.Equal(4242, process.SystemProcessId);
    }

    [Fact]
    public void Deserialize_WrongShape_ProtocolError()
    {
        var exception = Assert.Throws<DebuggerException>(
            () => DapJson.Deserialize<ThreadsResponseBody>(Parse("""{"threads":"none"}""")));

        Assert.Equal(DebugErrorCode.ProtocolError, exception.Code);
        Assert.StartsWith("Protocol error: ", exception.Message);
    }
}
