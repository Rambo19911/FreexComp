using System.Text.RegularExpressions;
using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// U-113, testing.md UIF-1206: every requirement id of <c>docs/spec/ui-framework/*.md</c> (except
/// <c>phase8-controls.md</c>) is covered by a test or listed with a reason in <c>ui-framework/tests/coverage-exempt.txt</c>.
/// A requirement is an id <c>UIF-nnn</c> that starts a rule line or the first cell of a table row; a later cell or prose
/// only refers to it. A test covers an id through the <c>// covers:</c> header of a <c>.pas</c> file under
/// <c>ui-framework/tests</c> or a <c>[Trait("Req", "UIF-nnn")]</c> in a <c>.cs</c> file there. A listed id must be a
/// requirement, must have a reason and must not be covered as well, so the list only shrinks.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class UiReqCoverageTests
{
    [GeneratedRegex(@"^(?:\|\s*)?(UIF-\d{3,4})\b")]
    private static partial Regex Definition();

    [GeneratedRegex(@"^\s*//\s*covers:(.*)$", RegexOptions.Multiline)]
    private static partial Regex CoversHeader();

    [GeneratedRegex(@"Trait\(\s*""Req""\s*,\s*""(UIF-\d{3,4})""\s*\)")]
    private static partial Regex ReqTrait();

    [GeneratedRegex(@"UIF-\d{3,4}")]
    private static partial Regex AnyId();

    public static string SpecDirectory => Path.Combine(RtlSetup.RepositoryRoot, "docs", "spec", "ui-framework");

    public static string TestsDirectory => Path.Combine(UiCorpus.AreaDirectory, "tests");

    public static string ExemptPath => Path.Combine(TestsDirectory, "coverage-exempt.txt");

    [Fact]
    [Trait("Category", "Spec")]              // reads docs/ to compare the tests with the spec (TESTING.md, U-132)
    [Trait("Req", "UIF-1206")]
    public void UiReq_Coverage()
    {
        // docs/ is git-ignored working material; without it the check has no input (as the RTL's Req_Coverage)
        if (SkipReason(SpecDirectory) is { } reason)
        {
            Assert.Skip(reason);
        }

        var requirements = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(SpecDirectory, "*.md")
            .Where(f => !string.Equals(Path.GetFileName(f), "phase8-controls.md", StringComparison.OrdinalIgnoreCase)))
        {
            requirements.UnionWith(Requirements(File.ReadAllLines(file)));
        }

        Assert.Contains("UIF-210", requirements);                   // a rule line (layout.md)
        Assert.Contains("UIF-321", requirements);                   // a table rule (layout.md §13)
        Assert.DoesNotContain("UIF-1001", requirements);            // phase8-controls.md is excluded

        var problems = Problems(requirements, CoveredIds(TestsDirectory), RequirementCoverageTests.ReadExempt(ExemptPath));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>U-132: <see cref="UiReq_Coverage"/> is a <c>Category=Spec</c> test (TESTING.md) whose only skip has the UIF-1203 reason.</summary>
    [Fact]
    [Trait("Req", "UIF-1203")]
    public void UiReq_Coverage_IsSpecAndSkipsOnlyWithoutDocs()
    {
        var method = typeof(UiReqCoverageTests).GetMethod(nameof(UiReq_Coverage))!;
        var categories = method.GetCustomAttributes(typeof(TraitAttribute), true).Cast<TraitAttribute>()
            .Concat(typeof(UiReqCoverageTests).GetCustomAttributes(typeof(TraitAttribute), true).Cast<TraitAttribute>())
            .Where(t => t.Name == "Category").Select(t => t.Value).ToList();

        Assert.Contains("Spec", categories);
        Assert.Equal("requirement documents not present", SkipReason(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"))));
        Assert.Null(SkipReason(SpecDirectory));
    }

    [Fact]
    [Trait("Req", "UIF-1206")]
    public void UiReq_Coverage_ReadsRuleLinesAndTables()
    {
        string[] doc =
        [
            "# title",
            "UIF-101  A rule line.",
            "| UIF-102 | a table rule |",
            "| `X` | UIF-999 in a later cell is a reference |",
            "See UIF-998 in prose.",
            "UIF-1203 four digits",
            "UIF-101  defined twice",
        ];

        Assert.Equal(["UIF-101", "UIF-102", "UIF-1203"], Requirements(doc));
    }

    [Fact]
    [Trait("Req", "UIF-1206")]
    public void UiReq_Coverage_ReportsMissingAndStaleEntries()
    {
        var requirements = new SortedSet<string>(StringComparer.Ordinal) { "UIF-001", "UIF-002", "UIF-003", "UIF-004" };
        var covered = new HashSet<string> { "UIF-001", "UIF-003" };
        var exempt = new Dictionary<string, string> { ["UIF-003"] = "documentation only", ["UIF-777"] = "x", ["UIF-004"] = "" };

        var problems = Problems(requirements, covered, exempt);

        Assert.Contains(problems, p => p.StartsWith("UIF-002: no test", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("UIF-003: covered by a test", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("UIF-777: listed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("UIF-004: listed without a reason", StringComparison.Ordinal));
        Assert.Equal(4, problems.Count);
    }

    /// <summary>UIF-1203: the reason of the one permitted skip when <paramref name="specDirectory"/> is absent; null when it exists.</summary>
    public static string? SkipReason(string specDirectory) =>
        Directory.Exists(specDirectory) ? null : "requirement documents not present";

    /// <summary>The rule ids defined in <paramref name="lines"/>, each once, in order of definition.</summary>
    public static List<string> Requirements(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. lines.Select(l => Definition().Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).Where(seen.Add)];
    }

    /// <summary>Ids named by <c>// covers:</c> headers of <c>.pas</c> files and by <c>Req</c> traits of <c>.cs</c> files below <paramref name="directory"/>.</summary>
    public static HashSet<string> CoveredIds(string directory)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.EndsWith(".pas", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match header in CoversHeader().Matches(File.ReadAllText(file)))
                {
                    covered.UnionWith(AnyId().Matches(header.Groups[1].Value).Select(m => m.Value));
                }
            }
            else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                covered.UnionWith(ReqTrait().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));
            }
        }

        return covered;
    }

    public static List<string> Problems(IReadOnlySet<string> requirements, IReadOnlySet<string> covered, IReadOnlyDictionary<string, string> exempt)
    {
        var problems = new List<string>();
        foreach (var id in requirements.Order(StringComparer.Ordinal))
        {
            if (!covered.Contains(id) && !exempt.ContainsKey(id))
            {
                problems.Add($"{id}: no test covers it; add it to a '// covers:' header or a Req trait, or list it in coverage-exempt.txt with a reason");
            }
        }

        foreach (var (id, reason) in exempt.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!requirements.Contains(id))
            {
                problems.Add($"{id}: listed in coverage-exempt.txt but not a requirement of docs/spec/ui-framework");
            }
            else if (covered.Contains(id))
            {
                problems.Add($"{id}: covered by a test; remove it from coverage-exempt.txt");
            }
            else if (reason.Length == 0)
            {
                problems.Add($"{id}: listed without a reason");
            }
        }

        return problems;
    }
}
