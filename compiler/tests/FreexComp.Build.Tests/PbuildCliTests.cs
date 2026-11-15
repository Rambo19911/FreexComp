using FreexComp.Compiler;
using FreexComp.PBuild;

namespace FreexComp.Build.Tests;

/// <summary>C-147: the <c>pbuild</c> command line (compiler-cli.md §4, build-engine.md CMP-840…842), run in-process.</summary>
[Trait("Phase", "3")]
public sealed class PbuildCliTests
{
    private const string Summary = @"\(\d+ errors, \d+ warnings, \d+ hints, \d+\.\d\d s\)$";

    private static (int ExitCode, string[] Out, string Err) Run(BuildWorkspace w, params string[] args) => Run(w, w.Fs, args);

    private static (int ExitCode, string[] Out, string Err) Run(BuildWorkspace w, IFileSystem fs, params string[] args)
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var env = new PbuildEnvironment(@"C:\p", stdout, stderr, fs)
        {
            ProcessRunner = w.Tools,
            ToolchainDirectory = BuildWorkspace.Llvm,
            RtlDirectory = BuildWorkspace.Rtl,
        };
        var code = PbuildCommand.Run(args, env, TestContext.Current.CancellationToken);
        var lines = stdout.ToString().Split('\n');
        return (code, lines[^1].Length == 0 ? lines[..^1] : lines, stderr.ToString());
    }

    [Fact]
    [Trait("Req", "CMP-840")]
    public void Version_And_Help()
    {
        var w = new BuildWorkspace();

        var (versionCode, version, _) = Run(w, "--version");
        var (helpCode, help, _) = Run(w, "--help");

        Assert.Equal(0, versionCode);
        Assert.Matches(@"^pbuild 0\.1\.0 \(abi 1, llvm \d+\.\d+\.\d+ pin\)$", Assert.Single(version));
        Assert.Equal(0, helpCode);
        Assert.StartsWith("pbuild <project.pproj | group.pgroup>", help[0], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-840")]
    [Trait("Req", "CMP-841")]
    public void Text_Success_InfoAndToolLines_ThenTheSummary_ExitZero()
    {
        var w = new BuildWorkspace();

        var (code, output, err) = Run(w, "Hello.pproj");

        Assert.Equal(0, code);
        Assert.Equal(["Compiling Hello", "Compiling System", "Linking Hello.exe", "fake lld-link done"], output[..^1]);
        Assert.Matches("^Build succeeded " + Summary, output[^1]);
        Assert.Empty(err);
    }

    [Fact]
    [Trait("Req", "CMP-840")]
    [Trait("Req", "CMP-841")]
    public void Text_CompileErrors_DiagnosticLines_BuildFailed_ExitOne()
    {
        var w = new BuildWorkspace("program Hello; begin Foo := 1 end.");

        var (code, output, _) = Run(w, "Hello.pproj");

        Assert.Equal(1, code);
        Assert.Equal(@"C:\p\Hello.pas(1,22): error E2003: Undeclared identifier 'Foo'", output[0]);
        Assert.Matches(@"^Build failed \(1 errors, 0 warnings, 0 hints, \d+\.\d\d s\)$", output[^1]);
        Assert.Equal(2, output.Length);
    }

    [Fact]
    [Trait("Req", "CMP-840")]
    public void Text_Verbose_EchoesCommandLinesWithPrompt()
    {
        var w = new BuildWorkspace();

        var (_, output, _) = Run(w, "Hello.pproj", "--verbose");

        Assert.Contains(output, l => l.StartsWith("> ", StringComparison.Ordinal) && l.Contains("lld-link.exe", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-840")]
    public void Json_EveryEventIsOneObjectPerLine_EndingWithTheSummary()
    {
        var w = new BuildWorkspace();

        var (code, output, _) = Run(w, "Hello.pproj", "--diagnostics", "json", "--verbose");

        Assert.Equal(0, code);
        Assert.Equal("""{"event":"started","project":"C:\\p\\Hello.pproj","configuration":"Debug","platform":"win64","target":"build"}""", output[0]);
        Assert.Equal("""{"event":"output","source":"info","text":"Compiling Hello"}""", output[1]);
        Assert.Contains("""{"event":"output","source":"tool","text":"fake lld-link done"}""", output);
        Assert.Contains(output, l => l.StartsWith("""{"event":"commandLine","text":""", StringComparison.Ordinal));
        Assert.Matches("""^\{"event":"finished","project":"C:\\\\p\\\\Hello.pproj","success":true,"upToDate":false,"errors":0,"warnings":0,"hints":0,"ms":\d+\}$""", output[^2]);
        Assert.Matches("""^\{"summary":\{"errors":0,"warnings":0,"hints":0,"output":"C:\\\\p\\\\bin\\\\Debug\\\\Hello.exe","ms":\d+\}\}$""", output[^1]);
        Assert.All(output, l => System.Text.Json.JsonDocument.Parse(l).Dispose());
    }

    [Fact]
    [Trait("Req", "CMP-840")]
    public void Json_Diagnostics_AreThoseOfOpc()
    {
        var w = new BuildWorkspace("program Hello; begin Foo := 1 end.");

        var (code, output, _) = Run(w, "Hello.pproj", "--diagnostics", "json");

        Assert.Equal(1, code);
        Assert.Contains(output, l => l.StartsWith("""{"file":"C:\\p\\Hello.pas","line":1,"col":22,""", StringComparison.Ordinal) && l.Contains("\"code\":\"E2003\"", StringComparison.Ordinal));
        Assert.Matches("""^\{"summary":\{"errors":1,"warnings":0,"hints":0,"output":null,"ms":\d+\}\}$""", output[^1]);
    }

    [Theory]
    [Trait("Req", "CMP-842")]
    [InlineData("release", 0)]
    [InlineData("RELEASE", 0)]
    [InlineData("Profile", 1)]
    public void Config_IsMatchedCaseInsensitively_UnknownIsE9005(string config, int expected)
    {
        var w = new BuildWorkspace();

        var (code, output, _) = Run(w, "Hello.pproj", "--config", config);

        Assert.Equal(expected, code);
        if (expected == 1)
        {
            Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9005: Unknown configuration 'Profile'", output[0]);
        }
        else
        {
            Assert.True(w.Fs.FileExists(@"C:\p\bin\Release\Hello.exe"));
        }
    }

    [Fact]
    [Trait("Req", "CMP-841")]
    public void MissingToolchain_ExitsWithCode2()
    {
        var w = new BuildWorkspace();
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            w.Fs.Delete(Path.Combine(BuildWorkspace.Llvm, tool));
        }

        var (code, output, _) = Run(w, "Hello.pproj");

        Assert.Equal(2, code);
        Assert.Contains(output, l => l.Contains(" error E4001: ", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Req", "CMP-840")]
    [InlineData(new string[0], "(0,0): error E0007: No input file")]
    [InlineData(new[] { "Hello.pproj", "--frobnicate" }, "(0,0): error E0005: Invalid command-line option '--frobnicate'")]
    [InlineData(new[] { "Hello.pproj", "--target", "deploy" }, "(0,0): error E0005: Invalid command-line option '--target deploy'")]
    [InlineData(new[] { "Hello.pproj", "--diagnostics", "xml" }, "(0,0): error E0005: Invalid command-line option '--diagnostics xml'")]
    [InlineData(new[] { "Hello.pproj", "--config" }, "(0,0): error E0006: Missing argument for option '--config'")]
    [InlineData(new[] { "A.pproj", "B.pproj" }, @"(0,0): error E0008: More than one input file ('C:\p\A.pproj' and 'C:\p\B.pproj')")]
    public void ArgumentErrors_ExitOne_NothingBuilt(string[] args, string expectedSuffix)
    {
        var w = new BuildWorkspace();

        var (code, output, _) = Run(w, args);

        Assert.Equal(1, code);
        Assert.EndsWith(expectedSuffix, output[0], StringComparison.Ordinal);
        Assert.Empty(w.Tools.Calls);
    }

    [Fact]
    [Trait("Req", "CMP-840")]
    public void ArgumentErrors_Json_EndWithTheSummaryLine()
    {
        var w = new BuildWorkspace();

        var (code, output, _) = Run(w, "--diagnostics", "json");

        Assert.Equal(1, code);
        Assert.Contains("\"code\":\"E0007\"", output[0], StringComparison.Ordinal);
        Assert.Matches("""^\{"summary":\{"errors":1,"warnings":0,"hints":0,"output":null,"ms":\d+\}\}$""", output[^1]);
    }

    [Theory]
    [Trait("Req", "CMP-840")]
    [InlineData("clean")]
    [InlineData("rebuild")]
    [InlineData("build")]
    public void Target_SelectsTheEngineTarget(string target)
    {
        var w = new BuildWorkspace();
        Run(w, "Hello.pproj");

        var (code, output, _) = Run(w, "Hello.pproj", "--target", target);

        Assert.Equal(0, code);
        Assert.Equal(target == "clean" ? [] : target == "rebuild" ? ["Compiling Hello", "Compiling System", "Linking Hello.exe", "fake lld-link done"] : ["Hello: up to date"], output[..^1]);
        Assert.Equal(target != "clean", w.Fs.FileExists(BuildWorkspace.Exe));
    }

    [Fact]
    [Trait("Req", "CMP-840")]
    public void Group_BuildsEveryProject()
    {
        var w = new BuildWorkspace();
        w.Fs.Add(@"C:\p\All.pgroup", """{ "formatVersion": 1, "projects": ["Hello.pproj"], "activeProject": "Hello.pproj" }""");

        var (code, output, _) = Run(w, "All.pgroup");

        Assert.Equal(0, code);
        Assert.Equal("Compiling Hello", output[0]);
    }

    [Fact]
    [Trait("Req", "CMP-841")]
    public void InternalError_IsE0001_OnStderr_ExitTwo()
    {
        var w = new BuildWorkspace();

        var (code, output, err) = Run(w, new ThrowingFileSystem(w.Fs), "Hello.pproj");
        var (_, _, verbose) = Run(w, new ThrowingFileSystem(w.Fs), "Hello.pproj", "--verbose");

        Assert.Equal(2, code);
        Assert.Empty(output);
        Assert.StartsWith(@"C:\p\Hello.pproj(0,0): error E0001: Internal compiler error: boom", err, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", err, StringComparison.Ordinal);
        Assert.Contains(" at ", verbose, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "CMP-841")]
    public void Run_ProgramExitCodeIsPrintedNotPropagated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var llvm = Path.Combine(dir, "llvm");
        Directory.CreateDirectory(llvm);
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            File.WriteAllText(Path.Combine(llvm, tool), "");
        }

        File.WriteAllText(Path.Combine(dir, "Hello.pproj"), BuildWorkspace.ProjectJson("Hello", extra: """ "run": { "arguments": "/c exit 7", "workingDir": "" }"""));
        File.WriteAllText(Path.Combine(dir, "Hello.pas"), BuildWorkspace.HelloProgram);
        var tools = new FakeToolchain(PhysicalFileSystem.Instance)
        {
            LinkCopiesFrom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
        };
        var stdout = new StringWriter { NewLine = "\n" };
        try
        {
            var code = PbuildCommand.Run(
                ["Hello.pproj", "--target", "run"],
                new PbuildEnvironment(dir, stdout, new StringWriter(), PhysicalFileSystem.Instance)
                {
                    ProcessRunner = tools,
                    ToolchainDirectory = llvm,
                    RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(0, code);
            Assert.Contains("Program exited with code 7", stdout.ToString().Split('\n'));
            Assert.Empty(tools.Programs);                                              // the child inherited the console
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Reading any file throws a non-I/O exception: an internal error.</summary>
    private sealed class ThrowingFileSystem(IFileSystem inner) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public string ReadAllText(string path) => throw new InvalidOperationException("boom");

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => inner.EnumerateFiles(directory, searchPattern);

        public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

        public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(path);

        public long GetLength(string path) => inner.GetLength(path);

        public void WriteAllBytes(string path, byte[] data) => inner.WriteAllBytes(path, data);

        public void Delete(string path) => inner.Delete(path);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);
    }
}
