using System.Text;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// dll-output.md §4 (task C-288; CMP-1061…1065): the export definition file, the library response file, the import library
/// output and the up-to-date rule of a library link — with recorded tool calls, no process is started.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class DllLinkTests
{
    private const string Bin = @"C:\llvm\bin";
    private const string ObjDir = @"C:\p\obj";
    private const string Dll = @"C:\p\bin\mathlib.dll";
    private const string Lib = @"C:\p\bin\mathlib.lib";
    private const string Def = ObjDir + @"\mathlib.exports.def";

    /// <summary>The example of dll-output.md §2.3.</summary>
    private const string MathLib = """
        library mathlib;

        type
          TVec3 = record X, Y, Z: Integer end;

        function Add(A, B: Integer): Integer; stdcall;
        begin
          Result := A + B;
        end;

        function MakeVec(X, Y, Z: Integer): TVec3; cdecl;
        begin
          Result.X := X; Result.Y := Y; Result.Z := Z;
        end;

        exports
          Add,
          MakeVec name 'make_vec' index 7;

        begin
        end.
        """;

    private const string NoExports = "library mathlib;\nbegin\nend.\n";

    private static InMemoryFileSystem Fs(string source)
    {
        var fs = new InMemoryFileSystem().AddSystem().Add(@"C:\p\mathlib.pas", source);
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            fs.Add(Path.Combine(Bin, tool), "");
        }

        foreach (var def in Directory.GetFiles(TestRtl.DefinitionDirectory, "*.def"))
        {
            fs.Add(Path.Combine(@"C:\winapi\def", Path.GetFileName(def)), File.ReadAllText(def));
        }

        return fs;
    }

    /// <summary>Tools that write what the real ones would: <c>-o</c> / <c>-l</c> outputs and the linker's <c>/out:</c> and <c>/implib:</c> files.</summary>
    private static FakeProcessRunner Tools(InMemoryFileSystem fs) => new((call, _) =>
    {
        if (call.Arguments is ["--version"])
        {
            return Task.FromResult((0, new[] { "clang version " + ToolchainVersionCheck.PinnedVersion }, Array.Empty<string>()));
        }

        var args = call.Arguments;
        if (args is [var rsp] && rsp.StartsWith('@'))
        {
            foreach (var line in fs.ReadAllText(rsp[1..]).Split('\n'))
            {
                if (line.StartsWith("/out:", StringComparison.Ordinal) || line.StartsWith("/implib:", StringComparison.Ordinal))
                {
                    fs.Add(line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..], "linked");
                }
            }
        }
        else
        {
            var output = Array.FindIndex(args, a => a is "-o" or "-l");
            fs.Add(args[output + 1], "tool output");
        }

        return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
    });

    private static CompilationOptions Options(bool debug = false, string manifest = "") => new()
    {
        MainFile = @"C:\p\mathlib.pas",
        RtlDirectory = @"C:\rtl",
        ObjectDirectory = ObjDir,
        ToolchainDirectory = Bin,
        AppType = AppType.Library,
        DebugInfo = debug,
        Manifest = manifest,
    };

    private static async Task<EmitResult> EmitAsync(InMemoryFileSystem fs, FakeProcessRunner tools, EmitStage stage = EmitStage.Exe,
        CompilationOptions? options = null, string output = Dll)
    {
        var result = await Compilation.Create(options ?? Options(), [], fs)
            .EmitAsync(new EmitOptions { Stage = stage, OutputPath = output, ProcessRunner = tools }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return result;
    }

    private static int Links(FakeProcessRunner tools) => tools.Calls.Count(c => c.FileName.EndsWith("lld-link.exe", StringComparison.Ordinal));

    [Fact]
    [Trait("Req", "CMP-1061")]
    [Trait("Req", "CMP-1066")]
    public async Task ExportDef_ExactContent()
    {
        var fs = Fs(MathLib);
        var tools = Tools(fs);

        // CMP-1066: written from stage obj on, not at stage ll
        await EmitAsync(fs, tools, EmitStage.Ll);
        Assert.False(fs.FileExists(Def));
        await EmitAsync(fs, tools, EmitStage.Obj);

        // UTF-8 without BOM, LF, final newline; ordinal order; four spaces, name, space, @ordinal
        Assert.Equal(Encoding.ASCII.GetBytes("EXPORTS\n    Add @1\n    make_vec @7\n"), fs.ReadAllBytes(Def));
        Assert.Equal(0, Links(tools));

        // written only when its content changes
        var written = fs.GetLastWriteTimeUtc(Def);
        await EmitAsync(fs, tools, EmitStage.Obj);
        Assert.Equal(written, fs.GetLastWriteTimeUtc(Def));

        // named after the output; without an output path after the main file (the default output is <main>.dll next to it)
        await EmitAsync(fs, tools, EmitStage.Obj, output: @"C:\out\other.dll");
        Assert.True(fs.FileExists(ObjDir + @"\other.exports.def"));
        await EmitAsync(fs, tools, EmitStage.Exe, output: "");
        Assert.True(fs.FileExists(@"C:\p\mathlib.dll"));

        // ordinal order, not source order; a library without exports has the single line
        var reordered = Fs(MathLib.Replace("Add,", "Add index 9,", StringComparison.Ordinal));
        await EmitAsync(reordered, Tools(reordered), EmitStage.Obj);
        Assert.Equal("EXPORTS\n    make_vec @7\n    Add @9\n", reordered.ReadAllText(Def));
        var none = Fs(NoExports);
        await EmitAsync(none, Tools(none), EmitStage.Obj);
        Assert.Equal("EXPORTS\n", none.ReadAllText(Def));
    }

    [Fact]
    [Trait("Req", "CMP-1062")]
    [Trait("Req", "CMP-1060")]
    public async Task ResponseFile_ExactContent()
    {
        var fs = Fs(MathLib);
        var result = await EmitAsync(fs, Tools(fs));

        Assert.Equal(Dll, result.OutputPath);
        var expected = string.Join("\n",
            "/nologo",
            "/out:" + Dll,
            "/dll",
            "/entry:_dllstart",
            "/subsystem:windows",
            "/machine:x64",
            "/nodefaultlib",
            "/incremental:no",
            "/opt:ref",
            "/opt:icf",
            "/Brepro",
            "/largeaddressaware",
            "/dynamicbase",
            "/nxcompat",
            "/def:" + Def,
            "/implib:" + Lib,
            ObjDir + @"\mathlib.obj",
            ObjDir + @"\System.obj",
            ObjDir + @"\imports\kernel32.lib",
            ObjDir + @"\imports\ntdll.lib",
            "");
        Assert.Equal(expected, fs.ReadAllText(ObjDir + @"\link.rsp"));

        // -g and a manifest file: after /implib, before the objects; the manifest is resource id 2
        fs.Add(@"C:\p\lib.manifest", "<assembly/>");
        await EmitAsync(fs, Tools(fs), options: Options(debug: true, manifest: @"C:\p\lib.manifest"));
        var lines = fs.ReadAllText(ObjDir + @"\link.rsp").Split('\n');
        var implib = Array.IndexOf(lines, "/implib:" + Lib);
        Assert.Equal(["/debug", "/manifest:embed,id=2", @"/manifestinput:C:\p\lib.manifest", ObjDir + @"\mathlib.obj"], lines[(implib + 1)..(implib + 5)]);
        Assert.DoesNotContain(lines, l => l.StartsWith("/stack", StringComparison.Ordinal));

        // `default` is treated as `none` for a library
        await EmitAsync(fs, Tools(fs), options: Options(manifest: "default"));
        Assert.DoesNotContain(fs.ReadAllText(ObjDir + @"\link.rsp").Split('\n'), l => l.StartsWith("/manifest", StringComparison.Ordinal));
        Assert.False(fs.FileExists(ObjDir + @"\app.manifest"));

        // the entry symbol and the extra lines come from the target (extra lines directly after /nxcompat)
        var target = TargetInfo.Win64 with { Id = "tools", LibraryEntrySymbol = "_fakedll", LinkerMachine = "fake", LinkerArguments = ["/fake:line"] };
        var other = Fs(MathLib);
        await EmitAsync(other, Tools(other), options: Options() with { Target = target });
        lines = other.ReadAllText(ObjDir + @"\link.rsp").Split('\n');
        Assert.Equal("/entry:_fakedll", lines[3]);
        Assert.Equal("/machine:fake", lines[5]);
        Assert.Equal(["/nxcompat", "/fake:line", "/def:" + Def], lines[13..16]);
    }

    [Fact]
    [Trait("Req", "CMP-1063")]
    public async Task NoExports_NoImplibLine_StaleLibDeleted()
    {
        var fs = Fs(NoExports).Add(Lib, "stale import library");
        var tools = Tools(fs);

        await EmitAsync(fs, tools);

        Assert.True(fs.FileExists(Dll));
        Assert.DoesNotContain(fs.ReadAllText(ObjDir + @"\link.rsp").Split('\n'), l => l.StartsWith("/implib:", StringComparison.Ordinal));
        Assert.Contains("/def:" + Def, fs.ReadAllText(ObjDir + @"\link.rsp").Split('\n'));
        Assert.False(fs.FileExists(Lib), "a library without exports leaves no stale import library");

        // up to date without an import library
        await EmitAsync(fs, tools);
        Assert.Equal(1, Links(tools));
    }

    /// <summary>CMP-1064: a failed link leaves neither the DLL nor its import library.</summary>
    [Fact]
    [Trait("Req", "CMP-1064")]
    public async Task FailedLink_DeletesDllAndImportLibrary()
    {
        var fs = Fs(MathLib);
        await EmitAsync(fs, Tools(fs));
        Assert.True(fs.FileExists(Dll) && fs.FileExists(Lib));
        fs.Add(@"C:\p\mathlib.pas", MathLib.Replace("A + B", "A - B", StringComparison.Ordinal));
        var failing = new FakeProcessRunner((call, _) => Task.FromResult(call.Arguments is ["--version"]
            ? (0, new[] { "clang version " + ToolchainVersionCheck.PinnedVersion }, Array.Empty<string>())
            : call.FileName.EndsWith("lld-link.exe", StringComparison.Ordinal)
                ? (1, Array.Empty<string>(), new[] { "lld-link: error: <root>: undefined symbol: Add" })
                : (0, Array.Empty<string>(), Array.Empty<string>())));

        var result = await Compilation.Create(Options(), [], fs)
            .EmitAsync(new EmitOptions { OutputPath = Dll, ProcessRunner = failing }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(["E4004"], result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code));   // never a user error
        Assert.False(fs.FileExists(Dll));
        Assert.False(fs.FileExists(Lib));
    }

    [Fact]
    [Trait("Req", "CMP-1065")]
    public async Task UpToDate_DefChangeRelinks()
    {
        var fs = Fs(MathLib);
        var tools = Tools(fs);
        await EmitAsync(fs, tools);
        Assert.Equal(1, Links(tools));

        // nothing changed: no tool runs
        var calls = tools.Calls.Count;
        var again = await EmitAsync(fs, tools);
        Assert.Equal(calls, tools.Calls.Count);
        Assert.Empty(again.CompiledUnits);
        Assert.Equal(Dll, again.OutputPath);

        // the content of the definition file changed in this run (here: it was tampered with and is rewritten) → relink only
        fs.Add(Def, "EXPORTS\n");
        var relinked = await EmitAsync(fs, tools);
        Assert.Empty(relinked.CompiledUnits);
        Assert.Equal(2, Links(tools));
        Assert.Equal("EXPORTS\n    Add @1\n    make_vec @7\n", fs.ReadAllText(Def));

        // the import library or the DLL is gone → relink only
        fs.Delete(Lib);
        Assert.Empty((await EmitAsync(fs, tools)).CompiledUnits);
        Assert.Equal(3, Links(tools));
        Assert.True(fs.FileExists(Lib));
        fs.Delete(Dll);
        Assert.Empty((await EmitAsync(fs, tools)).CompiledUnits);
        Assert.Equal(4, Links(tools));

        // an edited exports clause: the library file is recompiled, the definition file rewritten, the DLL relinked
        fs.Add(@"C:\p\mathlib.pas", MathLib.Replace("index 7", "index 8", StringComparison.Ordinal));
        Assert.Equal(["mathlib"], (await EmitAsync(fs, tools)).CompiledUnits);
        Assert.Equal(5, Links(tools));
        Assert.Equal("EXPORTS\n    Add @1\n    make_vec @8\n", fs.ReadAllText(Def));
        await EmitAsync(fs, tools);
        Assert.Equal(5, Links(tools));
    }
}
