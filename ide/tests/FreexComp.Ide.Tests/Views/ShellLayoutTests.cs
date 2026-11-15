using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Geometry and tokens of the shell regions: docks, toolbar row, tool-window frames, tab strips, hit targets.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ShellLayoutTests
{
    private static Rect InWindow(HeadlessHarness h, Control control) =>
        new(control.TranslatePoint(default, h.Window) ?? throw new InvalidOperationException("not in the window"), control.Bounds.Size);

    private static Color ColorOf(HeadlessHarness h, string brushKey) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.TryGetResource(brushKey, h.Window.ActualThemeVariant, out var value) ? value : null).Color;

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Task<HeadlessHarness> StartWithAllDocks() => HeadlessHarness.StartAsync(o =>
    {
        o.ToolWindows.Add(SampleToolViewModel.Definition("ProjectManager", ToolGroupId.Left, titleKey: "Cmd_view_projectManager"));
        o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom));
        o.ToolWindows.Add(SampleToolViewModel.Definition("Sample", ToolGroupId.RightUpper));
    });

    /// <summary>UI-WIN-001: the side docks span toolbar row to status bar; the bottom dock lies between them under the documents.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-001")]
    public async Task SideDocksSpanFullHeightBottomDockSitsBetweenThem()
    {
        await using var h = await StartWithAllDocks();
        h.Services.ToolWindows.Show("Sample", focus: false);
        Dispatcher.UIThread.RunJobs();

        var toolbar = InWindow(h, h.Window.FindControl<ToolBarRow>("ToolBars")!);
        var status = InWindow(h, h.Find<Control>(AutomationIds.StatusBar));
        var left = InWindow(h, h.Find<Control>(AutomationIds.DockLeft));
        var right = InWindow(h, h.Find<Control>(AutomationIds.DockRight));
        var bottom = InWindow(h, h.Find<Control>(AutomationIds.DockBottom));
        var documents = InWindow(h, h.Find<Control>(AutomationIds.DocumentArea));

        foreach (var side in new[] { left, right })
        {
            Assert.Equal(toolbar.Bottom, side.Top, 1);
            Assert.Equal(status.Top, side.Bottom, 1);
        }

        Assert.True(bottom.Left >= left.Right && bottom.Right <= right.Left, $"bottom dock {bottom} between {left} and {right}");
        Assert.Equal(status.Top, bottom.Bottom, 1);
        Assert.True(bottom.Top >= documents.Bottom, "the bottom dock is under the document area");
        Assert.Equal(documents.Left, bottom.Left, 1);
        Assert.Equal(documents.Right, bottom.Right, 1);
    }

    /// <summary>UI-TB-001: one 32-high row; toolbars in the fixed order, separated by 1 px border.default lines, space.4 each side, height 24.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-001")]
    public async Task ToolbarRowOrderAndSeparators()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var row = h.Window.FindControl<ToolBarRow>("ToolBars")!;
        Assert.Equal(32, row.Bounds.Height);
        Assert.Equal(["Standard", "Build", "Debug", "Designer"], Core.Menus.ToolBarDefinitions.All.Select(d => d.Name));

        // Phase 1 shows only Standard; with the toolbars of later phases reached, Standard and Build share the row
        // (Debug and Designer also need their context, UI-TB-004).
        row.Populate(Core.Menus.ToolBarRowModel.Build(h.Services.Commands, h.Services.Settings, implementedPhase: 8));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(32, row.Bounds.Height);
        var bars = row.GetVisualDescendants().OfType<StackPanel>()
            .Where(p => AutomationIds.Get(p) is { } id && id.StartsWith("ToolBar.", StringComparison.Ordinal) && p.IsEffectivelyVisible)
            .OrderBy(p => InWindow(h, p).Left).Select(p => AutomationIds.Get(p)!).ToList();
        Assert.Equal(["ToolBar.Standard", "ToolBar.Build"], bars);
        var host = row.GetVisualDescendants().OfType<StackPanel>().First(p => p.Children.OfType<StackPanel>().Any(c => AutomationIds.Get(c) == "ToolBar.Standard"));
        var separators = host.Children.OfType<Rectangle>().ToList();
        Assert.Equal(host.Children.OfType<StackPanel>().Count() - 1, separators.Count);
        Assert.All(separators, s =>
        {
            Assert.Equal(1, s.Width);
            Assert.Equal(32 - 8, s.Height);
            Assert.Equal(new Thickness(8, 0), s.Margin);
            Assert.Equal(ColorOf(h, "FxcBorderDefaultBrush"), ColorOf(s.Fill));
        });
    }

    /// <summary>UI-TW-001: caption bar (26), tool bar (28), content; the dock's tab strip (26) at the bottom, shown for a single window.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-001")]
    public async Task ToolWindowFrameRegions()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom)));
        var frame = h.Find<ToolWindowFrame>("Tool.Messages");
        var dock = InWindow(h, h.Find<Control>(AutomationIds.DockBottom));
        var regions = frame.Children.OrderBy(c => c.Bounds.Top).ToList();

        Assert.Equal(26, regions[0].Bounds.Height); // caption bar
        Assert.Contains(h.Find<TextBlock>("Tool.Messages.Caption"), regions[0].GetVisualDescendants());
        Assert.Equal(28, regions[1].Bounds.Height); // tool bar with the filter box
        Assert.Contains(h.Find<TextBox>("Tool.Messages.Filter"), regions[1].GetVisualDescendants());
        Assert.Contains(h.Find<ListBox>("Tool.Messages.List"), regions[2].GetVisualDescendants()); // content

        var tab = InWindow(h, h.Find<Button>("ToolTab.Messages")); // the only tool window of the dock still has its tab
        Assert.Equal(26, tab.Height);
        Assert.Equal(dock.Bottom, tab.Bottom, 1);
        Assert.True(InWindow(h, frame).Bottom <= tab.Top + 0.5, "the tab strip is below the frame");
    }

    /// <summary>
    /// UI-TW-003: text-only tabs (type.caption, padding space.4); active bg.panel / text.primary / 2 px accent bottom line,
    /// inactive bg.sunken / text.secondary; no close buttons; a chevron lists every tool window when the tabs do not fit.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-003")]
    public async Task ToolTabsAndOverflowChevron()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 1; // only the sample tool windows fill the left dock
            o.ToolWindows.Add(SampleToolViewModel.Definition("ProjectManager", ToolGroupId.Left, titleKey: "Cmd_view_projectManager"));
            o.ToolWindows.Add(SampleToolViewModel.Definition("Sample", ToolGroupId.Left, titleKey: "Cmd_view_searchResults"));
            o.ToolWindows.Add(SampleToolViewModel.Definition("Other", ToolGroupId.Left, titleKey: "Cmd_view_output"));
        });
        var tools = h.Services.ToolWindows;
        tools.Show("Sample", focus: false);
        tools.Show("Other", focus: false);
        tools.Show("ProjectManager", focus: false);
        Dispatcher.UIThread.RunJobs();

        var active = h.Find<Button>("ToolTab.ProjectManager");
        var inactive = h.Find<Button>("ToolTab.Sample");
        Assert.IsType<TextBlock>(active.Content);
        Assert.Contains("caption", ((TextBlock)active.Content!).Classes);
        Assert.Equal(new Thickness(8, 0), active.Padding);
        Assert.Equal(ColorOf(h, "FxcBgPanelBrush"), ColorOf(active.Background));
        Assert.Equal(ColorOf(h, "FxcTextPrimaryBrush"), ColorOf(((TextBlock)active.Content!).Foreground));
        Assert.Equal(new Thickness(0, 0, 0, 2), active.BorderThickness);
        Assert.Equal(ColorOf(h, "FxcAccentDefaultBrush"), ColorOf(active.BorderBrush));
        Assert.Equal(ColorOf(h, "FxcBgSunkenBrush"), ColorOf(inactive.Background));
        Assert.Equal(ColorOf(h, "FxcTextSecondaryBrush"), ColorOf(((TextBlock)inactive.Content!).Foreground));
        Assert.Equal(new Thickness(0), inactive.BorderThickness);
        Assert.Empty(active.GetVisualDescendants().OfType<Button>()); // no close buttons

        // Wide enough: no chevron. The narrowest left dock cannot hold the three tabs: the chevron lists them all.
        h.Services.Layout.Layout.LeftSize = 600;
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Button>(AutomationIds.DockLeft + ".TabOverflow").IsVisible);
        h.Services.Layout.Layout.LeftSize = 160;
        Dispatcher.UIThread.RunJobs();
        var chevron = h.Find<Button>(AutomationIds.DockLeft + ".TabOverflow");
        Assert.True(chevron.IsEffectivelyVisible);
        Assert.Equal(Core.Resources.Strings.ToolWindow_TabOverflow, Avalonia.Automation.AutomationProperties.GetName(chevron));

        h.Click(AutomationIds.DockLeft + ".TabOverflow");
        var menu = Assert.IsType<ContextMenu>(chevron.ContextMenu);
        var items = menu.Items.Cast<MenuItem>().ToList();
        Assert.Equal(
            [AutomationIds.DockLeft + ".TabOverflow.ProjectManager", AutomationIds.DockLeft + ".TabOverflow.Sample", AutomationIds.DockLeft + ".TabOverflow.Other"],
            items.Select(AutomationIds.Get));
        Assert.True(items[0].IsChecked);
        items[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Other", h.Services.Layout.Layout.Left.ActiveTool);
        menu.Close();
    }

    /// <summary>I-193: the tab look and the overflow chevron with the product's Phase 3 left dock (Project Manager, Structure).</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "3")]
    [Trait("Req", "UI-TW-003")]
    public async Task ToolTabsAndOverflowChevronAtDefaultPhase()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var active = h.Find<Button>("ToolTab.ProjectManager");
        var inactive = h.Find<Button>("ToolTab.Structure");
        Assert.Equal(ColorOf(h, "FxcBgPanelBrush"), ColorOf(active.Background));
        Assert.Equal(new Thickness(0, 0, 0, 2), active.BorderThickness);
        Assert.Equal(ColorOf(h, "FxcBgSunkenBrush"), ColorOf(inactive.Background));
        Assert.Equal(ColorOf(h, "FxcTextSecondaryBrush"), ColorOf(((TextBlock)inactive.Content!).Foreground));
        Assert.Equal(new Thickness(0), inactive.BorderThickness);
        Assert.False(h.Find<Button>(AutomationIds.DockBottom + ".TabOverflow").IsVisible); // three tabs fit in the window width

        // Wide enough: no chevron (the headless test font is wider than the product font, so 260 may not hold both).
        h.Services.Layout.Layout.LeftSize = 600;
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Button>(AutomationIds.DockLeft + ".TabOverflow").IsVisible);
        h.Services.Layout.Layout.LeftSize = 160;
        Dispatcher.UIThread.RunJobs();
        var chevron = h.Find<Button>(AutomationIds.DockLeft + ".TabOverflow");
        Assert.True(chevron.IsEffectivelyVisible);
        h.Click(AutomationIds.DockLeft + ".TabOverflow");
        var menu = Assert.IsType<ContextMenu>(chevron.ContextMenu);
        var items = menu.Items.Cast<MenuItem>().ToList();
        Assert.Equal([AutomationIds.DockLeft + ".TabOverflow.ProjectManager", AutomationIds.DockLeft + ".TabOverflow.Structure"], items.Select(AutomationIds.Get));
        Assert.True(items[0].IsChecked);
        items[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Structure", h.Services.Layout.Layout.Left.ActiveTool);
        menu.Close();
    }

    /// <summary>
    /// UI-ED-070: the document tab strip is 30 high on bg.sunken with a 1 px border.default bottom line; tabs 30 high,
    /// 80…240 wide, padding 10, separated by 1 px border.subtle; the active tab bg.editor, bodyStrong text.primary, 2 px
    /// tab.active.indicator on top; inactive transparent with text.secondary.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-070")]
    public async Task DocumentTabStripMetrics()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "unit A;\n";
            o.InitialFiles["LongerFileNameForTheSecondTabThatGetsTrimmedAtTheMaximumTabWidth.pas"] = "unit B;\n";
        });
        await h.Services.Documents.OpenAsync(System.IO.Path.Combine(h.TempDir, "LongerFileNameForTheSecondTabThatGetsTrimmedAtTheMaximumTabWidth.pas"));
        await h.Services.Documents.OpenAsync(System.IO.Path.Combine(h.TempDir, "A.pas"));
        Dispatcher.UIThread.RunJobs();

        var strip = h.Window.GetVisualDescendants().OfType<DocumentTabStrip>().Single();
        Assert.Equal(30 + 1, strip.Bounds.Height);
        Assert.Equal(new Thickness(0, 0, 0, 1), strip.BorderThickness);
        Assert.Equal(ColorOf(h, "FxcBgSunkenBrush"), ColorOf(strip.Background));
        Assert.Equal(ColorOf(h, "FxcBorderDefaultBrush"), ColorOf(strip.BorderBrush));

        var tabs = strip.GetVisualDescendants().OfType<DocumentTabView>().ToList();
        var active = tabs.Single(t => AutomationIds.Get(t) == "DocumentTab.A.pas");
        var inactive = tabs.Single(t => t != active);
        Assert.All(tabs, t =>
        {
            Assert.Equal(30, t.Bounds.Height);
            Assert.InRange(t.Bounds.Width, 80, 240);
        });
        Assert.Equal(240, inactive.MaxWidth); // a long title is trimmed inside the maximum width
        Assert.InRange(inactive.Bounds.Width, 200, 240);

        Assert.Equal(new Thickness(0, 2, 0, 0), active.BorderThickness);
        Assert.Equal(ColorOf(h, "FxcTabActiveIndicatorBrush"), ColorOf(active.BorderBrush));
        Assert.Equal(ColorOf(h, "FxcBgEditorBrush"), ColorOf(active.Background));
        var title = active.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "A.pas");
        Assert.Contains("bodyStrong", title.Classes);
        Assert.Equal(ColorOf(h, "FxcTextPrimaryBrush"), ColorOf(title.Foreground));
        var otherTitle = inactive.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.StartsWith("Longer", StringComparison.Ordinal) == true);
        Assert.Equal(ColorOf(h, "FxcTextSecondaryBrush"), ColorOf(otherTitle.Foreground));
        Assert.True(inactive.Background is null || ColorOf(inactive.Background).A == 0, "inactive tabs are transparent");

        var padding = active.GetVisualDescendants().OfType<Border>().First(b => b.Padding.Left > 0);
        Assert.Equal(new Thickness(10, 0), padding.Padding);
        var separator = active.GetVisualDescendants().OfType<Rectangle>().First(r => r.Width == 1);
        Assert.Equal(ColorOf(h, "FxcBorderSubtleBrush"), ColorOf(separator.Fill));
    }

    /// <summary>UI-A11Y-043: pointer targets are at least 16 × 16 DIP (tab close, gutter glyphs, tool-window buttons), rows at least 22 high.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-043")]
    public async Task HitTargetsAndRowsAreLargeEnough()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom));
            o.InitialFiles["A.pas"] = "unit A;\ninterface\nimplementation\nend.\n";
        });
        await h.Services.Documents.OpenAsync(System.IO.Path.Combine(h.TempDir, "A.pas"));
        Dispatcher.UIThread.RunJobs();

        foreach (var id in new[] { "DocumentTab.A.pas.Close", "Tool.Messages.Hide", "Tool.Messages.ToolBar.messages.clear" })
        {
            var target = h.Find<Control>(id).Bounds;
            Assert.True(target.Width >= 16 && target.Height >= 16, $"{id}: {target.Size}");
        }

        var glyphs = h.Window.GetVisualDescendants().OfType<FreexComp.Ide.Editor.GlyphMargin>().Single();
        Assert.True(glyphs.Bounds.Width >= 16, $"glyph margin {glyphs.Bounds.Width} wide");
        Assert.True(glyphs.Bounds.Height >= 16);

        var rows = h.Find<ListBox>("Tool.Messages.List").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.Bounds.Height >= 22, $"row {r.Bounds.Height} high"));
    }
}
