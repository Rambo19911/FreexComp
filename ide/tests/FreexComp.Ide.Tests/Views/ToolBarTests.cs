using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ToolBarTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-TB-002")]
    [Trait("Req", "UI-TB-003")]
    [Trait("Req", "UI-TB-005")]
    public async Task StandardToolbarMatchesSpec()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var rows = SpecTables.Read(SpecTables.DocPath("spec/ui/toolbars.md"), "2.1 Standard");
        var expected = rows.Select(r => r["Element"] == "Separator" ? "---" : $"ToolBar.{r["Command id"]}|{r["Icon name"].Split(' ')[0]}").ToList();

        var standard = h.Find<Panel>("ToolBar.Standard");
        var actual = standard.Children.Select(c => c is Button b
            ? $"{AutomationIds.Get(b)}|{Assert.IsType<IdeIcon>(b.Content).IconName}"
            : "---").ToList();

        Assert.Equal(expected, actual);
        var save = h.Find<Button>(AutomationIds.ToolBarButton("file.save"));
        Assert.Equal(28, save.Bounds.Width);
        Assert.Equal(28, save.Bounds.Height);
        Assert.Equal(16, Assert.IsType<IdeIcon>(save.Content).Bounds.Width);
        Assert.False(h.Find<Button>(AutomationIds.ToolBarButton("search.findInFiles")).IsEffectivelyEnabled); // P3 button: shown disabled
        Assert.Null(h.TryFind<Control>("ToolBar.Build")); // P3 toolbar: not shown (UI-TB-005)
        Assert.Null(h.TryFind<Control>("ToolBar.Debug"));
        Assert.Null(h.TryFind<Control>("ToolBar.Designer"));
        Assert.Equal(32, standard.FindAncestorOfType<ToolBarRow>()!.Bounds.Height);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-003")]
    [Trait("Req", "UI-KEY-005")]
    public async Task TooltipShowsTitleAndShortcut()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var saveAll = h.Find<Button>(AutomationIds.ToolBarButton("file.saveAll"));

        Assert.Equal("Save All (Ctrl+Shift+S)", ToolTip.GetTip(saveAll));
        Assert.Equal("Save All", AutomationProperties.GetName(saveAll));

        h.Services.Keymap.SetGestures("file.saveAll", []);
        Assert.Equal("Save All", ToolTip.GetTip(saveAll)); // no shortcut: title only

        h.Services.Keymap.SetGestures("file.saveAll", [CoreGesture.Parse("Ctrl+Shift+F3")]);
        Assert.Equal("Save All (Ctrl+Shift+F3)", ToolTip.GetTip(saveAll));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-006")]
    public async Task OverflowMovesWholeToolbars()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var row = h.Find<Panel>("ToolBar.Standard").FindAncestorOfType<ToolBarRow>()!;
        row.Populate(ToolBarRowModel.Build(h.Services.Commands, h.Services.Settings, implementedPhase: 3)); // Standard + Build
        row.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        Dispatcher.UIThread.RunJobs();
        var overflow = h.Find<Button>("ToolBar.Overflow");
        Assert.False(overflow.IsVisible); // everything fits in 1440

        row.Width = 500;
        Dispatcher.UIThread.RunJobs();

        Assert.True(overflow.IsVisible);
        Assert.Equal(["ToolBar.Standard"], row.InRow.Select(c => AutomationIds.Get(c)));
        Assert.Equal(["ToolBar.Build"], row.InOverflow.Select(c => AutomationIds.Get(c))); // whole toolbar, from the right
        Assert.Null(h.TryFind<Control>("ToolBar.Build")); // not in the row any more
        Assert.True(row.InRow.Concat([overflow]).Sum(c => c.Bounds.Width) <= 500);

        row.Width = 200; // not even Standard fits
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(row.InRow);
        Assert.Equal(["ToolBar.Standard", "ToolBar.Build"], row.InOverflow.Select(c => AutomationIds.Get(c)));

        row.Width = double.NaN; // back to the window's full width
        row.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Dispatcher.UIThread.RunJobs();
        Assert.False(overflow.IsVisible);
        Assert.Equal(["ToolBar.Standard", "ToolBar.Build"], row.InRow.Select(c => AutomationIds.Get(c)));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-004")]
    [Trait("Req", "IDE-P1-022")]
    public async Task ToggleCommandHidesToolbarAndCollapsesRow()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var row = h.Find<Panel>("ToolBar.Standard").FindAncestorOfType<ToolBarRow>()!;
        Assert.True(h.Services.Commands.Get("view.toolbar.standard").IsChecked);

        await h.ExecuteAsync("view.toolbar.standard");

        Assert.False(h.Services.Settings.Get(SettingsCatalog.AppearanceToolbarStandard));
        Assert.False(h.Services.Commands.Get("view.toolbar.standard").IsChecked);
        Assert.Null(h.TryFind<Control>("ToolBar.Standard"));
        Assert.False(row.IsVisible); // all hidden: the row takes no space

        await h.ExecuteAsync("view.toolbar.standard");
        Assert.NotNull(h.TryFind<Control>("ToolBar.Standard"));
        Assert.True(row.IsVisible);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-004")]
    public async Task RowContextMenuListsTheFourToggles()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var row = h.Find<Panel>("ToolBar.Standard").FindAncestorOfType<ToolBarRow>()!;

        var items = Assert.IsType<ContextMenu>(row.ContextMenu).Items.Cast<MenuItem>().Select(AutomationIds.Get);

        Assert.Equal(
            ["Context.view.toolbar.standard", "Context.view.toolbar.build", "Context.view.toolbar.debug", "Context.view.toolbar.designer"],
            items);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-008")]
    public async Task CommandIconNameComesFromToolbarTable()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var commands = h.Services.Commands;

        foreach (var definition in commands.Definitions)
        {
            Assert.Equal(ToolBarDefinitions.IconOf(definition.Id), commands.Get(definition.Id).IconName);
        }

        Assert.Equal("save", commands.Get("file.save").IconName);
        Assert.Equal("run-debug", commands.Get("run.start").IconName);
        Assert.Null(commands.Get("file.saveAs").IconName);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-008")]
    [Trait("Req", "UI-TOK-041")]
    public async Task MenuShowsIconsOfToolbarCommandsOnly()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var items = h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>()).ToList();
        MenuItem Item(string commandId) => items.Single(i => AutomationIds.Get(i) == AutomationIds.Menu(commandId));

        Assert.Equal("save", Assert.IsType<IdeIcon>(Item("file.save").Icon).IconName);
        Assert.Equal("find-in-files", Assert.IsType<IdeIcon>(Item("search.findInFiles").Icon).IconName);
        Assert.Null(Item("file.saveAs").Icon);
        Assert.Equal("undo", ToolBarDefinitions.IconOf("edit.undo"));
        Assert.Null(ToolBarDefinitions.IconOf("file.saveAs"));
    }
}
