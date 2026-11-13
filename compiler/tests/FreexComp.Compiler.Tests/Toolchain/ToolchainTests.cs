using System.Diagnostics;
using System.Text.Json;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>toolchain.md §1 (CMP-600…603): locating the tools, running them, version check.</summary>
[Trait("Phase", "2")]
public sealed partial class ToolchainTests
{
    private const string Main = @"C:\p\Hello.pas";

    private static readonly Location MainLocation = Location.ForFile(Main);

    private static InMemoryFileSystem WithTools(InMemoryFileSystem fs, string dir, params string[] tools)
    {
        foreach (var tool in tools.Length == 0 ? ["clang.exe", "lld-link.exe", "llvm-dlltool.exe"] : tools)
        {
            fs.Add(Path.Combine(dir, tool), "");
        }

        return fs;
    }

    // ---------------------------------------------------------------- CMP-600

    [Fact]
    [Trait("Req", "CMP-600")]
    public void Locator_ExplicitDirectory_WinsOverEveryOtherCandidate()
    {
        var fs = WithTools(WithTools(new InMemoryFileSystem(), @"C:\explicit"), @"C:\home\third_party\llvm\bin");
        var env = new ToolchainEnvironment(@"C:\home", @"C:\opc\bin", @"C:\onpath");

        var (paths, diagnostic) = ToolchainLocator.Locate(fs, @"C:\explicit", env, MainLocation);

        Assert.Null(diagnostic);
        Assert.Equal(@"C:\explicit", paths!.Directory);
        Assert.Equal(@"C:\explicit\clang.exe", paths.Clang);
        Assert.Equal(@"C:\explicit\lld-link.exe", paths.LldLink);
        Assert.Equal(@"C:\explicit\llvm-dlltool.exe", paths.DllTool);
    }

    [Fact]
    [Trait("Req", "CMP-600")]
    public void Locator_ExplicitDirectoryWithoutTools_IsE4001_WithoutFallback()
    {
        var fs = WithTools(WithTools(new InMemoryFileSystem(), @"C:\explicit", "clang.exe"), @"C:\home\third_party\llvm\bin");
        var env = new ToolchainEnvironment(@"C:\home", null, null);

        var (paths, diagnostic) = ToolchainLocator.Locate(fs, @"C:\explicit", env, MainLocation);

        Assert.Null(paths);
        Assert.Equal("E4001", diagnostic!.Code);
        Assert.Equal(@"Toolchain not found: lld-link.exe, llvm-dlltool.exe (searched: C:\explicit)", diagnostic.Message);
        Assert.Equal(MainLocation, diagnostic.Location);
    }

    [Fact]
    [Trait("Req", "CMP-600")]
    public void Locator_FreexCompHome_ComesBeforeTheExecutableWalkAndPath()
    {
        var fs = WithTools(WithTools(WithTools(new InMemoryFileSystem(), @"C:\home\third_party\llvm\bin"), @"C:\repo\third_party\llvm\bin"), @"C:\onpath");
        var env = new ToolchainEnvironment(@"C:\home", @"C:\repo\compiler\src\opc\bin", @"C:\onpath");

        var (paths, _) = ToolchainLocator.Locate(fs, "", env, MainLocation);

        Assert.Equal(@"C:\home\third_party\llvm\bin", paths!.Directory);
    }

    [Fact]
    [Trait("Req", "CMP-600")]
    public void Locator_WalksUpFromTheExecutableDirectory()
    {
        var fs = WithTools(WithTools(new InMemoryFileSystem(), @"C:\repo\third_party\llvm\bin"), @"C:\onpath");
        var env = new ToolchainEnvironment(null, @"C:\repo\compiler\src\opc\bin\Debug", @"C:\onpath");

        var (paths, _) = ToolchainLocator.Locate(fs, "", env, MainLocation);

        Assert.Equal(@"C:\repo\third_party\llvm\bin", paths!.Directory);
    }

    [Fact]
    [Trait("Req", "CMP-600")]
    public void Locator_FallsBackToPath_SkippingEntriesWithoutAllTools()
    {
        var fs = WithTools(WithTools(new InMemoryFileSystem(), @"C:\partial", "clang.exe"), @"C:\full llvm\bin");
        var env = new ToolchainEnvironment(null, @"C:\x", @"relative;C:\partial;;""C:\full llvm\bin""");

        var (paths, _) = ToolchainLocator.Locate(fs, "", env, MainLocation);

        Assert.Equal(@"C:\full llvm\bin", paths!.Directory);
    }

    [Fact]
    [Trait("Req", "CMP-600")]
    public void Locator_NothingFound_IsE4001_ListingEverySearchedDirectory()
    {
        var env = new ToolchainEnvironment(@"C:\home", @"C:\a\b", @"C:\p1;C:\p2");

        var (paths, diagnostic) = ToolchainLocator.Locate(new InMemoryFileSystem(), "", env, MainLocation);

        Assert.Null(paths);
        Assert.Equal(
            @"Toolchain not found: clang.exe, lld-link.exe, llvm-dlltool.exe (searched: C:\home\third_party\llvm\bin; " +
            @"C:\a\b\third_party\llvm\bin; C:\a\third_party\llvm\bin; C:\third_party\llvm\bin; C:\p1; C:\p2)",
            diagnostic!.Message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-600")]
    public void Locator_RealEnvironment_FindsThePinnedToolchainOfThisRepository()
    {
        var expected = PathUtil.Normalize(Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin"));
        Assert.True(File.Exists(Path.Combine(expected, "clang.exe")), $"Pinned toolchain missing: {expected} (run scripts/bootstrap.ps1).");
        var env = new ToolchainEnvironment(null, AppContext.BaseDirectory, null);

        var (paths, diagnostic) = ToolchainLocator.Locate(PhysicalFileSystem.Instance, "", env, MainLocation);

        Assert.Null(diagnostic);
        Assert.Equal(expected, paths!.Directory, ignoreCase: true);
    }

    // ---------------------------------------------------------------- CMP-602 / 603

    [Fact]
    [Trait("Req", "CMP-602")]
    public async Task Runner_PassesArgumentsAsAListAndTheWorkingDirectory()
    {
        var fake = new FakeProcessRunner(stdOut: ["out 1"], stdErr: ["err ü"]);
        var sinkLines = new List<string>();
        var runner = new ToolRunner(fake, new ToolRunOptions(MainLocation) { ToolOutputSink = sinkLines.Add });

        var result = await runner.RunAsync(@"C:\llvm\clang.exe", ["-c", @"C:\o b\U.ll"], @"C:\o b", TestContext.Current.CancellationToken);

        var call = Assert.Single(fake.Calls);
        Assert.Equal(@"C:\llvm\clang.exe", call.FileName);
        Assert.Equal(["-c", @"C:\o b\U.ll"], call.Arguments);
        Assert.Equal(@"C:\o b", call.WorkingDirectory);
        Assert.Null(result.Failure);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["out 1", "err ü"], result.Output);
        Assert.Equal(["out 1", "err ü"], sinkLines);
    }

    [Fact]
    [Trait("Req", "CMP-602")]
    public async Task Runner_Verbose_EchoesTheCommandLineWithWindowsQuotingBeforeItRuns()
    {
        var events = new List<string>();
        var fake = new FakeProcessRunner((call, _) =>
        {
            events.Add("run");
            return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
        });
        var runner = new ToolRunner(fake, new ToolRunOptions(MainLocation) { CommandLineSink = events.Add });

        await runner.RunAsync(@"C:\Program Files\llvm\clang.exe", ["-c", @"C:\a b\x.ll", "q\"x", @"C:\t\ s\", ""], @"C:\", TestContext.Current.CancellationToken);

        Assert.Equal(
            [@"""C:\Program Files\llvm\clang.exe"" -c ""C:\a b\x.ll"" q\""x ""C:\t\ s\\"" """"", "run"],
            events);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\tb", "\"a\tb\"")]
    [InlineData(@"a\b", @"a\b")]
    [InlineData("a\"b", "a\\\"b")]
    [InlineData(@"a\""b", @"a\\\""b")]
    [InlineData(@"a b\", @"""a b\\""")]
    [Trait("Req", "CMP-602")]
    public void Runner_QuoteWindows_FollowsTheCommandLineToArgvRules(string argument, string expected)
    {
        Assert.Equal(expected, ToolRunner.QuoteWindows(argument));
    }

    [Fact]
    [Trait("Req", "CMP-603")]
    public async Task Runner_NonZeroExit_IsE4004_WithTheOutputAsRelatedMessages()
    {
        var fake = new FakeProcessRunner(1, ["line a"], ["error: bad IR"]);
        var runner = new ToolRunner(fake, new ToolRunOptions(MainLocation));

        var result = await runner.RunAsync(@"C:\llvm\clang.exe", ["-c", "x.ll"], @"C:\o", TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        var d = result.Failure!;
        Assert.Equal("E4004", d.Code);
        Assert.Equal("clang.exe failed with exit code 1", d.Message);
        Assert.Equal(MainLocation, d.Location);
        Assert.Equal(["line a", "error: bad IR"], d.Related.Select(r => r.Message));
        Assert.All(d.Related, r => Assert.Equal(MainLocation, r.Location));
    }

    [Fact]
    [Trait("Req", "CMP-602")]
    public async Task Runner_Timeout_IsE4003_AndCancelsTheProcess()
    {
        var cancelled = false;
        var fake = new FakeProcessRunner(async (_, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }

            return (0, [], []);
        });
        var runner = new ToolRunner(fake, new ToolRunOptions(MainLocation) { Timeout = TimeSpan.FromMilliseconds(20) });

        var result = await runner.RunAsync(@"C:\llvm\lld-link.exe", [], @"C:\o", TestContext.Current.CancellationToken);

        Assert.True(cancelled);
        Assert.Equal("E4003", result.Failure!.Code);
        Assert.Equal("lld-link.exe timed out after 1 seconds", result.Failure.Message);
    }

    [Fact]
    [Trait("Req", "CMP-602")]
    public void Runner_DefaultTimeout_Is120Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), new ToolRunOptions(MainLocation).Timeout);
    }

    [Fact]
    [Trait("Req", "CMP-602")]
    public async Task Runner_CallerCancellation_IsNotATimeout_ItThrows()
    {
        var fake = new FakeProcessRunner(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return (0, [], []);
        });
        var runner = new ToolRunner(fake, new ToolRunOptions(MainLocation));
        using var cts = new CancellationTokenSource();
        var run = runner.RunAsync(@"C:\llvm\clang.exe", [], @"C:\o", cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "CMP-602")]
    public async Task Runner_RealProcess_OnTimeout_KillsTheProcessTree()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var processRunner = new ProcessRunner();
        var pid = 0;
        processRunner.ProcessStarted = id => pid = id;
        var runner = new ToolRunner(processRunner, new ToolRunOptions(MainLocation) { Timeout = TimeSpan.FromMilliseconds(500) });

        try
        {
            var result = await runner.RunAsync(powershell, ["-NoProfile", "-Command", "Start-Sleep -Seconds 60"], Path.GetTempPath(), TestContext.Current.CancellationToken);

            Assert.Equal("E4003", result.Failure!.Code);
            Assert.NotEqual(0, pid);
            Assert.True(HasExited(pid), "the timed-out process is still running");
        }
        finally
        {
            if (pid != 0 && !HasExited(pid))
            {
                Process.GetProcessById(pid).Kill(entireProcessTree: true);
            }
        }
    }

    private static bool HasExited(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-602")]
    public async Task Runner_RealClang_CapturesOutputAndExitCode()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var runner = new ToolRunner(new ProcessRunner(), new ToolRunOptions(MainLocation));

        var ok = await runner.RunAsync(Clang.Path, ["--version"], Path.GetTempPath(), TestContext.Current.CancellationToken);
        var bad = await runner.RunAsync(Clang.Path, ["-c", @"C:\does\not\exist.ll"], Path.GetTempPath(), TestContext.Current.CancellationToken);

        Assert.Null(ok.Failure);
        Assert.Contains(ok.Output, l => l.Contains("clang version", StringComparison.Ordinal));
        Assert.Equal("E4004", bad.Failure!.Code);
        Assert.NotEmpty(bad.Failure.Related);
    }

    // ---------------------------------------------------------------- CMP-601

    [Theory]
    [InlineData("clang version 23.1.5", "23.1.5")]
    [InlineData("Some vendor clang version 22.0.0 (https://example.invalid abc)", "22.0.0")]
    [InlineData("nothing here", null)]
    [Trait("Req", "CMP-601")]
    public void Runner_VersionCheck_ParsesTheClangVersionLine(string line, string? expected)
    {
        Assert.Equal(expected, ToolchainVersionCheck.ParseVersion(["Target: x86_64-pc-windows-msvc", line]));
    }

    [Theory]
    [InlineData("clang version 23.0.1", null)]
    [InlineData("clang version 22.1.5", "LLVM 22.1.5 found, 23.1.5 expected")]
    [InlineData("garbage", "LLVM unknown found, 23.1.5 expected")]
    [Trait("Req", "CMP-601")]
    public async Task Runner_VersionCheck_ComparesTheMajorWithThePin(string output, string? warning)
    {
        var check = new ToolchainVersionCheck("23.1.5");

        var d = await check.CheckAsync(new FakeProcessRunner(stdOut: [output]), @"C:\llvm\clang.exe", MainLocation, TestContext.Current.CancellationToken);

        Assert.Equal(warning, d?.Message);
        if (d is not null)
        {
            Assert.Equal("W4002", d.Code);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        }
    }

    [Fact]
    [Trait("Req", "CMP-601")]
    public async Task Runner_VersionCheck_RunsClangOncePerPath()
    {
        var check = new ToolchainVersionCheck("23.1.5");
        var fake = new FakeProcessRunner(stdOut: ["clang version 21.1.0"]);

        var first = await check.CheckAsync(fake, @"C:\llvm\clang.exe", MainLocation, TestContext.Current.CancellationToken);
        var second = await check.CheckAsync(fake, @"C:\LLVM\clang.exe", Location.ForFile(@"C:\q\Other.pas"), TestContext.Current.CancellationToken);

        var call = Assert.Single(fake.Calls);
        Assert.Equal(["--version"], call.Arguments);
        Assert.Equal("W4002", first!.Code);
        Assert.Equal(@"C:\q\Other.pas", second!.Location.Path);
    }

    [Fact]
    [Trait("Req", "CMP-601")]
    public void Runner_VersionCheck_PinEqualsScriptsToolchainJson()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "toolchain.json")));

        Assert.Equal(ToolchainVersionCheck.PinnedVersion, json.RootElement.GetProperty("llvm").GetProperty("version").GetString());
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-601")]
    public async Task Runner_VersionCheck_RealClangMatchesThePin()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");

        var d = await new ToolchainVersionCheck(ToolchainVersionCheck.PinnedVersion)
            .CheckAsync(new ProcessRunner(), Clang.Path, MainLocation, TestContext.Current.CancellationToken);

        Assert.Null(d);
    }
}
