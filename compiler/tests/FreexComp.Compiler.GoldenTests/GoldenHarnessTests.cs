using System.Text;

namespace FreexComp.Compiler.GoldenTests;

/// <summary>
/// Self-tests of the golden harness (CMP-900…905, task C-046): discovery, header comments, the <c>-O2</c> dimension,
/// text normalisation and (<c>Category=Toolchain</c>) that every expectation file is really compared, so a wrong
/// expectation fails.
/// </summary>
[Trait("Phase", "2")]
public sealed class GoldenHarnessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Put(string relativePath, string text)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- clean-up (C-047) ----

    [Fact]
    [Trait("Req", "CMP-901")]
    public void CleanUp_LockedFileInThePassedCaseFolder_DoesNotFailTheCase()
    {
        var locked = Put("case/Prog.exe", "image");
        var folder = Path.GetDirectoryName(locked)!;

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            GoldenRunner.TryDelete(folder);                                         // the OS still holds the image
        }

        GoldenRunner.TryDelete(folder);
        Assert.False(Directory.Exists(folder));
    }

    // ---- discovery (CMP-900, 901) ----

    [Fact]
    [Trait("Req", "CMP-900")]
    [Trait("Req", "CMP-901")]
    public void Discover_OneCasePerTestFile_IdIsMilestoneSlashName()
    {
        Put("m2.1/B.pas", "begin end.");
        Put("m2.1/B.err", "");
        Put("m2.1/A.pas", "begin end.");
        Put("m2.1/A.out", "");
        Put("m2.1/part.inc", "");
        Put("m2.2/C.pas", "begin end.");
        Put("m2.2/C.out", "");

        var cases = GoldenCorpus.Discover(_root);

        Assert.Equal(["m2.1/A", "m2.1/B", "m2.2/C"], cases.Select(c => c.Id));
        Assert.Equal(Path.Combine(_root, "m2.1", "A.pas"), cases[0].SourcePath);
        Assert.Equal("A", cases[0].Name);
    }

    [Fact]
    [Trait("Req", "CMP-900")]
    public void Discover_MultiUnitFolder_OnlyTheMainProgramIsACase()
    {
        Put("m2.3/UnitBasic/UnitBasic.pas", "uses Helper; begin end.");
        Put("m2.3/UnitBasic/UnitBasic.out", "");
        Put("m2.3/UnitBasic/Helper.pas", "unit Helper; interface implementation end.");
        Put("m2.3/UnitBasic/sub/Other.pas", "unit Other; interface implementation end.");

        var cases = GoldenCorpus.Discover(_root);

        var single = Assert.Single(cases);
        Assert.Equal("m2.3/UnitBasic", single.Id);
        Assert.Equal(Path.Combine(_root, "m2.3", "UnitBasic", "UnitBasic.pas"), single.SourcePath);
    }

    [Fact]
    [Trait("Req", "CMP-900")]
    public void Discover_TopLevelSourceWithoutExpectation_IsACaseThatFails()
    {
        Put("m2.1/Forgotten.pas", "begin end.");

        var forgotten = Assert.Single(GoldenCorpus.Discover(_root));
        var failure = GoldenRunner.CheckExpectations(forgotten);

        Assert.Equal("m2.1/Forgotten", forgotten.Id);
        Assert.Contains("neither Forgotten.out nor Forgotten.err", failure);
    }

    [Fact]
    [Trait("Req", "CMP-900")]
    public void CheckExpectations_OutAndErrTogether_Fails()
    {
        Put("m2.1/Both.pas", "begin end.");
        Put("m2.1/Both.out", "");
        Put("m2.1/Both.err", "");

        var failure = GoldenRunner.CheckExpectations(Assert.Single(GoldenCorpus.Discover(_root)));

        Assert.Contains("both Both.out and Both.err", failure);
    }

    [Fact]
    [Trait("Req", "CMP-902")]
    public void CheckExpectations_DiagWithoutOut_Fails()
    {
        Put("m2.1/D.pas", "begin end.");
        Put("m2.1/D.err", "");
        Put("m2.1/D.diag", "");

        var failure = GoldenRunner.CheckExpectations(Assert.Single(GoldenCorpus.Discover(_root)));

        Assert.Contains("D.diag needs D.out", failure);
    }

    [Fact]
    [Trait("Req", "CMP-900")]
    [Trait("Req", "CMP-901")]
    public void Corpus_PicksUpHelloWorldAndEmptyProgram()
    {
        var ids = GoldenCorpus.Discover(GoldenSetup.CorpusDirectory).Select(c => c.Id).ToList();

        Assert.Contains("m2.1/HelloWorld", ids);
        Assert.Contains("m2.1/EmptyProgram", ids);
    }

    // ---- header comments (CMP-901, 904, 905) ----

    [Fact]
    [Trait("Req", "CMP-901")]
    [Trait("Req", "CMP-904")]
    public void ReadHeader_OpcOptionsAndSkipO2_FromTheLeadingCommentLines()
    {
        var header = GoldenCorpus.ReadHeader("\uFEFF// covers: LANG-860\r\n// opc: -D FOO  -U sub\r\n// opc-skip-O2: timing differs\r\nprogram P;\r\n// opc: -D LATE\r\nbegin end.\r\n");

        Assert.Equal(["-D", "FOO", "-U", "sub"], header.Options);
        Assert.Equal("timing differs", header.SkipO2Reason);
    }

    [Fact]
    [Trait("Req", "CMP-904")]
    public void ReadHeader_NoDirectives_NoOptionsAndO2Runs()
    {
        var header = GoldenCorpus.ReadHeader("// covers: LANG-500\nprogram P;\nbegin end.\n");

        Assert.Empty(header.Options);
        Assert.Null(header.SkipO2Reason);
    }

    [Fact]
    [Trait("Req", "CMP-904")]
    public void Rows_EveryCaseAtO0_AndAtO2UnlessSkipped_O2RowsCarryTheOptTrait()
    {
        Put("m2.1/A.pas", "// covers: LANG-500\nbegin end.\n");
        Put("m2.1/A.out", "");
        Put("m2.1/B.pas", "// covers: LANG-500\n// opc-skip-O2: reason\nbegin end.\n");
        Put("m2.1/B.out", "");

        var rows = GoldenCorpus.Rows(GoldenCorpus.Discover(_root)).ToList();

        Assert.Equal(["m2.1/A", "m2.1/A -O2", "m2.1/B"], rows.Select(r => r.TestDisplayName));
        Assert.Equal(["m2.1/A O0", "m2.1/A O2", "m2.1/B O0"], rows.Select(r => string.Join(" ", ((ITheoryDataRow)r).GetData())));
        Assert.Equal(["O2"], rows[1].Traits["Opt"]);
        Assert.DoesNotContain("Opt", rows[0].Traits.Keys);
    }

    // ---- text rules (CMP-901, compiler-cli.md §5) ----

    [Fact]
    [Trait("Req", "CMP-901")]
    public void Normalize_CrlfBecomesLf_TrailingWhitespaceIsKept()
    {
        Assert.Equal("a  \nb\t\n\nc", GoldenText.Normalize("a  \r\nb\t\r\n\r\nc"));
    }

    [Fact]
    [Trait("Req", "CMP-901")]
    public void ReducePaths_LeadingDiagnosticPathBecomesTheFileName()
    {
        var text = "C:\\x\\m2.1\\Err.pas(3,5): error E2003: Undeclared identifier 'Foo'\n" +
                   "C:\\x\\m2.1\\sub dir\\part.inc(1,1): warning W1041: Variable 'C:\\y' might not have been initialized\n" +
                   "not a diagnostic (1,1): x\n";

        Assert.Equal(
            "Err.pas(3,5): error E2003: Undeclared identifier 'Foo'\n" +
            "part.inc(1,1): warning W1041: Variable 'C:\\y' might not have been initialized\n" +
            "not a diagnostic (1,1): x\n",
            GoldenText.ReducePaths(text));
    }

    [Fact]
    [Trait("Req", "CMP-903")]
    public void SelectRtl_EnvironmentWins_ThenRtlSrc_ThenTheMiniRtl()
    {
        Assert.Equal(Path.Combine(_root, "custom"), GoldenSetup.SelectRtl(_root, Path.Combine(_root, "custom")));
        Assert.Equal(Path.Combine(_root, "compiler", "tests", "minirtl"), GoldenSetup.SelectRtl(_root, null));

        Put("rtl/src/System.pas", "unit System; interface implementation end.");
        Assert.Equal(Path.Combine(_root, "rtl", "src"), GoldenSetup.SelectRtl(_root, ""));
    }

    // ---- real runs: every expectation is compared (Category=Toolchain) ----

    private async Task<string?> Run(string name, string source, (string Ext, string Text)[] files, bool o2 = false)
    {
        var path = Put("m2.1/" + name + ".pas", source);
        foreach (var (ext, text) in files)
        {
            Put("m2.1/" + name + ext, text);
        }

        var @case = Assert.Single(GoldenCorpus.Discover(_root), c => c.SourcePath == path);
        return await GoldenRunner.RunAsync(@case, o2, GoldenSetup.Default, Ct);
    }

    private const string Hello = "program Hello;\nbegin\n  Writeln('Hi');\nend.\n";

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-901")]
    public async Task Run_OutTest_MatchingOutputAndCrlfExpectation_Passes()
    {
        Assert.Null(await Run("Hello", Hello, [(".out", "Hi\r\n")]));
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-904")]
    public async Task Run_OutTest_AtO2_Passes()
    {
        Assert.Null(await Run("Hello", Hello, [(".out", "Hi\n")], o2: true));
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-901")]
    public async Task Run_OutTest_WrongOutput_Fails()
    {
        var failure = await Run("Hello", Hello, [(".out", "Hello\n")]);

        Assert.NotNull(failure);
        Assert.Contains("stdout", failure);
        Assert.Contains("Hi", failure);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-901")]
    public async Task Run_OutTest_ExitCodeIsComparedWithTheExitFile()
    {
        var failure = await Run("Hello", Hello, [(".out", "Hi\n"), (".exit", "3\n")]);

        Assert.NotNull(failure);
        Assert.Contains("exit code 0, expected 3", failure);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-901")]
    public async Task Run_OutTest_ThatDoesNotCompile_Fails()
    {
        var failure = await Run("Bad", "program Bad;\nbegin\n  Foo;\nend.\n", [(".out", "")]);

        Assert.NotNull(failure);
        Assert.Contains("opc exit code 1, expected 0", failure);
        Assert.Contains("Bad.pas(3,3): error E2003", failure);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-901")]
    public async Task Run_ErrTest_ComparesStdoutWithPathsReduced()
    {
        const string source = "program Bad;\nbegin\n  Foo;\nend.\n";
        Assert.Null(await Run("Bad", source, [(".err", "Bad.pas(3,3): error E2003: Undeclared identifier 'Foo'\r\n")]));
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-901")]
    public async Task Run_ErrTest_WrongDiagnostics_Fails()
    {
        var failure = await Run("Bad", "program Bad;\nbegin\n  Foo;\nend.\n", [(".err", "Bad.pas(3,3): error E2003: Undeclared identifier 'Bar'\n")]);

        Assert.NotNull(failure);
        Assert.Contains("diagnostics", failure);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-901")]
    public async Task Run_ErrTest_ThatCompiles_Fails()
    {
        var failure = await Run("Hello", Hello, [(".err", "")]);

        Assert.NotNull(failure);
        Assert.Contains("opc exit code 0, expected 1", failure);
    }

    private const string Unused = "program Unused;\nprocedure Q;\nvar X: Integer;\nbegin\nend;\nbegin\n  Q;\n  Writeln('ok');\nend.\n";

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-902")]
    public async Task Run_DiagTest_ComparesDiagnosticsAndRunsTheProgram()
    {
        Assert.Null(await Run("Unused", Unused, [(".out", "ok\n"), (".diag", "Unused.pas(3,5): hint H2001: Variable 'X' is declared but never used\n")]));
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-902")]
    public async Task Run_DiagTest_WrongDiagnostics_Fails()
    {
        var failure = await Run("Unused", Unused, [(".out", "ok\n"), (".diag", "")]);

        Assert.NotNull(failure);
        Assert.Contains("H2001", failure);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-901")]
    public async Task Run_OpcOptionsFromTheHeader_ArePassedToOpc()
    {
        const string source = "// covers: LANG-860\n// opc: -D FOO\nprogram Opt;\nbegin\n{$IFDEF FOO}\n  Writeln('foo');\n{$ENDIF}\nend.\n";
        Assert.Null(await Run("Opt", source, [(".out", "foo\n")]));
    }
}
