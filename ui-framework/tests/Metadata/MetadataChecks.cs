using System.Text.Json;
using System.Text.RegularExpressions;

namespace FreexComp.MetadataTesting;

/// <summary>Reads the metadata facts of controls-standard.md §29 (default events) and §29.1 (verbs, component editors).</summary>
public static partial class ControlsSpec
{
    public static SchemaExpectations Read(string controlsStandardPath)
    {
        var lines = File.ReadAllLines(controlsStandardPath);
        var defaults = new Dictionary<string, string?>(StringComparer.Ordinal);
        var verbs = new Dictionary<string, IReadOnlyList<SpecVerb>>(StringComparer.Ordinal);
        var editors = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = "";
        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                section = line.StartsWith("## 29.", StringComparison.Ordinal) ? "29"
                    : line.StartsWith("### 29.1", StringComparison.Ordinal) ? "29.1"
                    : "";
                continue;
            }

            if (!line.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (section == "29" && cells.Length == 9)
            {
                var cls = Code(cells[0]).First();
                var ev = cells[8] == "—" ? null : Code(cells[8]).FirstOrDefault();
                defaults[cls] = ev;
            }
            else if (section == "29.1" && cells.Length == 3)
            {
                var classes = Code(cells[0].Split('(')[0]).ToList();
                var owner = cells[0].Contains("declared once on `", StringComparison.Ordinal)
                    ? [DeclaredOnce().Match(cells[0]).Groups[1].Value]
                    : classes;
                var editor = cells[1] == "—" ? null : Code(cells[1]).First();
                var list = cells[2].Split("; ").Select(ParseVerb).ToList();
                foreach (var cls in owner)
                {
                    verbs[cls] = list;
                    if (editor is not null)
                    {
                        editors[cls] = editor;
                    }
                }
            }
        }

        return new SchemaExpectations(defaults, verbs, editors);
    }

    /// <summary><c>`id` — `caption` — `command` — `arg`</c> (the caption is in backticks).</summary>
    private static SpecVerb ParseVerb(string text)
    {
        var parts = text.Split(" — ").Select(p => p.Trim().Trim('`')).ToArray();
        return new SpecVerb(parts[0], parts[1], parts[2], parts.Length > 3 ? parts[3] : null);
    }

    private static IEnumerable<string> Code(string cell) => CodeSpan().Matches(cell).Select(m => m.Groups[1].Value);

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex("declared once on `([^`]+)`")]
    private static partial Regex DeclaredOnce();
}

/// <summary><c>MetadataMatchesSource</c> (component-metadata.md; metadata-authoring.md §4): one unit's file against its Pascal source.</summary>
public static class MetadataMatch
{
    /// <summary>
    /// Units that have no components file but whose classes metadata may declare (abstract, for a chain or a reference type;
    /// component-metadata.md §2.1 "Classes from units without a metadata file").
    /// </summary>
    public static readonly string[] UnitsWithoutFile = ["Classes"];

    public static List<string> Compare(MetadataRoot root, SourceComponents source, string unit)
    {
        var problems = new List<string>();
        foreach (var e in source.Errors(unit).Take(5))
        {
            problems.Add($"{unit}: source does not compile: {e}");
        }

        var file = root.FindFile(unit);
        if (file is null)
        {
            problems.Add($"{unit}: {unit}.components.json is not listed in index.json");
            return problems;
        }

        var classes = source.Components(unit);
        var entries = root.Components.Where(c => ReferenceEquals(c.File, file)).ToList();
        foreach (var cls in classes)
        {
            if (!entries.Any(e => e.Class == cls.Name))
            {
                problems.Add($"{cls.Name}: missing (declared in {unit}, no entry in {file.Name})");
            }
        }

        foreach (var entry in entries)
        {
            var cls = classes.FirstOrDefault(c => c.Name == entry.Class);
            if (cls is null)
            {
                var foreign = UnitsWithoutFile.Select(u => source.FindClass(u, entry.Class)).FirstOrDefault(k => k is not null);
                if (foreign is null)
                {
                    problems.Add($"{entry.Class}: extra (no such component class in {unit} or in a unit without a components file)");
                    continue;
                }

                if (!entry.Abstract)
                {
                    problems.Add($"{entry.Class}: a class of a unit without a components file must be abstract");
                }

                CompareClass(root, entry, foreign.Name, foreign.Parent?.Name, SourceComponents.Effective(foreign), problems);
                continue;
            }

            CompareClass(root, entry, cls.Name, cls.Ancestor, SourceComponents.Effective(cls.Symbol), problems);
        }

        return problems;
    }

    private static void CompareClass(MetadataRoot root, MetadataComponent entry, string name, string? ancestor,
        IReadOnlyList<SourceProperty> effective, List<string> problems)
    {
        if (entry.Ancestor != ancestor)
        {
            problems.Add($"{name}: ancestor: metadata {entry.Ancestor}, source {ancestor}");
        }

        var src = effective.Where(p => !p.IsEvent).ToList();
        var meta = root.Effective(entry, "properties");
        for (var i = 0; i < Math.Max(src.Count, meta.Length); i++)
        {
            var s = i < src.Count ? src[i] : null;
            var m = i < meta.Length ? meta[i] : null;
            var mName = m is not null && m.TryGetValue("name", out var n) ? n.GetString() : null;
            if (s is null)
            {
                problems.Add($"{name}.{mName}: extra (not a published property of the source)");
                continue;
            }

            if (m is null)
            {
                problems.Add($"{name}.{s.Name}: missing (published {s.Type} in source)");
                continue;
            }

            if (mName != s.Name)
            {
                var elsewhere = meta.Any(x => x["name"].GetString() == s.Name);
                problems.Add(elsewhere
                    ? $"{name}.{s.Name}: order mismatch (metadata position {i + 1} is {mName})"
                    : $"{name}.{s.Name}: missing (metadata position {i + 1} is {mName})");
                continue;
            }

            var mType = m.TryGetValue("type", out var t) ? t.GetString() : null;
            var mKind = m.TryGetValue("kind", out var k) ? k.GetString() : null;
            if (mType != s.Type || mKind != s.Kind)
            {
                problems.Add($"{name}.{s.Name}: type mismatch: metadata {mType}{(mKind is null ? "" : "/" + mKind)}, source {s.Type}{(s.Kind is null ? "" : "/" + s.Kind)}");
            }
        }

        var srcEvents = effective.Where(p => p.IsEvent).ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        var metaEvents = root.Effective(entry, "events").ToDictionary(
            e => e["name"].GetString()!, e => e.TryGetValue("signature", out var sg) ? sg.GetString() : null, StringComparer.Ordinal);
        foreach (var (ev, sig) in srcEvents.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!metaEvents.TryGetValue(ev, out var msig))
            {
                problems.Add($"{name}.{ev}: missing event ({sig} in source)");
            }
            else if (msig != sig)
            {
                problems.Add($"{name}.{ev}: signature mismatch: metadata {msig}, source {sig}");
            }
        }

        foreach (var ev in metaEvents.Keys.Where(e => !srcEvents.ContainsKey(e)).Order(StringComparer.Ordinal))
        {
            problems.Add($"{name}.{ev}: extra event (not a published event of the source)");
        }
    }

    /// <summary>Format for an assertion message.</summary>
    public static string Report(IEnumerable<string> problems) => string.Join("\n", problems);
}
