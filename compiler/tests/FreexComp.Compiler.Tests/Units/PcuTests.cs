using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-073: the <c>.pcu</c> file (architecture.md §6) written by <see cref="PcuWriter"/> — key order, canonical form, header,
/// sources, dependencies, link inputs, the interface symbols and types — and the interface hash (CMP-152): unchanged by body,
/// comment, whitespace and implementation-section edits, changed by interface edits and by a used unit's interface edit.
/// </summary>
[Trait("Phase", "2")]
public sealed class PcuTests
{
    private const string Dir = @"C:\p";

    internal const string UnitV = "unit V;\ninterface\ntype\n  TThing = (One, Two);\nimplementation\nend.\n";

    internal const string UnitU = """
        unit U;
        interface
        uses V;
        const
          Answer = 42;
          Greeting = 'hi';
          Ratio = 0.5;
          Yes = True;
          Limit: Integer = 7;
        type
          TColor = (Red, Green, Blue);
          TSmall = 1..10;
          TPoint = record
            X, Y: Integer;
            C: TColor;
          end;
          PPoint = ^TPoint;
          TNum = Integer;
          TProc = procedure(A: Integer; var B: TSmall);
          TColors = set of TColor;
          TRow = array[TColor] of Byte;
          TList = array of TPoint;
        var
          Origin: TPoint;
          Shared: Integer; public name 'u_shared';
        function Add(A, B: Integer): Integer;
        procedure Show(const S: string; var N: TSmall); overload;
        procedure Show; overload;
        function Ticks: Cardinal; stdcall; external 'kernel32.dll' name 'GetTickCount';
        function First: TThing;
        implementation
        function Add(A, B: Integer): Integer;
        begin
          Result := A + B;
        end;
        procedure Show(const S: string; var N: TSmall);
        begin
        end;
        procedure Show;
        begin
        end;
        function First: TThing;
        begin
          Result := One;
        end;
        end.
        """;

    internal const string Program = "program P;\nuses U;\nbegin\n  Writeln(Add(1, 2));\nend.\n";

    internal static InMemoryFileSystem Files(string u = UnitU, string v = UnitV, string program = Program) =>
        new InMemoryFileSystem()
            .Add(Path.Combine(Dir, "P.pas"), program)
            .Add(Path.Combine(Dir, "U.pas"), u)
            .Add(Path.Combine(Dir, "V.pas"), v)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));

    internal static CompilationOptions Options(params string[] defines) =>
        new() { MainFile = Path.Combine(Dir, "P.pas"), RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj", Defines = [.. defines] };

    internal static Dictionary<string, string> Pcus(InMemoryFileSystem fs, CompilationOptions? options = null)
    {
        var texts = Compilation.Create(options ?? Options(), [], fs).PcuTexts();
        Assert.NotEmpty(texts);
        return texts.ToDictionary(t => t.Unit, t => t.Text);
    }

    private static JsonObject Pcu(string unit, InMemoryFileSystem? fs = null) => (JsonObject)JsonNode.Parse(Pcus(fs ?? Files())[unit])!;

    private static string Hash(string unit, InMemoryFileSystem fs) => (string)Pcu(unit, fs)["interfaceHash"]!;

    // ------------------------------------------------------------------ file shape

    [Fact]
    [Trait("Req", "CMP-160")]
    public void Write_KeysInTheOrderOfTheFormat_CanonicalCompactUtf8_OneFinalNewline()
    {
        var text = Pcus(Files())["U"];

        Assert.Equal(
            ["formatVersion", "unit", "kind", "compilerFingerprint", "optionsFingerprint", "sources", "dependencies", "interfaceHash",
             "hasInit", "hasFinal", "linkInputs", "resources", "interface", "genericBodies"],
            ((JsonObject)JsonNode.Parse(text)!).Select(p => p.Key));
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal(PcuWriter.Serialize(JsonNode.Parse(text)!), text);           // no insignificant whitespace
        Assert.Equal(1, Pcu("U")["formatVersion"]!.GetValue<int>());
        Assert.Equal("unit", (string)Pcu("U")["kind"]!);
        Assert.Equal(["uses", "symbols", "types"], ((JsonObject)Pcu("U")["interface"]!).Select(p => p.Key));
    }

    [Fact]
    [Trait("Req", "CMP-150")]
    public void Write_Fingerprints_CompilerAndTheCompilationsOptionsFingerprint()
    {
        var fs = Files();
        var options = Options("FOO");

        var pcu = (JsonObject)JsonNode.Parse(Pcus(fs, options)["U"])!;

        Assert.Equal($"opc {CompilerInfo.Version}; abi {AbiInfo.Version}; llvm {CompilerInfo.LlvmPin}; build {typeof(PcuWriter).Assembly.ManifestModule.ModuleVersionId:N}",
            (string)pcu["compilerFingerprint"]!);
        Assert.Equal(Compilation.Create(options, [], fs).OptionsFingerprint(), (string)pcu["optionsFingerprint"]!);
        Assert.NotEqual((string)Pcu("U", fs)["optionsFingerprint"]!, (string)pcu["optionsFingerprint"]!);
    }

    [Fact]
    [Trait("Req", "CMP-150")]
    public void Write_Sources_TheUnitThenItsIncludes_WithSizeTimeAndSha256()
    {
        var fs = Files(u: UnitU.Replace("implementation\n", "implementation\n{$I body.inc}\n", StringComparison.Ordinal))
            .Add(@"C:\p\body.inc", "{ nothing }");
        var unitBytes = fs.ReadAllBytes(@"C:\p\U.pas");

        var sources = (JsonArray)Pcu("U", fs)["sources"]!;

        Assert.Equal(2, sources.Count);
        Assert.Equal(
            $$"""{"path":"C:\\p\\U.pas","role":"unit","size":{{unitBytes.Length}},"mtime":"{{fs.GetLastWriteTimeUtc(@"C:\p\U.pas"):yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'}}","sha256":"{{Convert.ToHexStringLower(SHA256.HashData(unitBytes))}}"}""",
            sources[0]!.ToJsonString());
        Assert.Equal(@"C:\p\body.inc", (string)sources[1]!["path"]!);
        Assert.Equal("include", (string)sources[1]!["role"]!);
        Assert.Equal(11, sources[1]!["size"]!.GetValue<long>());
    }

    [Fact]
    [Trait("Req", "CMP-150")]
    public void Write_Dependencies_SystemThenInterfaceUsesThenImplementationUses_WithTheirInterfaceHashes()
    {
        var fs = Files(u: UnitU.Replace("implementation\n", "implementation\nuses W;\n", StringComparison.Ordinal))
            .Add(@"C:\p\W.pas", "unit W; interface implementation end.");
        var pcus = Pcus(fs);
        string HashOf(string unit) => (string)JsonNode.Parse(pcus[unit])!["interfaceHash"]!;

        var pcu = JsonNode.Parse(pcus["U"])!;

        Assert.Equal(
            $$"""[{"unit":"System","path":"C:\\rtl\\System.pas","section":"interface","interfaceHash":"{{HashOf("System")}}"},""" +
            $$"""{"unit":"V","path":"C:\\p\\V.pas","section":"interface","interfaceHash":"{{HashOf("V")}}"},""" +
            $$"""{"unit":"W","path":"C:\\p\\W.pas","section":"implementation","interfaceHash":"{{HashOf("W")}}"}]""",
            pcu["dependencies"]!.ToJsonString());
        Assert.Equal(
            $$"""[{"unit":"System","interfaceHash":"{{HashOf("System")}}"},{"unit":"V","interfaceHash":"{{HashOf("V")}}"}]""",
            pcu["interface"]!["uses"]!.ToJsonString());
        Assert.Matches("^sha256:[0-9a-f]{64}$", HashOf("U"));
    }

    [Fact]
    [Trait("Req", "CMP-430")]
    [Trait("Req", "CMP-620")]
    public void Write_LinkInputs_DllsObjectsAndDefs()
    {
        var fs = Files(u: UnitU.Replace("interface\n", "{$L native.obj}\n{$L defs\\gdi32.def}\ninterface\n", StringComparison.Ordinal))
            .Add(@"C:\p\native.obj", "")
            .Add(@"C:\p\defs\gdi32.def", "");

        Assert.Equal(
            """{"dlls":["kernel32.dll"],"objects":["C:\\p\\native.obj"],"defs":["C:\\p\\defs\\gdi32.def"]}""",
            Pcu("U", fs)["linkInputs"]!.ToJsonString());
        Assert.Equal("""{"dlls":[],"objects":[],"defs":[]}""", Pcu("V", fs)["linkInputs"]!.ToJsonString());
    }

    [Fact]
    [Trait("Req", "CMP-163")]
    public void Write_Program_HasKindProgram_UnitOrderAndAppType_AndSectionFlags()
    {
        var pcu = Pcu("P");

        Assert.Equal("program", (string)pcu["kind"]!);
        Assert.Equal("""["System","V","U"]""", pcu["unitOrder"]!.ToJsonString());
        Assert.Equal("console", (string)pcu["appType"]!);
        Assert.False(pcu["hasInit"]!.GetValue<bool>());
        Assert.False(Pcu("V")["hasInit"]!.GetValue<bool>());
        var withSections = Pcu("V", Files(v: UnitV.Replace("end.", "initialization\nend.", StringComparison.Ordinal)));
        Assert.True(withSections["hasInit"]!.GetValue<bool>());
        Assert.True(withSections["hasFinal"]!.GetValue<bool>());
    }

    // ------------------------------------------------------------------ interface symbols and types

    /// <summary>One symbol entry without <c>id</c>/<c>decl</c>, its type ids replaced by the described types (named types by name).</summary>
    private static string[] Symbols(JsonObject pcu)
    {
        var types = (JsonArray)pcu["interface"]!["types"]!;
        return [.. ((JsonArray)pcu["interface"]!["symbols"]!).Select(s => Inline((JsonObject)s!.DeepClone(), types, depth: 0).ToJsonString())];
    }

    private static JsonNode Inline(JsonObject node, JsonArray types, int depth)
    {
        node.Remove("id");
        node.Remove("decl");
        foreach (var key in new[] { "type", "base", "element", "target", "result" })
        {
            if (node[key] is JsonValue value && value.TryGetValue<int>(out var id))
            {
                node[key] = TypeText(id, types, depth);
            }
        }

        if (node["index"] is JsonArray index)
        {
            node["index"] = new JsonArray([.. index.Select(i => TypeText(i!.GetValue<int>(), types, depth))]);
        }

        foreach (var key in new[] { "params", "fields" })
        {
            if (node[key] is JsonArray items)
            {
                node[key] = new JsonArray([.. items.Select(i => Inline((JsonObject)i!.DeepClone(), types, depth + 1))]);
            }
        }

        return node;
    }

    /// <summary>builtin → its name; ref → <c>Unit.Name</c>; others → the entry (inner named types as their name, so cycles end).</summary>
    private static JsonNode TypeText(int id, JsonArray types, int depth)
    {
        var entry = (JsonObject)types[id]!;
        Assert.Equal(id, entry["id"]!.GetValue<int>());
        return (string)entry["kind"]! switch
        {
            "builtin" => JsonValue.Create((string)entry["name"]!),
            "ref" => JsonValue.Create((string)entry["unit"]! + "." + (string)entry["name"]!),
            _ when depth > 0 && ((string?)entry["name"] ?? "").Length > 0 && char.IsAsciiLetter(((string)entry["name"]!)[0]) => JsonValue.Create("=" + (string)entry["name"]!),
            _ => Inline((JsonObject)entry.DeepClone(), types, depth + 1),
        };
    }

    [Fact]
    [Trait("Req", "CMP-152")]
    [Trait("Req", "CMP-161")]
    public void Write_InterfaceSymbols_InScopeOrder_WithTheirTypes()
    {
        Assert.Equal(
            [
                """{"kind":"const","name":"Answer","type":"Integer","value":42}""",
                """{"kind":"const","name":"Greeting","type":"string","value":"hi"}""",
                """{"kind":"const","name":"Ratio","type":"Double","value":"0.5"}""",
                """{"kind":"const","name":"Yes","type":"Boolean","value":true}""",
                """{"kind":"const","name":"Limit","type":"Integer","typed":true,"linkName":"U.Limit"}""",
                """{"kind":"enumMember","name":"Red","type":{"kind":"enum","name":"TColor","members":[{"name":"Red","ordinal":0},{"name":"Green","ordinal":1},{"name":"Blue","ordinal":2}],"size":1,"scoped":false},"ordinal":0}""",
                """{"kind":"enumMember","name":"Green","type":{"kind":"enum","name":"TColor","members":[{"name":"Red","ordinal":0},{"name":"Green","ordinal":1},{"name":"Blue","ordinal":2}],"size":1,"scoped":false},"ordinal":1}""",
                """{"kind":"enumMember","name":"Blue","type":{"kind":"enum","name":"TColor","members":[{"name":"Red","ordinal":0},{"name":"Green","ordinal":1},{"name":"Blue","ordinal":2}],"size":1,"scoped":false},"ordinal":2}""",
                """{"kind":"typeAlias","name":"TColor","type":{"kind":"enum","name":"TColor","members":[{"name":"Red","ordinal":0},{"name":"Green","ordinal":1},{"name":"Blue","ordinal":2}],"size":1,"scoped":false},"distinct":true}""",
                """{"kind":"typeAlias","name":"TSmall","type":{"kind":"subrange","name":"TSmall","base":"Byte","lo":1,"hi":10},"distinct":true}""",
                """{"kind":"typeAlias","name":"TPoint","type":{"kind":"record","name":"TPoint","size":12,"align":4,"packed":false,"packing":8,"anonymous":false,"fields":[{"name":"X","type":"Integer","offset":0,"visibility":"public"},{"name":"Y","type":"Integer","offset":4,"visibility":"public"},{"name":"C","type":"=TColor","offset":8,"visibility":"public"}],"methods":[],"properties":[],"managed":false},"distinct":true}""",
                """{"kind":"typeAlias","name":"PPoint","type":{"kind":"pointer","name":"PPoint","target":"=TPoint","pointerMath":false},"distinct":true}""",
                """{"kind":"typeAlias","name":"TNum","type":"Integer","distinct":false}""",
                """{"kind":"typeAlias","name":"TProc","type":{"kind":"procedural","name":"TProc","params":[{"name":"A","type":"Integer","mode":"value","openArray":false},{"name":"B","type":"=TSmall","mode":"var","openArray":false}],"result":null,"ofObject":false,"reference":false},"distinct":true}""",
                """{"kind":"typeAlias","name":"TColors","type":{"kind":"set","name":"TColors","base":"=TColor"},"distinct":true}""",
                """{"kind":"typeAlias","name":"TRow","type":{"kind":"array","name":"TRow","index":["=TColor"],"element":"Byte"},"distinct":true}""",
                """{"kind":"typeAlias","name":"TList","type":{"kind":"dynArray","name":"TList","element":"=TPoint"},"distinct":true}""",
                """{"kind":"var","name":"Origin","type":{"kind":"record","name":"TPoint","size":12,"align":4,"packed":false,"packing":8,"anonymous":false,"fields":[{"name":"X","type":"Integer","offset":0,"visibility":"public"},{"name":"Y","type":"Integer","offset":4,"visibility":"public"},{"name":"C","type":"=TColor","offset":8,"visibility":"public"}],"methods":[],"properties":[],"managed":false},"linkName":"U.Origin"}""",
                """{"kind":"var","name":"Shared","type":"Integer","linkName":"u_shared"}""",
                """{"kind":"routine","name":"Add","linkName":"U.Add","params":[{"name":"A","type":"Integer","mode":"value","openArray":false},{"name":"B","type":"Integer","mode":"value","openArray":false}],"result":"Integer","flags":[],"dll":null,"importName":null}""",
                """{"kind":"routine","name":"Show","linkName":"U.Show$c:string,v:U.TSmall","params":[{"name":"S","type":"string","mode":"const","openArray":false},{"name":"N","type":"=TSmall","mode":"var","openArray":false}],"result":null,"flags":["overload"],"dll":null,"importName":null}""",
                """{"kind":"routine","name":"Show","linkName":"U.Show$","params":[],"result":null,"flags":["overload"],"dll":null,"importName":null}""",
                """{"kind":"routine","name":"Ticks","linkName":"GetTickCount","params":[],"result":"Cardinal","flags":["external"],"dll":"kernel32.dll","importName":"GetTickCount"}""",
                """{"kind":"routine","name":"First","linkName":"U.First","params":[],"result":"V.TThing","flags":[],"dll":null,"importName":null}""",
            ],
            Symbols(Pcu("U")));
    }

    [Fact]
    [Trait("Req", "CMP-152")]
    public void Write_TypesTable_IdsAreIndexes_EachTypeOnce_InFirstReferenceOrder()
    {
        var types = (JsonArray)Pcu("U")["interface"]!["types"]!;

        Assert.Equal(Enumerable.Range(0, types.Count), types.Select(t => t!["id"]!.GetValue<int>()));
        Assert.Equal(
            ["builtin Integer", "builtin string", "builtin Double", "builtin Boolean", "enum TColor", "subrange TSmall", "builtin Byte",
             "record TPoint", "pointer PPoint", "procedural TProc", "set TColors", "array TRow", "dynArray TList",
             "builtin Cardinal", "ref TThing"],
            types.Select(t => (string)t!["kind"]! + " " + (string)t["name"]!));
        Assert.Equal("V", (string)types[^1]!["unit"]!);
    }

    [Fact]
    [Trait("Req", "CMP-154")]
    public void Write_Declarations_IndexIntoSourcesWithStartAndLength()
    {
        var symbols = (JsonArray)Pcu("U")["interface"]!["symbols"]!;
        var add = symbols.Single(s => (string)s!["name"]! == "Add")!;
        var start = UnitU.IndexOf("function Add", StringComparison.Ordinal) + "function ".Length;

        Assert.Equal($$"""{"file":0,"start":{{start}},"length":3}""", add["decl"]!.ToJsonString());
    }

    [Fact]
    [Trait("Req", "CMP-152")]
    public void Write_SameInputsTwice_AreByteIdentical()
    {
        Assert.Equal(Pcus(Files()), Pcus(Files()));
    }

    // ------------------------------------------------------------------ interface hash (CMP-152)

    [Theory]
    [InlineData("body", "Result := A + B;", "Result := B + A;")]
    [InlineData("comment", "interface\n", "interface\n{ a comment } // another\n")]
    [InlineData("whitespace", "  Answer = 42;", "\n\n     Answer   =   42  ;")]
    [InlineData("implementation symbol", "implementation\n", "implementation\nconst Hidden = 1;\nvar Private: Integer;\nprocedure Helper; begin end;\n")]
    [InlineData("doc comment", "function Add(", "{** Adds. }\nfunction Add(")]
    [Trait("Req", "CMP-152")]
    public void InterfaceHash_UnchangedBy(string edit, string from, string to)
    {
        var before = Hash("U", Files());

        var after = Hash("U", Files(u: ReplaceOnce(UnitU, from, to)));

        Assert.True(before == after, edit);
    }

    [Theory]
    [InlineData("parameter type", "var N: TSmall", "var N: Integer")]
    [InlineData("result type", "function Add(A, B: Integer): Integer", "function Add(A, B: Integer): Int64")]
    [InlineData("parameter mode", "procedure Show(const S", "procedure Show(var S")]
    [InlineData("constant value", "Answer = 42;", "Answer = 43;")]
    [InlineData("new symbol", "var\n  Origin", "var\n  Extra: Byte;\n  Origin")]
    [InlineData("field type", "X, Y: Integer;", "X, Y: Int64;")]
    [InlineData("link name", "public name 'u_shared'", "public name 'u_shared2'")]
    [InlineData("external DLL", "external 'kernel32.dll'", "external 'kernelbase.dll'")]
    [InlineData("alignment directive", "  TPoint = record", "  {$A1}TPoint = record")]
    [Trait("Req", "CMP-152")]
    public void InterfaceHash_ChangedBy(string edit, string from, string to)
    {
        var before = Hash("U", Files());

        var after = Hash("U", Files(u: UnitU.Replace(from, to, StringComparison.Ordinal)));

        Assert.True(before != after, edit);
    }

    [Fact]
    [Trait("Req", "CMP-152")]
    public void InterfaceHash_ChangesWithAUsedUnitsInterface_ButNotWithItsBody()
    {
        var before = Hash("U", Files());

        var interfaceEdit = Hash("U", Files(v: UnitV.Replace("(One, Two)", "(One, Two, Three)", StringComparison.Ordinal)));
        var bodyEdit = Hash("U", Files(v: UnitV.Replace("implementation\n", "implementation\nvar Z: Integer;\n", StringComparison.Ordinal)));

        Assert.NotEqual(before, interfaceEdit);
        Assert.Equal(before, bodyEdit);
    }

    [Fact]
    [Trait("Req", "CMP-152")]
    public void InterfaceHash_IsTheSha256OfTheInterfaceWithoutPositionsAndComments()
    {
        var pcu = Pcu("U");
        var hashed = (JsonObject)pcu["interface"]!.DeepClone();
        foreach (var symbol in ((JsonArray)hashed["symbols"]!).OfType<JsonObject>())
        {
            symbol.Remove("decl");
            symbol.Remove("doc");
        }

        var text = PcuWriter.Serialize(hashed).TrimEnd('\n');

        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), (string)pcu["interfaceHash"]!);
    }

    [Fact]
    [Trait("Req", "CMP-150")]
    public void Fingerprints_AreComparedWithTheCurrentOnes()
    {
        var fs = Files();
        var pcu = PcuReader.Parse(Pcus(fs)["U"])!;
        var current = Compilation.Create(Options(), [], fs).OptionsFingerprint();

        Assert.True(PcuReader.HasFingerprints(pcu, PcuWriter.CompilerFingerprint, current));
        Assert.True(PcuReader.HasFingerprints(pcu, PcuWriter.CompilerFingerprint));
        Assert.False(PcuReader.HasFingerprints(pcu, PcuWriter.CompilerFingerprint, Compilation.Create(Options("X"), [], fs).OptionsFingerprint()));
        Assert.False(PcuReader.HasFingerprints(pcu, "opc 0.0.0; abi 0; llvm 0", current));
        Assert.Null(PcuReader.Parse(Pcus(fs)["U"].Replace("\"formatVersion\":1", "\"formatVersion\":2", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------------ round trip (C-074): write → read → write

    [Fact]
    [Trait("Req", "CMP-160")]
    [Trait("Req", "CMP-161")]
    public void RoundTrip_PcuOnlyUnits_WrittenAgainFromTheImportedSymbols_AreByteIdentical()
    {
        var (fs, options, original) = PcuImportTests.PrecompiledInMemory();

        var again = Compilation.Create(options, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => t.Text);

        Assert.Equal(original["V"], again["V"]);
        Assert.Equal(original["U"], again["U"]);
        Assert.Equal(JsonNode.Parse(original["P"])!["interfaceHash"]!.ToJsonString(), JsonNode.Parse(again["P"])!["interfaceHash"]!.ToJsonString());
    }

    [Fact]
    [Trait("Req", "CMP-160")]
    public void RoundTrip_EveryTypeAndSymbolKind_SurvivesTheImport()
    {
        const string Rich = """
            unit U;
            interface
            uses V;
            type
              TColor = (Red, Green, Blue);
              TSmall = 1..10;
              TGreens = Green..Blue;
              TLetters = 'a'..'z';
              TFlag = type Boolean;
              TReal = type Single;
              TChars = type PChar;
              TName = string[20];
              TAnsi = AnsiString;
              PNode = ^TNode;
              TNode = record
                Next: PNode;
                Name: string;
                Grid: array[1..2, TColor] of TSmall;
              end;
              TPacked = packed record
                A: Byte;
                B: Int64;
              end;
              TFunc = function(const A: TNode; out B: TFlag): TReal;
              TMatrix = array of array of TReal;
              TThings = set of TThing;
            {$SCOPEDENUMS ON}
              TScoped = (Alpha, Beta);
            {$SCOPEDENUMS OFF}
            const
              Nothing = nil;
              Letter = 'Z';
              Big = 18446744073709551615;
              Negative = -9223372036854775808;
              Some = [Red, Blue];
              Chars = ['a'..'c'];
            var
              Head: PNode;
              Callback: TFunc;
            procedure Stop; noreturn;
            function Twice(X: Integer): Integer; inline;
            procedure Defaults(A: Integer = -1; const S: string = 'x'; P: PNode = nil; C: TThings = []; R: Double = 1.5; F: TFlag = True);
            implementation
            procedure Defaults(A: Integer; const S: string; P: PNode; C: TThings; R: Double; F: TFlag); begin end;
            procedure Stop; begin Halt(1); end;
            function Twice(X: Integer): Integer; begin Result := X * 2; end;
            end.
            """;
        const string Empty = "program P;\nuses U;\nbegin\nend.\n";
        var pcus = Pcus(Files(u: Rich, program: Empty));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Empty)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")))
            .Add(@"C:\lib\U.pcu", pcus["U"]).Add(@"C:\lib\U.obj", "")
            .Add(@"C:\lib\V.pcu", pcus["V"]).Add(@"C:\lib\V.obj", "");

        var again = Compilation.Create(Options() with { SearchPaths = [@"C:\lib"] }, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => t.Text);

        Assert.Contains("\"default\":-1", pcus["U"], StringComparison.Ordinal);       // C-105: default values (nil = null)
        Assert.Contains("\"default\":null", pcus["U"], StringComparison.Ordinal);
        Assert.Equal(pcus["U"], again["U"]);
    }

    /// <summary>C-189: classes of every member kind (architecture.md §6 <c>class</c>, <c>classRef</c>).</summary>
    internal const string ClassUnit = """
        unit U;
        interface
        uses V;
        type
          TNode = class;
          TPair = record
            A, B: Integer;
          end;
          TBase = class
          private
            FCount: Integer;
            FItems: array[0..3] of Integer;
            FPair: TPair;
            function GetItem(I: Integer): Integer;
            function GetFlag(Index: Integer): Boolean;
          protected
            procedure Changed; virtual;
          public
            Name: string;
            Next: TNode;
            class var Instances: Integer;
            constructor Create(const AName: string); virtual;
            destructor Destroy; override;
            procedure Run; virtual; abstract;
            class function Kind: string; virtual;
            class procedure Reset; static;
            function Twice(X: Integer): Integer; overload;
            function Twice(X: string): string; overload;
            property Count: Integer read FCount write FCount;
            property First: Integer read FPair.A;
            property Items[I: Integer]: Integer read GetItem; default;
            property Flag: Boolean index 3 read GetFlag;
            class property Total: Integer read Instances;
          end;
          TNode = class(TBase)
            procedure Run; override;
            procedure Changed; override; final;
            class function Kind: string; override;
          end;
          TBaseClass = class of TBase;
          TSealed = class sealed(TNode)
          end;
          TAbstract = class abstract
          end;
        var
          Kinds: TBaseClass;
        function Make(C: TBaseClass): TBase;
        implementation
        function TBase.GetItem(I: Integer): Integer; begin Result := FItems[I]; end;
        function TBase.GetFlag(Index: Integer): Boolean; begin Result := Index > 0; end;
        procedure TBase.Changed; begin end;
        constructor TBase.Create(const AName: string); begin Name := AName; end;
        destructor TBase.Destroy; begin inherited; end;
        class function TBase.Kind: string; begin Result := 'base'; end;
        class procedure TBase.Reset; begin Instances := 0; end;
        function TBase.Twice(X: Integer): Integer; begin Result := X * 2; end;
        function TBase.Twice(X: string): string; begin Result := X + X; end;
        procedure TNode.Run; begin end;
        procedure TNode.Changed; begin end;
        class function TNode.Kind: string; begin Result := 'node'; end;
        function Make(C: TBaseClass): TBase; begin Result := C.Create('made'); end;
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-160")]
    [Trait("Req", "CMP-161")]
    public void RoundTrip_Classes()
    {
        const string Empty = "program P;\nuses U;\nbegin\nend.\n";
        var pcus = Pcus(Files(u: ClassUnit, program: Empty));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Empty)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")))
            .Add(@"C:\lib\U.pcu", pcus["U"]).Add(@"C:\lib\U.obj", "")
            .Add(@"C:\lib\V.pcu", pcus["V"]).Add(@"C:\lib\V.obj", "");

        var again = Compilation.Create(Options() with { SearchPaths = [@"C:\lib"] }, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => t.Text);

        Assert.Contains("\"kind\":\"class\"", pcus["U"], StringComparison.Ordinal);
        Assert.Equal(pcus["U"], again["U"]);
    }

    /// <summary>C-205: a unit with interfaces (GUID, ancestor, overloads, properties) and classes implementing them.</summary>
    internal const string InterfaceUnit = """
        unit U;
        interface
        uses V;
        type
          IShape = interface
            ['{0A1B2C3D-0000-1111-2222-333344445555}']
            function Area: Integer;
            function GetName: string;
            property Name: string read GetName;
          end;
          IBox = interface(IShape)
            ['{0A1B2C3D-6666-7777-8888-9999AAAABBBB}']
            procedure Grow(N: Integer); overload;
            procedure Grow(const S: string); overload;
            function GetSide(I: Integer): Integer;
            property Sides[I: Integer]: Integer read GetSide; default;
          end;
          TNotify = procedure(Sender: TObject) of object;
          TBox = class(TInterfacedObject, IBox)
            FSide: Integer;
            FOnGrow: TNotify;
            property OnGrow: TNotify read FOnGrow write FOnGrow;
            function Area: Integer;
            function GetName: string;
            procedure Grow(N: Integer); overload;
            procedure Grow(const S: string); overload;
            function GetSide(I: Integer): Integer; virtual;
          end;
        function MakeBox(Side: Integer): IBox;
        implementation
        function TBox.Area: Integer; begin Result := FSide * FSide end;
        function TBox.GetName: string; begin Result := 'box' end;
        procedure TBox.Grow(N: Integer); begin Inc(FSide, N) end;
        procedure TBox.Grow(const S: string); begin Inc(FSide, Length(S)) end;
        function TBox.GetSide(I: Integer): Integer; begin Result := FSide + I end;
        function MakeBox(Side: Integer): IBox; var B: TBox; begin B := TBox.Create; B.FSide := Side; Result := B end;
        end.
        """;

    /// <summary>C-205: interface types and the classes' <c>interfaces</c> entries survive write → import → write byte for byte.</summary>
    [Fact]
    [Trait("Req", "CMP-160")]
    [Trait("Req", "LANG-660")]
    public void RoundTrip_Interfaces()
    {
        const string Empty = "program P;\nuses U;\nbegin\nend.\n";
        var pcus = Pcus(Files(u: InterfaceUnit, program: Empty));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Empty)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")))
            .Add(@"C:\lib\U.pcu", pcus["U"]).Add(@"C:\lib\U.obj", "")
            .Add(@"C:\lib\V.pcu", pcus["V"]).Add(@"C:\lib\V.obj", "");

        var again = Compilation.Create(Options() with { SearchPaths = [@"C:\lib"] }, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => t.Text);

        Assert.Contains("\"kind\":\"interface\"", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"ofObject\":true", pcus["U"], StringComparison.Ordinal);                     // C-103
        Assert.Contains("\"guid\":\"{0A1B2C3D-0000-1111-2222-333344445555}\"", pcus["U"], StringComparison.Ordinal);
        Assert.Matches("\"name\":\"Sides\",\"type\":\\d+,\"read\":7,\"write\":null,\"index\":null", pcus["U"]);
        Assert.Equal(pcus["U"], again["U"]);
    }

    /// <summary>
    /// C-106…C-108: open array and <c>array of const</c> parameters (element type + <c>openArray</c>, <c>arrayOfConst</c>) and a
    /// variant record (<c>variant</c>, overlapping offsets) survive write → import → write byte for byte.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-160")]
    [Trait("Req", "LANG-430")]
    [Trait("Req", "ABI-061")]
    public void RoundTrip_OpenArraysAndVariantRecords()
    {
        const string Unit = """
            unit U;
            interface
            uses V;
            type
              TOverlay = record case Byte of 0: (A: Integer); 1: (B: Int64) end;
            function Sum(const A: array of Integer; var B: array of Byte): Integer; overload;
            procedure Show(const Args: array of const); overload;
            implementation
            function Sum(const A: array of Integer; var B: array of Byte): Integer; begin Result := High(A) end;
            procedure Show(const Args: array of const); begin end;
            end.
            """;
        const string Empty = "program P;\nuses U;\nbegin\nend.\n";
        var pcus = Pcus(Files(u: Unit, program: Empty));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Empty)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")))
            .Add(@"C:\lib\U.pcu", pcus["U"]).Add(@"C:\lib\U.obj", "")
            .Add(@"C:\lib\V.pcu", pcus["V"]).Add(@"C:\lib\V.obj", "");

        var again = Compilation.Create(Options() with { SearchPaths = [@"C:\lib"] }, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => t.Text);

        Assert.Contains("\"linkName\":\"U.Sum$ca:Integer,va:Byte\"", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"linkName\":\"U.Show$ca:const\"", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"openArray\":true,\"arrayOfConst\":true", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"variant\":true", pcus["U"], StringComparison.Ordinal);
        Assert.Equal(pcus["U"], again["U"]);
    }

    /// <summary>C-202: untyped parameters are written as the builtin <c>untyped</c> and read back (overloads mangle it as a type).</summary>
    [Fact]
    [Trait("Req", "CMP-160")]
    [Trait("Req", "LANG-420")]
    public void RoundTrip_UntypedParameters()
    {
        const string Unit = """
            unit U;
            interface
            uses V;
            procedure Fill(var Data; Count: Integer); overload;
            procedure Fill(const Source; out Dest); overload;
            implementation
            procedure Fill(var Data; Count: Integer); begin end;
            procedure Fill(const Source; out Dest); begin end;
            end.
            """;
        const string Empty = "program P;\nuses U;\nbegin\nend.\n";
        var pcus = Pcus(Files(u: Unit, program: Empty));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Empty)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")))
            .Add(@"C:\lib\U.pcu", pcus["U"]).Add(@"C:\lib\U.obj", "")
            .Add(@"C:\lib\V.pcu", pcus["V"]).Add(@"C:\lib\V.obj", "");

        var again = Compilation.Create(Options() with { SearchPaths = [@"C:\lib"] }, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => t.Text);

        Assert.Contains("\"linkName\":\"U.Fill$v:untyped,Integer\"", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"linkName\":\"U.Fill$c:untyped,o:untyped\"", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"builtin\",\"name\":\"untyped\"}", pcus["U"], StringComparison.Ordinal);
        Assert.Equal(pcus["U"], again["U"]);
    }

    // ------------------------------------------------------------------ Emit writes it (CMP-141, toolchain.md §6)

    [Fact]
    [Trait("Req", "CMP-141")]
    public void Write_LlStage_WritesNoPcu()
    {
        var fs = Files();

        var result = Compilation.Create(Options(), [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.True(fs.FileExists(@"C:\p\obj\U.ll"));
        Assert.False(fs.FileExists(@"C:\p\obj\U.pcu"));
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-141")]
    public void Write_ObjStage_WritesEveryUnitsPcuNextToItsObj()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var fs = Files();
        var options = Options() with { ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!, ObjectDirectory = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")) };
        var expected = Compilation.Create(options, [], fs).PcuTexts();
        var physical = new ObjOnDisk(fs, options.ObjectDirectory);

        try
        {
            var result = Compilation.Create(options, [], physical).Emit(new EmitOptions { Stage = EmitStage.Obj }, TestContext.Current.CancellationToken);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
            foreach (var (unit, text) in expected)
            {
                Assert.True(File.Exists(Path.Combine(options.ObjectDirectory, unit + ".obj")), unit);
                Assert.Equal(text, File.ReadAllText(Path.Combine(options.ObjectDirectory, unit + ".pcu")));
            }
        }
        finally
        {
            Directory.Delete(options.ObjectDirectory, recursive: true);
        }
    }

    private static string ReplaceOnce(string text, string from, string to)
    {
        var at = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(at >= 0, from);
        return text[..at] + to + text[(at + from.Length)..];
    }

    /// <summary>Sources in memory, the object directory on disk (clang reads and writes real files).</summary>
    private sealed class ObjOnDisk(InMemoryFileSystem sources, string objectDirectory) : IFileSystem
    {
        private bool InObj(string path) => Path.GetFullPath(path).StartsWith(objectDirectory, StringComparison.OrdinalIgnoreCase);

        private IFileSystem For(string path) => InObj(path) ? PhysicalFileSystem.Instance : sources;

        public bool FileExists(string path) => For(path).FileExists(path) || (!InObj(path) && PhysicalFileSystem.Instance.FileExists(path));

        public string ReadAllText(string path) => For(path).FileExists(path) ? For(path).ReadAllText(path) : PhysicalFileSystem.Instance.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => For(path).FileExists(path) ? For(path).ReadAllBytes(path) : PhysicalFileSystem.Instance.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => For(directory).EnumerateFiles(directory, searchPattern);

        public IEnumerable<string> EnumerateDirectories(string directory) => For(directory).EnumerateDirectories(directory);

        public DateTime GetLastWriteTimeUtc(string path) => For(path).GetLastWriteTimeUtc(path);

        public long GetLength(string path) => For(path).GetLength(path);

        public void WriteAllBytes(string path, byte[] data) => For(path).WriteAllBytes(path, data);

        public void Delete(string path) => For(path).Delete(path);

        public void CreateDirectory(string path) => For(path).CreateDirectory(path);
    }
}
