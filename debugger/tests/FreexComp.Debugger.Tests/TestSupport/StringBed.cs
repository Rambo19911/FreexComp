using System.Text;
using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;
using FreexComp.Debugger.Values.Visualizers;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// A program with one variable of every string kind and of every character-array kind, stopped in routine <c>Test</c> on
/// line 50: the bench of the string visualiser tests (visualizers.md §4, §8). Variables lie 0x40 apart from
/// <see cref="Stack"/>; the payload of the n-th variable at <see cref="Heap"/> + n × 0x2000. Heap string blocks carry a
/// reference count at payload − 16 (the layout of the run-time library).
/// </summary>
internal sealed class StringBed
{
    public const string UnitFile = @"C:\work\Str\U.pas";
    public const int StopLine = 50;
    public const ulong Stack = 0x30000;
    public const ulong Heap = 0x100000;

    /// <summary>The text of <c>U</c>: 10 UTF-16 units (the emoji is two).</summary>
    public const string UText = "Grüße ✓ 😀";

    /// <summary>The UTF-8 bytes of "Grüße" (7 bytes).</summary>
    public static readonly byte[] Gruesse = Encoding.UTF8.GetBytes("Grüße");

    // name, type id
    private static readonly (string Name, int Type)[] TestVariables =
    [
        ("U", ValueTestBed.String),
        ("NilU", ValueTestBed.String),
        ("K", ValueTestBed.String),
        ("A8", ValueTestBed.AnsiString),
        ("A1252", ValueTestBed.AnsiString),
        ("A1200", ValueTestBed.AnsiString),
        ("A65535", ValueTestBed.AnsiString),
        ("A437", ValueTestBed.AnsiString),
        ("NilA", ValueTestBed.AnsiString),
        ("Sh", ValueTestBed.ShortString10),
        ("P", ValueTestBed.PChar),
        ("NilP", ValueTestBed.PChar),
        ("PA", ValueTestBed.PAnsiChar),
        ("CA", ValueTestBed.StaticChars),
        ("DA", ValueTestBed.DynChars),
        ("AA", ValueTestBed.StaticAnsiChars),
        ("LongP", ValueTestBed.PChar),
        ("NoTermP", ValueTestBed.PChar),
        ("Num", ValueTestBed.Integer),
        ("Pt", ValueTestBed.TPoint),
    ];

    public static ulong AddressOf(string name) => Stack + (ulong)(0x40 * Index(name));

    /// <summary>Where the payload of a variable lies (strings, dynamic arrays, the text a pointer points to).</summary>
    public static ulong PayloadOf(string name) => Heap + (ulong)(0x2000 * Index(name));

    private static int Index(string name)
    {
        var index = Array.FindIndex(TestVariables, v => v.Name == name);
        return index >= 0 ? index : throw new ArgumentException("No variable " + name, nameof(name));
    }

    private static string Escaped(string path) => path.Replace(@"\", @"\\", StringComparison.Ordinal);

    public static string PdiJson => $$"""
        { "formatVersion": 1, "program": "Str", "container": "codeview", "abi": {{PdiFixture.Abi}},
          "types": [ {{ValueTestBed.Types}} ],
          "units": [ { "name": "U", "file": "{{Escaped(UnitFile)}}", "globals": [],
            "routines": [
              { "displayName": "Test", "linkageName": "U.Test", "file": "{{Escaped(UnitFile)}}", "line": 10, "bodyLine": 40, "endLine": 60,
                "self": null, "vars": [ {{string.Join(", ", TestVariables.Select(v => $$"""{ "name": "{{v.Name}}", "kind": "local", "type": {{v.Type}} }"""))}} ] } ] } ] }
        """;

    /// <summary>The variable names in declaration order (the adapter lists them all in its Locals scope).</summary>
    public static IEnumerable<string> VariableNames => TestVariables.Select(v => v.Name);

    /// <summary>A heap UnicodeString block: reference count at −16, the rest as <see cref="FakeMemory.PutUnicodeString"/>.</summary>
    public static void PutUnicode(FakeMemory memory, ulong payload, string text, int refCount) =>
        memory.PutInt32(payload - 16, refCount).PutUnicodeString(payload, text);

    /// <summary>A heap AnsiString block: reference count at −16, the rest as <see cref="FakeMemory.PutAnsiString"/>.</summary>
    public static void PutAnsi(FakeMemory memory, ulong payload, byte[] bytes, ushort codePage, int refCount) =>
        memory.PutInt32(payload - 16, refCount).PutAnsiString(payload, bytes, codePage);

    public static FakeDebuggee NewDebuggee()
    {
        var debuggee = new FakeDebuggee();
        var memory = debuggee.Memory;
        memory.Map(Stack, 0x40 * TestVariables.Length);
        // The heap is readable (zeros) up to the region of NoTermP, whose text is followed by unreadable memory.
        memory.Map(Heap - 0x100, 0x100 + 0x2000 * Index("NoTermP"));
        foreach (var (name, _) in TestVariables)
            debuggee.Add(name, AddressOf(name));

        void Points(string name, ulong payload) => memory.PutPointer(AddressOf(name), payload);

        PutUnicode(memory, PayloadOf("U"), UText, refCount: 2);
        Points("U", PayloadOf("U"));
        Points("NilU", 0);
        PutUnicode(memory, PayloadOf("K"), "const", refCount: -1);
        Points("K", PayloadOf("K"));
        PutAnsi(memory, PayloadOf("A8"), Gruesse, 65001, refCount: 1);
        Points("A8", PayloadOf("A8"));
        PutAnsi(memory, PayloadOf("A1252"), [0x80, (byte)'a'], 1252, refCount: 1);
        Points("A1252", PayloadOf("A1252"));
        PutAnsi(memory, PayloadOf("A1200"), [(byte)'h', 0, (byte)'i', 0], 1200, refCount: 1);
        Points("A1200", PayloadOf("A1200"));
        PutAnsi(memory, PayloadOf("A65535"), [.. "raw"u8], 65535, refCount: 1);
        Points("A65535", PayloadOf("A65535"));
        PutAnsi(memory, PayloadOf("A437"), [.. "dos"u8], 437, refCount: 1);
        Points("A437", PayloadOf("A437"));
        Points("NilA", 0);
        memory.Put(AddressOf("Sh"), [5, .. "short"u8, 0, 0, 0, 0, 0]);
        Points("P", PayloadOf("U"));
        Points("NilP", 0);
        Points("PA", PayloadOf("A8"));
        memory.Put(AddressOf("CA"), [.. "abc\0\0\0\0\0".SelectMany(c => new[] { (byte)c, (byte)0 })]);
        memory.PutInt64(PayloadOf("DA") - 8, 5).Put(PayloadOf("DA"), [.. "hello".SelectMany(c => new[] { (byte)c, (byte)0 })]);
        Points("DA", PayloadOf("DA"));
        memory.Put(AddressOf("AA"), [(byte)'x', (byte)'y', 0, 0]);
        // A text longer than the display limit: its length is found by scanning.
        memory.Put(PayloadOf("LongP"), [.. (new string('y', 3000) + "\0").SelectMany(c => new[] { (byte)c, (byte)0 })]);
        Points("LongP", PayloadOf("LongP"));
        // One block of 128 units and then no more readable memory: no terminator.
        memory.Put(PayloadOf("NoTermP"), [.. new string('q', 128).SelectMany(c => new[] { (byte)c, (byte)0 })]);
        Points("NoTermP", PayloadOf("NoTermP"));
        memory.PutInt32(AddressOf("Num"), 42);
        memory.PutInt32(AddressOf("Pt"), 1).PutInt32(AddressOf("Pt") + 4, 2);
        return debuggee;
    }

    /// <param name="registry">The visualiser table; null: the built-in one.</param>
    /// <param name="debuggee">The memory; null: <see cref="NewDebuggee"/>.</param>
    /// <param name="options">Display options.</param>
    public StringBed(VisualizerRegistry? registry = null, FakeDebuggee? debuggee = null, ValueFormatOptions? options = null)
    {
        Pdi = PdiFixture.Parse(PdiJson);
        Debuggee = debuggee ?? NewDebuggee();
        Host = new VisualizerHost(registry ?? VisualizerRegistry.BuiltIn, new ManualTimeProvider(), Log.Add);
        Frame = new FrameContext(DemoMemory.AdapterFrameId, Pdi.Units.SelectMany(u => u.Routines).Single(), StopLine);
        Values = new ValueService(Pdi, Debuggee, options ?? new ValueFormatOptions(), canWriteMemory: true, visualizers: Host);
        Values.Reset(1);
    }

    public PdiFile Pdi { get; }
    public FakeDebuggee Debuggee { get; }
    public VisualizerHost Host { get; }
    public FrameContext Frame { get; }
    public ValueService Values { get; }

    /// <summary>The lines the session would raise as <c>Output(Debugger)</c>.</summary>
    public List<string> Log { get; } = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task<VariableInfo> EvalAsync(string expression) => Values.EvaluateAsync(expression, Frame, allowFormatSuffix: true, expandable: true, Ct);

    public Task<IReadOnlyList<VariableInfo>> LocalsAsync() =>
        Values.GetChildrenAsync(Values.GetScopes(Frame).Single(s => s.Kind == ScopeKind.Locals).Handle, 0, 0, Ct);

    public Task<TextDetails> DetailsAsync(VariableInfo info) => Values.GetTextDetailsAsync(TextOf(info), Ct);

    public Task<TextChunk> ReadAsync(VariableInfo info, long start, int count) => Values.ReadTextAsync(TextOf(info), start, count, Ct);

    /// <summary>The whole text, chunk after chunk from 0 until the end; the chunks are returned too.</summary>
    public async Task<(string Text, List<TextChunk> Chunks)> ReadAllAsync(VariableInfo info, int count = ValueService.MaxTextChunk)
    {
        var chunks = new List<TextChunk>();
        var text = new StringBuilder();
        for (long start = 0; ;)
        {
            var chunk = await ReadAsync(info, start, count);
            Assert.Equal(start, chunk.Start);
            chunks.Add(chunk);
            text.Append(chunk.Text);
            if (chunk.AtEnd)
                return (text.ToString(), chunks);
            Assert.True(chunk.Next > start, $"No progress at {start}");
            start = chunk.Next;
        }
    }

    public static VariableHandle TextOf(VariableInfo info) => info.Text ?? throw new InvalidOperationException(info.Name + " has no text");

    /// <summary>"$" and 16 hexadecimal digits.</summary>
    public static string Address(ulong address) => "$" + address.ToString("X16");
}
