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
public sealed partial class PcuTests
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
            .AddSystem();

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

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1055")]
    [Trait("Req", "LANG-542")]
    public void LibraryKindAndExportsRecorded()
    {
        const string library = """
            library P;
            uses U;
            type TVec3 = record X, Y, Z: Integer; end;
            function MakeVec(X: Integer): TVec3; cdecl; begin Result.X := X; end;
            function Version: Integer; public name 'lib_version'; begin Result := 1; end;
            exports
              MakeVec name 'make_vec' index 7,
              U.Add,
              Version name 'lib_version',
              U.Add name 'add2';
            end.
            """;
        var fs = Files(program: library);
        var pcu = Pcu("P", fs);

        Assert.Equal("library", (string)pcu["kind"]!);
        Assert.Equal("library", (string)pcu["appType"]!);
        Assert.Equal("""["System","V","U"]""", pcu["unitOrder"]!.ToJsonString());
        // ordinal order; "symbol" is the link name of the routine itself (the thunk's name is "name")
        Assert.Equal(
            """[{"name":"Add","index":1,"symbol":"U.Add"},{"name":"lib_version","index":2,"symbol":"lib_version"},{"name":"add2","index":3,"symbol":"U.Add"},{"name":"make_vec","index":7,"symbol":"P.MakeVec"}]""",
            pcu["exports"]!.ToJsonString());
        Assert.Equal("exports", ((JsonObject)pcu).Select(p => p.Key).Last());
        Assert.Null(Pcu("U", fs)["exports"]);
        Assert.Null(Pcu("P")["exports"]);                                           // a program records none

        // LANG-542: the reader treats the kind as "not a unit" - a library's .pcu on the search path cannot be used
        Assert.True(PcuReader.IsLibrary(pcu));
        Assert.False(PcuReader.IsLibrary(Pcu("U", fs)));
        Assert.False(PcuReader.IsLibrary(Pcu("P")));
        var user = new InMemoryFileSystem()
            .Add(Path.Combine(Dir, "Host.pas"), "program Host;\nuses P;\nbegin\nend.\n")
            .Add(Path.Combine(Dir, "U.pas"), UnitU)
            .Add(Path.Combine(Dir, "V.pas"), UnitV)
            .Add(@"C:\lib\P.pcu", Pcus(fs)["P"])
            .Add(@"C:\lib\P.obj", "")
            .AddSystem();
        var options = Options() with { MainFile = Path.Combine(Dir, "Host.pas"), SearchPaths = [@"C:\lib"] };
        var diagnostics = Compilation.Create(options, [], user).GetDiagnostics(null, TestContext.Current.CancellationToken);
        Assert.Equal(["E2217: 'P' is a library, not a unit"], diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code + ": " + d.Message));
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
                """{"kind":"routine","name":"Ticks","linkName":"GetTickCount","params":[],"result":"Cardinal","flags":["external","cc:StdCall"],"dll":"kernel32.dll","importName":"GetTickCount"}""",
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
            .AddSystem()
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
            .AddSystem()
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
            .AddSystem()
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
            .AddSystem()
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
            .AddSystem()
            .Add(@"C:\lib\U.pcu", pcus["U"]).Add(@"C:\lib\U.obj", "")
            .Add(@"C:\lib\V.pcu", pcus["V"]).Add(@"C:\lib\V.obj", "");

        var again = Compilation.Create(Options() with { SearchPaths = [@"C:\lib"] }, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => t.Text);

        Assert.Contains("\"linkName\":\"U.Fill$v:untyped,Integer\"", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"linkName\":\"U.Fill$c:untyped,o:untyped\"", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"builtin\",\"name\":\"untyped\"}", pcus["U"], StringComparison.Ordinal);
        Assert.Equal(pcus["U"], again["U"]);
    }

    // ------------------------------------------------------------------ generics (C-119, CMP-162)

    internal const string GenericUnit = """
        unit U;
        interface
        type
          TBase = class
          end;
          TBox<T: class> = class
          private
            FItem: T;
          public
            class var Made: Integer;
            constructor Create(const AItem: T);
            function Item: T;
            function Describe: string;
          end;
          TPair<K, V> = record
            Key: K;
            Value: V;
          end;
        function Pair(const S: string; N: Integer): TPair<string, Integer>;
        implementation
        const
          Prefix = 'box#';
        var
          Counter: Integer;
        function Next: Integer;
        begin
          Inc(Counter);
          Result := Counter;
        end;
        constructor TBox<T>.Create(const AItem: T);
        begin
          inherited Create;
          FItem := AItem;
          Inc(Made);
        end;
        function TBox<T>.Item: T;
        begin
          Result := FItem;
        end;
        function TBox<T>.Describe: string;
        begin
          if Next > 1 then
            Result := Prefix + 'again'
          else
            Result := Prefix + 'first';
        end;
        function Pair(const S: string; N: Integer): TPair<string, Integer>;
        begin
          Result.Key := S;
          Result.Value := N;
        end;
        end.
        """;

    internal const string GenericProgram = """
        program P;
        uses U;
        var
          B: TBox<TBase>;
          Q: TPair<string, Integer>;
        begin
          B := TBox<TBase>.Create(TBase.Create);
          Writeln(B.Describe, B.Describe, TBox<TBase>.Made);
          Q := Pair('a', 1);
          Writeln(Q.Key, Q.Value);
        end.
        """;

    /// <summary>
    /// CMP-162 (C-119): each generic's declaration and method implementations are written as source pieces at their original
    /// positions, and the implementation-only routines, variables and constants they name are written as <c>genericSupport</c>
    /// symbols (with their link names: instances in other modules call them).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-162")]
    public void GenericBodies_AreWrittenWithTheSymbolsTheyNeed()
    {
        var pcu = Pcu("U", Files(u: GenericUnit, program: GenericProgram));

        var bodies = pcu["genericBodies"]!.AsArray();
        Assert.Equal(2, bodies.Count);
        var box = bodies[0]!;
        Assert.Equal("TBox", (string)pcu["interface"]!["types"]![(int)box["symbol"]!]!["name"]!);
        var text = (string)box["text"]!;
        Assert.StartsWith("unit U;", text, StringComparison.Ordinal);
        Assert.Contains("TBox<T: class> = class", text, StringComparison.Ordinal);
        Assert.Contains("implementation", text, StringComparison.Ordinal);
        Assert.Contains("function TBox<T>.Describe: string;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("function Next", text, StringComparison.Ordinal);
        Assert.DoesNotContain("function Pair", text, StringComparison.Ordinal);
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), (string)box["sha256"]!);
        var pieces = box["pieces"]!.AsArray();
        Assert.Equal((1, 1), ((int)pieces[0]!["line"]!, (int)pieces[0]!["col"]!));              // 'unit U;' where it was
        Assert.Equal(text.Length, pieces.Sum(p => (int)p!["length"]!));
        var support = pcu["interface"]!["genericSupport"]!.AsArray().Select(s => $"{s!["kind"]} {s["name"]}").ToArray();
        Assert.Equal(["const Prefix", "routine Next"], support);
    }

    /// <summary>CMP-162 (C-119): dependents compile the bodies into their instances, so a body edit changes the interface hash.</summary>
    [Fact]
    [Trait("Req", "CMP-152")]
    [Trait("Req", "CMP-162")]
    public void GenericBody_Edit_ChangesTheInterfaceHash_OtherBodiesDoNot()
    {
        var before = Hash("U", Files(u: GenericUnit, program: GenericProgram));

        Assert.NotEqual(before, Hash("U", Files(u: ReplaceOnce(GenericUnit, "Result := FItem;", "Result := nil;"), program: GenericProgram)));
        Assert.Equal(before, Hash("U", Files(u: ReplaceOnce(GenericUnit, "Result.Value := N;", "Result.Value := N + 1;"), program: GenericProgram)));
    }

    /// <summary>CMP-162 (C-119): a program instantiating the generics of a <c>.pcu</c>-only unit gives the same IR as against its source.</summary>
    [Fact]
    [Trait("Req", "CMP-162")]
    [Trait("Req", "LANG-710")]
    public void GenericFromPcu_GivesTheSameIrAsAgainstTheSource() =>
        PcuImportTests.AssertSameIrAgainstPcu(GenericUnit, GenericProgram);

    /// <summary>C-227: generic methods of a plain class (class, instance) are written to the <c>.pcu</c> and instantiated from it.</summary>
    [Fact]
    [Trait("Req", "CMP-162")]
    [Trait("Req", "LANG-716")]
    public void GenericMethodOfPlainClassFromPcu() =>
        PcuImportTests.AssertSameIrAgainstPcu("""
            unit U;
            interface
            type
              TUtil = class
                class function Pick<T>(First: Boolean; const A, B: T): T;
                function Echo<T>(const A: T): T;
              end;
            implementation
            class function TUtil.Pick<T>(First: Boolean; const A, B: T): T;
            begin
              if First then
                Result := A
              else
                Result := B;
            end;
            function TUtil.Echo<T>(const A: T): T;
            begin
              Result := A;
            end;
            end.
            """, """
            program P;
            uses U;
            var
              Util: TUtil;
            begin
              Writeln(TUtil.Pick<Integer>(True, 1, 2));
              Writeln(TUtil.Pick<string>(False, 'a', 'b'));
              Util := TUtil.Create;
              Writeln(Util.Echo<Integer>(5));
            end.
            """);

    /// <summary>
    /// C-232: generic methods of a record (static and instance, one constrained) are written to the <c>.pcu</c> and instantiated
    /// from it, like those of a plain class (C-227).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-162")]
    [Trait("Req", "LANG-716")]
    public void GenericMethodOfRecordFromPcu() =>
        PcuImportTests.AssertSameIrAgainstPcu("""
            unit U;
            interface
            type
              TPair = record
                Left, Right: Integer;
                class function Pick<T>(First: Boolean; const A, B: T): T; static;
                function Scale<T>(const Factor: T): Integer;
                function Plain: Integer;
              end;
            implementation
            class function TPair.Pick<T>(First: Boolean; const A, B: T): T;
            begin
              if First then
                Result := A
              else
                Result := B;
            end;
            function TPair.Scale<T>(const Factor: T): Integer;
            begin
              Result := (Left + Right) * SizeOf(Factor);
            end;
            function TPair.Plain: Integer;
            begin
              Result := Left - Right;
            end;
            end.
            """, """
            program P;
            uses U;
            var
              Pair: TPair;
            begin
              Pair.Left := 3;
              Pair.Right := 4;
              Writeln(TPair.Pick<Integer>(True, 1, 2));
              Writeln(TPair.Pick<string>(False, 'a', 'b'));
              Writeln(Pair.Scale<Byte>(1));
              Writeln(Pair.Scale<Int64>(1));
              Writeln(Pair.Plain);
            end.
            """);

    /// <summary>
    /// C-419 (LANG-980, 441): a type of a <c>.pcu</c> with generic and non-generic methods of one name resolves calls as against
    /// its source — the non-generic method as an equally good match, the generic one otherwise, over an ancestor and a descendant
    /// in both directions (the <c>overload</c> directive of a generic method is read back with its declaration), and the generic
    /// method whose body does not compile for <c>Integer</c> is not instantiated for the call the non-generic method takes.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-980")]
    [Trait("Req", "CMP-1477")]
    public void MixedGenericOverloadsFromPcu() =>
        PcuImportTests.AssertSameIrAgainstPcu("""
            unit U;
            interface
            type
              TBase = class
                class procedure Log<T>(const V: T); overload; static;
                class procedure Note(I: Integer); overload; static;
              end;
              TOut = class(TBase)
                class procedure Show(I: Integer); overload; static;
                class procedure Show<T>(const V: T); overload; static;
                class procedure Log(I: Integer); overload; static;
                class procedure Note<T>(const V: T); overload; static;
                class procedure Take(I: Integer); overload; static;
                class procedure Take<T>(const V: T); overload; static;
              end;
              TRec = record
                procedure Put(I: Integer); overload;
                procedure Put<T>(const V: T); overload;
              end;
            implementation
            class procedure TBase.Log<T>(const V: T); begin Writeln('Log<T> ', SizeOf(T)); end;
            class procedure TBase.Note(I: Integer); begin Writeln('Note ', I); end;
            class procedure TOut.Show(I: Integer); begin Writeln('Show ', I); end;
            class procedure TOut.Show<T>(const V: T); begin Writeln('Show<T> ', SizeOf(T)); end;
            class procedure TOut.Log(I: Integer); begin Writeln('Log ', I); end;
            class procedure TOut.Note<T>(const V: T); begin Writeln('Note<T> ', SizeOf(T)); end;
            class procedure TOut.Take(I: Integer); begin Writeln('Take ', I); end;
            class procedure TOut.Take<T>(const V: T); begin V.Free; end;
            procedure TRec.Put(I: Integer); begin Writeln('Put ', I); end;
            procedure TRec.Put<T>(const V: T); begin Writeln('Put<T> ', SizeOf(T)); end;
            end.
            """, """
            program P;
            uses U;
            var
              B: Byte;
              R: TRec;
            begin
              B := 1;
              TOut.Show(5);
              TOut.Show('xy');
              TOut.Show(B);
              TOut.Show<Integer>(5);
              TOut.Log(5);
              TOut.Log('xy');
              TOut.Note(5);
              TOut.Note('xy');
              TOut.Take(5);
              R.Put(5);
              R.Put(B);
            end.
            """);

    /// <summary>
    /// C-227: a generic body naming an implementation-only type and variable and an enumeration of a unit used only by the
    /// implementation part compiles from the <c>.pcu</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-162")]
    public void GenericBodyUsesImplementationType() =>
        PcuImportTests.AssertSameIrAgainstPcu("""
            unit U;
            interface
            type
              TBag<T> = class
                function Count(const A: T): Integer;
              end;
            implementation
            uses V;
            type
              TTally = record
                N: Integer;
              end;
            var
              Tally: TTally;
            function TBag<T>.Count(const A: T): Integer;
            var
              Local: TTally;
            begin
              Local.N := Ord(Two);
              Inc(Tally.N, Local.N);
              Result := Tally.N;
            end;
            end.
            """, """
            program P;
            uses U;
            var
              B: TBag<Integer>;
            begin
              B := TBag<Integer>.Create;
              Writeln(B.Count(1));
            end.
            """);

    /// <summary>
    /// C-227: a conditional inside a stored generic body is evaluated with the declaring compilation's defines (the stored text
    /// is the resolved one), not with the instantiating compilation's.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-162")]
    public void GenericBodyIfdefFrozen()
    {
        const string Unit = """
            unit U;
            interface
            {$DEFINE FAST}
            type
              TMode<T> = class
                class function Name: string;
              end;
            implementation
            class function TMode<T>.Name: string;
            begin
              {$IFDEF FAST}
              Result := 'fast';
              {$ELSE}
              Result := 'slow';
              {$ENDIF}
            end;
            end.
            """;
        const string Program = "program P;\nuses U;\nbegin\n  Writeln(TMode<Integer>.Name);\nend.\n";

        var text = (string)Pcu("U", Files(u: Unit, program: Program))["genericBodies"]![0]!["text"]!;
        Assert.Contains("'fast'", text, StringComparison.Ordinal);
        Assert.DoesNotContain("'slow'", text, StringComparison.Ordinal);
        PcuImportTests.AssertSameIrAgainstPcu(Unit, Program);
    }

    internal const string NestedClassUnit = """
        unit U;
        interface
        type
          TOuter = class
          strict private
            class var FCount: Integer;
          public
            type
              IInner = interface
                ['{6B2D3C41-7E0A-4F55-9C1B-2A7D8E9F0A11}']
                function Get: Integer;
              end;
              TInner = class(TInterfacedObject, IInner)
              private
                FValue: Integer;
              public
                constructor Create(AValue: Integer);
                function Get: Integer;
                class function Total: Integer;
              end;
              TPoint = record
                X, Y: Integer;
              end;
              TA = class
              public
                type
                  TB = class
                    function Deep: string;
                  end;
              end;
            function Make(N: Integer): TInner;
          end;
        implementation
        constructor TOuter.TInner.Create(AValue: Integer);
        begin
          inherited Create;
          FValue := AValue;
          Inc(FCount);
        end;
        function TOuter.TInner.Get: Integer;
        begin
          Result := FValue;
        end;
        class function TOuter.TInner.Total: Integer;
        begin
          Result := FCount;
        end;
        function TOuter.TA.TB.Deep: string;
        begin
          Result := ClassName;
        end;
        function TOuter.Make(N: Integer): TInner;
        begin
          Result := TInner.Create(N);
        end;
        end.
        """;

    /// <summary>
    /// CMP-1392 a (C-344): the nested class, interface and record types of an interface-part class are written as type entries
    /// attached to their enclosing type and read back so that <c>TOuter.TInner</c> binds and links as against the source.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    [Trait("Req", "LANG-775")]
    public void NestedClassOfInterfaceClassFromPcu()
    {
        const string Program = """
            program P;
            uses U;
            var
              O: TOuter;
              I: TOuter.TInner;
              X: U.TOuter.IInner;
              Pt: TOuter.TPoint;
              B: TOuter.TA.TB;
              Obj: TObject;
            begin
              O := TOuter.Create;
              I := O.Make(3);
              Obj := I;
              Writeln(Obj is TOuter.TInner, I.Get, TOuter.TInner.Total, I.ClassName);
              X := TOuter.TInner.Create(4);
              Writeln(X.Get);
              Pt.X := 1;
              B := TOuter.TA.TB.Create;
              Writeln(B.Deep);
            end.
            """;

        var pcu = Pcu("U", Files(u: NestedClassUnit, program: Program));
        var types = (JsonArray)pcu["interface"]!["types"]!;
        var outer = types.Single(t => (string?)t!["name"] == "TOuter")!;
        var nested = ((JsonArray)outer["nested"]!).Select(n => ((string)n!["name"]!, (string)types[(int)n["type"]!]!["kind"]!)).ToList();
        Assert.Equal([("IInner", "interface"), ("TInner", "class"), ("TPoint", "record"), ("TA", "class")], nested);
        PcuImportTests.AssertSameIrAgainstPcu(NestedClassUnit, Program);
    }

    /// <summary>
    /// CMP-1392 c (C-344): a <c>.pcu</c> as written before this card — the same file without the <c>nested</c> keys — is still read;
    /// a program that names no nested type compiles against it to the same IR as against the source.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    public void OldPcuWithoutNestedClasses_StillReads()
    {
        const string Program = "program P;\nuses U;\nvar\n  O: TOuter;\nbegin\n  O := TOuter.Create;\n  Writeln(O.Make(2).Get);\nend.\n";
        var sources = Files(u: NestedClassUnit, program: Program);
        var reference = Compilation.Create(Options(), [], sources).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(reference.Success, string.Join("\n", reference.Diagnostics.Select(d => d.ToText())));
        var old = Pcu("U", Files(u: NestedClassUnit, program: Program));
        var stripped = 0;
        foreach (var type in ((JsonArray)old["interface"]!["types"]!).OfType<JsonObject>())
        {
            stripped += type.Remove("nested") ? 1 : 0;
        }

        Assert.Equal(2, stripped);                                                     // TOuter and TOuter.TA
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", Program).AddSystem()
            .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), old.ToJsonString() + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");

        var result = Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
            .Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System"], result.CompiledUnits);
        Assert.Equal(sources.ReadAllText(@"C:\p\obj\P.ll"), fs.ReadAllText(@"C:\p\obj\P.ll"));
    }

    /// <summary>
    /// CMP-1392, LANG-774 (C-359): a nested type alias is written with its section's visibility and read back with it, so naming a
    /// private or strict protected alias from another unit is E2610 against the <c>.pcu</c> as against the source; a <c>.pcu</c> written
    /// before (alias entries without <c>visibility</c>) reads the aliases as public.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    [Trait("Req", "LANG-774")]
    public void NestedAliasVisibilityFromPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              TOuter = class
              private
                type TPriv = Byte;
              strict protected
                type TProt = Word;
              public
                type TOpen = Integer;
              end;
            implementation
            end.
            """;
        const string Program = "program P;\nuses U;\nvar\n  A: TOuter.TPriv;\n  B: TOuter.TProt;\n  C: TOuter.TOpen;\nbegin\n  Writeln(SizeOf(TOuter.TPriv) + SizeOf(B) + SizeOf(C));\nend.\n";
        string[] expected =
        [
            "P.pas(4,13): E2610 Cannot access private member 'TPriv' of 'TOuter'",
            "P.pas(5,13): E2610 Cannot access strict protected member 'TProt' of 'TOuter'",
            "P.pas(8,25): E2610 Cannot access private member 'TPriv' of 'TOuter'",
        ];
        var sources = Files(u: Unit, program: Program);
        Assert.Equal(expected, Errors(Compilation.Create(Options(), [], sources).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken)));

        var pcu = Pcu("U", Files(u: Unit, program: "program P;\nuses U;\nbegin\nend.\n"));
        var outer = ((JsonArray)pcu["interface"]!["types"]!).Single(t => (string?)t!["name"] == "TOuter")!;
        var nested = ((JsonArray)outer["nested"]!).Select(n => ((string)n!["name"]!, (bool?)n["alias"], (string?)n["visibility"])).ToList();
        Assert.Equal([("TPriv", true, "private"), ("TProt", true, "strict protected"), ("TOpen", true, "public")], nested);
        Assert.Equal(expected, Errors(AgainstPcu(pcu.ToJsonString())));

        foreach (var entry in ((JsonArray)outer["nested"]!).OfType<JsonObject>())
        {
            entry.Remove("visibility");
        }

        var old = AgainstPcu(pcu.ToJsonString());
        Assert.True(old.Success, string.Join("\n", old.Diagnostics.Select(d => d.ToText())));     // U.pcu only: no U.pas in that file system

        EmitResult AgainstPcu(string text)
        {
            var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", Program).AddSystem()
                .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), text + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
            return Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
                .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        }

        static string[] Errors(EmitResult result) =>
        [
            .. result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"),
        ];
    }

    /// <summary>
    /// CMP-1392, LANG-774 (C-361): a class's nested constants are written with their value and their section's visibility and read
    /// back with them, so naming a private or strict protected constant from another unit is E2610 against the <c>.pcu</c> as against
    /// the source (also in a constant expression; a helper's too) and a public one keeps its value; a constant entry without
    /// <c>visibility</c> (an older writer) reads as public.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    [Trait("Req", "LANG-774")]
    public void NestedConstantVisibilityFromPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              TOuter = class
              private
                const Priv = 1;
              strict protected
                const Prot = 2;
              public
                const Open = 40;
              end;
              TIntHelper = record helper for Integer
              strict private
                const Hidden = 5;
              public
                const Shown = 6;
              end;
            implementation
            end.
            """;
        const string Program = "program P;\nuses U;\nconst\n  C = TOuter.Priv + 1;\n  D = TOuter.Open + 2;\nbegin\n  Writeln(TOuter.Prot + D + C);\n  Writeln(Integer.Shown + Integer.Hidden);\nend.\n";
        string[] expected =
        [
            "P.pas(4,14): E2610 Cannot access private member 'Priv' of 'TOuter'",
            "P.pas(7,18): E2610 Cannot access strict protected member 'Prot' of 'TOuter'",
            "P.pas(8,35): E2610 Cannot access strict private member 'Hidden' of 'TIntHelper'",
        ];
        var sources = Files(u: Unit, program: Program);
        Assert.Equal(expected, Errors(Compilation.Create(Options(), [], sources).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken)));

        var pcu = Pcu("U", Files(u: Unit, program: "program P;\nuses U;\nbegin\nend.\n"));
        var outer = ((JsonArray)pcu["interface"]!["types"]!).Single(t => (string?)t!["name"] == "TOuter")!;
        var nested = ((JsonArray)outer["nested"]!).Select(n => ((string)n!["name"]!, (bool?)n["const"], (string?)n["visibility"], n["value"]?.ToJsonString())).ToList();
        Assert.Equal([("Priv", true, "private", "1"), ("Prot", true, "strict protected", "2"), ("Open", true, "public", "40")], nested);
        Assert.Equal(expected, Errors(AgainstPcu(pcu.ToJsonString(), Program)));

        const string Uses = "program P;\nuses U;\nconst\n  D = TOuter.Open + 2;\nvar\n  A: array[0..D] of Byte;\nbegin\n  Writeln(SizeOf(A) + TOuter.Open);\nend.\n";
        var open = AgainstPcu(pcu.ToJsonString(), Uses);
        Assert.True(open.Success, string.Join("\n", open.Diagnostics.Select(d => d.ToText())));

        foreach (var entry in ((JsonArray)pcu["interface"]!["types"]!).SelectMany(t => t!["nested"] as JsonArray ?? []).OfType<JsonObject>())
        {
            entry.Remove("visibility");
        }

        var old = AgainstPcu(pcu.ToJsonString(), Program);
        Assert.True(old.Success, string.Join("\n", old.Diagnostics.Select(d => d.ToText())));     // U.pcu only: no U.pas in that file system

        EmitResult AgainstPcu(string text, string program)
        {
            var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem()
                .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), text + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
            return Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
                .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        }

        static string[] Errors(EmitResult result) =>
        [
            .. result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"),
        ];
    }

    /// <summary>
    /// CMP-1392, LANG-774 (C-363): the nested types of a helper are written with <c>outer</c> (the helper) and read back inside it with
    /// their section's visibility, so naming a private, strict private or strict protected one from another unit is E2610 against the
    /// <c>.pcu</c> as against the source and a public one stays usable; an entry without <c>outer</c> (an older writer) reads as public.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    [Trait("Req", "LANG-774")]
    public void HelperNestedTypeVisibilityFromPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              TIntHelper = record helper for Integer
              private
                type TPriv = record X: Byte; end;
              strict private
                type TSmall = (sA, sB);
              strict protected
                type TRange = 0..9;
              public
                type TOpen = (oA, oB, oC);
              end;
            implementation
            end.
            """;
        const string Program = "program P;\nuses U;\nvar\n  A: TIntHelper.TPriv;\n  B: TIntHelper.TOpen;\nbegin\n"
            + "  Writeln(SizeOf(TIntHelper.TSmall) + SizeOf(A) + SizeOf(B) + SizeOf(TIntHelper.TRange));\nend.\n";
        string[] expected =
        [
            "P.pas(4,17): E2610 Cannot access private member 'TPriv' of 'TIntHelper'",
            "P.pas(7,29): E2610 Cannot access strict private member 'TSmall' of 'TIntHelper'",
            "P.pas(7,81): E2610 Cannot access strict protected member 'TRange' of 'TIntHelper'",
        ];
        Assert.Equal(expected, Errors(Compilation.Create(Options(), [], Files(u: Unit, program: Program))
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken)));

        var pcu = Pcu("U", Files(u: Unit, program: "program P;\nuses U;\nbegin\nend.\n"));
        var types = (JsonArray)pcu["interface"]!["types"]!;
        var helper = types.Single(t => (string?)t!["name"] == "TIntHelper")!;
        int? helperId = (int)helper["id"]!;
        var nested = ((JsonArray)helper["nested"]!).Select(n => ((string)n!["name"]!, (string?)n["visibility"], (int?)types[(int)n["type"]!]!["outer"])).ToList();
        Assert.Equal([("TPriv", "private", helperId), ("TSmall", "strict private", helperId), ("TRange", "strict protected", helperId), ("TOpen", "public", helperId)], nested);
        Assert.Equal(expected, Errors(AgainstPcu(pcu.ToJsonString(), Program)));

        const string Uses = "program P;\nuses U;\nvar\n  B: TIntHelper.TOpen;\nbegin\n  B := High(TIntHelper.TOpen);\n  Writeln(Ord(B));\nend.\n";
        var open = AgainstPcu(pcu.ToJsonString(), Uses);
        Assert.True(open.Success, string.Join("\n", open.Diagnostics.Select(d => d.ToText())));

        foreach (var entry in types.OfType<JsonObject>().Where(t => (int?)t["outer"] == helperId))
        {
            entry.Remove("outer");
        }

        var old = AgainstPcu(pcu.ToJsonString(), Program);
        Assert.True(old.Success, string.Join("\n", old.Diagnostics.Select(d => d.ToText())));     // U.pcu only: no U.pas in that file system

        EmitResult AgainstPcu(string text, string program)
        {
            var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem()
                .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), text + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
            return Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
                .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        }

        static string[] Errors(EmitResult result) =>
        [
            .. result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"),
        ];
    }

    /// <summary>
    /// CMP-1392 (C-365): a helper's nested type used by another unit's interface is written there as a reference by its path
    /// (<c>"kind": "ref"</c>, <c>TIntHelper.TOpen</c>), as for the nested types of classes and records, not described a second time;
    /// read back, it is the helper's own type, so values pass between the two units' declarations.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    public void HelperNestedTypeReferencedByPath()
    {
        const string Unit = """
            unit U;
            interface
            type
              TIntHelper = record helper for Integer
              public
                type
                  TOpen = (oA, oB, oC);
                  TCell = record
                    V: Integer;
                  end;
              end;
            implementation
            end.
            """;
        const string User = """
            unit V;
            interface
            uses U;
            var
              Pick: TIntHelper.TOpen;
            function Make(N: Integer): TIntHelper.TCell;
            implementation
            function Make(N: Integer): TIntHelper.TCell;
            begin
              Result.V := N;
            end;
            end.
            """;
        const string Program = "program P;\nuses U, V;\nvar\n  B: TIntHelper.TOpen;\n  C: TIntHelper.TCell;\nbegin\n"
            + "  Pick := High(TIntHelper.TOpen);\n  B := Pick;\n  C := Make(3);\n  Writeln(Ord(B), C.V);\nend.\n";
        var pcus = Pcus(Files(u: Unit, v: User, program: Program));
        var types = (JsonArray)JsonNode.Parse(pcus["V"])!["interface"]!["types"]!;
        var refs = types.OfType<JsonObject>().Where(t => (string?)t["kind"] == "ref").Select(t => ((string)t["unit"]!, (string)t["name"]!)).ToList();
        Assert.Contains(("U", "TIntHelper.TOpen"), refs);
        Assert.Contains(("U", "TIntHelper.TCell"), refs);
        Assert.DoesNotContain(types.OfType<JsonObject>(), t => (string?)t["kind"] != "ref" && (string?)t["name"] is "TOpen" or "TCell");

        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", Program).AddSystem()
            .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), pcus["U"]).Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "")
            .Add(Path.Combine(PcuImportTests.Lib, "V.pcu"), pcus["V"]).Add(Path.Combine(PcuImportTests.Lib, "V.obj"), "");
        var result = Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
    }

    /// <summary>
    /// CMP-1392 (C-370): the elements of an unscoped enumeration nested in a class or a helper are members of the outer type, so
    /// <c>TOuter.eC</c> and <c>TIntHelper.fC</c> bind against the <c>.pcu</c> as against the source (with the enumeration's type and
    /// its section's visibility: an element of a strict private one is E2610 from another unit in both).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    public void NestedEnumElementThroughOuterType()
    {
        const string Unit = """
            unit U;
            interface
            type
              TOuter = class
              public
                type TE = (eA, eB, eC);
              end;
              TIntHelper = record helper for Integer
              strict private
                type THidden = (hA, hB);
              public
                type TF = (fA, fB, fC);
              end;
            implementation
            end.
            """;
        const string Program = "program P;\nuses U;\nconst\n  N = Ord(TOuter.eC) + Ord(TIntHelper.fB);\nvar\n  E: TOuter.TE;\n  F: TIntHelper.TF;\n"
            + "  A: array[0..N] of Byte;\nbegin\n  E := TOuter.eC;\n  F := TIntHelper.fC;\n  Writeln(Ord(E) + Ord(F) + SizeOf(A));\nend.\n";
        const string Hidden = "program P;\nuses U;\nbegin\n  Writeln(Ord(TIntHelper.hB));\nend.\n";
        string[] hiddenErrors = ["P.pas(4,26): E2610 Cannot access strict private member 'hB' of 'TIntHelper'"];

        var source = Compilation.Create(Options(), [], Files(u: Unit, program: Program))
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        Assert.True(source.Success, string.Join("\n", source.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(hiddenErrors, Errors(Compilation.Create(Options(), [], Files(u: Unit, program: Hidden))
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken)));

        var pcu = Pcu("U", Files(u: Unit, program: "program P;\nuses U;\nbegin\nend.\n")).ToJsonString();
        var fromPcu = AgainstPcu(pcu, Program);
        Assert.True(fromPcu.Success, string.Join("\n", fromPcu.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(hiddenErrors, Errors(AgainstPcu(pcu, Hidden)));

        EmitResult AgainstPcu(string text, string program)
        {
            var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem()
                .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), text + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
            return Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
                .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        }

        static string[] Errors(EmitResult result) =>
        [
            .. result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"),
        ];
    }

    /// <summary>
    /// CMP-1392 (C-372): an anonymous enumeration inside a nested type (<c>type TS = set of (sA, sB, sC);</c> in a class or a helper)
    /// has its elements as members of the outer type, so <c>TOuter.sC</c> binds against the <c>.pcu</c> as against the source, with
    /// the set's element type and the section's visibility (an element of a strict private one is E2610 from another unit in both).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1392")]
    public void AnonymousEnumInNestedTypeThroughPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              TOuter = class
              strict private
                type TSecret = set of (xA, xB);
              public
                type TS = set of (sA, sB, sC);
              end;
              TIntHelper = record helper for Integer
              strict private
                type THidden = set of (hA, hB);
              public
                type TF = set of (fA, fB, fC);
              end;
            implementation
            end.
            """;
        const string Program = "program P;\nuses U;\nconst\n  N = Ord(TOuter.sC) + Ord(TIntHelper.fB);\nvar\n  S: TOuter.TS;\n  F: TIntHelper.TF;\n"
            + "  A: array[0..N] of Byte;\nbegin\n  S := [TOuter.sA, TOuter.sC];\n  F := [TIntHelper.fC];\n"
            + "  if (TOuter.sC in S) and not (TOuter.sB in S) and (TIntHelper.fC in F) then Writeln(SizeOf(A));\nend.\n";
        const string Hidden = "program P;\nuses U;\nbegin\n  Writeln(Ord(TIntHelper.hB));\n  Writeln(Ord(TOuter.xB));\nend.\n";
        string[] hiddenErrors =
        [
            "P.pas(4,26): E2610 Cannot access strict private member 'hB' of 'TIntHelper'",
            "P.pas(5,22): E2610 Cannot access strict private member 'xB' of 'TOuter'",
        ];

        var source = Compilation.Create(Options(), [], Files(u: Unit, program: Program))
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        Assert.True(source.Success, string.Join("\n", source.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(hiddenErrors, Errors(Compilation.Create(Options(), [], Files(u: Unit, program: Hidden))
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken)));

        var pcu = Pcu("U", Files(u: Unit, program: "program P;\nuses U;\nbegin\nend.\n")).ToJsonString();
        var fromPcu = AgainstPcu(pcu, Program);
        Assert.True(fromPcu.Success, string.Join("\n", fromPcu.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(hiddenErrors, Errors(AgainstPcu(pcu, Hidden)));

        EmitResult AgainstPcu(string text, string program)
        {
            var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem()
                .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), text + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
            return Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
                .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        }

        static string[] Errors(EmitResult result) =>
        [
            .. result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"),
        ];
    }

    /// <summary>
    /// C-379 (LANG-210): an anonymous enumeration as the type of a field or class var (<c>FMode: (mOff, mOn);</c> in a class or a
    /// record, also in a nested type or a class with nested type sections) declares its elements in the enclosing declaration scope
    /// — the unit — not in the type: <c>mOn</c> and <c>U.mOn</c> bind, the field's section visibility does not apply to them (an
    /// element of a strict private field is reachable), and <c>TC.mOn</c> is E2005; against the <c>.pcu</c> as against the source.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-210")]
    public void AnonymousEnumFieldTypeThroughPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              TC = class
              strict private
                FHidden: (hA, hB);
              public
                FMode: (mOff, mOn, mAuto);
                function IsOn: Boolean;
              end;
              TR = record
                Kind: (kA, kB);
              end;
              TOuter = class
              public
                type TInner = class
                  FIn: (iA, iB);
                end;
                class var Shared: (sA, sB, sC);
                var FOut: (oA, oB);
                function Last: Integer;
              end;
            implementation
            function TC.IsOn: Boolean;
            begin
              FHidden := hB;
              Result := (FMode = mOn) and (FHidden = hB);
            end;
            function TOuter.Last: Integer;
            begin
              FOut := oB;
              Shared := sC;
              Result := Ord(FOut) + Ord(Shared) + Ord(iB);
            end;
            end.
            """;
        const string Program = "program P;\nuses U;\ntype\n  TD = class(TC)\n    procedure SwitchOn;\n  end;\n"
            + "procedure TD.SwitchOn;\nbegin\n  FMode := mOn;\nend;\n"
            + "const\n  N = Ord(mAuto) + Ord(U.kB) + Ord(hB) + Ord(oB) + Ord(iB) + Ord(U.sC);\nvar\n  D: TD;\n  R: TR;\n  O: TOuter;\n"
            + "  I: TOuter.TInner;\n  A: array[0..N] of Byte;\n"
            + "begin\n  D := TD.Create;\n  D.SwitchOn;\n  R.Kind := kB;\n  O := TOuter.Create;\n  I := TOuter.TInner.Create;\n  I.FIn := iB;\n"
            + "  O.FOut := U.oA;\n  TOuter.Shared := sB;\n"
            + "  if D.IsOn and (D.FMode = mOn) and (D.FMode > U.mOff) and (R.Kind = kB) and (I.FIn = iB) and (O.FOut = oA) and (TOuter.Shared = sB) then\n"
            + "    Writeln(SizeOf(A), O.Last);\n  D.Free;\n  O.Free;\n  I.Free;\nend.\n";
        const string Members = "program P;\nuses U;\nbegin\n  Writeln(Ord(TC.mOn));\n  Writeln(Ord(TOuter.oB));\nend.\n";
        string[] memberErrors =
        [
            "P.pas(4,18): E2005 'TC' does not contain a member named 'mOn'",
            "P.pas(5,22): E2005 'TOuter' does not contain a member named 'oB'",
        ];

        var source = Compilation.Create(Options(), [], Files(u: Unit, program: Program))
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        Assert.True(source.Success, string.Join("\n", source.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(memberErrors, Errors(Compilation.Create(Options(), [], Files(u: Unit, program: Members))
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken)));

        var pcu = Pcu("U", Files(u: Unit, program: "program P;\nuses U;\nbegin\nend.\n")).ToJsonString();
        var fromPcu = AgainstPcu(pcu, Program);
        Assert.True(fromPcu.Success, string.Join("\n", fromPcu.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(memberErrors, Errors(AgainstPcu(pcu, Members)));

        EmitResult AgainstPcu(string text, string program)
        {
            var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem()
                .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), text + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
            return Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
                .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        }

        static string[] Errors(EmitResult result) =>
        [
            .. result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"),
        ];
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
    [Trait("Kind", "Native")]
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
            TestDirectories.Delete(options.ObjectDirectory);
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
