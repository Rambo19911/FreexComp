using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FreexComp.Ide.Core.Themes;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Themes;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed partial class TokenTests
{
    /// <summary>design-tokens.md tables with Token / Light / Dark columns.</summary>
    private static readonly string[] ColorSections = ["2.1 ", "2.2 ", "2.3 ", "2.4 ", "2.5 ", "3.1 ", "3.2 ", "3.3 "];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-005")]
    public void AllTokensDefinedInBothThemes()
    {
        var light = Load("Light");
        var dark = Load("Dark");

        Assert.NotEmpty(light.Keys);
        Assert.Equal(Keys(light), Keys(dark));
        foreach (var key in Keys(light).Where(k => !k.EndsWith("Brush", StringComparison.Ordinal)))
        {
            Assert.True(light.ContainsKey(key + "Brush"), $"{key} has no {key}Brush (UI-TOK-002)");
        }

        var fluent = Theme(Load("FluentOverrides"));
        Assert.Equal(Keys(fluent["Default"]), Keys(fluent["Dark"])); // light = Default, like Fluent's own dictionary
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-TOK-002")]
    public void SpecValuesMatchDictionaries()
    {
        var tokens = SpecTokens();
        var problems = new List<string>();

        foreach (var (theme, column) in new[] { ("Light", 1), ("Dark", 2) })
        {
            var dict = Load(theme);
            foreach (var t in tokens)
            {
                var key = ResourceKey(t.Token);
                var expected = ParseSpecColor(column == 1 ? t.Light : t.Dark);
                if (!dict.TryGetValue(key, out var value) || value is not Color color)
                {
                    problems.Add($"{theme}: {key} missing or not a Color");
                    continue;
                }

                if (color != expected)
                {
                    problems.Add($"{theme}: {key} = {color}, spec {expected}");
                }

                if (!dict.TryGetValue(key + "Brush", out var brush) || brush is not ISolidColorBrush b || b.Color != expected)
                {
                    problems.Add($"{theme}: {key}Brush missing or not {expected}");
                }
            }

            var specKeys = tokens.SelectMany(t => new[] { ResourceKey(t.Token), ResourceKey(t.Token) + "Brush" }).ToHashSet(StringComparer.Ordinal);
            problems.AddRange(Keys(dict).Where(k => !specKeys.Contains(k)).Select(k => $"{theme}: {k} is not a token of design-tokens.md"));

            // UI-TOK-007: Fluent accent resources follow accent.default / hover / pressed.
            var fluent = Theme(Load("FluentOverrides"))[theme == "Light" ? "Default" : theme];
            var accent = tokens.ToDictionary(t => t.Token, t => ParseSpecColor(column == 1 ? t.Light : t.Dark));
            foreach (var (fluentKey, token) in new[] { ("SystemAccentColor", "accent.default"), ("SystemAccentColorLight1", "accent.hover"), ("SystemAccentColorDark1", "accent.pressed") })
            {
                if (!fluent.TryGetValue(fluentKey, out var v) || v is not Color c || c != accent[token])
                {
                    problems.Add($"FluentOverrides {theme}: {fluentKey} is not {token}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-TOK-006")]
    [Trait("Req", "UI-A11Y-040")]
    public void ContrastRulesHold()
    {
        var path = SpecTables.DocPath("spec/ui/design-tokens.md");
        var all = SpecTokens().Select(t => t.Token).ToList();
        var problems = new List<string>();
        var checkedPairs = 0;

        foreach (var theme in new[] { "Light", "Dark" })
        {
            var dict = Load(theme);
            string Hex(string token)
            {
                var c = (Color)dict[ResourceKey(token)]!;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }

            foreach (var row in SpecTables.Read(path, "2.6 "))
            {
                var minimum = double.Parse(row["Minimum"], CultureInfo.InvariantCulture);
                foreach (var fg in Expand(row["Foreground"], all, own: null))
                {
                    foreach (var bg in Expand(row["Background"], all, own: fg))
                    {
                        checkedPairs++;
                        var ratio = ColorContrast.Ratio(Hex(fg), Hex(bg));
                        if (ratio < minimum)
                        {
                            problems.Add($"{theme}: {fg} on {bg} = {ratio:0.00}, minimum {minimum}");
                        }
                    }
                }
            }
        }

        Assert.True(checkedPairs > 100, $"only {checkedPairs} pairs parsed from §2.6");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// UI-TOK-007: every colour resource the running <see cref="FluentTheme"/> exposes (enumerated at run time, so a
    /// toolkit update that adds keys is caught) resolves, from the application, to a colour token of that theme.
    /// Brushes must be opaque (Opacity 1, otherwise the drawn colour is not the token's); fully transparent
    /// resources mean "no colour" and are allowed.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-007")]
    public void FluentResourcesOverridden()
    {
        var fluent = Application.Current!.Styles.OfType<FluentTheme>().Single();
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        CollectKeys(fluent.Resources, keys);
        Assert.True(keys.Count > 500, $"only {keys.Count} Fluent resource keys found");

        var problems = new List<string>();
        var colours = 0;
        foreach (var (name, variant) in new[] { ("Light", ThemeVariant.Light), ("Dark", ThemeVariant.Dark) })
        {
            var tokens = Load(name).Values.OfType<Color>().ToHashSet();
            foreach (var key in keys)
            {
                if (!Application.Current.TryGetResource(key, variant, out var value))
                {
                    continue;
                }

                switch (value)
                {
                    case Color c:
                        colours++;
                        if (c.A != 0 && !tokens.Contains(c))
                        {
                            problems.Add($"{name}: {key} = {c} is not a token colour");
                        }

                        break;
                    case ISolidColorBrush b:
                        colours++;
                        if (b.Color.A != 0 && (!tokens.Contains(b.Color) || b.Opacity != 1))
                        {
                            problems.Add($"{name}: {key} = {b.Color} (opacity {b.Opacity}) is not a token colour");
                        }

                        break;
                    case IBrush other:
                        colours++;
                        problems.Add($"{name}: {key} is a {other.GetType().Name}, not a token brush");
                        break;
                }
            }
        }

        Assert.True(colours > 1000, $"only {colours} colour resources resolved");
        Assert.True(problems.Count == 0, problems.Count + " Fluent colour resources not overridden:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private static void CollectKeys(IResourceDictionary dict, ISet<string> keys)
    {
        foreach (var key in dict.Keys)
        {
            if (key is string s)
            {
                keys.Add(s);
            }
        }

        foreach (var theme in dict.ThemeDictionaries.Values.OfType<IResourceDictionary>())
        {
            CollectKeys(theme, keys);
        }

        foreach (var merged in dict.MergedDictionaries.OfType<IResourceDictionary>())
        {
            CollectKeys(merged, keys);
        }
    }

    /// <summary>
    /// A §2.6 cell: token names, wildcards (<c>statusbar.*.bg</c>, <c>syntax.*</c>) and "their own .bg token".
    /// Words without a dot ("all", "four", "every") are prose.
    /// </summary>
    private static IEnumerable<string> Expand(string cell, IReadOnlyList<string> all, string? own)
    {
        if (own is not null && cell.Contains("own .bg", StringComparison.Ordinal))
        {
            yield return own + ".bg";
        }

        foreach (Match m in TokenWord().Matches(cell.Replace("own .bg", "", StringComparison.Ordinal)))
        {
            var word = m.Value;
            if (!word.Contains('*', StringComparison.Ordinal))
            {
                Assert.Contains(word, all);
                yield return word;
                continue;
            }

            var pattern = new Regex("^" + Regex.Escape(word).Replace(@"\*", "[^.]+", StringComparison.Ordinal) + (word.EndsWith('*') ? "(\\..+)?$" : "$"));
            var matches = all.Where(t => pattern.IsMatch(t)).ToList();
            Assert.NotEmpty(matches);
            foreach (var t in matches)
            {
                yield return t;
            }
        }
    }

    private static List<(string Token, string Light, string Dark)> SpecTokens()
    {
        var path = SpecTables.DocPath("spec/ui/design-tokens.md");
        return [.. ColorSections.SelectMany(s => SpecTables.Read(path, s)).Select(r => (r["Token"], r["Light"], r["Dark"]))];
    }

    /// <summary>UI-TOK-002: <c>Fxc</c> + PascalCase of the dotted segments.</summary>
    private static string ResourceKey(string token) =>
        "Fxc" + string.Concat(token.Split('.').Select(s => char.ToUpperInvariant(s[0]) + s[1..]));

    /// <summary>Spec colors are <c>#RRGGBB</c> or <c>#RRGGBBAA</c> (§2.1 <c>bg.overlay</c>).</summary>
    private static Color ParseSpecColor(string hex)
    {
        var v = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length == 9
            ? Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8))
            : Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private static ResourceDictionary Load(string name) =>
        (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://FreexComp.Ide/Themes/{name}.axaml"));

    private static Dictionary<string, ResourceDictionary> Theme(ResourceDictionary dict) =>
        dict.ThemeDictionaries.ToDictionary(p => p.Key.ToString()!, p => (ResourceDictionary)p.Value);

    private static List<string> Keys(IResourceDictionary dict) =>
        [.. dict.Keys.Select(k => k.ToString()!).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"[a-z][A-Za-z]*(\.[A-Za-z*]+)+")]
    private static partial Regex TokenWord();
}
