using System.Text;
using System.Text.Json;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>The unit alias table <c>rtl/src/unit-aliases.json</c> (RTL-007; format LANG-533, validity LANG-534).</summary>
[Trait("Phase", "2")]
public sealed class UnitAliasTests
{
    public static string TablePath => Path.Combine(RtlSetup.RepositoryRoot, "rtl", "src", "unit-aliases.json");

    /// <summary>
    /// RTL-007: the table is valid per LANG-534, keys are ordinally sorted, every shipped unit except <c>System</c> has
    /// exactly one alias built by the prefix rule, and every alias target is a shipped unit.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "RTL-007")]
    [Trait("Req", "LANG-534")]
    public void Aliases_Complete()
    {
        Assert.True(File.Exists(TablePath), TablePath + " does not exist");
        var problems = new List<string>();
        var aliases = Validate(File.ReadAllBytes(TablePath), problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        var keys = aliases.Select(a => a.Alias).ToList();
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);

        // Shipped units: canonical name = file name; prefix per RTL-007 (null = ui-framework: any one scope prefix).
        string?[] prefixes = ["System.", "Winapi.", null, "Data."];
        var units = DefFileTests.UnitDirectories.Zip(prefixes)
            .Where(d => Directory.Exists(d.First))
            .SelectMany(d => Directory.EnumerateFiles(d.First, "*.pas", SearchOption.AllDirectories)
                .Select(f => (Name: Path.GetFileNameWithoutExtension(f), Prefix: d.Second)))
            .Where(u => u.Name != "System")
            .ToList();
        Assert.Contains(units, u => u.Name == "Windows");

        foreach (var (name, prefix) in units)
        {
            var mine = aliases.Where(a => a.Canonical == name).Select(a => a.Alias).ToList();
            if (mine.Count != 1)
            {
                problems.Add($"unit '{name}' has {mine.Count} aliases (exactly one expected)");
                continue;
            }

            var expected = prefix is null ? null
                : prefix == "Data." && name.StartsWith("Data.", StringComparison.Ordinal) ? null : prefix + name;
            if (expected is not null ? mine[0] != expected : !mine[0].EndsWith("." + name, StringComparison.Ordinal))
            {
                problems.Add($"unit '{name}': alias '{mine[0]}' does not follow the prefix rule of RTL-007");
            }
        }

        foreach (var (alias, canonical) in aliases.Where(a => !units.Any(u => u.Name == a.Canonical)))
        {
            problems.Add($"alias '{alias}' names '{canonical}', which is not a shipped unit");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The validator rejects each LANG-534 violation.</summary>
    [Theory]
    [Trait("Req", "LANG-534")]
    [InlineData("""[]""")]
    [InlineData("""{ "aliases": {}, "extra": {} }""")]
    [InlineData("""{ "aliases": { "System.Types": 1 } }""")]
    [InlineData("""{ "aliases": { "System.Types": "Ty pes" } }""")]
    [InlineData("""{ "aliases": { "System.Begin": "Types" } }""")]
    [InlineData("""{ "aliases": { "Types": "types" } }""")]
    [InlineData("""{ "aliases": { "A.B": "C", "C": "D" } }""")]
    [InlineData("""{ "aliases": { "A.B": "C", "a.b": "D" } }""")]
    [InlineData("""not json""")]
    public void Aliases_ValidatorRejects(string json)
    {
        var problems = new List<string>();
        Validate(Encoding.UTF8.GetBytes(json), problems);
        Assert.NotEmpty(problems);
    }

    /// <summary>A valid table with a BOM is accepted.</summary>
    [Fact]
    [Trait("Req", "LANG-534")]
    public void Aliases_ValidatorAcceptsBom()
    {
        var problems = new List<string>();
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("""{ "aliases": { "System.Types": "Types" } }""")).ToArray();
        var aliases = Validate(bytes, problems);
        Assert.Empty(problems);
        Assert.Equal([("System.Types", "Types")], aliases);
    }

    /// <summary>LANG-534 checks; returns the entries in file order.</summary>
    private static List<(string Alias, string Canonical)> Validate(byte[] bytes, List<string> problems)
    {
        var result = new List<(string Alias, string Canonical)>();
        JsonDocument document;
        try
        {
            var start = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? 3 : 0;
            document = JsonDocument.Parse(bytes.AsMemory(start));
        }
        catch (JsonException e)
        {
            problems.Add("not valid JSON: " + e.Message);
            return result;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("aliases", out var table) || table.ValueKind != JsonValueKind.Object)
            {
                problems.Add("the root must be an object with exactly the member 'aliases' (an object)");
                return result;
            }

            foreach (var entry in table.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String)
                {
                    problems.Add($"'{entry.Name}': value is not a string");
                    continue;
                }

                result.Add((entry.Name, entry.Value.GetString()!));
            }
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, canonical) in result)
        {
            foreach (var name in new[] { alias, canonical }.Where(n => !IsUnitName(n)))
            {
                problems.Add($"'{name}' is not a valid unit name");
            }

            if (string.Equals(alias, canonical, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"'{alias}' maps to itself");
            }

            if (!keys.Add(alias))
            {
                problems.Add($"'{alias}' is a duplicate key (case-insensitive)");
            }
        }

        foreach (var (_, canonical) in result.Where(a => keys.Contains(a.Canonical)))
        {
            problems.Add($"'{canonical}' is both a value and a key (aliases are never chained)");
        }

        return result;
    }

    private static bool IsUnitName(string name) =>
        name.Split('.').All(part => part.Length > 0 && !SyntaxFacts.IsReservedWord(part)
            && (char.IsAsciiLetter(part[0]) || part[0] == '_') && part.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'));
}
