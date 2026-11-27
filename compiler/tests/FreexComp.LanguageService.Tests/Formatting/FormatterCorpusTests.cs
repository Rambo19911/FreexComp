using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.LanguageService.Formatting;

namespace FreexComp.LanguageService.Tests.Formatting;

/// <summary>
/// formatter.md §5, card acceptance 2–3 (C-404, C-405): every <c>tests/compiler/**/*.pas</c> of the repository is formatted with
/// the built-in style, with the C-405 options off and with other values of them. A refused file (a syntax error, CMP-1438) is counted and listed, never a failure, but the refusal must be
/// for a syntax error and at least 90 % of the corpus must be formatted. Formatted files keep their token stream (CMP-1437)
/// and format to themselves (CMP-1445).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class FormatterCorpusTests(ITestOutputHelper output)
{
    private sealed record Outcome(string Name, string Text, FormatResult Result, string? Formatted);

    /// <summary>
    /// The built-in style (every C-405 option on), every C-405 option off, and other values of them (with C-404 options changed
    /// as well).
    /// </summary>
    private static readonly Dictionary<string, FormatProfile> Profiles = new()
    {
        ["default"] = FormatProfile.Default,
        ["off"] = FormatProfile.Default with
        {
            Keywords = KeywordCase.AsIs, Directives = KeywordCase.AsIs, IdentifiersAsDeclared = false, Uses = UsesWrap.AsIs, MaxLineLength = 0,
        },
        ["other"] = FormatProfile.Default with
        {
            Keywords = KeywordCase.Upper, Directives = KeywordCase.Lower, Uses = UsesWrap.Fill, MaxLineLength = 60, Begin = BeginStyle.SameLine,
            IndentSize = 3,
        },
    };

    private static readonly Dictionary<string, Lazy<Outcome[]>> Corpus = Profiles.ToDictionary(p => p.Key, p => new Lazy<Outcome[]>(() =>
    {
        var root = Path.Combine(RepoPaths.Root, "tests", "compiler");
        return [.. Directory.EnumerateFiles(root, "*.pas", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path =>
        {
            var text = File.ReadAllText(path);
            var result = PascalFormatter.Format(SourceText.From(path, text), p.Value, null, CancellationToken.None);
            return new Outcome(Path.GetRelativePath(root, path), text, result, result.Refused ? null : FormatterHarness.Apply(text, result));
        })];
    }));

    public static TheoryData<string> ProfileNames() => [.. Profiles.Keys];

    [Theory]
    [Trait("Req", "CMP-1437")]
    [Trait("Req", "CMP-1438")]
    [Trait("Req", "CMP-1448")]
    [Trait("Req", "CMP-1449")]
    [Trait("Req", "CMP-1450")]
    [Trait("Req", "CMP-1451")]
    [MemberData(nameof(ProfileNames))]
    public void TokenStreamUnchanged(string profile)
    {
        var corpus = Corpus[profile].Value;
        Report(profile, corpus);
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

    [Theory]
    [Trait("Req", "CMP-1445")]
    [MemberData(nameof(ProfileNames))]
    public void Idempotent(string profile)
    {
        var problems = new List<string>();
        foreach (var c in Corpus[profile].Value.Where(c => !c.Result.Refused))
        {
            var again = PascalFormatter.Format(SourceText.From(@"C:\corpus\" + c.Name, c.Formatted!), Profiles[profile], null, TestContext.Current.CancellationToken);
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

    private void Report(string profile, Outcome[] corpus)
    {
        var refused = corpus.Where(c => c.Result.Refused).ToList();
        var changed = corpus.Count(c => c.Formatted is { } f && f != c.Text);
        output.WriteLine($"formatter corpus ({profile}): {corpus.Length} files, {corpus.Length - refused.Count} formatted ({changed} changed), {refused.Count} refused");
        foreach (var c in refused)
        {
            output.WriteLine($"  refused {c.Name}: {c.Result.RefusalReason}");
        }
    }
}
