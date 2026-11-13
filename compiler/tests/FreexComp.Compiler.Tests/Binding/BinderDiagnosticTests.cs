using System.Globalization;
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
        var diagnostic = Assert.Single(Bind(snippet).Result.Diagnostics);

        Assert.Equal(
            (number, code, line, column),
            (number, diagnostic.Code, diagnostic.Location.LineSpan.Start.Line + 1, diagnostic.Location.LineSpan.Start.Column + 1));
    }

    [Theory]
    [InlineData("E2002")]
    [InlineData("E2003")]
    [InlineData("E2004")]
    [InlineData("E2009")]
    [InlineData("E2010")]
    [InlineData("E2011")]
    [InlineData("E2012")]
    [InlineData("E2053")]
    [InlineData("E2060")]
    [InlineData("E2061")]
    [InlineData("E2062")]
    [InlineData("E2063")]
    [InlineData("E2064")]
    [InlineData("E2065")]
    [InlineData("E2066")]
    [InlineData("W1010")]
    [InlineData("W1021")]
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
