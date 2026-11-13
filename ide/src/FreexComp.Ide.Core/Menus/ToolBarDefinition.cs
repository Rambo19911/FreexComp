using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Menus;

/// <summary>An element of a toolbar (UI-TB-002).</summary>
public abstract record ToolBarItem;

/// <summary>An icon-only button running <paramref name="CommandId"/>; toggle commands show their checked state.</summary>
public sealed record ToolBarButton(string CommandId, string IconName) : ToolBarItem;

public sealed record ToolBarSeparator : ToolBarItem
{
    public static readonly ToolBarSeparator Instance = new();
}

/// <summary>A combo box filled by <paramref name="ProviderId"/>; <paramref name="Name"/> completes its AutomationId.</summary>
public sealed record ToolBarCombo(string Name, string ProviderId, double Width) : ToolBarItem;

/// <summary>
/// One toolbar of toolbars.md §2: <paramref name="Name"/> completes <c>ToolBar.&lt;Name&gt;</c>, <paramref name="Phase"/>
/// is when it first appears (UI-TB-005), <paramref name="Visibility"/> its setting (UI-TB-004), <paramref name="ToggleCommandId"/>
/// the <c>view.toolbar.*</c> command.
/// </summary>
public sealed record ToolBarDefinition(string Name, int Phase, SettingKey<bool> Visibility, string ToggleCommandId, IReadOnlyList<ToolBarItem> Items);

/// <summary>The toolbars as data, in row order (UI-TB-001/002).</summary>
public static class ToolBarDefinitions
{
    public static readonly ToolBarDefinition Standard = new("Standard", 1, SettingsCatalog.AppearanceToolbarStandard, "view.toolbar.standard",
    [
        Button("file.newFile", "new-file"), Button("file.openProject", "open-folder"), Button("file.save", "save"), Button("file.saveAll", "save-all"),
        Separator,
        Button("edit.undo", "undo"), Button("edit.redo", "redo"),
        Separator,
        Button("search.find", "find"), Button("search.findInFiles", "find-in-files"),
        Separator,
        Button("search.navigateBack", "nav-back"), Button("search.navigateForward", "nav-forward"),
    ]);

    public static readonly ToolBarDefinition Build = new("Build", 3, SettingsCatalog.AppearanceToolbarBuild, "view.toolbar.build",
    [
        new ToolBarCombo("Configuration", "project.setConfiguration", 120), new ToolBarCombo("Platform", "project.platforms", 88),
        Separator,
        Button("project.build", "build"), Button("project.cancelBuild", "cancel-build"),
        Separator,
        Button("run.start", "run-debug"), Button("run.startWithoutDebugging", "run"), Button("run.stop", "stop"), Button("run.restart", "restart"),
    ]);

    public static readonly ToolBarDefinition Debug = new("Debug", 6, SettingsCatalog.AppearanceToolbarDebug, "view.toolbar.debug",
    [
        Button("run.pause", "pause"),
        Separator,
        Button("run.stepOver", "step-over"), Button("run.stepInto", "step-into"), Button("run.stepOut", "step-out"), Button("run.runToCursor", "run-to-cursor"),
        Separator,
        Button("run.showExecutionPoint", "execution-point"), Button("run.evaluate", "evaluate"), Button("run.toggleBreakpoint", "breakpoint"),
    ]);

    public static readonly ToolBarDefinition Designer = new("Designer", 5, SettingsCatalog.AppearanceToolbarDesigner, "view.toolbar.designer",
    [
        Button("designer.alignLeft", "align-left"), Button("designer.alignHCenter", "align-hcenter"), Button("designer.alignRight", "align-right"),
        Button("designer.alignTop", "align-top"), Button("designer.alignVCenter", "align-vcenter"), Button("designer.alignBottom", "align-bottom"),
        Separator,
        Button("designer.sameWidth", "same-width"), Button("designer.sameHeight", "same-height"),
        Button("designer.spaceHEqually", "space-h"), Button("designer.spaceVEqually", "space-v"),
        Separator,
        Button("designer.bringToFront", "bring-front"), Button("designer.sendToBack", "send-back"),
        Separator,
        Button("designer.tabOrder", "tab-order"), Button("designer.lockControls", "lock"), Button("designer.toggleGrid", "grid"),
        Separator,
        Button("view.toggleFormCode", "form-code"),
    ]);

    /// <summary>Row order (UI-TB-001).</summary>
    public static readonly IReadOnlyList<ToolBarDefinition> All = [Standard, Build, Debug, Designer];

    private static readonly Dictionary<string, string> Icons = All.SelectMany(t => t.Items).OfType<ToolBarButton>()
        .ToDictionary(b => b.CommandId, b => b.IconName, StringComparer.Ordinal);

    /// <summary>The icon of a command that has a toolbar button, else null (UI-MENU-008: only those show an icon in menus).</summary>
    public static string? IconOf(string commandId) => Icons.GetValueOrDefault(commandId);

    private static ToolBarSeparator Separator => ToolBarSeparator.Instance;

    private static ToolBarButton Button(string commandId, string iconName) => new(commandId, iconName);
}
