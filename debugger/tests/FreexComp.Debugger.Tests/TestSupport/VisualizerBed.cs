using System.Buffers.Binary;
using System.Collections.Concurrent;
using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;
using FreexComp.Debugger.Values.Visualizers;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// A program with generic list classes (<c>TList&lt;Integer&gt;</c> and its relatives), a record alias and a record that
/// holds a list, stopped in routine <c>Test</c> on line 50: the bench of the visualiser framework tests (visualizers.md §8).
/// Every list instance is laid out as VMT pointer, <c>FItems</c> (dynamic array of Integer) at +8, <c>FCount</c> at +16.
/// </summary>
internal sealed class VisualizerBed
{
    public const string UnitFile = @"C:\work\Vis\U.pas";
    public const int StopLine = 50;
    public const ulong Stack = 0x30000;        // variables, 0x40 apart, in declaration order
    public const ulong Heap = 0x50000;
    public const ulong ObjectVmt = 0x60100;

    // Type ids beyond ValueTestBed's catalogue.
    public const int ListOfInteger = 100, ListOfPair = 101, NonGenericList = 102, ListHelper = 103, ObjectListOfAnimal = 104;
    public const int SortedList = 105, NamedList = 106, TPos = 107, THolder = 108, NodeOfGeneric = 109, PList = 110;

    internal const string ExtraTypes = """
        { "id": 100, "name": "TList<Integer>", "unit": "Gen", "kind": "class", "size": 8, "ancestor": 70, "instanceSize": 24,
          "fields": [ { "name": "FItems", "type": 41, "offset": 8, "visibility": "private" }, { "name": "FCount", "type": 1, "offset": 16, "visibility": "private" } ] },
        { "id": 101, "name": "TList<TPair<string, Integer>>", "unit": "Gen", "kind": "class", "size": 8, "ancestor": 70, "instanceSize": 24,
          "fields": [ { "name": "FItems", "type": 41, "offset": 8, "visibility": "private" }, { "name": "FCount", "type": 1, "offset": 16, "visibility": "private" } ] },
        { "id": 102, "name": "TList", "unit": "Gen", "kind": "class", "size": 8, "ancestor": 70, "instanceSize": 24,
          "fields": [ { "name": "FItems", "type": 41, "offset": 8, "visibility": "private" }, { "name": "FCount", "type": 1, "offset": 16, "visibility": "private" } ] },
        { "id": 103, "name": "TListHelper", "unit": "Gen", "kind": "class", "size": 8, "ancestor": 70, "instanceSize": 24,
          "fields": [ { "name": "FItems", "type": 41, "offset": 8, "visibility": "private" }, { "name": "FCount", "type": 1, "offset": 16, "visibility": "private" } ] },
        { "id": 104, "name": "TObjectList<TAnimal>", "unit": "Gen", "kind": "class", "size": 8, "ancestor": 70, "instanceSize": 24,
          "fields": [ { "name": "FItems", "type": 41, "offset": 8, "visibility": "private" }, { "name": "FCount", "type": 1, "offset": 16, "visibility": "private" } ] },
        { "id": 105, "name": "TSortedList", "unit": "Gen", "kind": "class", "size": 8, "ancestor": 100, "instanceSize": 24, "fields": [] },
        { "id": 106, "name": "TNamedList", "unit": "Gen", "kind": "class", "size": 8, "ancestor": 100, "instanceSize": 24, "fields": [] },
        { "id": 107, "name": "TPos", "unit": "U", "kind": "record", "size": 8, "aliasOf": 60,
          "fields": [ { "name": "X", "type": 1, "offset": 0 }, { "name": "Y", "type": 1, "offset": 4 } ] },
        { "id": 108, "name": "THolder", "unit": "U", "kind": "record", "size": 16,
          "fields": [ { "name": "Tag", "type": 1, "offset": 0 }, { "name": "List", "type": 100, "offset": 8 } ] },
        { "id": 109, "name": "TList<Integer>.TNode<Byte>", "unit": "Gen", "kind": "class", "size": 8, "ancestor": 70, "instanceSize": 24,
          "fields": [ { "name": "FItems", "type": 41, "offset": 8, "visibility": "private" }, { "name": "FCount", "type": 1, "offset": 16, "visibility": "private" } ] },
        { "id": 110, "name": "PList", "unit": "U", "kind": "pointer", "size": 8, "to": 100 }
        """;

    // name, type id, the list's class name and elements (null: not a list)
    private static readonly (string Name, int Type, string? Class, int[]? Items)[] TestVariables =
    [
        ("L", ListOfInteger, "TList<Integer>", [10, 20, 30]),
        ("L2", ListOfInteger, "TList<Integer>", [7]),
        ("P", ListOfPair, "TList<TPair<string, Integer>>", [1, 2]),
        ("N", NonGenericList, "TList", [1]),
        ("H", ListHelper, "TListHelper", [1]),
        ("O", ObjectListOfAnimal, "TObjectList<TAnimal>", [1]),
        ("Q", NodeOfGeneric, "TList<Integer>.TNode<Byte>", [1]),
        ("S", SortedList, "TSortedList", [4, 5]),
        ("NL", NamedList, "TNamedList", [6]),
        ("NilL", ListOfInteger, null, null),
        ("Pos", TPos, null, null),
        ("Pt", ValueTestBed.TPoint, null, null),
        ("Hd", THolder, null, null),
        ("PL", PList, null, null),
        ("Num", ValueTestBed.Integer, null, null),
    ];

    public static ulong AddressOf(string name) => Stack + (ulong)(0x40 * Array.FindIndex(TestVariables, v => v.Name == name));

    /// <summary>The instance of a list variable.</summary>
    public static ulong InstanceOf(string name) => Heap + (ulong)(0x200 * Array.FindIndex(TestVariables, v => v.Name == name));

    private static string Escaped(string path) => path.Replace(@"\", @"\\", StringComparison.Ordinal);

    public static string PdiJson => $$"""
        { "formatVersion": 1, "program": "Vis", "container": "codeview", "abi": {{PdiFixture.Abi}},
          "types": [ {{ValueTestBed.Types}}, {{ExtraTypes}} ],
          "units": [ { "name": "U", "file": "{{Escaped(UnitFile)}}", "globals": [],
            "routines": [
              { "displayName": "Test", "linkageName": "U.Test", "file": "{{Escaped(UnitFile)}}", "line": 10, "bodyLine": 40, "endLine": 60,
                "self": null, "vars": [ {{string.Join(", ", TestVariables.Select(v => $$"""{ "name": "{{v.Name}}", "kind": "local", "type": {{v.Type}} }"""))}} ] } ] } ] }
        """;

    public static PdiFile LoadPdi() => PdiFixture.Parse(PdiJson);

    /// <summary>The variable names in declaration order (the adapter lists them all in its Locals scope).</summary>
    public static IEnumerable<string> VariableNames => TestVariables.Select(v => v.Name);

    public static FakeDebuggee NewDebuggee()
    {
        var debuggee = new FakeDebuggee();
        var memory = debuggee.Memory;
        memory.Map(Stack, 0x40 * TestVariables.Length);
        memory.PutVmt(ObjectVmt, "TObject", 0, 0x70000);
        var vmts = new Dictionary<string, ulong>(StringComparer.Ordinal) { ["TObject"] = ObjectVmt };

        // Each class's VMT names its class; the parent is its ancestor's VMT (TSortedList and TNamedList descend from TList<Integer>).
        ulong Vmt(string className)
        {
            if (vmts.TryGetValue(className, out var known))
                return known;
            var parent = className is "TSortedList" or "TNamedList" ? Vmt("TList<Integer>") : ObjectVmt;
            var vmt = 0x61000 + (ulong)(0x100 * vmts.Count);
            memory.PutVmt(vmt, className, parent, 0x71000 + (ulong)(0x100 * vmts.Count));
            vmts[className] = vmt;
            return vmt;
        }

        foreach (var (name, _, className, items) in TestVariables)
        {
            debuggee.Add(name, AddressOf(name));
            if (className is null)
                continue;
            var instance = InstanceOf(name);
            var payload = instance + 0x100;
            memory.PutPointer(AddressOf(name), instance)
                .PutPointer(instance, Vmt(className)).PutPointer(instance + 8, payload).PutInt32(instance + 16, items!.Length).PutInt32(instance + 20, 0)
                .PutIntArray(payload, items);
        }

        memory.PutPointer(AddressOf("NilL"), 0);
        memory.PutInt32(AddressOf("Pos"), 3).PutInt32(AddressOf("Pos") + 4, 4);
        memory.PutInt32(AddressOf("Pt"), 5).PutInt32(AddressOf("Pt") + 4, 6);
        memory.PutInt32(AddressOf("Hd"), 9).PutPointer(AddressOf("Hd") + 8, InstanceOf("L"));
        memory.PutPointer(AddressOf("PL"), AddressOf("L"));
        memory.PutInt32(AddressOf("Num"), 42);
        return debuggee;
    }

    public VisualizerBed(IEnumerable<VisualizerRegistration> registrations, ValueFormatOptions? options = null, TimeProvider? time = null)
    {
        Pdi = LoadPdi();
        Debuggee = NewDebuggee();
        Clock = time ?? new ManualTimeProvider();
        Host = new VisualizerHost(new VisualizerRegistry(registrations), Clock, Log.Enqueue);
        Options = options ?? new ValueFormatOptions();
        Frame = new FrameContext(DemoMemory.AdapterFrameId, Pdi.Units.SelectMany(u => u.Routines).Single(), StopLine);
        Values = NewStop(1);
    }

    public PdiFile Pdi { get; }
    public FakeDebuggee Debuggee { get; }
    public TimeProvider Clock { get; }
    public VisualizerHost Host { get; }
    public ValueFormatOptions Options { get; }
    public FrameContext Frame { get; }
    public ValueService Values { get; private set; }

    /// <summary>The lines the session would raise as <c>Output(Debugger)</c>.</summary>
    public ConcurrentQueue<string> Log { get; } = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A new stop: the session makes a new value service; the visualiser state is the session's.</summary>
    public ValueService NewStop(int epoch)
    {
        Values = new ValueService(Pdi, Debuggee, Options, canWriteMemory: true, visualizers: Host);
        Values.Reset(epoch);
        return Values;
    }

    public Task<VariableInfo> EvalAsync(string expression, CancellationToken? ct = null) =>
        Values.EvaluateAsync(expression, Frame, allowFormatSuffix: true, expandable: true, ct ?? Ct);

    public Task<IReadOnlyList<VariableInfo>> ChildrenAsync(VariableInfo info, int start = 0, int count = 0) =>
        Values.GetChildrenAsync(info.Children ?? throw new InvalidOperationException(info.Name + " has no children"), start, count, Ct);

    public Task<IReadOnlyList<VariableInfo>> LocalsAsync() =>
        Values.GetChildrenAsync(Values.GetScopes(Frame).Single(s => s.Kind == ScopeKind.Locals).Handle, 0, 0, Ct);

    /// <summary>"Name = Value" of every child.</summary>
    public async Task<string[]> ChildLinesAsync(VariableInfo info) => [.. (await ChildrenAsync(info)).Select(c => c.Name + " = " + c.Value)];

    /// <summary>"$" and 16 hexadecimal digits.</summary>
    public static string Address(ulong address) => "$" + address.ToString("X16");

    /// <summary>The plain rendering of a list variable: its class and instance address.</summary>
    public static string PlainList(string variable, string className) => className + "(" + Address(InstanceOf(variable)) + ")";

    public static VisualizerRegistration Pattern(IValueVisualizer visualizer, string pattern, bool summarizesNested = false, string? unit = null) =>
        new(visualizer, unit, pattern, null, Transparent: false, SummarizesNested: summarizesNested);

    public static int Int32(byte[] bytes) => BinaryPrimitives.ReadInt32LittleEndian(bytes);
}

/// <summary>
/// A visualiser for the bench's list layout: summary <c>Count = n</c> (read through the context), children the
/// <c>FItems</c> elements named <c>[i]</c>. Every call is recorded; behaviour can be bent per test.
/// </summary>
internal sealed class ListVisualizer(string id = "test.list") : IValueVisualizer
{
    public string Id => id;

    /// <summary>"Summary L", "Nested Hd.List", "Children L", "ChildAt L 1".</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    public Func<PdiType, bool> Accepts { get; init; } = _ => true;

    /// <summary>Thrown by SummaryAsync, when set.</summary>
    public string? SummaryThrows { get; init; }

    /// <summary>ChildAt of this index throws.</summary>
    public int? ChildThrows { get; init; }

    /// <summary>Extra 4-byte reads SummaryAsync makes through the context (each of FCount's address).</summary>
    public int ExtraReads { get; init; }

    /// <summary>One more read of this many bytes SummaryAsync makes through the context.</summary>
    public int? BigRead { get; init; }

    /// <summary>SummaryAsync catches what the context throws and goes on.</summary>
    public bool SwallowContextErrors { get; init; }

    /// <summary>SummaryAsync completes this, then waits for its token without a timer of its own.</summary>
    public TaskCompletionSource? BlockSummary { get; init; }

    /// <summary>In nested mode the summary also decodes the first element (a second pointer level).</summary>
    public bool NestedReadsElements { get; init; }

    /// <summary>Summary prefix, to tell visualisers apart.</summary>
    public string Prefix { get; init; } = "";

    public bool Matches(PdiType type) => Accepts(type);

    public async Task<string> SummaryAsync(ValueNode value, VisualizerContext context, CancellationToken ct)
    {
        Calls.Enqueue((context.Nested ? "Nested " : "Summary ") + value.EvaluateName);
        if (SummaryThrows is { } message)
            throw new InvalidOperationException(message);
        if (BlockSummary is { } block)
        {
            block.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }

        var count = await context.FieldAsync(value, "FCount");
        for (var i = 0; i < ExtraReads + (BigRead is null ? 0 : 1); i++)
        {
            try
            {
                await context.ReadMemoryAsync(count.Address!.Value, i == ExtraReads ? BigRead!.Value : 4);
            }
            catch (Exception) when (SwallowContextErrors)
            {
            }
        }

        if (context.Nested && NestedReadsElements)
        {
            var items = await context.FieldAsync(value, "FItems");
            await context.DecodeAsync("[0]", items.Type.Element!, items.Pointer, null);
        }

        return Prefix + (context.Nested ? "#" : "Count = ") + context.FormatNested(count);
    }

    public async Task<VisualizerChildren?> ChildrenAsync(ValueNode value, VisualizerContext context, CancellationToken ct)
    {
        Calls.Enqueue("Children " + value.EvaluateName);
        var count = VisualizerBed.Int32((await context.FieldAsync(value, "FCount")).Bytes);
        var items = await context.FieldAsync(value, "FItems");
        return new VisualizerChildren(count, async (index, token) =>
        {
            Calls.Enqueue("ChildAt " + value.EvaluateName + " " + index);
            if (index == ChildThrows)
                throw new InvalidOperationException("bad element");
            var evaluateName = value.EvaluateName is null ? null : value.EvaluateName + ".FItems[" + index + "]";
            var element = await context.DecodeAsync("[" + index + "]", items.Type.Element!, items.Pointer + (ulong)(4 * index), evaluateName);
            return new VisualizerChild("[" + index + "]", element, evaluateName);
        });
    }

    public ITextSource? TextSource(ValueNode value, VisualizerContext context) => null;
}
