using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Menus;

/// <summary>
/// The design-surface context menu (designer.md §8): the commands with their main-menu labels, the sub-menus
/// <c>Context.Designer.Align</c>, <c>.Size</c> and <c>.Order</c> (UI-DSG-081) and check marks on the toggles. Enabled
/// states come from the commands (design-surface.md §13). The verb group (UI-DSG-082) joins with D-097.
/// </summary>
public static class DesignerContextMenu
{
    private static readonly string?[] Align =
    [
        "designer.alignLeft", "designer.alignRight", "designer.alignTop", "designer.alignBottom", "designer.alignHCenter",
        "designer.alignVCenter", null, "designer.centerHInParent", "designer.centerVInParent", null, "designer.spaceHEqually",
        "designer.spaceVEqually", null, "designer.alignToGrid",
    ];

    private static readonly string?[] Size = ["designer.sameWidth", "designer.sameHeight", "designer.sameSize", null, "designer.sizeToGrid"];

    private static readonly string?[] Order = ["designer.bringToFront", "designer.sendToBack"];

    public static IReadOnlyList<MenuItemModel> Build(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        MenuItemModel Item(string id) => MenuItemModel.ForCommand(MenuLabels.For(new CommandMenuNode(id)), commands.Get(id), null,
            commands.Get(id).Definition.IsToggle ? MenuToggleKind.Check : MenuToggleKind.None);
        List<MenuItemModel> Items(IEnumerable<string?> ids) => [.. ids.Select(id => id is null ? MenuItemModel.Separator() : Item(id))];
        MenuItemModel Submenu(string key, string label, string?[] ids) => MenuItemModel.ForSubmenu(key, label, Items(ids), alwaysEnabled: false);

        return
        [
            .. Items(["edit.cut", "edit.copy", "edit.paste", "edit.delete", "edit.selectAll", "designer.selectParent"]),
            MenuItemModel.Separator(),
            Submenu("Designer.Align", Strings.Menu_Edit_Align, Align),
            Submenu("Designer.Size", Strings.Menu_Edit_Size, Size),
            Submenu("Designer.Order", Strings.Menu_Designer_Order, Order),
            MenuItemModel.Separator(),
            .. Items(["designer.tabOrder", "designer.lockControls", null, "designer.toggleGrid", "designer.toggleSnapToGrid",
                "designer.toggleGuidelines", null, "designer.createDefaultHandler", "view.toggleFormCode", "view.formAsText"]),
        ];
    }
}
