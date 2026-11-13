using System.Text.RegularExpressions;

namespace FreexComp.Compiler.Tests.Diagnostics;

/// <summary>
/// CMP-210: parses the Markdown tables of docs/spec/compiler/diagnostics.md and compares them with
/// <see cref="DiagnosticCatalog.All"/> (code, name, severity, template, order).
/// </summary>
[Trait("Phase", "1")]
[Trait("Req", "CMP-210")]
[Trait("Req", "CMP-500")]
public sealed partial class DiagnosticCatalogMatchesSpec
{
    [Fact]
    [Trait("Category", "Spec")]
    public void DiagnosticCatalogMatchesSpec_EveryCodeNameSeverityAndTemplate()
    {
        var expected = ParseSpec(Path.Combine(RepoPaths.RequireDocs(), "spec", "compiler", "diagnostics.md"));
        var actual = DiagnosticCatalog.All;

        Assert.True(expected.Count > 200, $"only {expected.Count} codes parsed from the spec");

        var missing = expected.Where(e => !actual.Contains(e)).ToList();
        var extra = actual.Where(a => !expected.Contains(a)).ToList();
        Assert.True(
            missing.Count == 0 && extra.Count == 0,
            "Catalogue differs from diagnostics.md.\nMissing or different (spec form):\n"
                + string.Join('\n', missing.Select(ToCSharp))
                + "\nNot in spec (catalogue form):\n" + string.Join('\n', extra.Select(ToCSharp)));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DiagnosticCatalog_Get_ReturnsDescriptorByCode()
    {
        Assert.Equal("Undeclared identifier '%s'", DiagnosticCatalog.Get("E2003").Template);
        Assert.Equal("UNUSED_VARIABLE", DiagnosticCatalog.Get("H2001").Name);
        Assert.Throws<KeyNotFoundException>(() => DiagnosticCatalog.Get("E9999"));
    }

    [Fact]
    public void DiagnosticCatalog_CodesAreUnique()
    {
        Assert.Equal(DiagnosticCatalog.All.Count, DiagnosticCatalog.All.Select(d => d.Code).Distinct().Count());
    }

    private static List<DiagnosticDescriptor> ParseSpec(string path)
    {
        var result = new List<DiagnosticDescriptor>();
        var hasNameColumn = false;
        foreach (var line in File.ReadLines(path))
        {
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 3 || cells[0].Length != 0)
            {
                continue;
            }

            if (cells[1] == "Code")
            {
                hasNameColumn = cells[2] == "Name";
                continue;
            }

            if (!CodePattern().IsMatch(cells[1]))
            {
                continue;
            }

            var code = cells[1];
            var name = hasNameColumn ? cells[2] : "—";
            var template = Annotation().Replace(hasNameColumn ? cells[3] : cells[2], "");
            var severity = code[0] switch
            {
                'E' => DiagnosticSeverity.Error,
                'W' => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Hint,
            };
            result.Add(new DiagnosticDescriptor(code, name == "—" ? null : name, severity, template));
        }

        return result;
    }

    private static string ToCSharp(DiagnosticDescriptor d) =>
        $"        new(\"{d.Code}\", {(d.Name is null ? "null" : "\"" + d.Name + "\"")}, DiagnosticSeverity.{d.DefaultSeverity}, \"{d.Template}\"),";

    [GeneratedRegex(@"^[EWH]\d{4}$")]
    private static partial Regex CodePattern();

    // Trailing editorial note in a template cell, e.g. "%s  *(user {$MESSAGE ERROR})*".
    [GeneratedRegex(@"\s+\*\(.*\)\*$")]
    private static partial Regex Annotation();
}
