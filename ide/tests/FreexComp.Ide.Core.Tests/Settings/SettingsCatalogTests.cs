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
