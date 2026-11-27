using System.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// CMP-1419 (C-396): the go-to-definition caret corpus <c>GoToDefinition/</c> — one case per <c>*.pas</c> file or per folder of
/// units. <c>{|}</c> marks the caret, <c>{#}</c> the expected start of the target (in any file of the case), <c>{#none}</c> an
/// empty result. The markers are removed before the case is opened; the main file is the case's <c>program</c> file (else the
/// caret's file). Each case runs through <see cref="NavigationService.GoToDefinition"/> as CMP-740.
/// </summary>
[Trait("Phase", "8")]
[Trait("Req", "CMP-1419")]
public sealed class GoToDefinitionCorpusTests
{
    private const string Caret = "{|}";

    private const string Target = "{#}";

    private const string None = "{#none}";

    private static string Root => Path.Combine(AppContext.BaseDirectory, "GoToDefinition");

    /// <summary>The case names: file names without <c>.pas</c> and folder names, ordinal order.</summary>
    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var name in CaseNames())
        {
            data.Add(name);
        }

        return data;
    }

    private static IEnumerable<string> CaseNames() =>
        Directory.EnumerateFiles(Root, "*.pas").Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Concat(Directory.EnumerateDirectories(Root).Select(Path.GetFileName).OfType<string>())
            .Order(StringComparer.Ordinal);

    [Fact]
    public void CorpusHasAtLeast30Cases() => Assert.True(CaseNames().Count() >= 30, $"{CaseNames().Count()} cases");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case(string name)
    {
        var folder = Path.Combine(Root, name);
        var paths = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.pas").Order(StringComparer.Ordinal).ToArray()
            : [Path.Combine(Root, name + ".pas")];
        var files = paths.Select(p => Strip(Path.GetFileName(p), File.ReadAllText(p))).ToArray();

        var carets = files.Where(f => f.Caret >= 0).ToArray();
        var caret = Assert.Single(carets);
        var targets = files.Where(f => f.Target >= 0).ToArray();
        var none = files.Count(f => f.None);
        Assert.True(targets.Length + none == 1, $"{name}: exactly one {Target} or {None} expected");

        var main = files.FirstOrDefault(f => f.Text.StartsWith("program", StringComparison.OrdinalIgnoreCase)) ?? caret;
        var workspace = new Workspace(TestFiles.Files([.. files.Select(f => (f.Name, f.Text))]));
        workspace.SetProject(TestFiles.Options(main.Name));
        var doc = workspace.GetDocument(TestFiles.PathOf(caret.Name));

        var result = NavigationService.GoToDefinition(doc, caret.Caret, TestContext.Current.CancellationToken);

        if (none == 1)
        {
            Assert.Empty(result);
            return;
        }

        var target = Assert.Single(result);
        Assert.Equal((TestFiles.PathOf(targets[0].Name), targets[0].Target), (target.Path, target.Span.Start));
    }

    private sealed record CaseFile(string Name, string Text, int Caret, int Target, bool None);

    /// <summary>Removes the markers (line endings normalised to LF) and records where they were.</summary>
    private static CaseFile Strip(string name, string markup)
    {
        markup = markup.ReplaceLineEndings("\n");
        var text = new StringBuilder();
        int caret = -1, target = -1;
        var none = false;
        for (var i = 0; i < markup.Length; i++)
        {
            if (string.CompareOrdinal(markup, i, None, 0, None.Length) == 0)
            {
                Assert.False(none, $"{name}: two {None}");
                none = true;
                i += None.Length - 1;
            }
            else if (string.CompareOrdinal(markup, i, Caret, 0, Caret.Length) == 0)
            {
                Assert.True(caret < 0, $"{name}: two {Caret}");
                caret = text.Length;
                i += Caret.Length - 1;
            }
            else if (string.CompareOrdinal(markup, i, Target, 0, Target.Length) == 0)
            {
                Assert.True(target < 0, $"{name}: two {Target}");
                target = text.Length;
                i += Target.Length - 1;
            }
            else
            {
                text.Append(markup[i]);
            }
        }

        return new CaseFile(name, text.ToString(), caret, target, none);
    }
}
