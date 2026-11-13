using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;

namespace FreexComp.Ide;

/// <summary>
/// The AutomationIds of accessibility.md §3 (UI-A11Y-035): constants for fixed ids and builders for generated ones.
/// Views and tests both use these, so an id is typed exactly once.
/// </summary>
public static partial class AutomationIds
{
    public const string MainWindow = "MainWindow";
    public const string MenuBar = "MainWindow.MenuBar";
    public const string DocumentArea = "DocumentArea";
    public const string DocumentAreaEmpty = "DocumentArea.Empty";
    public const string DockLeft = "Dock.Left";
    public const string DockRight = "Dock.Right";
    public const string DockBottom = "Dock.Bottom";

    /// <summary>Upper and lower tool group of the right dock (each has its own tab strip, tool-windows.md §1.2).</summary>
    public const string DockRightUpper = "Dock.Right.Upper";
    public const string DockRightLower = "Dock.Right.Lower";
    public const string StatusBar = "StatusBar";

    /// <summary>The AutomationId of <paramref name="element"/>, or null.</summary>
    public static string? Get(StyledElement element) => AutomationProperties.GetAutomationId(element);

    /// <summary>Menu item of a command (UI-A11Y-033): <c>Menu.&lt;commandId&gt;[:&lt;argument&gt;]</c>.</summary>
    public static string Menu(string commandId, string? argument = null) => FromCommand("Menu", commandId, argument);

    /// <summary>Toolbar button of a command: <c>ToolBar.&lt;commandId&gt;[:&lt;argument&gt;]</c>.</summary>
    public static string ToolBarButton(string commandId, string? argument = null) => FromCommand("ToolBar", commandId, argument);

    /// <summary>Context-menu item of a command: <c>Context.&lt;commandId&gt;[:&lt;argument&gt;]</c>.</summary>
    public static string Context(string commandId, string? argument = null) => FromCommand("Context", commandId, argument);

    /// <summary>Top-level menu (UI-A11Y-034): <c>Menu.File</c>.</summary>
    public static string TopMenu(string name) => "Menu." + name;

    /// <summary>Sub-menu that is not a command (UI-A11Y-034): <c>Menu.File.OpenRecent</c>.</summary>
    public static string SubMenu(string parent, string name) => $"Menu.{parent}.{name}";

    /// <summary><c>DocumentTab.&lt;fileName&gt;</c>; the n-th tab with the same name (n ≥ 2) appends <c>@n</c>.</summary>
    public static string DocumentTab(string fileName, int occurrence = 1) =>
        "DocumentTab." + fileName + (occurrence > 1 ? "@" + occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");

    public static string DocumentTabClose(string tabName) => "DocumentTab." + tabName + ".Close";

    public static string DocumentTabModified(string tabName) => "DocumentTab." + tabName + ".Modified";

    public static string Tool(string name) => "Tool." + name;

    public static string ToolTab(string name) => "ToolTab." + name;

    public static string Dialog(string name) => "Dialog." + name;

    public static string DialogField(string dialog, string field) => $"Dialog.{dialog}.{field}";

    public static string InfoBar(string kind) => "InfoBar." + kind;

    public static string InfoBarAction(string kind, string name) => $"InfoBar.{kind}.Action.{name}";

    public static string StatusBarField(string name) => "StatusBar." + name;

    /// <summary>
    /// The splitter that resizes a dock (UI-WIN-005): <c>Dock.Left.Splitter</c>, <c>Dock.Right.Splitter</c>,
    /// <c>Dock.Bottom.Splitter</c>; <c>Dock.Right.Upper.Splitter</c> divides the right dock's two groups.
    /// </summary>
    public static string Splitter(string dockId) => dockId + ".Splitter";

    /// <summary>
    /// UI-A11Y-031: a PascalCase first segment, then dot-separated segments; the tail may hold any file-name or
    /// identifier character (paths, <c>@n</c>, <c>$new</c>) but no control character, and no segment is empty.
    /// </summary>
    public static bool IsWellFormed(string? id) => id is not null && WellFormed().IsMatch(id);

    private static string FromCommand(string prefix, string commandId, string? argument)
    {
        ArgumentException.ThrowIfNullOrEmpty(commandId);
        return prefix + "." + commandId + (argument is null ? "" : ":" + argument);
    }

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9_]*(\.[^\s.\p{Cc}][^\p{Cc}]*(?<!\s))?$")]
    private static partial Regex WellFormed();
}
