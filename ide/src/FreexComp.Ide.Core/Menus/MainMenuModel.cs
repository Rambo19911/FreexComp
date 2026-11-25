using System.Globalization;
using FreexComp.Ide.Core.Commands;

namespace FreexComp.Ide.Core.Menus;

/// <summary>Builds the menu-item tree that the menu bar view renders (UI-MENU-002).</summary>
public static class MainMenuModel
{
    /// <param name="lists">Entries of the dynamic lists; a menu holding one is regenerated on its
    /// <see cref="IDynamicMenuSource.Changed"/>. Null = every dynamic list is empty.</param>
    public static IReadOnlyList<MenuItemModel> Build(ICommandService commands, IDynamicMenuSource? lists = null) =>
        [.. MainMenuDefinition.TopLevel.Select(top => Submenu(top, commands, lists, topLevel: true))];

    private static MenuItemModel Submenu(SubmenuNode node, ICommandService commands, IDynamicMenuSource? lists, bool topLevel)
    {
        var model = MenuItemModel.ForSubmenu(node.Key, MenuLabels.For(node), Children(node.Children, commands, lists), alwaysEnabled: topLevel);
        if (lists is not null && node.Children.Any(c => c is DynamicListNode))
            lists.Changed += (_, _) => model.SetChildren(Children(node.Children, commands, lists));
        return model;
    }

    /// <summary>
    /// The items of one level. Dynamic lists contribute their entries (the first nine of the level get the digit
    /// mnemonics <c>&amp;1</c>…<c>&amp;9</c>, UI-MENU-009); separators left leading, trailing or doubled by empty lists
    /// are dropped.
    /// </summary>
    private static List<MenuItemModel> Children(IReadOnlyList<MenuNode> nodes, ICommandService commands, IDynamicMenuSource? lists)
    {
        var items = new List<MenuItemModel>();
        var numbered = 0;
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
                    items.Add(Submenu(s, commands, lists, topLevel: false));
                    break;
                case DynamicListNode d when lists is not null:
                    var provider = commands.Get(d.ProviderId);
                    foreach (var entry in lists.GetEntries(d.ProviderId, d.Max))
                    {
                        var label = ++numbered <= 9 ? string.Create(CultureInfo.InvariantCulture, $"&{numbered} {entry.Label}") : entry.Label;
                        items.Add(MenuItemModel.ForEntry(label, provider, entry));
                    }

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
