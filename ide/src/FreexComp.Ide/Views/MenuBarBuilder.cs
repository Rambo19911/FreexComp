using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Input;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Views;

/// <summary>
/// Generates the menu bar's <see cref="MenuItem"/>s from the menu model (UI-MENU-002): no item is written in XAML.
/// Pure mapping; every decision is in the model and the commands.
/// </summary>
public static class MenuBarBuilder
{
    public static void Populate(Menu menu, IReadOnlyList<MenuItemModel> topLevel)
    {
        menu.Items.Clear();
        foreach (var model in topLevel)
        {
            menu.Items.Add(Create(model));
        }
    }

    private static Control Create(MenuItemModel model)
    {
        if (model.IsSeparator)
        {
            return new Separator();
        }

        var item = new MenuItem { Header = ToAccessText(model.Label) };
        if (model.Command is { } command)
        {
            AutomationProperties.SetAutomationId(item, AutomationIds.Menu(command.Id, model.Argument));
            item.Command = command;
            item.CommandParameter = model.Argument;
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

            // UI-MENU-005: display only (InputGesture, not HotKey) — key routing owns the gestures.
            item.InputGesture = ToAvalonia(command.GestureText);
            command.PropertyChanged += (_, e) => OnCommandChanged(item, command, e);
            return item;
        }

        AutomationProperties.SetAutomationId(item, AutomationIds.TopMenu(model.Key!));
        item.Bind(MenuItem.IsEnabledProperty, new Binding(nameof(MenuItemModel.IsEnabled)) { Source = model, Mode = BindingMode.OneWay });
        foreach (var child in model.Children)
        {
            item.Items.Add(Create(child));
        }

        return item;
    }

    private static void OnCommandChanged(MenuItem item, IdeCommand command, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IdeCommand.GestureText))
        {
            item.InputGesture = ToAvalonia(command.GestureText);
        }
    }

    private static Avalonia.Input.KeyGesture? ToAvalonia(string gestureText)
    {
        if (!CoreGesture.TryParse(gestureText, out var gesture))
        {
            return null;
        }

        var (key, modifiers) = KeyMapping.ToAvalonia(gesture);
        return new Avalonia.Input.KeyGesture(key, modifiers);
    }

    /// <summary>Spec labels mark the mnemonic with <c>&amp;</c>; Avalonia access text uses <c>_</c> (literal <c>_</c> doubled).</summary>
    private static string ToAccessText(string label) => label.Replace("_", "__", StringComparison.Ordinal).Replace('&', '_');
}
