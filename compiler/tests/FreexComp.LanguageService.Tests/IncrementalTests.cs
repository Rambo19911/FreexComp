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
    public void SymbolKeyStableAcrossBodyEdit()
    {
        var baseline = Baseline();
        _ = baseline.GetSemanticModel(TestFiles.PathOf("P.pas"));
        var edited = baseline.WithSourceText(SourceText.From(TestFiles.PathOf("U1.pas"), Unit1.Replace("A * 2", "A * 2 + 0", StringComparison.Ordinal)));
        Assert.Equal(["U1"], edited.BoundUnits);

        var program = edited.GetSemanticModel(TestFiles.PathOf("P.pas"));
        var call = program.SyntaxTree.FindToken(Program.IndexOf("Twice", StringComparison.Ordinal)).Parent!;
        var referenced = Assert.IsAssignableFrom<RoutineSymbol>(program.GetSymbolInfo(call).Symbol);
        var unit = edited.GetSemanticModel(TestFiles.PathOf("U1.pas"));
        var declared = Assert.IsAssignableFrom<RoutineSymbol>(unit.GetDeclaredSymbol(unit.SyntaxTree.FindToken(Unit1.IndexOf("Twice", StringComparison.Ordinal))));

        Assert.NotSame(referenced, declared);                                 // P was taken over: it still holds the earlier U1's symbol
        Assert.Equal(SymbolKey.Create(declared), SymbolKey.Create(referenced));
        Assert.True(SymbolKey.AreSame(declared, referenced));
        Assert.Equal(SymbolKey.Create(declared.Parameters[0]), SymbolKey.Create(referenced.Parameters[0]));
        Assert.NotEqual(SymbolKey.Create(declared), SymbolKey.Create(declared.Parameters[0]));
        Assert.NotEqual(SymbolKey.Create(declared), SymbolKey.Create(unit.Unit));
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

    /// <summary>
    /// C-351 (CMP-1382): a unit whose implementation gains or loses a <c>{$FORM}</c> keeps its interface text, but the units using
    /// it are not taken over: the root keyword of a descendant's form is checked again (E3105, E3106) as in a fresh compilation.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1382")]
    public void AncestorGainsOrLosesForm_DescendantIsCheckedAgain()
    {
        const string ancestor = "unit UAnc;\ninterface\ntype\n  TAncForm = class\n  end;\nimplementation\n{$FORM UAnc.form}\nend.";
        const string descendant = "unit UDesc;\ninterface\nuses UAnc;\ntype\n  TDescForm = class(TAncForm)\n  end;\nimplementation\n{$FORM UDesc.form}\nend.";
        var withoutForm = ancestor.Replace("{$FORM UAnc.form}\n", "", StringComparison.Ordinal);
        var path = TestFiles.PathOf("UAnc.pas");
        InMemoryFileSystem Files(string descendantForm) => TestFiles.Files(("P.pas", "program P;\nuses UDesc;\nbegin\nend."), ("UAnc.pas", ancestor),
            ("UAnc.form", "object AncForm: TAncForm\nend\n"), ("UDesc.pas", descendant), ("UDesc.form", descendantForm));

        foreach (var (root, edited, code) in new[] { ("inherited", withoutForm, "E3105"), ("object", ancestor, "E3106") })
        {
            var files = Files(root + " DescForm: TDescForm\nend\n");
            var start = root == "object" ? withoutForm : ancestor;
            var baseline = Compilation.Create(TestFiles.Options(), [SourceText.From(path, start)], files);
            Assert.DoesNotContain(baseline.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

            var changed = baseline.WithSourceText(SourceText.From(path, edited));
            var fresh = Compilation.Create(TestFiles.Options(), [SourceText.From(path, edited)], files);

            Assert.Equal([code], fresh.GetDiagnostics().Select(d => d.Code));
            Assert.Equal(fresh.GetDiagnostics().Select(d => d.ToText()), changed.GetDiagnostics().Select(d => d.ToText()));
        }
    }
}
