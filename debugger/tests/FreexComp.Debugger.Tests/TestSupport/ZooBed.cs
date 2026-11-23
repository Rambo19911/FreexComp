using System.Buffers.Binary;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// A program with one variable of every type family of <see cref="ValueTestBed"/>'s catalogue, stopped in routine
/// <c>Test</c> (line 50) or in the method <c>TAnimal.Speak</c> (line 75): the bench for evaluator rows that the demo
/// program has no variable for.
/// </summary>
internal static class ZooBed
{
    public const ulong Stack = 0x30000;        // variables, 0x40 apart, in declaration order
    public const ulong Heap = 0x50000;
    public const ulong DogVmt = 0x60300, AnimalVmt = 0x60200, ObjectVmt = 0x60100;
    public const ulong Instance = 0x58000;

    // name, kind, type id
    private static readonly (string Name, string Kind, int Type)[] TestVariables =
    [
        ("A", "param", ValueTestBed.OpenInts), ("A$high", "param", ValueTestBed.Integer), ("V", "varParam", ValueTestBed.Integer),
        ("B", "local", ValueTestBed.Byte), ("Sh", "local", ValueTestBed.ShortInt), ("W", "local", ValueTestBed.Word),
        ("C32", "local", ValueTestBed.Cardinal), ("L", "local", ValueTestBed.Int64), ("U", "local", ValueTestBed.UInt64),
        ("Flag", "local", ValueTestBed.Boolean), ("AC", "local", ValueTestBed.AnsiChar), ("WC", "local", ValueTestBed.Char),
        ("Col", "local", ValueTestBed.TColor), ("Big", "local", ValueTestBed.TBig),
        ("F", "local", ValueTestBed.Single), ("D", "local", ValueTestBed.Double), ("Cur", "local", ValueTestBed.Currency),
        ("Cols", "local", ValueTestBed.TColors), ("Bytes", "local", ValueTestBed.SetOfByte),
        ("Str", "local", ValueTestBed.String), ("Empty", "local", ValueTestBed.String), ("Short", "local", ValueTestBed.ShortString10),
        ("PC", "local", ValueTestBed.PChar),
        ("Ints", "local", ValueTestBed.StaticInts), ("Dyn", "local", ValueTestBed.DynInts), ("NilDyn", "local", ValueTestBed.DynInts),
        ("ByColor", "local", ValueTestBed.ByColor), ("ByLetter", "local", ValueTestBed.ByLetter), ("Matrix", "local", ValueTestBed.Matrix),
        ("Pt", "local", ValueTestBed.TPoint), ("PPt", "local", ValueTestBed.PPoint), ("PI", "local", ValueTestBed.PInteger),
        ("NilPI", "local", ValueTestBed.PInteger), ("Untyped", "local", ValueTestBed.Pointer), ("PPI", "local", ValueTestBed.PPInteger),
        ("Animal", "local", ValueTestBed.TAnimal), ("NilAnimal", "local", ValueTestBed.TAnimal), ("Ref", "local", ValueTestBed.TAnimalClass),
        ("Proc", "local", ValueTestBed.TProc), ("Intf", "local", ValueTestBed.IInterface),
        ("Big250", "local", ValueTestBed.DynInts), ("Pts", "local", ValueTestBed.DynPoints),
        ("Result", "result", ValueTestBed.Integer),
    ];

    private static readonly (string Name, string Kind, int Type)[] SpeakVariables =
    [
        ("Self", "self", ValueTestBed.TAnimal), ("Legs", "local", ValueTestBed.Integer), ("Result", "result", ValueTestBed.String),
    ];

    public static ulong AddressOf(string name) => Stack + (ulong)(0x40 * Array.FindIndex(TestVariables, v => v.Name == name));

    private static string Vars((string Name, string Kind, int Type)[] variables) =>
        string.Join(", ", variables.Select(v => $$"""{ "name": "{{v.Name}}", "kind": "{{v.Kind}}", "type": {{v.Type}} }"""));

    public static PdiFile Pdi() => PdiFixture.Parse($$"""
        { "formatVersion": 1, "program": "Zoo", "container": "codeview", "abi": {{PdiFixture.Abi}},
          "types": [ {{ValueTestBed.Types}} ],
          "units": [ { "name": "U", "file": "C:\\work\\Zoo\\U.pas",
            "globals": [ { "name": "Counter", "type": 1, "linkageName": "U.Counter" } ],
            "routines": [
              { "displayName": "Test", "linkageName": "U.Test", "file": "C:\\work\\Zoo\\U.pas", "line": 10, "bodyLine": 40, "endLine": 60,
                "self": null, "vars": [ {{Vars(TestVariables)}} ] },
              { "displayName": "TAnimal.Speak", "linkageName": "U.TAnimal.Speak", "file": "C:\\work\\Zoo\\U.pas", "line": 70, "bodyLine": 71,
                "endLine": 80, "self": {{ValueTestBed.TAnimal}}, "vars": [ {{Vars(SpeakVariables)}} ] } ] } ] }
        """);

    /// <summary>Stopped in <c>Test</c>.</summary>
    public static EvalBed Test(ValueFormatOptions? options = null) => new(Pdi(), Debuggee(inMethod: false), "Test", 50, options);

    /// <summary>Stopped in the method <c>TAnimal.Speak</c>; <c>Self</c> is the dog.</summary>
    public static EvalBed Speak() => new(Pdi(), Debuggee(inMethod: true), "TAnimal.Speak", 75);

    /// <param name="inMethod">The adapter lists the variables of the method's frame instead of those of <c>Test</c>.</param>
    public static FakeDebuggee Debuggee(bool inMethod)
    {
        var debuggee = new FakeDebuggee();
        var memory = debuggee.Memory;
        memory.Map(Stack, 0x40 * TestVariables.Length);
        foreach (var (name, _, _) in TestVariables)
        {
            if (inMethod)
                debuggee.Variables[name] = AddressOf(name);
            else
                debuggee.Add(name, AddressOf(name));
        }

        ulong At(string name) => AddressOf(name);
        memory.PutPointer(At("A"), Heap + 0x100).PutInt32(At("A$high"), 2);
        memory.PutInt32(Heap + 0x100, 5).PutInt32(Heap + 0x104, 6).PutInt32(Heap + 0x108, 7);
        memory.PutInt32(At("V"), 77);
        memory.Put(At("B"), 200).Put(At("Sh"), 0xFB).Put(At("W"), 0xFF, 0xFF);
        memory.PutInt32(At("C32"), unchecked((int)4000000000)).PutInt64(At("L"), -5000000000).PutInt64(At("U"), -1);
        memory.Put(At("Flag"), 1).Put(At("AC"), (byte)'x').Put(At("WC"), 0xE9, 0x00);
        memory.Put(At("Col"), 2).Put(At("Big"), 0x2C, 0x01);
        memory.Put(At("F"), Single(1.5f)).PutInt64(At("D"), BitConverter.DoubleToInt64Bits(2.25)).PutInt64(At("Cur"), 125000);
        memory.Put(At("Cols"), 0x05).Put(At("Bytes"), new byte[32]).Put(At("Bytes"), 0x8E);   // 1, 2, 3, 7
        memory.PutPointer(At("Str"), Heap + 0x220).PutUnicodeString(Heap + 0x220, "abc");
        memory.PutPointer(At("Empty"), 0);
        memory.Put(At("Short"), 2, (byte)'h', (byte)'i', 0, 0, 0, 0, 0, 0, 0, 0);
        memory.PutPointer(At("PC"), Heap + 0x300).Put(Heap + 0x300, (byte)'p', 0, (byte)'c', 0, 0, 0);
        memory.PutInt32(At("Ints"), 10).PutInt32(At("Ints") + 4, 20).PutInt32(At("Ints") + 8, 30).PutInt32(At("Ints") + 12, 40);
        memory.PutPointer(At("Dyn"), Heap + 0x410).PutIntArray(Heap + 0x410, 1, 2, 3);
        memory.PutPointer(At("NilDyn"), 0);
        memory.Put(At("ByColor"), 7, 8, 9);
        memory.PutInt32(At("ByLetter"), 100).PutInt32(At("ByLetter") + 4, 200).PutInt32(At("ByLetter") + 8, 300);
        memory.Put(At("Matrix"), 1, 2, 3, 4, 5, 6);
        memory.PutInt32(At("Pt"), 3).PutInt32(At("Pt") + 4, 4);
        memory.PutPointer(At("PPt"), At("Pt")).PutPointer(At("PI"), At("Ints") + 4).PutPointer(At("NilPI"), 0);
        memory.PutPointer(At("Untyped"), At("Pt")).PutPointer(At("PPI"), At("PI"));

        // A dog: VMT pointer, Name, Legs, Tricks.
        memory.PutVmt(ObjectVmt, "TObject", 0, 0x60920).PutVmt(AnimalVmt, "TAnimal", ObjectVmt, 0x60960).PutVmt(DogVmt, "TDog", AnimalVmt, 0x609A0);
        memory.PutPointer(Instance, DogVmt).PutPointer(Instance + 8, Heap + 0x520).PutUnicodeString(Heap + 0x520, "Rex")
            .PutInt32(Instance + 16, 4).PutInt32(Instance + 20, 9);
        memory.PutPointer(At("Animal"), Instance).PutPointer(At("NilAnimal"), 0).PutPointer(At("Ref"), DogVmt);
        memory.PutPointer(At("Proc"), 0x401000).PutPointer(At("Intf"), 0);

        memory.PutPointer(At("Big250"), Heap + 0x1010).PutIntArray(Heap + 0x1010, [.. Enumerable.Range(0, 250).Select(i => i * 2)]);
        memory.PutPointer(At("Pts"), Heap + 0x2010).PutInt64(Heap + 0x2008, 2)
            .PutInt32(Heap + 0x2010, 1).PutInt32(Heap + 0x2014, 2).PutInt32(Heap + 0x2018, 3).PutInt32(Heap + 0x201C, 4);
        memory.PutInt32(At("Result"), 0);

        // The unit's global, and the frame of the method.
        memory.PutInt32(0x40000, 5);
        debuggee.Add("Counter", 0x40000, ScopeKind.Globals);
        memory.PutPointer(0x41000, Instance).PutInt32(0x41040, 99).PutPointer(0x41080, 0);
        if (inMethod)
        {
            // "Result" of the method lies where Test's does: an empty string there.
            debuggee.Add("Self", 0x41000).Add("Legs", 0x41040).Add("Result", AddressOf("Result"));
            memory.PutPointer(AddressOf("Result"), 0);
        }

        return debuggee;
    }

    private static byte[] Single(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return bytes;
    }
}
