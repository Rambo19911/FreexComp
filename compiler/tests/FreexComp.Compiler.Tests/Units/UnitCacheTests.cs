using System.Text.Json.Nodes;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-075: each rule of CMP-150 in isolation (<see cref="UnitCache"/> over an <see cref="InMemoryFileSystem"/>), then the
/// compilation using it: a unit whose <c>.pcu</c>/<c>.obj</c> in the object directory are up to date is not recompiled
/// (in memory: when every unit is up to date no tool runs at all); precompiled units are checked for rules 3 and 5 too.
/// The runs with the real toolchain (body / interface edits, link skipping) are <c>EmitTests.Incremental*</c>.
/// </summary>
[Trait("Phase", "2")]
[Trait("Req", "CMP-150")]
public sealed class UnitCacheTests
{
    private const string Obj = @"C:\p\obj";

    /// <summary>The sources of <see cref="PcuTests.Files"/> with every unit's current <c>.pcu</c> and an (empty) <c>.obj</c> in <see cref="Obj"/>.</summary>
    private static (InMemoryFileSystem Fs, Dictionary<string, JsonObject> Pcus, UnitCache Cache) Cached(CompilationOptions? options = null)
    {
        var fs = PcuTests.Files();
        options ??= PcuTests.Options();
        var pcus = PcuTests.Pcus(fs, options);
        foreach (var (unit, text) in pcus)
        {
            fs.Add(Path.Combine(Obj, unit + ".pcu"), text).Add(Path.Combine(Obj, unit + ".obj"), "");
        }

        var cache = new UnitCache(fs, PcuWriter.CompilerFingerprint, Compilation.Create(options, [], fs).OptionsFingerprint());
        return (fs, pcus.ToDictionary(p => p.Key, p => (JsonObject)JsonNode.Parse(p.Value)!), cache);
    }

    /// <summary>The dependencies as they are now: each unit's own file and recorded interface hash.</summary>
    private static Func<string, UnitCacheDependency?> Current(Dictionary<string, JsonObject> pcus, string? changed = null, string? movedTo = null) =>
        name => pcus.TryGetValue(name, out var pcu)
            ? new UnitCacheDependency(
                name == changed && movedTo is not null ? movedTo : (string)pcu["sources"]![0]!["path"]!,
                name == changed && movedTo is null ? "sha256:other" : (string)pcu["interfaceHash"]!)
            : null;

    private static string? CheckU(InMemoryFileSystem fs, UnitCache cache, Dictionary<string, JsonObject> pcus, Func<string, UnitCacheDependency?>? current = null) =>
        cache.Check(Path.Combine(Obj, "U.pcu"), Path.Combine(Obj, "U.obj"), current ?? Current(pcus), out _);

    [Fact]
    public void AllRulesHold_IsUpToDate()
    {
        var (fs, pcus, cache) = Cached();

        Assert.Null(cache.Check(Path.Combine(Obj, "U.pcu"), Path.Combine(Obj, "U.obj"), Current(pcus), out var pcu));

        Assert.Equal("U", (string)pcu!["unit"]!);
        Assert.All(new[] { "P", "System", "V" }, unit => Assert.Null(cache.Check(Path.Combine(Obj, unit + ".pcu"), Path.Combine(Obj, unit + ".obj"), Current(pcus), out _)));
    }

    [Fact]
    public void Rule4_UnitNowResolvesToAnotherFile_IsNotUpToDate()
    {
        var (fs, pcus, cache) = Cached();
        var recorded = (string)pcus["U"]["sources"]![0]!["path"]!;

        var same = cache.Check(Path.Combine(Obj, "U.pcu"), Path.Combine(Obj, "U.obj"), Current(pcus), out _, recorded);
        var moved = cache.Check(Path.Combine(Obj, "U.pcu"), Path.Combine(Obj, "U.obj"), Current(pcus), out _, @"C:\other\U.pas");

        Assert.Null(same);
        Assert.Equal(@"CMP-150 rule 4: the unit now resolves to C:\other\U.pas", moved);
    }

    [Theory]
    [InlineData("U.pcu")]
    [InlineData("U.obj")]
    public void Rule1_MissingPcuOrObj(string file)
    {
        var (fs, pcus, cache) = Cached();
        fs.Delete(Path.Combine(Obj, file));

        Assert.Equal("CMP-150 rule 1: no .pcu or .obj", CheckU(fs, cache, pcus));
    }

    [Theory]
    [InlineData("\"formatVersion\":1", "\"formatVersion\":2")]
    [InlineData("{\"formatVersion\"", "{{\"formatVersion\"")]
    public void Rule1_PcuOfAnotherFormatOrNotJson(string from, string to)
    {
        var (fs, pcus, cache) = Cached();
        fs.Add(Path.Combine(Obj, "U.pcu"), fs.ReadAllText(Path.Combine(Obj, "U.pcu")).Replace(from, to, StringComparison.Ordinal));

        Assert.Equal("CMP-150 rule 1: the .pcu does not parse with the current format", CheckU(fs, cache, pcus));
    }

    [Fact]
    public void Rule2_AnotherCompilerFingerprint()
    {
        var (fs, pcus, _) = Cached();
        var cache = new UnitCache(fs, PcuWriter.CompilerFingerprint + "x", (string)pcus["U"]["optionsFingerprint"]!);

        Assert.Equal("CMP-150 rule 2: another compiler fingerprint", CheckU(fs, cache, pcus));
    }

    /// <summary>
    /// C-195: the fingerprint names the build of the compiler assembly (its MVID, which a deterministic build changes with
    /// every change of the binary), so a <c>.pcu</c> written by another build of the same version is out of date.
    /// </summary>
    [Fact]
    public void CompilerChange_InvalidatesCache()
    {
        var build = typeof(PcuWriter).Assembly.ManifestModule.ModuleVersionId;
        Assert.Equal(PcuWriter.CompilerFingerprintFor(build), PcuWriter.CompilerFingerprint);
        Assert.EndsWith($"; build {build:N}", PcuWriter.CompilerFingerprint, StringComparison.Ordinal);

        var rebuilt = PcuWriter.CompilerFingerprintFor(new Guid("0123456789abcdef0123456789abcdef"));
        Assert.NotEqual(PcuWriter.CompilerFingerprint, rebuilt);
        var (fs, pcus, _) = Cached();
        var cache = new UnitCache(fs, rebuilt, (string)pcus["U"]["optionsFingerprint"]!);

        Assert.Equal("CMP-150 rule 2: another compiler fingerprint", CheckU(fs, cache, pcus));
    }

    [Fact]
    public void Rule3_AnotherOptionsFingerprint()
    {
        var (fs, pcus, _) = Cached();
        var cache = new UnitCache(fs, PcuWriter.CompilerFingerprint, Compilation.Create(PcuTests.Options("DEBUG"), [], fs).OptionsFingerprint());

        Assert.Equal("CMP-150 rule 3: another options fingerprint", CheckU(fs, cache, pcus));
    }

    [Fact]
    public void Rule4_SourceEdited()
    {
        var (fs, pcus, cache) = Cached();
        fs.Add(@"C:\p\U.pas", PcuTests.UnitU.Replace("Result := A + B;", "Result := B + A;", StringComparison.Ordinal));

        Assert.Equal(@"CMP-150 rule 4: C:\p\U.pas changed", CheckU(fs, cache, pcus));
    }

    [Fact]
    public void Rule4_SourceMissing()
    {
        var (fs, pcus, cache) = Cached();
        fs.Delete(@"C:\p\U.pas");

        Assert.Equal(@"CMP-150 rule 4: C:\p\U.pas is missing", CheckU(fs, cache, pcus));
    }

    /// <summary>Rewritten with the same bytes: another last-write time, so the hash is compared — and it is equal.</summary>
    [Fact]
    public void Rule4_TouchedButEqual_IsUpToDate()
    {
        var (fs, pcus, cache) = Cached();
        fs.Add(@"C:\p\U.pas", PcuTests.UnitU);

        Assert.Null(CheckU(fs, cache, pcus));
    }

    /// <summary>Equal size and last-write time: the hash is assumed equal (the recorded one is not even compared).</summary>
    [Fact]
    public void Rule4_SameSizeAndTime_SkipsTheHash()
    {
        var (fs, pcus, cache) = Cached();
        var pcu = pcus["U"];
        pcu["sources"]![0]!["sha256"] = new string('0', 64);
        fs.Add(Path.Combine(Obj, "U.pcu"), PcuWriter.Serialize(pcu));

        Assert.Null(CheckU(fs, cache, pcus));

        pcu["sources"]![0]!["size"] = 1;
        fs.Add(Path.Combine(Obj, "U.pcu"), PcuWriter.Serialize(pcu));
        Assert.Equal(@"CMP-150 rule 4: C:\p\U.pas changed", CheckU(fs, cache, pcus));
    }

    [Fact]
    public void Rule4_IncludeFileEdited()
    {
        var fs = PcuTests.Files(u: PcuTests.UnitU.Replace("function First: TThing;\nimplementation", "function First: TThing;\n{$I extra.inc}\nimplementation", StringComparison.Ordinal))
            .Add(@"C:\p\extra.inc", "const Extra = 1;\n");
        var pcus = PcuTests.Pcus(fs);
        fs.Add(Path.Combine(Obj, "U.pcu"), pcus["U"]).Add(Path.Combine(Obj, "U.obj"), "");
        var parsed = pcus.ToDictionary(p => p.Key, p => (JsonObject)JsonNode.Parse(p.Value)!);
        var cache = new UnitCache(fs, PcuWriter.CompilerFingerprint, Compilation.Create(PcuTests.Options(), [], fs).OptionsFingerprint());
        Assert.Equal(@"C:\p\extra.inc", (string)parsed["U"]["sources"]![1]!["path"]!);
        Assert.Null(CheckU(fs, cache, parsed));

        fs.Add(@"C:\p\extra.inc", "const Extra = 2;\n");

        Assert.Equal(@"CMP-150 rule 4: C:\p\extra.inc changed", CheckU(fs, cache, parsed));
    }

    /// <summary>An open file wins over the disk: its text is hashed.</summary>
    [Fact]
    public void Rule4_OpenFileText_IsHashed()
    {
        var (fs, pcus, _) = Cached();
        var options = (string)pcus["U"]["optionsFingerprint"]!;
        var same = new UnitCache(fs, PcuWriter.CompilerFingerprint, options, path => path == @"C:\p\U.pas" ? PcuTests.UnitU : null);
        var edited = new UnitCache(fs, PcuWriter.CompilerFingerprint, options, path => path == @"C:\p\U.pas" ? PcuTests.UnitU + " " : null);

        Assert.Null(CheckU(fs, same, pcus));
        Assert.Equal(@"CMP-150 rule 4: C:\p\U.pas changed", CheckU(fs, edited, pcus));
    }

    [Fact]
    public void Rule5_DependencyInterfaceChanged()
    {
        var (fs, pcus, cache) = Cached();

        Assert.Equal("CMP-150 rule 5: the interface of V changed", CheckU(fs, cache, pcus, Current(pcus, changed: "V")));
        Assert.Equal("CMP-150 rule 5: the interface of System changed", CheckU(fs, cache, pcus, Current(pcus, changed: "System")));
    }

    [Fact]
    public void Rule5_DependencyResolvesToAnotherFile()
    {
        var (fs, pcus, cache) = Cached();

        Assert.Equal(@"CMP-150 rule 5: unit V resolves to C:\q\V.pas", CheckU(fs, cache, pcus, Current(pcus, changed: "V", movedTo: @"C:\q\V.pas")));
    }

    [Fact]
    public void Rule5_DependencyNoLongerPartOfTheCompilation()
    {
        var (fs, pcus, cache) = Cached();

        Assert.Equal("CMP-150 rule 5: unit V is no longer part of the compilation", CheckU(fs, cache, pcus, name => name == "V" ? null : Current(pcus)(name)));
    }

    /// <summary>A precompiled unit has no source here and was compiled elsewhere: rules 2, 3 and the hashes of rule 5 apply, not the paths.</summary>
    [Fact]
    public void Precompiled_ChecksFingerprintsAndHashes_NotSourcesOrPaths()
    {
        var (fs, pcus, cache) = Cached();
        fs.Delete(@"C:\p\U.pas");

        Assert.Null(cache.CheckPrecompiled(pcus["U"], Current(pcus, changed: "V", movedTo: @"C:\lib\V.pcu")));
        Assert.Equal("CMP-150 rule 5: the interface of V changed", cache.CheckPrecompiled(pcus["U"], Current(pcus, changed: "V")));
        var other = new UnitCache(fs, PcuWriter.CompilerFingerprint, "sha256:other");
        Assert.Equal("CMP-150 rule 3: another options fingerprint", other.CheckPrecompiled(pcus["U"], Current(pcus)));
    }

    // ------------------------------------------------------------------ the compilation

    private static CompilationOptions WithFakeToolchain(InMemoryFileSystem fs, CompilationOptions options)
    {
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            fs.Add(Path.Combine(@"C:\llvm", tool), "");
        }

        return options with { ToolchainDirectory = @"C:\llvm" };
    }

    /// <summary>Everything up to date at <c>--emit obj</c>: nothing is emitted, no tool runs (the fake tools would fail), no file is written.</summary>
    [Fact]
    [Trait("Req", "CMP-153")]
    public void EverythingUpToDate_CompilesNothing()
    {
        var (fs, _, _) = Cached();
        var options = WithFakeToolchain(fs, PcuTests.Options());
        var before = fs.GetLastWriteTimeUtc(Path.Combine(Obj, "U.pcu"));

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Obj }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Empty(result.CompiledUnits);
        Assert.Equal(["P", "System", "V", "U"], result.UpToDateUnits);
        Assert.False(fs.FileExists(Path.Combine(Obj, "U.ll")));
        Assert.Equal(before, fs.GetLastWriteTimeUtc(Path.Combine(Obj, "U.pcu")));
    }

    /// <summary><c>--emit ll</c> ignores the cache and so does <see cref="EmitOptions.ForceRebuild"/>.</summary>
    [Fact]
    public void EmitLlAndForceRebuild_IgnoreTheCache()
    {
        var (fs, _, _) = Cached();
        var options = WithFakeToolchain(fs, PcuTests.Options());

        var ll = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        var forced = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Obj, ForceRebuild = true }, TestContext.Current.CancellationToken);

        Assert.Equal(["P", "System", "V", "U"], ll.CompiledUnits);
        Assert.Empty(ll.UpToDateUnits);
        Assert.Equal(["P", "System", "V", "U"], forced.CompiledUnits);
        Assert.False(fs.FileExists(Path.Combine(Obj, "U.pcu")), "a unit being recompiled loses its .pcu first (CMP-141)");
    }

    /// <summary>
    /// The program's code holds the unit table (CMP-355): an implementation-only edit that gives a unit an initialization section
    /// leaves every interface hash alone, yet the program must be recompiled.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-355")]
    public void UnitTableChange_MakesTheProgramStale()
    {
        var (fs, pcus, _) = Cached();
        var options = WithFakeToolchain(fs, PcuTests.Options());
        fs.Add(@"C:\p\V.pas", PcuTests.UnitV.Replace("implementation\nend.", "implementation\ninitialization\n  Writeln('V');\nend.", StringComparison.Ordinal));
        Assert.Equal(pcus["V"]["interfaceHash"]!.ToJsonString(), JsonNode.Parse(PcuTests.Pcus(fs)["V"])!["interfaceHash"]!.ToJsonString());

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Obj }, TestContext.Current.CancellationToken);

        Assert.Equal(["P", "V"], result.CompiledUnits);
        Assert.Equal(["System", "U"], result.UpToDateUnits);
    }

    [Fact]
    public void ProgramPcu_RecordsTheUnitTable() =>
        Assert.Equal(
            """[{"unit":"System","hasInit":false,"hasFinal":false},{"unit":"V","hasInit":false,"hasFinal":false},{"unit":"U","hasInit":false,"hasFinal":false}]""",
            JsonNode.Parse(PcuTests.Pcus(PcuTests.Files())["P"])!["unitTable"]!.ToJsonString());

    [Fact]
    [Trait("Req", "CMP-160")]
    public void PrecompiledUnit_CompiledWithOtherOptions_IsE2202AtTheFile()
    {
        var (fs, options, _) = PcuImportTests.PrecompiledInMemory();

        var result = Compilation.Create(options with { Defines = ["OTHER"] }, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(@"C:\lib\U.pcu(0,0): error E2202: Unit 'U' not found", result.Diagnostics.Select(d => d.ToText()));
    }

    /// <summary>U.pcu recorded V's interface hash; V's source next to it now has another interface: U cannot be used (nor recompiled).</summary>
    [Fact]
    public void PrecompiledUnit_WhoseDependencyInterfaceChanged_IsE2202AtTheFile()
    {
        var (fs, options, _) = PcuImportTests.PrecompiledInMemory();
        fs.Add(@"C:\lib\V.pas", PcuTests.UnitV.Replace("(One, Two)", "(One, Two, Three)", StringComparison.Ordinal));

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal([@"C:\lib\U.pcu(0,0): error E2202: Unit 'U' not found"], result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToText()));
    }

    [Fact]
    public void PrecompiledUnit_WhoseDependencyIsUnchanged_IsUsed()
    {
        var (fs, options, _) = PcuImportTests.PrecompiledInMemory();
        fs.Add(@"C:\lib\V.pas", PcuTests.UnitV.Replace("implementation\nend.", "implementation\nconst Hidden = 1;\nend.", StringComparison.Ordinal));

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
    }
}
