using System.Globalization;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Every case of <c>diagnostics.cases</c> gives exactly its diagnostic (testing.md §3).</summary>
[Trait("Phase", "2")]
public sealed class BinderDiagnosticTests
{
    private static readonly string CasesPath =
        System.IO.Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Compiler.Tests", "Binding", "diagnostics.cases");

    /// <summary>(code, line, column, snippet, case number) in file order.</summary>
    public static TheoryData<string, int, int, string, int> Cases()
    {
        var data = new TheoryData<string, int, int, string, int>();
        foreach (var c in ReadCases())
        {
            data.Add(c.Code, c.Line, c.Column, c.Snippet, c.Number);
        }

        return data;
    }

    private static List<(string Code, int Line, int Column, string Snippet, int Number)> ReadCases()
    {
        var cases = new List<(string, int, int, string, int)>();
        var lines = File.ReadAllLines(CasesPath);
        var number = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("=== ", StringComparison.Ordinal))
            {
                continue;
            }

            var header = lines[i][4..].Split(' ', ',');
            var snippet = new List<string>();
            for (var j = i + 1; j < lines.Length && !lines[j].StartsWith("=== ", StringComparison.Ordinal); j++)
            {
                snippet.Add(lines[j]);
            }

            number++;
            cases.Add((header[0], int.Parse(header[1], CultureInfo.InvariantCulture), int.Parse(header[2], CultureInfo.InvariantCulture),
                string.Join("\n", snippet), number));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case(string code, int line, int column, string snippet, int number)
    {
        var diagnostics = Diagnose(snippet);
        Assert.True(diagnostics.Length == 1, $"case {number} ({code}): {string.Join("; ", Format(diagnostics))}");
        var diagnostic = diagnostics[0];

        Assert.Equal(
            (number, code, line, column),
            (number, diagnostic.Code, diagnostic.Location.LineSpan.Start.Line + 1, diagnostic.Location.LineSpan.Start.Column + 1));
    }

    /// <summary>
    /// Every diagnostic a source snippet gets from the front end, as <c>opc --check</c> collects them for one unit:
    /// the tree's (no syntax errors allowed: warnings/hints of the preprocessor and parser), the binder's, then flow
    /// analysis (C-031).
    /// </summary>
    private static Diagnostic[] Diagnose(string snippet)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, snippet));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var result = Binder.BindCompilationUnit(tree, new BuiltInTypes());
        return [.. tree.Diagnostics, .. result.Diagnostics, .. FlowAnalyzer.Analyze(result)];
    }

    // ---- coverage of the catalogue (testing.md §3, CMP-905) ----

    private static readonly string UncoveredPath =
        System.IO.Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Compiler.Tests", "Binding", "diagnostics.uncovered");

    private static readonly string[] Statuses = ["reserved", "retired", "unemitted", "M2.2", "M2.3", "M2.4", "M2.5", "M2.6", "Phase3", "Phase4", "Phase8", "elsewhere"];

    /// <summary>The codes this suite answers for: E2xxx, W1xxx, W4xxx and Hxxxx (E0/E1/E4 have their own suites, W9 is the build engine's).</summary>
    private static IEnumerable<string> CodesInScope() =>
        DiagnosticCatalog.All.Select(d => d.Code).Where(c => c.StartsWith("E2", StringComparison.Ordinal)
            || c.StartsWith("W1", StringComparison.Ordinal) || c.StartsWith("W4", StringComparison.Ordinal) || c[0] == 'H');

    /// <summary>(code, status, reason) of every line of <c>diagnostics.uncovered</c>.</summary>
    private static List<(string Code, string Status, string Reason)> ReadUncovered() =>
        [.. File.ReadAllLines(UncoveredPath)
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries))
            .Select(p => (p[0], p.Length > 1 ? p[1] : "", p.Length > 2 ? p[2].Trim() : ""))];

    [Fact]
    [Trait("Req", "CMP-905")]
    public void EveryDiagnosticIsCovered()
    {
        var covered = ReadCases().Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var uncovered = ReadUncovered();
        var listed = uncovered.Select(u => u.Code).ToList();
        var scope = CodesInScope().ToList();

        var problems = new List<string>();
        problems.AddRange(scope.Where(c => !covered.Contains(c) && !listed.Contains(c)).Select(c => $"{c}: no case and not listed in diagnostics.uncovered"));
        problems.AddRange(listed.Where(covered.Contains).Select(c => $"{c}: has a case, remove it from diagnostics.uncovered"));
        problems.AddRange(listed.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => $"{g.Key}: listed {g.Count()} times"));
        problems.AddRange(listed.Concat(covered).Where(c => !scope.Contains(c)).Distinct().Select(c => $"{c}: not a catalogue code in scope"));
        problems.AddRange(uncovered.Where(u => !Statuses.Contains(u.Status)).Select(u => $"{u.Code}: unknown status '{u.Status}'"));
        problems.AddRange(uncovered.Where(u => u.Status != "elsewhere" && u.Reason.Length == 0).Select(u => $"{u.Code}: reason missing"));
        foreach (var (code, _, reason) in uncovered.Where(u => u.Status == "elsewhere"))
        {
            var testClass = reason.Split(' ')[0];
            if (!typeof(BinderDiagnosticTests).Assembly.GetTypes().Any(t => t.Name == testClass))
            {
                problems.Add($"{code}: 'elsewhere' names no test class of this project ('{testClass}')");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    [Trait("Category", "Spec")]
    [Trait("Req", "CMP-500")]
    public void UncoveredReservedAndRetired_MatchTheSpecNotes()
    {
        var spec = File.ReadAllLines(System.IO.Path.Combine(RepoPaths.RequireDocs(), "spec", "compiler", "diagnostics.md"));
        var scope = CodesInScope().ToHashSet(StringComparer.Ordinal);
        var marked = spec
            .Select(l => l.Split('|').Select(c => c.Trim()).ToArray())
            .Where(c => c.Length > 2 && scope.Contains(c[1]) && (Row(c).Contains("(reserved", StringComparison.Ordinal) || Row(c).Contains("(retired", StringComparison.Ordinal)))
            .Select(c => c[1])
            .Order(StringComparer.Ordinal);

        Assert.Equal(marked, ReadUncovered().Where(u => u.Status is "reserved" or "retired").Select(u => u.Code).Order(StringComparer.Ordinal));

        static string Row(string[] cells) => string.Join("|", cells);
    }

    /// <summary>
    /// C-096: codes reserved by a lead decision (E2604, W1081) are never reported: no compiler source outside the catalogue
    /// names them. C-197: their status is <c>reserved</c> (matching the "(reserved, not emitted)" marker of their rows).
    /// C-246: W4003 joins them (CMP-629 variant 3 only; C-167 selected variant 1).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-500")]
    public void UnemittedCodes_AreNamedOnlyByTheCatalogue()
    {
        var unemitted = ReadUncovered()
            .Where(u => u.Status == "unemitted" || (u.Status == "reserved" && u.Reason.StartsWith("lead decision", StringComparison.Ordinal)))
            .Select(u => u.Code).ToList();
        Assert.Equal(["E2604", "W1081", "W4003"], unemitted.Order(StringComparer.Ordinal));
        var sources = Directory.EnumerateFiles(System.IO.Path.Combine(RepoPaths.Root, "compiler", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !string.Equals(System.IO.Path.GetFileName(p), "DiagnosticCatalog.cs", StringComparison.Ordinal));
        var named = sources.SelectMany(p => unemitted.Where(c => File.ReadAllText(p).Contains('"' + c + '"', StringComparison.Ordinal)).Select(c => $"{c}: {p}"));
        Assert.Empty(named);
    }

    [Theory]
    [InlineData("E2001")]
    [InlineData("H2003")]
    [InlineData("E2002")]
    [InlineData("E2003")]
    [InlineData("E2004")]
    [InlineData("E2005")]
    [InlineData("E2006")]
    [InlineData("E2009")]
    [InlineData("E2010")]
    [InlineData("E2011")]
    [InlineData("E2012")]
    [InlineData("E2013")]
    [InlineData("E2014")]
    [InlineData("E2016")]
    [InlineData("E2020")]
    [InlineData("E2021")]
    [InlineData("E2017")]
    [InlineData("E2019")]
    [InlineData("E2030")]
    [InlineData("E2031")]
    [InlineData("E2032")]
    [InlineData("E2035")]
    [InlineData("E2036")]
    [InlineData("E2037")]
    [InlineData("E2041")]
    [InlineData("E2042")]
    [InlineData("E2053")]
    [InlineData("E2060")]
    [InlineData("E2061")]
    [InlineData("E2062")]
    [InlineData("E2063")]
    [InlineData("E2064")]
    [InlineData("E2065")]
    [InlineData("E2066")]
    [InlineData("E2100")]
    [InlineData("E2101")]
    [InlineData("E2110")]
    [InlineData("E2111")]
    [InlineData("E2112")]
    [InlineData("E2113")]
    [InlineData("E2130")]
    [InlineData("E2912")]
    [InlineData("E2913")]
    [InlineData("W1010")]
    [InlineData("W1020")]
    [InlineData("W1021")]
    [InlineData("E2054")]
    [InlineData("E2007")]
    [InlineData("E2022")]
    [InlineData("E2023")]
    [InlineData("E2910")]
    [InlineData("E2601")]
    [InlineData("E2602")]
    [InlineData("E2603")]
    [InlineData("E2605")]
    [InlineData("E2606")]
    [InlineData("E2607")]
    [InlineData("E2608")]
    [InlineData("E2620")]
    [InlineData("E2621")]
    [InlineData("E2622")]
    [InlineData("E2623")]
    [InlineData("E2624")]
    [InlineData("E2625")]
    [InlineData("E2626")]
    [InlineData("W1060")]
    [InlineData("E2600")]
    [InlineData("W1061")]
    [InlineData("E2610")]
    [InlineData("E2067")]
    [InlineData("E2800")]
    [InlineData("E2801")]
    [InlineData("E2802")]
    [InlineData("W1080")]
    [InlineData("W1062")]
    [InlineData("E2133")]
    [InlineData("E2134")]
    [InlineData("E2611")]
    [InlineData("E2630")]
    [InlineData("E2631")]
    [InlineData("E2632")]
    [InlineData("E2633")]
    [InlineData("E2634")]
    [InlineData("E2635")]
    [InlineData("E2636")]
    [InlineData("E2637")]
    [InlineData("E2075")]
    [InlineData("E2700")]
    [InlineData("E2701")]
    [InlineData("E2702")]
    [InlineData("E2703")]
    [InlineData("E2704")]
    [InlineData("E2710")]
    [InlineData("E2711")]
    [InlineData("E2712")]
    [InlineData("E2730")]
    [InlineData("E2731")]
    [InlineData("E2732")]
    [InlineData("E2733")]
    [InlineData("E2750")]
    [InlineData("E2751")]
    [InlineData("E2122")]
    [InlineData("E2114")]
    [InlineData("E2115")]
    [InlineData("E2117")]
    public void CodeHasACase(string code) => Assert.Contains(ReadCases(), c => c.Code == code);

    [Fact]
    [Trait("Req", "CMP-508")]
    public void Redeclaration_CarriesThePreviousDeclarationAsRelatedLocation()
    {
        var diagnostic = Assert.Single(Bind("var\n  Count: Integer;\n  count: Byte;\nbegin end.").Result.Diagnostics);

        Assert.Equal("E2004: Identifier redeclared: 'count'", $"{diagnostic.Code}: {diagnostic.Message}");
        var related = Assert.Single(diagnostic.Related);
        Assert.Equal((TestPath, 1, 2), (related.Location.Path, related.Location.LineSpan.Start.Line, related.Location.LineSpan.Start.Column));
    }

    [Theory]
    [InlineData("var X: C; begin end.", "E2003(1,8): Undeclared identifier 'C'")]
    [InlineData("const C = 1; var X: C; begin end.", "E2002(1,21): 'C' is not a type identifier")]
    public void Messages(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    [Fact]
    [Trait("Req", "CMP-507")]
    public void Undeclared_IsReportedOncePerName()
    {
        Assert.Equal(
            ["E2003(1,8): Undeclared identifier 'Foo'", "E2003(1,24): Undeclared identifier 'Bar'"],
            BindDiagnostics("var A: Foo; B: foo; C: Bar; const D = FOO; begin end."));
    }

    private const string W1060Snippet =
        "type TA = class procedure P; virtual; end; TB = class(TA) procedure P; end; procedure TA.P; begin end; procedure TB.P; begin end; begin end.";

    private const string W1020Snippet = "var s: string; a: AnsiString; begin a := s end.";

    /// <summary>C-196 (CMP-505): binder warnings honour <c>{$WARNINGS}</c>, <c>{$WARN}</c> at their location.</summary>
    [Theory]
    [Trait("Req", "CMP-505")]
    [InlineData("{$WARNINGS OFF}" + W1060Snippet, "")]
    [InlineData("{$WARN W1060 OFF}" + W1060Snippet, "")]
    [InlineData("{$WARN HIDING_MEMBER OFF}" + W1060Snippet, "")]
    [InlineData("{$WARNINGS OFF}{$WARN W1060 ON}" + W1060Snippet, "W1060 Warning")]
    [InlineData("{$WARN W1060 ERROR}" + W1060Snippet, "W1060 Error")]
    [InlineData("{$WARN W1061 OFF}" + W1060Snippet, "W1060 Warning")]
    [InlineData("{$WARNINGS OFF}" + W1020Snippet, "")]
    [InlineData("var s: string; a: AnsiString; begin {$WARNINGS OFF} a := s {$WARNINGS ON} end.", "")]
    [InlineData("{$WARNINGS OFF} var s: string; a: AnsiString; begin {$WARNINGS ON} a := s end.", "W1020 Warning")]
    [InlineData("{$HINTS OFF}" + W1020Snippet, "W1020 Warning")]
    public void WarningsOff_SilencesBinderWarnings(string snippet, string expected) =>
        Assert.Equal(expected, string.Join("; ", Diagnose(snippet).Select(d => $"{d.Code} {d.Severity}")));

    // ---- inline declarations: placement, initialiser, hiding (inline-variables.md LANG-568, 569; C-307) ----

    /// <summary>The diagnostics of <paramref name="marked"/> without its <c>^</c>, and the 1-based column the <c>^</c> stood at.</summary>
    private static (string[] Diagnostics, int Column) BindMarked(string marked)
    {
        var at = marked.IndexOf('^', StringComparison.Ordinal);
        return (BindDiagnostics(marked.Remove(at, 1)), at + 1);
    }

    /// <summary>LANG-568: an inline declaration that is the statement of another statement, not an element of a statement list.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-568")]
    [InlineData("var c: Boolean; begin if c then ^var x := 1; end.")]
    [InlineData("var c: Boolean; begin if c then else ^var x: Integer; end.")]
    [InlineData("var c: Boolean; begin if c then ^const k = 1; end.")]
    [InlineData("var n: Integer; begin case n of 1: ^var x := 1; end; end.")]
    [InlineData("var n: Integer; begin case n of 1: ; 2, 3: ^const k = 2; else end; end.")]
    [InlineData("var c: Boolean; begin while c do ^var x := 1; end.")]
    [InlineData("var i: Integer; begin for i := 1 to 2 do ^var x := i; end.")]
    [InlineData("var a: array[0..1] of Integer; begin for var e in a do ^var x := e; end.")]
    [InlineData("var r: record a: Integer; end; begin with r do ^var x := 1; end.")]
    [InlineData("type TE = class end; begin try except on E: TE do ^var x := 1; end; end.")]
    public void InlineDeclaration_NotInStatementList_E2077(string marked)
    {
        var (diagnostics, column) = BindMarked(marked);

        Assert.Equal([$"E2077(1,{column}): An inline declaration is only allowed directly in a statement list"], diagnostics);
    }

    /// <summary>LANG-568: the statement lists of LANG-560 take inline declarations (no E2077).</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-568")]
    [InlineData("var c: Boolean; begin if c then begin var x := 1; end; end.")]
    [InlineData("var n: Integer; begin case n of 1: ; else var x := 1; const k = 2; end; end.")]
    [InlineData("begin repeat var x := 1; until x = 1; end.")]
    [InlineData("begin try var x := 1; finally var y := 2; end; try var z := 3; except var w := 4; end; end.")]
    public void InlineDeclaration_InStatementList_IsAccepted(string text) => Assert.Empty(BindDiagnostics(text));

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-568")]
    public void InlineInitialiserWithNameList_E2078()
    {
        const string Message = "An initialiser is only allowed for a single inline variable";

        Assert.Equal([$"E2078(1,25): {Message}"], BindDiagnostics("begin var a, b: Integer := 1; end."));
        Assert.Equal([$"E2078(1,16): {Message}"], BindDiagnostics("begin var a, b := 1; end."));
        Assert.Equal([$"E2078(3,15): {Message}"], BindDiagnostics("begin\n  var a, b, c\n    : Integer := 1;\n  a := b + c;\nend."));
        Assert.Empty(BindDiagnostics("begin var a, b: Integer; var c: Integer := 1; var d := 2; end."));
    }

    /// <summary>
    /// LANG-569: an inline name that hides a member reachable through <c>Self</c>, a name of an enclosing routine or a unit-level
    /// name of the same unit gets H2011 with the qualified name of the hidden entity; a name of a used unit gives none.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-569")]
    [InlineData("type TFoo = class Count: Integer; procedure P; end; procedure TFoo.P; begin var ^Count := 1; end; begin end.", "TFoo.Count")]
    [InlineData("type TFoo = class FC: Integer; property Count: Integer read FC; procedure P; end; procedure TFoo.P; begin var ^Count := 1; end; begin end.", "TFoo.Count")]
    [InlineData("type TFoo = class procedure Count; procedure P; end; procedure TFoo.Count; begin end; procedure TFoo.P; begin var ^Count := 1; end; begin end.", "TFoo.Count")]
    [InlineData("type TBase = class Count: Integer; end; TFoo = class(TBase) procedure P; end; procedure TFoo.P; begin var ^Count := 1; end; begin end.", "TBase.Count")]
    [InlineData("procedure Outer; var Total: Integer; procedure Inner; begin var ^Total := 1; end; begin end; begin end.", "Outer.Total")]
    [InlineData("procedure Outer(Total: Integer); procedure Inner; begin const ^Total = 1; end; begin end; begin end.", "Outer.Total")]
    [InlineData("procedure Outer; begin var Total := 1; var P := procedure begin var ^Total := 2; end; end; begin end.", "Outer.Total")]
    [InlineData("unit Unit1; interface var Limit: Integer; implementation procedure P; begin var ^Limit := 1; end; end.", "Unit1.Limit")]
    [InlineData("unit Unit1; interface implementation const Limit = 5; procedure P; begin if Limit > 0 then begin var ^Limit: Integer; end; end; end.", "Unit1.Limit")]
    [InlineData("program Prog; var Limit: Integer; procedure P; begin var ^Limit := 1; end; begin end.", "Prog.Limit")]
    [InlineData("program Prog; var Limit: Integer; begin var ^Limit := 1; end.", "Prog.Limit")]
    [InlineData("program Prog; procedure P; begin var ^Output := 1; end; begin var Input := 2; var Integer := 3; end.", null)]
    [InlineData("program Prog; procedure P; begin var ^Fresh := 1; end; begin var Fresh := 2; end.", null)]
    public void InlineHidesOuter_H2011(string marked, string? hidden)
    {
        var (diagnostics, column) = BindMarked(marked);
        var name = marked[(marked.IndexOf('^', StringComparison.Ordinal) + 1)..].Split(' ', ':', ';')[0];

        Assert.Equal(hidden is null ? [] : [$"H2011(1,{column}): Inline declaration '{name}' hides '{hidden}'"], diagnostics);
    }

    /// <summary>LANG-569: H2011 follows <c>{$HINTS}</c> and <c>{$WARN INLINE_HIDES_OUTER}</c>.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-569")]
    [InlineData("", "H2011 Hint")]
    [InlineData("{$HINTS OFF}", "")]
    [InlineData("{$WARN INLINE_HIDES_OUTER OFF}", "")]
    [InlineData("{$WARN INLINE_HIDES_OUTER ERROR}", "H2011 Error")]
    public void InlineHidesOuter_FollowsTheDirectives(string directive, string expected) =>
        Assert.Equal(expected, string.Join("; ", Diagnose(directive + "var Limit: Integer; begin var Limit := 1; Limit := 2; end.")
            .Where(d => d.Code == "H2011").Select(d => $"{d.Code} {d.Severity}")));

    /// <summary>LANG-570: before the declaration, in its own initialiser and after the scope the name is the outer entity (or E2003).</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-570")]
    public void InlineName_ResolvesOutsideItsScope()
    {
        Assert.Equal(["E2003(1,7): Undeclared identifier 'x'"], BindDiagnostics("begin x := 1; var x := 2; end."));
        Assert.Equal(["E2003(1,16): Undeclared identifier 'x'"], BindDiagnostics("begin var x := x; end."));
        Assert.Equal(["E2003(1,38): Undeclared identifier 'x'"], BindDiagnostics("begin begin var x := 1; x := 2; end; x := 3; end."));
        Assert.Equal(["E2003(1,53): Undeclared identifier 'x'"], BindDiagnostics("begin repeat var x := 1; until x = 1; if 1 = 1 then x := 2; end."));

        // the outer entity: the global is a string, the inline variable an Integer
        Assert.Equal(["H2011(1,42): Inline declaration 'G' hides 'test.G'"],
            BindDiagnostics("var G: string; begin G := 'a'; begin var G := 1; G := G + 1; end; G := 'b'; end."));
        Assert.Equal(["H2011(1,26): Inline declaration 'G' hides 'test.G'", "E2010(1,40): Incompatible types: 'string' and 'Integer'"],
            BindDiagnostics("var G: string; begin var G: Integer := G; end."));
    }
}
