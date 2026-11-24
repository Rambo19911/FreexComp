using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Core.Tests.Settings;

[Trait("Phase", "1")]
public sealed class SettingsCatalogTests
{
    private static readonly string[] KeyTables = ["4.1 ", "4.2 ", "4.4 ", "4.5 ", "4.6 "];

    [Fact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "IDE-SET-030")]
    public void MatchesSpec()
    {
        var settingsMd = SpecTables.DocPath("spec/ide/settings.md");
        var tokensMd = SpecTables.DocPath("spec/ui/design-tokens.md");

        var expected = new List<Expected>();
        foreach (var section in KeyTables)
        {
            foreach (var row in SpecTables.Read(settingsMd, section))
            {
                expected.Add(FromKeyRow(row["Key"], row["Type"], row["Default"], row["Range / rule"]));
            }
        }

        // Rows a later spec document gives as text to apply to settings.md count until they are there.
        expected.AddRange(RowsToApply(SpecTables.DocPath("spec/ui/phase8-ui-additions.md")).Where(r => expected.All(e => e.Name != r.Name)));

        Assert.Equal(
            ["colors.light.<token>", "colors.dark.<token>", "colors.light.<syntaxToken>.style", "colors.dark.<syntaxToken>.style"],
            SpecTables.Read(settingsMd, "4.3 ").Select(r => r["Key pattern"]));
        var styles = new[] { "regular", "bold", "italic", "bolditalic" };
        foreach (var row in SpecTables.Read(tokensMd, "3.1 ").Concat(SpecTables.Read(tokensMd, "3.2 ")))
        {
            var token = row["Token"];
            expected.Add(new Expected($"colors.light.{token}", SettingType.Color, row["Light"], [], []));
            expected.Add(new Expected($"colors.dark.{token}", SettingType.Color, row["Dark"], [], []));
            if (row.TryGetValue("Style", out var style))
            {
                var value = StyleValue(style);
                expected.Add(new Expected($"colors.light.{token}.style", SettingType.Enum, value, styles, []));
                expected.Add(new Expected($"colors.dark.{token}.style", SettingType.Enum, value, styles, []));
            }
        }

        var actual = SettingsCatalog.All.ToDictionary(k => k.Name, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var e in expected)
        {
            if (!actual.TryGetValue(e.Name, out var key))
            {
                problems.Add($"{e.Name}: missing from SettingsCatalog");
                continue;
            }

            if (key.Type != e.Type)
            {
                problems.Add($"{e.Name}: type {key.Type}, spec {e.Type}");
            }

            if (!SettingValues.AreEqual(key.DefaultValue, e.Default))
            {
                problems.Add($"{e.Name}: default {Show(key.DefaultValue)}, spec {Show(e.Default)}");
            }

            if (!key.AllowedValues.SequenceEqual(e.Allowed))
            {
                problems.Add($"{e.Name}: values [{string.Join("|", key.AllowedValues)}], spec [{string.Join("|", e.Allowed)}]");
            }

            if (!key.Ranges.SequenceEqual(e.Ranges))
            {
                problems.Add($"{e.Name}: range [{string.Join(", ", key.Ranges)}], spec [{string.Join(", ", e.Ranges)}]");
            }
        }

        problems.AddRange(actual.Keys.Except(expected.Select(e => e.Name), StringComparer.Ordinal)
                                     .Select(n => $"{n}: in SettingsCatalog but not in the spec"));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-030")]
    public void EveryFieldIsInAllAndNamesAreUnique()
    {
        var fields = typeof(SettingsCatalog).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => typeof(ISettingKey).IsAssignableFrom(f.FieldType))
            .ToList();

        Assert.All(fields, f => Assert.True(f.IsInitOnly, f.Name));
        Assert.All(fields, f => Assert.Contains((ISettingKey)f.GetValue(null)!, SettingsCatalog.All));
        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.All.Select(k => k.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(SettingsCatalog.All, k => Assert.True(k.IsValid(k.DefaultValue), k.Name));
    }

    [Fact(Timeout = 30000)]
    public void ColorKeysAreLookedUpByToken()
    {
        Assert.Equal("colors.dark.syntax.keyword", SettingsCatalog.ColorKey("dark", "syntax.keyword").Name);
        Assert.Equal("colors.light.syntax.comment.style", SettingsCatalog.StyleKey("light", "syntax.comment").Name);
        Assert.Equal("italic", SettingsCatalog.StyleKey("light", "syntax.comment").Default);
    }

    /// <summary>
    /// IDE-SET-031: no source file under <c>ide/src</c> except <c>SettingsCatalog.cs</c> names a setting by a string
    /// literal (<c>Settings.Get("…")</c>, <c>IsDefault("…")</c>, <c>e.Key == "…"</c>, …): keys come from the catalog fields.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-031")]
    public void NoStringKeysInSource()
    {
        var offenders = StringKeyLiterals(Path.Combine(SpecTables.RepoRootPath(), "ide", "src"));
        Assert.True(offenders.Count == 0, "Settings named by string literal (IDE-SET-031):\n" + string.Join("\n", offenders));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-031")]
    public void StringKeyScanFindsLiterals()
    {
        var root = Directory.CreateTempSubdirectory("fxc-set031-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "A.cs"), "var a = settings.Get(\"editor.tabWidth\");\nvar b = \"editor.tab\" + x;");
            File.WriteAllText(Path.Combine(root, "B.cs"), "if (e.Key == \"colors.dark.syntax.keyword\") { }\nsettings.IsDefault(\"files.trimTrailingWhitespaceOnSave\");");
            File.WriteAllText(Path.Combine(root, "C.cs"), "Command(\"designer.lockControls\");\nsettings.Get(\"designer.lockControls\");\nStrings.Get(\"file.save\");");
            File.WriteAllText(Path.Combine(root, "SettingsCatalog.cs"), "Bool(\"editor.wordWrap\", false);");
            var offenders = StringKeyLiterals(root);
            Assert.Equal(["A.cs(1)", "B.cs(1)", "B.cs(2)", "C.cs(2)"], offenders.Select(o => o[..o.IndexOf(':', StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
            Assert.Contains(offenders, o => o.StartsWith("A.cs(1)", StringComparison.Ordinal));
            Assert.DoesNotContain(offenders, o => o.StartsWith("SettingsCatalog.cs", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A string literal names a setting when it is a catalog key name or a per-token color key (<c>colors.light.*</c>).
    /// Key names that are also command ids (<c>designer.lockControls</c>) count only as the first argument of a
    /// settings call (<c>Get/Set/IsDefault/Reset/ResetAll</c>) or in a <c>Key ==</c> comparison, because the same text
    /// is a legitimate command id elsewhere.
    /// </summary>
    private static List<string> StringKeyLiterals(string root)
    {
        var commandIds = FreexComp.Ide.Core.Commands.CommandCatalog.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var allNames = SettingsCatalog.All.Select(k => k.Name).ToHashSet(StringComparer.Ordinal);
        var settingContext = new System.Text.RegularExpressions.Regex(
            @"(\b(Get|Set|IsDefault|Reset|ResetAll)\s*(<[^>]*>)?\s*\(\s*|\bKey\s*(==|!=|is)\s*)""(?<key>[^""]*)""");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")
                || Path.GetExtension(file) is not (".cs" or ".axaml")
                || Path.GetFileName(file) == "SettingsCatalog.cs")
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var inSettingCall = settingContext.Matches(lines[i]).Any(m => allNames.Contains(m.Groups["key"].Value));
                var literalKey = System.Text.RegularExpressions.Regex.Matches(lines[i], "\"((?:[^\"\\\\]|\\\\.)*)\"")
                    .Select(m => m.Groups[1].Value)
                    .Any(text => (allNames.Contains(text) && !commandIds.Contains(text))
                                 || text.StartsWith("colors.light.", StringComparison.Ordinal)
                                 || text.StartsWith("colors.dark.", StringComparison.Ordinal));
                if (inSettingCall || literalKey)
                {
                    offenders.Add($"{relative}({i + 1}): {lines[i].Trim()}");
                }
            }
        }

        return offenders;
    }

    /// <summary>
    /// <c>Interface text to apply (`settings.md`)</c> rows: <c>`| name | enum a, b | default | text |`</c> on a line of its own.
    /// </summary>
    private static IEnumerable<Expected> RowsToApply(string path)
    {
        var lines = File.ReadAllLines(path);
        for (var i = 1; i < lines.Length; i++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(lines[i], @"^\s*`\| (?<name>[A-Za-z.]+) \| enum (?<values>[a-z, ]+) \| (?<default>[a-z]+) \|");
            if (match.Success && lines[i - 1].Contains("(`settings.md`)", StringComparison.Ordinal))
            {
                yield return new Expected(match.Groups["name"].Value, SettingType.Enum, match.Groups["default"].Value,
                    match.Groups["values"].Value.Split(", "), []);
            }
        }
    }

    private static Expected FromKeyRow(string name, string type, string defaultText, string rule)
    {
        if (type.StartsWith("enum(", StringComparison.Ordinal))
        {
            var values = type["enum(".Length..^1].Split('|');
            return new Expected(name, SettingType.Enum, defaultText, values, []);
        }

        return type switch
        {
            "bool" => new Expected(name, SettingType.Bool, bool.Parse(defaultText), [], []),
            "int" => new Expected(name, SettingType.Int, int.Parse(defaultText, CultureInfo.InvariantCulture), [], Ranges(rule)),
            "number" => new Expected(name, SettingType.Number, double.Parse(defaultText, CultureInfo.InvariantCulture), [], Ranges(rule)),
            "string" => new Expected(name, SettingType.String, defaultText.Trim('"'), [], []),
            "string[]" => new Expected(name, SettingType.StringArray, JsonSerializer.Deserialize<string[]>(defaultText)!, [], []),
            "color" => new Expected(name, SettingType.Color, defaultText, [], []),
            _ => throw new InvalidOperationException($"{name}: unknown type '{type}' in the spec"),
        };
    }

    /// <summary><c>6–72</c>, <c>0 or 5–600</c>, <c>5, 7, 9 or 11</c>; text after <c>;</c> is commentary; other rules are free text.</summary>
    private static SettingRange[] Ranges(string rule)
    {
        var head = rule.Split(';')[0].Trim();
        var parts = head.Replace(" or ", ",", StringComparison.Ordinal).Split(',', StringSplitOptions.TrimEntries);
        var ranges = new List<SettingRange>();
        foreach (var part in parts)
        {
            var bounds = part.Split('–', StringSplitOptions.TrimEntries);
            if (bounds.Length is < 1 or > 2 || !bounds.All(b => double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                return [];
            }

            var low = double.Parse(bounds[0], CultureInfo.InvariantCulture);
            ranges.Add(new SettingRange(low, bounds.Length == 2 ? double.Parse(bounds[1], CultureInfo.InvariantCulture) : low));
        }

        return [.. ranges];
    }

    /// <summary>design-tokens.md §3.1 "Style": <c>semi-bold</c> maps to <c>bold</c>; trailing notes are ignored.</summary>
    private static string StyleValue(string style)
    {
        var word = style.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)[0];
        return word switch
        {
            "semi-bold" or "bold" => "bold",
            "italic" => "italic",
            "regular" => "regular",
            _ => throw new InvalidOperationException($"unknown style '{style}'"),
        };
    }

    private static string Show(object value) =>
        value is IReadOnlyList<string> list ? "[" + string.Join(", ", list) + "]" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";

    private sealed record Expected(string Name, SettingType Type, object Default, IReadOnlyList<string> Allowed, IReadOnlyList<SettingRange> Ranges);
}
