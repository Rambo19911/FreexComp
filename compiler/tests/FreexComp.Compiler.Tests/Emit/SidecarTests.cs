using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-153 (CMP-425, debug-info-sidecar.md, debug-info.md §8): the Pascal debug-info sidecar — per-unit fragments written with the
/// debug metadata, the merge into <c>&lt;output&gt;.pdi</c>, the schema of §2–§5, rule DBG-145 (exactly the emitted variables),
/// DBG-146 (merge), the <c>abi</c> block of rtl-abi.md and the layouts the code generator uses (DBG-144).
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Unit")]
public sealed partial class SidecarTests
{
    /// <summary>The demo program of debug-info-sidecar.md §2 (enum, set, record, dynamic array, classes, pointer, routines).</summary>
    internal const string DemoSource = """
        program DbgDemo;

        type
          TColor = (clRed, clGreen, clBlue);
          TColors = set of TColor;
          TPoint = record
            X, Y: Integer;
          end;
          TAnimal = class
          public
            Name: string;
            Legs: Integer;
            procedure Speak;
          end;
          TDog = class(TAnimal)
          end;
          PInt = ^Integer;

        var
          Counter: Integer;
          Colors: TColors;
          Nums: array of Integer;
          Pet: TAnimal;

        function Factorial(N: Integer): Integer;
        begin
          if N <= 1 then
            Result := 1
          else
            Result := N * Factorial(N - 1);
        end;

        procedure TAnimal.Speak;
        var
          P: TPoint;
          Q: PInt;
        begin
          P.X := Legs;
          Q := nil;
        end;

        procedure Show(const S: string; var P: TPoint; C: TColor);
        var
          I, K: Integer;

          procedure Inner;
          begin
            K := K + 1;
          end;

        begin
          for I := 1 to 2 do
            P.X := P.X + I;
          Inner;
        end;

        begin
          Counter := Factorial(5);
        end.
        """;

    /// <summary>Records with padding, a packed record, nested records, a class hierarchy, arrays, sets and strings (DBG-144).</summary>
    internal const string LayoutSource = """
        unit L;
        interface
        type
          TInner = record
            B: Byte;
            D: Double;
          end;
          TRec = record
            A: Byte;
            Inner: TInner;
            W: Word;
            S: string;
            C: Char;
          end;
          TPacked = packed record
            A: Byte;
            I: Integer;
            W: Word;
          end;
          TBase = class
            F1: Byte;
          end;
          TDerived = class(TBase)
            F2: Int64;
            F3: Word;
          end;
          TGrid = array[1..3, 0..4] of Word;
          TShort = string[10];
          TBig = set of 0..200;
          TSmall = set of 0..20;
        var
          R: TRec;
          P: TPacked;
          O: TDerived;
          G: TGrid;
          T: TShort;
          Big: TBig;
          Small: TSmall;
        implementation
        end.
        """;

    private static readonly HashSet<string> Kinds =
    [
        "integer", "float", "boolean", "char", "enum", "set", "string", "staticArray", "dynArray", "openArray", "record", "class", "classRef",
        "pointer", "interface", "procedural", "opaque",
    ];

    private static readonly HashSet<string> VariableKinds = ["param", "varParam", "self", "result", "local"];

    /// <summary>Emits <paramref name="source"/> with debug information; the <c>.ll</c> text and the unit's sidecar fragment.</summary>
    internal static (string Ir, string Fragment, BindingResult Result) EmitWithSidecar(string source, string path, DebugFormat format = DebugFormat.CodeView)
    {
        var tree = SyntaxTree.Parse(SourceText.From(path, source));
        Assert.Empty(tree.Diagnostics);
        var result = BindWithStubSystem(tree);
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        var options = new DebugInfoOptions(format, IsOptimized: false) { ReadFile = p => PathUtil.Comparer.Equals(p, path) ? Encoding.UTF8.GetBytes(source) : null };
        var fragment = IrEmitter.EmitUnit(Lowerer.Lower(result), writer, debug: options);
        Assert.NotNull(fragment);
        return (writer.GetText(), fragment, result);
    }

    private static JsonObject Demo() =>
        JsonNode.Parse(PdiSidecar.Merge(FreexComp.Compiler.Targets.TargetInfo.Win64, "DbgDemo", "codeview", [EmitWithSidecar(DemoSource, @"C:\work\DbgDemo\DbgDemo.pas").Fragment]))!.AsObject();

    private static int LineOf(string source, string text) =>
        Array.FindIndex(source.Split('\n'), l => l.TrimEnd('\r').Equals(text, StringComparison.Ordinal)) + 1;

    // ------------------------------------------------------------------ Sidecar_Schema

    /// <summary>The sidecar of <c>DbgDemo</c> validates against debug-info-sidecar.md §2–§5 and holds the expected routines and variables.</summary>
    [Fact]
    [Trait("Req", "CMP-425")]
    [Trait("Req", "DBG-145")]
    public void Sidecar_Schema()
    {
        var pdi = Demo();

        ValidateSchema(pdi);
        Assert.Equal("DbgDemo", pdi["program"]!.GetValue<string>());
        Assert.Equal("codeview", pdi["container"]!.GetValue<string>());
        var types = pdi["types"]!.AsArray().Select(t => t!.AsObject()).ToDictionary(t => t["id"]!.GetValue<int>());
        JsonObject Named(string name) => types.Values.Single(t => t["name"]!.GetValue<string>() == name);
        string KindOf(JsonNode? id) => types[id!.GetValue<int>()]["kind"]!.GetValue<string>();

        var color = Named("TColor");
        Assert.Equal("enum", color["kind"]!.GetValue<string>());
        Assert.Equal("DbgDemo", color["unit"]!.GetValue<string>());
        Assert.Equal(["clRed:0", "clGreen:1", "clBlue:2"], color["values"]!.AsArray().Select(v => $"{v!["name"]}:{v["value"]}"));
        Assert.Equal(color["id"]!.GetValue<int>(), Named("TColors")["base"]!.GetValue<int>());
        Assert.Equal(["X:0", "Y:4"], Named("TPoint")["fields"]!.AsArray().Select(f => $"{f!["name"]}:{f["offset"]}"));
        var animal = Named("TAnimal");
        Assert.Equal(24, animal["instanceSize"]!.GetValue<long>());
        Assert.Equal(["Name:8:public:string", "Legs:16:public:integer"],
            animal["fields"]!.AsArray().Select(f => $"{f!["name"]}:{f["offset"]}:{f["visibility"]}:{KindOf(f["type"])}"));
        Assert.Equal(animal["id"]!.GetValue<int>(), Named("TDog")["ancestor"]!.GetValue<int>());
        Assert.Equal("integer", KindOf(Named("PInt")["to"]));
        Assert.Null(Named("Integer")["unit"]);

        var unit = pdi["units"]!.AsArray().Single()!.AsObject();
        Assert.Equal("DbgDemo", unit["name"]!.GetValue<string>());
        Assert.Equal(@"C:\work\DbgDemo\DbgDemo.pas", unit["file"]!.GetValue<string>());
        // program-level variables are globals (C-128 decision); the dynamic array's type is an anonymous dynArray
        Assert.Equal(["Colors:set", "Counter:integer", "Nums:dynArray", "Pet:class"],
            unit["globals"]!.AsArray().Select(g => $"{g!["name"]}:{KindOf(g["type"])}"));
        Assert.Equal("DbgDemo.Counter", unit["globals"]![1]!["linkageName"]!.GetValue<string>());

        var routines = unit["routines"]!.AsArray().Select(r => r!.AsObject()).ToArray();
        string Describe(JsonObject r) => $"{r["displayName"]}|{r["linkageName"]}|{r["line"]}|{r["bodyLine"]}|{r["endLine"]}|"
            + $"{(r["self"] is { } self ? KindOf(self) : "-")}|{r["parent"]?.GetValue<string>() ?? "-"}|"
            + string.Join(",", r["vars"]!.AsArray().Select(v => $"{v!["name"]}:{v["kind"]}:{KindOf(v["type"])}"));
        int Line(string text) => LineOf(DemoSource, text);
        Assert.Equal(
            [
                $"DbgDemo|$main|1|{Line("  Counter := Factorial(5);") - 1}|{Line("  Counter := Factorial(5);") + 1}|-|-|",
                $"Factorial|DbgDemo.Factorial|{Line("function Factorial(N: Integer): Integer;")}|{Line("function Factorial(N: Integer): Integer;") + 1}|{Line("function Factorial(N: Integer): Integer;") + 6}|-|-|N:param:integer,Result:result:integer",
                $"TAnimal.Speak|DbgDemo.TAnimal.Speak|{Line("procedure TAnimal.Speak;")}|{Line("procedure TAnimal.Speak;") + 4}|{Line("procedure TAnimal.Speak;") + 7}|class|-|Self:self:class,P:local:record,Q:local:pointer",
                $"Show|DbgDemo.Show|{Line("procedure Show(const S: string; var P: TPoint; C: TColor);")}|{Line("  Inner;") - 3}|{Line("  Inner;") + 1}|-|-|S:param:string,P:varParam:record,C:param:enum,I:local:integer,K:local:integer",
                $"Show.Inner|DbgDemo.Show.Inner|{Line("  procedure Inner;")}|{Line("  procedure Inner;") + 1}|{Line("  procedure Inner;") + 3}|-|DbgDemo.Show|",
            ],
            routines.Select(Describe));
    }

    /// <summary>DBG-145: every <c>DILocalVariable</c> / <c>DIGlobalVariable</c> of the module, with the same spelling, and nothing else.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    [Trait("Req", "DBG-145")]
    public void Sidecar_ListsExactlyTheEmittedVariables(string name)
    {
        var path = name == "Hello" ? @"C:\p\hello.pas" : @"C:\p\M.pas";
        var (ir, fragment, _) = EmitWithSidecar(DebugInfoTests.Sources[name], path);
        var pdi = JsonNode.Parse(fragment)!.AsObject();
        ValidateSchema(pdi);
        var unit = pdi["units"]!.AsArray().Single()!;

        var emittedLocals = LocalVariableRegex().Matches(ir).Select(m => m.Groups[1].Value).Where(n => !n.StartsWith('$')).Order(StringComparer.Ordinal);
        var emittedGlobals = GlobalVariableRegex().Matches(ir).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);
        var emittedRoutines = SubprogramRegex().Matches(ir).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);

        Assert.Equal(emittedLocals, unit["routines"]!.AsArray().SelectMany(r => r!["vars"]!.AsArray()).Select(v => v!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Equal(emittedGlobals, unit["globals"]!.AsArray().Select(g => g!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Equal(emittedRoutines, unit["routines"]!.AsArray().Select(r => r!["linkageName"]!.GetValue<string>()).Order(StringComparer.Ordinal));
    }

    public static TheoryData<string> Names() => [.. DebugInfoTests.Sources.Keys];

    [GeneratedRegex("""!DILocalVariable\(name: "([^"]*)",""")]
    private static partial Regex LocalVariableRegex();

    [GeneratedRegex("""!DIGlobalVariable\(name: "([^"]*)",""")]
    private static partial Regex GlobalVariableRegex();

    [GeneratedRegex("""!DISubprogram\(name: "[^"]*", linkageName: "([^"]*)",""")]
    private static partial Regex SubprogramRegex();

    /// <summary>debug-info-sidecar.md §2–§5: members, kinds, references and sort orders.</summary>
    internal static void ValidateSchema(JsonObject pdi)
    {
        Assert.Equal(1, pdi["formatVersion"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(pdi["program"]!.GetValue<string>()));
        Assert.Contains(pdi["container"]!.GetValue<string>(), new[] { "codeview", "dwarf" });
        Assert.IsType<JsonObject>(pdi["abi"]);
        var types = pdi["types"]!.AsArray().Select(t => t!.AsObject()).ToList();
        var ids = types.Select(t => t["id"]!.GetValue<int>()).ToList();
        Assert.All(ids, id => Assert.True(id > 0));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        void Reference(JsonNode? node, bool nullable = false)
        {
            if (node is null)
            {
                Assert.True(nullable, "a null type reference");
                return;
            }

            Assert.Contains(node.GetValue<int>(), ids);
        }

        foreach (var type in types)
        {
            var kind = type["kind"]!.GetValue<string>();
            Assert.Contains(kind, Kinds);
            Assert.NotNull(type["name"]);
            Assert.True(type["size"]!.GetValue<long>() >= 0);
            if (type.ContainsKey("aliasOf"))
            {
                Reference(type["aliasOf"]);                                        // C-268: an alias names its target entry
                Assert.NotNull(type["unit"]);
            }

            switch (kind)
            {
                case "integer":
                    Assert.NotNull(type["signed"]);
                    break;
                case "float":
                    Assert.Contains(type["format"]!.GetValue<string>(), new[] { "single", "double", "currency", "datetime" });
                    break;
                case "enum":
                    var values = type["values"]!.AsArray().Select(v => v!["value"]!.GetValue<long>()).ToList();
                    Assert.Equal(values.Order(), values);
                    break;
                case "set":
                    Reference(type["base"], nullable: true);
                    Assert.NotNull(type["bitBase"]);
                    break;
                case "string":
                    var flavor = type["flavor"]!.GetValue<string>();
                    Assert.Contains(flavor, new[] { "unicode", "ansi", "short", "pchar", "pansichar" });
                    if (flavor == "short")
                    {
                        Assert.Equal(type["maxLength"]!.GetValue<long>() + 1, type["size"]!.GetValue<long>());
                    }

                    break;
                case "staticArray":
                    Reference(type["element"]);
                    Assert.NotNull(type["low"]);
                    Assert.NotNull(type["count"]);
                    break;
                case "dynArray" or "openArray":
                    Reference(type["element"]);
                    break;
                case "record" or "class":
                    foreach (var field in type["fields"]!.AsArray())
                    {
                        Assert.False(string.IsNullOrEmpty(field!["name"]!.GetValue<string>()));
                        Reference(field["type"]);
                        Assert.True(field["offset"]!.GetValue<long>() >= 0);
                        if (kind == "class")
                        {
                            Assert.NotNull(field["visibility"]);
                        }
                    }

                    if (kind == "class")
                    {
                        Assert.Equal(8, type["size"]!.GetValue<long>());
                        Reference(type["ancestor"], nullable: true);
                        Assert.True(type["instanceSize"]!.GetValue<long>() >= 8);
                    }

                    break;
                case "classRef":
                    Reference(type["of"]);
                    break;
                case "pointer":
                    Reference(type["to"], nullable: true);
                    break;
                case "procedural":
                    Assert.Equal(type["method"]!.GetValue<bool>() ? 16 : 8, type["size"]!.GetValue<long>());
                    break;
            }
        }

        foreach (var unit in pdi["units"]!.AsArray())
        {
            Assert.False(string.IsNullOrEmpty(unit!["name"]!.GetValue<string>()));
            Assert.True(Path.IsPathFullyQualified(unit["file"]!.GetValue<string>()));
            var globals = unit["globals"]!.AsArray().Select(g => g!.AsObject()).ToList();
            Assert.Equal(globals.Select(g => g["name"]!.GetValue<string>()).Order(StringComparer.OrdinalIgnoreCase), globals.Select(g => g["name"]!.GetValue<string>()));
            foreach (var global in globals)
            {
                Reference(global["type"]);
                Assert.False(string.IsNullOrEmpty(global["linkageName"]!.GetValue<string>()));
            }

            var routines = unit["routines"]!.AsArray().Select(r => r!.AsObject()).ToList();
            var order = routines.Select(r => (File: r["file"]!.GetValue<string>(), Line: r["line"]!.GetValue<int>())).ToList();
            Assert.Equal(order.OrderBy(o => o.File, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Line), order);
            foreach (var routine in routines)
            {
                Assert.False(string.IsNullOrEmpty(routine["displayName"]!.GetValue<string>()));
                Assert.False(string.IsNullOrEmpty(routine["linkageName"]!.GetValue<string>()));
                Assert.True(routine.ContainsKey("self"));
                Reference(routine["self"], nullable: true);
                var line = routine["line"]!.GetValue<int>();
                Assert.True(line > 0 && line <= routine["bodyLine"]!.GetValue<int>() && routine["bodyLine"]!.GetValue<int>() <= routine["endLine"]!.GetValue<int>(),
                    $"{routine["displayName"]}: line/bodyLine/endLine out of order");
                foreach (var variable in routine["vars"]!.AsArray())
                {
                    Assert.False(variable!["name"]!.GetValue<string>().StartsWith('$'));
                    Assert.Contains(variable["kind"]!.GetValue<string>(), VariableKinds);
                    Reference(variable["type"]);
                }
            }
        }
    }

    // ------------------------------------------------------------------ Sidecar_Deterministic

    /// <summary>DBG-106: the same sources give byte-identical fragments and merged sidecars (UTF-8 without BOM, LF only).</summary>
    [Fact]
    [Trait("Req", "DBG-106")]
    [Trait("Req", "CMP-425")]
    public void Sidecar_Deterministic()
    {
        foreach (var source in DebugInfoTests.Sources.Values.Append(DemoSource).Append(LayoutSource))
        {
            var first = EmitWithSidecar(source, @"C:\p\M.pas").Fragment;
            var second = EmitWithSidecar(source, @"C:\p\M.pas").Fragment;

            Assert.Equal(first, second);
            Assert.Equal(PdiSidecar.Merge(FreexComp.Compiler.Targets.TargetInfo.Win64, "P", "codeview", [first]), PdiSidecar.Merge(FreexComp.Compiler.Targets.TargetInfo.Win64, "P", "codeview", [second]));
            Assert.DoesNotContain('\r', first);
            Assert.EndsWith("}\n", first, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ merge (DBG-146)

    /// <summary>DBG-146: ids are re-numbered; identical named types and structurally identical unnamed types are unified.</summary>
    [Fact]
    [Trait("Req", "DBG-146")]
    public void Sidecar_Merge_RenumbersAndUnifiesTypes()
    {
        var layout = EmitWithSidecar(LayoutSource, @"C:\p\L.pas").Fragment;
        var demo = EmitWithSidecar(DemoSource, @"C:\p\DbgDemo.pas").Fragment;

        var merged = JsonNode.Parse(PdiSidecar.Merge(FreexComp.Compiler.Targets.TargetInfo.Win64, "DbgDemo", "dwarf", [demo, layout, layout]))!.AsObject();

        ValidateSchema(merged);
        Assert.Equal("dwarf", merged["container"]!.GetValue<string>());
        Assert.Equal(["DbgDemo", "L", "L"], merged["units"]!.AsArray().Select(u => u!["name"]!.GetValue<string>()));
        var types = merged["types"]!.AsArray().Select(t => t!.AsObject()).ToList();
        Assert.Equal(Enumerable.Range(1, types.Count), types.Select(t => t["id"]!.GetValue<int>()));
        Assert.Single(types, t => t["name"]!.GetValue<string>() == "Integer");
        Assert.Single(types, t => t["name"]!.GetValue<string>() == "TRec");
        // the second copy of L adds no type; the unnamed and built-in types of both programs are each described once
        var alone = JsonNode.Parse(PdiSidecar.Merge(FreexComp.Compiler.Targets.TargetInfo.Win64, "L", "codeview", [layout]))!["types"]!.AsArray().Select(t => t!.AsObject()).ToList();
        Assert.Equal(alone.Count(t => t["unit"] is not null), types.Count(t => t["unit"]?.GetValue<string>() == "L"));
        var structures = types.Where(t => t["unit"] is null).Select(t => { var c = (JsonObject)t.DeepClone(); c.Remove("id"); return c.ToJsonString(); }).ToList();
        Assert.Equal(structures.Count, structures.Distinct(StringComparer.Ordinal).Count());
        var units = merged["units"]!.AsArray();
        Assert.Equal(units[1]!.ToJsonString(), units[2]!.ToJsonString());
    }

    // ------------------------------------------------------------------ Sidecar_AbiBlock_MatchesRtlAbi

    /// <summary>
    /// debug-info-sidecar.md §3: the <c>abi</c> block carries the rtl-abi.md values (string header §3.1, dynamic arrays §4, VMT §7.2,
    /// ABI-125) — checked against the expected block, against the RTL source the program links (<c>System._Raise</c>,
    /// <c>_PascalExceptionCode</c>, <c>Exception.FMessage</c>) and against the offsets the code generator inlines (ABI-023, ABI-041).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-425")]
    [Trait("Req", "ABI-125")]
    public void Sidecar_AbiBlock_MatchesRtlAbi()
    {
        const string Expected = """
            {"pointerSize":8,"raiseHookSymbol":"System._Raise","pascalExceptionCode":3763355987,"exceptionMessageField":"FMessage",
            "unicodeString":{"lengthOffset":-12,"lengthSize":4,"lengthUnit":"chars","charSize":2},
            "ansiString":{"lengthOffset":-12,"lengthSize":4,"codePageOffset":-8,"codePageSize":2},
            "dynArray":{"lengthOffset":-8,"lengthSize":8},
            "vmt":{"classNameOffset":16,"classNameKind":"pointerToUnicodeString","parentOffset":0,"parentKind":"pointerToVmt"},
            "set":{"bitOrder":"lsb0"}}
            """;
        var abi = Demo()["abi"]!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Expected), abi), abi.ToJsonString());

        // the RTL: the raise hook, the exception code and the message field
        var system = File.ReadAllText(Path.Combine(TestRtl.Directory, "System.pas"));
        var code = Regex.Match(system, @"_PascalExceptionCode\s*=\s*\$([0-9A-Fa-f]+)\s*;");
        Assert.True(code.Success);
        Assert.Equal(Convert.ToUInt32(code.Groups[1].Value, 16), abi["pascalExceptionCode"]!.GetValue<uint>());
        Assert.Matches(@"(?m)^procedure _Raise\(Obj: Pointer; Addr: Pointer\)", system);
        var sysUtils = File.ReadAllText(Path.Combine(TestRtl.Directory, "SysUtils.pas"));
        var exception = Regex.Match(sysUtils, @"(?m)^\s*Exception\s*=\s*class\(TObject\)\s*(?:\r?\n\s*(?:private|protected|public|strict private)?\s*)*(\w+)\s*:\s*string\s*;");
        Assert.True(exception.Success, "Exception class with a first string field not found in SysUtils.pas");
        Assert.Equal(abi["exceptionMessageField"]!.GetValue<string>(), exception.Groups[1].Value);

        // the code generator: Length(S) reads the Int32 at payload - 12, Length(A) the NativeInt at payload - 8
        var ir = IrSnapshotTests.Emit("""
            unit M;
            interface
            type
              TInts = array of Integer;
            function Lengths(const S: string; const A: TInts): Int64;
            implementation
            function Lengths(const S: string; const A: TInts): Int64;
            begin
              Result := Length(S) + Length(A);
            end;
            end.
            """);
        Assert.Matches(@"getelementptr (?:inbounds )?i8, ptr %[\w.$]+, i64 -12\n\s+%[\w.$]+ = load i32", ir);
        Assert.Matches(@"getelementptr (?:inbounds )?i8, ptr %[\w.$]+, i64 -8\n\s+%[\w.$]+ = load i64", ir);
    }

    // ------------------------------------------------------------------ DebugInfoLayoutMatches

    /// <summary>
    /// DBG-144: every size and field offset in the sidecar equals the code generator's layout (<see cref="TypeLayout"/>, the field
    /// offsets the code addresses) and the <c>size:</c>/<c>offset:</c> bits of the debug metadata emitted next to it.
    /// </summary>
    [Fact]
    [Trait("Req", "DBG-144")]
    public void DebugInfoLayoutMatches()
    {
        foreach (var (source, path) in new[] { (LayoutSource, @"C:\p\L.pas"), (DemoSource, @"C:\p\DbgDemo.pas"), (DebugInfoTests.VariablesSource, @"C:\p\M.pas") })
        {
            var (ir, fragment, result) = EmitWithSidecar(source, path);
            var pdi = JsonNode.Parse(fragment)!.AsObject();
            var symbols = result.Unit.InterfaceMembers.Concat(result.Unit.ImplementationMembers).OfType<TypeSymbol>()
                .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
            var metadata = Metadata(ir);
            var checkedTypes = 0;
            foreach (var type in pdi["types"]!.AsArray().Select(t => t!.AsObject()))
            {
                var name = type["name"]!.GetValue<string>();
                if (type["unit"] is null || !symbols.TryGetValue(name, out var symbol))
                {
                    continue;
                }

                var kind = type["kind"]!.GetValue<string>();
                if (kind == "class")
                {
                    var @class = (ClassTypeSymbol)symbol;
                    Assert.Equal(@class.InstanceSize, type["instanceSize"]!.GetValue<long>());
                    // a class no variable of the unit uses has no debug metadata (it is in the sidecar for DBG-168)
                    if (metadata.SingleOrDefault(m => m.Name == name && m.Tag == "DW_TAG_structure_type") is { } structure)
                    {
                        Assert.Equal(@class.InstanceSize * 8, structure.SizeBits);
                        AssertFields(type, @class.Fields, structure, metadata);
                    }
                }
                else
                {
                    Assert.Equal(TypeLayout.Of(symbol).Size, type["size"]!.GetValue<long>());
                    if (kind == "record")
                    {
                        var record = (RecordTypeSymbol)symbol;
                        var structure = metadata.Single(m => m.Name == name && m.Tag == "DW_TAG_structure_type");
                        Assert.Equal(type["size"]!.GetValue<long>() * 8, structure.SizeBits);
                        AssertFields(type, record.Fields, structure, metadata);
                    }
                }

                checkedTypes++;
            }

            Assert.True(checkedTypes >= 3, $"{path}: only {checkedTypes} declared types checked");
        }

        static void AssertFields(JsonObject type, System.Collections.Immutable.ImmutableArray<FreexComp.Compiler.Symbols.FieldSymbol> fields, Node structure, List<Node> metadata)
        {
            var sidecar = type["fields"]!.AsArray().Select(f => (Name: f!["name"]!.GetValue<string>(), Offset: f["offset"]!.GetValue<long>())).ToList();
            Assert.Equal(fields.Select(f => (f.Name, f.Offset)), sidecar);
            foreach (var (name, offset) in sidecar)
            {
                var member = metadata.Single(m => m.Tag == "DW_TAG_member" && m.Name == name && m.Scope == structure.Id);
                Assert.Equal(offset * 8, member.OffsetBits);
            }
        }
    }

    /// <summary>One debug metadata node of interest: its number, tag, name, scope, size and offset in bits.</summary>
    private sealed record Node(int Id, string Tag, string? Name, int? Scope, long SizeBits, long OffsetBits);

    private static List<Node> Metadata(string ir) =>
    [
        .. MetadataRegex().Matches(ir).Select(m => new Node(
            int.Parse(m.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture),
            Regex.Match(m.Value, @"tag: (\w+)").Groups[1].Value,
            Regex.Match(m.Value, "name: \"([^\"]*)\"") is { Success: true } n ? n.Groups[1].Value : null,
            Regex.Match(m.Value, @"scope: !(\d+)") is { Success: true } s ? int.Parse(s.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null,
            Regex.Match(m.Value, @"size: (\d+)") is { Success: true } z ? long.Parse(z.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0,
            Regex.Match(m.Value, @"offset: (\d+)") is { Success: true } o ? long.Parse(o.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0)),
    ];

    [GeneratedRegex(@"(?m)^!(?<id>\d+) = (?:distinct )?!(?:DICompositeType|DIDerivedType)\(.*\)$")]
    private static partial Regex MetadataRegex();
}
