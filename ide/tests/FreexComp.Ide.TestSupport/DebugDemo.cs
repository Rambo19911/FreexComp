using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// The debugger area's demo program for the IDE's debug view tests (I-142…I-146): its source, its type-information
/// sidecar and the memory of its frame <c>Run</c> stopped on line 71 — <c>TestData/debug-demo</c> next to the test
/// assembly, linked from that area's fixtures — as building blocks of scripted-adapter scenarios. With them a session
/// over the scripted adapter reads real Pascal values: the locals of <c>Run</c> (integers, a record, a dynamic array, a
/// class instance, a set …), which <c>writeMemory</c> changes like a running program would.
/// </summary>
public static class DebugDemo
{
    /// <summary>The line of <c>Run</c> the memory was taken at.</summary>
    public const int StopLine = 71;

    /// <summary>The line of the main block that calls <c>Run</c>.</summary>
    public const int CallerLine = 77;

    private const string FixtureSource = @"C:\work\DbgDemo\DbgDemo.pas";

    private static string Folder => Path.Combine(AppContext.BaseDirectory, "TestData", "debug-demo");

    private static JsonElement Memory => JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "DbgDemo.line71.json"), Encoding.UTF8)).RootElement;

    /// <summary>The program's source; a test writes it where its scenario says the frames are.</summary>
    public static string SourceText => File.ReadAllText(Path.Combine(Folder, "DbgDemo.pas"), Encoding.UTF8);

    /// <summary>The type-information sidecar for the source at <paramref name="sourcePath"/>; a test writes it as <c>&lt;program&gt;.pdi</c>.</summary>
    public static string TypeInfo(string sourcePath) =>
        File.ReadAllText(Path.Combine(Folder, "DbgDemo.pdi"), Encoding.UTF8)
            .Replace(JsonSerializer.Serialize(FixtureSource), JsonSerializer.Serialize(sourcePath), StringComparison.Ordinal);

    /// <summary>The address of a local of <c>Run</c>.</summary>
    public static ulong AddressOf(string variable) =>
        ulong.Parse(Memory.GetProperty("variables").GetProperty(variable).GetString()![2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>
    /// The scenario members <c>"variables"</c> (name → address) and <c>"memory"</c>; <paramref name="patches"/> replace
    /// bytes (address, hexadecimal bytes) before the session reads them.
    /// </summary>
    public static string Data(params (ulong Address, string Hex)[] patches)
    {
        var memory = JsonNode.Parse(Memory.GetProperty("memory").GetRawText())!.AsArray();
        foreach (var (address, hex) in patches)
        {
            var region = memory.Select(r => r!.AsObject()).First(r =>
            {
                var start = ulong.Parse(r["address"]!.GetValue<string>()[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return address >= start && address + (ulong)(hex.Length / 2) <= start + (ulong)(r["hex"]!.GetValue<string>().Length / 2);
            });
            var regionStart = ulong.Parse(region["address"]!.GetValue<string>()[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var bytes = region["hex"]!.GetValue<string>();
            var at = (int)(address - regionStart) * 2;
            region["hex"] = bytes[..at] + hex + bytes[(at + hex.Length)..];
        }

        // The parameter and the result of Factorial (a frame with an Arguments scope) live below Run's locals.
        var variables = JsonNode.Parse(Memory.GetProperty("variables").GetRawText())!.AsObject();
        variables["N"] = "0x7F3A6FFA00";
        variables["Result"] = "0x7F3A6FFA04";
        memory.Add(new JsonObject { ["address"] = "0x7F3A6FFA00", ["hex"] = "0200000001000000" + new string('0', 496) });
        return $"\"variables\": {variables.ToJsonString()}, \"memory\": {memory.ToJsonString()}";
    }

    /// <summary>The line of <c>Factorial</c> a step into it stops on.</summary>
    public const int FactorialLine = 37;

    /// <summary>The frames of a stop in <c>Factorial</c>, called by <c>Run</c> on <paramref name="callerLine"/>.</summary>
    public static (string Routine, string Path, int Line)[] FactorialFrames(string sourcePath, int callerLine = 69) =>
        [("Factorial", sourcePath, FactorialLine), ("Run", sourcePath, callerLine), ("DbgDemo", sourcePath, CallerLine)];

    /// <summary>Steps: the adapter's scopes of the frame <c>Factorial</c> and the variables of its Locals scope (N = 2, Result = 1).</summary>
    public const string FactorialScopes = """
        { "on": "scopes", "respond": { "scopes": [ { "name": "Locals", "presentationHint": "locals", "variablesReference": 21 } ] } },
        { "on": "variables", "expectArguments": { "variablesReference": 21 }, "respond": { "variables": [
            { "name": "N", "value": "2", "type": "?", "variablesReference": 0 }, { "name": "Result", "value": "1", "type": "?", "variablesReference": 0 } ] } }
        """;

    /// <summary>The frames of the stop: <c>Run</c> on <paramref name="line"/>, called by the main block.</summary>
    public static (string Routine, string Path, int Line)[] Frames(string sourcePath, int line = StopLine) =>
        [("Run", sourcePath, line), ("DbgDemo", sourcePath, CallerLine)];

    /// <summary>Steps: the adapter's scopes of the frame <c>Run</c> and the variables of its Locals scope (what Locals asks at a stop).</summary>
    public static string Scopes => $$"""
        { "on": "scopes", "respond": { "scopes": [ { "name": "Locals", "presentationHint": "locals", "variablesReference": 11 },
                                                   { "name": "Globals", "variablesReference": 12 } ] } },
        { "on": "variables", "expectArguments": { "variablesReference": 11 }, "respond": { "variables": [ {{string.Join(", ",
            Memory.GetProperty("locals").EnumerateArray().Select(l =>
                $$"""{ "name": {{l.GetProperty("name").GetRawText()}}, "value": {{l.GetProperty("value").GetRawText()}}, "type": "?", "variablesReference": 0 }"""))}} ] } }
        """;

    /// <summary>Steps: what a stop in <c>Run</c> on <paramref name="line"/> asks for (<see cref="DebugScenarios.AtStop"/> with the demo's scopes).</summary>
    public static string AtStop(string sourcePath, int line = StopLine) => DebugScenarios.AtStop(Frames(sourcePath, line), Scopes);
}
