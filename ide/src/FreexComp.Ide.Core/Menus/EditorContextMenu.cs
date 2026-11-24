using FreexComp.Ide.Core.Commands;

namespace FreexComp.Ide.Core.Menus;

/// <summary>
/// The editor text-area context menu (menus.md §12): the commands of the main menu with their main-menu labels
/// (UI-MENU-040/041), in the order of the host table; items of phases not reached yet are omitted (UI-MENU-042) and
/// separators collapse around them. <c>view.toggleFormCode</c> (P5) appears only for documents with a form.
/// </summary>
public static class EditorContextMenu
{
    /// <summary>The host table; null = separator.</summary>
    public static readonly IReadOnlyList<string?> Commands =
    [
        "edit.cut", "edit.copy", "edit.paste", "edit.selectAll", null,
        "search.goToDeclaration", "search.findReferences", "refactor.rename", null,
        "edit.toggleLineComment", "edit.insertTemplate", "edit.formatDocument", null,
        "run.toggleBreakpoint", "run.runToCursor", "run.addWatch", "run.evaluate", null,
        "view.toggleFormCode", "file.showInExplorer", "file.copyPath",
    ];

    public static IReadOnlyList<MenuItemModel> Build(ICommandService commands, int implementedPhase, bool hasForm)
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
            if (!Composition.Phase8Features.IsAvailable(id, command.Definition.Phase, implementedPhase) || (id == "view.toggleFormCode" && !hasForm))
                continue;
            if (pendingSeparator)
                items.Add(MenuItemModel.Separator());
            pendingSeparator = false;
            items.Add(MenuItemModel.ForCommand(MenuLabels.For(new CommandMenuNode(id)), command, null, MenuToggleKind.None));
        }

        return items;
    }
}
