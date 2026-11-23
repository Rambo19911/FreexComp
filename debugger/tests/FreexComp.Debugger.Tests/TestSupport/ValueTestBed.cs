using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// A handwritten type catalogue, a <see cref="FakeMemory"/> and a decoder + formatter over them: the bench of the
/// value decoder tests (debugger testing §2).
/// </summary>
internal sealed class ValueTestBed
{
    /// <summary>Where single test values are placed.</summary>
    public const ulong At = 0x20000;

    // Type ids.
    public const int Integer = 1, Byte = 2, ShortInt = 3, SmallInt = 4, Word = 5, Cardinal = 6, Int64 = 7, UInt64 = 8;
    public const int Boolean = 9, ByteBool = 10, LongBool = 11, Char = 12, AnsiChar = 13, TColor = 14, TBig = 15;
    public const int Single = 16, Double = 17, Currency = 18, TDateTime = 19;
    public const int TColors = 20, SetOfByte = 21, SetOfAnsiChar = 22, SetOfBoolean = 23, SetFrom8 = 24, SetOfChar = 25;
    public const int String = 30, AnsiString = 31, ShortString10 = 32, PChar = 33, PAnsiChar = 34;
    public const int StaticInts = 40, DynInts = 41, OpenInts = 42, ByColor = 43, ByLetter = 44, StaticChars = 45, DynChars = 46;
    public const int Matrix = 47, MatrixRow = 48, DynStrings = 49, DynPoints = 50, StaticAnsiChars = 51, DynDynInts = 52;
    public const int TPoint = 60, TPet = 61, TEmpty = 62, TVariant = 63, TWide = 64, TOwner = 65;
    public const int TObject = 70, TAnimal = 71, TDog = 72, TAnimalClass = 73, OtherDog = 74, AnonymousClassRef = 75, TDogAlias = 76;
    public const int PInteger = 80, Pointer = 81, PPoint = 82, IInterface = 83, TProc = 84, TNotify = 85, AnonymousProc = 86;
    public const int Text = 87, TSmall = 88, AnonymousMethod = 89, PPInteger = 90;

    internal const string Types = """
        { "id": 1, "name": "Integer", "kind": "integer", "size": 4, "signed": true },
        { "id": 2, "name": "Byte", "kind": "integer", "size": 1, "signed": false },
        { "id": 3, "name": "ShortInt", "kind": "integer", "size": 1, "signed": true },
        { "id": 4, "name": "SmallInt", "kind": "integer", "size": 2, "signed": true },
        { "id": 5, "name": "Word", "kind": "integer", "size": 2, "signed": false },
        { "id": 6, "name": "Cardinal", "kind": "integer", "size": 4, "signed": false },
        { "id": 7, "name": "Int64", "kind": "integer", "size": 8, "signed": true },
        { "id": 8, "name": "UInt64", "kind": "integer", "size": 8, "signed": false },
        { "id": 9, "name": "Boolean", "kind": "boolean", "size": 1 },
        { "id": 10, "name": "ByteBool", "kind": "boolean", "size": 1 },
        { "id": 11, "name": "LongBool", "kind": "boolean", "size": 4 },
        { "id": 12, "name": "Char", "kind": "char", "size": 2 },
        { "id": 13, "name": "AnsiChar", "kind": "char", "size": 1 },
        { "id": 14, "name": "TColor", "unit": "U", "kind": "enum", "size": 1,
          "values": [ { "name": "clRed", "value": 0 }, { "name": "clGreen", "value": 1 }, { "name": "clBlue", "value": 2 } ] },
        { "id": 15, "name": "TBig", "unit": "U", "kind": "enum", "size": 2, "values": [ { "name": "bgFirst", "value": 0 }, { "name": "bgLast", "value": 300 } ] },
        { "id": 16, "name": "Single", "kind": "float", "size": 4, "format": "single" },
        { "id": 17, "name": "Double", "kind": "float", "size": 8, "format": "double" },
        { "id": 18, "name": "Currency", "kind": "float", "size": 8, "format": "currency" },
        { "id": 19, "name": "TDateTime", "kind": "float", "size": 8, "format": "datetime" },
        { "id": 20, "name": "TColors", "unit": "U", "kind": "set", "size": 1, "base": 14, "bitBase": 0 },
        { "id": 21, "name": "", "kind": "set", "size": 32, "base": 2, "bitBase": 0 },
        { "id": 22, "name": "", "kind": "set", "size": 32, "base": 13, "bitBase": 0 },
        { "id": 23, "name": "", "kind": "set", "size": 1, "base": 9, "bitBase": 0 },
        { "id": 24, "name": "TFrom8", "unit": "U", "kind": "set", "size": 1, "base": 2, "bitBase": 8 },
        { "id": 25, "name": "", "kind": "set", "size": 32, "base": 12, "bitBase": 0 },
        { "id": 30, "name": "string", "kind": "string", "size": 8, "flavor": "unicode" },
        { "id": 31, "name": "AnsiString", "kind": "string", "size": 8, "flavor": "ansi" },
        { "id": 32, "name": "", "kind": "string", "size": 11, "flavor": "short", "maxLength": 10 },
        { "id": 33, "name": "PChar", "kind": "string", "size": 8, "flavor": "pchar" },
        { "id": 34, "name": "PAnsiChar", "kind": "string", "size": 8, "flavor": "pansichar" },
        { "id": 40, "name": "", "kind": "staticArray", "size": 16, "element": 1, "low": 0, "count": 4 },
        { "id": 41, "name": "", "kind": "dynArray", "size": 8, "element": 1 },
        { "id": 42, "name": "", "kind": "openArray", "size": 8, "element": 1 },
        { "id": 43, "name": "", "kind": "staticArray", "size": 3, "element": 2, "low": 0, "count": 3, "indexType": 14 },
        { "id": 44, "name": "", "kind": "staticArray", "size": 12, "element": 1, "low": 97, "count": 3, "indexType": 13 },
        { "id": 45, "name": "", "kind": "staticArray", "size": 16, "element": 12, "low": 0, "count": 8 },
        { "id": 46, "name": "", "kind": "dynArray", "size": 8, "element": 12 },
        { "id": 47, "name": "TMatrix", "unit": "U", "kind": "staticArray", "size": 6, "element": 48, "low": 0, "count": 2 },
        { "id": 48, "name": "", "kind": "staticArray", "size": 3, "element": 2, "low": -1, "count": 3 },
        { "id": 49, "name": "", "kind": "dynArray", "size": 8, "element": 30 },
        { "id": 50, "name": "", "kind": "dynArray", "size": 8, "element": 60 },
        { "id": 51, "name": "", "kind": "staticArray", "size": 4, "element": 13, "low": 1, "count": 4 },
        { "id": 52, "name": "", "kind": "dynArray", "size": 8, "element": 41 },
        { "id": 60, "name": "TPoint", "unit": "U", "kind": "record", "size": 8,
          "fields": [ { "name": "X", "type": 1, "offset": 0 }, { "name": "Y", "type": 1, "offset": 4 } ] },
        { "id": 61, "name": "TPet", "unit": "U", "kind": "record", "size": 24,
          "fields": [ { "name": "Name", "type": 30, "offset": 0 }, { "name": "Pos", "type": 60, "offset": 8 }, { "name": "Tags", "type": 49, "offset": 16 } ] },
        { "id": 62, "name": "TEmpty", "unit": "U", "kind": "record", "size": 0, "fields": [] },
        { "id": 63, "name": "TVariant", "unit": "U", "kind": "record", "size": 8,
          "fields": [ { "name": "Tag", "type": 2, "offset": 0 }, { "name": "AsInt", "type": 1, "offset": 4 }, { "name": "AsWord", "type": 5, "offset": 4 } ] },
        { "id": 64, "name": "TWide", "unit": "U", "kind": "record", "size": 10,
          "fields": [ { "name": "F1", "type": 2, "offset": 0 }, { "name": "F2", "type": 2, "offset": 1 }, { "name": "F3", "type": 2, "offset": 2 },
                      { "name": "F4", "type": 2, "offset": 3 }, { "name": "F5", "type": 2, "offset": 4 }, { "name": "F6", "type": 2, "offset": 5 },
                      { "name": "F7", "type": 2, "offset": 6 }, { "name": "F8", "type": 2, "offset": 7 }, { "name": "F9", "type": 2, "offset": 8 },
                      { "name": "F10", "type": 2, "offset": 9 } ] },
        { "id": 65, "name": "TOwner", "unit": "U", "kind": "record", "size": 40,
          "fields": [ { "name": "Pet", "type": 71, "offset": 0 }, { "name": "Next", "type": 80, "offset": 8 }, { "name": "Kind", "type": 14, "offset": 16 },
                      { "name": "Flags", "type": 20, "offset": 17 }, { "name": "Nums", "type": 41, "offset": 24 }, { "name": "None", "type": 62, "offset": 32 },
                      { "name": "Ok", "type": 9, "offset": 32 }, { "name": "Rate", "type": 17, "offset": 32 } ] },
        { "id": 70, "name": "TObject", "unit": "System", "kind": "class", "size": 8, "ancestor": null, "instanceSize": 8, "fields": [] },
        { "id": 71, "name": "TAnimal", "unit": "U", "kind": "class", "size": 8, "ancestor": 70, "instanceSize": 24,
          "fields": [ { "name": "Name", "type": 30, "offset": 8, "visibility": "public" }, { "name": "Legs", "type": 1, "offset": 16, "visibility": "public" } ] },
        { "id": 72, "name": "TDog", "unit": "U", "kind": "class", "size": 8, "ancestor": 71, "instanceSize": 24,
          "fields": [ { "name": "Tricks", "type": 1, "offset": 20, "visibility": "private" } ] },
        { "id": 73, "name": "TAnimalClass", "unit": "U", "kind": "classRef", "size": 8, "of": 71 },
        { "id": 74, "name": "TDog", "unit": "Other", "kind": "class", "size": 8, "ancestor": 70, "instanceSize": 16,
          "fields": [ { "name": "Bark", "type": 1, "offset": 8, "visibility": "public" } ] },
        { "id": 75, "name": "", "kind": "classRef", "size": 8, "of": 71 },
        { "id": 76, "name": "TPuppy", "unit": "U", "kind": "class", "size": 8, "ancestor": 71, "instanceSize": 24, "aliasOf": 72,
          "fields": [ { "name": "Tricks", "type": 1, "offset": 20, "visibility": "private" } ] },
        { "id": 80, "name": "", "kind": "pointer", "size": 8, "to": 1 },
        { "id": 81, "name": "Pointer", "kind": "pointer", "size": 8, "to": null },
        { "id": 82, "name": "PPoint", "kind": "pointer", "size": 8, "to": 60 },
        { "id": 83, "name": "IInterface", "unit": "System", "kind": "interface", "size": 8 },
        { "id": 84, "name": "TProc", "unit": "U", "kind": "procedural", "size": 8, "method": false },
        { "id": 85, "name": "TNotify", "unit": "U", "kind": "procedural", "size": 16, "method": true },
        { "id": 86, "name": "", "kind": "procedural", "size": 8, "method": false },
        { "id": 87, "name": "Text", "kind": "opaque", "size": 624 },
        { "id": 88, "name": "TSmall", "unit": "U", "kind": "opaque", "size": 3 },
        { "id": 89, "name": "", "kind": "procedural", "size": 16, "method": true },
        { "id": 90, "name": "", "kind": "pointer", "size": 8, "to": 80 }
        """;

    public ValueTestBed(ValueFormatOptions? options = null, string? abi = null)
    {
        Options = options ?? new ValueFormatOptions();
        Pdi = PdiFixture.WithTypes(Types, abi ?? PdiFixture.Abi);
        Decoder = new ValueDecoder(Pdi, Memory, Options);
        Formatter = new ValueFormatter(Options);
    }

    public ValueFormatOptions Options { get; }
    public PdiFile Pdi { get; }
    public FakeMemory Memory { get; } = new();
    public ValueDecoder Decoder { get; }
    public ValueFormatter Formatter { get; }

    public PdiType Type(int id) => Pdi.Types.Single(type => type.Id == id);

    /// <summary>Decodes the variable <paramref name="name"/> of type <paramref name="typeId"/> stored at <paramref name="address"/>.</summary>
    public Task<ValueNode> DecodeAsync(int typeId, ulong address = At, string name = "V", long? openArrayHigh = null) =>
        Decoder.DecodeAsync(name, Type(typeId), address, name, TestContext.Current.CancellationToken, openArrayHigh);

    /// <summary>Puts the storage bytes at <see cref="At"/>, decodes and returns the node.</summary>
    public Task<ValueNode> DecodeHexAsync(int typeId, string storageHex, string name = "V")
    {
        Memory.PutHex(At, storageHex);
        return DecodeAsync(typeId, At, name);
    }

    /// <summary>Puts the storage bytes at <see cref="At"/>, decodes and renders the value.</summary>
    public async Task<string> ValueAsync(int typeId, string storageHex) => Formatter.Format(await DecodeHexAsync(typeId, storageHex));

    public string Format(ValueNode node) => Formatter.Format(node);

    public Task<IReadOnlyList<ValueNode>> ChildrenAsync(ValueNode node, int start = 0, int count = 0) =>
        Decoder.GetChildrenAsync(node, start, count, TestContext.Current.CancellationToken);

    /// <summary>The children as "Name = Value" lines.</summary>
    public async Task<string[]> ChildLinesAsync(ValueNode node, int start = 0, int count = 0) =>
        [.. (await ChildrenAsync(node, start, count)).Select(child => child.Name + " = " + Format(child))];

    public VariableInfo Info(ValueNode node, bool canWriteMemory = true) =>
        Formatter.ToVariableInfo(node, new VariableHandle(1, 1), canWriteMemory);

    /// <summary>"$" and 16 hexadecimal digits.</summary>
    public static string Address(ulong address) => "$" + address.ToString("X16");
}
