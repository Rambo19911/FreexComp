using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// Generates <see cref="MenuItem"/>s from the menu model (UI-MENU-002, UI-MENU-040): no item is written in XAML.
/// Pure mapping; every decision is in the model and the commands.
/// </summary>
public static class MenuBarBuilder
{
    /// <summary>The shortcut text block of the Fluent <see cref="MenuItem"/> template.</summary>
    private const string InputGestureTextPart = "PART_InputGestureText";

    public static void Populate(Menu menu, IReadOnlyList<MenuItemModel> topLevel)
    {
        menu.Items.Clear();
        foreach (var model in topLevel)
        {
            menu.Items.Add(Create(model, context: false));
        }
    }

    /// <summary>A context menu of command items (ids <c>Context.&lt;commandId&gt;</c>).</summary>
    public static ContextMenu CreateContextMenu(IReadOnlyList<MenuItemModel> items)
    {
        var menu = new ContextMenu();
        foreach (var model in items)
        {
            menu.Items.Add(Create(model, context: true));
        }

        return menu;
    }

    private static Control Create(MenuItemModel model, bool context)
    {
        if (model.IsSeparator)
        {
            return new Separator();
        }

        var item = new MenuItem { Header = ToAccessText(model.Label) };
        if (model.Run is { } run)
        {
            AutomationProperties.SetAutomationId(item, (context ? "Context." : "Menu.") + model.ActionId);
            if (model.IsCheckedAction is { } isChecked)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = isChecked;
            }

            item.Click += (_, _) => run();
            return item;
        }

        if (model.Command is { } command)
        {
            AutomationProperties.SetAutomationId(item, context ? AutomationIds.Context(command.Id, model.Argument) : AutomationIds.Menu(command.Id, model.Argument));
            item.Command = command;
            item.CommandParameter = model.Argument;
            if (model.ToolTip is { } tip)
            {
                ToolTip.SetTip(item, tip); // UI-MENU-009: the full path
            }
            if (model.IconName is { } icon)
            {
                item.Icon = new IdeIcon { IconName = icon }; // UI-MENU-008
            }

            item.ToggleType = model.ToggleKind switch
            {
                MenuToggleKind.Check => MenuItemToggleType.CheckBox,
                MenuToggleKind.Radio => MenuItemToggleType.Radio,
                _ => MenuItemToggleType.None,
            };
            if (model.ToggleKind != MenuToggleKind.None)
            {
                item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(IdeCommand.IsChecked)) { Source = command, Mode = BindingMode.OneWay });
            }

            // UI-MENU-005: the shortcut column shows IdeCommand.GestureText in UI-KEY-001 spelling. The toolkit's own
            // formatting of InputGesture differs for punctuation ("Ctrl++" for Ctrl+=), so InputGesture stays unset and
            // the template's text block is bound instead; no HotKey either — key routing owns the gestures.
            item.TemplateApplied += (_, e) =>
                e.NameScope.Find<TextBlock>(InputGestureTextPart)?.Bind(
                    TextBlock.TextProperty, new Binding(nameof(IdeCommand.GestureText)) { Source = command, Mode = BindingMode.OneWay });
            return item;
        }

        AutomationProperties.SetAutomationId(item, context ? "Context." + model.Key : AutomationIds.TopMenu(model.Key!)); // UI-DSG-081
        item.Bind(MenuItem.IsEnabledProperty, new Binding(nameof(MenuItemModel.IsEnabled)) { Source = model, Mode = BindingMode.OneWay });
        foreach (var child in model.Children)
        {
            item.Items.Add(Create(child, context));
        }

        // A submenu with dynamic lists gets new children when the lists change (UI-MENU-009).
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MenuItemModel.Children))
            {
                return;
            }

            item.Items.Clear();
            foreach (var child in model.Children)
            {
                item.Items.Add(Create(child, context));
            }
        };
        return item;
    }

    /// <summary>Spec labels mark the mnemonic with <c>&amp;</c>; Avalonia access text uses <c>_</c> (literal <c>_</c> doubled).</summary>
    private static string ToAccessText(string label) => label.Replace("_", "__", StringComparison.Ordinal).Replace('&', '_');
}
