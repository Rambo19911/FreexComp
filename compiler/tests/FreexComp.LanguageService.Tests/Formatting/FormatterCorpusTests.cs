using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.LanguageService.Formatting;

namespace FreexComp.LanguageService.Tests.Formatting;

/// <summary>
/// formatter.md §5, card acceptance 2–3 (C-404): every <c>tests/compiler/**/*.pas</c> of the repository is formatted with the
/// built-in style. A refused file (a syntax error, CMP-1438) is counted and listed, never a failure, but the refusal must be
/// for a syntax error and at least 90 % of the corpus must be formatted. Formatted files keep their token stream (CMP-1437)
/// and format to themselves (CMP-1445).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class FormatterCorpusTests(ITestOutputHelper output)
{
    private sealed record Outcome(string Name, string Text, FormatResult Result, string? Formatted);

    private static readonly Lazy<Outcome[]> Corpus = new(() =>
    {
        var root = Path.Combine(RepoPaths.Root, "tests", "compiler");
        return [.. Directory.EnumerateFiles(root, "*.pas", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path =>
        {
            var text = File.ReadAllText(path);
            var result = PascalFormatter.Format(SourceText.From(path, text), FormatProfile.Default, null, CancellationToken.None);
            return new Outcome(Path.GetRelativePath(root, path), text, result, result.Refused ? null : FormatterHarness.Apply(text, result));
        })];
    });

    [Fact]
    [Trait("Req", "CMP-1437")]
    [Trait("Req", "CMP-1438")]
    public void TokenStreamUnchanged()
    {
        var corpus = Corpus.Value;
        Report(corpus);
        var problems = new List<string>();
        foreach (var c in corpus)
        {
            if (c.Result.Refused)
            {
                var tree = SyntaxTree.Parse(SourceText.From(@"C:\corpus\" + c.Name, c.Text));
                if (!tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Code.StartsWith("E1", StringComparison.Ordinal)))
                {
                    problems.Add($"{c.Name}: refused without a syntax error: {c.Result.RefusalReason}");
                }

                continue;
            }

            if (!FormatterHarness.Stream(c.Text).SequenceEqual(FormatterHarness.Stream(c.Formatted!)))
            {
                problems.Add($"{c.Name}: token stream changed");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        var formatted = corpus.Count(c => !c.Result.Refused);
        Assert.True(formatted * 10 >= corpus.Length * 9, $"only {formatted} of {corpus.Length} files formatted");
    }

    [Fact]
    [Trait("Req", "CMP-1445")]
    public void Idempotent()
    {
        var problems = new List<string>();
        foreach (var c in Corpus.Value.Where(c => !c.Result.Refused))
        {
            var again = PascalFormatter.Format(SourceText.From(@"C:\corpus\" + c.Name, c.Formatted!), FormatProfile.Default, null, TestContext.Current.CancellationToken);
            if (again.Refused)
            {
                problems.Add($"{c.Name}: the formatted text is refused: {again.RefusalReason}");
            }
            else if (!again.Edits.IsEmpty)
            {
                var e = again.Edits[0];
                var line = c.Formatted!.AsSpan(0, e.Span.Start).Count('\n') + 1;
                problems.Add($"{c.Name}: {again.Edits.Length} edits on the second run, first at line {line}: '{c.Formatted!.Substring(e.Span.Start, e.Span.Length)}' -> '{e.NewText}'");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private void Report(Outcome[] corpus)
    {
        var refused = corpus.Where(c => c.Result.Refused).ToList();
        output.WriteLine($"formatter corpus: {corpus.Length} files, {corpus.Length - refused.Count} formatted, {refused.Count} refused");
        foreach (var c in refused)
        {
            output.WriteLine($"  refused {c.Name}: {c.Result.RefusalReason}");
        }
    }
}
