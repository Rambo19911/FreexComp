using System.Reflection;
using System.Text.RegularExpressions;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// DBG-332: every public rule of the debug-session interface document (the first cell of a rule table row,
/// <c>DBG-4xx</c>) has at least one test in this assembly that carries the rule's id as a <c>Req</c> trait, on the test
/// method or on its class. There is no exemption list: a rule of the interface without a test fails this test.
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
