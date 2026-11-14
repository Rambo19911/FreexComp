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

    private static readonly string[] Statuses = ["reserved", "retired", "M2.2", "M2.3", "M2.4", "M2.5", "M2.6", "Phase3", "Phase4", "Phase8", "elsewhere"];

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

    [Theory]
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
}
