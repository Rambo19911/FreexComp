using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// CMP-1421 (C-401): the completion caret corpus <c>Completion/</c> — one case per <c>*.pas</c> file or per folder of units, as the
/// go-to-definition corpus (CMP-1419). <c>{|}</c> marks the caret; <c>{#has A, B}</c> labels that must be offered (matched ignoring
/// case, then the declared casing is checked); <c>{#not A}</c> labels that must not; <c>{#top A}</c> the label sorting first by
/// <see cref="CompletionItem.SortText"/>, then label (CMP-723); <c>{#kind A=Field}</c> the kind of the one item labelled A;
/// <c>{#none}</c> an empty result; <c>{#ranked A}</c> / <c>{#unranked A}</c> the item labelled A is offered with / without the
/// expected-type prefix <c>!</c> on its <see cref="CompletionItem.SortText"/> (CMP-1426). The markers are removed before the case is opened (line endings LF). The caret's file starts with
/// <c>{ context: &lt;name&gt; }</c> (CMP-1422). The RTL units (and the Win32 binding units) are on the search path as for a project.
/// </summary>
[Trait("Phase", "8")]
public sealed class CompletionCorpusTests
{
    private const string Caret = "{|}";

    /// <summary>CMP-1422: the contexts the corpus must cover.</summary>
    private static readonly string[] RequiredContexts =
    [
        "member-field", "member-method", "member-property", "member-class", "member-instance", "member-class-var", "member-nested-type",
        "member-constant", "inherited-members", "class-helper", "record-helper", "generic-instantiated", "generic-inside",
        "generic-constraint", "interface", "enum-type", "unit-qualifier", "uses-clause", "type-position", "inherited", "raise",
        "on-handler", "override", "directive", "routine-directive", "property-specifier", "declaration-start", "statement-start",
        "expression", "locals", "parameters", "inline-variable", "with", "visibility", "strict-private", "strict-protected", "broken",
        "none-comment", "none-string", "none-disabled", "expected-type",
    ];

    private static readonly Regex ContextLine = new(@"^\{ context: ([a-z\-]+) \}\n", RegexOptions.CultureInvariant);

    private static readonly Regex Marker = new(@"\{#(has|not|top|kind|none|ranked|unranked)\b([^}]*)\}", RegexOptions.CultureInvariant);

    private static string Root => Path.Combine(AppContext.BaseDirectory, "Completion");

    /// <summary>The RTL as a project sees it: <c>rtl\src</c> under <see cref="TestFiles.RtlDir"/>, <c>rtl\winapi\src</c> beside it.</summary>
    private static readonly Lazy<(string Path, string Text)[]> Rtl = new(() =>
    {
        var src = Path.Combine(RepoPaths.Root, "rtl", "src");
        var winapi = Path.Combine(RepoPaths.Root, "rtl", "winapi", "src");
        var winapiDir = Path.GetFullPath(Path.Combine(TestFiles.RtlDir, "..", "winapi", "src"));
        var files = Directory.GetFiles(src).Where(p => Path.GetExtension(p) is ".pas" or ".inc" or ".json").Select(p => (Dir: TestFiles.RtlDir, Source: p))
            .Concat(Directory.GetFiles(winapi, "*.pas").Select(p => (Dir: winapiDir, Source: p)));
        return [.. files.OrderBy(f => f.Source, StringComparer.Ordinal).Select(f => (Path.Combine(f.Dir, Path.GetFileName(f.Source)), File.ReadAllText(f.Source)))];
    });

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
    [Trait("Req", "CMP-1422")]
    public void CorpusHasAtLeastSixtyCases()
    {
        var names = CaseNames().ToList();
        Assert.True(names.Count >= 60, $"{names.Count} cases");
        var contexts = names.Select(n => Load(n).Context).ToHashSet(StringComparer.Ordinal);
        var missing = RequiredContexts.Where(c => !contexts.Contains(c)).ToList();
        var unknown = contexts.Where(c => !RequiredContexts.Contains(c)).ToList();          // a typo would be a context of its own
        Assert.True(missing.Count == 0, "contexts not covered: " + string.Join(", ", missing));
        Assert.True(unknown.Count == 0, "unknown contexts: " + string.Join(", ", unknown));
        Assert.True(names.Count(n => n.StartsWith("With", StringComparison.Ordinal)) >= 3, "CMP-1423: three With* cases");
        Assert.True(names.Count(n => n.StartsWith("Broken", StringComparison.Ordinal)) >= 3, "CMP-1425: three Broken* cases");
        Assert.Contains(names, n => n.StartsWith("StrictPrivate", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("StrictProtected", StringComparison.Ordinal));
        Assert.True(names.Count(n => n.StartsWith("Expected", StringComparison.Ordinal)) >= 6, "CMP-1426: six Expected* cases");
    }

    [Theory]
    [Trait("Req", "CMP-1421")]
    [MemberData(nameof(Cases))]
    public void Case(string name)
    {
        var @case = Load(name);
        var fs = TestFiles.Files([.. @case.Files.Select(f => (f.Name, f.Text))]);
        foreach (var (path, text) in Rtl.Value)
        {
            fs.Add(path, text);
        }

        var main = @case.Files.FirstOrDefault(f => Regex.IsMatch(f.Text, @"^program\b", RegexOptions.Multiline | RegexOptions.IgnoreCase)) ?? @case.CaretFile;
        var workspace = new Workspace(fs);
        workspace.SetProject(TestFiles.Options(main.Name));
        var doc = workspace.GetDocument(TestFiles.PathOf(@case.CaretFile.Name));

        var items = CompletionService.GetCompletions(doc, @case.CaretFile.Caret, new CompletionTrigger(CompletionTriggerKind.Invoked, '\0'),
            TestContext.Current.CancellationToken).Items;

        var e = @case.Expect;
        if (e.None)
        {
            Assert.True(items.IsEmpty, $"{name}: expected no items, got {string.Join(", ", items.Take(10).Select(i => i.Label))}");
            return;
        }

        Assert.False(items.IsEmpty, $"{name}: no items");
        var problems = new List<string>();
        foreach (var label in e.Has)
        {
            var found = items.Where(i => string.Equals(i.Label, label, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0)
            {
                problems.Add($"missing {label}");
            }
            else if (!found.Any(i => i.Label == label))
            {
                problems.Add($"{label} offered as {found[0].Label}");
            }
        }

        problems.AddRange(e.Not.Where(l => items.Any(i => string.Equals(i.Label, l, StringComparison.OrdinalIgnoreCase))).Select(l => $"unexpected {l}"));
        if (e.Top is { } top)
        {
            var first = items.OrderBy(i => i.SortText, StringComparer.Ordinal).ThenBy(i => i.Label, StringComparer.Ordinal).First();
            if (first.Label != top)
            {
                problems.Add($"top is {first.Label} ({first.SortText}), expected {top}");
            }
        }

        foreach (var (label, kind) in e.Kinds)
        {
            var found = items.Where(i => string.Equals(i.Label, label, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count != 1 || found[0].Kind != kind)
            {
                problems.Add($"{label}: expected one {kind} item, got [{string.Join(", ", found.Select(i => i.Kind))}]");
            }
        }

        foreach (var (label, ranked) in e.Ranked)
        {
            var found = items.Where(i => string.Equals(i.Label, label, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0)
            {
                problems.Add($"missing {label}");
            }
            else if (found.Any(i => i.SortText.StartsWith('!')) != ranked)
            {
                problems.Add($"{label}: expected {(ranked ? "" : "no ")}'!' prefix, got {string.Join(", ", found.Select(i => i.SortText))}");
            }
        }

        Assert.True(problems.Count == 0, $"{name}: {string.Join("; ", problems)}");
    }

    private sealed record CaseFile(string Name, string Text, int Caret);

    private sealed record Expectation(List<string> Has, List<string> Not, string? Top, List<(string Label, CompletionItemKind Kind)> Kinds, bool None,
                                       List<(string Label, bool Ranked)> Ranked);

    private sealed record LoadedCase(IReadOnlyList<CaseFile> Files, CaseFile CaretFile, string Context, Expectation Expect);

    private static LoadedCase Load(string name)
    {
        var folder = Path.Combine(Root, name);
        var paths = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.pas").Order(StringComparer.Ordinal).ToArray()
            : [Path.Combine(Root, name + ".pas")];
        var expect = new Expectation([], [], null, [], false, []);
        var files = paths.Select(p => Strip(name, Path.GetFileName(p), File.ReadAllText(p), ref expect)).ToArray();
        var caret = Assert.Single(files, f => f.Caret >= 0);
        var context = ContextLine.Match(caret.Text);
        Assert.True(context.Success, $"{name}: the caret's file must start with '{{ context: <name> }}'");
        Assert.True(expect.None != (expect.Has.Count + expect.Not.Count + expect.Kinds.Count + expect.Ranked.Count > 0 || expect.Top is not null),
            $"{name}: either {{#none}} or at least one other marker");
        return new LoadedCase(files, caret, context.Groups[1].Value, expect);
    }

    /// <summary>Removes the caret and the expectation markers (line endings normalised to LF), collecting the expectations.</summary>
    private static CaseFile Strip(string @case, string name, string markup, ref Expectation expect)
    {
        markup = markup.ReplaceLineEndings("\n");
        var text = new StringBuilder();
        var caret = -1;
        for (var i = 0; i < markup.Length; i++)
        {
            Match marker;
            if (string.CompareOrdinal(markup, i, Caret, 0, Caret.Length) == 0)
            {
                Assert.True(caret < 0, $"{@case}: two {Caret} in {name}");
                caret = text.Length;
                i += Caret.Length - 1;
            }
            else if (markup[i] == '{' && i + 1 < markup.Length && markup[i + 1] == '#' && (marker = Marker.Match(markup, i)).Success && marker.Index == i)
            {
                var args = marker.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                switch (marker.Groups[1].Value)
                {
                    case "has":
                        expect.Has.AddRange(args);
                        break;
                    case "not":
                        expect.Not.AddRange(args);
                        break;
                    case "ranked" or "unranked":
                        expect.Ranked.AddRange(args.Select(a => (a, marker.Groups[1].Value == "ranked")));
                        break;
                    case "top":
                        Assert.True(expect.Top is null && args.Length == 1, $"{@case}: one {{#top X}}");
                        expect = expect with { Top = args[0] };
                        break;
                    case "kind":
                        foreach (var arg in args)
                        {
                            var parts = arg.Split('=', StringSplitOptions.TrimEntries);
                            Assert.True(parts.Length == 2 && Enum.TryParse<CompletionItemKind>(parts[1], out _), $"{@case}: bad {{#kind {arg}}}");
                            expect.Kinds.Add((parts[0], Enum.Parse<CompletionItemKind>(parts[1])));
                        }

                        break;
                    default:
                        Assert.True(args.Length == 0 && !expect.None, $"{@case}: one {{#none}}");
                        expect = expect with { None = true };
                        break;
                }

                i += marker.Length - 1;
            }
            else
            {
                Assert.False(markup[i] == '{' && i + 1 < markup.Length && markup[i + 1] == '#', $"{@case}: unknown marker in {name}");
                text.Append(markup[i]);
            }
        }

        return new CaseFile(name, text.ToString(), caret);
    }
}
