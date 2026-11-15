using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-074: a unit available only as <c>.pcu</c> + <c>.obj</c> (no source) is imported by <see cref="Units.PcuReader"/>: a program
/// binds, compiles, links and runs against it (LANG-510 directories also hold precompiled units); unusable files are E2202.
/// The round trip write → read → write is in <see cref="PcuTests"/> (<c>RoundTrip*</c>).
/// </summary>
[Trait("Phase", "2")]
public sealed class PcuImportTests
{
    internal const string Lib = @"C:\lib";

    internal const string ImportingProgram = """
        program P;
        uses U, V;
        var
          C: TColor;
          S: TSmall;
          Q: PPoint;
          R: TRow;
        begin
          Writeln(Add(Answer, 1));
          Writeln(Greeting, ' ', Limit, ' ', Yes);
          Origin.X := 5;
          Q := @Origin;
          Writeln(Q^.X);
          C := Blue;
          Writeln(Ord(C));
          S := 3;
          Show('x', S);
          Show;
          Shared := 9;
          Writeln(Shared);
          R[Green] := 7;
          Writeln(R[Green]);
          Writeln(Ord(First) = Ord(One));
          Writeln(Ticks > 0);
        end.

        """;

    internal static readonly string[] ExpectedOutput = ["43", "hi 7 TRUE", "5", "2", "9", "7", "TRUE", "TRUE"];

    /// <summary>The program's sources with U and V replaced by their <c>.pcu</c> (from a source compilation) and an empty <c>.obj</c> in <see cref="Lib"/>.</summary>
    internal static (InMemoryFileSystem Fs, CompilationOptions Options, Dictionary<string, string> Pcus) PrecompiledInMemory(string program = ImportingProgram)
    {
        var pcus = PcuTests.Pcus(PcuTests.Files(program: program));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", program)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        foreach (var unit in new[] { "U", "V" })
        {
            fs.Add(Path.Combine(Lib, unit + ".pcu"), pcus[unit]).Add(Path.Combine(Lib, unit + ".obj"), "");
        }

        return (fs, PcuTests.Options() with { SearchPaths = [Lib] }, pcus);
    }

    private static EmitResult Emit(InMemoryFileSystem fs, CompilationOptions options, EmitStage stage) =>
        Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = stage }, TestContext.Current.CancellationToken);

    [Fact]
    [Trait("Req", "LANG-510")]
    public void ProgramAgainstPcuOnlyUnits_BindsAndEmits_OnlyTheSourceUnits()
    {
        var (fs, options, _) = PrecompiledInMemory();

        var result = Emit(fs, options, EmitStage.Ll);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System"], result.CompiledUnits);
        var ir = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.Contains("call i32 @\"U.Add\"(", ir, StringComparison.Ordinal);
        Assert.Contains("@u_shared", ir, StringComparison.Ordinal);
        Assert.Contains("declare dllimport i32 @GetTickCount()", ir, StringComparison.Ordinal);
        Assert.Contains("call void @\"U.Show$c:string,v:U.TSmall\"(", ir, StringComparison.Ordinal);
        Assert.False(fs.FileExists(@"C:\p\obj\U.ll"));
    }

    /// <summary>
    /// C-189: a program using the classes of a <c>.pcu</c>-only unit — construction through the class and a class reference,
    /// virtual and static calls, class methods, class vars, properties of every accessor kind, a descendant class with its own
    /// VMT — binds and gives the same IR as against the unit's source.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-510")]
    [Trait("Req", "CMP-160")]
    public void ClassFromPcu()
    {
        const string Program = """
            program P;
            uses U;
            type
              TMine = class(TNode)
                procedure Run; override;
                class function Kind: string; override;
              end;
            procedure TMine.Run; begin Writeln(Count, First, Items[1], Flag, Total) end;
            class function TMine.Kind: string; begin Result := 'mine'; end;
            var
              B: TBase;
              C: TBaseClass;
            begin
              B := TNode.Create('n');
              B.Run;
              B.Count := 2;
              Writeln(B[0], B.Twice(3), B.Twice('x'), B.Name, TBase.Kind, B.Kind, TBase.Instances);
              TBase.Reset;
              B.Free;
              C := TMine;
              Kinds := C;
              B := Make(C);
              B.Run;
              Writeln(B is TNode, C.Kind, B.ClassName);
              (B as TMine).Free;
            end.
            """;
        var sources = PcuTests.Files(u: PcuTests.ClassUnit, program: Program);
        var reference = Emit(sources, PcuTests.Options(), EmitStage.Ll);
        Assert.True(reference.Success, string.Join("\n", reference.Diagnostics.Select(d => d.ToText())));
        var pcus = PcuTests.Pcus(PcuTests.Files(u: PcuTests.ClassUnit, program: Program));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Program)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        foreach (var unit in new[] { "U", "V" })
        {
            fs.Add(Path.Combine(Lib, unit + ".pcu"), pcus[unit]).Add(Path.Combine(Lib, unit + ".obj"), "");
        }

        var result = Emit(fs, PcuTests.Options() with { SearchPaths = [Lib] }, EmitStage.Ll);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System"], result.CompiledUnits);
        Assert.Equal(sources.ReadAllText(@"C:\p\obj\P.ll"), fs.ReadAllText(@"C:\p\obj\P.ll"));
    }

    /// <summary>
    /// C-112 / ABI-142: the <c>typeinfo</c> class flag survives the <c>.pcu</c> round trip, so a program's descendant of an imported
    /// <c>{$M+}</c> class publishes its members before the first visibility keyword (class RTTI field entry with Visibility 3); the IR
    /// equals the one against the unit's source.
    /// </summary>
    [Fact]
    [Trait("Req", "ABI-142")]
    [Trait("Req", "LANG-611")]
    public void TypeInfoFlagFromPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              {$M+}
              TBase = class
              end;
              {$M-}
            implementation
            end.
            """;
        const string Program = """
            program P;
            uses U;
            type
              TChild = class(TBase)
                FItem: TBase;
              end;
            begin
            end.
            """;
        var sources = PcuTests.Files(u: Unit, program: Program);
        var reference = Emit(sources, PcuTests.Options(), EmitStage.Ll);
        Assert.True(reference.Success, string.Join("\n", reference.Diagnostics.Select(d => d.ToText())));
        var pcus = PcuTests.Pcus(PcuTests.Files(u: Unit, program: Program));
        Assert.Contains("\"typeinfo\"", pcus["U"], StringComparison.Ordinal);
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Program)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        fs.Add(Path.Combine(Lib, "U.pcu"), pcus["U"]).Add(Path.Combine(Lib, "U.obj"), "");

        var result = Emit(fs, PcuTests.Options() with { SearchPaths = [Lib] }, EmitStage.Ll);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var ir = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.Equal(sources.ReadAllText(@"C:\p\obj\P.ll"), ir);
        Assert.Matches(@"@""P\.TChild\.\$rtti\.fields"" = .*i64 8, ptr @""U\.TBase\.\$rtti"", i8 3, ", ir);
        Assert.Contains("@\"U.TBase.$rtti\" = external global ptr", ir, StringComparison.Ordinal);
    }

    /// <summary>
    /// C-113: a descendant of a <c>.pcu</c>-only class with a <c>message</c> handler — bare <c>inherited</c> in the program's handler
    /// for the same id calls the imported handler statically (the <c>msgId</c> survives the round trip), and for an id without an
    /// imported handler calls virtual <c>DefaultHandler</c>; the IR equals the one against the unit's source.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-622")]
    [Trait("Req", "CMP-370")]
    public void MessageHandlerFromPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              TMsg = record Id: Cardinal; end;
              TBase = class
                procedure OnOne(var M: TMsg); message 1;
              end;
            implementation
            procedure TBase.OnOne(var M: TMsg); begin Writeln('base') end;
            end.
            """;
        const string Program = """
            program P;
            uses U;
            type
              TMine = class(TBase)
                procedure One(var M: TMsg); message 1;
                procedure Two(var M: TMsg); message 2;
              end;
            procedure TMine.One(var M: TMsg); begin inherited end;
            procedure TMine.Two(var M: TMsg); begin inherited end;
            begin
            end.
            """;
        var sources = PcuTests.Files(u: Unit, program: Program);
        var reference = Emit(sources, PcuTests.Options(), EmitStage.Ll);
        Assert.True(reference.Success, string.Join("\n", reference.Diagnostics.Select(d => d.ToText())));
        var pcus = PcuTests.Pcus(PcuTests.Files(u: Unit, program: Program));
        Assert.Contains("\"msgId\":1", pcus["U"], StringComparison.Ordinal);
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Program)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        fs.Add(Path.Combine(Lib, "U.pcu"), pcus["U"]).Add(Path.Combine(Lib, "U.obj"), "");

        var result = Emit(fs, PcuTests.Options() with { SearchPaths = [Lib] }, EmitStage.Ll);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var ir = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.Equal(sources.ReadAllText(@"C:\p\obj\P.ll"), ir);
        Assert.Contains("call void @\"U.TBase.OnOne\"(", ir, StringComparison.Ordinal);
        Assert.Contains("@\"P.TMine.$msgtable\" = private unnamed_addr constant { i64, [2 x { i64, ptr }] } { i64 2, [2 x { i64, ptr }] [{ i64, ptr } { i64 1, ptr @\"P.TMine.One\" }, { i64, ptr } { i64 2, ptr @\"P.TMine.Two\" }] }", ir, StringComparison.Ordinal);
    }

    /// <summary>
    /// C-205: a program using the interfaces of a <c>.pcu</c>-only unit — calls through the IMT, an interface property and default
    /// array property, overloads, class → interface conversion of an imported class, <c>is</c>/<c>as</c> with an imported GUID, a
    /// descendant class listing an imported interface — binds and gives the same IR as against the unit's source.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-510")]
    [Trait("Req", "CMP-160")]
    [Trait("Req", "LANG-664")]
    public void InterfaceFromPcu()
    {
        const string Program = """
            program P;
            uses U;
            type
              TBigBox = class(TBox, IShape)
                function GetSide(I: Integer): Integer; override;
                procedure Notify(Sender: TObject);
              end;
            function TBigBox.GetSide(I: Integer): Integer; begin Result := 100 + I end;
            procedure TBigBox.Notify(Sender: TObject); begin Writeln('grown') end;
            var
              B: IBox;
              S: IShape;
              O: TBox;
            begin
              B := MakeBox(3);
              B.Grow(1);
              B.Grow('ab');
              Writeln(B.Area, B.Name, B[2], B.Sides[1]);
              S := B;
              O := TBigBox.Create;
              B := O;
              S := O as IShape;
              Writeln(O is IBox, S.Area, B[1]);
              O.OnGrow := TBigBox(O).Notify;
              if Assigned(O.OnGrow) then O.OnGrow(nil);
            end.
            """;
        var sources = PcuTests.Files(u: PcuTests.InterfaceUnit, program: Program);
        var reference = Emit(sources, PcuTests.Options(), EmitStage.Ll);
        Assert.True(reference.Success, string.Join("\n", reference.Diagnostics.Select(d => d.ToText())));
        var pcus = PcuTests.Pcus(PcuTests.Files(u: PcuTests.InterfaceUnit, program: Program));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", Program)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        foreach (var unit in new[] { "U", "V" })
        {
            fs.Add(Path.Combine(Lib, unit + ".pcu"), pcus[unit]).Add(Path.Combine(Lib, unit + ".obj"), "");
        }

        var result = Emit(fs, PcuTests.Options() with { SearchPaths = [Lib] }, EmitStage.Ll);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System"], result.CompiledUnits);
        Assert.Equal(sources.ReadAllText(@"C:\p\obj\P.ll"), fs.ReadAllText(@"C:\p\obj\P.ll"));
    }

    /// <summary>
    /// C-219: a program using a <c>.pcu</c>-only unit's record with methods — instance and static methods, a constructor,
    /// operators (incl. two <c>Implicit</c>s differing only in their result), properties reading a field and writing through a
    /// method, a class var — binds and gives the same IR as against the unit's source.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-730")]
    [Trait("Req", "LANG-731")]
    public void RecordMethodsFromPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              TMoney = record
              private
                FCents: Integer;
                procedure SetCents(V: Integer);
              public
                class var Created: Integer;
                constructor Create(ACents: Integer);
                function Euros: Integer;
                procedure Add(N: Integer); overload;
                procedure Add(const M: TMoney); overload;
                class function Zero: TMoney; static;
                class operator Add(const A, B: TMoney): TMoney;
                class operator Equal(const A, B: TMoney): Boolean;
                class operator Implicit(N: Integer): TMoney;
                class operator Implicit(const M: TMoney): Integer;
                property Cents: Integer read FCents write SetCents;
              end;
            implementation
            constructor TMoney.Create(ACents: Integer); begin FCents := ACents; Inc(Created) end;
            procedure TMoney.SetCents(V: Integer); begin FCents := V end;
            function TMoney.Euros: Integer; begin Result := FCents div 100 end;
            procedure TMoney.Add(N: Integer); begin Inc(FCents, N) end;
            procedure TMoney.Add(const M: TMoney); begin Inc(FCents, M.FCents) end;
            class function TMoney.Zero: TMoney; begin Result.FCents := 0 end;
            class operator TMoney.Add(const A, B: TMoney): TMoney; begin Result.FCents := A.FCents + B.FCents end;
            class operator TMoney.Equal(const A, B: TMoney): Boolean; begin Result := A.FCents = B.FCents end;
            class operator TMoney.Implicit(N: Integer): TMoney; begin Result.FCents := N end;
            class operator TMoney.Implicit(const M: TMoney): Integer; begin Result := M.FCents end;
            end.
            """;
        const string Program = """
            program P;
            uses U;
            var
              A, B: TMoney;
              I: Integer;
            begin
              A := TMoney.Create(250);
              B := 120;
              A.Add(5);
              A.Add(B);
              B := A + TMoney.Zero;
              I := B;
              B.Cents := 7;
              Writeln(A.Euros, ' ', I, ' ', B.Cents, ' ', A = B, ' ', TMoney.Created);
            end.
            """;

        AssertSameIrAgainstPcu(Unit, Program);
    }

    /// <summary>
    /// C-219: a <c>.pcu</c>-only unit's record helper for a predeclared type and class helper for its class (methods, a property,
    /// a class var, a class method) are active in the program (LANG-753) and give the same IR as against the unit's source.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-750")]
    [Trait("Req", "LANG-753")]
    public void HelperFromPcu()
    {
        const string Unit = """
            unit U;
            interface
            type
              TIntHelper = record helper for Integer
                function Twice: Integer;
                procedure Bump;
              end;
              TThing = class
                FSize: Integer;
              end;
              TThingHelper = class helper for TThing
              private
                function GetDouble: Integer;
              public
                class var Grown: Integer;
                procedure Grow(N: Integer);
                class function Kind: string;
                property Double: Integer read GetDouble;
              end;
            implementation
            function TIntHelper.Twice: Integer; begin Result := Self * 2 end;
            procedure TIntHelper.Bump; begin Inc(Self) end;
            function TThingHelper.GetDouble: Integer; begin Result := FSize * 2 end;
            procedure TThingHelper.Grow(N: Integer); begin Inc(FSize, N); Inc(Grown) end;
            class function TThingHelper.Kind: string; begin Result := 'thing' end;
            end.
            """;
        const string Program = """
            program P;
            uses U;
            var
              I: Integer;
              T: TThing;
            begin
              I := 4;
              I.Bump;
              T := TThing.Create;
              T.Grow(I.Twice);
              Writeln(I, ' ', T.Double, ' ', TThing.Kind, ' ', TThing.Grown);
              T.Free;
            end.
            """;

        AssertSameIrAgainstPcu(Unit, Program);
    }

    /// <summary>The program compiled against <c>.pcu</c>-only U and V gives the IR it gets against their sources.</summary>
    private static void AssertSameIrAgainstPcu(string unit, string program)
    {
        var sources = PcuTests.Files(u: unit, program: program);
        var reference = Emit(sources, PcuTests.Options(), EmitStage.Ll);
        Assert.True(reference.Success, string.Join("\n", reference.Diagnostics.Select(d => d.ToText())));
        var pcus = PcuTests.Pcus(PcuTests.Files(u: unit, program: program));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", program)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        foreach (var name in new[] { "U", "V" }.Where(pcus.ContainsKey))
        {
            fs.Add(Path.Combine(Lib, name + ".pcu"), pcus[name]).Add(Path.Combine(Lib, name + ".obj"), "");
        }

        var result = Emit(fs, PcuTests.Options() with { SearchPaths = [Lib] }, EmitStage.Ll);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System"], result.CompiledUnits);
        Assert.Equal(sources.ReadAllText(@"C:\p\obj\P.ll"), fs.ReadAllText(@"C:\p\obj\P.ll"));
    }

    [Fact]
    [Trait("Req", "LANG-510")]
    public void ProgramAgainstPcuOnlyUnits_GivesTheSameProgramIrAsAgainstTheirSources()
    {
        var sources = PcuTests.Files(program: ImportingProgram);
        Assert.True(Emit(sources, PcuTests.Options(), EmitStage.Ll).Success);
        var (fs, options, _) = PrecompiledInMemory();

        Assert.True(Emit(fs, options, EmitStage.Ll).Success);

        Assert.Equal(sources.ReadAllText(@"C:\p\obj\P.ll"), fs.ReadAllText(@"C:\p\obj\P.ll"));
    }

    [Fact]
    [Trait("Req", "LANG-510")]
    public void PcuOnlyUnit_ImplementationSymbolsStayInvisible()
    {
        var (fs, options, _) = PrecompiledInMemory();
        fs.Add(@"C:\p\P.pas", "program P;\nuses U;\nbegin\n  Writeln(Hidden);\nend.\n");

        var result = Emit(fs, options, EmitStage.Check);

        Assert.Equal(["P.pas(4,11): E2003 Undeclared identifier 'Hidden'"],
            result.Diagnostics.Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"));
    }

    [Theory]
    [InlineData("format version", "\"formatVersion\":1", "\"formatVersion\":2")]
    [InlineData("compiler fingerprint", "\"compilerFingerprint\":\"opc ", "\"compilerFingerprint\":\"opc 9")]
    [InlineData("not JSON", "{\"formatVersion\"", "{{\"formatVersion\"")]
    [InlineData("dangling type id", "\"result\":0", "\"result\":999")]
    [InlineData("unknown symbol kind", "\"kind\":\"routine\"", "\"kind\":\"macro\"")]
    [Trait("Req", "CMP-160")]
    public void UnusablePcu_IsE2202AtTheFile(string what, string from, string to)
    {
        var (fs, options, pcus) = PrecompiledInMemory();
        Assert.Contains(from, pcus["U"], StringComparison.Ordinal);
        fs.Add(@"C:\lib\U.pcu", pcus["U"].Replace(from, to, StringComparison.Ordinal));

        var result = Emit(fs, options, EmitStage.Check);

        Assert.False(result.Success, what);
        Assert.Contains(@"C:\lib\U.pcu(0,0): error E2202: Unit 'U' not found", result.Diagnostics.Select(d => d.ToText()));
    }

    [Fact]
    [Trait("Req", "CMP-160")]
    public void Pcu_WithUnknownKeys_IsImported()
    {
        var (fs, options, pcus) = PrecompiledInMemory();
        var pcu = (JsonObject)JsonNode.Parse(pcus["U"])!;
        pcu["futureKey"] = new JsonArray(1, 2);
        ((JsonObject)pcu["interface"]!["symbols"]![0]!)["futureFlag"] = true;
        ((JsonObject)pcu["interface"]!["types"]![0]!)["futureNote"] = "x";
        fs.Add(@"C:\lib\U.pcu", pcu.ToJsonString());

        var result = Emit(fs, options, EmitStage.Check);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
    }

    [Fact]
    [Trait("Req", "LANG-510")]
    public void PcuWithoutItsObj_IsNotAUnit()
    {
        var (fs, options, _) = PrecompiledInMemory();
        fs.Delete(@"C:\lib\U.obj");

        var result = Emit(fs, options, EmitStage.Check);

        Assert.Contains(result.Diagnostics, d => d.Code == "E2202" && d.Message == "Unit 'U' not found" && d.Location.Path == @"C:\p\P.pas");
    }

    [Fact]
    [Trait("Req", "LANG-510")]
    public void SourceInADirectory_WinsOverAPcuInTheSameDirectory()
    {
        var (fs, options, _) = PrecompiledInMemory();
        fs.Add(@"C:\lib\V.pas", PcuTests.UnitV);

        var result = Emit(fs, options, EmitStage.Ll);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System", "V"], result.CompiledUnits);
    }

    /// <summary>End to end: compile from sources (writing the .pcu/.obj files), then build the program against only those files and run it.</summary>
    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "LANG-510")]
    [Trait("Req", "CMP-141")]
    public async Task ProgramAgainstPcuOnlyUnits_LinksAndRuns_LikeTheSourceBuild()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var ct = TestContext.Current.CancellationToken;
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        var fs = PhysicalFileSystem.Instance;
        try
        {
            void Put(string relative, string text) => fs.WriteAllBytes(Path.Combine(dir, relative), Encoding.UTF8.GetBytes(text));
            Put(@"src\P.pas", ImportingProgram);
            Put(@"src\U.pas", PcuTests.UnitU);
            Put(@"src\V.pas", PcuTests.UnitV);
            Put(@"app\P.pas", ImportingProgram);
            CompilationOptions Options(string folder) => new()
            {
                MainFile = Path.Combine(dir, folder, "P.pas"),
                RtlDirectory = MiniRtlCompilesTests.MiniRtlDirectory,
                ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
                ObjectDirectory = Path.Combine(dir, folder, "obj"),
            };

            var fromSources = Compilation.Create(Options("src"), [], fs).Emit(new EmitOptions { OutputPath = Path.Combine(dir, "src", "P.exe") }, ct);
            Assert.True(fromSources.Success, string.Join("\n", fromSources.Diagnostics.Select(d => d.ToText())));
            foreach (var file in new[] { "U.pcu", "U.obj", "V.pcu", "V.obj" })
            {
                File.Copy(Path.Combine(dir, "src", "obj", file), Path.Combine(dir, "app", file));
            }

            var fromPcus = Compilation.Create(Options("app"), [], fs).Emit(new EmitOptions { OutputPath = Path.Combine(dir, "app", "P.exe") }, ct);

            Assert.True(fromPcus.Success, string.Join("\n", fromPcus.Diagnostics.Select(d => d.ToText())));
            Assert.Equal(["P", "System"], fromPcus.CompiledUnits);
            Assert.Contains(Path.Combine(dir, "app", "U.obj"), File.ReadAllLines(Path.Combine(dir, "app", "obj", "link.rsp")));
            foreach (var exe in new[] { @"src\P.exe", @"app\P.exe" })
            {
                var stdout = new List<string>();
                Assert.Equal(0, await new ProcessRunner().RunAsync(Path.Combine(dir, exe), [], dir, stdout.Add, _ => { }, ct));
                Assert.Equal(ExpectedOutput, stdout);
            }
        }
        finally
        {
            try
            {
                TestDirectories.Delete(dir);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
