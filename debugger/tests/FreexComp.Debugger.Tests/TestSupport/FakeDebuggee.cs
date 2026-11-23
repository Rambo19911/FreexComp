using System.Globalization;
using System.Text;
using System.Text.Json;
using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// A debuggee for value and evaluator tests: <see cref="FakeMemory"/>, the addresses of root variables (as the adapter
/// answers "address of"), the adapter's own variable lists, and memory writes.
/// </summary>
internal sealed class FakeDebuggee : IDebuggeeAccess
{
    public FakeMemory Memory { get; } = new();

    /// <summary>Declared spelling → address; a name that is not here cannot be located.</summary>
    public Dictionary<string, ulong> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>What the adapter lists per scope.</summary>
    public Dictionary<ScopeKind, List<RawVariable>> Listed { get; } = new()
    {
        [ScopeKind.Locals] = [],
        [ScopeKind.Globals] = [],
    };

    /// <summary>The adapter's children per variables reference.</summary>
    public Dictionary<int, List<RawVariable>> RawChildren { get; } = [];

    /// <summary>Every request other than a memory read, e.g. "addr 7:Total", "list 7:Locals", "children 12", "write 20000:2A000000".</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Places a variable: locatable and listed by the adapter.</summary>
    public FakeDebuggee Add(string name, ulong address, ScopeKind scope = ScopeKind.Locals)
    {
        Variables[name] = address;
        Listed[scope].Add(new RawVariable(name, "<raw " + name + ">", "?"));
        return this;
    }

    public Task<byte[]?> ReadMemoryAsync(ulong address, int count, CancellationToken ct) => Memory.ReadMemoryAsync(address, count, ct);

    public Task<bool> WriteMemoryAsync(ulong address, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        Requests.Add($"write {address:X}:{Convert.ToHexString(data.Span)}");
        Memory.Put(address, data.ToArray());
        return Task.FromResult(true);
    }

    public Task<ulong?> AddressOfAsync(int frameId, string variablePath, CancellationToken ct)
    {
        Requests.Add($"addr {frameId}:{variablePath}");
        return Task.FromResult<ulong?>(Variables.TryGetValue(variablePath, out var address) ? address : null);
    }

    public Task<IReadOnlyList<RawVariable>> GetRawVariablesAsync(int frameId, ScopeKind scope, CancellationToken ct)
    {
        Requests.Add($"list {frameId}:{scope}");
        return Task.FromResult<IReadOnlyList<RawVariable>>(Listed.TryGetValue(scope, out var list) ? [.. list] : []);
    }

    public Task<IReadOnlyList<RawVariable>> GetRawChildrenAsync(int reference, CancellationToken ct)
    {
        Requests.Add($"children {reference}");
        return Task.FromResult<IReadOnlyList<RawVariable>>(RawChildren.TryGetValue(reference, out var list) ? [.. list] : []);
    }
}

/// <summary>The memory fixture of the demo program's frame <c>Run</c> on line 71 (<c>Fixtures/memory/DbgDemo.line71.json</c>).</summary>
internal static class DemoMemory
{
    public const int AdapterFrameId = 524288;

    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "memory", "DbgDemo.line71.json");

    private static JsonElement Root => JsonDocument.Parse(File.ReadAllText(Path, Encoding.UTF8)).RootElement;

    /// <summary>The <c>memory</c> array as JSON text: the form a FakeAdapter scenario takes.</summary>
    public static string MemoryJson => Root.GetProperty("memory").GetRawText();

    /// <summary>The <c>variables</c> object as JSON text (name → address), for a FakeAdapter scenario.</summary>
    public static string VariablesJson => Root.GetProperty("variables").GetRawText();

    /// <summary>The adapter's variable list of the frame: a <c>variables</c> response body.</summary>
    public static string LocalsResponseJson =>
        "{ \"variables\": [" + string.Join(", ", Root.GetProperty("locals").EnumerateArray().Select(l =>
            $$"""{ "name": {{l.GetProperty("name").GetRawText()}}, "value": {{l.GetProperty("value").GetRawText()}}, "type": "?", "variablesReference": 0 }""")) + "] }";

    public static ulong AddressOf(string name) => ParseAddress(Root.GetProperty("variables").GetProperty(name).GetString()!);

    public static FakeDebuggee Load()
    {
        var root = Root;
        var debuggee = new FakeDebuggee();
        foreach (var region in root.GetProperty("memory").EnumerateArray())
            debuggee.Memory.PutHex(ParseAddress(region.GetProperty("address").GetString()!), region.GetProperty("hex").GetString()!);
        foreach (var variable in root.GetProperty("variables").EnumerateObject())
            debuggee.Variables[variable.Name] = ParseAddress(variable.Value.GetString()!);
        foreach (var local in root.GetProperty("locals").EnumerateArray())
        {
            debuggee.Listed[ScopeKind.Locals].Add(new RawVariable(
                local.GetProperty("name").GetString()!, local.GetProperty("value").GetString()!, local.GetProperty("type").GetString()));
        }

        return debuggee;
    }

    private static ulong ParseAddress(string text) => ulong.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

/// <summary>Type information, a fake debuggee, a frame and the value service over them: the bench of the value-service and evaluator tests.</summary>
internal sealed class EvalBed
{
    public EvalBed(PdiFile pdi, FakeDebuggee debuggee, string routine, int line, ValueFormatOptions? options = null,
        bool canWriteMemory = true, bool canSetVariable = false)
    {
        Pdi = pdi;
        Debuggee = debuggee;
        Frame = new FrameContext(DemoMemory.AdapterFrameId, pdi.Units.SelectMany(u => u.Routines).Single(r => r.DisplayName == routine), line);
        Values = new ValueService(pdi, debuggee, options ?? new ValueFormatOptions(), canWriteMemory, canSetVariable);
        Values.Reset(1);
    }

    public PdiFile Pdi { get; }
    public FakeDebuggee Debuggee { get; }
    public FrameContext Frame { get; }
    public ValueService Values { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The demo program stopped in <c>Run</c> on line 71.</summary>
    public static EvalBed Demo(ValueFormatOptions? options = null) => new(PdiFixture.LoadDbgDemo(), DemoMemory.Load(), "Run", 71, options);

    /// <summary>Evaluates as a watch does (format suffix allowed) and returns the result node.</summary>
    public Task<VariableInfo> EvalAsync(string expression) => Values.EvaluateAsync(expression, Frame, allowFormatSuffix: true, expandable: true, Ct);

    /// <summary>"value : TypeName" of the result.</summary>
    public async Task<string> ResultAsync(string expression)
    {
        var info = await EvalAsync(expression);
        return info.Value + " : " + info.TypeName;
    }

    /// <summary>The message of the evaluation error (code <c>ExpressionEvaluation</c>).</summary>
    public async Task<string> ErrorAsync(string expression)
    {
        var exception = await Assert.ThrowsAsync<DebuggerException>(() => EvalAsync(expression));
        Assert.Equal(DebugErrorCode.ExpressionEvaluation, exception.Code);
        return exception.Message;
    }

    /// <summary>The raw value of the expression (a location is not read).</summary>
    public Task<Value> ValueAsync(string expression) =>
        new Evaluator(Pdi, Debuggee, Frame).EvaluateAsync(
            SubsetValidator.Parse(expression, name => Evaluator.FindType(Pdi, name) is not null).Tree, Ct);

    public Task<IReadOnlyList<VariableInfo>> ChildrenAsync(VariableHandle handle, int start = 0, int count = 0) =>
        Values.GetChildrenAsync(handle, start, count, Ct);

    public Task<IReadOnlyList<VariableInfo>> ScopeAsync(ScopeKind kind) =>
        ChildrenAsync(Values.GetScopes(Frame).Single(scope => scope.Kind == kind).Handle);
}
