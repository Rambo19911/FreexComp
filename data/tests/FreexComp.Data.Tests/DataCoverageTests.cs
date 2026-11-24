using System.Text.RegularExpressions;

namespace FreexComp.Data.Tests;

/// <summary>
/// Requirement coverage of the data area (task A-073): every rule of <c>docs/spec/data/*.md</c> in Phase 7 scope
/// (<c>DAT-001 … DAT-799</c>; the ids from 800 are Phase 8 cards) is named by the <c>// covers:</c> header of a golden
/// program or by a <c>[Trait("Req", "DAT-nnn")]</c> of a test of this project, or is listed with a category and a reason in
/// <c>data/tests/coverage-exempt.txt</c>. A listed id must be a rule and must not also be covered, so the list only
/// shrinks. A rule is an id in the first cell of a table row, at the start of a rule line, or in the parentheses of a
/// heading or of a "Design choice" paragraph.
/// </summary>
public sealed partial class DataCoverageTests
{
    /// <summary>The exemption categories; all but <c>informative</c> name the task that will bring the test.</summary>
    private static readonly string[] Categories = ["pending", "design-time", "crud-sample", "informative"];

    [GeneratedRegex(@"^(?:\|\s*)?(DAT-\d{3})\b")]
    private static partial Regex Definition();

    [GeneratedRegex(@"^(?:#{1,6}\s.*|Design choice\s*)\(([^)]*DAT-\d{3}[^)]*)\)")]
    private static partial Regex ParenthesisedDefinition();

    [GeneratedRegex(@"DAT-\d{3}")]
    private static partial Regex AnyId();

    [GeneratedRegex(@"Trait\(\s*""Req""\s*,\s*""(DAT-\d{3})""\s*\)")]
    private static partial Regex ReqTrait();

    [GeneratedRegex(@"^A-\d{3}\b")]
    private static partial Regex TaskId();

    public static string SpecDirectory => Path.Combine(DataCorpus.RepositoryRoot, "docs", "spec", "data");

    public static string ExemptPath => Path.Combine(DataCorpus.RepositoryRoot, "data", "tests", "coverage-exempt.txt");

    [Fact]
    [Trait("Category", "Spec")]
    public void AllPhase7RulesCovered()
    {
        // docs/ is git-ignored working material: without it this check cannot run (TESTING.md, Category=Spec).
        if (!Directory.Exists(SpecDirectory))
        {
            Assert.Skip(SpecDirectory + " is not present (plans are not part of the repository)");
        }

        var rules = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(SpecDirectory, "*.md").Order(StringComparer.Ordinal))
        {
            rules.UnionWith(Phase7Rules(File.ReadAllLines(file)));
        }

        // every way a rule is written in the documents is recognised, and the cards are left out
        Assert.Contains("DAT-001", rules);      // "Design choice (DAT-001)"
        Assert.Contains("DAT-034", rules);      // a rule line
        Assert.Contains("DAT-130", rules);      // a heading
        Assert.Contains("DAT-235", rules);      // a table row
        Assert.DoesNotContain("DAT-812", rules);
        Assert.True(rules.Count >= 200, $"Only {rules.Count} rule ids found in {SpecDirectory}");

        Assert.True(File.Exists(ExemptPath), ExemptPath + " does not exist");
        var problems = Problems(rules, CoveredIds(), Exemptions(File.ReadAllLines(ExemptPath)));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The check reads rule lines, table rows and headings, and ignores references and cards.</summary>
    [Fact]
    public void Check_ReadsRules()
    {
        string[] document =
        [
            "# Title",
            "Requirement IDs: `DAT-100 … DAT-199`.",
            "Design choice (DAT-101): a paragraph rule.",
            "| ID | Rule |",
            "| DAT-102 | A table rule that mentions DAT-198. |",
            "| `X` | DAT-197 in a later cell is a reference |",
            "DAT-103  A rule line.",
            "See DAT-196 in prose (DAT-195).",
            "### 6.1 A heading (DAT-104, DAT-105; added later)",
            "| DAT-102 | repeated |",
            "| DAT-860 | A Phase 8 card. |",
        ];

        Assert.Equal(["DAT-101", "DAT-102", "DAT-103", "DAT-104", "DAT-105"], Phase7Rules(document));
    }

    /// <summary>The check sees a gap, a stale exemption and a covered id that is no rule; bad exemption lines are refused.</summary>
    [Fact]
    public void Check_ReportsMissingAndStaleEntries()
    {
        var rules = new SortedSet<string>(StringComparer.Ordinal) { "DAT-001", "DAT-002", "DAT-003", "DAT-004" };
        var covered = new HashSet<string>(StringComparer.Ordinal) { "DAT-001", "DAT-003", "DAT-555", "DAT-801" };
        var exemptions = Exemptions(
        [
            "# comment",
            "",
            "DAT-003 | informative | a note",
            "DAT-004 | pending | A-055 comes with the edit control",
            "DAT-777 | crud-sample | A-068 the sample",
        ]);

        var problems = Problems(rules, covered, exemptions);

        Assert.Contains(problems, p => p.StartsWith("DAT-002: no test", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("DAT-003: covered by a test", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("DAT-777: listed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("DAT-555: named by a test", StringComparison.Ordinal));
        Assert.Equal(4, problems.Count);
        Assert.Empty(Problems(rules, new HashSet<string> { "DAT-001", "DAT-002", "DAT-003" }, Exemptions(["DAT-004 | informative | a note"])));

        // no "id | category | reason", an unknown category, an empty reason, a task category without its task, a repeated id
        Assert.Throws<FormatException>(() => Exemptions(["DAT-004 informative"]));
        Assert.Throws<FormatException>(() => Exemptions(["DAT-004 | whatever | reason"]));
        Assert.Throws<FormatException>(() => Exemptions(["DAT-004 | informative | "]));
        Assert.Throws<FormatException>(() => Exemptions(["DAT-004 | pending | later"]));
        Assert.Throws<FormatException>(() => Exemptions(["DAT-004 | informative | a", "DAT-004 | informative | b"]));
    }

    /// <summary>The rule ids below 800 that a document defines, in document order, each once.</summary>
    public static List<string> Phase7Rules(IEnumerable<string> lines)
    {
        var ids = new List<string>();
        foreach (var line in lines)
        {
            IEnumerable<string> found = [];
            if (Definition().Match(line) is { Success: true } definition)
            {
                found = [definition.Groups[1].Value];
            }
            else if (ParenthesisedDefinition().Match(line) is { Success: true } parenthesised)
            {
                found = AnyId().Matches(parenthesised.Groups[1].Value).Select(m => m.Value);
            }

            foreach (var id in found)
            {
                if (IsPhase7(id) && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static bool IsPhase7(string id) => int.Parse(id.AsSpan(4), System.Globalization.CultureInfo.InvariantCulture) < 800;

    /// <summary>Ids named by the <c>// covers:</c> headers of the golden programs and by <c>Req</c> traits of this project's sources.</summary>
    public static HashSet<string> CoveredIds()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var program in DataCorpus.Discover())
        {
            covered.UnionWith(DataCorpus.ReadHeader(File.ReadAllText(program.SourcePath)).Covers.Where(id => AnyId().IsMatch(id)));
        }

        var project = Path.Combine(DataCorpus.RepositoryRoot, "data", "tests", "FreexComp.Data.Tests");
        foreach (var file in Directory.EnumerateFiles(project, "*.cs"))
        {
            covered.UnionWith(ReqTrait().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));
        }

        return covered;
    }

    /// <summary>The exemption list: one <c>id | category | reason</c> per line; empty lines and <c>#</c> lines are skipped.</summary>
    public static Dictionary<string, (string Category, string Reason)> Exemptions(IEnumerable<string> lines)
    {
        var exemptions = new Dictionary<string, (string Category, string Reason)>(StringComparer.Ordinal);
        foreach (var line in lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            var cells = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (cells.Length != 3 || !Regex.IsMatch(cells[0], @"^DAT-\d{3}$") || !Categories.Contains(cells[1]) || cells[2].Length == 0
                || (cells[1] != "informative" && !TaskId().IsMatch(cells[2]))
                || !exemptions.TryAdd(cells[0], (cells[1], cells[2])))
            {
                throw new FormatException("Bad exemption line: " + line);
            }
        }

        return exemptions;
    }

    public static List<string> Problems(IReadOnlySet<string> rules, IReadOnlySet<string> covered,
        IReadOnlyDictionary<string, (string Category, string Reason)> exemptions)
    {
        var problems = new List<string>();
        foreach (var id in rules)
        {
            if (!covered.Contains(id) && !exemptions.ContainsKey(id))
            {
                problems.Add($"{id}: no test covers it; add it to a '// covers:' header or a Req trait, or to coverage-exempt.txt with a reason");
            }
        }

        foreach (var id in exemptions.Keys.Order(StringComparer.Ordinal))
        {
            if (!rules.Contains(id))
            {
                problems.Add($"{id}: listed in coverage-exempt.txt but not a Phase 7 rule of docs/spec/data");
            }
            else if (covered.Contains(id))
            {
                problems.Add($"{id}: covered by a test; remove it from coverage-exempt.txt");
            }
        }

        foreach (var id in covered.Where(IsPhase7).Order(StringComparer.Ordinal))
        {
            if (!rules.Contains(id))
            {
                problems.Add($"{id}: named by a test but not a Phase 7 rule of docs/spec/data");
            }
        }

        return problems;
    }
}
