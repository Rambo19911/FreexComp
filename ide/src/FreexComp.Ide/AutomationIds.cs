using Avalonia;
using Avalonia.Automation;

namespace FreexComp.Ide;

/// <summary>
/// The AutomationIds of accessibility.md §3 (UI-A11Y-035): views and tests both use these, so an id is typed once.
/// Grows with the views that need ids.
/// </summary>
public static class AutomationIds
{
    public const string MainWindow = "MainWindow";
    public const string DocumentArea = "DocumentArea";
    public const string StatusBar = "StatusBar";
    public const string MenuBar = "MainWindow.MenuBar";
    public const string DockLeft = "Dock.Left";
    public const string DockRight = "Dock.Right";
    public const string DockBottom = "Dock.Bottom";

    /// <summary>The AutomationId of <paramref name="element"/>, or null.</summary>
    public static string? Get(StyledElement element) => AutomationProperties.GetAutomationId(element);
}
