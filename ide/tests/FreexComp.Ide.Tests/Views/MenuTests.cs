using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Tests.Infrastructure;
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
        var file = Item(h, AutomationIds.TopMenu("File"));
        file.Open();
        Dispatcher.UIThread.RunJobs();
        var save = Item(h, AutomationIds.Menu("file.save"));

        Assert.Equal("Ctrl+S", ShownGesture(save));
        Assert.Null(save.HotKey); // display only: key routing owns the gestures (IDE-ARCH-043)
        Assert.Null(save.InputGesture);
        Assert.Equal("", ShownGesture(Item(h, AutomationIds.Menu("file.saveAs"))));

        h.Services.Keymap.SetGestures("file.save", [CoreGesture.Parse("Ctrl+Shift+F2"), CoreGesture.Parse("F2")]);
        Assert.Equal("Ctrl+Shift+F2", ShownGesture(save)); // first of several
        file.Close();

        var view = Item(h, AutomationIds.TopMenu("View"));
        view.Open();
        Dispatcher.UIThread.RunJobs();
        var zoom = Item(h, AutomationIds.SubMenu("View", "Zoom"));
        zoom.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Ctrl+=", ShownGesture(Item(h, AutomationIds.Menu("view.zoomIn")))); // UI-KEY-001 spelling, not the toolkit's
        Assert.Equal("Ctrl+-", ShownGesture(Item(h, AutomationIds.Menu("view.zoomOut"))));
        zoom.Close();
        view.Close();
    }

    /// <summary>The shortcut text the menu item's template shows.</summary>
    private static string ShownGesture(MenuItem item)
    {
        var text = item.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == "PART_InputGestureText");
        Assert.NotNull(text);
        Assert.True(string.IsNullOrEmpty(text.Text) || text.IsEffectivelyVisible, $"{AutomationIds.Get(item)}: shortcut text hidden");
        return text.Text ?? "";
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

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-009")]
    [Trait("Req", "IDE-P1-131")]
    public async Task RecentListsAreGenerated()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var recent = h.Services.RecentItems;
        var openRecent = Item(h, AutomationIds.SubMenu("File", "OpenRecent"));
        Assert.False(openRecent.IsEnabled); // no recent item yet

        const string longPath = @"C:\Users\someone\Documents\FreexComp Projects\A very long folder name\Hello\Hello.pproj";
        recent.Add(RecentKind.Project, @"C:\p\One\One.pproj");
        recent.Add(RecentKind.Project, longPath);
        for (var n = 1; n <= 12; n++)
            recent.Add(RecentKind.File, $@"C:\w\f{n}.pas");
        recent.SetPinned(RecentKind.File, @"C:\w\f1.pas", true);

        var file = Item(h, AutomationIds.TopMenu("File"));
        file.Open();
        Dispatcher.UIThread.RunJobs();
        openRecent = Item(h, AutomationIds.SubMenu("File", "OpenRecent"));
        Assert.True(openRecent.IsEnabled);
        var rows = Flatten(openRecent.Items.Cast<object?>(), 0);

        // Projects (pinned first, then newest), files (max 10), Clear; the first nine get digit mnemonics.
        string[] files = [@"C:\w\f1.pas", .. Enumerable.Range(4, 9).Reverse().Select(n => $@"C:\w\f{n}.pas")];
        Assert.Equal(
            [
                AutomationIds.Menu("file.openRecentProject", longPath) + @"|&1 C:\Users\…\A very long folder name\Hello\Hello.pproj",
                AutomationIds.Menu("file.openRecentProject", @"C:\p\One\One.pproj") + @"|&2 C:\p\One\One.pproj",
                "---",
                .. files.Select((f, i) => AutomationIds.Menu("file.openRecentFile", f) + "|" + (i < 7 ? $"&{i + 3} " : "") + f),
                "---",
                AutomationIds.Menu("file.clearRecent") + "|&Clear Recent Lists",
            ],
            rows);
        var longItem = Item(h, AutomationIds.Menu("file.openRecentProject", longPath));
        Assert.Equal(longPath, ToolTip.GetTip(longItem));
        Assert.True(((string)longItem.Header!).Length <= 60 + 3);
        Assert.True(longItem.IsEffectivelyEnabled);

        // The lists follow the service: clearing leaves the pinned file.
        await h.ExecuteAsync("file.clearRecent");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            [AutomationIds.Menu("file.openRecentFile", @"C:\w\f1.pas") + @"|&1 C:\w\f1.pas", "---", AutomationIds.Menu("file.clearRecent") + "|&Clear Recent Lists"],
            Flatten(openRecent.Items.Cast<object?>(), 0));
        Assert.True(openRecent.IsEnabled);
        openRecent.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.False(Item(h, AutomationIds.Menu("file.clearRecent")).IsEffectivelyEnabled); // only a pinned entry left
        Assert.True(Item(h, AutomationIds.Menu("file.openRecentFile", @"C:\w\f1.pas")).IsEffectivelyEnabled);
        openRecent.Close();
        file.Close();
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
