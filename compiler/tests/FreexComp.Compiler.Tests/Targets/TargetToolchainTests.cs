using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Tests.Toolchain;
using FreexComp.Compiler.Toolchain;
using FreexComp.Tests.Shared;
using static FreexComp.Compiler.Tests.Targets.TargetTestSupport;

namespace FreexComp.Compiler.Tests.Targets;

/// <summary>targets.md CMP-1006, CMP-1014…1016 (task C-282): the tool recipes and the layout facts of a target.</summary>
[Trait("Phase", "8")]
public sealed class TargetToolchainTests
{
    private const string Bin = @"C:\llvm\bin";
    private const string ObjDir = @"C:\p\obj\Debug";

    /// <summary>Builds program <c>P</c> (uses unit <c>A</c>) to an exe with recorded tool calls: the calls (sorted) and <c>link.rsp</c>.</summary>
    private static async Task<(string[] Calls, string ResponseFile, string ProgramLl)> BuildAsync(TargetInfo target)
    {
        var fs = new InMemoryFileSystem().AddSystem()
            .Add(@"C:\p\P.pas", "program P; uses A; begin Writeln(FA); end.")
            .Add(@"C:\p\A.pas", "unit A; interface function FA: Integer; implementation function FA: Integer; begin Result := 1; end; end.");
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            fs.Add(Path.Combine(Bin, tool), "");
        }

        foreach (var def in Directory.GetFiles(TestRtl.DefinitionDirectory, "*.def"))
        {
            fs.Add(Path.Combine(@"C:\winapi\def", Path.GetFileName(def)), File.ReadAllText(def));
        }

        var runner = new FakeProcessRunner((call, _) => Task.FromResult(call.Arguments is ["--version"]
            ? (0, new[] { "clang version " + ToolchainVersionCheck.PinnedVersion }, Array.Empty<string>())
            : (0, Array.Empty<string>(), Array.Empty<string>())));
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = ObjDir, ToolchainDirectory = Bin, Target = target };
        var result = await Compilation.Create(options, [], fs)
            .EmitAsync(new EmitOptions { Stage = EmitStage.Exe, OutputPath = @"C:\p\P.exe", ProcessRunner = runner }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));

        var calls = runner.Calls.Where(c => c.Arguments is not ["--version"])
            .Select(c => Path.GetFileName(c.FileName) + " " + string.Join(' ', c.Arguments)).Order(StringComparer.Ordinal).ToArray();
        return (calls, fs.ReadAllText(Path.Combine(ObjDir, "link.rsp")), fs.ReadAllText(Path.Combine(ObjDir, "P.ll")));
    }

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1014")]
    [Trait("Req", "CMP-1015")]
    [Trait("Req", "CMP-1016")]
    public async Task Win64_CommandLinesUnchanged()
    {
        var (calls, rsp, _) = await BuildAsync(TargetInfo.Win64);

        string[] expected =
        [
            // toolchain.md CMP-605
            $@"clang.exe -c {ObjDir}\A.ll -o {ObjDir}\A.obj -O0 --target=x86_64-pc-windows-msvc -fno-stack-protector -Wno-override-module -mno-incremental-linker-compatible",
            $@"clang.exe -c {ObjDir}\P.ll -o {ObjDir}\P.obj -O0 --target=x86_64-pc-windows-msvc -fno-stack-protector -Wno-override-module -mno-incremental-linker-compatible",
            $@"clang.exe -c {ObjDir}\System.ll -o {ObjDir}\System.obj -O0 --target=x86_64-pc-windows-msvc -fno-stack-protector -Wno-override-module -mno-incremental-linker-compatible",
            // CMP-625
            $@"lld-link.exe @{ObjDir}\link.rsp",
            // CMP-622
            $@"llvm-dlltool.exe -m i386:x86-64 -d C:\winapi\def\kernel32.def -D kernel32.dll -l {ObjDir}\imports\kernel32.lib",
            $@"llvm-dlltool.exe -m i386:x86-64 -d C:\winapi\def\ntdll.def -D ntdll.dll -l {ObjDir}\imports\ntdll.lib",
        ];
        Assert.Equal(expected, calls);

        // CMP-625: byte for byte
        var expectedRsp = string.Join("\n",
            "/nologo",
            @"/out:C:\p\P.exe",
            "/entry:_start",
            "/subsystem:console",
            "/machine:x64",
            "/nodefaultlib",
            "/incremental:no",
            "/opt:ref",
            "/opt:icf",
            "/Brepro",
            "/stack:1048576,16384",
            "/largeaddressaware",
            "/dynamicbase",
            "/nxcompat",
            "/manifest:embed",                                              // CMP-1100 (C-295): a console program embeds the generated manifest
            $@"/manifestinput:{ObjDir}\app.manifest",
            $@"{ObjDir}\P.obj",
            $@"{ObjDir}\System.obj",
            $@"{ObjDir}\A.obj",
            $@"{ObjDir}\imports\kernel32.lib",
            $@"{ObjDir}\imports\ntdll.lib",
            "");
        Assert.Equal(expectedRsp, rsp);

        // the argument builders without a target are those of the baseline target, -g last
        Assert.Equal(
            ["-c", "a.ll", "-o", "a.obj", "-O2", "--target=x86_64-pc-windows-msvc", "-fno-stack-protector", "-Wno-override-module", "-mno-incremental-linker-compatible", "-g"],
            ClangInvoker.BuildArguments("a.ll", "a.obj", OptimizationLevel.O2, debugInfo: true));
        Assert.Equal(ClangInvoker.BuildArguments("a.ll", "a.obj", OptimizationLevel.O1, false), ClangInvoker.BuildArguments("a.ll", "a.obj", OptimizationLevel.O1, false, TargetInfo.Win64));
        Assert.Equal(["-m", "i386:x86-64", "-d", "k.def", "-D", "k.dll", "-l", "k.lib"], DllToolInvoker.BuildArguments("k.def", "k.dll", "k.lib"));
    }

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1014")]
    [Trait("Req", "CMP-1015")]
    [Trait("Req", "CMP-1016")]
    public async Task FakeTarget_MachineFlagsFromDescriptor()
    {
        var target = TargetInfo.Win64 with
        {
            Id = "tools",
            ClangArguments = ["--target=x86_64-pc-windows-msvc", "-mfake-cpu"],
            DllToolMachine = "fake:machine",
            LinkerMachine = "fake",
            LinkerArguments = ["/safeseh:no", "/fake:line"],
            EntrySymbol = "_fakestart",
        };

        var (calls, rsp, programLl) = await BuildAsync(target);

        // CMP-1014: -O<n>, then the target's arguments, then the fixed flags
        Assert.Equal(3, calls.Count(c => c.StartsWith("clang.exe ", StringComparison.Ordinal)));
        Assert.All(calls.Where(c => c.StartsWith("clang.exe ", StringComparison.Ordinal)), c => Assert.EndsWith(
            " -O0 --target=x86_64-pc-windows-msvc -mfake-cpu -fno-stack-protector -Wno-override-module -mno-incremental-linker-compatible", c, StringComparison.Ordinal));
        // CMP-1015
        Assert.Equal(2, calls.Count(c => c.StartsWith("llvm-dlltool.exe -m fake:machine -d ", StringComparison.Ordinal)));
        Assert.DoesNotContain(calls, c => c.Contains(TargetInfo.Win64.DllToolMachine, StringComparison.Ordinal));
        // CMP-1016: entry and machine from the target, its extra lines directly after /nxcompat
        var lines = rsp.Split('\n');
        Assert.Equal("/entry:_fakestart", lines[2]);
        Assert.Equal("/machine:fake", lines[4]);
        var nxcompat = Array.IndexOf(lines, "/nxcompat");
        Assert.Equal(
            ["/safeseh:no", "/fake:line", "/manifest:embed", $@"/manifestinput:{ObjDir}\app.manifest", $@"{ObjDir}\P.obj"],   // manifest lines: CMP-1100 (C-295)
            lines[(nxcompat + 1)..(nxcompat + 6)]);
        Assert.DoesNotContain("/machine:x64", lines);
        Assert.DoesNotContain("/entry:_start", lines);
        // the entry routine the linker is told about is the one the program module defines
        Assert.Contains("define void @_fakestart() #0", programLl, StringComparison.Ordinal);
        Assert.DoesNotContain("@_start", programLl, StringComparison.Ordinal);

        // the debug flags follow the target's lines' position rule too: /debug comes after them
        var request = new LinkRequest(@"C:\p\P.exe", ObjDir, [$@"{ObjDir}\P.obj"]) { Target = target, DebugInfo = true };
        var args = LinkInvoker.BuildArguments(request);
        Assert.Equal(["/nxcompat", "/safeseh:no", "/fake:line", "/debug"], args.Skip(args.IndexOf("/nxcompat")).Take(4));
    }

    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1006")]
    public async Task Win64_DataLayoutMatchesToolchain()
    {
        var target = TargetInfo.Win64;
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // an empty module, compiled to textual IR with the descriptor's own arguments: the back end states its layout
            await File.WriteAllTextAsync(Path.Combine(dir, "empty.ll"), "; empty module\n", TestContext.Current.CancellationToken);
            var output = new ConcurrentQueue<string>();
            var exitCode = await NativeRun.RunAsync(Clang.Path, [.. target.ClangArguments, "-Wno-override-module", "-S", "-emit-llvm", "empty.ll", "-o", "out.ll"],
                dir, output.Enqueue, output.Enqueue);
            Assert.True(exitCode == 0, string.Join("\n", output));
            var emitted = await File.ReadAllTextAsync(Path.Combine(dir, "out.ll"), TestContext.Current.CancellationToken);
            var layout = Regex.Match(emitted, "^target datalayout = \"([^\"]*)\"", RegexOptions.Multiline).Groups[1].Value;

            Assert.Equal(target.DataLayout, layout);
            Assert.StartsWith(target.Triple, Regex.Match(emitted, "^target triple = \"([^\"]*)\"", RegexOptions.Multiline).Groups[1].Value, StringComparison.Ordinal);

            // each layout fact of the front end against its component (LangRef "Data Layout"; absent components take LangRef's defaults)
            var components = layout.Split('-');
            static int Bits(string text) => int.Parse(text, CultureInfo.InvariantCulture);
            var pointer = components.FirstOrDefault(c => c.StartsWith("p:", StringComparison.Ordinal))?.Split(':');      // p[0]:<size>:<abi>; default p:64:64
            Assert.Equal(target.PointerSize * 8, pointer is null ? 64 : Bits(pointer[1]));
            Assert.Equal(target.PointerAlign * 8, pointer is null ? 64 : Bits(pointer[2]));
            var i64 = components.FirstOrDefault(c => c.StartsWith("i64:", StringComparison.Ordinal))?.Split(':');         // i64:<abi>; default i64:32:64
            Assert.Equal(target.Int64Align * 8, i64 is null ? 32 : Bits(i64[1]));
            var f64 = components.FirstOrDefault(c => c.StartsWith("f64:", StringComparison.Ordinal))?.Split(':');         // f64:<abi>; default f64:64:64
            Assert.Equal(target.Float64Align * 8, f64 is null ? 64 : Bits(f64[1]));
            var stack = components.Single(c => Regex.IsMatch(c, "^S[0-9]+$"));                                              // S<bits>
            Assert.Equal(target.StackAlign * 8, Bits(stack[1..]));
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }
}
