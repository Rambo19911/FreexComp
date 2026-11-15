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

    /// <summary>R-209: the CPU limit wins over load; the next check is the earliest moment either limit can be reached.</summary>
    [Fact]
    [Trait("Req", "RTL-901")]
    public void Decide_StopsOnCpuOrWallLimitAndReArmsForTheEarliest()
    {
        var limits = new RunLimits(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(120));
        static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

        Assert.Equal(((string?)null, S(19)), GoldenRunner.Decide(S(1), S(30), limits));        // 30 s wall at 1 s CPU: keeps running
        Assert.Equal(((string?)null, S(10)), GoldenRunner.Decide(S(1), S(110), limits));       // the wall cap is nearer
        Assert.Equal("CPU limit 20.0 s reached", GoldenRunner.Decide(S(20), S(25), limits).StopReason);
        Assert.Equal("wall-clock limit 120.0 s reached", GoldenRunner.Decide(S(2), S(120), limits).StopReason);
        Assert.Equal(TimeSpan.FromMilliseconds(50), GoldenRunner.Decide(S(19.99), S(30), limits).NextCheck);
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

    /// <summary>RTL-909: a missing toolchain or opc fails the run (it is never skipped), and the message names what is missing.</summary>
    [Fact]
    [Trait("Req", "RTL-909")]
    public async Task Run_MissingToolchain_FailsAndNamesWhatIsMissing()
    {
        var dir = NewDir();
        var @case = Write(dir, Program, (".out", "out\n"));
        var missingLlvm = Path.Combine(dir, "no-llvm");
        var missingOpc = Path.Combine(dir, "no-opc.exe");

        var noToolchain = await GoldenRunner.RunAsync(@case, RtlSetup.Default with { ToolchainDirectory = missingLlvm },
            TestContext.Current.CancellationToken);
        var noOpc = await GoldenRunner.RunAsync(@case, RtlSetup.Default with { OpcPath = missingOpc }, TestContext.Current.CancellationToken);

        Assert.NotNull(noToolchain);
        Assert.Contains("toolchain missing", noToolchain, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(missingLlvm, "clang.exe"), noToolchain, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(missingLlvm, "lld-link.exe"), noToolchain, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(missingLlvm, "llvm-dlltool.exe"), noToolchain, StringComparison.Ordinal);
        Assert.NotNull(noOpc);
        Assert.Contains(missingOpc, noOpc, StringComparison.Ordinal);
        GoldenRunner.TryDelete(dir);
    }

    /// <summary>
    /// R-205: the directories of <see cref="RtlSetup.ExtraUnitDirectories"/> are passed to opc as further <c>-U</c>
    /// options, so a caller (the UI framework harness) puts its sources on the unit path without a <c>// opc:</c> marker.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-901")]
    public async Task Run_ExtraUnitDirectories_AreOnTheUnitPath()
    {
        var dir = NewDir();
        var units = Path.Combine(dir, "units");
        Directory.CreateDirectory(units);
        File.WriteAllText(Path.Combine(units, "SynthExtra.pas"),
            "unit SynthExtra;\ninterface\nfunction Answer: Integer;\nimplementation\nfunction Answer: Integer;\nbegin\n  Result := 42;\nend;\nend.\n");
        var @case = Write(dir, "program Synth;\nuses SynthExtra;\nbegin\n  Writeln(Answer);\nend.\n", (".out", "42\n"));

        var without = await GoldenRunner.RunAsync(@case, RtlSetup.Default, TestContext.Current.CancellationToken);
        var with = await GoldenRunner.RunAsync(@case, RtlSetup.Default with { ExtraUnitDirectories = [units] },
            TestContext.Current.CancellationToken);

        Assert.NotNull(without);
        Assert.Contains("SynthExtra", without, StringComparison.Ordinal);
        Assert.Null(with);
        Assert.Empty(RtlSetup.Default.ExtraUnitDirectories);
        GoldenRunner.TryDelete(dir);
    }

    /// <summary>Sleeps or spins (busy, using CPU) for the milliseconds of its second argument, then writes <c>done</c>.</summary>
    private const string Waiter = "program Synth;\nuses SysUtils;\nvar\n  Ms: Integer;\n  Start: UInt64;\nbegin\n"
        + "  Ms := StrToInt(ParamStr(2));\n  if ParamStr(1) = 'sleep' then\n    Sleep(Ms)\n  else\n  begin\n"
        + "    Start := GetTickCount64;\n    while GetTickCount64 - Start < UInt64(Ms) do\n      ;\n  end;\n  Writeln('done');\nend.\n";

    /// <summary>
    /// R-209 (TESTING.md rule 4, C-249): a program is stopped by its own CPU time, not by the wall clock, so one that
    /// waits 25 s with almost no CPU (as a loaded full-solution run makes a program wait) passes under the default limits.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-901")]
    public async Task SlowWallTimeDoesNotStopAProgram()
    {
        var dir = NewDir();
        var @case = Write(dir, "// opc-run: rawargs sleep 25000\n" + Waiter, (".out", "done\n"));

        var failure = await GoldenRunner.RunAsync(@case, RtlSetup.Default, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
        GoldenRunner.TryDelete(dir);
    }

    /// <summary>R-209: a busy program is stopped by the CPU limit, and the failure reports wall time, CPU time and the process state.</summary>
    [Fact]
    [Trait("Req", "RTL-901")]
    public async Task SpinningProgramIsStoppedByTheCpuLimit()
    {
        var dir = NewDir();
        var @case = Write(dir, "// opc-run: rawargs spin 100000\n" + Waiter, (".out", "done\n"));

        var failure = await GoldenRunner.RunAsync(@case, RtlSetup.Default, TestContext.Current.CancellationToken,
            limits: new RunLimits(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(120)));

        Assert.NotNull(failure);
        Assert.Matches(@"program CPU limit 2\.0 s reached: Synth\.exe stopped after \d+\.\d s wall, \d+\.\d s CPU, still running, threads \[.+\]", failure);
        GoldenRunner.TryDelete(dir);
    }

    /// <summary>R-209: a program blocked without using CPU is stopped by the wall-clock cap.</summary>
    [Fact]
    [Trait("Req", "RTL-901")]
    public async Task BlockedProgramIsStoppedByTheWallClockCap()
    {
        var dir = NewDir();
        var @case = Write(dir, "// opc-run: rawargs sleep 100000\n" + Waiter, (".out", "done\n"));

        var failure = await GoldenRunner.RunAsync(@case, RtlSetup.Default, TestContext.Current.CancellationToken,
            limits: new RunLimits(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3)));

        Assert.NotNull(failure);
        Assert.Matches(@"program wall-clock limit 3\.0 s reached: Synth\.exe stopped after \d+\.\d s wall, \d+\.\d s CPU, still running, threads \[.*wait:.+\]", failure);
        GoldenRunner.TryDelete(dir);
    }

    /// <summary>
    /// Longer test programs from before RTL-905 was checked, each with the line count it may not exceed. Empty since
    /// R-201 split the last of them; a program is never added here, it is split instead.
    /// </summary>
    public static readonly Dictionary<string, int> OversizedPrograms = new(StringComparer.Ordinal);

    /// <summary>"At most ~300 lines": the bound the size check applies (300 plus a tolerance of 20 lines for the "~").</summary>
    public const int MaxProgramLines = 320;

    /// <summary>
    /// RTL-905: every test program starts with a header comment that has a <c>// covers:</c> line, and is at most ~300
    /// lines (<see cref="MaxProgramLines"/>; the programs of <see cref="OversizedPrograms"/> may not grow).
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-905")]
    public void Programs_HaveACoversHeaderAndStaySmall()
    {
        var problems = new List<string>();
        foreach (var @case in RtlCorpus.Discover(RtlSetup.TestsDirectory))
        {
            var text = File.ReadAllText(@case.SourcePath);
            if (!text.TrimStart('﻿').Split('\n').TakeWhile(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    .Any(l => l.TrimStart()[2..].TrimStart().StartsWith("covers:", StringComparison.Ordinal)))
            {
                problems.Add($"{@case.Id}: no '// covers:' line in the leading comment block");
            }

            var lines = text.TrimEnd('\n').Split('\n').Length;
            var limit = OversizedPrograms.TryGetValue(@case.Id, out var old) ? old : MaxProgramLines;
            if (lines > limit)
            {
                problems.Add($"{@case.Id}: {lines} lines (at most {limit})");
            }
        }

        foreach (var id in OversizedPrograms.Keys.Where(id => !RtlCorpus.Discover(RtlSetup.TestsDirectory).Any(c => c.Id == id)))
        {
            problems.Add($"{id}: listed in OversizedPrograms but not a test program");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
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
