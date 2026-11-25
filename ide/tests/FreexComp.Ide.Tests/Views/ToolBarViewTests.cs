using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The platform combo of the Build tool bar (phase8-ui-additions.md §11, UI-TB-022, 023; platform-selector.md IDE-PLAT-004; task I-278).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ToolBarViewTests
{
    private const string NotInstalled = "Win32 (not installed)";

    private static IEnumerable<MenuItem> Descendants(IEnumerable<object?> items) =>
        items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(Descendants(i.Items.Cast<object?>())));

    private static MenuItem MenuItem(HeadlessHarness h, string id) =>
        Descendants(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<object>()).Single(i => AutomationIds.Get(i) == id);

    /// <summary>
    /// UI-TB-022, 023: the combo keeps its width of 88 and its name; its items are the labels of the listed platforms —
    /// the id with an upper-case first letter, a platform that is not installed with its suffix — each elided at its end
    /// when it does not fit and shown in full as its tooltip; the active platform is selected and choosing another one
    /// runs <c>project.setPlatform</c> (the status bar and the Project menu bullet follow). Disabled while not Idle, and
    /// with a project that lists one platform — whose label stays visible.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-022")]
    [Trait("Req", "UI-TB-023")]
    [Trait("Req", "IDE-PLAT-004")]
    public async Task PlatformComboRendersItemsAndDisabledState()
    {
        await using var h = await BuildScenario.StartAsync(projectExtra: """
            , "platforms": ["win64", "win32"]
            """);
        var combo = h.Find<ComboBox>("ToolBar.Build.Platform");
        Assert.Equal((88.0, "Platform"), (combo.Width, AutomationProperties.GetName(combo)));
        Assert.Equal(88, combo.Bounds.Width);
        Assert.Equal(["Win64", NotInstalled], combo.ItemsSource!.Cast<string>());
        Assert.Equal("Win64", combo.SelectedItem);
        Assert.True(combo.IsEnabled);
        Assert.Equal("Debug | Win64", h.Status.Field("Configuration"));

        // An item: its text ends in an ellipsis when it does not fit; the full label is its tooltip.
        var item = Assert.IsType<TextBlock>(combo.ItemTemplate!.Build(NotInstalled));
        Assert.Equal((NotInstalled, TextTrimming.CharacterEllipsis, NotInstalled), (item.Text, item.TextTrimming, ToolTip.GetTip(item)));

        // The Project menu lists the same labels; the bullet is on the active platform.
        Assert.Equal(("_1 Win64", true, MenuItemToggleType.Radio),
            (MenuItem(h, AutomationIds.Menu("project.setPlatform", "win64")).Header, MenuItem(h, AutomationIds.Menu("project.setPlatform", "win64")).IsChecked,
                MenuItem(h, AutomationIds.Menu("project.setPlatform", "win64")).ToggleType));
        Assert.Equal(("_2 " + NotInstalled, false), (MenuItem(h, AutomationIds.Menu("project.setPlatform", "win32")).Header, MenuItem(h, AutomationIds.Menu("project.setPlatform", "win32")).IsChecked));

        // Choosing the other platform runs the command: the project file, the selection box, the status bar, the bullet.
        combo.SelectedItem = NotInstalled;
        await h.WaitForAsync(() => h.Services.Projects.Project?.ActivePlatform == "win32", "platform switched");
        Assert.Contains("\"activePlatform\": \"win32\"", File.ReadAllText(BuildScenario.ProjectPath(h)), StringComparison.Ordinal);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(NotInstalled, combo.SelectedItem);
        Assert.Equal("Debug | " + NotInstalled, h.Status.Field("Configuration"));
        Assert.True(MenuItem(h, AutomationIds.Menu("project.setPlatform", "win32")).IsChecked);
        Assert.False(MenuItem(h, AutomationIds.Menu("project.setPlatform", "win64")).IsChecked);

        // The selection box: the label is cut inside the combo (width 88) with an ellipsis, never drawn over its neighbours.
        var shown = combo.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == NotInstalled && t.IsEffectivelyVisible);
        Assert.Equal(TextTrimming.CharacterEllipsis, shown.TextTrimming);
        Assert.Equal(NotInstalled, ToolTip.GetTip(shown));
        Assert.True(shown.Bounds.Width < 88, $"text width {shown.Bounds.Width}");
        Assert.True(shown.TextLayout.TextLines[0].HasCollapsed, "the label does not fit and is elided");
        Assert.Equal(88, combo.Bounds.Width);

        // Disabled while Building (UI-TB-020), enabled again afterwards.
        var held = h.Build.Enqueue(FakeBuildEngine.Success(BuildScenario.ProjectPath(h), BuildScenario.ExePath(h)).Held());
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => held.Request is not null, "build started");
        Assert.Equal("win32", held.Request!.Platform);
        Dispatcher.UIThread.RunJobs();
        Assert.False(combo.IsEnabled);
        Assert.Equal(NotInstalled, combo.SelectedItem);
        held.Release();
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "build finished");
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsEnabled);

        // A project that lists a single platform: the label stays visible, the combo is disabled.
        await h.ExecuteAsync("project.setPlatform", "win64");
        var definition = h.Services.Projects.Definition!;
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(definition with { Platforms = ["win64"], ActivePlatform = null }));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Win64"], combo.ItemsSource!.Cast<string>());
        Assert.Equal("Win64", combo.SelectedItem);
        Assert.False(combo.IsEnabled);
        Assert.Contains(combo.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Win64" && t.IsEffectivelyVisible);
        Assert.Equal("Debug | Win64", h.Status.Field("Configuration"));
        Assert.Equal("Win64", ToolTip.GetTip(combo.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Win64")));
    }
}
