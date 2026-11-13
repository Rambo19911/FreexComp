using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Views;

/// <summary>
/// Builds the fixed dock model of UI-WIN-006 — a horizontal <see cref="ProportionalDock"/> holding the left
/// <see cref="ToolDock"/>, the center (document dock over the bottom tool dock) and the right dock (two tool docks) —
/// and keeps each tool dock's <c>VisibleDockables</c> a projection of its <see cref="ToolGroupViewModel"/>
/// (UI-WIN-007: the model is rebuilt from our <see cref="LayoutState"/>, never serialized). Every dockable is created
/// with floating, dragging, dropping and pinning off (UI-WIN-003, IDE-ARCH-006).
/// </summary>
public sealed class IdeDockFactory : Factory
{
    public const string CenterId = "Center";
    public const string DocumentsId = "Documents";

    private readonly Dictionary<ToolGroupId, ToolDock> _toolDocks = [];

    public static string IdOf(ToolGroupId group) => group switch
    {
        ToolGroupId.Left => AutomationIds.DockLeft,
        ToolGroupId.RightUpper => AutomationIds.DockRightUpper,
        ToolGroupId.RightLower => AutomationIds.DockRightLower,
        _ => AutomationIds.DockBottom,
    };

    public IRootDock Build(LayoutViewModel layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var center = Fixed(new ProportionalDock
        {
            Id = CenterId,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                Fixed(new DocumentDock { Id = DocumentsId }),
                Splitter(),
                ToolDockFor(layout.Bottom)),
        });
        var right = Fixed(new ProportionalDock
        {
            Id = AutomationIds.DockRight,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(ToolDockFor(layout.RightUpper), Splitter(), ToolDockFor(layout.RightLower)),
        });
        var main = Fixed(new ProportionalDock
        {
            Id = "Main",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(ToolDockFor(layout.Left), Splitter(), center, Splitter(), right),
        });
        var root = Fixed(new RootDock { Id = "Root", VisibleDockables = CreateList<IDockable>(main), ActiveDockable = main });
        foreach (var group in layout.Groups)
        {
            Sync(group);
            group.PropertyChanged += (_, _) => Sync(group);
        }

        return root;
    }

    private ToolDock ToolDockFor(ToolGroupViewModel group)
    {
        var dock = Fixed(new ToolDock { Id = IdOf(group.Id), VisibleDockables = CreateList<IDockable>() });
        _toolDocks[group.Id] = dock;
        return dock;
    }

    /// <summary>The tool dock shows exactly the group's tools, in order, with the group's active tab.</summary>
    private void Sync(ToolGroupViewModel group)
    {
        var dock = _toolDocks[group.Id];
        var existing = dock.VisibleDockables!.ToDictionary(d => d.Id, StringComparer.Ordinal);
        dock.VisibleDockables!.Clear();
        foreach (var name in group.Tools)
        {
            dock.VisibleDockables.Add(existing.TryGetValue(name, out var tool) ? tool : Fixed(new Tool { Id = name, Title = name, CanClose = true }));
        }

        dock.ActiveDockable = dock.VisibleDockables.FirstOrDefault(d => d.Id == group.ActiveTool);
    }

    private ProportionalDockSplitter Splitter() => Fixed(new ProportionalDockSplitter());

    private static T Fixed<T>(T dockable)
        where T : IDockable
    {
        dockable.CanFloat = false;
        dockable.CanDrag = false;
        dockable.CanDrop = false;
        dockable.CanPin = false;
        if (dockable is not Tool)
        {
            dockable.CanClose = false;
        }

        return dockable;
    }
}
