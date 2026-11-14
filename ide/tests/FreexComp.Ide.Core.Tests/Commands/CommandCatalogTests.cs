using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Core.Tests.Commands;

[Trait("Phase", "1")]
public sealed partial class CommandCatalogTests
{
    private static readonly string[] Sections = ["2.1 ", "2.2 ", "2.3 ", "2.4 ", "2.5 ", "2.6 ", "2.7 ", "2.8 ", "2.9 ", "2.10 ", "2.11 "];

    [Fact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-CMD-002")]
    public void MatchesSpec()
    {
        var path = SpecTables.DocPath("spec/ui/commands.md");
        var expected = Sections.SelectMany(s => SpecTables.Read(path, s)).SelectMany(ParseRow).ToList();
        var actual = CommandCatalog.All.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var titles = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!
            .Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.Ordinal);
        var problems = new List<string>();

        problems.AddRange(expected.GroupBy(e => e.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key}: listed {g.Count()} times in the spec"));
        foreach (var e in expected.DistinctBy(e => e.Id))
        {
            if (!actual.TryGetValue(e.Id, out var c))
            {
                problems.Add($"{e.Id}: missing from CommandCatalog");
                continue;
            }

            void Check(string what, object a, object s)
            {
                if (!Equals(a, s))
                {
                    problems.Add($"{e.Id}: {what} {a}, spec {s}");
                }
            }

            Check("category", c.Category, e.Id[..e.Id.IndexOf('.')]);
            Check("phase", c.Phase, e.Phase);
            Check("shortcut", string.Join(" | ", c.DefaultGestures), string.Join(" | ", e.Gestures));
            Check("scopes", string.Join(", ", c.Scopes), string.Join(", ", e.Scopes));
            Check("debug", c.RequiresDebugSession, e.RequiresDebug);
            Check("toggle", c.IsToggle, e.IsToggle);
            Check("argument", c.TakesArgument, e.TakesArgument);
            Check("title", titles.GetValueOrDefault(c.TitleKey, "<missing " + c.TitleKey + ">"), e.Title);
        }

        problems.AddRange(actual.Keys.Except(expected.Select(e => e.Id), StringComparer.Ordinal)
            .Select(id => $"{id}: in CommandCatalog but not in the spec"));
        problems.AddRange(titles.Keys.Where(k => k.StartsWith("Cmd_", StringComparison.Ordinal))
            .Except(CommandCatalog.All.Select(c => c.TitleKey), StringComparer.Ordinal)
            .Select(k => $"{k}: in Strings.resx but no command uses it"));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-CMD-001")]
    public void IdsAreWellFormedAndUnique()
    {
        Assert.Equal(CommandCatalog.All.Count, CommandCatalog.All.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(CommandCatalog.All, c => Assert.Matches(IdShape(), c.Id));
        Assert.Equal("Cmd_file_saveAll", CommandCatalog.Get("file.saveAll").TitleKey);
        Assert.Equal(CommandScope.Global, CommandCatalog.Get("file.saveAll").Scope);
        Assert.Throws<KeyNotFoundException>(() => CommandCatalog.Get("file.nope"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-CMD-003")]
    public void TitlesComeFromCmdResources()
    {
        Assert.All(CommandCatalog.All, c =>
        {
            Assert.Equal("Cmd_" + c.Id.Replace('.', '_'), c.TitleKey);
            Assert.False(string.IsNullOrEmpty(Strings.ResourceManager.GetString(c.TitleKey, CultureInfo.InvariantCulture)), c.TitleKey);
        });
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-CMD-006")]
    public void ParameterizedCommandsHaveNoDefaultShortcut()
    {
        var parameterized = CommandCatalog.All.Where(c => c.TakesArgument).ToList();
        Assert.Contains(parameterized, c => c.Id == "file.openRecentProject");
        Assert.All(parameterized, c => Assert.Empty(c.DefaultGestures));
    }

    /// <summary>One spec row → one or (for <c>a1 … a9</c> families) several commands.</summary>
    private static IEnumerable<Expected> ParseRow(IReadOnlyDictionary<string, string> row)
    {
        var idCell = row["Id"];
        var takesArg = idCell.Contains("*arg*", StringComparison.Ordinal);
        idCell = idCell.Replace("*arg*", "", StringComparison.Ordinal).Trim();

        var title = row["Title"];
        var isToggle = title.EndsWith(" (toggle)", StringComparison.Ordinal);
        if (isToggle)
        {
            title = title[..^" (toggle)".Length];
        }

        var arg = ArgMarker().Match(title);
        if (arg.Success)
        {
            takesArg = true;
            title = title[..arg.Index];
        }

        var scopes = new List<CommandScope>();
        var debug = false;
        foreach (var part in row["Scope"].Split(", "))
        {
            var name = part;
            if (name.EndsWith("+Debug", StringComparison.Ordinal))
            {
                debug = true;
                name = name[..^"+Debug".Length];
            }

            scopes.Add(Enum.Parse<CommandScope>(name));
        }

        var phase = int.Parse(PhaseNumber().Match(row["Phase"]).Groups[1].Value, CultureInfo.InvariantCulture);
        var shortcut = row["Default shortcut"];

        var family = Family().Match(idCell);
        if (!family.Success)
        {
            yield return new Expected(idCell, title, Gestures(shortcut), scopes, debug, phase, isToggle, takesArg);
            yield break;
        }

        // `search.bookmark.toggle1 … toggle9` | `Toggle Bookmark 1…9` | `Ctrl+Shift+1 … Ctrl+Shift+9`
        var first = int.Parse(family.Groups[2].Value, CultureInfo.InvariantCulture);
        var last = int.Parse(family.Groups[3].Value, CultureInfo.InvariantCulture);
        var titleStem = TitleRange().Replace(title, "");
        var keyStem = shortcut.Split(" … ")[0].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        for (var n = first; n <= last; n++)
        {
            var gestures = shortcut == "—" ? [] : new[] { keyStem + n };
            yield return new Expected(family.Groups[1].Value + n, titleStem + n, gestures, scopes, debug, phase, isToggle, takesArg);
        }
    }

    /// <summary><c>Ctrl+W (also Ctrl+F4)</c> → both; other parentheses are notes (<c>Delete (ProjectManager)</c>).</summary>
    private static string[] Gestures(string cell)
    {
        if (cell == "—")
        {
            return [];
        }

        var main = cell.Split(" (")[0].Trim();
        var also = AlsoGesture().Match(cell);
        return also.Success ? [main, also.Groups[1].Value.Trim()] : [main];
    }

    [GeneratedRegex(@" \((\*arg\*|arg )")]
    private static partial Regex ArgMarker();

    [GeneratedRegex(@"P(\d+)")]
    private static partial Regex PhaseNumber();

    [GeneratedRegex(@"^(.*?)(\d) … [A-Za-z]*(\d)$")]
    private static partial Regex Family();

    [GeneratedRegex(@"\d…\d$")]
    private static partial Regex TitleRange();

    [GeneratedRegex(@"\(also ([^)]+)\)")]
    private static partial Regex AlsoGesture();

    // UI-CMD-001 says "category is lower-case", but §2.11 uses camelCase categories (searchResults, projectManager, …).
    [GeneratedRegex(@"^[a-z][A-Za-z]*(\.[a-z][A-Za-z0-9]*)+$")]
    private static partial Regex IdShape();

    private sealed record Expected(
        string Id, string Title, IReadOnlyList<string> Gestures, IReadOnlyList<CommandScope> Scopes,
        bool RequiresDebug, int Phase, bool IsToggle, bool TakesArgument);
}
