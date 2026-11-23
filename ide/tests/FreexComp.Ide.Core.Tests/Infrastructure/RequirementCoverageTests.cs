using System.Text.RegularExpressions;

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>
/// IDE-TEST-030: every requirement id of the implemented phases is referenced by a test. Ids come from the rule-table
/// rows (<c>| IDE-… |</c> / <c>| UI-… |</c>) of the spec files listed in <c>TestData/coverage-scope.txt</c>; a section
/// whose heading names only later phases (<c>(P3)</c>, <c>P5</c>, <c>P8+</c> …) is out of scope with its subsections, and
/// so is a row whose rule text starts with a later-phase marker or that has a later-phase cell. A test references an id
/// through <c>[Trait("Req", "…")]</c> or <c>[Requirement("…")]</c> in <c>ide/tests/**/*.cs</c>, or through a
/// <c># name: &lt;id&gt; …</c> case of <c>TestData/editor-ops</c> (run by <c>EditorOperationsTests.DataDrivenCases</c>).
/// Ids without a test are listed in <c>coverage-exempt.txt</c> (structural, superseded, later phase, not checkable
/// headless) or <c>coverage-pending.txt</c> (behaviour without a test yet), each with a reason; an id may not be in a list
/// once a test covers it, so both lists only shrink.
/// </summary>
[Trait("Phase", "1")]
public sealed partial class RequirementCoverageTests
{
    [GeneratedRegex(@"^\|\s*((?:IDE|UI)-[A-Z0-9]+-\d+[a-z]?)\s*\|(.*)$")]
    private static partial Regex RuleRow();

    [GeneratedRegex(@"\bP([1-9])\+?(?![0-9])")]
    private static partial Regex PhaseMarker();

    [GeneratedRegex(@"^\(?\*{0,2}P([1-9])\+?(?![0-9])")]
    private static partial Regex LeadingPhase();

    [GeneratedRegex(@"(?:Trait\(\s*""Req""\s*,|Requirement\()\s*""([A-Z][A-Z0-9]*-[A-Z0-9-]+)""\s*\)")]
    private static partial Regex TestReference();

    [GeneratedRegex(@"^# name: ((?:IDE|UI)-[A-Z0-9]+-\d+[a-z]?)\b", RegexOptions.Multiline)]
    private static partial Regex CaseReference();

    [Fact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "IDE-TEST-030")]
    public void ImplementedPhaseRequirementsHaveTests()
    {
        var root = SpecTables.RepoRootPath();
        var testData = Path.Combine(root, "ide", "tests", "TestData");
        var (phase, files) = ReadScope(Path.Combine(testData, "coverage-scope.txt"));
        var inScope = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            inScope.UnionWith(ScopedIds(File.ReadAllLines(SpecTables.DocPath(file)), phase));
        }

        // I-207: the scope follows the product's implemented phase (Phase 5: the shell, build/run and the designer windows).
        Assert.Equal(Composition.ServiceGraph.ImplementedPhase, phase);
        Assert.Contains("IDE-P1-056", inScope);
        Assert.Contains("IDE-P3-001", inScope); // phase3-build-run.md
        Assert.Contains("UI-TW-400", inScope); // tool-windows.md §6 Messages (P3)
        Assert.DoesNotContain("UI-TW-800", inScope); // tool-windows.md §10 Breakpoints (P6)

        var covered = CoveredIds(Path.Combine(root, "ide", "tests"));
        var exempt = ReadList(Path.Combine(testData, "coverage-exempt.txt")).Where(e => !IsDesignerId(e.Key))
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal); // designer ids: AllDesignerRequirementsHaveTests
        var pending = ReadList(Path.Combine(testData, "coverage-pending.txt"));

        var problems = new List<string>();
        foreach (var id in inScope)
        {
            var kinds = (covered.Contains(id) ? 1 : 0) + (exempt.ContainsKey(id) ? 1 : 0) + (pending.ContainsKey(id) ? 1 : 0);
            if (kinds == 0)
                problems.Add($"{id}: no test references it (add [Trait(\"Req\", \"{id}\")] to the test that verifies it)");
            else if (covered.Contains(id) && kinds > 1)
                problems.Add($"{id}: covered by a test; remove it from coverage-{(exempt.ContainsKey(id) ? "exempt" : "pending")}.txt");
            else if (kinds > 1)
                problems.Add($"{id}: listed in both coverage-exempt.txt and coverage-pending.txt");
        }

        foreach (var id in exempt.Keys.Concat(pending.Keys).Where(id => !inScope.Contains(id)))
            problems.Add($"{id}: listed but not an in-scope requirement id");
        foreach (var (id, reason) in exempt.Concat(pending).Where(e => e.Value.Length == 0))
            problems.Add($"{id}: listed without a reason");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [GeneratedRegex(@"^\|\s*(DSG-[A-Z0-9]+-\d+[a-z]?|UI-DSG-\d+[a-z]?)\s*\|")]
    private static partial Regex DesignerRuleRow();

    /// <summary><c>&lt;category&gt;: &lt;reason&gt;</c>; the categories of the IDE list plus <c>pending &lt;task&gt;</c> (D-087).</summary>
    [GeneratedRegex(@"^(?:structural|superseded|later phase|not checkable headless|platform|principle|process|definition|absence|pending [A-Z]-\d{3}): \S")]
    private static partial Regex DesignerExemption();

    private static bool IsDesignerId(string id) => id.StartsWith("DSG-", StringComparison.Ordinal) || id.StartsWith("UI-DSG-", StringComparison.Ordinal);

    /// <summary>
    /// D-087, DSG-TEST-001, DSG-TEST-003: every requirement id of the designer specification — the rule-table rows
    /// <c>| DSG-… |</c> of <c>docs/spec/designer/*.md</c> and <c>| UI-DSG-… |</c> of <c>docs/spec/ui/designer.md</c> — is
    /// referenced by a test (<c>[Trait("Req", "…")]</c> in <c>ide/tests/**/*.cs</c>) or listed in
    /// <c>TestData/coverage-exempt.txt</c> with a category and a reason (testing.md lead decisions 2026-11-23). An exempt id
    /// that a test covers fails ("remove it"); <c>pending &lt;task&gt;</c> marks an id owned by an open task.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Category", "Spec")]
    [Trait("Req", "DSG-TEST-001")]
    [Trait("Req", "DSG-TEST-003")]
    public void AllDesignerRequirementsHaveTests()
    {
        var files = Directory.EnumerateFiles(Path.GetDirectoryName(SpecTables.DocPath(Path.Combine("spec", "designer", "README.md")))!, "*.md")
            .Append(SpecTables.DocPath(Path.Combine("spec", "ui", "designer.md")));
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var inFence = false;
            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                    inFence = !inFence;
                if (!inFence && DesignerRuleRow().Match(line) is { Success: true } row)
                    ids.Add(row.Groups[1].Value);
            }
        }

        Assert.Contains("DSG-SUR-198", ids);
        Assert.Contains("UI-DSG-082", ids);
        var root = SpecTables.RepoRootPath();
        var covered = CoveredIds(Path.Combine(root, "ide", "tests"));
        var exempt = ReadList(Path.Combine(root, "ide", "tests", "TestData", "coverage-exempt.txt")).Where(e => IsDesignerId(e.Key))
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        var problems = DesignerProblems(ids, covered, exempt);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    internal static List<string> DesignerProblems(IReadOnlySet<string> ids, IReadOnlySet<string> covered, IReadOnlyDictionary<string, string> exempt)
    {
        var problems = new List<string>();
        var missing = ids.Where(id => !covered.Contains(id) && !exempt.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            problems.Add($"{missing.Count} of {ids.Count} designer requirement ids have no test: {string.Join(", ", missing)}");
        foreach (var (id, reason) in exempt)
        {
            if (covered.Contains(id))
                problems.Add($"{id}: covered by a test; remove it from coverage-exempt.txt");
            else if (!ids.Contains(id))
                problems.Add($"{id}: listed in coverage-exempt.txt but not a designer requirement id");
            if (!DesignerExemption().IsMatch(reason))
                problems.Add($"{id}: coverage-exempt.txt needs \"<category>: <reason>\" (got \"{reason}\")");
        }

        return problems;
    }

    /// <summary><c>phase N</c> line plus one spec path (relative to <c>docs/</c>) per line; <c>#</c> starts a comment.</summary>
    private static (int Phase, List<string> Files) ReadScope(string path)
    {
        int phase = 0;
        var files = new List<string>();
        foreach (var line in Lines(path))
        {
            if (line.StartsWith("phase ", StringComparison.Ordinal))
                phase = int.Parse(line[6..], System.Globalization.CultureInfo.InvariantCulture);
            else
                files.Add(line);
        }

        Assert.True(phase > 0, "coverage-scope.txt names the implemented phase");
        return (phase, files);
    }

    /// <summary><c>&lt;id&gt; &lt;reason&gt;</c> per line.</summary>
    private static Dictionary<string, string> ReadList(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Lines(path))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            var id = space < 0 ? line : line[..space];
            Assert.True(entries.TryAdd(id, space < 0 ? "" : line[(space + 1)..].Trim()), $"{Path.GetFileName(path)}: {id} listed twice");
        }

        return entries;
    }

    private static IEnumerable<string> Lines(string path) =>
        File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));

    private static bool IsLater(IEnumerable<int> phases, int implemented)
    {
        var list = phases.ToList();
        return list.Count > 0 && list.All(p => p > implemented);
    }

    internal static IEnumerable<string> ScopedIds(string[] lines, int implemented)
    {
        var sections = new List<(int Level, bool Later)>();
        bool inFence = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                inFence = !inFence;
            if (inFence)
                continue;

            if (line.StartsWith('#'))
            {
                var level = line.TakeWhile(c => c == '#').Count();
                sections.RemoveAll(s => s.Level >= level);
                sections.Add((level, IsLater(PhaseMarker().Matches(line).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)), implemented)));
                continue;
            }

            if (sections.Any(s => s.Later) || RuleRow().Match(line) is not { Success: true } row)
                continue;

            var cells = row.Groups[2].Value.Split('|').Select(c => c.Trim().Trim('*')).ToList();
            var leading = LeadingPhase().Match(cells[0]);
            if (leading.Success && int.Parse(leading.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > implemented)
                continue;
            if (cells.Skip(1).Any(c => PhaseMarker().Match(c) is { Success: true } m && m.Value.Length == c.Length
                && int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > implemented))
                continue;

            yield return row.Groups[1].Value;
        }
    }

    private static HashSet<string> CoveredIds(string testsRoot)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var parts = file.Split(Path.DirectorySeparatorChar);
            if (parts.Contains("obj") || parts.Contains("bin"))
                continue;
            covered.UnionWith(TestReference().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(testsRoot, "TestData", "editor-ops"), "*.txt"))
            covered.UnionWith(CaseReference().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));

        return covered;
    }
}
