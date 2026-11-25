using FreexComp.Compiler;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Build.Tests;

/// <summary>resource-compiler.md §5 (CMP-1313…1322): resource scripts and compiled resource files in a project build.</summary>
[Trait("Phase", "8")]
public sealed class ResourceBuildTests
{
    private const string Obj = @"C:\p\obj\Debug";
    private const string LibrarySource = "library Hello;\nfunction Add(A, B: Integer): Integer; stdcall;\nbegin\n  Result := A + B;\nend;\nexports Add;\nbegin\nend.\n";

    private static readonly byte[] PlainRes = FakeResourceTools.File((10, 7, [1, 2, 3]));
    private static readonly byte[] ManifestRes = FakeResourceTools.File((10, 7, [1, 2, 3]), (ResourceFile.ManifestType, 1, [60, 97, 47, 62]));

    /// <param name="files">The elements of the project's <c>files</c> array.</param>
    private static BuildWorkspace Workspace(string files, string extra = "", string program = BuildWorkspace.HelloProgram)
    {
        var w = new BuildWorkspace(program, BuildWorkspace.ProjectJson("Hello", extra: $"\"files\": [{files}]" + (extra.Length > 0 ? ", " + extra : "")));
        w.Fs.Add(BuildWorkspace.Llvm + @"\llvm-rc.exe", "");
        return w;
    }

    private static string[] Rsp(BuildWorkspace w) => w.Fs.ReadAllText(Obj + @"\link.rsp").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static List<Diagnostic> Diagnostics(EventLog log) => [.. log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value)];

    [Fact]
    [Trait("Req", "CMP-1315")]
    [Trait("Req", "CMP-1316")]
    [Trait("Req", "CMP-1319")]
    public async Task ScriptIsCompiledBeforeTheLink_AndLinked()
    {
        var w = Workspace("""{ "path": "res/app.rc" }, { "path": "pre.res" }, { "path": "second.rc" }""");
        w.Fs.Add(@"C:\p\res\app.rc", "1 RCDATA \"data.bin\"\n").Add(@"C:\p\res\data.bin", "data").Add(@"C:\p\second.rc", "2 RCDATA BEGIN 1 END\n");
        w.Fs.WriteAllBytes(@"C:\p\pre.res", PlainRes);

        var (result, log) = await w.RunAsync(w.Request());

        Assert.True(result.Success, string.Join("\n", log.Lines));

        // after every unit, script by script in file order, then the link
        var calls = w.Tools.Calls;
        Assert.Equal(["clang.exe", "llvm-rc.exe", "clang.exe", "llvm-rc.exe", "lld-link.exe"], calls[^5..]);
        Assert.DoesNotContain(calls[..^5], c => c is "llvm-rc.exe" or "lld-link.exe");   // everything before: the units and import libraries
        Assert.Equal(
            [("clang.exe", @"C:\p\res\app.rc"), ("llvm-rc.exe", Obj + @"\app.rc.i"), ("clang.exe", @"C:\p\second.rc"), ("llvm-rc.exe", Obj + @"\second.rc.i")],
            w.Tools.ResourceCalls.Select(c => (c.Tool, c.Arguments[^1])));
        Assert.Equal(@"C:\p\res", w.Tools.ResourceCalls[1].WorkingDirectory);
        var info = log.Info;
        Assert.Equal(
            ["Compiling resources app.rc", "Compiling resources second.rc", "Linking Hello.exe"],
            info.Where(l => l.StartsWith("Compiling resources ", StringComparison.Ordinal) || l.StartsWith("Linking ", StringComparison.Ordinal)));
        Assert.All(info[..info.IndexOf("Compiling resources app.rc")], l => Assert.StartsWith("Compiling ", l, StringComparison.Ordinal));   // the units come first
        Assert.True(info.IndexOf("Compiling resources app.rc") >= 2);

        // CMP-1316: X.rc → <objdir>\X.res with its record; a listed .res is used where it lies
        Assert.True(w.Fs.FileExists(Obj + @"\app.res") && w.Fs.FileExists(Obj + @"\app.res.json") && w.Fs.FileExists(Obj + @"\second.res.json"));
        Assert.False(w.Fs.FileExists(Obj + @"\pre.res"));

        // CMP-1319: after the objects, before the import libraries, in file order
        var rsp = Rsp(w);
        var first = Array.IndexOf(rsp, Obj + @"\app.res");
        Assert.EndsWith(".obj", rsp[first - 1], StringComparison.Ordinal);
        Assert.Equal([Obj + @"\app.res", @"C:\p\pre.res", Obj + @"\second.res"], rsp[first..(first + 3)]);
        Assert.All(rsp[(first + 3)..], line => Assert.EndsWith(".lib", line, StringComparison.Ordinal));
        Assert.NotEmpty(rsp[(first + 3)..]);

        // CMP-1318: what the link took
        Assert.Equal(
            string.Concat(new[] { Obj + @"\app.res", @"C:\p\pre.res", Obj + @"\second.res" }.Select(p => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(w.Fs.ReadAllBytes(p))) + " " + p + "\n")),
            w.Fs.ReadAllText(Obj + @"\link.resources"));

        // CMP-1317, 1318: nothing changed → no process, up to date
        var (again, againLog) = await w.RunAsync(w.Request());
        Assert.True(again.Success && again.UpToDate, string.Join("\n", againLog.Lines));
        Assert.Equal(calls, w.Tools.Calls);
    }

    [Fact]
    [Trait("Req", "CMP-1316")]
    public async Task ListedResIsLinkedWithoutCompiling()
    {
        var w = Workspace("""{ "path": "pre.res" }""");
        w.Fs.WriteAllBytes(@"C:\p\pre.res", PlainRes);

        var (result, log) = await w.RunAsync(w.Request());

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Empty(w.Tools.ResourceCalls);
        Assert.DoesNotContain(log.Info, l => l.StartsWith("Compiling resources", StringComparison.Ordinal));
        Assert.Contains(@"C:\p\pre.res", Rsp(w));
        Assert.False(w.Fs.FileExists(Obj + @"\rc-include\rcdefs.h"));
    }

    [Fact]
    [Trait("Req", "CMP-1314")]
    public async Task DefinesAndSearchPathsOfTheConfigurationReachTheTools()
    {
        var w = Workspace(
            """{ "path": "app.rc" }""",
            """
            "searchPaths": ["shared"], "defines": ["PROJ"],
            "configurations": {
              "Debug": { "optimization": 0, "debugInfo": false, "defines": ["DBG"], "searchPaths": ["dbg"], "outputDir": "bin/Debug" },
              "Demo": { "inherits": "Debug", "defines": ["DEMO=1"], "searchPaths": ["demo"], "outputDir": "bin/Demo" }
            }
            """);
        w.Fs.Add(@"C:\p\app.rc", "1 RCDATA BEGIN 1 END\n");

        var (debug, debugLog) = await w.RunAsync(w.Request());
        var (demo, demoLog) = await w.RunAsync(w.Request() with { Configuration = "Demo" });

        Assert.True(debug.Success, string.Join("\n", debugLog.Lines));
        Assert.True(demo.Success, string.Join("\n", demoLog.Lines));
        var calls = w.Tools.ResourceCalls;
        Assert.Equal(["clang.exe", "llvm-rc.exe", "clang.exe", "llvm-rc.exe"], calls.Select(c => c.Tool));
        Assert.Equal(
            ["-E", "-x", "c", "-w", "-nostdinc", "-DRC_INVOKED", "-DPROJ", "-DDBG", "-I", @"C:\p", "-I", @"C:\p\shared", "-I", @"C:\p\dbg",
                "-isystem", Obj + @"\rc-include", "-o", Obj + @"\app.rc.pp", @"C:\p\app.rc"],
            calls[0].Arguments);
        Assert.Equal(
            ["/no-preprocess", "/C", "65001", "/I", @"C:\p", "/I", @"C:\p\shared", "/I", @"C:\p\dbg", "/fo", Obj + @"\app.res.tmp", Obj + @"\app.rc.i"],
            calls[1].Arguments);
        const string DemoObj = @"C:\p\obj\Demo";
        Assert.Equal(
            ["-E", "-x", "c", "-w", "-nostdinc", "-DRC_INVOKED", "-DPROJ", "-DDBG", "-DDEMO=1", "-I", @"C:\p", "-I", @"C:\p\shared", "-I", @"C:\p\dbg", "-I", @"C:\p\demo",
                "-isystem", DemoObj + @"\rc-include", "-o", DemoObj + @"\app.rc.pp", @"C:\p\app.rc"],
            calls[2].Arguments);
    }

    [Fact]
    [Trait("Req", "CMP-1316")]
    public async Task SameBaseName_E3114()
    {
        var w = Workspace("""{ "path": "a/app.rc" }, { "path": "b/APP.rc" }""");
        w.Fs.Add(@"C:\p\a\app.rc", "1 RCDATA BEGIN 1 END\n").Add(@"C:\p\b\APP.rc", "2 RCDATA BEGIN 1 END\n");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): error E3114: Resource scripts 'C:\p\a\app.rc' and 'C:\p\b\APP.rc' have the same name; their compiled files would collide"],
            Diagnostics(log).Select(d => d.ToText()));
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);
        Assert.Empty(w.Tools.ResourceCalls);
    }

    [Fact]
    [Trait("Req", "CMP-1313")]
    public async Task UnsupportedExtension_E3117()
    {
        var w = Workspace("""{ "path": "notes.txt", "kind": "resource" }""");
        w.Fs.Add(@"C:\p\notes.txt", "not a script");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): error E3117: Resource file 'C:\p\notes.txt' has an unsupported extension (expected .rc or .res)"],
            Diagnostics(log).Select(d => d.ToText()));
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);

        // "kind": "other" keeps a script out of the build
        var other = Workspace("""{ "path": "app.rc", "kind": "other" }""");
        other.Fs.Add(@"C:\p\app.rc", FakeResourceTools.SyntaxError);
        var (ok, okLog) = await other.RunAsync(other.Request());
        Assert.True(ok.Success, string.Join("\n", okLog.Lines));
        Assert.Empty(other.Tools.ResourceCalls);
        Assert.DoesNotContain(Rsp(other), l => l.EndsWith(".res", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-1321")]
    public async Task InvalidRes_E3116()
    {
        var w = Workspace("""{ "path": "pre.res" }""");
        w.Fs.Add(@"C:\p\pre.res", "this is not a compiled resource file, although it is long enough");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal([@"C:\p\Hello.pproj(0,0): error E3116: 'C:\p\pre.res' is not a valid compiled resource file"], Diagnostics(log).Select(d => d.ToText()));
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);
    }

    [Theory]
    [InlineData(false, "", true)]                                           // program, generated manifest: both would embed one
    [InlineData(false, "\"manifest\": \"none\"", false)]                    // the manifest of the resource file is the program's
    [InlineData(true, "\"appType\": \"library\"", false)]                   // a library embeds none by default
    [InlineData(true, "\"appType\": \"library\", \"manifest\": \"lib.manifest\"", true)]
    [Trait("Req", "CMP-1320")]
    public async Task ManifestInResource_E3115_UnlessManifestIsNone(bool library, string extra, bool error)
    {
        var w = Workspace("""{ "path": "plain.res" }, { "path": "with.res" }""", extra, library ? LibrarySource : BuildWorkspace.HelloProgram);
        w.Fs.Add(@"C:\p\lib.manifest", "<assembly/>");
        w.Fs.WriteAllBytes(@"C:\p\plain.res", PlainRes);
        w.Fs.WriteAllBytes(@"C:\p\with.res", ManifestRes);

        var (result, log) = await w.RunAsync(w.Request());

        if (error)
        {
            Assert.False(result.Success);
            Assert.Equal(
                [@"C:\p\Hello.pproj(0,0): error E3115: Resource file 'C:\p\with.res' contains an application manifest and the program embeds one too; "
                    + "set the manifest option to none or remove the manifest from the resource file"],
                Diagnostics(log).Select(d => d.ToText()));
            Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);
        }
        else
        {
            Assert.True(result.Success, string.Join("\n", log.Lines));
            Assert.Contains(@"C:\p\with.res", Rsp(w));
            Assert.DoesNotContain(Rsp(w), l => l.StartsWith("/manifestinput:", StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Req", "CMP-1315")]
    public async Task ScriptError_NoLink_BuildFails()
    {
        var w = Workspace("""{ "path": "bad.rc" }, { "path": "good.rc" }""");
        w.Fs.Add(@"C:\p\bad.rc", "1 RCDATA " + FakeResourceTools.SyntaxError + "\n").Add(@"C:\p\good.rc", "1 RCDATA BEGIN 1 END\n");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\p\bad.rc(0,0): error E3112: Resource script error: Error parsing file: expected int or string, got SYNTAX"],
            Diagnostics(log).Select(d => d.ToText()));
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.False(w.Fs.FileExists(Obj + @"\bad.res") || w.Fs.FileExists(Obj + @"\bad.res.tmp") || w.Fs.FileExists(Obj + @"\bad.res.json"));
        Assert.True(w.Fs.FileExists(Obj + @"\good.res.json"));              // the other script was still compiled

        // corrected: only that script is compiled, then the link
        w.Fs.Add(@"C:\p\bad.rc", "1 RCDATA BEGIN 1 END\n");
        var before = w.Tools.ResourceCalls.Length;
        var (fixedResult, fixedLog) = await w.RunAsync(w.Request());
        Assert.True(fixedResult.Success, string.Join("\n", fixedLog.Lines));
        Assert.Equal(["clang.exe", "llvm-rc.exe"], w.Tools.ResourceCalls[before..].Select(c => c.Tool));
        Assert.Equal(["Compiling resources bad.rc", "Linking Hello.exe"], fixedLog.Info);
    }

    [Fact]
    [Trait("Req", "CMP-1322")]
    public async Task MissingListedResource_W9002_BuildContinues()
    {
        var w = Workspace("""{ "path": "gone.rc" }, { "path": "gone.res" }""");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal([("W9002", @"C:\p\gone.rc"), ("W9002", @"C:\p\gone.res")], Diagnostics(log).Select(d => (d.Code, (string)d.Arguments[0])));
        Assert.Empty(w.Tools.ResourceCalls);
        Assert.DoesNotContain(Rsp(w), l => l.EndsWith(".res", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-1322")]
    public async Task Cancel_DuringAScript_RemovesPartialOutput()
    {
        var w = Workspace("""{ "path": "app.rc" }""");
        w.Fs.Add(@"C:\p\app.rc", "1 RCDATA BEGIN 1 END\n");
        using var cts = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        w.Tools.BeforeTool = async (tool, _, ct) =>
        {
            if (tool == "llvm-rc.exe")
            {
                w.Fs.Add(Obj + @"\app.res.tmp", "half-written");
                reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };

        var run = w.RunAsync(w.Request(), cts.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        cts.Cancel();
        var (result, log) = await run;

        Assert.False(result.Success);
        Assert.True(result.Canceled);
        Assert.Equal("E9010", Assert.Single(Diagnostics(log)).Code);
        Assert.False(w.Fs.FileExists(Obj + @"\app.res.tmp"));
        Assert.False(w.Fs.FileExists(Obj + @"\app.res") || w.Fs.FileExists(Obj + @"\app.res.json"));
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);

        // the next build compiles the script and links
        w.Tools.BeforeTool = null;
        var (next, nextLog) = await w.RunAsync(w.Request());
        Assert.True(next.Success, string.Join("\n", nextLog.Lines));
        Assert.Contains("Compiling resources app.rc", nextLog.Info);
        Assert.Contains("Linking Hello.exe", nextLog.Info);
    }
}
