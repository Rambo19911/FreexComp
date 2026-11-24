using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Menus;

[Trait("Phase", "1")]
public sealed class MenuTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-003")]
    public void MnemonicsAreUniquePerLevel()
    {
        var problems = new List<string>();
        Check("(menu bar)", MainMenuDefinition.TopLevel, problems);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-002")]
    [Trait("Req", "UI-CMD-002")]
    public void EveryMenuCommandIsInTheCatalog()
    {
        var ids = CommandCatalog.All.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = Commands(MainMenuDefinition.TopLevel).Where(c => !ids.Contains(c.CommandId)).Select(c => c.CommandId).ToList();

        Assert.Empty(unknown);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-004")]
    [Trait("Req", "UI-MENU-011")]
    public void ModelDisablesLaterPhasesAndDerivesSubmenuState()
    {
        var services = TestComposition.Build();
        var menu = MainMenuModel.Build(services.Commands);
        var edit = menu.Single(m => m.Key == "Edit");
        var codeAssist = edit.Children.Single(m => m.Key == "Edit.CodeAssist"); // P3 children only
        var theme = menu.Single(m => m.Key == "View").Children.Single(m => m.Key == "View.Theme");

        Assert.All(menu, m => Assert.True(m.IsEnabled)); // UI-MENU-001
        Assert.False(codeAssist.IsEnabled);
        Assert.True(theme.IsEnabled); // view.theme.* have handlers
        Assert.Equal(MenuToggleKind.Radio, theme.Children[0].ToggleKind);
        Assert.Equal(MenuToggleKind.Check, menu.Single(m => m.Key == "View").Children.Single(m => m.CommandId == "view.statusBar").ToggleKind);

        var recent = menu.Single(m => m.Key == "File").Children.Single(m => m.Key == "File.OpenRecent");
        Assert.Equal(["file.clearRecent"], recent.Children.Select(c => c.CommandId)); // empty lists: no stray separators
        Assert.False(menu.Single(m => m.Key == "Window").Children[^1].IsSeparator);
    }

    [Fact(Timeout = 30000)]
    public Task SubmenuStateFollowsChildHandlers() => TestUiThread.RunAsync(() => SubmenuStateFollowsChildHandlersOnUiThread()); // I-209

    private async Task SubmenuStateFollowsChildHandlersOnUiThread()
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        var lineMenu = MainMenuModel.Build(services.Commands).Single(m => m.Key == "Edit").Children.Single(m => m.Key == "Edit.Line");
        var changes = 0;
        lineMenu.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(MenuItemModel.IsEnabled) ? 1 : 0;
        Assert.False(lineMenu.IsEnabled); // the line commands need a text document (I-048)

        fs.CreateDirectory(@"C:\p");
        fs.WriteAllBytes(@"C:\p\a.txt", "x\r\n"u8.ToArray());
        Assert.True((await services.Documents.OpenAsync(@"C:\p\a.txt")).Success);
        services.Commands.InvalidateCanExecute();

        Assert.True(lineMenu.IsEnabled);
        Assert.True(changes > 0);
    }

    private static void Check(string level, IReadOnlyList<MenuNode> nodes, List<string> problems)
    {
        var letters = new Dictionary<char, string>();
        foreach (var node in nodes)
        {
            if (node is not (CommandMenuNode or SubmenuNode))
            {
                continue;
            }

            var label = MenuLabels.For(node);
            if (MenuLabels.Mnemonic(label) is not { } letter)
            {
                problems.Add($"{level}: '{label}' has no mnemonic");
            }
            else if (!letters.TryAdd(letter, label))
            {
                problems.Add($"{level}: '{label}' and '{letters[letter]}' share '{letter}'");
            }

            if (node is SubmenuNode sub)
            {
                Check(sub.Key, sub.Children, problems);
            }
        }
    }

    /// <summary>I-226: Breakpoint Properties has no main-menu, toolbar or editor-menu entry; the breakpoint glyph and the Breakpoints rows offer it.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "6")]
    [Trait("Req", "UI-MENU-021")]
    public void BreakpointPropertiesOnlyInGutterAndBreakpointsMenus()
    {
        const string Id = "run.breakpointProperties";
        var services = TestComposition.Build(o => o.ImplementedPhase = 6);
        var debug = services.DebugShell!;

        Assert.DoesNotContain(Commands(MainMenuDefinition.TopLevel), c => c.CommandId == Id);
        Assert.DoesNotContain(ToolBarDefinitions.All.SelectMany(t => t.Items).OfType<ToolBarButton>(), b => b.CommandId == Id);
        Assert.DoesNotContain(Id, EditorContextMenu.Commands);
        Assert.DoesNotContain(Id, MessagesViewModel.ContextCommands);
        Assert.DoesNotContain(Id, OutputViewModel.ContextCommands);
        DebugGridViewModel[] others = [debug.CallStack, debug.Locals, debug.Watches, debug.Threads, debug.EventLog];
        Assert.All(others, grid => Assert.DoesNotContain(Id, grid.ContextCommands));
        Assert.DoesNotContain(GutterMenu.Build([], services.Commands, services.Settings), m => m.CommandId == Id); // no breakpoint on the line

        Assert.Contains(GutterMenu.Build([new LineGlyph(LineGlyphKind.Breakpoint)], services.Commands, services.Settings), m => m.CommandId == Id);
        Assert.Contains(Id, debug.Breakpoints.ContextCommands);
    }

    private static IEnumerable<CommandMenuNode> Commands(IEnumerable<MenuNode> nodes) =>
        nodes.SelectMany(n => n switch
        {
            CommandMenuNode c => [c],
            SubmenuNode s => Commands(s.Children),
            _ => [],
        });
}

[Trait("Phase", "1")]
public sealed class ToolBarRowModelTests
{
    [Theory]
    [Trait("Req", "UI-TB-006")]
    [InlineData(1000, 3)] // 100 + 9 + 200 + 9 + 300 = 618: all fit, no overflow button
    [InlineData(618, 3)]
    [InlineData(617, 2)]  // overflow button (20) + 100 + 9 + 200 = 329
    [InlineData(329, 2)]
    [InlineData(328, 1)]
    [InlineData(119, 0)]  // not even the first toolbar beside the button
    public void FittingCountKeepsWholeToolbarsFromTheLeft(double available, int expected) =>
        Assert.Equal(expected, ToolBarRowModel.FittingCount([100, 200, 300], 9, available, 20));
}
