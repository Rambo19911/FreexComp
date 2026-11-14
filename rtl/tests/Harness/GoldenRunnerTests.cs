namespace FreexComp.Rtl.Tests.Harness;

/// <summary>Checks of the runner's own rules (RTL-900…903) that do not need the toolchain.</summary>
[Trait("Phase", "2")]
public sealed class GoldenRunnerTests
{
    [Fact]
    [Trait("Req", "RTL-901")]
    [Trait("Req", "RTL-903")]
    [Trait("Req", "RTL-908")]
    public void ReadHeader_ParsesEveryMarkerOfTheLeadingCommentBlock()
    {
        const string text = "\uFEFF// phase: 4\r\n// covers: RTL-073, RTL-075\n// opc: -D FOO  --range-checks on\n"
            + "// opc-run: tempdir\n// opc-run: variants 1 2 3\n// opc-run: rawargs a \"b c\"  d\nprogram P;\n// opc: -D LATE\n";

        var header = RtlCorpus.ReadHeader(text);

        Assert.Equal(4, header.Phase);
        Assert.Equal(["RTL-073", "RTL-075"], header.Covers);
        Assert.Equal(["-D", "FOO", "--range-checks", "on"], header.OpcOptions);
        Assert.True(header.TempDir);
        Assert.Equal(["1", "2", "3"], header.Variants);
        Assert.Equal("a \"b c\"  d", header.RawArgs);
    }

    [Fact]
    [Trait("Req", "RTL-908")]
    public void ReadHeader_WithoutMarkers_IsPhase2WithNoOptions()
    {
        var header = RtlCorpus.ReadHeader("program P;\nbegin\nend.\n");

        Assert.Equal(2, header.Phase);
        Assert.Empty(header.Covers);
        Assert.Empty(header.OpcOptions);
        Assert.False(header.TempDir);
        Assert.Empty(header.Variants);
        Assert.Null(header.RawArgs);
    }

    [Fact]
    [Trait("Req", "RTL-902")]
    public void StderrPatterns_EachNonEmptyLineMustMatchSomeStderrLine()
    {
        const string stderr = "first line\r\nRuntime error 201 at $00007FF600001234\r\n";

        Assert.Null(GoldenRunner.CheckStderr("^Runtime error 201 at \\$[0-9A-F]{16}$\n\nfirst\n", stderr));
        var failure = GoldenRunner.CheckStderr("^first line$\n^Runtime error 202", stderr);
        Assert.NotNull(failure);
        Assert.Contains("^Runtime error 202", failure, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RTL-903")]
    public void CommandLine_QuotesArgumentsAndAppendsRawArguments()
    {
        Assert.Equal("", GoldenRunner.CommandLine([], null));
        Assert.Equal("plain \"with space\" \"\" \"q\\\"uote\" a\\ \"tr ail\\\\\"", GoldenRunner.CommandLine(["plain", "with space", "", "q\"uote", "a\\", "tr ail\\"], null));
        Assert.Equal("C:\\t\\x 1 a \"b c\"", GoldenRunner.CommandLine(["C:\\t\\x", "1"], "a \"b c\""));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "RTL-900")]
    public void Discover_FindsGroupProgramsOnly_WithGroupSlashNameIds()
    {
        var cases = RtlCorpus.Discover(RtlSetup.TestsDirectory);

        Assert.Contains(cases, c => c.Id == "infra/Harness_SelfTest");
        Assert.All(cases, c => Assert.Matches("^[A-Za-z0-9_]+/[A-Za-z0-9_]+$", c.Id));
        Assert.DoesNotContain(cases, c => c.Id.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) || c.Id.StartsWith("obj/", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>End-to-end checks of the runner on synthetic programs: <c>.exit</c>, <c>.err.txt</c>, variants, mismatch reporting.</summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class GoldenRunnerEndToEndTests
{
    private const string Program = "program Synth;\nbegin\n  Writeln('out');\n  Writeln(ErrOutput, 'boom 17');\n  Halt(3);\nend.\n";

    [Fact]
    [Trait("Req", "RTL-901")]
    [Trait("Req", "RTL-902")]
    public async Task Run_ComparesStdoutExitCodeAndStderrPatterns()
    {
        var dir = NewDir();
        var @case = Write(dir, Program, (".out", "out\n"), (".exit", "3\n"), (".err.txt", "^boom \\d+$\n"));

        Assert.Null(await GoldenRunner.RunAsync(@case, RtlSetup.Default, TestContext.Current.CancellationToken));

        File.WriteAllText(Path.Combine(dir, "Synth.err.txt"), "^bang$\n");
        var failure = await GoldenRunner.RunAsync(@case, RtlSetup.Default, TestContext.Current.CancellationToken);
        Assert.NotNull(failure);
        Assert.Contains("^bang$", failure, StringComparison.Ordinal);
        GoldenRunner.TryDelete(dir);
    }

    [Fact]
    [Trait("Req", "RTL-901")]
    public async Task Run_Variants_UseTheirOwnExpectationFiles()
    {
        var dir = NewDir();
        var @case = Write(dir, "// opc-run: variants a b\n" + Program, (".a.out", "out\n"), (".a.exit", "3"), (".b.out", "other\n"), (".b.exit", "3"));

        var failure = await GoldenRunner.RunAsync(@case, RtlSetup.Default, TestContext.Current.CancellationToken);

        Assert.NotNull(failure);
        Assert.Contains("variant b", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("variant a", failure, StringComparison.Ordinal);
        GoldenRunner.TryDelete(dir);
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static RtlCase Write(string dir, string source, params (string Extension, string Text)[] files)
    {
        var path = Path.Combine(dir, "Synth.pas");
        File.WriteAllText(path, source);
        foreach (var (extension, text) in files)
        {
            File.WriteAllText(Path.Combine(dir, "Synth" + extension), text);
        }

        return new RtlCase("synth/Synth", path);
    }
}
