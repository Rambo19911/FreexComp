using System.Reflection;
using System.Text.RegularExpressions;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// DBG-332: every public rule of the debug-session interface document (the first cell of a rule table row,
/// <c>DBG-4xx</c>) has at least one test in this assembly that carries the rule's id as a <c>Req</c> trait, on the test
/// method or on its class. There is no exemption list: a rule of the interface without a test fails this test.
/// The same holds for the Phase 6 rows of the feature document (<c>DBG-2xx</c>), except that a row the debugger library
/// cannot test - an informative note, a rule another area implements - is listed with its reason in
/// <c>Fixtures/coverage/features-p6-exemptions.txt</c>.
/// </summary>
[Trait("Req", "DBG-332")]
public sealed partial class RequirementCoverageTests
{
    [GeneratedRegex(@"^\|\s*(DBG-4\d\d)\s*\|", RegexOptions.Multiline)]
    private static partial Regex RuleRow();

    [Fact]
    public void AllInterfaceRulesHaveTests()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "interfaces", "debug-session.md");
        Assert.True(File.Exists(path), "Interface document not found: " + path);
        var rules = RuleIds(File.ReadAllText(path));
        // The document has two rule tables (calls, events); far fewer ids means the tables were not recognised.
        Assert.True(rules.Count >= 20, $"Only {rules.Count} rule ids found in {path}: {string.Join(", ", rules)}");

        var missing = Uncovered(rules, CoveredIds(typeof(RequirementCoverageTests).Assembly));

        Assert.True(
            missing.Count == 0,
            "Interface rules without a test that carries [Trait(\"Req\", \"<id>\")]: " + string.Join(", ", missing));
    }

    /// <summary>The check itself must see a gap: a rule row whose id no test carries is reported, a covered one is not.</summary>
    [Fact]
    public void Check_RuleWithoutTest_IsReported()
    {
        const string document = """
            | ID | Rule |
            |---|---|
            | DBG-401 | A covered rule that mentions DBG-498 in its text. |
            | DBG-499 | A rule nobody tests. |
            Prose that names DBG-497 is not a rule row.
            """;

        var rules = RuleIds(document);
        var missing = Uncovered(rules, CoveredIds(typeof(RequirementCoverageTests).Assembly));

        Assert.Equal(["DBG-401", "DBG-499"], rules);
        Assert.Equal(["DBG-499"], missing);
    }

    // ---- the Phase 6 rows of the feature document ----

    private static readonly string[] ExemptionCategories = ["informative", "ide", "other-area"];
    private const string IdeReasonPrefix = "implemented and tested in ide";

    [GeneratedRegex(@"^\|\s*(DBG-\d{3})\s*\|")]
    private static partial Regex FeatureRow();

    [Fact]
    public void AllPhase6FeatureRulesHaveTestsOrAnExemption()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "spec", "debugger", "features.md");
        Assert.True(File.Exists(path), "Feature document not found: " + path);
        var rules = Phase6FeatureIds(File.ReadAllLines(path));
        Assert.True(rules.Count >= 50, $"Only {rules.Count} Phase 6 rule ids found in {path}: {string.Join(", ", rules)}");
        var exemptionPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "coverage", "features-p6-exemptions.txt");
        var exemptions = File.Exists(exemptionPath) ? Exemptions(File.ReadAllLines(exemptionPath)) : [];
        var covered = CoveredIds(typeof(RequirementCoverageTests).Assembly);

        var missing = rules.Where(id => !covered.Contains(id) && !exemptions.ContainsKey(id)).ToList();
        // An exemption is for a Phase 6 row that has no test; any other entry is out of date.
        var stale = exemptions.Keys.Where(id => !rules.Contains(id) || covered.Contains(id)).ToList();

        Assert.True(
            missing.Count == 0,
            "Phase 6 feature rules without a test that carries [Trait(\"Req\", \"<id>\")] and without an exemption: " + string.Join(", ", missing));
        Assert.True(stale.Count == 0, "Exemptions for ids that are no Phase 6 feature rule or that have a test: " + string.Join(", ", stale));
    }

    /// <summary>The check itself: phase tags of sections and rows are honoured, an exempt row is not reported, an uncovered one is.</summary>
    [Fact]
    public void Check_Phase6Rows_AndExemptions()
    {
        string[] document =
        [
            "## 1. A section [P6]",
            "| ID | Rule |",
            "|---|---|",
            "| DBG-401 | A covered rule. |",
            "### 1.1 A subsection without a tag",
            "| DBG-298 | A rule nobody tests. |",
            "| DBG-297 | An informative note. |",
            "## 2. Mixed",
            "| DBG-296 | **Something [P6]:** by row tag. |",
            "| DBG-295 | **Something else [P8+]:** later. |",
            "## 3. Cards [P6]",
            "| DBG-294 | **Later [P8+]:** excluded by its own tag. |",
            "## 4. Phase 8+ cards",
            "| DBG-800 | A card. |",
        ];
        var exemptions = Exemptions(
        [
            "# comment",
            "",
            "DBG-297 | informative | a note, nothing to test",
            "DBG-296 | ide | implemented and tested in ide (I-000)",
        ]);

        var rules = Phase6FeatureIds(document);
        var covered = CoveredIds(typeof(RequirementCoverageTests).Assembly);

        Assert.Equal(["DBG-401", "DBG-298", "DBG-297", "DBG-296"], rules);
        Assert.Equal(["DBG-298"], rules.Where(id => !covered.Contains(id) && !exemptions.ContainsKey(id)));
        Assert.Equal(("ide", "implemented and tested in ide (I-000)"), exemptions["DBG-296"]);
        // A line that is no "id | category | reason", an unknown category, an empty reason, an "ide" entry that does
        // not say so, and a repeated id are refused.
        Assert.Throws<FormatException>(() => Exemptions(["DBG-297 informative"]));
        Assert.Throws<FormatException>(() => Exemptions(["DBG-297 | whatever | reason"]));
        Assert.Throws<FormatException>(() => Exemptions(["DBG-297 | informative | "]));
        Assert.Throws<FormatException>(() => Exemptions(["DBG-297 | ide | somewhere else"]));
        Assert.Throws<FormatException>(() => Exemptions(["DBG-297 | informative | a", "DBG-297 | informative | b"]));
    }

    /// <summary>
    /// The ids of the Phase 6 rule rows of the feature document, in document order: rows of a section whose
    /// <c>##</c> heading carries <c>[P6]</c> (unless the row itself says <c>[P8+]</c>), and rows that carry <c>[P6]</c>.
    /// </summary>
    private static List<string> Phase6FeatureIds(IEnumerable<string> lines)
    {
        var ids = new List<string>();
        var sectionIsPhase6 = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
                sectionIsPhase6 = line.Contains("[P6]", StringComparison.Ordinal);
            else if (FeatureRow().Match(line) is { Success: true } row
                && (line.Contains("[P6]", StringComparison.Ordinal) || (sectionIsPhase6 && !line.Contains("[P8+]", StringComparison.Ordinal)))
                && !ids.Contains(row.Groups[1].Value))
                ids.Add(row.Groups[1].Value);
        }

        return ids;
    }

    /// <summary>The exemption list: one <c>id | category | reason</c> per line; empty lines and <c>#</c> lines are skipped.</summary>
    private static Dictionary<string, (string Category, string Reason)> Exemptions(IEnumerable<string> lines)
    {
        var exemptions = new Dictionary<string, (string Category, string Reason)>(StringComparer.Ordinal);
        foreach (var line in lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            var cells = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (cells.Length != 3 || !Regex.IsMatch(cells[0], @"^DBG-\d{3}$") || !ExemptionCategories.Contains(cells[1]) || cells[2].Length == 0
                || (cells[1] == "ide" && !cells[2].StartsWith(IdeReasonPrefix, StringComparison.Ordinal))
                || !exemptions.TryAdd(cells[0], (cells[1], cells[2])))
                throw new FormatException("Bad exemption line: " + line);
        }

        return exemptions;
    }

    /// <summary>The rule ids of a document: the ids in the first cell of table rows, in document order.</summary>
    private static List<string> RuleIds(string document) =>
        [.. RuleRow().Matches(document).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    private static List<string> Uncovered(IEnumerable<string> rules, IReadOnlySet<string> covered) =>
        [.. rules.Where(id => !covered.Contains(id))];

    /// <summary>The <c>Req</c> trait values of every test method of an assembly, including those of its class.</summary>
    private static HashSet<string> CoveredIds(Assembly assembly)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes())
        {
            var tests = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any())
                .ToList();
            if (tests.Count == 0)
                continue;
            covered.UnionWith(ReqTraits(type));
            foreach (var test in tests)
                covered.UnionWith(ReqTraits(test));
        }

        return covered;
    }

    private static IEnumerable<string> ReqTraits(MemberInfo member) =>
        from attribute in member.GetCustomAttributesData()
        where attribute.AttributeType == typeof(TraitAttribute) && attribute.ConstructorArguments.Count == 2
        where attribute.ConstructorArguments[0].Value is "Req"
        select attribute.ConstructorArguments[1].Value as string;

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreexComp.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}
