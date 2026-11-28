using System.Text.Json.Nodes;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>C-376 (generics.md CMP-1399): the <c>classConstructor</c> / <c>classDestructor</c> keys of class and record entries.</summary>
public sealed partial class PcuTests
{
    /// <summary>A class with both (next to an ordinary constructor of the same name), its nested class with a class destructor only, a record with a class constructor only and a class with neither.</summary>
    internal const string ClassConstructorUnit = """
        unit U;
        interface
        type
          TA = class
          public
            class var Count: Integer;
            class constructor Create;
            class destructor Destroy;
            constructor Create;
            type
              TN = class
                class destructor Done;
              end;
          end;
          TR = record
            X: Integer;
            class constructor Init;
          end;
          TPlain = class
          end;
        implementation
        class constructor TA.Create;
        begin
          Count := 1;
        end;
        class destructor TA.Destroy;
        begin
          Count := 0;
        end;
        constructor TA.Create;
        begin
          inherited Create;
          Count := Count + 1;
        end;
        class destructor TA.TN.Done;
        begin
        end;
        class constructor TR.Init;
        begin
        end;
        end.
        """;

    private const string ClassConstructorProgram = """
        program P;
        uses U;
        var
          A: TA;
          R: TR;
        begin
          A := TA.Create;
          R.X := TA.Count;
          Writeln(R.X, TA.TN.ClassName, TPlain.ClassName);
        end.
        """;

    /// <summary>
    /// CMP-1399: the keys name the declared class constructor and class destructor of class, record and nested entries and are written
    /// only when the type has one; the reader keeps them on the imported type for display, never for lookup (LANG-787): a program
    /// against the <c>.pcu</c> calls the ordinary <c>TA.Create</c>, calls no class constructor (the declaring unit's <c>$init</c> does)
    /// and gives the same IR as against the source.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1399")]
    [Trait("Req", "LANG-787")]
    public void ClassConstructorKeys_WrittenAndReadBack()
    {
        var pcu = Pcu("U", Files(u: ClassConstructorUnit, program: ClassConstructorProgram));
        var types = (JsonArray)pcu["interface"]!["types"]!;
        JsonObject Entry(string name) => (JsonObject)types.Single(t => (string?)t!["name"] == name)!;
        (string?, string?) Keys(string name) => ((string?)Entry(name)["classConstructor"], (string?)Entry(name)["classDestructor"]);
        Assert.Equal(("Create", "Destroy"), Keys("TA"));
        Assert.Equal((null, "Done"), Keys("TN"));
        Assert.Equal(("Init", null), Keys("TR"));
        Assert.Equal((null, null), Keys("TPlain"));
        Assert.False(Entry("TN").ContainsKey("classConstructor"));
        Assert.False(Entry("TPlain").ContainsKey("classConstructor") || Entry("TPlain").ContainsKey("classDestructor"));

        var pcus = Pcus(Files(u: ClassConstructorUnit, program: ClassConstructorProgram));
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", ClassConstructorProgram).AddSystem()
            .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), pcus["U"]).Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
        var compilation = Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs);
        var unit = compilation.GetUnitSymbol("U");
        Assert.NotNull(unit);
        var ta = Assert.IsType<ClassTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TA"));
        Assert.Equal(("Create", MethodKind.ClassConstructor), (ta.ClassConstructor?.Name, ta.ClassConstructor?.MethodKind));
        Assert.Equal(("Destroy", MethodKind.ClassDestructor), (ta.ClassDestructor?.Name, ta.ClassDestructor?.MethodKind));
        Assert.DoesNotContain(ta.Methods, m => m.MethodKind is MethodKind.ClassConstructor or MethodKind.ClassDestructor);
        var tn = Assert.IsType<ClassTypeSymbol>(TypeMembers.NestedMembers(ta).Single(m => m.Name == "TN"));
        Assert.Null(tn.ClassConstructor);
        Assert.Equal("Done", tn.ClassDestructor?.Name);
        var tr = Assert.IsType<RecordTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TR"));
        Assert.Equal(("Init", null), (tr.ClassConstructor?.Name, tr.ClassDestructor?.Name));
        var plain = Assert.IsType<ClassTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TPlain"));
        Assert.Equal((null, null), (plain.ClassConstructor, plain.ClassDestructor));

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var ir = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.DoesNotContain("$cctor", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("$cdtor", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("$ccdone", ir, StringComparison.Ordinal);
        PcuImportTests.AssertSameIrAgainstPcu(ClassConstructorUnit, ClassConstructorProgram);
    }

    /// <summary>
    /// CMP-1399: a <c>.pcu</c> written before this card — the same file without the two keys — is still read (an entry without them means
    /// none); a program compiles against it to the same IR as against the source.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1399")]
    public void OldPcuWithoutClassConstructorKeys_StillReads()
    {
        var sources = Files(u: ClassConstructorUnit, program: ClassConstructorProgram);
        var reference = Compilation.Create(Options(), [], sources).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(reference.Success, string.Join("\n", reference.Diagnostics.Select(d => d.ToText())));
        var old = Pcu("U", Files(u: ClassConstructorUnit, program: ClassConstructorProgram));
        var stripped = 0;
        foreach (var type in ((JsonArray)old["interface"]!["types"]!).OfType<JsonObject>())
        {
            stripped += type.Remove("classConstructor") ? 1 : 0;
            stripped += type.Remove("classDestructor") ? 1 : 0;
        }

        Assert.Equal(4, stripped);                                                     // TA (both), TA.TN, TR
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", ClassConstructorProgram).AddSystem()
            .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), old.ToJsonString() + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
        var compilation = Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs);

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System"], result.CompiledUnits);
        Assert.Equal(sources.ReadAllText(@"C:\p\obj\P.ll"), fs.ReadAllText(@"C:\p\obj\P.ll"));
        var ta = Assert.IsType<ClassTypeSymbol>(compilation.GetUnitSymbol("U")!.InterfaceMembers.Single(m => m.Name == "TA"));
        Assert.Equal((null, null), (ta.ClassConstructor, ta.ClassDestructor));
    }

    /// <summary>
    /// CMP-1399, CMP-152: for interface-part types the keys are part of the type entry and so of the interface hash — adding a class
    /// constructor or a class destructor, or renaming one, changes it.
    /// </summary>
    [Theory]
    [InlineData("class constructor added")]
    [InlineData("class destructor added")]
    [InlineData("class destructor renamed")]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1399")]
    [Trait("Req", "CMP-152")]
    public void ClassConstructorKey_ChangesTheInterfaceHash(string edit)
    {
        static string Unit(string tb, string td, string implementation) => $$"""
            unit U;
            interface
            type
              TB = class
                X: Integer;
                {{tb}}
              end;
              TD = record
                Y: Integer;
                class destructor {{td}};
              end;
            implementation
            class destructor TD.{{td}};
            begin
            end;
            {{implementation}}
            end.
            """;
        var original = Unit("", "Destroy", "");
        var edited = edit switch
        {
            "class constructor added" => Unit("class constructor Init;", "Destroy", "class constructor TB.Init; begin end;"),
            "class destructor added" => Unit("class destructor Done;", "Destroy", "class destructor TB.Done; begin end;"),
            _ => Unit("", "Finish", ""),
        };
        const string Program = "program P;\nuses U;\nbegin\nend.\n";
        Assert.Empty(Compilation.Create(Options(), [], Files(u: edited, program: Program)).GetDiagnostics(ct: TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToText()));

        var before = Hash("U", Files(u: original, program: Program));
        var after = Hash("U", Files(u: edited, program: Program));

        Assert.True(before != after, edit);
    }

    /// <summary>A class helper with a class constructor and class destructor, a record helper with a class constructor only, and a helper with neither.</summary>
    private const string HelperClassConstructorUnit = """
        unit U;
        interface
        type
          TThing = class
            X: Integer;
          end;
          TThingH = class helper for TThing
            class var Made: Integer;
            class constructor Create;
            class destructor Destroy;
            function Twice: Integer;
          end;
          TIntH = record helper for Integer
            class var Calls: Integer;
            class constructor Init;
            function Half: Integer;
          end;
          TPlainH = record helper for Boolean
            function Text: string;
          end;
        implementation
        class constructor TThingH.Create; begin Made := 1 end;
        class destructor TThingH.Destroy; begin Made := 0 end;
        function TThingH.Twice: Integer; begin Result := X * 2 end;
        class constructor TIntH.Init; begin Calls := 10 end;
        function TIntH.Half: Integer; begin Result := Self div 2 end;
        function TPlainH.Text: string; begin if Self then Result := 'yes' else Result := 'no' end;
        end.
        """;

    private const string HelperClassConstructorProgram = """
        program P;
        uses U;
        var
          T: TThing;
          N: Integer;
        begin
          T := TThing.Create;
          N := T.Twice + Integer.Calls + 9.Half + TThingH.Made;
          Writeln(N, True.Text);
        end.
        """;

    /// <summary>
    /// CMP-1490 (b) (C-441): a helper entry carries <c>classConstructor</c> / <c>classDestructor</c> with the declared names, exactly as a
    /// class or record entry (CMP-1399); the reader keeps them on the imported helper for display, never for lookup, and an importer
    /// never calls them (the declaring unit's <c>$init</c> / <c>$final</c> do): the same IR against the <c>.pcu</c> as against the source.
    /// The keys are part of the interface hash.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1490")]
    [Trait("Req", "LANG-995")]
    public void HelperClassConstructorFromPcu()
    {
        var pcu = Pcu("U", Files(u: HelperClassConstructorUnit, program: HelperClassConstructorProgram));
        var types = (JsonArray)pcu["interface"]!["types"]!;
        JsonObject Entry(string name) => (JsonObject)types.Single(t => (string?)t!["name"] == name)!;
        (string?, string?) Keys(string name) => ((string?)Entry(name)["classConstructor"], (string?)Entry(name)["classDestructor"]);
        Assert.Equal(("Create", "Destroy"), Keys("TThingH"));
        Assert.Equal(("Init", null), Keys("TIntH"));
        Assert.False(Entry("TIntH").ContainsKey("classDestructor"));
        Assert.False(Entry("TPlainH").ContainsKey("classConstructor") || Entry("TPlainH").ContainsKey("classDestructor"));
        Assert.True((bool)pcu["hasInit"]! && (bool)pcu["hasFinal"]!);

        var pcus = Pcus(Files(u: HelperClassConstructorUnit, program: HelperClassConstructorProgram));
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", HelperClassConstructorProgram).AddSystem()
            .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), pcus["U"]).Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
        var compilation = Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs);
        var unit = compilation.GetUnitSymbol("U");
        Assert.NotNull(unit);
        var thing = Assert.IsType<HelperTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TThingH"));
        Assert.Equal(("Create", MethodKind.ClassConstructor), (thing.ClassConstructor?.Name, thing.ClassConstructor?.MethodKind));
        Assert.Equal(("Destroy", MethodKind.ClassDestructor), (thing.ClassDestructor?.Name, thing.ClassDestructor?.MethodKind));
        Assert.DoesNotContain(thing.Members, m => m is MethodSymbol { MethodKind: MethodKind.ClassConstructor or MethodKind.ClassDestructor });
        var integer = Assert.IsType<HelperTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TIntH"));
        Assert.Equal(("Init", null), (integer.ClassConstructor?.Name, integer.ClassDestructor?.Name));
        var plain = Assert.IsType<HelperTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TPlainH"));
        Assert.Equal((null, null), (plain.ClassConstructor, plain.ClassDestructor));

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var ir = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.DoesNotContain("$cctor", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("$cdtor", ir, StringComparison.Ordinal);
        Assert.Contains("{ ptr @\"U.$init\", ptr @\"U.$final\" }", ir);
        PcuImportTests.AssertSameIrAgainstPcu(HelperClassConstructorUnit, HelperClassConstructorProgram);

        // the keys are part of the interface hash
        const string Empty = "program P;\nuses U;\nbegin\nend.\n";
        var hash = Hash("U", Files(u: HelperClassConstructorUnit, program: Empty));
        var renamed = HelperClassConstructorUnit.Replace("class destructor Destroy;", "class destructor Finish;", StringComparison.Ordinal)
            .Replace("class destructor TThingH.Destroy;", "class destructor TThingH.Finish;", StringComparison.Ordinal);
        var added = HelperClassConstructorUnit.Replace("function Text: string;", "function Text: string;\n    class destructor Done;", StringComparison.Ordinal)
            .Replace("end.", "class destructor TPlainH.Done; begin end;\nend.", StringComparison.Ordinal);
        var body = HelperClassConstructorUnit.Replace("Calls := 10", "Calls := 11", StringComparison.Ordinal);
        Assert.NotEqual(hash, Hash("U", Files(u: renamed, program: Empty)));
        Assert.NotEqual(hash, Hash("U", Files(u: added, program: Empty)));
        Assert.Equal(hash, Hash("U", Files(u: body, program: Empty)));
    }
}
