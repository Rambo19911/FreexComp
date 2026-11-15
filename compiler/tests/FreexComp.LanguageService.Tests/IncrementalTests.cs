// Diagnostics and model lookups of an in-memory snapshot are synchronous; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService.Tests;

/// <summary>CMP-130: a new snapshot rebinds only what an edit can affect (instrumented by <c>Compilation.BoundUnits</c>, C-130).</summary>
[Trait("Phase", "3")]
public sealed class IncrementalTests
{
    private const string Program = "program P;\nuses U1;\nbegin\n  Writeln(Twice(2));\nend.";

    private const string Unit1 = "unit U1;\ninterface\nfunction Twice(A: Integer): Integer;\nimplementation\nfunction Twice(A: Integer): Integer;\nbegin\n  Result := A * 2;\nend;\nend.";

    private static Compilation Baseline()
    {
        var compilation = Compilation.Create(TestFiles.Options(), [], TestFiles.Files(("P.pas", Program), ("U1.pas", Unit1)));
        Assert.Equal(["System", "P", "U1"], compilation.BoundUnits);
        return compilation;
    }

    [Fact]
    [Trait("Req", "CMP-130")]
    public void EditingABody_DoesNotRebindOtherUnits()
    {
        var baseline = Baseline();
        var edited = baseline.WithSourceText(SourceText.From(TestFiles.PathOf("U1.pas"), Unit1.Replace("A * 2", "A + A + Hidden", StringComparison.Ordinal)));

        Assert.Equal(["U1"], edited.BoundUnits);
        Assert.Contains(edited.GetDiagnostics(TestFiles.PathOf("U1.pas")), d => d.Code == "E2003");
        Assert.DoesNotContain(edited.GetDiagnostics(TestFiles.PathOf("P.pas")), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("Req", "CMP-130")]
    public void EditingTheProgramBody_RebindsOnlyTheProgram()
    {
        var edited = Baseline().WithSourceText(SourceText.From(TestFiles.PathOf("P.pas"), Program.Replace("Twice(2)", "Twice(3)", StringComparison.Ordinal)));

        Assert.Equal(["P"], edited.BoundUnits);
    }

    [Fact]
    [Trait("Req", "CMP-130")]
    public void EditingAnInterface_RebindsTheUnitsUsingIt()
    {
        var edited = Baseline().WithSourceText(SourceText.From(TestFiles.PathOf("U1.pas"),
            Unit1.Replace("interface\n", "interface\nconst Extra = 1;\n", StringComparison.Ordinal)));

        Assert.Equal(["P", "U1"], edited.BoundUnits.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-130")]
    public void NewOptions_RebindEverything()
    {
        var edited = Baseline().WithOptions(TestFiles.Options() with { Defines = ["X"] });

        Assert.Equal(["System", "P", "U1"], edited.BoundUnits);
    }

    [Fact]
    [Trait("Req", "CMP-130")]
    public void SnapshotsWithoutAnAnalysisInBetween_AccumulateTheirChanges()
    {
        var baseline = Baseline();
        var path = TestFiles.PathOf("U1.pas");
        var first = baseline.WithSourceText(SourceText.From(path, Unit1.Replace("A * 2", "A * 3", StringComparison.Ordinal)));
        var second = first.WithSourceText(SourceText.From(TestFiles.PathOf("P.pas"), Program.Replace("Twice(2)", "Twice(4)", StringComparison.Ordinal)));

        Assert.Equal(["P", "U1"], second.BoundUnits.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-130")]
    public void IncrementalResultsEqualAFreshCompilation()
    {
        var text = Unit1.Replace("A * 2", "A * Nope", StringComparison.Ordinal);
        var edited = Baseline().WithSourceText(SourceText.From(TestFiles.PathOf("U1.pas"), text));
        var fresh = Compilation.Create(TestFiles.Options(), [SourceText.From(TestFiles.PathOf("U1.pas"), text)], TestFiles.Files(("P.pas", Program), ("U1.pas", Unit1)));

        Assert.Equal(fresh.GetDiagnostics().Select(d => d.ToText()), edited.GetDiagnostics().Select(d => d.ToText()));
        var twice = edited.GetSemanticModel(TestFiles.PathOf("P.pas")).LookupSymbols(Program.IndexOf("Writeln", StringComparison.Ordinal), "Twice");
        Assert.IsAssignableFrom<RoutineSymbol>(Assert.Single(twice));
    }

    [Fact]
    [Trait("Req", "CMP-130")]
    public void ClosingAFile_ReadsTheDiskAgain()
    {
        var path = TestFiles.PathOf("U1.pas");
        var open = Baseline().WithSourceText(SourceText.From(path, Unit1.Replace("A * 2", "A * Nope", StringComparison.Ordinal)));
        Assert.Contains(open.GetDiagnostics(path), d => d.Code == "E2003");

        var closed = open.WithoutSourceText(path);

        Assert.DoesNotContain(closed.GetDiagnostics(path), d => d.Code == "E2003");
        Assert.Equal(["U1"], closed.BoundUnits);
    }
}
