using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>toolchain.md §2, §4, §5 (CMP-605, 620…627): command lines, import libraries, link, error mapping.</summary>
public sealed partial class ToolchainTests
{
    private const string Obj = @"C:\p\obj\Debug";
    private const string Bin = @"C:\llvm\bin";

    private static ToolRunner Runner(IProcessRunner fake, Action<string>? commandLines = null) =>
        new(fake, new ToolRunOptions(MainLocation) { CommandLineSink = commandLines });

    // ---------------------------------------------------------------- CMP-605 clang

    [Theory]
    [InlineData(OptimizationLevel.O0, false, "-O0")]
    [InlineData(OptimizationLevel.O1, false, "-O1")]
    [InlineData(OptimizationLevel.O2, true, "-O2")]
    [Trait("Req", "CMP-605")]
    public void CommandLines_Clang_MatchesCmp605(OptimizationLevel level, bool debug, string flag)
    {
        var args = ClangInvoker.BuildArguments(Obj + @"\Hello.ll", Obj + @"\Hello.obj", level, debug);

        string[] expected =
        [
            "-c", Obj + @"\Hello.ll", "-o", Obj + @"\Hello.obj", flag, "--target=x86_64-pc-windows-msvc",
            "-fno-stack-protector", "-Wno-override-module", .. debug ? new[] { "-g" } : [],
        ];
        Assert.Equal(expected, args);
    }

    [Fact]
    [Trait("Req", "CMP-605")]
    public async Task CommandLines_Clang_RunsInTheObjectDirectory_AndEchoesTheSpecLine()
    {
        var fake = new FakeProcessRunner();
        var lines = new List<string>();

        var result = await new ClangInvoker(Runner(fake, lines.Add), Bin + @"\clang.exe")
            .CompileAsync(Obj + @"\System.ll", Obj + @"\System.obj", OptimizationLevel.O0, debugInfo: false, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        var call = Assert.Single(fake.Calls);
        Assert.Equal(Bin + @"\clang.exe", call.FileName);
        Assert.Equal(Obj, call.WorkingDirectory);
        Assert.Equal(
            [Bin + @"\clang.exe -c " + Obj + @"\System.ll -o " + Obj + @"\System.obj -O0 --target=x86_64-pc-windows-msvc -fno-stack-protector -Wno-override-module"],
            lines);
    }

    [Fact]
    [Trait("Req", "CMP-603")]
    public async Task ErrorMapping_ClangFailure_IsE4004_AndKeepsTheLl()
    {
        var fs = new InMemoryFileSystem().Add(Obj + @"\Hello.ll", "bad");
        var fake = new FakeProcessRunner(1, stdErr: [Obj + @"\Hello.ll:1:1: error: expected top-level entity"]);

        var result = await new ClangInvoker(Runner(fake), Bin + @"\clang.exe")
            .CompileAsync(Obj + @"\Hello.ll", Obj + @"\Hello.obj", OptimizationLevel.O2, debugInfo: false, TestContext.Current.CancellationToken);

        Assert.Equal("E4004", result.Failure!.Code);
        Assert.Equal("clang.exe failed with exit code 1", result.Failure.Message);
        Assert.Equal(Obj + @"\Hello.ll:1:1: error: expected top-level entity", Assert.Single(result.Failure.Related).Message);
        Assert.True(fs.FileExists(Obj + @"\Hello.ll"));
    }

    // ---------------------------------------------------------------- CMP-620 … 622 import libraries

    [Fact]
    [Trait("Req", "CMP-620")]
    public void CommandLines_ImportDefinition_SearchOrder()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\units\Kernel32.def", "unit-def")
            .Add(@"C:\decl\kernel32.def", "declaring-dir")
            .Add(@"C:\repo\rtl\winapi\def\kernel32.def", "rtl")
            .Add(@"C:\repo\rtl\winapi\def\ntdll.def", "rtl")
            .Add(@"C:\decl\user32.def", "declaring-dir");
        const string Rtl = @"C:\repo\rtl\src";

        Assert.Equal(@"C:\units\Kernel32.def", Find("kernel32.dll", [@"C:\units\other.def", @"C:\units\Kernel32.def"]));
        Assert.Equal(@"C:\decl\kernel32.def", Find("kernel32.dll", []));
        Assert.Equal(@"C:\repo\rtl\winapi\def\ntdll.def", Find("ntdll.dll", []));
        Assert.Equal(@"C:\decl\user32.def", Find("USER32.DLL", []), ignoreCase: true);

        var (path, diagnostic) = DllToolInvoker.FindDefinition(fs, "gdi32.dll", [], [@"C:\decl"], Rtl, MainLocation);
        Assert.Null(path);
        Assert.Equal("E4006", diagnostic!.Code);
        Assert.Equal("No import definition (.def) found for 'gdi32.dll'", diagnostic.Message);

        string? Find(string dll, ImmutableArray<string> unitDefs) =>
            DllToolInvoker.FindDefinition(fs, dll, unitDefs, [@"C:\none", @"C:\decl"], Rtl, MainLocation).Path;
    }

    [Fact]
    [Trait("Req", "CMP-622")]
    public async Task CommandLines_DllTool_MatchesCmp622_AndRecordsTheHash()
    {
        const string Def = @"C:\repo\rtl\winapi\def\kernel32.def";
        var fs = new InMemoryFileSystem().Add(Def, "LIBRARY kernel32.dll\nEXPORTS\nExitProcess\n");
        var fake = new FakeProcessRunner((call, _) =>
        {
            fs.Add(call.Arguments[^1], "lib");
            return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
        });
        var lines = new List<string>();

        var result = await new DllToolInvoker(Runner(fake, lines.Add), Bin + @"\llvm-dlltool.exe", fs)
            .BuildAsync("kernel32.dll", Def, Obj, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.True(result.Regenerated);
        Assert.Equal(Obj + @"\imports\kernel32.lib", result.LibraryPath);
        var call = Assert.Single(fake.Calls);
        Assert.Equal(["-m", "i386:x86-64", "-d", Def, "-D", "kernel32.dll", "-l", Obj + @"\imports\kernel32.lib"], call.Arguments);
        Assert.Equal(Obj, call.WorkingDirectory);
        Assert.Equal([Bin + @"\llvm-dlltool.exe -m i386:x86-64 -d " + Def + " -D kernel32.dll -l " + Obj + @"\imports\kernel32.lib"], lines);
        Assert.Equal(Sha256Hex(fs.ReadAllBytes(Def)), fs.ReadAllText(Obj + @"\imports\kernel32.lib.sha256"));
    }

    [Fact]
    [Trait("Req", "CMP-622")]
    public async Task CommandLines_DllTool_RegeneratesOnlyWhenTheDefChangesOrTheLibIsMissing()
    {
        const string Def = @"C:\d\kernel32.def";
        var fs = new InMemoryFileSystem().Add(Def, "v1");
        var fake = new FakeProcessRunner((call, _) =>
        {
            fs.Add(call.Arguments[^1], "lib");
            return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
        });
        var invoker = new DllToolInvoker(Runner(fake), Bin + @"\llvm-dlltool.exe", fs);
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await invoker.BuildAsync("kernel32.dll", Def, Obj, ct)).Regenerated);
        Assert.False((await invoker.BuildAsync("kernel32.dll", Def, Obj, ct)).Regenerated);
        fs.Add(Def, "v2");
        Assert.True((await invoker.BuildAsync("kernel32.dll", Def, Obj, ct)).Regenerated);
        fs.Delete(Obj + @"\imports\kernel32.lib");
        Assert.True((await invoker.BuildAsync("kernel32.dll", Def, Obj, ct)).Regenerated);
        fs.Delete(Obj + @"\imports\kernel32.lib.sha256");
        Assert.True((await invoker.BuildAsync("kernel32.dll", Def, Obj, ct)).Regenerated);
        Assert.Equal(4, fake.Calls.Count);
    }

    [Fact]
    [Trait("Req", "CMP-622")]
    public async Task ErrorMapping_DllToolFailure_IsE4004_AndForgetsTheHash()
    {
        const string Def = @"C:\d\kernel32.def";
        var fs = new InMemoryFileSystem().Add(Def, "v2").Add(Obj + @"\imports\kernel32.lib", "old").Add(Obj + @"\imports\kernel32.lib.sha256", "stale");

        var result = await new DllToolInvoker(Runner(new FakeProcessRunner(1, stdErr: ["llvm-dlltool: error: bad"])), Bin + @"\llvm-dlltool.exe", fs)
            .BuildAsync("kernel32.dll", Def, Obj, TestContext.Current.CancellationToken);

        Assert.Equal("E4004", result.Failure!.Code);
        Assert.Equal("llvm-dlltool.exe failed with exit code 1", result.Failure.Message);
        Assert.False(fs.FileExists(Obj + @"\imports\kernel32.lib.sha256"));
    }

    [Fact]
    [Trait("Req", "CMP-622")]
    public async Task CommandLines_DllTool_CreatesTheImportsDirectory()
    {
        var fs = new RecordingFileSystem(new InMemoryFileSystem().Add(@"C:\d\ntdll.def", "x"));

        await new DllToolInvoker(Runner(new FakeProcessRunner()), Bin + @"\llvm-dlltool.exe", fs)
            .BuildAsync("ntdll.dll", @"C:\d\ntdll.def", Obj, TestContext.Current.CancellationToken);

        Assert.Contains(Obj + @"\imports", fs.CreatedDirectories);
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    // ---------------------------------------------------------------- CMP-625 link

    private static LinkRequest HelloRequest() => new(
        @"C:\p\bin\Hello.exe",
        Obj,
        [Obj + @"\Hello.obj", Obj + @"\System.obj", Obj + @"\SysUtils.obj", Obj + @"\Classes.obj"])
    {
        ExtraObjects = [@"C:\x\zeta.obj", @"C:\x\Alpha.obj"],
        ImportLibraries = [Obj + @"\imports\ntdll.lib", Obj + @"\imports\kernel32.lib"],
    };

    [Fact]
    [Trait("Req", "CMP-625")]
    public void CommandLines_Link_ResponseFileMatchesCmp625()
    {
        var text = LinkInvoker.RenderResponseFile(LinkInvoker.BuildArguments(HelloRequest()));

        Assert.Equal(
            """
            /nologo
            /out:C:\p\bin\Hello.exe
            /entry:_start
            /subsystem:console
            /machine:x64
            /nodefaultlib
            /incremental:no
            /opt:ref
            /opt:icf
            /Brepro
            /stack:1048576,16384
            /largeaddressaware
            /dynamicbase
            /nxcompat
            C:\p\obj\Debug\Hello.obj
            C:\p\obj\Debug\System.obj
            C:\p\obj\Debug\SysUtils.obj
            C:\p\obj\Debug\Classes.obj
            C:\x\Alpha.obj
            C:\x\zeta.obj
            C:\p\obj\Debug\imports\kernel32.lib
            C:\p\obj\Debug\imports\ntdll.lib

            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    [Trait("Req", "CMP-625")]
    public void CommandLines_Link_GuiDebugManifestAndStack()
    {
        var request = HelloRequest() with
        {
            OutputPath = @"C:\my app\Hello.exe",
            AppType = AppType.Gui,
            DebugInfo = true,
            ManifestPath = Obj + @"\app.manifest",
            MaxStackSize = 4194304,
            MinStackSize = 65536,
        };

        var args = LinkInvoker.BuildArguments(request);

        Assert.Equal(
            ["/nologo", @"/out:C:\my app\Hello.exe", "/entry:_start", "/subsystem:windows", "/machine:x64", "/nodefaultlib",
             "/incremental:no", "/opt:ref", "/opt:icf", "/Brepro", "/stack:4194304,65536", "/largeaddressaware", "/dynamicbase",
             "/nxcompat", "/debug", "/manifest:embed", @"/manifestinput:C:\p\obj\Debug\app.manifest"],
            args.Take(17));
        Assert.StartsWith("\"/out:C:\\my app\\Hello.exe\"\n", LinkInvoker.RenderResponseFile(args)[8..], StringComparison.Ordinal);
        Assert.Contains("/debug:dwarf", LinkInvoker.BuildArguments(request with { DebugFormat = DebugFormat.Dwarf }));
        Assert.DoesNotContain("/debug", LinkInvoker.BuildArguments(request with { DebugFormat = DebugFormat.Dwarf }));
    }

    [Fact]
    [Trait("Req", "CMP-625")]
    [Trait("Req", "CMP-627")]
    public async Task CommandLines_Link_WritesTheResponseFileAndRunsLldLinkInTheObjectDirectory()
    {
        var fs = new RecordingFileSystem(new InMemoryFileSystem());
        var fake = new FakeProcessRunner();
        var lines = new List<string>();

        var result = await new LinkInvoker(Runner(fake, lines.Add), Bin + @"\lld-link.exe", fs).LinkAsync(HelloRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        var call = Assert.Single(fake.Calls);
        Assert.Equal(["@" + Obj + @"\link.rsp"], call.Arguments);
        Assert.Equal(Obj, call.WorkingDirectory);
        Assert.Equal([Bin + @"\lld-link.exe @" + Obj + @"\link.rsp"], lines);
        Assert.Equal(LinkInvoker.RenderResponseFile(LinkInvoker.BuildArguments(HelloRequest())), fs.ReadAllText(Obj + @"\link.rsp"));
        Assert.Contains(@"C:\p\bin", fs.CreatedDirectories);
    }

    // ---------------------------------------------------------------- CMP-626 / 627 error mapping

    [Fact]
    [Trait("Req", "CMP-626")]
    [Trait("Req", "CMP-623")]
    public void ErrorMapping_UndefinedAndDuplicateSymbols()
    {
        string[] output =
        [
            "lld-link: error: undefined symbol: __declspec(dllimport) Foo",
            ">>> referenced by Hello.obj:(_start)",
            "",
            "lld-link: error: undefined symbol: floor",
            ">>> referenced by System.obj:(Trunc)",
            "lld-link: error: duplicate symbol: Dup",
            ">>> defined at a.obj",
            ">>> defined at b.obj",
        ];
        var defs = ImmutableDictionary.Create<string, string>().Add("Foo", @"C:\repo\rtl\winapi\def\kernel32.def");

        var d = LinkInvoker.MapErrors(output, 1, MainLocation, defs);

        Assert.Equal(["E4005", "E4005", "E4007"], d.Select(x => x.Code));
        Assert.Equal("Unresolved external symbol '__imp_Foo'", d[0].Message);
        Assert.Equal(@"add it to C:\repo\rtl\winapi\def\kernel32.def", Assert.Single(d[0].Related).Message);
        Assert.Equal("Unresolved external symbol 'floor'", d[1].Message);
        Assert.Empty(d[1].Related);
        Assert.Equal("Duplicate symbol 'Dup'", d[2].Message);
        Assert.All(d, x => Assert.Equal(MainLocation, x.Location));
    }

    [Fact]
    [Trait("Req", "CMP-626")]
    public void ErrorMapping_AnyOtherLinkError_IsE4004_WithTheWholeOutput()
    {
        string[] output = ["lld-link: error: could not open 'x.obj': no such file or directory", "lld-link: error: undefined symbol: Bar"];

        var d = LinkInvoker.MapErrors(output, 1, MainLocation, ImmutableDictionary<string, string>.Empty);

        Assert.Equal(["E4004", "E4005"], d.Select(x => x.Code));
        Assert.Equal("lld-link.exe failed with exit code 1", d[0].Message);
        Assert.Equal(output, d[0].Related.Select(r => r.Message));
        Assert.Equal("E4004", Assert.Single(LinkInvoker.MapErrors(["something odd"], 3, MainLocation, ImmutableDictionary<string, string>.Empty)).Code);
        Assert.Empty(LinkInvoker.MapErrors(["lld-link: warning: ignoring unknown argument"], 0, MainLocation, ImmutableDictionary<string, string>.Empty));
    }

    [Fact]
    [Trait("Req", "CMP-627")]
    public async Task ErrorMapping_LinkFailure_DeletesTheStaleExe()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\bin\Hello.exe", "old exe");
        var fake = new FakeProcessRunner(1, stdErr: ["lld-link: error: undefined symbol: Bar"]);

        var result = await new LinkInvoker(Runner(fake), Bin + @"\lld-link.exe", fs).LinkAsync(HelloRequest(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("E4005", Assert.Single(result.Diagnostics).Code);
        Assert.False(fs.FileExists(@"C:\p\bin\Hello.exe"));
    }

    // ---------------------------------------------------------------- real toolchain

    private const string HelloIr = """
        target triple = "x86_64-pc-windows-msvc"
        declare dllimport ptr @GetStdHandle(i32)
        declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
        declare dllimport void @ExitProcess(i32)
        @msg = private constant [7 x i8] c"linked\0A"
        define void @_start() {
          %w = alloca i32
          %h = call ptr @GetStdHandle(i32 -11)
          %r = call i32 @WriteFile(ptr %h, ptr @msg, i32 7, ptr %w, ptr null)
          call void @ExitProcess(i32 7)
          unreachable
        }

        """;

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-605")]
    [Trait("Req", "CMP-622")]
    [Trait("Req", "CMP-625")]
    public async Task CommandLines_RealToolchain_CompileImportLinkAndRun()
    {
        var bin = Path.GetDirectoryName(Clang.Path)!;
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "with space");
        var obj = Path.Combine(dir, "obj");
        var fs = PhysicalFileSystem.Instance;
        fs.WriteAllBytes(Path.Combine(obj, "Hello.ll"), Encoding.UTF8.GetBytes(HelloIr));
        fs.WriteAllBytes(Path.Combine(dir, "kernel32.def"), Encoding.UTF8.GetBytes("LIBRARY kernel32.dll\nEXPORTS\nExitProcess\nGetStdHandle\nWriteFile\n"));
        var runner = new ToolRunner(new ProcessRunner(), new ToolRunOptions(MainLocation));
        var ct = TestContext.Current.CancellationToken;

        var compiled = await new ClangInvoker(runner, Clang.Path).CompileAsync(Path.Combine(obj, "Hello.ll"), Path.Combine(obj, "Hello.obj"), OptimizationLevel.O2, false, ct);
        Assert.Null(compiled.Failure);
        var lib = await new DllToolInvoker(runner, Path.Combine(bin, "llvm-dlltool.exe"), fs).BuildAsync("kernel32.dll", Path.Combine(dir, "kernel32.def"), obj, ct);
        Assert.Null(lib.Failure);
        var exe = Path.Combine(dir, "out bin", "Hello.exe");
        var linked = await new LinkInvoker(runner, Path.Combine(bin, "lld-link.exe"), fs)
            .LinkAsync(new LinkRequest(exe, obj, [Path.Combine(obj, "Hello.obj")]) { ImportLibraries = [lib.LibraryPath] }, ct);
        Assert.True(linked.Success, string.Join("\n", linked.Diagnostics.Select(d => d.ToText() + " " + string.Join(" | ", d.Related.Select(r => r.Message)))));

        var output = new List<string>();
        var exitCode = await new ProcessRunner().RunAsync(exe, [], dir, output.Add, output.Add, ct);
        Assert.Equal(7, exitCode);
        Assert.Equal(["linked"], output);
        Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-626")]
    [Trait("Req", "CMP-627")]
    public async Task ErrorMapping_RealLinker_UnresolvedImportAndDuplicate()
    {
        var bin = Path.GetDirectoryName(Clang.Path)!;
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var fs = PhysicalFileSystem.Instance;
        fs.WriteAllBytes(Path.Combine(dir, "a.ll"), Encoding.UTF8.GetBytes("declare dllimport void @Foo()\ndefine void @_start() {\n  call void @Foo()\n  ret void\n}\ndefine void @Dup() {\n  ret void\n}\n"));
        fs.WriteAllBytes(Path.Combine(dir, "b.ll"), Encoding.UTF8.GetBytes("define void @Dup() {\n  ret void\n}\n"));
        var exe = Path.Combine(dir, "x.exe");
        fs.WriteAllBytes(exe, [1, 2, 3]);
        var runner = new ToolRunner(new ProcessRunner(), new ToolRunOptions(MainLocation));
        var clang = new ClangInvoker(runner, Clang.Path);
        var link = new LinkInvoker(runner, Path.Combine(bin, "lld-link.exe"), fs);
        var ct = TestContext.Current.CancellationToken;
        foreach (var name in new[] { "a", "b" })
        {
            Assert.Null((await clang.CompileAsync(Path.Combine(dir, name + ".ll"), Path.Combine(dir, name + ".obj"), OptimizationLevel.O0, false, ct)).Failure);
        }

        var unresolved = await link.LinkAsync(
            new LinkRequest(exe, dir, [Path.Combine(dir, "a.obj")]) { ImportDefinitions = ImmutableDictionary<string, string>.Empty.Add("Foo", @"C:\defs\k.def") }, ct);
        var duplicate = await link.LinkAsync(new LinkRequest(exe, dir, [Path.Combine(dir, "a.obj"), Path.Combine(dir, "b.obj")]), ct);

        var e4005 = Assert.Single(unresolved.Diagnostics);
        Assert.Equal("Unresolved external symbol '__imp_Foo'", e4005.Message);
        Assert.Equal(@"add it to C:\defs\k.def", Assert.Single(e4005.Related).Message);
        Assert.Equal("Duplicate symbol 'Dup'", Assert.Single(duplicate.Diagnostics).Message);
        Assert.False(File.Exists(exe));
        Directory.Delete(dir, recursive: true);
    }
}

/// <summary>Delegates to another file system and records <see cref="IFileSystem.CreateDirectory"/> calls.</summary>
internal sealed class RecordingFileSystem(IFileSystem inner) : IFileSystem
{
    public List<string> CreatedDirectories { get; } = [];

    public bool FileExists(string path) => inner.FileExists(path);

    public string ReadAllText(string path) => inner.ReadAllText(path);

    public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => inner.EnumerateFiles(directory, searchPattern);

    public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

    public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(path);

    public long GetLength(string path) => inner.GetLength(path);

    public void WriteAllBytes(string path, byte[] data) => inner.WriteAllBytes(path, data);

    public void Delete(string path) => inner.Delete(path);

    public void CreateDirectory(string path)
    {
        CreatedDirectories.Add(path);
        inner.CreateDirectory(path);
    }
}
