using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Menus;

/// <summary>
/// The context menu of the views of a diff document (diff-viewer.md §2, local-history.md IDE-HIST-021): Copy and Select
/// All, the block navigation and, when the right side is a live editor buffer, <c>Copy Block to Current</c>. Labels are the
/// main-menu labels where the command has one (UI-MENU-041), else the command titles; items of phases not reached yet are
/// omitted (UI-MENU-042).
/// </summary>
public static class DiffContextMenu
{
    /// <summary>The host table; null = separator.</summary>
    public static readonly IReadOnlyList<string?> Commands =
    [
        "edit.copy", "edit.selectAll", null,
        "diff.previousDifference", "diff.nextDifference", null,
        "diff.copyBlockToRight",
    ];

    public static IReadOnlyList<MenuItemModel> Build(ICommandService commands, int implementedPhase, bool liveRight)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var items = new List<MenuItemModel>();
        var pendingSeparator = false;
        foreach (var id in Commands)
        {
            if (id is null)
            {
                pendingSeparator = items.Count > 0;
                continue;
            }

            var command = commands.Get(id);
            if (!Composition.Phase8Features.IsAvailable(id, command.Definition.Phase, implementedPhase) || (id == "diff.copyBlockToRight" && !liveRight))
                continue;
            if (pendingSeparator)
                items.Add(MenuItemModel.Separator());
            pendingSeparator = false;
            var node = new CommandMenuNode(id);
            var label = Strings.ResourceManager.GetString(MenuLabels.ResourceName(node), CultureInfo.CurrentCulture) ?? command.Title;
            items.Add(MenuItemModel.ForCommand(label, command, null, MenuToggleKind.None));
        }

        return items;
    }
}
