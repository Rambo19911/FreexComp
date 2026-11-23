// FakeAdapter: a scripted Debug Adapter Protocol server on stdin/stdout (debugger testing specification §3).
// Usage: FakeAdapter <scenario.json>   (without an argument the path is taken from FAKEADAPTER_SCENARIO)
// Independent implementation of the framing: this project must not reference the debugger library (DBG-330).
// Exit codes: 0 = input closed or disconnect, 2 = bad scenario or bad input, 3 = unexpected request or argument
// mismatch, any other = "thenExit" of a step. "SCENARIO-COMPLETE" is written to stderr at exit iff every step was
// consumed and the exit is not a failure (2, 3).
// Step extensions beyond the specification: "thenRaw" (text sent as one framed message after the response and events,
// not necessarily JSON: simulates a protocol error) and "hang": true (after the step the adapter blocks forever and
// ignores its input: simulates an adapter that does not react to disconnect or end of input).
// Built-in answers (no step needed): initialize, launch, configurationDone, disconnect, setExceptionBreakpoints,
// setFunctionBreakpoints (every breakpoint verified, ids from 1001), readMemory, writeMemory.
// Scenario extension: "variables": { "Name": "0x1000" } answers an "evaluate" of "&Name" that no step claims the way the
// real adapter does (result = the address as "0x" and 16 lower-case digits; an unknown name fails).

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FakeAdapter;

internal static class Program
{
    private const int ExitBadScenario = 2;
    private const int ExitUnexpected = 3;

    private static readonly Stream Input = new BufferedStream(Console.OpenStandardInput());
    private static readonly Stream Output = Console.OpenStandardOutput();
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly List<Step> Steps = [];
    private static readonly SortedDictionary<ulong, byte> Memory = [];
    private static readonly Dictionary<string, ulong> Variables = new(StringComparer.Ordinal);
    private static JsonElement? _capabilities;
    private static string _initializedAfter = "launch";
    private static string _launchResponseAfter = "configurationDone";
    private static bool _failOnUnexpected = true;
    private static byte[]? _deferredLaunchResponse;
    private static int _seq;

    private sealed class Step(JsonElement json)
    {
        public JsonElement Json { get; } = json;
        public string On { get; } = json.GetProperty("on").GetString()!;
        public bool Consumed { get; set; }
    }

    private static int Main(string[] args)
    {
        // Test support for the adapter-process tests: "--wait" blocks forever; "--spawn-child" starts such a child,
        // reports its pid and then serves nothing until its input closes; "--print-env NAME" reports one environment
        // variable and the working directory; "--stderr-lines N" writes N numbered lines to stderr.
        if (args is ["--wait"])
        {
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        if (args is ["--print-env", var variable])
        {
            Console.Error.WriteLine($"ENV {variable}={Environment.GetEnvironmentVariable(variable)}");
            Console.Error.WriteLine("CWD " + Environment.CurrentDirectory);
            return 0;
        }

        if (args is ["--stderr-lines", var count])
        {
            for (var i = 1; i <= int.Parse(count, CultureInfo.InvariantCulture); i++)
                Console.Error.WriteLine("line " + i.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        if (args is ["--spawn-child"])
        {
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait") { UseShellExecute = false })!;
            Console.Error.WriteLine("CHILD-PID " + child.Id.ToString(CultureInfo.InvariantCulture));
            while (Input.ReadByte() >= 0)
            {
            }

            return 0;
        }

        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var scenarioPath = args.Length == 1 ? args[0] : Environment.GetEnvironmentVariable("FAKEADAPTER_SCENARIO");
        try
        {
            if (string.IsNullOrEmpty(scenarioPath))
                throw new InvalidOperationException("usage: FakeAdapter <scenario.json>");
            LoadScenario(scenarioPath);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Console.Error.WriteLine("SCENARIO-ERROR: " + e.Message);
            return ExitBadScenario;
        }

        while (ReadMessage() is { } message)
        {
            using (message)
                Handle(message.RootElement);
        }

        Exit(0);
        return 0;
    }

    // ---- scenario ----

    private static void LoadScenario(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement.Clone();
        if (root.TryGetProperty("capabilities", out var capabilities))
            _capabilities = capabilities;
        _initializedAfter = Choice(root, "initializedAfter", _initializedAfter, "initialize", "launch", "never");
        _launchResponseAfter = Choice(root, "launchResponseAfter", _launchResponseAfter, "launch", "configurationDone");
        _failOnUnexpected = Choice(root, "onUnexpected", "fail", "fail", "ignore") == "fail";
        if (root.TryGetProperty("memory", out var memory))
        {
            foreach (var region in memory.EnumerateArray())
            {
                var address = ParseAddress(region.GetProperty("address").GetString()!);
                foreach (var value in Convert.FromHexString(region.GetProperty("hex").GetString()!))
                    Memory[address++] = value;
            }
        }

        if (root.TryGetProperty("variables", out var variables))
        {
            foreach (var variable in variables.EnumerateObject())
                Variables[variable.Name] = ParseAddress(variable.Value.GetString()!);
        }

        if (root.TryGetProperty("steps", out var steps))
        {
            foreach (var step in steps.EnumerateArray())
                Steps.Add(new Step(step));
        }
    }

    private static string Choice(JsonElement root, string name, string fallback, params string[] allowed)
    {
        if (!root.TryGetProperty(name, out var value))
            return fallback;
        var text = value.GetString() ?? "";
        return allowed.Contains(text) ? text : throw new InvalidOperationException($"{name}: unknown value '{text}'");
    }

    private static ulong ParseAddress(string text) =>
        ulong.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // ---- requests ----

    private static void Handle(JsonElement request)
    {
        if (request.GetProperty("type").GetString() != "request")
            return;   // responses to reverse requests and anything else are not scripted
        var command = request.GetProperty("command").GetString()!;
        var requestSeq = request.GetProperty("seq").GetInt32();
        var hasArguments = request.TryGetProperty("arguments", out var arguments);

        var step = Steps.FirstOrDefault(s => !s.Consumed && s.On == command);
        byte[]? response;
        if (step is not null)
        {
            step.Consumed = true;
            if (step.Json.TryGetProperty("expectArguments", out var expected)
                && Mismatch(expected, hasArguments ? arguments : null, "$") is { } difference)
            {
                Console.Error.WriteLine($"ARGUMENT-MISMATCH: {command} at {difference}");
                Exit(ExitUnexpected);
            }

            if (step.Json.TryGetProperty("delayMs", out var delay))
                Thread.Sleep(delay.GetInt32());
            response = step.Json.TryGetProperty("fail", out var fail) ? Failure(requestSeq, command, fail.GetString() ?? "")
                : step.Json.TryGetProperty("respond", out var respond) ? Success(requestSeq, command, w => respond.WriteTo(w))
                : null;   // neither: the request stays unanswered
        }
        else
        {
            response = command switch
            {
                "initialize" => Success(requestSeq, command, WriteCapabilities),
                "launch" or "configurationDone" or "disconnect" or "setExceptionBreakpoints" => Success(requestSeq, command, null),
                "setFunctionBreakpoints" => FunctionBreakpoints(requestSeq, arguments),
                "readMemory" => ReadMemory(requestSeq, arguments),
                "writeMemory" => WriteMemory(requestSeq, arguments),
                "evaluate" when Variables.Count > 0 => AddressOf(requestSeq, arguments),
                _ => Unexpected(requestSeq, command),
            };
        }

        switch (command)
        {
            case "initialize":
                Send(response);
                if (_initializedAfter == "initialize")
                    Send(Event("initialized", null));
                break;
            case "launch" when _launchResponseAfter == "configurationDone":
                _deferredLaunchResponse = response;
                if (_initializedAfter == "launch")
                    Send(Event("initialized", null));
                break;
            case "launch":
                Send(response);
                if (_initializedAfter == "launch")
                    Send(Event("initialized", null));
                break;
            case "configurationDone":
                Send(response);
                Send(_deferredLaunchResponse);
                _deferredLaunchResponse = null;
                break;
            default:
                Send(response);
                break;
        }

        if (step is not null && step.Json.TryGetProperty("thenEvents", out var events))
        {
            foreach (var item in events.EnumerateArray())
                Send(Event(item.GetProperty("event").GetString()!, item.TryGetProperty("body", out var body) ? body : null));
        }

        if (step is not null && step.Json.TryGetProperty("thenRaw", out var raw))
            Send(Encoding.UTF8.GetBytes(raw.GetString() ?? ""));
        if (step is not null && step.Json.TryGetProperty("hang", out var hang) && hang.ValueKind == JsonValueKind.True)
        {
            Output.Flush();
            Thread.Sleep(Timeout.Infinite);
        }

        if (step is not null && step.Json.TryGetProperty("thenExit", out var exitCode))
            Exit(exitCode.GetInt32());
        if (step is null && command == "disconnect")
            Exit(0);
    }

    private static byte[]? Unexpected(int requestSeq, string command)
    {
        if (!_failOnUnexpected)
            return Success(requestSeq, command, w => { w.WriteStartObject(); w.WriteEndObject(); });
        Console.Error.WriteLine("UNEXPECTED: " + command);
        Exit(ExitUnexpected);
        return null;
    }

    /// <summary>Null when <paramref name="actual"/> contains everything of <paramref name="expected"/>, else the first difference.</summary>
    private static string? Mismatch(JsonElement expected, JsonElement? actual, string path)
    {
        string Difference() => $"{path}: expected {expected.GetRawText()}, actual {(actual is { } a ? a.GetRawText() : "<missing>")}";

        if (actual is not { } value)
            return Difference();
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                if (value.ValueKind != JsonValueKind.Object)
                    return Difference();
                foreach (var property in expected.EnumerateObject())
                {
                    JsonElement? found = value.TryGetProperty(property.Name, out var member) ? member : null;
                    if (Mismatch(property.Value, found, path + "." + property.Name) is { } inner)
                        return inner;
                }

                return null;
            case JsonValueKind.Array:
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != expected.GetArrayLength())
                    return Difference();
                for (var i = 0; i < expected.GetArrayLength(); i++)
                {
                    if (Mismatch(expected[i], value[i], $"{path}[{i}]") is { } inner)
                        return inner;
                }

                return null;
            default:
                return JsonElement.DeepEquals(expected, value) ? null : Difference();
        }
    }

    /// <summary>Built-in answer: every requested function breakpoint is verified; ids count from 1001.</summary>
    private static byte[] FunctionBreakpoints(int requestSeq, JsonElement arguments)
    {
        var count = arguments.TryGetProperty("breakpoints", out var breakpoints) ? breakpoints.GetArrayLength() : 0;
        return Success(requestSeq, "setFunctionBreakpoints", w =>
        {
            w.WriteStartObject();
            w.WriteStartArray("breakpoints");
            for (var i = 0; i < count; i++)
            {
                w.WriteStartObject();
                w.WriteNumber("id", 1001 + i);
                w.WriteBoolean("verified", true);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    // ---- memory ----

    private static byte[] ReadMemory(int requestSeq, JsonElement arguments)
    {
        var address = TargetAddress(arguments);
        var count = arguments.GetProperty("count").GetInt32();
        var data = new List<byte>();
        while (data.Count < count && Memory.TryGetValue(address + (ulong)data.Count, out var value))
            data.Add(value);
        return Success(requestSeq, "readMemory", w =>
        {
            w.WriteStartObject();
            w.WriteString("address", FormatAddress(address));
            if (data.Count > 0)
                w.WriteString("data", Convert.ToBase64String(data.ToArray()));
            w.WriteNumber("unreadableBytes", count - data.Count);
            w.WriteEndObject();
        });
    }

    private static byte[] WriteMemory(int requestSeq, JsonElement arguments)
    {
        var address = TargetAddress(arguments);
        var data = Convert.FromBase64String(arguments.GetProperty("data").GetString()!);
        var written = 0;
        while (written < data.Length && Memory.ContainsKey(address + (ulong)written))
        {
            Memory[address + (ulong)written] = data[written];
            written++;
        }

        if (written == 0 && data.Length > 0)
            return Failure(requestSeq, "writeMemory", "memory at " + FormatAddress(address) + " is not writable");
        return Success(requestSeq, "writeMemory", w =>
        {
            w.WriteStartObject();
            w.WriteNumber("bytesWritten", written);
            w.WriteEndObject();
        });
    }

    /// <summary>"&amp;Name" for a name of the scenario's <c>variables</c>: its address, as the real adapter prints it.</summary>
    private static byte[] AddressOf(int requestSeq, JsonElement arguments)
    {
        var expression = arguments.GetProperty("expression").GetString() ?? "";
        var name = expression.StartsWith('&') ? expression[1..] : expression;
        if (!expression.StartsWith('&') || !Variables.TryGetValue(name, out var address))
            return Failure(requestSeq, "evaluate", $"no variable named '{name}' found in this frame");
        return Success(requestSeq, "evaluate", w =>
        {
            w.WriteStartObject();
            w.WriteString("result", "0x" + address.ToString("x16", CultureInfo.InvariantCulture));
            w.WriteString("type", "?");
            w.WriteNumber("variablesReference", 0);
            w.WriteEndObject();
        });
    }

    private static ulong TargetAddress(JsonElement arguments) =>
        ParseAddress(arguments.GetProperty("memoryReference").GetString()!)
        + (arguments.TryGetProperty("offset", out var offset) ? (ulong)offset.GetInt64() : 0);

    private static string FormatAddress(ulong address) => "0x" + address.ToString("X", CultureInfo.InvariantCulture);

    // ---- messages ----

    private static void WriteCapabilities(Utf8JsonWriter writer)
    {
        if (_capabilities is { } capabilities)
        {
            capabilities.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteBoolean("supportsConfigurationDoneRequest", true);
        writer.WriteBoolean("supportsReadMemoryRequest", true);
        writer.WriteBoolean("supportsFunctionBreakpoints", true);
        writer.WriteBoolean("supportsWriteMemoryRequest", true);
        writer.WriteEndObject();
    }

    private static byte[] Success(int requestSeq, string command, Action<Utf8JsonWriter>? writeBody) =>
        Message(w =>
        {
            w.WriteString("type", "response");
            w.WriteNumber("request_seq", requestSeq);
            w.WriteBoolean("success", true);
            w.WriteString("command", command);
            if (writeBody is not null)
            {
                w.WritePropertyName("body");
                writeBody(w);
            }
        });

    private static byte[] Failure(int requestSeq, string command, string text) =>
        Message(w =>
        {
            w.WriteString("type", "response");
            w.WriteNumber("request_seq", requestSeq);
            w.WriteBoolean("success", false);
            w.WriteString("command", command);
            w.WriteString("message", text);
            w.WriteStartObject("body");
            w.WriteStartObject("error");
            w.WriteNumber("id", 1);
            w.WriteString("format", text);
            w.WriteBoolean("showUser", true);
            w.WriteEndObject();
            w.WriteEndObject();
        });

    private static byte[] Event(string name, JsonElement? body) =>
        Message(w =>
        {
            w.WriteString("type", "event");
            w.WriteString("event", name);
            if (body is { } value)
            {
                w.WritePropertyName("body");
                value.WriteTo(w);
            }
        });

    private static byte[] Message(Action<Utf8JsonWriter> writeMembers)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", ++_seq);
            writeMembers(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    // ---- framing ----

    private static void Send(byte[]? content)
    {
        if (content is null)
            return;
        var header = Encoding.ASCII.GetBytes($"Content-Length: {content.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        Output.Write([.. header, .. content]);
        Output.Flush();
    }

    /// <summary>The next framed message from stdin; null at end of input.</summary>
    private static JsonDocument? ReadMessage()
    {
        var header = new StringBuilder();
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var value = Input.ReadByte();
            if (value < 0)
                return null;
            header.Append((char)value);
        }

        const string name = "Content-Length: ";
        var line = header.ToString().Split("\r\n").FirstOrDefault(l => l.StartsWith(name, StringComparison.Ordinal));
        if (line is null || !int.TryParse(line[name.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            Console.Error.WriteLine("BAD-HEADER: " + header.ToString().Trim());
            Exit(ExitBadScenario);
            return null;
        }

        var content = new byte[length];
        try
        {
            Input.ReadExactly(content);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        return JsonDocument.Parse(content);
    }

    private static void Exit(int code)
    {
        Output.Flush();
        if (code is not (ExitBadScenario or ExitUnexpected) && Steps.All(s => s.Consumed))
            Console.Error.WriteLine("SCENARIO-COMPLETE");
        Console.Error.Flush();
        Environment.Exit(code);
    }
}
