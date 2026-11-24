using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>targets.md CMP-1023 (task C-283): the import-definition lookup tries the target's sub-directory before the flat folder.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ImportLibraryPlanTests
{
    private const string RtlSrc = @"C:\repo\rtl\src";
    private const string Flat = @"C:\repo\rtl\winapi\def";
    private static readonly Location Main = Location.ForFile(@"C:\p\P.pas");

    private static string[] Planned(InMemoryFileSystem fs, TargetInfo? target, params string[] dlls)
    {
        var unit = new LinkUnit(@"C:\p", new UnitLinkInputs([.. dlls], [], []));
        var (libraries, errors) = target is null
            ? ImportLibraryPlan.Create(fs, [unit], @"C:\rtl-sys", RtlSrc, Main)
            : ImportLibraryPlan.Create(fs, [unit], @"C:\rtl-sys", RtlSrc, Main, target);
        Assert.Empty(errors);
        return [.. libraries.Select(l => l.Dll + " <- " + l.Definition)];
    }

    [Fact]
    [Trait("Req", "CMP-1023")]
    public void TargetDefSubdirectoryWinsOverFlatFolder()
    {
        var fake = TargetInfo.Win64 with { Id = "fake32", ImportDefSubdirectory = "fake32" };
        var fs = new InMemoryFileSystem()
            .Add(Flat + @"\kernel32.def", "flat")
            .Add(Flat + @"\ntdll.def", "flat")
            .Add(Flat + @"\user32.def", "flat")
            .Add(Flat + @"\fake32\user32.def", "decorated")
            .Add(Flat + @"\fake32\kernel32.def", "decorated");

        // the target's sub-directory first, the flat folder for what it does not hold
        Assert.Equal(
            [$@"kernel32.dll <- {Flat}\fake32\kernel32.def", $@"ntdll.dll <- {Flat}\ntdll.def", $@"user32.dll <- {Flat}\fake32\user32.def"],
            Planned(fs, fake, "user32.dll"));

        // the baseline target has no sub-directory on disk: skipped silently, every definition from the flat folder as before
        string[] flat = [$@"kernel32.dll <- {Flat}\kernel32.def", $@"ntdll.dll <- {Flat}\ntdll.def", $@"user32.dll <- {Flat}\user32.def"];
        Assert.Equal(flat, Planned(fs, TargetInfo.Win64, "user32.dll"));
        Assert.Equal(flat, Planned(fs, null, "user32.dll"));

        // … and would win there too once it exists
        fs.Add(Flat + @"\win64\user32.def", "x64");
        Assert.Equal($@"user32.dll <- {Flat}\win64\user32.def", Planned(fs, TargetInfo.Win64, "user32.dll")[2]);

        // the earlier steps of CMP-620 are unchanged: the declaring unit's directory still wins over both
        fs.Add(@"C:\p\user32.def", "unit");
        Assert.Equal(@"user32.dll <- C:\p\user32.def", Planned(fs, fake, "user32.dll")[2]);

        // not found anywhere → E4006, as before
        var (path, diagnostic) = DllToolInvoker.FindDefinition(fs, "gdi32.dll", [], [@"C:\p"], RtlSrc, Main, fake);
        Assert.Null(path);
        Assert.Equal("E4006", diagnostic!.Code);
    }

    private static string[] PlannedWith(InMemoryFileSystem fs, string[] libraryPaths, LinkUnit unit)
    {
        var (libraries, errors) = ImportLibraryPlan.Create(fs, [unit], @"C:\rtl-sys", RtlSrc, Main, null, libraryPaths);
        Assert.Empty(errors);
        return [.. libraries.Select(l => l.Dll + (l.ReadyMade ? " = " : " <- ") + l.Definition)];
    }

    /// <summary>dll-output.md CMP-1071 (task C-289): a ready-made import library is step 3 of the resolution order.</summary>
    [Fact]
    [Trait("Req", "CMP-1071")]
    [Trait("Req", "CMP-1072")]
    public void ReadyMadeLibBetweenUnitDefAndRtlDef()
    {
        var fs = new InMemoryFileSystem()
            .Add(Flat + @"\kernel32.def", "flat")
            .Add(Flat + @"\ntdll.def", "flat")
            .Add(Flat + @"\user32.def", "flat")
            .Add(@"C:\libs2\mathlib.lib", "second")
            .Add(@"C:\libs2\user32.lib", "ready-made")
            .Add(@"C:\libs2\kernel32.lib", "ready-made");
        string[] paths = [@"C:\libs1", @"C:\libs2"];
        var unit = new LinkUnit(@"C:\p", new UnitLinkInputs(["mathlib.dll", "user32.dll"], [], []));

        // step 3 before step 4 (the RTL's definitions), also for the always-linked DLLs; a DLL in no library path keeps step 4
        Assert.Equal(
            [@"kernel32.dll = C:\libs2\kernel32.lib", @"mathlib.dll = C:\libs2\mathlib.lib", $@"ntdll.dll <- {Flat}\ntdll.def", @"user32.dll = C:\libs2\user32.lib"],
            PlannedWith(fs, paths, unit));

        // the library paths in order: the first directory that has the file wins
        fs.Add(@"C:\libs1\mathlib.lib", "first");
        Assert.Equal(@"mathlib.dll = C:\libs1\mathlib.lib", PlannedWith(fs, paths, unit)[1]);
        Assert.Equal(@"mathlib.dll = C:\libs2\mathlib.lib", PlannedWith(fs, [@"C:\libs2", @"C:\libs1"], unit)[1]);

        // step 2 (the declaring unit's directory) before step 3
        fs.Add(@"C:\p\mathlib.def", "unit");
        Assert.Equal(@"mathlib.dll <- C:\p\mathlib.def", PlannedWith(fs, paths, unit)[1]);
        fs.Delete(@"C:\p\mathlib.def");

        // step 1 ({$L file.def} of a unit) before step 3
        fs.Add(@"C:\x\MathLib.def", "linked");
        Assert.Equal(@"mathlib.dll <- C:\x\MathLib.def",
            PlannedWith(fs, paths, new LinkUnit(@"C:\p", new UnitLinkInputs(["mathlib.dll", "user32.dll"], [], [@"C:\x\MathLib.def"])))[1]);

        // without library paths nothing changes; nothing found anywhere stays E4006
        var (_, errors) = ImportLibraryPlan.Create(fs, [unit], @"C:\rtl-sys", RtlSrc, Main);
        Assert.Equal(["No import definition (.def) found for 'mathlib.dll'"], errors.Select(e => e.Message));

        // CMP-1072: a name the ready-made library does not provide is E4005 with its own hint
        var imports = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty
            .Add("Foo", @"C:\libs2\mathlib.lib").Add("Bar", @"C:\p\other.def");
        var mapped = LinkInvoker.MapErrors(
            ["lld-link: error: undefined symbol: __declspec(dllimport) Foo", "lld-link: error: undefined symbol: __declspec(dllimport) Bar"], 1, Main, imports);
        Assert.Equal(["E4005", "E4005"], mapped.Select(d => d.Code));
        Assert.Equal(@"not exported by C:\libs2\mathlib.lib", Assert.Single(mapped[0].Related).Message);
        Assert.Equal(@"add it to C:\p\other.def", Assert.Single(mapped[1].Related).Message);
    }

    /// <summary>
    /// CMP-1071 step 3 end to end with recorded tools: the ready-made library goes to the linker by absolute path (not copied, no
    /// <c>llvm-dlltool</c> run); its content hash is recorded, and a library rebuilt with other content relinks.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1070")]
    [Trait("Req", "CMP-1071")]
    public async Task ReadyMadeLibContentChangeRelinks()
    {
        const string objDir = @"C:\p\obj";
        const string hashFile = objDir + @"\imports\mathlib.ext.sha256";
        var fs = new InMemoryFileSystem().AddSystem()
            .Add(@"C:\p\P.pas", "program P;\nfunction Add(A, B: Integer): Integer; stdcall; external 'mathlib.dll';\nbegin\n  Writeln(Add(1, 2));\nend.\n")
            .Add(@"C:\libs\mathlib.lib", "import library 1");
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            fs.Add(Path.Combine(@"C:\llvm", tool), "");
        }

        foreach (var def in Directory.GetFiles(TestRtl.DefinitionDirectory, "*.def"))
        {
            fs.Add(Path.Combine(@"C:\winapi\def", Path.GetFileName(def)), File.ReadAllText(def));
        }

        var tools = new FakeProcessRunner((call, _) =>
        {
            if (call.Arguments is ["--version"])
            {
                return Task.FromResult((0, new[] { "clang version " + ToolchainVersionCheck.PinnedVersion }, Array.Empty<string>()));
            }

            if (call.Arguments is [var rsp] && rsp.StartsWith('@'))
            {
                fs.Add(@"C:\p\P.exe", "linked");
            }
            else
            {
                fs.Add(call.Arguments[Array.FindIndex(call.Arguments, a => a is "-o" or "-l") + 1], "tool output");
            }

            return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
        });
        int Links() => tools.Calls.Count(c => c.FileName.EndsWith("lld-link.exe", StringComparison.Ordinal));
        async Task<EmitResult> BuildAsync(params string[] libraryPaths)
        {
            var options = new CompilationOptions
            {
                MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = objDir, ToolchainDirectory = @"C:\llvm", LibraryPaths = [.. libraryPaths],
            };
            var result = await Compilation.Create(options, [], fs)
                .EmitAsync(new EmitOptions { OutputPath = @"C:\p\P.exe", ProcessRunner = tools }, TestContext.Current.CancellationToken);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
            return result;
        }

        await BuildAsync(@"C:\libs");

        // by absolute path, among the import libraries (sorted by name); not copied, not generated
        var lines = fs.ReadAllText(objDir + @"\link.rsp").Split('\n');
        Assert.Equal([objDir + @"\imports\kernel32.lib", @"C:\libs\mathlib.lib", objDir + @"\imports\ntdll.lib", ""], lines[^4..]);
        Assert.False(fs.FileExists(objDir + @"\imports\mathlib.lib"));
        Assert.DoesNotContain(tools.Calls, c => c.Arguments.Contains("mathlib.dll"));
        var hash = fs.ReadAllText(hashFile);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fs.ReadAllBytes(@"C:\libs\mathlib.lib"))), hash);
        Assert.Equal(1, Links());

        // unchanged: no tool runs
        var calls = tools.Calls.Count;
        await BuildAsync(@"C:\libs");
        Assert.Equal(calls, tools.Calls.Count);

        // the import library was rebuilt with other content: relink only, the new hash is recorded
        fs.Add(@"C:\libs\mathlib.lib", "import library 2");
        Assert.Empty((await BuildAsync(@"C:\libs")).CompiledUnits);
        Assert.Equal(2, Links());
        Assert.NotEqual(hash, fs.ReadAllText(hashFile));
        await BuildAsync(@"C:\libs");
        Assert.Equal(2, Links());

        // the same content from another directory: link.rsp names another path, so it relinks as well
        fs.Add(@"C:\first\mathlib.lib", "import library 2");
        Assert.Empty((await BuildAsync(@"C:\first", @"C:\libs")).CompiledUnits);
        Assert.Equal(3, Links());
        Assert.Contains(@"C:\first\mathlib.lib", fs.ReadAllText(objDir + @"\link.rsp").Split('\n'));

        // no library path has it and no definition exists: E4006 as before
        var missing = await Compilation.Create(
                new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = objDir, ToolchainDirectory = @"C:\llvm" }, [], fs)
            .EmitAsync(new EmitOptions { OutputPath = @"C:\p\P.exe", ProcessRunner = tools }, TestContext.Current.CancellationToken);
        Assert.Equal(["E4006"], missing.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code));
    }
}
