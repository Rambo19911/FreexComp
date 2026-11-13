using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Input;
using FreexComp.Ide.Tests.Infrastructure;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed partial class MenuTests
{
    private static readonly (string Section, string Key, string Label)[] TopMenus =
    [
        ("2. File", "File", "&File"), ("3. Edit", "Edit", "&Edit"), ("4. Search", "Search", "&Search"),
        ("5. View", "View", "&View"), ("6. Project", "Project", "&Project"), ("7. Run", "Run", "&Run"),
        ("8. Component", "Component", "&Component"), ("9. Tools", "Tools", "&Tools"), ("10. Window", "Window", "&Window"),
        ("11. Help", "Help", "&Help"),
    ];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-MENU-001")]
    [Trait("Req", "UI-MENU-002")]
    [Trait("Req", "UI-MENU-003")]
    [Trait("Req", "UI-A11Y-033")]
    [Trait("Req", "UI-A11Y-034")]
    public async Task MenuMatchesSpec()
    {
        await using var h = await HeadlessHarness.StartAsync();

        var expected = Flatten(SpecTree(), 0);
        var actual = Flatten(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<object>(), 0);

        Assert.True(expected.Count > 200, $"spec not read: {expected.Count} rows");
        Assert.Equal(expected, actual);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-004")]
    [Trait("Req", "UI-MENU-001")]
    public async Task LaterPhaseItemsAreVisibleAndDisabled()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var items = AllItems(h).ToList();
        var later = items.Where(i => Definition(i)?.Phase > 1).ToList();

        Assert.NotEmpty(later);
        Assert.All(later, i =>
        {
            Assert.True(i.IsVisible, AutomationIds.Get(i));
            Assert.False(i.Command!.CanExecute(i.CommandParameter), AutomationIds.Get(i));
        });
        Assert.All(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<MenuItem>(), top => Assert.True(top.IsEnabled));
        Assert.False(Item(h, AutomationIds.SubMenu("Edit", "CodeInsight")).IsEnabled); // only P3+ children
        Assert.False(Item(h, AutomationIds.Menu("project.build")).Command!.CanExecute(null));
        Assert.True(Item(h, AutomationIds.Menu("view.theme.dark")).Command!.CanExecute(null)); // P1 with handler
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-005")]
    public async Task ShortcutTextFollowsKeymap()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var save = Item(h, AutomationIds.Menu("file.save"));

        Assert.Equal(Gesture("Ctrl+S"), save.InputGesture);
        Assert.Null(save.HotKey); // display only: key routing owns the gestures (IDE-ARCH-043)
        Assert.Null(Item(h, AutomationIds.Menu("file.saveAs")).InputGesture);

        h.Services.Keymap.SetGestures("file.save", [CoreGesture.Parse("Ctrl+Shift+F2"), CoreGesture.Parse("F2")]);

        Assert.Equal(Gesture("Ctrl+Shift+F2"), save.InputGesture); // first of several
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-007")]
    public async Task ExclusiveGroupsShowTheActiveEntry()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = "light");
        var light = Item(h, AutomationIds.Menu("view.theme.light"));
        var dark = Item(h, AutomationIds.Menu("view.theme.dark"));

        Assert.Equal(MenuItemToggleType.Radio, light.ToggleType);
        Assert.Equal(MenuItemToggleType.CheckBox, Item(h, AutomationIds.Menu("view.statusBar")).ToggleType);
        Assert.True(light.IsChecked);

        await h.ExecuteAsync("view.theme.dark");

        Assert.True(dark.IsChecked);
        Assert.False(light.IsChecked);
    }

    private static Avalonia.Input.KeyGesture Gesture(string text)
    {
        var (key, modifiers) = KeyMapping.ToAvalonia(CoreGesture.Parse(text));
        return new Avalonia.Input.KeyGesture(key, modifiers);
    }

    private static CommandDefinition? Definition(MenuItem item) =>
        item.Command is IdeCommand command ? command.Definition : null;

    private static MenuItem Item(HeadlessHarness h, string id) =>
        AllItems(h).FirstOrDefault(i => AutomationIds.Get(i) == id) ?? new MenuItem();

    private static IEnumerable<MenuItem> AllItems(HeadlessHarness h) => Descendants(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<object>());

    private static IEnumerable<MenuItem> Descendants(IEnumerable<object?> items) =>
        items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(Descendants(i.Items.Cast<object?>())));

    private static List<string> Flatten(IEnumerable<object?> items, int depth) =>
        [.. items.SelectMany(i => i switch
        {
            Separator => [Indent(depth) + "---"],
            MenuItem m => new[] { Indent(depth) + AutomationIds.Get(m) + "|" + ToSpecLabel((string)m.Header!) }
                .Concat(Flatten(m.Items.Cast<object?>(), depth + 1)),
            _ => [Indent(depth) + "?" + i],
        })];

    private static List<string> Flatten(IEnumerable<SpecNode> nodes, int depth) =>
        [.. nodes.SelectMany(n => new[] { Indent(depth) + n.Text }.Concat(Flatten(n.Children, depth + 1)))];

    private static string Indent(int depth) => new(' ', depth * 2);

    /// <summary>Avalonia access text (<c>_</c>) back to the spec's <c>&amp;</c> notation.</summary>
    private static string ToSpecLabel(string header) => header.Replace("__", "\0", StringComparison.Ordinal).Replace('_', '&').Replace('\0', '_');

    /// <summary>menus.md §2–§11 as a tree; dynamic lists are empty in Phase 1 and separators they leave dangling are dropped.</summary>
    private static List<SpecNode> SpecTree()
    {
        var path = SpecTables.DocPath("spec/ui/menus.md");
        var top = new List<SpecNode>();
        foreach (var (section, key, label) in TopMenus)
        {
            var menu = new SpecNode($"{AutomationIds.TopMenu(key)}|{label}", []);
            SpecNode? submenu = null;
            foreach (var row in SpecTables.Read(path, section))
            {
                var cell = row["Label"];
                var isChild = cell.StartsWith('├') || cell.StartsWith('└');
                var text = cell.TrimStart('├', '└', ' ').Trim();
                var command = row["Command id"];
                SpecNode? node = text switch
                {
                    "─" => new SpecNode("---", []),
                    _ when text.StartsWith("*(", StringComparison.Ordinal) => null, // dynamic list, empty in Phase 1
                    _ when command.StartsWith("submenu ", StringComparison.Ordinal) =>
                        new SpecNode($"{command["submenu ".Length..]}|{SubmenuLabel().Replace(text, "")}", []),
                    _ => new SpecNode($"Menu.{command}|{RunLabel().Replace(text, "")}", []),
                };

                if (isChild)
                {
                    if (node is not null)
                    {
                        submenu!.Children.Add(node);
                    }

                    continue;
                }

                submenu = node is not null && command.StartsWith("submenu ", StringComparison.Ordinal) ? node : null;
                if (node is not null)
                {
                    menu.Children.Add(node);
                }
            }

            top.Add(menu);
        }

        Normalize(top);
        return top;
    }

    /// <summary>Drops leading, trailing and doubled separators on every level.</summary>
    private static void Normalize(List<SpecNode> nodes)
    {
        var result = new List<SpecNode>();
        foreach (var node in nodes)
        {
            Normalize(node.Children);
            if (node.Text == "---" && (result.Count == 0 || result[^1].Text == "---"))
            {
                continue;
            }

            result.Add(node);
        }

        if (result.Count > 0 && result[^1].Text == "---")
        {
            result.RemoveAt(result.Count - 1);
        }

        nodes.Clear();
        nodes.AddRange(result);
    }

    [GeneratedRegex(@"\s*▸\s*$")]
    private static partial Regex SubmenuLabel();

    [GeneratedRegex(@"\s*\(label .*\)$")]
    private static partial Regex RunLabel();

    private sealed record SpecNode(string Text, List<SpecNode> Children);
}
