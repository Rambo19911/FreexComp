using FreexComp.LanguageService.Formatting;

namespace FreexComp.LanguageService.Tests.Formatting;

/// <summary>
/// formatter.md §5 (C-404, C-405): every <c>Formatting/Golden/&lt;name&gt;.in.pas</c> formats to <c>&lt;name&gt;.out.pas</c> with the
/// profile <c>&lt;name&gt;.pasformat</c> (the built-in style without one); the expected text formats to itself (CMP-1445) and
/// keeps the token stream (CMP-1437). <c>UPDATE_FORMAT_GOLDEN=1</c> writes the actual results into the source folder (review
/// the diff).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
public sealed class FormatterGoldenTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Formatting", "Golden");

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var name in Directory.EnumerateFiles(Root, "*.in.pas").Select(p => Path.GetFileName(p)[..^".in.pas".Length]).Order(StringComparer.Ordinal))
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    [Trait("Req", "CMP-1440")]
    [Trait("Req", "CMP-1448")]
    [Trait("Req", "CMP-1449")]
    [Trait("Req", "CMP-1450")]
    [Trait("Req", "CMP-1451")]
    public void EveryRuleAndOptionHasAPair()
    {
        var names = Cases().Select(c => (string)c.Data).ToList();
        Assert.True(names.Count >= 30, $"only {names.Count} golden pairs");
        string[] prefixes = ["indent-", "breaks-", "spacing-", "blank-", "format-off", "case-keywords-", "case-directives-", "case-identifiers", "uses-", "wrap-"];
        foreach (var prefix in prefixes)
        {
            Assert.True(names.Count(n => n.StartsWith(prefix, StringComparison.Ordinal)) >= 2, $"fewer than two {prefix}* pairs");
        }

        var profiles = names.Where(n => File.Exists(Path.Combine(Root, n + ".pasformat")))
            .Select(n => FormatProfile.Parse(File.ReadAllText(Path.Combine(Root, n + ".pasformat")), null)!).ToList();
        Assert.Contains(profiles, p => p.Begin == BeginStyle.SameLine);
        Assert.Contains(profiles, p => p.IndentSize != FormatProfile.Default.IndentSize);
        Assert.Contains(profiles, p => p.MaxBlankLines == 0);
        Assert.Contains(profiles, p => p.MaxBlankLines > 1);
        Assert.Contains(profiles, p => !p.SpaceAroundOperators);
        Assert.Contains(profiles, p => !p.SpaceAfterComma);
        Assert.Contains(profiles, p => !p.SpaceAroundAssign);
        Assert.Contains(profiles, p => p.SpaceBeforeColonInDecl);

        // C-405 (formatter.md §3): every value of the capitalisation, uses and line-length options; the defaults are the pairs
        // without a profile
        Assert.Contains(profiles, p => p.Keywords == KeywordCase.Upper);
        Assert.Contains(profiles, p => p.Keywords == KeywordCase.AsIs);
        Assert.Contains(profiles, p => p.Directives == KeywordCase.Lower);
        Assert.Contains(profiles, p => p.Directives == KeywordCase.AsIs);
        Assert.Contains(profiles, p => !p.IdentifiersAsDeclared);
        Assert.Contains(profiles, p => p.Uses == UsesWrap.Fill);
        Assert.Contains(profiles, p => p.Uses == UsesWrap.AsIs);
        Assert.Contains(profiles, p => p.MaxLineLength == 0);
        Assert.Contains(profiles, p => p.MaxLineLength is > 0 and < 120);
        string[] defaults = ["case-keywords-lower", "case-directives-upper", "case-identifiers", "uses-oneperline", "wrap-arguments", "wrap-unbreakable"];
        Assert.All(defaults, n => Assert.True(names.Contains(n) && !File.Exists(Path.Combine(Root, n + ".pasformat")), $"{n}: no default pair"));
    }

    [Theory]
    [Trait("Req", "CMP-1440")]
    [Trait("Req", "CMP-1441")]
    [Trait("Req", "CMP-1442")]
    [Trait("Req", "CMP-1443")]
    [Trait("Req", "CMP-1444")]
    [Trait("Req", "CMP-1448")]
    [Trait("Req", "CMP-1449")]
    [Trait("Req", "CMP-1450")]
    [Trait("Req", "CMP-1451")]
    [MemberData(nameof(Cases))]
    public void Case(string name)
    {
        var input = File.ReadAllText(Path.Combine(Root, name + ".in.pas"));
        var profilePath = Path.Combine(Root, name + ".pasformat");
        var profile = File.Exists(profilePath) ? FormatProfile.Parse(File.ReadAllText(profilePath), profilePath) : FormatProfile.Default;
        Assert.NotNull(profile);

        var actual = FormatterHarness.Format(input, profile);
        var update = Environment.GetEnvironmentVariable("UPDATE_FORMAT_GOLDEN") == "1";
        if (update)
        {
            var source = Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.LanguageService.Tests", "Formatting", "Golden", name + ".out.pas");
            File.WriteAllText(source, actual);
        }

        var expected = update ? actual : File.ReadAllText(Path.Combine(Root, name + ".out.pas"));
        Assert.Equal(expected, actual);
        Assert.Equal(FormatterHarness.Stream(input), FormatterHarness.Stream(actual));
        Assert.Equal(expected, FormatterHarness.Format(expected, profile));
    }
}
