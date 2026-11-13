using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class LayoutTests
{
    private const double Splitter = 5; // design-tokens.md §8: splitter hit area

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-WIN-006")]
    [Trait("Req", "UI-WIN-010")]
    [Trait("Req", "UI-WIN-011")]
    public async Task EditProfileDefaultArrangement()
    {
        // The Edit column of main-window.md §2 is the built-in profile.
        var rows = SpecTables.Read(SpecTables.DocPath("spec/ui/main-window.md"), "2. Default layout per profile");
        var profile = LayoutProfiles.Create(LayoutProfiles.Edit);
        foreach (var group in Enum.GetValues<ToolGroupId>())
        {
            Assert.Equal(ExpectedGroup(rows, group), Describe(profile.Group(group)));
        }

        Assert.Equal((260, 300, 200, 0.5), (profile.LeftSize, profile.RightSize, profile.BottomSize, profile.RightSplit));

        // Phase 1: only the P1 tool windows are registered; the profile skips the others (UI-WIN-011).
        var phase1 = rows.Where(r => r["First phase"] == "P1").Select(r => ToolName(r["Tool window"])).ToHashSet();
        Assert.Equal(["ProjectManager"], phase1);
        var (window, view, layout) = Show(phase1);
        try
        {
            Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
            var left = Find(window, "Dock.Left");
            Assert.True(left.IsVisible);
            Assert.Equal(260, left.Bounds.Width);
            Assert.Equal(["ToolTab.ProjectManager"], Ids(window, "ToolTab."));
            Assert.Equal("ProjectManager", layout.Left.ActiveTool);
            Assert.False(Find(window, "Dock.Bottom").IsVisible); // Messages, Output, Search Results are P3
            Assert.False(Find(window, "Dock.Right").IsVisible);  // UI-WIN-010: empty in Edit
            var documents = Find(window, AutomationIds.DocumentArea);
            Assert.Equal(1440 - 260 - Splitter, documents.Bounds.Width);
            Assert.Equal(900, documents.Bounds.Height);

            // UI-WIN-006: the Dock model built by IdeDockFactory — fixed docks, nothing floats, drags or pins.
            var root = Assert.IsAssignableFrom<IProportionalDock>(Assert.Single(view.DockModel.VisibleDockables!));
            Assert.Equal(Orientation.Horizontal, root.Orientation);
            Assert.Equal(
                ["Dock.Left", "Splitter", "Center", "Splitter", "Dock.Right"],
                root.VisibleDockables!.Select(d => d is IProportionalDockSplitter ? "Splitter" : d.Id));
            var center = Assert.IsAssignableFrom<IProportionalDock>(root.VisibleDockables![2]);
            Assert.Equal(Orientation.Vertical, center.Orientation);
            Assert.IsAssignableFrom<IDocumentDock>(center.VisibleDockables![0]);
            Assert.Equal("Dock.Bottom", Assert.IsAssignableFrom<IToolDock>(center.VisibleDockables![2]).Id);
            var right = Assert.IsAssignableFrom<IProportionalDock>(root.VisibleDockables![4]);
            Assert.Equal(["Dock.Right.Upper", "Splitter", "Dock.Right.Lower"], right.VisibleDockables!.Select(d => d is IProportionalDockSplitter ? "Splitter" : d.Id));
            var leftDock = Assert.IsAssignableFrom<IToolDock>(root.VisibleDockables![0]);
            var tool = Assert.Single(leftDock.VisibleDockables!);
            Assert.Equal("ProjectManager", tool.Id);
            Assert.Same(tool, leftDock.ActiveDockable);
            foreach (var dockable in All(view.DockModel))
            {
                Assert.False(dockable.CanFloat || dockable.CanDrag || dockable.CanDrop || dockable.CanPin, dockable.Id);
            }

            Assert.True(tool.CanClose); // close = hide
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-004")]
    [Trait("Req", "UI-TW-005")]
    public async Task EmptyDockCollapses()
    {
        // The shell of Phase 1 has no registered tool window yet: every dock is collapsed.
        await using (var h = await HeadlessHarness.StartAsync())
        {
            Assert.False(h.Find<Control>("Dock.Left").IsVisible);
            Assert.False(h.Find<Control>("Dock.Right").IsVisible);
            Assert.False(h.Find<Control>("Dock.Bottom").IsVisible);
            Assert.Equal(1440, h.Find<Control>(AutomationIds.DocumentArea).Bounds.Width);
        }

        var (window, view, layout) = Show(new HashSet<string>(["ProjectManager", "Messages"], StringComparer.Ordinal));
        try
        {
            var documents = Find(window, AutomationIds.DocumentArea);
            Assert.Equal(200, Find(window, "Dock.Bottom").Bounds.Height);
            Assert.Equal(900 - 200 - Splitter, documents.Bounds.Height);
            Assert.Equal(["Messages"], view.DockModel.VisibleDockables!.OfType<IDock>().SelectMany(Docks).Single(d => d.Id == "Dock.Bottom").VisibleDockables!.Select(d => d.Id));

            Assert.True(layout.SetVisible("Messages", false)); // last visible tool of the bottom dock
            Dispatcher.UIThread.RunJobs();
            Assert.False(Find(window, "Dock.Bottom").IsVisible);
            Assert.Equal(900, documents.Bounds.Height); // the document area takes the space, splitter included
            Assert.Empty(view.DockModel.VisibleDockables!.OfType<IDock>().SelectMany(Docks).Single(d => d.Id == "Dock.Bottom").VisibleDockables!);

            layout.LeftSize = 300;
            Assert.True(layout.SetVisible("ProjectManager", false));
            Dispatcher.UIThread.RunJobs();
            Assert.False(Find(window, "Dock.Left").IsVisible);
            Assert.Equal(1440, documents.Bounds.Width);
            Assert.Null(layout.Left.ActiveTool);

            Assert.True(layout.SetVisible("ProjectManager", true));
            Assert.True(layout.SetVisible("Messages", true));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, Find(window, "Dock.Left").Bounds.Width); // restored at its last size
            Assert.Equal(200, Find(window, "Dock.Bottom").Bounds.Height);
            Assert.Equal(1440 - 300 - Splitter, documents.Bounds.Width);
            Assert.Equal("ProjectManager", layout.Left.ActiveTool);
            Assert.False(layout.SetVisible("Watches", true)); // not in the profile: nothing to show (I-023 adds home docks)
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, LayoutView View, LayoutViewModel Layout) Show(IReadOnlySet<string> registered)
    {
        var services = TestComposition.Build();
        var layout = new LayoutViewModel(LayoutState.CreateDefault(), registered.Contains, new DocumentAreaViewModel(services.Commands, services.Keymap));
        var view = new LayoutView { DataContext = layout };
        var window = new Window { Width = 1440, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, layout);
    }

    private static Control Find(Window window, string id) =>
        window.GetVisualDescendants().OfType<Control>().Single(c => AutomationIds.Get(c) == id);

    private static List<string> Ids(Window window, string prefix) =>
        [.. window.GetVisualDescendants().OfType<Control>().Select(c => AutomationIds.Get(c)).OfType<string>().Where(id => id.StartsWith(prefix, StringComparison.Ordinal))];

    private static IEnumerable<IDock> Docks(IDock dock) =>
        new[] { dock }.Concat(dock.VisibleDockables?.OfType<IDock>().SelectMany(Docks) ?? []);

    private static IEnumerable<IDockable> All(IDockable dockable) =>
        new[] { dockable }.Concat(dockable is IDock { VisibleDockables: { } children } ? children.SelectMany(All) : []);

    private static string ToolName(string display) => display.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    private static string Describe(ToolGroupLayout group) =>
        $"tools=[{string.Join(",", group.Tools)}] hidden=[{string.Join(",", group.Hidden)}] active={group.Active}";

    /// <summary>Cells like "Left ①, active", "Bottom ④, hidden", "hidden" (= not placed in this profile).</summary>
    private static string ExpectedGroup(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ToolGroupId group)
    {
        var prefix = group switch
        {
            ToolGroupId.Left => "Left",
            ToolGroupId.RightUpper => "Right-upper",
            ToolGroupId.RightLower => "Right-lower",
            _ => "Bottom",
        };
        var placed = rows
            .Select(r => (Name: ToolName(r["Tool window"]), Cell: r["Edit profile"]))
            .Where(t => t.Cell.StartsWith(prefix + " ", StringComparison.Ordinal))
            .Select(t => (t.Name, Order: "①②③④⑤⑥⑦⑧⑨".IndexOf(t.Cell[prefix.Length + 1], StringComparison.Ordinal), Hidden: t.Cell.EndsWith("hidden", StringComparison.Ordinal), Active: t.Cell.EndsWith("active", StringComparison.Ordinal)))
            .OrderBy(t => t.Order)
            .ToList();
        var tools = placed.Where(t => !t.Hidden).Select(t => t.Name);
        var hidden = placed.Where(t => t.Hidden).Select(t => t.Name);
        var active = placed.SingleOrDefault(t => t.Active).Name;
        return $"tools=[{string.Join(",", tools)}] hidden=[{string.Join(",", hidden)}] active={active}";
    }
}
