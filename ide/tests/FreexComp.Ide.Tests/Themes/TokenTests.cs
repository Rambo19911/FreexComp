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

    /// <summary>
    /// UI-TOK-008: the view code of <c>FreexComp.Ide</c> (<c>*.axaml</c> and <c>*.cs</c> outside <c>Themes/</c>) holds
    /// no colour literal; every hit is named as <c>&lt;path&gt;(&lt;line&gt;): &lt;text&gt;</c>.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TOK-008")]
    public void NoColourLiteralsInViews()
    {
        var root = SpecTables.RepoRootPath();
        var views = Path.Combine(root, "ide", "src", "FreexComp.Ide");
        var themes = Path.Combine(views, "Themes") + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(views, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.StartsWith(themes, StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutput(Path.GetRelativePath(views, f)))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(files.Count > 50, $"only {files.Count} view files found under {views}");

        var hits = new List<string>();
        foreach (var file in files)
        {
            var path = Path.GetRelativePath(root, file).Replace('\\', '/');
            hits.AddRange(ColourLiterals(File.ReadAllText(file)).Select(h => $"{path}({h.Line}): {h.Text}"));
        }

        Assert.True(hits.Count == 0, hits.Count + " colour literals in view code (UI-TOK-008):" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    /// <summary>UI-TOK-008: the scanner of <see cref="NoColourLiteralsInViews"/> finds each form and nothing that is allowed.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TOK-008")]
    public void NoColourLiteralsInViews_FindsEachForm()
    {
        string[] found =
        [
            "var a = \"#FFF\";",                                  // (a) #RGB
            "var a = \"#8FFF\";",                                 // (a) #ARGB
            "var a = \"#1E1E1E\";",                               // (a) #RRGGBB
            "var a = \"#801E1E1E\";",                             // (a) #AARRGGBB
            "<Border Background=\"#FFFFFF\" />",                  // (a) as an .axaml attribute value
            "var c = Color.Parse(\"#FFFFFF\");",                  // (b) string literal (also (a))
            "var c = Color.FromRgb(255, 255, 255);",              // (b) number literal
            "var c = Color.FromArgb(0xFF, r, g, b);",             // (b) hexadecimal number literal
            "var c = Color.FromUInt32(0xFFFFFFFF);",              // (b)
            "var c = Avalonia.Media.Color.FromArgb( 255, 1, 2, 3);", // (b) qualified, space before the argument
            "var b = Brushes.White;",                             // (c)
            "var c = Colors.Red;",                                // (c)
            "var p = new Pen(Avalonia.Media.Brushes.Gray, 1);",   // (c) qualified
            "var b = x ?? Brushes.Black; // fallback",            // (c) before a comment
        ];
        string[] allowed =
        [
            "var b = Brushes.Transparent;",
            "var c = Colors.Transparent;",
            "var c = Color.Parse(value);",
            "var c = Color.FromArgb(c.A, c.R, c.G, c.B);",
            "var b = ThemeBrushes.Find(this, \"editor.breakpoint\");",
            "var s = \"#1\";",
            "#region Colours",
            "var s = \"#region\";",
            "var s = \"#FFFFF\";",                                // five digits: no colour form
            "var b = Brush(\"bg.editor\"); // was \"#FFFFFF\" and Brushes.White",
            "/// <summary>Draws in \"#FFFFFF\" like Brushes.White.</summary>",
            "Id(\"Colors.Element.\" + token);",                   // a string, not code
            "Id(\"Brushes.White\");",                             // a string, not code
            "var x = model.Colors.Count;",                        // Count is no named colour
            "var x = SomeColors.Red;",                            // another type
            "var u = \"http://host/#FFFFFF\";",                   // // inside a string is no comment; not a whole colour string
        ];

        foreach (var sample in found)
        {
            var hits = ColourLiterals(sample);
            Assert.True(hits.Count == 1 && hits[0].Line == 1 && hits[0].Text == sample.Trim(), $"expected one hit: {sample}");
        }

        foreach (var sample in allowed)
        {
            Assert.True(ColourLiterals(sample).Count == 0, $"expected no hit: {sample}");
        }

        var lines = ColourLiterals("var a = 1;\n  var b = Brushes.White;\r\nvar c = 2;\nvar d = \"#000\";");
        Assert.Equal(new (int, string)[] { (2, "var b = Brushes.White;"), (4, "var d = \"#000\";") }, lines);
    }

    /// <summary>Named colours of Avalonia's <see cref="Colors"/> (by reflection) except <c>Transparent</c> (UI-TOK-008 (c)).</summary>
    private static readonly HashSet<string> NamedColours = typeof(Colors)
        .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(p => p.PropertyType == typeof(Color) && p.Name != nameof(Colors.Transparent))
        .Select(p => p.Name)
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsBuildOutput(string relative)
    {
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("bin", StringComparison.OrdinalIgnoreCase) || first.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The colour literals of a C# or .axaml text (UI-TOK-008): (a) on the string literals, (b) and (c) on the code with
    /// string contents blanked; <c>//</c> and <c>///</c> comments are skipped. One hit per line.
    /// </summary>
    private static List<(int Line, string Text)> ColourLiterals(string text)
    {
        var hits = new List<(int, string)>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var (code, strings) = SplitLine(line);
            var hit = strings.Any(s => HexColour().IsMatch(s))
                || LiteralColourCall().IsMatch(code)
                || NamedColour().Matches(code).Any(m => NamedColours.Contains(m.Groups[1].Value));
            if (hit)
            {
                hits.Add((i + 1, line.Trim()));
            }
        }

        return hits;
    }

    /// <summary>
    /// Splits one line into its code without the <c>//</c> comment (string contents replaced by spaces, quotes kept) and
    /// its string literals (each with its quotes). Handles <c>"…"</c> with escapes, verbatim <c>@"…"</c> and char literals.
    /// </summary>
    private static (string Code, List<string> Strings) SplitLine(string line)
    {
        var code = new System.Text.StringBuilder(line.Length);
        var strings = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            if (c == '\'' && i + 2 < line.Length)
            {
                var end = line[i + 1] == '\\' ? line.IndexOf('\'', i + 3) : (line[i + 2] == '\'' ? i + 2 : -1);
                if (end > 0)
                {
                    code.Append('\'').Append(' ', end - i - 1).Append('\'');
                    i = end + 1;
                    continue;
                }
            }

            if (c == '"')
            {
                var verbatim = i > 0 && (line[i - 1] == '@' || (line[i - 1] == '$' && i > 1 && line[i - 2] == '@'));
                var j = i + 1;
                while (j < line.Length)
                {
                    if (!verbatim && line[j] == '\\')
                    {
                        j += 2;
                        continue;
                    }

                    if (line[j] == '"')
                    {
                        if (verbatim && j + 1 < line.Length && line[j + 1] == '"')
                        {
                            j += 2;
                            continue;
                        }

                        break;
                    }

                    j++;
                }

                var close = Math.Min(j, line.Length - 1);
                strings.Add(line[i..(close + 1)]);
                code.Append('"').Append(' ', Math.Max(0, close - i - 1));
                if (j < line.Length)
                {
                    code.Append('"');
                }

                i = j + 1;
                continue;
            }

            code.Append(c);
            i++;
        }

        return (code.ToString(), strings);
    }

    [GeneratedRegex(@"^""#(?:[0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})""$")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"\bColor\s*\.\s*(?:Parse|FromRgb|FromArgb|FromUInt32)\s*\(\s*(?:\(\s*\w+\s*\)\s*)?(?:""|'|[0-9])")]
    private static partial Regex LiteralColourCall();

    [GeneratedRegex(@"\b(?:Colors|Brushes)\s*\.\s*([A-Za-z]+)\b")]
    private static partial Regex NamedColour();

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
