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
                Directory.Delete(dir, recursive: true);
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
