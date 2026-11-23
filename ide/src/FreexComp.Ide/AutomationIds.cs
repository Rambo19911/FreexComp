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
    public const string DocumentAreaTabOverflow = "DocumentArea.TabOverflow";

    /// <summary>An entry of the tab overflow menu: <c>DocumentArea.TabOverflow.&lt;tabName&gt;</c>.</summary>
    public static string DocumentAreaTabOverflowItem(string tabName) => DocumentAreaTabOverflow + "." + tabName;
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

    /// <summary>A tab of a form document's view switcher (UI-DSG-001): <c>Document.&lt;fileName&gt;.ViewTabs.&lt;Code|Design|Text&gt;</c>.</summary>
    public static string DocumentViewTab(string fileName, string view) => "Document." + fileName + ".ViewTabs." + view;

    /// <summary>The design surface of a form (accessibility.md §3 Designer): <c>Designer.&lt;formName&gt;</c>.</summary>
    public static string Designer(string formName) => "Designer." + formName;

    /// <summary>
    /// The progress ring of a Design view whose component metadata is still loading (DSG-TP-003):
    /// <c>Designer.&lt;formName&gt;.Loading</c> (the form's file name; the form is not read yet).
    /// </summary>
    public static string DesignerLoading(string formName) => Designer(formName) + ".Loading";

    /// <summary>The form frame on the design surface: <c>Designer.&lt;formName&gt;.Form</c>.</summary>
    public static string DesignerForm(string formName) => Designer(formName) + ".Form";

    /// <summary>A design-time control (virtual element): <c>Designer.&lt;formName&gt;.Control.&lt;ComponentName&gt;</c>.</summary>
    public static string DesignerControl(string formName, string componentName) => Designer(formName) + ".Control." + componentName;

    /// <summary>The component tray: <c>Designer.&lt;formName&gt;.Tray</c>.</summary>
    public static string DesignerTray(string formName) => Designer(formName) + ".Tray";

    /// <summary>A tray item: <c>Designer.&lt;formName&gt;.Tray.&lt;ComponentName&gt;</c>.</summary>
    public static string DesignerTrayItem(string formName, string componentName) => DesignerTray(formName) + "." + componentName;

    /// <summary>A selection handle of the primary: <c>Designer.&lt;formName&gt;.Handle.&lt;NW|N|NE|E|SE|S|SW|W&gt;</c>.</summary>
    public static string DesignerHandle(string formName, string position) => Designer(formName) + ".Handle." + position;

    public static string Tool(string name) => "Tool." + name;

    public static string ToolTab(string name) => "ToolTab." + name;

    public static string Dialog(string name) => "Dialog." + name;

    public static string DialogField(string dialog, string field) => $"Dialog.{dialog}.{field}";

    public static string InfoBar(string kind) => "InfoBar." + kind;

    /// <summary>The <c>TextEditor</c> of a document (accessibility.md): <c>Editor.&lt;fileName&gt;</c>.</summary>
    public static string Editor(string fileName) => "Editor." + fileName;

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
