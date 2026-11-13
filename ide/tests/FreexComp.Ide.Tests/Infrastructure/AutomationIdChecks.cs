using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>Tree checks of UI-A11Y-035 over one window's visual tree.</summary>
public static class AutomationIdChecks
{
    /// <summary>The control kinds that must carry an id (UI-A11Y-035); subclasses count (CheckBox, RadioButton, …).</summary>
    private static readonly Type[] Interactive =
    [
        typeof(Button), typeof(MenuItem), typeof(TextBox), typeof(ComboBox), typeof(ListBox), typeof(TreeView),
        typeof(TabItem), typeof(Slider),
    ];

    /// <summary>Type names of interactive controls without an id. Parts of a control template are the template owner's.</summary>
    public static IReadOnlyList<string> ControlsWithoutId(Visual root) =>
        [.. Controls(root)
            .Where(c => c.TemplatedParent is null && IsInteractive(c) && string.IsNullOrEmpty(AutomationIds.Get(c)))
            .Select(c => c.GetType().Name)];

    /// <summary>Ids that occur more than once in the window.</summary>
    public static IReadOnlyList<string> DuplicateIds(Visual root) =>
        [.. AllIds(root).GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).Order(StringComparer.Ordinal)];

    public static IReadOnlyList<string> AllIds(Visual root) =>
        [.. Controls(root).Select(c => AutomationIds.Get(c)).OfType<string>().Where(id => id.Length > 0)];

    /// <summary>Visual and logical descendants: items of closed menus exist only in the logical tree.</summary>
    private static IEnumerable<Control> Controls(Visual root) =>
        new[] { root }.Concat(root.GetVisualDescendants()).Concat(((ILogical)root).GetLogicalDescendants())
            .OfType<Control>().Distinct();

    private static bool IsInteractive(Control control) =>
        Interactive.Any(t => t.IsInstanceOfType(control)) || control.GetType().Name == "DataGrid";
}
