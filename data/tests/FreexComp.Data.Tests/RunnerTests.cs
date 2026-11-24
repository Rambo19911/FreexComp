namespace FreexComp.Data.Tests;

/// <summary>The runner's own rules (task A-001): header markers, output comparison, missing prerequisites fail.</summary>
public sealed class RunnerTests
{
    [Fact]
    [Trait("Req", "DAT-400")]
    public void ReadHeader_CoversAndOpcOptions_FromLeadingCommentBlockOnly()
    {
        var header = DataCorpus.ReadHeader("// covers: DAT-010, DAT-011 DAT-012\n// opc: --app-type gui -D X\nprogram P;\n// covers: DAT-999\n");

        Assert.Equal(["DAT-010", "DAT-011", "DAT-012"], header.Covers);
        Assert.Equal(["--app-type", "gui", "-D", "X"], header.OpcOptions);
    }

    [Fact]
    [Trait("Req", "DAT-400")]
    public void Compare_IgnoresCrLfOnly()
    {
        Assert.Null(GoldenProgramRunner.Compare("ok a\nok b\n", "ok a\r\nok b\r\n"));
        Assert.Contains("program stdout differs", GoldenProgramRunner.Compare("ok a\n", "FAIL a\n"));
        Assert.NotNull(GoldenProgramRunner.Compare("ok a\n", "ok a \n"));
    }

    [Fact]
    [Trait("Req", "DAT-401")]
    [Trait("Req", "DAT-409")]
    public void MissingPrerequisites_NamesEveryMissingFile()
    {
        var nowhere = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "absent-" + Guid.NewGuid().ToString("N"));

        var message = GoldenProgramRunner.MissingPrerequisites(nowhere, Path.Combine(nowhere, "opc.exe"), Path.Combine(nowhere, "sqlite3.dll"));

        Assert.NotNull(message);
        Assert.Contains("clang.exe", message);
        Assert.Contains("opc.exe", message);
        Assert.Contains("sqlite3.dll", message);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Category", "Integration")]
    [Trait("Req", "DAT-401")]
    public void Prerequisites_Present()
    {
        Assert.Null(GoldenProgramRunner.MissingPrerequisites(DataCorpus.ToolchainDirectory, DataCorpus.OpcPath, DataCorpus.SqliteLibrary));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DAT-400")]
    public void EveryProgram_HasExpectedOutput()
    {
        var programs = DataCorpus.Discover();

        Assert.NotEmpty(programs);
        Assert.All(programs, p => Assert.True(File.Exists(p.ExpectedPath), p.Name + ".out does not exist"));
    }
}
