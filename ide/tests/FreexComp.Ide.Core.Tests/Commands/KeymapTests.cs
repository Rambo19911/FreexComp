using System.Globalization;
using System.Text.RegularExpressions;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Core.Tests.Commands;

[Trait("Phase", "1")]
public sealed partial class KeymapTests
{
    private static readonly string[] Sections = ["3.1 ", "3.2 ", "3.3 ", "3.4 ", "3.5 "];

    /// <summary>I-054 (tasks.md): the classic clipboard keys, Focus scope like Ctrl+X / Ctrl+C / Ctrl+V.</summary>
    private static readonly (string Gesture, string CommandId)[] NotYetInSpec =
        [("Shift+Delete", "edit.cut"), ("Ctrl+Insert", "edit.copy"), ("Shift+Insert", "edit.paste")];

    private static readonly CommandScope[] FocusScopes =
        [CommandScope.Editor, CommandScope.Designer, CommandScope.ProjectManager, CommandScope.ToolList];

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-009")]
    [Trait("Req", "UI-KEY-003")]
    [Trait("Req", "UI-KEY-004")]
    public void DefaultKeymapHasNoConflicts()
    {
        var bindings = DefaultKeymap.Bindings;
        var problems = new List<string>();

        // At most one command per gesture and scope (Focus and Global are the same, outermost level).
        problems.AddRange(bindings
            .GroupBy(b => (b.Gesture, Level: b.Scope == CommandScope.Focus ? CommandScope.Global : b.Scope, b.ToolWindow))
            .Where(g => g.Select(b => b.CommandId).Distinct().Count() > 1)
            .Select(g => $"{g.Key.Gesture} in {g.Key.Level}/{g.Key.ToolWindow}: {string.Join(", ", g.Select(b => b.CommandId))}"));

        // A Global gesture must stay reachable in at least one focus scope.
        foreach (var outer in bindings.Where(b => b.Scope is CommandScope.Global or CommandScope.Focus))
        {
            if (FocusScopes.All(s => bindings.Any(b => b.Gesture == outer.Gesture && b.Scope == s && b.ToolWindow is null)))
            {
                problems.Add($"{outer.Gesture} ({outer.CommandId}) is shadowed in every focus scope");
            }
        }

        // UI-KEY-003: no Ctrl+Alt+<letter, digit or punctuation>.
        problems.AddRange(bindings
            .Where(b => b.Gesture.Modifiers.HasFlag(KeyModifiers.Ctrl) && b.Gesture.Modifiers.HasFlag(KeyModifiers.Alt) && b.Gesture.Key.Length == 1)
            .Select(b => $"{b.Gesture} ({b.CommandId}) is an AltGr combination"));

        // Every binding names a catalog command whose scopes allow it.
        foreach (var b in bindings)
        {
            var command = CommandCatalog.All.FirstOrDefault(c => c.Id == b.CommandId);
            if (command is null)
            {
                problems.Add($"{b.CommandId}: not in CommandCatalog");
            }
            else if (!command.Scopes.Contains(b.Scope))
            {
                problems.Add($"{b.CommandId}: bound in {b.Scope}, command scopes {string.Join(", ", command.Scopes)}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-KEY-009")]
    public void DefaultKeymapMatchesSpec()
    {
        var path = SpecTables.DocPath("spec/ui/shortcuts.md");
        var expected = new List<string>();
        var phases = new List<(string Id, int Phase)>();
        foreach (var row in Sections.SelectMany(s => SpecTables.Read(path, s)))
        {
            if (row["Scope"] == "(system)")
            {
                continue; // Shift+F10: OS context-menu key, not a command
            }

            foreach (var (gesture, id) in Expand(row["Gesture"], row["Command id"]))
            {
                phases.Add((id, int.Parse(PhaseNumber().Match(row["Phase"]).Groups[1].Value, CultureInfo.InvariantCulture)));
                foreach (var (scope, tool) in Scopes(row["Scope"]))
                {
                    expected.Add(Describe(KeyGesture.Parse(gesture), scope, tool, id));
                }
            }
        }

        // Rows a task added before shortcuts.md lists them (the lead merges them into §3, then they leave this list).
        foreach (var extra in NotYetInSpec.Select(r => Describe(KeyGesture.Parse(r.Gesture), CommandScope.Focus, null, r.CommandId)))
        {
            if (!expected.Contains(extra))
                expected.Add(extra);
        }

        var actual = DefaultKeymap.Bindings.Select(b => Describe(b.Gesture, b.Scope, b.ToolWindow, b.CommandId)).ToList();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.All(phases, p => Assert.Equal(p.Phase, CommandCatalog.Get(p.Id).Phase));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-004")]
    [Trait("Req", "UI-KEY-005")]
    public void LookupIsInnermostFirstAndFirstGestureIsDisplayed()
    {
        var keymap = DefaultKeymap.Create();

        Assert.Equal("watches.edit", keymap.FindCommand(KeyGesture.Parse("F2"), CommandScope.ToolList, "Watches"));
        Assert.Equal("locals.modifyValue", keymap.FindCommand(KeyGesture.Parse("F2"), CommandScope.ToolList, "Locals"));
        Assert.Equal("project.renameFile", keymap.FindCommand(KeyGesture.Parse("F2"), CommandScope.ProjectManager, null));
        Assert.Null(keymap.FindCommand(KeyGesture.Parse("F2"), CommandScope.Global, null));
        Assert.Equal("objectInspector.togglePage", keymap.FindCommand(KeyGesture.Parse("Ctrl+Tab"), CommandScope.ToolList, "ObjectInspector"));
        Assert.Equal("window.nextDocument", keymap.FindCommand(KeyGesture.Parse("Ctrl+Tab"), CommandScope.ToolList, "Messages"));
        Assert.Equal("search.find", keymap.FindCommand(KeyGesture.Parse("Ctrl+F"), CommandScope.ToolList, "Messages"));
        Assert.Equal("edit.copy", keymap.FindCommand(KeyGesture.Parse("Ctrl+C"), CommandScope.ToolList, "Messages"));
        Assert.Equal("project.removeFile", keymap.FindCommand(KeyGesture.Parse("Delete"), CommandScope.ProjectManager, null));
        Assert.Equal("edit.delete", keymap.FindCommand(KeyGesture.Parse("Delete"), CommandScope.Editor, null));
        Assert.Null(keymap.FindCommand(KeyGesture.Parse("Ctrl+D"), CommandScope.Designer, null));

        Assert.Equal(["Ctrl+W", "Ctrl+F4"], keymap.GetGestures("file.close").Select(g => g.ToString()));
        Assert.Equal(["Ctrl+Y", "Ctrl+Shift+Z"], keymap.GetGestures("edit.redo").Select(g => g.ToString()));
        Assert.Equal(["Alt+3", "F4"], keymap.GetGestures("view.objectInspector").Select(g => g.ToString()));
        Assert.Empty(keymap.GetGestures("file.newUnit"));
    }

    private static string Describe(KeyGesture gesture, CommandScope scope, string? tool, string id) =>
        $"{gesture} | {scope}{(tool is null ? "" : "/" + tool)} | {id}";

    /// <summary><c>Ctrl+1 … Ctrl+9</c> with <c>a.goTo1 … goTo9</c>; <c>Ctrl+= , Ctrl+Add</c> (two gestures, one command).</summary>
    private static IEnumerable<(string Gesture, string Id)> Expand(string gestureCell, string idCell)
    {
        var family = Family().Match(idCell);
        if (family.Success)
        {
            var stem = gestureCell.Split(" … ")[0].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            for (var n = int.Parse(family.Groups[2].Value, CultureInfo.InvariantCulture); n <= int.Parse(family.Groups[3].Value, CultureInfo.InvariantCulture); n++)
            {
                yield return (stem + n, family.Groups[1].Value + n);
            }

            yield break;
        }

        // Lists are separated by a comma and a space; "Ctrl+," itself ends in a comma.
        foreach (var g in Regex.Split(gestureCell, @"\s*,\s+").Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            yield return (g, idCell);
        }
    }

    /// <summary><c>G</c>, <c>E, TL</c>, <c>TL Search Results (search running)</c>, <c>E (multi-line selection)</c>, <c>Focus</c>.</summary>
    private static IEnumerable<(CommandScope Scope, string? Tool)> Scopes(string cell)
    {
        foreach (var part in cell.Split(", "))
        {
            var text = part.Split(" (")[0].Trim();
            var space = text.IndexOf(' ', StringComparison.Ordinal);
            var abbreviation = space < 0 ? text : text[..space];
            var tool = space < 0 ? null : text[(space + 1)..].Replace(" ", "", StringComparison.Ordinal);
            var scope = abbreviation switch
            {
                "G" => CommandScope.Global,
                "E" => CommandScope.Editor,
                "D" => CommandScope.Designer,
                "PM" => CommandScope.ProjectManager,
                "TL" => CommandScope.ToolList,
                "Focus" => CommandScope.Focus,
                _ => throw new InvalidOperationException($"unknown scope '{cell}'"),
            };
            yield return (scope, tool);
        }
    }

    [GeneratedRegex(@"^(.*?)(\d) … [A-Za-z]*(\d)$")]
    private static partial Regex Family();

    [GeneratedRegex(@"P(\d+)")]
    private static partial Regex PhaseNumber();
}
