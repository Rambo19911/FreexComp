using System.Text.Json;
using System.Text.RegularExpressions;

namespace FreexComp.MetadataTesting;

/// <summary>One row of controls-standard.md §29 (the metadata facts of one class), cells as written.</summary>
public sealed record SummaryRow(string Class, string Unit, string Kind, string Size, string Prefix, string Page, string Order, string Renderer, string DefaultEvent);

/// <summary>One property name of metadata-authoring.md §2.3 with its category and, when written <c>`Name` (words)</c>, the class the words name.</summary>
public sealed record CategoryEntry(string Name, string Category, string? Class);

/// <summary>
/// <c>MetadataCategoriesFollowSpec</c> (metadata-authoring.md §2.3) and <c>MetadataMatchesControlsSpec</c> (controls-standard.md §29):
/// the metadata against the tables of the spec, parsed from the Markdown.
/// </summary>
public static partial class MetadataSpecChecks
{
    /// <summary>The rows of the §29 summary table.</summary>
    public static List<SummaryRow> ReadSummary(string controlsStandardPath)
    {
        var rows = new List<SummaryRow>();
        var inSection = false;
        foreach (var line in File.ReadAllLines(controlsStandardPath))
        {
            if (line.StartsWith('#'))
            {
                inSection = line.StartsWith("## 29.", StringComparison.Ordinal);
                continue;
            }

            if (inSection && line.StartsWith("| `", StringComparison.Ordinal))
            {
                var c = line.Trim('|').Split('|').Select(x => x.Trim()).ToArray();
                if (c.Length == 9)
                {
                    rows.Add(new SummaryRow(Unquote(c[0]), c[1], c[2], c[3], c[4], c[5], c[6], Unquote(c[7]), Unquote(c[8])));
                }
            }
        }

        return rows;
    }

    /// <summary>The §2.3 table plus the "`Name` is `Cat` for `TClass`" sentences under it (rules for names listed twice without words).</summary>
    public static (List<CategoryEntry> Entries, List<(string Name, string Category, string Class)> ForClass) ReadCategories(string metadataAuthoringPath)
    {
        var entries = new List<CategoryEntry>();
        var forClass = new List<(string, string, string)>();
        var inSection = false;
        foreach (var line in File.ReadAllLines(metadataAuthoringPath))
        {
            if (line.StartsWith('#'))
            {
                inSection = line.StartsWith("### 2.3", StringComparison.Ordinal);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            if (line.StartsWith("| `", StringComparison.Ordinal))
            {
                var c = line.Trim('|').Split('|').Select(x => x.Trim()).ToArray();
                var category = Unquote(c[0]);
                foreach (Match m in Listed().Matches(c[1]))
                {
                    var words = m.Groups[2].Success ? m.Groups[2].Value : null;
                    entries.Add(new CategoryEntry(m.Groups[1].Value, category, words is null ? null : ClassOf(words)));
                }
            }
            else
            {
                foreach (Match m in ForClassRule().Matches(line))
                {
                    forClass.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
                }
            }
        }

        return (entries, forClass);
    }

    /// <summary>Every property of every component's effective list has the category §2.3 gives it (names not listed: <c>Misc</c>).</summary>
    public static List<string> CategoriesFollowSpec(MetadataRoot root, string metadataAuthoringPath)
    {
        var (entries, forClass) = ReadCategories(metadataAuthoringPath);
        var problems = new List<string>();
        foreach (var c in root.Components)
        {
            var chain = root.Chain(c).Select(k => k.Class).ToList();
            foreach (var p in root.Effective(c, "properties"))
            {
                var name = p["name"].GetString()!;
                var actual = p.TryGetValue("category", out var cat) ? cat.GetString() : null;
                var expected = Expected(name, chain, entries, forClass);
                if (actual != expected)
                {
                    problems.Add($"{c.Class}.{name}: category {actual ?? "(none)"}, expected {expected} (metadata-authoring.md §2.3)");
                }
            }
        }

        return problems;
    }

    private static string Expected(string name, List<string> chain, List<CategoryEntry> entries, List<(string Name, string Category, string Class)> forClass)
    {
        var candidates = entries.Where(e => e.Name == name).ToList();
        if (candidates.Count == 0)
        {
            return "Misc";
        }

        if (candidates.FirstOrDefault(e => e.Class is not null && chain.Contains(e.Class)) is { } qualified)
        {
            return qualified.Category;
        }

        var rules = forClass.Where(r => r.Name == name).ToList();
        if (rules.FirstOrDefault(r => chain.Contains(r.Class)) is { Name: not null } rule)
        {
            return rule.Category;
        }

        // a name with a class rule falls to its other category for the remaining classes
        var rest = candidates.Where(e => e.Class is null && !rules.Any(r => r.Category == e.Category)).ToList();
        return (rest.Count > 0 ? rest : candidates.Where(e => e.Class is null).DefaultIfEmpty(candidates[0])).First().Category;
    }

    /// <summary>Row + column of every §29 fact that differs from the metadata; palette classes missing from §29 are reported too.</summary>
    public static List<string> MatchesControlsSpec(MetadataRoot root, string controlsStandardPath)
    {
        var problems = new List<string>();
        var rows = ReadSummary(controlsStandardPath);
        foreach (var row in rows)
        {
            var at = $"§29 {row.Class}";
            if (root.Find(row.Class) is not { } c)
            {
                problems.Add($"{at}: no metadata entry");
                continue;
            }

            Check(at, "Unit", row.Unit, c.File.Unit, problems);
            Check(at, "kind", row.Kind, Inherited(root, c, "kind")?.GetString(), problems);
            var size = Inherited(root, c, "defaultSize");
            var actualSize = size is { } s ? $"{s.GetProperty("width").GetInt32()} × {s.GetProperty("height").GetInt32()}" : "—";
            if (SizeIn().Match(row.Size) is { Success: true } m)
            {
                Check(at, "Default W × H", $"{m.Groups[1].Value} × {m.Groups[2].Value}", actualSize, problems);
            }
            else if (row.Size == "—" && row.Kind == "nonvisual")
            {
                Check(at, "Default W × H", "—", actualSize, problems);
            }

            Check(at, "namePrefix", row.Prefix, c.Json.TryGetProperty("namePrefix", out var np) ? np.GetString() : c.Class[1..], problems);
            var palette = c.Json.TryGetProperty("palette", out var pal) ? pal : (JsonElement?)null;
            Check(at, "Palette page", row.Page, palette is { } p1 ? p1.GetProperty("page").GetString() : "—", problems);
            Check(at, "Palette order", row.Order, palette is { } p2 ? p2.GetProperty("order").GetRawText() : "—", problems);
            Check(at, "renderer", row.Renderer, Inherited(root, c, "renderer")?.GetString(), problems);
            var chosen = root.Chain(c).Select(k => k.Events.FirstOrDefault(e => e.TryGetProperty("default", out _)))
                .FirstOrDefault(e => e.ValueKind == JsonValueKind.Object);
            Check(at, "Default event", row.DefaultEvent, chosen.ValueKind == JsonValueKind.Object ? chosen.GetProperty("name").GetString() : "—", problems);
        }

        foreach (var c in root.Components.Where(c => c.Json.TryGetProperty("palette", out _) && !rows.Any(r => r.Class == c.Class)))
        {
            problems.Add($"{c.File.Name}: {c.Class}: on the palette but not in controls-standard.md §29");
        }

        return problems;
    }

    private static void Check(string at, string column, string expected, string? actual, List<string> problems)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            problems.Add($"{at}: {column}: spec {expected}, metadata {actual ?? "(none)"}");
        }
    }

    private static JsonElement? Inherited(MetadataRoot root, MetadataComponent c, string key) =>
        root.Chain(c).Select(k => k.Json.TryGetProperty(key, out var v) ? v : (JsonElement?)null).FirstOrDefault(v => v is not null);

    /// <summary>"list box" → <c>TListBox</c>.</summary>
    private static string ClassOf(string words) =>
        "T" + string.Concat(words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Unquote(string cell) => cell.Trim().Trim('`');

    [GeneratedRegex(@"`(\w+)`(?: \(([a-z ]+)\))?")]
    private static partial Regex Listed();

    [GeneratedRegex(@"`(\w+)` is `(\w+)` for `(T\w+)`")]
    private static partial Regex ForClassRule();

    [GeneratedRegex(@"(\d+) × (\d+)")]
    private static partial Regex SizeIn();
}
