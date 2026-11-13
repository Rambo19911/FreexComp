using FreexComp.Ide.Core.Commands;

namespace FreexComp.Ide.Core.Menus;

/// <summary>Builds the menu-item tree that the menu bar view renders (UI-MENU-002).</summary>
public static class MainMenuModel
{
    public static IReadOnlyList<MenuItemModel> Build(ICommandService commands) =>
        [.. MainMenuDefinition.TopLevel.Select(top => MenuItemModel.ForSubmenu(top.Key, MenuLabels.For(top), Children(top.Children, commands), alwaysEnabled: true))];

    /// <summary>
    /// The items of one level. Dynamic lists contribute nothing until their providers exist; separators left leading,
    /// trailing or doubled by that are dropped.
    /// </summary>
    private static List<MenuItemModel> Children(IReadOnlyList<MenuNode> nodes, ICommandService commands)
    {
        var items = new List<MenuItemModel>();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case SeparatorNode when items.Count > 0 && !items[^1].IsSeparator:
                    items.Add(MenuItemModel.Separator());
                    break;
                case CommandMenuNode c:
                    var command = commands.Get(c.CommandId);
                    var toggle = c.Exclusive ? MenuToggleKind.Radio : command.Definition.IsToggle ? MenuToggleKind.Check : MenuToggleKind.None;
                    items.Add(MenuItemModel.ForCommand(MenuLabels.For(c), command, c.Argument, toggle));
                    break;
                case SubmenuNode s:
                    items.Add(MenuItemModel.ForSubmenu(s.Key, MenuLabels.For(s), Children(s.Children, commands), alwaysEnabled: false));
                    break;
            }
        }

        if (items.Count > 0 && items[^1].IsSeparator)
        {
            items.RemoveAt(items.Count - 1);
        }

        return items;
    }
}
