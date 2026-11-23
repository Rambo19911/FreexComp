using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
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
        await using var h = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 1); // UI-TB-005 at the implemented phase 1
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

    /// <summary>I-193: UI-TB-005 at the product's phase 3: Standard and Build are shown, Debug (P6) and Designer (P5) are not.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "3")]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-TB-002")]
    [Trait("Req", "UI-TB-005")]
    public async Task StandardAndBuildToolbarsMatchSpecAtDefaultPhase()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var rows = SpecTables.Read(SpecTables.DocPath("spec/ui/toolbars.md"), "2.1 Standard");
        var expected = rows.Select(r => r["Element"] == "Separator" ? "---" : $"ToolBar.{r["Command id"]}|{r["Icon name"].Split(' ')[0]}").ToList();
        var actual = h.Find<Panel>("ToolBar.Standard").Children.Select(c => c is Button b
            ? $"{AutomationIds.Get(b)}|{Assert.IsType<IdeIcon>(b.Content).IconName}"
            : "---").ToList();
        Assert.Equal(expected, actual);

        // Build and Run (toolbars.md §2.2, P3): the buttons in table order (the combo boxes are tested in BuildToolbarTests).
        Assert.Equal(["project.build", "project.cancelBuild", "run.start", "run.startWithoutDebugging", "run.stop", "run.restart"],
            h.Find<Panel>("ToolBar.Build").Children.OfType<Button>().Select(b => AutomationIds.Get(b)!["ToolBar.".Length..]));

        var row = h.Find<Panel>("ToolBar.Standard").FindAncestorOfType<ToolBarRow>()!;
        Assert.Equal(["ToolBar.Standard", "ToolBar.Build"], row.InRow.Select(c => AutomationIds.Get(c)));
        Assert.True(h.Find<Button>(AutomationIds.ToolBarButton("search.findInFiles")).IsEffectivelyEnabled); // P3 button: enabled now
        Assert.Null(h.TryFind<Control>("ToolBar.Debug"));
        Assert.Null(h.TryFind<Control>("ToolBar.Designer"));
        Assert.Equal(32, row.Bounds.Height);
    }

    /// <summary>I-193: <see cref="ToggleCommandHidesToolbarAndCollapsesRow"/> with the product's Standard and Build toolbars.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "3")]
    [Trait("Req", "UI-TB-004")]
    [Trait("Req", "IDE-P1-022")]
    public async Task ToggleCommandHidesToolbarAndCollapsesRowAtDefaultPhase()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var row = h.Find<Panel>("ToolBar.Standard").FindAncestorOfType<ToolBarRow>()!;

        await h.ExecuteAsync("view.toolbar.standard");
        Assert.Null(h.TryFind<Control>("ToolBar.Standard"));
        Assert.NotNull(h.TryFind<Control>("ToolBar.Build"));
        Assert.True(row.IsVisible); // Build is still shown

        await h.ExecuteAsync("view.toolbar.build");
        Assert.False(h.Services.Commands.Get("view.toolbar.build").IsChecked);
        Assert.Null(h.TryFind<Control>("ToolBar.Build"));
        Assert.False(row.IsVisible); // all hidden: the row takes no space

        await h.ExecuteAsync("view.toolbar.build");
        await h.ExecuteAsync("view.toolbar.standard");
        Assert.Equal(["ToolBar.Standard", "ToolBar.Build"], row.InRow.Select(c => AutomationIds.Get(c)));
        Assert.True(row.IsVisible);
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
    [Trait("Req", "UI-WIN-061")]
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
        await using var h = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 1); // the Standard toolbar is the only one
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

    /// <summary>I-130: the Designer toolbar (toolbars.md §2.4) is shown only while a Design view is the active document view.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-TB-004")]
    [Trait("Req", "UI-TB-005")]
    public async Task DesignerToolbarVisibleOnlyInDesignView()
    {
        await using var h = await HeadlessHarness.StartAsync(DesignerViewTests.Phase5);
        Assert.Null(h.TryFind<Control>("ToolBar.Designer"));
        var document = await DesignerViewTests.OpenFormAsync(h);
        Assert.Null(h.TryFind<Control>("ToolBar.Designer")); // the Code view

        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        var rows = SpecTables.Read(SpecTables.DocPath("spec/ui/toolbars.md"), "2.4 Designer");
        var expected = rows.Select(r => r["Element"] == "Separator" ? "---" : $"ToolBar.{r["Command id"]}|{r["Icon name"]}|{r["Element"] == "Toggle"}").ToList();
        var designer = h.Find<Panel>("ToolBar.Designer");
        var actual = designer.Children.Select(c => c is Button b
            ? $"{AutomationIds.Get(b)}|{Assert.IsType<IdeIcon>(b.Content).IconName}|{b is ToggleButton}"
            : "---").ToList();
        Assert.Equal(expected, actual);
        var row = designer.FindAncestorOfType<ToolBarRow>()!;
        Assert.Equal(["ToolBar.Standard", "ToolBar.Build", "ToolBar.Designer"], row.InRow.Select(c => AutomationIds.Get(c)));

        // The buttons work on the active design surface; the toggles show their state.
        var grid = h.Find<ToggleButton>(AutomationIds.ToolBarButton("designer.toggleGrid"));
        Assert.True(grid.IsChecked);
        Assert.True(grid.IsEffectivelyEnabled);
        h.Click(AutomationIds.ToolBarButton("designer.toggleGrid"));
        Assert.False(h.Services.Settings.Get(SettingsCatalog.DesignerShowGrid));
        Assert.False(grid.IsChecked);
        Assert.False(h.Find<Button>(AutomationIds.ToolBarButton("designer.alignLeft")).IsEffectivelyEnabled); // the form alone is selected
        document.Designer.Surface!.Selection.Set(["NameEdit", "GreetButton"]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Button>(AutomationIds.ToolBarButton("designer.alignLeft")).IsEffectivelyEnabled);
        h.Click(AutomationIds.ToolBarButton("designer.alignTop"));
        int Top(string name) => FreexComp.Designer.Model.PropertyAccess.GetInt(document.Designer.Model!.FindByName(name)!, "Top");
        Assert.Equal(Top("NameEdit"), Top("GreetButton"));
        Assert.True(document.IsModified);

        // View Code from the toolbar: the Code view, the toolbar goes.
        h.Click(AutomationIds.ToolBarButton("view.toggleFormCode"));
        Assert.Equal(DocumentView.Code, document.ActiveView);
        Assert.Null(h.TryFind<Control>("ToolBar.Designer"));

        // Its visibility setting still applies (view.toolbar.designer).
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.NotNull(h.TryFind<Control>("ToolBar.Designer"));
        Assert.True(h.Services.Commands.Get("view.toolbar.designer").IsChecked);
        await h.ExecuteAsync("view.toolbar.designer");
        Assert.False(h.Services.Settings.Get(SettingsCatalog.AppearanceToolbarDesigner));
        Assert.Null(h.TryFind<Control>("ToolBar.Designer"));
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

    /// <summary>
    /// I-214: UI-TB-009 is in scope at Phase 5 — the Designer toolbar's toggles show the checked state of
    /// design-tokens.md §9 (bg.selected fill, 1 px accent.default border; released: no fill).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-TB-009")]
    public async Task DesignerTogglesShowTheCheckedState()
    {
        await using var h = await HeadlessHarness.StartAsync(DesignerViewTests.Phase5);
        await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Avalonia.Media.Color Token(string key) =>
            Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(h.Window.TryFindResource(key, h.Window.ActualThemeVariant, out var v) ? v : null).Color;
        foreach (var id in new[] { "designer.lockControls", "designer.tabOrder", "designer.toggleGrid" })
        {
            var toggle = h.Find<ToggleButton>(AutomationIds.ToolBarButton(id));
            foreach (var on in new[] { true, false })
            {
                toggle.IsChecked = on;
                Dispatcher.UIThread.RunJobs();
                var presenter = toggle.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                var fill = (presenter.Background as Avalonia.Media.ISolidColorBrush)?.Color;
                if (on)
                {
                    Assert.Equal(Token("FxcBgSelectedBrush"), fill);
                    Assert.Equal(Token("FxcAccentDefaultBrush"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(presenter.BorderBrush).Color);
                    Assert.Equal(new Avalonia.Thickness(1), presenter.BorderThickness);
                }
                else
                {
                    Assert.NotEqual(Token("FxcBgSelectedBrush"), fill);
                    Assert.NotEqual(Token("FxcAccentDefaultBrush"), fill);
                }
            }
        }
    }
}
