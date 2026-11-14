using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-906 (testing.md §1): every requirement of an RTL phase that <c>STATUS.md</c> marks complete is covered by a test.
/// Requirements are the ids <c>RTL-nnn</c> that start a rule line or the first cell of a table row in
/// <c>docs/spec/rtl/*.md</c>; an id belongs to the phase named by the nearest enclosing heading (<c>— Phase N</c>), else
/// by the file's introduction, else Phase 2. A test covers an id through the <c>// covers:</c> header of a golden
/// <c>.pas</c> or a <c>[Trait("Req", "RTL-nnn")]</c> in <c>rtl/tests/**/*.cs</c>. Ids without a test are listed in
/// <c>rtl/tests/coverage-exempt.txt</c> with a reason (documentation-only rules); a listed id must be a requirement and
/// must not also be covered, so the list only shrinks.
/// </summary>
[Trait("Phase", "2")]
public sealed partial class RequirementCoverageTests
{
    [GeneratedRegex(@"^(?:\|\s*)?(RTL-\d{3})\b")]
    private static partial Regex Definition();

    [GeneratedRegex(@"^(#{1,6})\s")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\bPhase (\d+)")]
    private static partial Regex PhaseMarker();

    [GeneratedRegex(@"\*\*Phase (\d+) complete\*\*")]
    private static partial Regex CompletePhase();

    [GeneratedRegex(@"^\s*//\s*covers:(.*)$", RegexOptions.Multiline)]
    private static partial Regex CoversHeader();

    [GeneratedRegex(@"Trait\(\s*""Req""\s*,\s*""(RTL-\d{3})""\s*\)")]
    private static partial Regex ReqTrait();

    [GeneratedRegex(@"RTL-\d{3}")]
    private static partial Regex AnyId();

    public static string SpecDirectory => Path.Combine(RtlSetup.RepositoryRoot, "docs", "spec", "rtl");

    public static string ExemptPath => Path.Combine(RtlSetup.TestsDirectory, "coverage-exempt.txt");

    /// <summary>Tracked list of complete phases (the plans, <c>docs/</c> and <c>STATUS.md</c>, are not in the repository).</summary>
    public static string PhasesPath => Path.Combine(RtlSetup.TestsDirectory, "coverage-phases.txt");

    [Fact]
    [Trait("Category", "Spec")]
    [Trait("Req", "RTL-906")]
    public void Req_Coverage()
    {
        // docs/ is git-ignored working material: without it this check cannot run (TESTING.md, Category=Spec).
        if (!Directory.Exists(SpecDirectory))
        {
            Assert.Skip(SpecDirectory + " is not present (plans are not part of the repository)");
        }

        var statusPath = PhasesPath;
        Assert.True(File.Exists(statusPath), statusPath + " does not exist");

        var requirements = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(SpecDirectory, "*.md").Order(StringComparer.Ordinal))
        {
            foreach (var (id, phase) in Requirements(File.ReadAllLines(file)))
            {
                requirements.TryAdd(id, phase);
            }
        }

        // the phase assignment reads the documents as intended
        Assert.Equal(2, requirements["RTL-258"]);
        Assert.Equal(2, requirements["RTL-630"]);
        Assert.Equal(4, requirements["RTL-663"]);
        Assert.Equal(7, requirements["RTL-670"]);

        var problems = Problems(requirements, CompletePhases(File.ReadAllText(statusPath)), CoveredIds(RtlSetup.TestsDirectory),
            ReadExempt(ExemptPath));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    [Trait("Req", "RTL-906")]
    public void Req_Coverage_ReadsRulesTablesAndPhases()
    {
        string[] doc =
        [
            "# unit",
            "Intro text. Phase 3; milestone M2.5.",
            "RTL-101  A rule line.",
            "| RTL-102 | a table rule |",
            "| `X` | RTL-999 in a later cell is a reference, not a rule |",
            "See RTL-998 in prose.",
            "## 1. Later — Phase 7, M2.6",
            "RTL-103  phase from the heading",
            "### 1.1 Sub",
            "| RTL-104 | phase inherited from the parent heading |",
            "## 2. Back",
            "RTL-105  phase of the introduction again",
        ];
        var found = Requirements(doc).ToList();
        Assert.Equal(
            new (string, int)[] { ("RTL-101", 3), ("RTL-102", 3), ("RTL-103", 7), ("RTL-104", 7), ("RTL-105", 3) },
            found);
        Assert.Equal(new (string, int)[] { ("RTL-001", 2) }, Requirements(["# no marker", "RTL-001  default phase"]).ToList());
    }

    [Fact]
    [Trait("Req", "RTL-906")]
    public void Req_Coverage_ReportsMissingAndStaleEntries()
    {
        var requirements = new Dictionary<string, int> { ["RTL-001"] = 2, ["RTL-002"] = 2, ["RTL-003"] = 2, ["RTL-004"] = 4 };
        var covered = new HashSet<string> { "RTL-001", "RTL-003" };
        var exempt = new Dictionary<string, string> { ["RTL-003"] = "documentation only", ["RTL-777"] = "x", ["RTL-004"] = "" };

        Assert.Empty(Problems(requirements, new HashSet<int> { 1 }, covered, new Dictionary<string, string>()));
        var problems = Problems(requirements, new HashSet<int> { 1, 2 }, covered, exempt);
        Assert.Contains(problems, p => p.StartsWith("RTL-002: no test", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("RTL-003: covered by a test", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("RTL-777: listed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("RTL-004: listed without a reason", StringComparison.Ordinal));
        Assert.Equal(4, problems.Count);
        Assert.Equal(new[] { 1, 4 }, CompletePhases("**Phase 1 complete** (gate) ... **Phase 4 complete**").Order());
    }

    /// <summary>Each rule id defined in <paramref name="lines"/> with its phase (first definition wins).</summary>
    public static IEnumerable<(string Id, int Phase)> Requirements(IReadOnlyList<string> lines)
    {
        var intro = 2;
        var seenHeading2 = false;
        var introSet = false;
        var stack = new List<(int Level, int Phase)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var heading = Heading().Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                if (level >= 2)
                {
                    seenHeading2 = true;
                }

                while (stack.Count > 0 && stack[^1].Level >= level)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                var marker = PhaseMarker().Match(line);
                var parent = stack.Count > 0 ? stack[^1].Phase : intro;
                stack.Add((level, marker.Success ? int.Parse(marker.Groups[1].Value) : parent));
                continue;
            }

            if (!seenHeading2 && !introSet && PhaseMarker().Match(line) is { Success: true } introMarker)
            {
                intro = int.Parse(introMarker.Groups[1].Value);
                introSet = true;
                for (var i = 0; i < stack.Count; i++)
                {
                    stack[i] = (stack[i].Level, intro);
                }
            }

            var definition = Definition().Match(line);
            if (definition.Success && seen.Add(definition.Groups[1].Value))
            {
                yield return (definition.Groups[1].Value, stack.Count > 0 ? stack[^1].Phase : intro);
            }
        }
    }

    public static HashSet<int> CompletePhases(string status) =>
        [.. CompletePhase().Matches(status).Select(m => int.Parse(m.Groups[1].Value))];

    /// <summary>Ids named by <c>// covers:</c> headers of <c>.pas</c> files and by <c>Req</c> traits of <c>.cs</c> files.</summary>
    public static HashSet<string> CoveredIds(string testsDirectory)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(testsDirectory, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(testsDirectory, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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

    /// <summary><c>RTL-nnn reason</c> per line; <c>#</c> starts a comment line.</summary>
    public static Dictionary<string, string> ReadExempt(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var space = line.IndexOfAny([' ', '\t']);
            var id = space < 0 ? line : line[..space];
            result[id] = space < 0 ? "" : line[space..].Trim();
        }

        return result;
    }

    public static List<string> Problems(IReadOnlyDictionary<string, int> requirements, IReadOnlySet<int> completePhases,
        IReadOnlySet<string> covered, IReadOnlyDictionary<string, string> exempt)
    {
        var problems = new List<string>();
        foreach (var (id, phase) in requirements.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (!completePhases.Contains(phase))
            {
                continue;
            }

            if (!covered.Contains(id) && !exempt.ContainsKey(id))
            {
                problems.Add($"{id}: no test covers it (Phase {phase} is complete); add it to a '// covers:' header or a Req trait");
            }
        }

        foreach (var (id, reason) in exempt.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!requirements.ContainsKey(id))
            {
                problems.Add($"{id}: listed in coverage-exempt.txt but not a requirement of docs/spec/rtl");
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
